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
    private const int MaximumIncomingMessageLength = 16_384;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<Guid, PositionDrawing> _drawings = [];
    private readonly IranMarketNormalizationAdapter _normalizationAdapter = new();
    private readonly SymbolMetadata _symbol = new(
        "POC_IR_SAMPLE",
        tickSize: 10m,
        quantityStep: 1m,
        minimumQuantity: 1m,
        pointValue: 1m,
        lotSize: 1m);
    private readonly DispatcherTimer _realtimeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1_500)
    };
    private readonly Random _random = new(73_421);
    private readonly List<MockBar> _bars = [];

    private bool _bridgeReady;
    private int _updatesOnCurrentBar;

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

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            var type = RequiredString(root, "type");

            switch (type)
            {
                case "ready":
                    InitializeChart();
                    break;
                case "createPosition":
                    CreatePosition(root);
                    break;
                case "updatePosition":
                    UpdatePosition(root);
                    break;
                case "movePosition":
                    MovePosition(root);
                    break;
                case "deletePosition":
                    DeletePosition(root);
                    break;
                default:
                    SendBridgeError("نوع پیام پشتیبانی نمی‌شود.");
                    break;
            }
        }
        catch (Exception exception) when (
            exception is JsonException or
            ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            SendBridgeError(exception.Message);
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
            symbol = new
            {
                _symbol.Symbol,
                _symbol.TickSize,
                _symbol.QuantityStep,
                _symbol.MinimumQuantity
            },
            bars = _bars
        });

        StatusTextBlock.Text = "آماده — نمودار کاملاً محلی و فقط برای تحلیل است.";
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
            _symbol,
            _normalizationAdapter);

        _drawings.Add(drawing.Id, drawing);
        SendPosition(drawing);
    }

    private void UpdatePosition(JsonElement root)
    {
        var id = RequiredGuid(root, "id");
        if (!_drawings.TryGetValue(id, out var drawing))
        {
            throw new ArgumentException("Position پیدا نشد.");
        }

        var handleText = RequiredString(root, "handle");
        if (!Enum.TryParse<PositionHandle>(handleText, ignoreCase: true, out var handle) ||
            handle is PositionHandle.StartEdge or PositionHandle.EndEdge)
        {
            throw new ArgumentException("Handle قیمت معتبر نیست.");
        }

        var proposedPrice = _normalizationAdapter.NormalizePrice(
            RequiredDecimal(root, "proposedPrice"),
            _symbol,
            StepRoundingMode.Nearest);

        var updated = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            handle,
            proposedPrice,
            _symbol.TickSize);

        _drawings[id] = updated;
        SendPosition(updated);
    }

    private void MovePosition(JsonElement root)
    {
        var id = RequiredGuid(root, "id");
        if (!_drawings.TryGetValue(id, out var drawing))
        {
            throw new ArgumentException("Position پیدا نشد.");
        }

        var proposedEntry = _normalizationAdapter.NormalizePrice(
            RequiredDecimal(root, "proposedEntryPrice"),
            _symbol,
            StepRoundingMode.Nearest);
        var proposedStart = RequiredDouble(root, "proposedStartTime");

        var lowestPrice = Math.Min(
            drawing.EntryPrice,
            Math.Min(drawing.TargetPrice, drawing.StopPrice));
        var requestedPriceDelta = proposedEntry - drawing.EntryPrice;
        var minimumPriceDelta = _symbol.TickSize - lowestPrice;

        var updated = PositionDrawingEditor.Move(
            drawing,
            Math.Max(requestedPriceDelta, minimumPriceDelta),
            proposedStart - drawing.HorizontalRange.Start);

        _drawings[id] = updated;
        SendPosition(updated);
    }

    private void DeletePosition(JsonElement root)
    {
        var id = RequiredGuid(root, "id");
        if (_drawings.Remove(id))
        {
            PostMessage(new { type = "positionDeleted", id });
        }
    }

    private void SendPosition(PositionDrawing drawing)
    {
        var metrics = RiskRewardCalculator.Calculate(drawing);
        PostMessage(new
        {
            type = "positionState",
            position = new
            {
                drawing.Id,
                side = drawing.Side.ToString(),
                drawing.EntryPrice,
                drawing.StopPrice,
                drawing.TargetPrice,
                startTime = drawing.HorizontalRange.Start,
                endTime = drawing.HorizontalRange.End,
                metrics.RiskPerUnit,
                metrics.RewardPerUnit,
                metrics.RiskPercent,
                metrics.RewardPercent,
                metrics.RewardToRiskRatio
            }
        });
    }

    private void SendBridgeError(string message)
    {
        StatusTextBlock.Text = $"خطای ورودی نمودار: {message}";
        PostMessage(new { type = "bridgeError", message });
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
            var close = Math.Max(1m, open + decimal.Round(drift, 0));
            var high = Math.Max(open, close) + _random.Next(40, 181);
            var low = Math.Max(1m, Math.Min(open, close) - _random.Next(40, 181));
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
            var close = Math.Max(1m, open + _random.Next(-160, 161));
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
            var close = Math.Max(1m, last.Close + _random.Next(-90, 91));
            updated = last with
            {
                High = Math.Max(last.High, close),
                Low = Math.Min(last.Low, close),
                Close = close
            };
            _bars[^1] = updated;
        }

        PostMessage(new { type = "barUpdate", bar = updated });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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

    private sealed record MockBar(long Time, decimal Open, decimal High, decimal Low, decimal Close);
}
