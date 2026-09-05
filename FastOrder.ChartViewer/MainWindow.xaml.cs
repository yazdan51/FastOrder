using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using FastOrder.ChartTools.Calculations;
using FastOrder.ChartTools.Creation;
using FastOrder.ChartTools.Interaction;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;
using Microsoft.Web.WebView2.Core;

namespace FastOrder.ChartViewer;

public partial class MainWindow : Window
{
    private const string LocalHostName = "chartviewer.local";
    private const string MockTimeframe = "1m";
    private const int MaximumIncomingMessageLength = 16_384;
    private const double MinimumHorizontalRangeSeconds = 60d;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PositionWorkspace _workspace = new();
    private readonly PositionSelectionState _selection = new();
    private readonly IranMarketNormalizationAdapter _normalizationAdapter = new();
    private readonly SymbolMetadata _mockSymbol = new(
        "IR_DEMO_MOCK",
        tickSize: 10m,
        quantityStep: 100m,
        minimumQuantity: 100m,
        pointValue: 1m,
        lotSize: 1m,
        quantityPrecision: 0);
    private readonly PositionSizingInputs _defaultSizingInputs = new(
        accountSize: 1_000_000_000m,
        RiskInputMode.PercentOfAccount,
        riskValue: 1m,
        leverage: 1m);
    private readonly LocalPositionStore _positionStore = new();
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _realtimeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1_500)
    };
    private readonly Random _random = new(73_421);
    private readonly List<MockBar> _bars = [];

    private bool _bridgeReady;
    private int _updatesOnCurrentBar;
    private long _realtimeUpdateCount;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        _realtimeTimer.Tick += OnRealtimeTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ChartWebView.EnsureCoreWebView2Async();
            ConfigureWebView();
            ChartWebView.CoreWebView2.Navigate($"https://{LocalHostName}/index.html");
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"راه‌اندازی ChartViewer ناموفق بود: {exception.Message}";
        }
    }

    private void ConfigureWebView()
    {
        var core = ChartWebView.CoreWebView2;
        var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");

        core.SetVirtualHostNameToFolderMapping(
            LocalHostName,
            webRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;

        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.WebMessageReceived += OnWebMessageReceived;
    }

    private static bool IsTrustedLocalUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, LocalHostName, StringComparison.OrdinalIgnoreCase);
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsTrustedLocalUri(e.Uri))
        {
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "www.tradingview.com", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                StatusTextBlock.Text = $"بازکردن پیوند TradingView ناموفق بود: {exception.Message}";
            }
        }
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrustedLocalUri(e.Source))
        {
            return;
        }

        var messageJson = e.WebMessageAsJson;
        if (messageJson.Length > MaximumIncomingMessageLength)
        {
            SendBridgeError("پیام ورودی بیش از حد مجاز است.");
            return;
        }

        Guid? requestedPositionId = null;
        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            requestedPositionId = OptionalGuid(root, "id");
            var type = RequiredString(root, "type");

            switch (type)
            {
                case "ready":
                    InitializeChart();
                    break;
                case "createPosition":
                    CreatePosition(root);
                    break;
                case "selectPosition":
                    SelectPosition(root);
                    break;
                case "updatePosition":
                    UpdatePosition(root);
                    break;
                case "resizePosition":
                    ResizePosition(root);
                    break;
                case "movePosition":
                    MovePosition(root);
                    break;
                case "editPosition":
                    EditPosition(root);
                    break;
                case "deletePosition":
                    DeletePosition(root);
                    break;
                case "savePositions":
                    await SavePositionsAsync(_shutdown.Token);
                    break;
                case "loadPositions":
                    await LoadPositionsAsync(_shutdown.Token);
                    break;
                case "clientError":
                    ReportClientError(root);
                    break;
                default:
                    SendBridgeError("نوع پیام پشتیبانی نمی‌شود.");
                    break;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is JsonException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            IOException or
            UnauthorizedAccessException or
            OverflowException)
        {
            SendBridgeError(exception.Message);
            if (requestedPositionId is Guid id && _workspace.TryGet(id, out var current))
            {
                SendPosition(current);
            }
        }
    }

    private void InitializeChart()
    {
        if (_bridgeReady)
        {
            return;
        }

        CreateMockHistory();
        _bridgeReady = true;
        PostMessage(new
        {
            type = "initialize",
            symbol = BuildSymbolPayload(_mockSymbol),
            bars = _bars,
            persistenceFileName = Path.GetFileName(_positionStore.FilePath)
        });

        StatusTextBlock.Text = "آماده — داده و متادیتا نمایشی‌اند؛ ابزار فقط برای تحلیل است.";
        _realtimeTimer.Start();
    }

    private void CreatePosition(JsonElement root)
    {
        var sideText = RequiredString(root, "side");
        if (!Enum.TryParse<PositionSide>(sideText, ignoreCase: true, out var side))
        {
            throw new ArgumentException("جهت Position معتبر نیست.");
        }

        var drawing = PositionDrawingFactory.CreatePocDefault(
            side,
            RequiredDecimal(root, "entryPrice"),
            new ChartHorizontalRange(
                RequiredDouble(root, "startTime"),
                RequiredDouble(root, "endTime")),
            _mockSymbol,
            _normalizationAdapter);
        var position = new PositionAnalysisState(
            drawing,
            MockTimeframe,
            _defaultSizingInputs,
            _mockSymbol);

        _workspace.Add(position);
        _selection.Select(_workspace, position.Id);
        SendPosition(position);
    }

    private void SelectPosition(JsonElement root)
    {
        var id = NullableGuid(root, "id");
        _selection.Select(_workspace, id);
        SendSelection();
    }

    private void UpdatePosition(JsonElement root)
    {
        var position = GetSelectedPosition(root);

        var handleText = RequiredString(root, "handle");
        if (!Enum.TryParse<PositionHandle>(handleText, ignoreCase: true, out var handle) ||
            handle is PositionHandle.StartEdge or PositionHandle.EndEdge)
        {
            throw new ArgumentException("Handle قیمت معتبر نیست.");
        }

        var proposedPrice = _normalizationAdapter.NormalizePrice(
            RequiredDecimal(root, "proposedPrice"),
            position.SymbolMetadata,
            StepRoundingMode.Nearest);

        var drawing = PositionDrawingEditor.UpdatePriceClamped(
            position.Drawing,
            handle,
            proposedPrice,
            position.SymbolMetadata.TickSize);
        var updated = position.WithDrawing(drawing);

        _workspace.Update(updated);
        SendPosition(updated);
    }

    private void MovePosition(JsonElement root)
    {
        var position = GetSelectedPosition(root);
        var drawing = position.Drawing;
        var symbol = position.SymbolMetadata;

        var proposedEntry = _normalizationAdapter.NormalizePrice(
            RequiredDecimal(root, "proposedEntryPrice"),
            symbol,
            StepRoundingMode.Nearest);
        var proposedStart = RequiredDouble(root, "proposedStartTime");

        var lowestPrice = Math.Min(
            drawing.EntryPrice,
            Math.Min(drawing.TargetPrice, drawing.StopPrice));
        var requestedPriceDelta = proposedEntry - drawing.EntryPrice;
        var minimumPriceDelta = symbol.TickSize - lowestPrice;

        var updatedDrawing = PositionDrawingEditor.Move(
            drawing,
            Math.Max(requestedPriceDelta, minimumPriceDelta),
            proposedStart - drawing.HorizontalRange.Start);
        var updated = position.WithDrawing(updatedDrawing);

        _workspace.Update(updated);
        SendPosition(updated);
    }

    private void ResizePosition(JsonElement root)
    {
        var position = GetSelectedPosition(root);
        var handleText = RequiredString(root, "handle");
        if (!Enum.TryParse<PositionHandle>(handleText, ignoreCase: true, out var handle) ||
            handle is not (PositionHandle.StartEdge or PositionHandle.EndEdge))
        {
            throw new ArgumentException("Handle افقی معتبر نیست.");
        }

        var drawing = PositionDrawingEditor.ResizeHorizontalClamped(
            position.Drawing,
            handle,
            RequiredDouble(root, "proposedTime"),
            MinimumHorizontalRangeSeconds);
        var updated = position.WithDrawing(drawing);

        _workspace.Update(updated);
        SendPosition(updated);
    }

    private void EditPosition(JsonElement root)
    {
        var position = GetSelectedPosition(root);
        var field = RequiredString(root, "field");

        var updated = field switch
        {
            "entryPrice" => UpdatePriceFromPanel(position, PositionHandle.Entry, RequiredDecimal(root, "value")),
            "stopPrice" => UpdatePriceFromPanel(position, PositionHandle.Stop, RequiredDecimal(root, "value")),
            "targetPrice" => UpdatePriceFromPanel(position, PositionHandle.Target, RequiredDecimal(root, "value")),
            "accountSize" => position.WithSizingInputs(
                position.SizingInputs.WithAccountSize(RequiredDecimal(root, "value"))),
            "riskMode" => position.WithSizingInputs(
                position.SizingInputs.WithRiskMode(RequiredRiskMode(root, "value"))),
            "riskValue" => position.WithSizingInputs(
                position.SizingInputs.WithRiskValue(RequiredDecimal(root, "value"))),
            "leverage" => position.WithSizingInputs(
                position.SizingInputs.WithLeverage(RequiredDecimal(root, "value"))),
            "pointValue" => position.WithSymbolMetadata(
                position.SymbolMetadata.WithSizing(
                    RequiredDecimal(root, "value"),
                    position.SymbolMetadata.LotSize,
                    position.SymbolMetadata.QuantityPrecision)),
            "lotSize" => position.WithSymbolMetadata(
                position.SymbolMetadata.WithSizing(
                    position.SymbolMetadata.PointValue,
                    RequiredDecimal(root, "value"),
                    position.SymbolMetadata.QuantityPrecision)),
            "quantityPrecision" => position.WithSymbolMetadata(
                position.SymbolMetadata.WithSizing(
                    position.SymbolMetadata.PointValue,
                    position.SymbolMetadata.LotSize,
                    RequiredInt(root, "value"))),
            _ => throw new ArgumentException("فیلد Position پشتیبانی نمی‌شود.")
        };

        _workspace.Update(updated);
        SendPosition(updated);
    }

    private PositionAnalysisState UpdatePriceFromPanel(
        PositionAnalysisState position,
        PositionHandle handle,
        decimal proposedPrice)
    {
        var symbol = position.SymbolMetadata;
        var normalizedPrice = _normalizationAdapter.NormalizePrice(
            proposedPrice,
            symbol,
            StepRoundingMode.Nearest);
        var drawing = PositionDrawingEditor.UpdatePriceClamped(
            position.Drawing,
            handle,
            normalizedPrice,
            symbol.TickSize);
        return position.WithDrawing(drawing);
    }

    private void DeletePosition(JsonElement root)
    {
        var id = RequiredGuid(root, "id");
        if (_selection.RemoveSelected(_workspace, id))
        {
            PostMessage(new { type = "positionDeleted", id, selectedId = _selection.SelectedId });
        }
    }

    private async Task SavePositionsAsync(CancellationToken cancellationToken)
    {
        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            await _positionStore.SaveAsync(_workspace.Positions, cancellationToken);
            SendOperationStatus(
                $"{_workspace.Count} Position در فایل محلی {Path.GetFileName(_positionStore.FilePath)} ذخیره شد.");
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private async Task LoadPositionsAsync(CancellationToken cancellationToken)
    {
        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            var savedFileExists = _positionStore.Exists;
            var positions = await _positionStore.LoadAsync(cancellationToken);

            _workspace.ReplaceAll(positions);
            _selection.Reconcile(_workspace);
            PostMessage(new
            {
                type = "positionsReplaced",
                positions = _workspace.Positions.Select(BuildPositionPayload).ToArray(),
                selectedId = _selection.SelectedId
            });
            SendOperationStatus(savedFileExists
                ? $"{_workspace.Count} Position از فایل محلی بارگذاری شد."
                : "فایل ذخیره‌شده‌ای وجود نداشت؛ workspace خالی بارگذاری شد.");
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private void SendPosition(PositionAnalysisState position)
    {
        PostMessage(new
        {
            type = "positionState",
            position = BuildPositionPayload(position),
            selectedId = _selection.SelectedId
        });
    }

    private void SendSelection()
    {
        PostMessage(new { type = "selectionChanged", selectedId = _selection.SelectedId });
    }

    private PositionAnalysisState GetSelectedPosition(JsonElement root)
    {
        var id = RequiredGuid(root, "id");
        return _selection.GetSelectedRequired(_workspace, id);
    }

    private object BuildPositionPayload(PositionAnalysisState position)
    {
        var drawing = position.Drawing;
        var inputs = position.SizingInputs;
        var metrics = PositionAnalysisCalculator.Calculate(position, _normalizationAdapter);
        var riskReward = metrics.RiskReward;
        var sizing = metrics.Sizing;
        var pnl = metrics.Pnl;

        return new
        {
            drawing.Id,
            side = drawing.Side.ToString(),
            drawing.EntryPrice,
            drawing.StopPrice,
            drawing.TargetPrice,
            startTime = drawing.HorizontalRange.Start,
            endTime = drawing.HorizontalRange.End,
            position.Timeframe,
            inputs.AccountSize,
            riskMode = inputs.RiskMode.ToString(),
            inputs.RiskValue,
            inputs.Leverage,
            symbol = BuildSymbolPayload(position.SymbolMetadata),
            riskReward.RiskPerUnit,
            riskReward.RewardPerUnit,
            riskReward.RiskPercent,
            riskReward.RewardPercent,
            riskReward.RewardToRiskRatio,
            sizing.RiskAmount,
            riskLimitedQuantity = sizing.QuantityByRisk,
            leverageLimitedQuantity = sizing.QuantityByLeverage,
            sizing.FinalQuantity,
            pnl.ProfitPnl,
            pnl.LossPnl,
            accountBalanceAfterTp = pnl.ProfitAccountBalance,
            accountBalanceAfterSl = pnl.StopAccountBalance
        };
    }

    private static object BuildSymbolPayload(SymbolMetadata symbol) => new
    {
        symbolId = symbol.Symbol,
        symbol.TickSize,
        symbol.QuantityStep,
        symbol.MinimumQuantity,
        symbol.PointValue,
        symbol.LotSize,
        symbol.QuantityPrecision,
        isAuthoritative = false,
        notice = "Mock Iran-market profile; not exchange-authoritative"
    };

    private void SendOperationStatus(string message)
    {
        StatusTextBlock.Text = message;
        PostMessage(new { type = "operationStatus", message });
    }

    private void SendBridgeError(string message)
    {
        var safeMessage = message.Length <= 500 ? message : message[..500];
        StatusTextBlock.Text = $"خطای ورودی نمودار: {safeMessage}";
        PostMessage(new { type = "bridgeError", message = safeMessage });
    }

    private void ReportClientError(JsonElement root)
    {
        var message = RequiredString(root, "message");
        StatusTextBlock.Text = $"خطای runtime رابط نمودار: {message[..Math.Min(message.Length, 500)]}";
    }

    private void PostMessage(object message)
    {
        if (ChartWebView.CoreWebView2 is null)
        {
            return;
        }

        ChartWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private void CreateMockHistory()
    {
        _bars.Clear();
        var time = new DateTimeOffset(2026, 1, 5, 5, 30, 0, TimeSpan.Zero);
        var previousClose = 124_000m;

        for (var index = 0; index < 90; index++)
        {
            var open = previousClose;
            var drift = (decimal)(Math.Sin(index / 6d) * 210d) + _random.Next(-130, 131);
            var close = NormalizeMockPrice(
                Math.Max(_mockSymbol.TickSize, open + decimal.Round(drift, 0)));
            var high = _normalizationAdapter.NormalizePrice(
                Math.Max(open, close) + _random.Next(40, 181),
                _mockSymbol,
                StepRoundingMode.Up);
            var low = _normalizationAdapter.NormalizePrice(
                Math.Max(_mockSymbol.TickSize, Math.Min(open, close) - _random.Next(40, 181)),
                _mockSymbol,
                StepRoundingMode.Down);
            low = Math.Max(_mockSymbol.TickSize, low);
            _bars.Add(new MockBar(time.ToUnixTimeSeconds(), open, high, low, close));
            previousClose = close;
            time = time.AddMinutes(1);
        }
    }

    private void OnRealtimeTick(object? sender, EventArgs e)
    {
        if (!_bridgeReady || _bars.Count == 0)
        {
            return;
        }

        var last = _bars[^1];
        _updatesOnCurrentBar++;

        MockBar updated;
        if (_updatesOnCurrentBar >= 4)
        {
            _updatesOnCurrentBar = 0;
            var open = last.Close;
            var close = NormalizeMockPrice(
                Math.Max(_mockSymbol.TickSize, open + _random.Next(-160, 161)));
            updated = new MockBar(
                last.Time + 60,
                open,
                Math.Max(open, close),
                Math.Min(open, close),
                close);
            _bars.Add(updated);
        }
        else
        {
            var close = NormalizeMockPrice(
                Math.Max(_mockSymbol.TickSize, last.Close + _random.Next(-90, 91)));
            updated = last with
            {
                High = Math.Max(last.High, close),
                Low = Math.Min(last.Low, close),
                Close = close
            };
            _bars[^1] = updated;
        }

        _realtimeUpdateCount++;
        PostMessage(new
        {
            type = "barUpdate",
            bar = updated,
            updateCount = _realtimeUpdateCount,
            positionCount = _workspace.Count
        });
    }

    private decimal NormalizeMockPrice(decimal price) =>
        _normalizationAdapter.NormalizePrice(price, _mockSymbol, StepRoundingMode.Nearest);

    private void OnClosed(object? sender, EventArgs e)
    {
        _shutdown.Cancel();
        _realtimeTimer.Stop();
        _realtimeTimer.Tick -= OnRealtimeTick;

        if (ChartWebView.CoreWebView2 is not null)
        {
            ChartWebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            ChartWebView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            ChartWebView.CoreWebView2.PermissionRequested -= OnPermissionRequested;
            ChartWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        ChartWebView.Dispose();
        _shutdown.Dispose();
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"{propertyName} الزامی است.");
        }

        return property.GetString()!;
    }

    private static decimal RequiredDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || !property.TryGetDecimal(out var value))
        {
            throw new ArgumentException($"{propertyName} باید عدد معتبر باشد.");
        }

        return value;
    }

    private static int RequiredInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
        {
            throw new ArgumentException($"{propertyName} باید عدد صحیح معتبر باشد.");
        }

        return value;
    }

    private static double RequiredDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            !property.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw new ArgumentException($"{propertyName} باید عدد محدود معتبر باشد.");
        }

        return value;
    }

    private static Guid RequiredGuid(JsonElement root, string propertyName)
    {
        var value = RequiredString(root, propertyName);
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new ArgumentException($"{propertyName} شناسه معتبر نیست.");
    }

    private static Guid? OptionalGuid(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out var id) &&
               id != Guid.Empty
            ? id
            : null;
    }

    private static Guid? NullableGuid(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out var id) &&
            id != Guid.Empty)
        {
            return id;
        }

        throw new ArgumentException($"{propertyName} شناسه معتبر نیست.");
    }

    private static RiskInputMode RequiredRiskMode(JsonElement root, string propertyName)
    {
        var value = RequiredString(root, propertyName);
        return Enum.TryParse<RiskInputMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ArgumentException("حالت ریسک معتبر نیست.");
    }

    private sealed record MockBar(long Time, decimal Open, decimal High, decimal Low, decimal Close);
}
