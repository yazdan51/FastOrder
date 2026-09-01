
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FastOrder
{
    public partial class MainWindow : Window
    {
        private const string EasyTraderUrl =
            "https://d.easytrader.ir/";

        private const string ApiHost =
            "api-mts.orbis.easytrader.ir";

        private static readonly TimeSpan ConfirmedOrderLifetime =
            TimeSpan.FromMinutes(
                10);

        private static readonly TimeSpan LiveSubmissionResponseTimeout =
            TimeSpan.FromSeconds(
                30);

        private static readonly TimeSpan ScheduledOrderRetryDelay =
            TimeSpan.FromSeconds(
                1);

        private static readonly TimeSpan ScheduledOrderPreWarmLeadTime =
            TimeSpan.FromSeconds(
                2);

        private bool _webViewReady = false;

        private bool _monitoringEnabled = false;

        // فقط نمایش Live Log متوقف می‌شود.
        // Monitoring شبکه همچنان ادامه دارد.
        private bool _pauseLog = false;
        private bool _authorizationHeaderObserved = false;
        private bool _successfulSessionResponseObserved = false;
        private bool _successfulProtectedApiResponseObserved = false;
        private ConfirmedOrderSnapshot? _confirmedOrderSnapshot;
        private readonly ObservableCollection<OrderSession> _orderSessions =
            new ObservableCollection<OrderSession>();
        private long _nextOrderSessionSequence = 0;
        private OrderSession? _activeOrderSession;
        private bool _hasCurrentOrderSetup = false;
        private bool _liveSubmissionInProgress = false;
        private bool _liveOrderRequestObserved = false;
        private string? _activeLiveSubmissionId;
        private string? _activeLiveSubmissionFingerprint;
        private TaskCompletionSource<LiveOrderNetworkObservation>?
            _activeLiveSubmissionCompletion;
        private CancellationTokenSource? _scheduledOrderCancellation;
        private bool _scheduledOrderActive = false;

        private WindowState _lastNonMinimizedWindowState =
            WindowState.Normal;

        private bool _webViewTimingTestActive = false;

        private bool _orderUiDryRunTimingActive = false;

        private enum ScheduledOrderAttemptOutcome
        {
            Succeeded,
            RetryableFailure,
            AmbiguousFailure
        }

        private sealed class LiveOrderNetworkObservation
        {
            public int? StatusCode
            {
                get;
                init;
            }

            public bool RequestObserved
            {
                get;
                init;
            }

            public bool ResponseObserved
            {
                get;
                init;
            }
        }
        public MainWindow()
        {
            InitializeComponent();

            SessionDataGrid.ItemsSource =
                _orderSessions;

            RestoreWindowLayout();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private void RestoreWindowLayout()
        {
            try
            {
                Properties.Settings settings =
                    Properties.Settings.Default;

                Rect virtualScreen =
                    new(
                        SystemParameters.VirtualScreenLeft,
                        SystemParameters.VirtualScreenTop,
                        SystemParameters.VirtualScreenWidth,
                        SystemParameters.VirtualScreenHeight);

                double maximumWidth =
                    Math.Max(
                        MinWidth,
                        virtualScreen.Width);

                double maximumHeight =
                    Math.Max(
                        MinHeight,
                        virtualScreen.Height);

                double restoredWidth =
                    NormalizeLayoutDimension(
                        settings.MainWindowWidth,
                        Width,
                        MinWidth,
                        maximumWidth);

                double restoredHeight =
                    NormalizeLayoutDimension(
                        settings.MainWindowHeight,
                        Height,
                        MinHeight,
                        maximumHeight);

                Rect primaryWorkArea =
                    SystemParameters.WorkArea;

                double restoredLeft =
                    double.IsFinite(
                        settings.MainWindowLeft)
                        ? settings.MainWindowLeft
                        : primaryWorkArea.Left +
                          Math.Max(
                              0,
                              (primaryWorkArea.Width - restoredWidth) / 2);

                double restoredTop =
                    double.IsFinite(
                        settings.MainWindowTop)
                        ? settings.MainWindowTop
                        : primaryWorkArea.Top +
                          Math.Max(
                              0,
                              (primaryWorkArea.Height - restoredHeight) / 2);

                Rect restoredBounds =
                    new(
                        restoredLeft,
                        restoredTop,
                        restoredWidth,
                        restoredHeight);

                Rect visibleBounds =
                    Rect.Intersect(
                        restoredBounds,
                        virtualScreen);

                const double minimumVisibleEdge =
                    80;

                if (visibleBounds.IsEmpty ||
                    visibleBounds.Width < minimumVisibleEdge ||
                    visibleBounds.Height < minimumVisibleEdge)
                {
                    restoredWidth =
                        Math.Min(
                            restoredWidth,
                            primaryWorkArea.Width);

                    restoredHeight =
                        Math.Min(
                            restoredHeight,
                            primaryWorkArea.Height);

                    restoredLeft =
                        primaryWorkArea.Left +
                        Math.Max(
                            0,
                            (primaryWorkArea.Width - restoredWidth) / 2);

                    restoredTop =
                        primaryWorkArea.Top +
                        Math.Max(
                            0,
                            (primaryWorkArea.Height - restoredHeight) / 2);
                }
                else
                {
                    restoredLeft =
                        Math.Clamp(
                            restoredLeft,
                            virtualScreen.Left,
                            Math.Max(
                                virtualScreen.Left,
                                virtualScreen.Right - restoredWidth));

                    restoredTop =
                        Math.Clamp(
                            restoredTop,
                            virtualScreen.Top,
                            Math.Max(
                                virtualScreen.Top,
                                virtualScreen.Bottom - restoredHeight));
                }

                WindowStartupLocation =
                    WindowStartupLocation.Manual;

                Width =
                    restoredWidth;

                Height =
                    restoredHeight;

                Left =
                    restoredLeft;

                Top =
                    restoredTop;

                ControlPanelColumn.Width =
                    new GridLength(
                        NormalizeLayoutDimension(
                            settings.ControlPanelWidth,
                            ControlPanelColumn.Width.Value,
                            ControlPanelColumn.MinWidth,
                            ControlPanelColumn.MaxWidth));

                LogAreaRow.Height =
                    new GridLength(
                        NormalizeLayoutDimension(
                            settings.LogAreaHeight,
                            LogAreaRow.Height.Value,
                            LogAreaRow.MinHeight,
                            LogAreaRow.MaxHeight));

                _lastNonMinimizedWindowState =
                    settings.MainWindowState ==
                    (int)WindowState.Maximized
                        ? WindowState.Maximized
                        : WindowState.Normal;

                WindowState =
                    _lastNonMinimizedWindowState;
            }
            catch
            {
                _lastNonMinimizedWindowState =
                    WindowState.Normal;
            }
        }

        private static double NormalizeLayoutDimension(
            double value,
            double fallback,
            double minimum,
            double maximum)
        {
            double validFallback =
                double.IsFinite(
                    fallback)
                    ? fallback
                    : minimum;

            double candidate =
                double.IsFinite(
                    value)
                    ? value
                    : validFallback;

            return Math.Clamp(
                candidate,
                minimum,
                Math.Max(
                    minimum,
                    maximum));
        }

        private void MainWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState !=
                WindowState.Minimized)
            {
                _lastNonMinimizedWindowState =
                    WindowState;
            }
        }

        private void SaveWindowLayout()
        {
            try
            {
                Rect bounds =
                    WindowState ==
                    WindowState.Normal
                        ? new Rect(
                            Left,
                            Top,
                            ActualWidth,
                            ActualHeight)
                        : RestoreBounds;

                Properties.Settings settings =
                    Properties.Settings.Default;

                if (!bounds.IsEmpty &&
                    double.IsFinite(
                        bounds.Left) &&
                    double.IsFinite(
                        bounds.Top) &&
                    double.IsFinite(
                        bounds.Width) &&
                    double.IsFinite(
                        bounds.Height))
                {
                    settings.MainWindowLeft =
                        bounds.Left;

                    settings.MainWindowTop =
                        bounds.Top;

                    settings.MainWindowWidth =
                        bounds.Width;

                    settings.MainWindowHeight =
                        bounds.Height;
                }

                settings.MainWindowState =
                    (int)_lastNonMinimizedWindowState;

                settings.ControlPanelWidth =
                    NormalizeLayoutDimension(
                        ControlPanelColumn.ActualWidth,
                        ControlPanelColumn.Width.Value,
                        ControlPanelColumn.MinWidth,
                        ControlPanelColumn.MaxWidth);

                settings.LogAreaHeight =
                    NormalizeLayoutDimension(
                        LogAreaRow.ActualHeight,
                        LogAreaRow.Height.Value,
                        LogAreaRow.MinHeight,
                        LogAreaRow.MaxHeight);

                settings.Save();
            }
            catch
            {
                // Layout persistence must never prevent a safe application shutdown.
            }
        }

        private async void PreviewOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_scheduledOrderActive || _liveSubmissionInProgress)
            {
                SetStatus("زمان‌بندی یا ارسال قبلی هنوز فعال است.");
                return;
            }

            if (!_webViewReady || Browser.CoreWebView2 == null)
            {
                SetStatus("EasyTrader هنوز آماده نیست.");
                return;
            }

            try
            {
                ClearConfirmedOrder();

                string json = await Browser.CoreWebView2.ExecuteScriptAsync(
                    OfficialOrderUiBridge.BuildOpenCurrentSymbolBuyDialogScript());

                OfficialOrderUiBridgeResult result =
                    OfficialOrderUiBridge.ParseResult(json);

                bool trustedClickFallbackUsed = false;

                if (result.HasStatus(OfficialOrderUiBridge.DialogOpenRequestedStatus) &&
                    result.ClickX > 0 &&
                    result.ClickY > 0)
                {
                    trustedClickFallbackUsed = true;

                    string moveJson = JsonSerializer.Serialize(new
                    {
                        type = "mouseMoved",
                        x = result.ClickX,
                        y = result.ClickY,
                        button = "none",
                        clickCount = 0
                    });

                    string downJson = JsonSerializer.Serialize(new
                    {
                        type = "mousePressed",
                        x = result.ClickX,
                        y = result.ClickY,
                        button = "left",
                        clickCount = 1
                    });

                    string upJson = JsonSerializer.Serialize(new
                    {
                        type = "mouseReleased",
                        x = result.ClickX,
                        y = result.ClickY,
                        button = "left",
                        clickCount = 1
                    });

                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchMouseEvent",
                        moveJson);

                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchMouseEvent",
                        downJson);

                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchMouseEvent",
                        upJson);
                }

                WriteImportant("");
                WriteImportant("========================================");
                WriteImportant("OPEN CURRENT EASYTRADER ORDER FORM");
                WriteImportant("========================================");
                WriteImportant("STATUS: " + result.Status);
                WriteImportant("REASON: " + result.Reason);
                WriteImportant(
                    "TRUSTED BUY CLICK FALLBACK: " +
                    (trustedClickFallbackUsed ? "YES" : "NO"));
                WriteImportant("HTTP POST: NOT SENT");
                WriteImportant("FINAL SUBMIT CLICK: NO");
                WriteImportant("========================================");

                bool opened =
                    result.HasStatus(OfficialOrderUiBridge.DialogAlreadyOpenStatus) ||
                    result.HasStatus(OfficialOrderUiBridge.DialogOpenRequestedStatus);

                ReadOrderFormButton.IsEnabled = opened;

                SetStatus(opened
                    ? "فرم رسمی خرید باز شد؛ قیمت و تعداد را در EasyTrader وارد کنید، سپس «خواندن و تأیید فرم» را بزنید."
                    : "فرم نماد جاری باز نشد: " + OfficialOrderUiBridge.GetUserMessage(result.Status));
            }
            catch (Exception ex)
            {
                ReadOrderFormButton.IsEnabled = false;
                WriteImportant("OPEN CURRENT ORDER FORM ERROR: " + ex.Message);
                SetStatus("خطا در باز کردن فرم سفارش EasyTrader.");
            }
        }

        private async void ReadOrderFormButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_scheduledOrderActive || _liveSubmissionInProgress)
            {
                SetStatus("زمان‌بندی یا ارسال قبلی هنوز فعال است.");
                return;
            }

            if (!_webViewReady || Browser.CoreWebView2 == null)
            {
                SetStatus("EasyTrader هنوز آماده نیست.");
                return;
            }

            ReadOrderFormButton.IsEnabled = false;

            try
            {
                string json = await Browser.CoreWebView2.ExecuteScriptAsync(
                    OfficialOrderUiBridge.BuildReadCurrentOrderFormScript());

                OfficialOrderFormReadResult form =
                    OfficialOrderUiBridge.ParseOrderFormReadResult(json);

                if (!form.HasStatus(OfficialOrderUiBridge.FormReadStatus))
                {
                    WriteImportant("");
                    WriteImportant("========================================");
                    WriteImportant("READ EASYTRADER FORM FAILED");
                    WriteImportant("========================================");
                    WriteImportant("STATUS: " + form.Status);
                    WriteImportant("REASON: " + form.Reason);
                    WriteImportant("SYMBOL: " + form.SymbolName);
                    WriteImportant("ISIN: " + form.SymbolIsin);
                    WriteImportant("PRICE: " + form.Price);
                    WriteImportant("QUANTITY: " + form.Quantity);
                    WriteImportant("COMMISSION: " + form.CommissionAmount);
                    WriteImportant("TOTAL VALUE: " + form.TotalValue);
                    WriteImportant("HTTP POST: NOT SENT");
                    WriteImportant("========================================");

                    ReadOrderFormButton.IsEnabled = true;
                    SetStatus("اطلاعات فرم خوانده نشد: " + form.Status);
                    return;
                }

                if (!TryBuildPayloadFromOfficialForm(
                    form,
                    out CreateOrderPayload? payload,
                    out OrderCalculationResult? calculation,
                    out string error) ||
                    payload?.Order == null ||
                    calculation == null)
                {
                    WriteImportant("");
                    WriteImportant("========================================");
                    WriteImportant("FORM DATA VALIDATION FAILED");
                    WriteImportant("========================================");
                    WriteImportant("ERROR: " + error);
                    WriteImportant("SYMBOL: " + form.SymbolName);
                    WriteImportant("ISIN: " + form.SymbolIsin);
                    WriteImportant("PRICE: " + form.Price);
                    WriteImportant("QUANTITY: " + form.Quantity);
                    WriteImportant("COMMISSION: " + form.CommissionAmount);
                    WriteImportant("TOTAL VALUE: " + form.TotalValue);
                    WriteImportant("HTTP POST: NOT SENT");
                    WriteImportant("========================================");

                    ReadOrderFormButton.IsEnabled = true;
                    SetStatus("اطلاعات خوانده‌شده معتبر نیست: " + error);
                    return;
                }

                string payloadJson = JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    });

                OrderConfirmationWindow confirmationWindow =
                    new OrderConfirmationWindow(payload, calculation, payloadJson)
                    {
                        Owner = this
                    };

                if (confirmationWindow.ShowDialog() != true)
                {
                    ClearConfirmedOrder();
                    ReadOrderFormButton.IsEnabled = true;
                    SetStatus("تأیید سفارش لغو شد؛ هیچ سفارشی ارسال نشد.");
                    return;
                }

                _confirmedOrderSnapshot = ConfirmedOrderSnapshot.Create(payloadJson);
                PrepareOrderButton.IsEnabled = true;
                PrepareOrderButton.Content = "آماده‌سازی محلی";

                UpdateCurrentOrderSetup(
                    payload.Order);

                WriteImportant("");
                WriteImportant("========================================");
                WriteImportant("ORDER READ FROM EASYTRADER FORM");
                WriteImportant("========================================");
                WriteImportant("SYMBOL: " + payload.Order.SymbolName);
                WriteImportant("ISIN: " + payload.Order.SymbolIsin);
                WriteImportant("PRICE: " + payload.Order.Price);
                WriteImportant("QUANTITY: " + payload.Order.Quantity);
                WriteImportant("SIDE: BUY");
                WriteImportant("COMMISSION AMOUNT (FROM EASYTRADER): " + form.CommissionAmount);
                WriteImportant("COMMISSION RATE: DERIVED FROM EASYTRADER FORM");
                WriteImportant("TOTAL VALUE (FROM EASYTRADER): " + form.TotalValue);
                WriteImportant("HTTP POST: NOT SENT");
                WriteImportant("========================================");

                PrepareOrderButton_Click(PrepareOrderButton, new RoutedEventArgs());

                SetStatus("فرم خوانده و تأیید شد؛ اگر LOCALLY READY است، «افزودن به زمان‌بندی» را بزنید.");
            }
            catch (Exception ex)
            {
                ReadOrderFormButton.IsEnabled = true;
                WriteImportant("READ EASYTRADER ORDER FORM ERROR: " + ex.Message);
                SetStatus("خطا در خواندن فرم سفارش EasyTrader.");
            }
        }

        private static bool TryBuildPayloadFromOfficialForm(
            OfficialOrderFormReadResult form,
            out CreateOrderPayload? payload,
            out OrderCalculationResult? calculation,
            out string error)
        {
            payload = null;
            calculation = null;

            string symbolName = (form.SymbolName ?? "").Trim();
            string isin = (form.SymbolIsin ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                error = "نام نماد قابل تشخیص نبود.";
                return false;
            }

            if (isin.Length != 12 || !isin.StartsWith("IR", StringComparison.Ordinal))
            {
                error = "ISIN معتبر قابل تشخیص نبود.";
                return false;
            }

            if (!long.TryParse(form.Price,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long price) || price <= 0)
            {
                error = "قیمت معتبر خوانده نشد.";
                return false;
            }

            if (!long.TryParse(form.Quantity,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long quantity) || quantity <= 0)
            {
                error = "تعداد معتبر خوانده نشد.";
                return false;
            }

            if (form.Side != 0)
            {
                error = "نسخه فعلی زمان‌بندی فقط خرید را پشتیبانی می‌کند.";
                return false;
            }

            if (!long.TryParse(form.CommissionAmount,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long commissionAmount) || commissionAmount <= 0)
            {
                error = "کارمزد معتبر از فرم EasyTrader خوانده نشد.";
                return false;
            }

            if (!long.TryParse(form.TotalValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long totalValueFromForm) || totalValueFromForm <= 0)
            {
                error = "قیمت کل معتبر از فرم EasyTrader خوانده نشد.";
                return false;
            }

            try
            {
                OrderCalculationResult gross =
                    OrderCalculator.Calculate(price, quantity, 0);

                if (gross.GrossValue <= 0)
                {
                    error = "ارزش ناخالص سفارش معتبر نیست.";
                    return false;
                }

                double commissionRate =
                    (double)commissionAmount / gross.GrossValue;

                calculation =
                    OrderCalculator.Calculate(price, quantity, commissionAmount);

                payload = new CreateOrderPayload
                {
                    Order = new Order
                    {
                        Commission = commissionRate,
                        CreateDateTime = DateTime.Now.ToString(
                            "M/d/yyyy, h:mm:ss tt",
                            System.Globalization.CultureInfo.InvariantCulture),
                        OrderFrom = 34,
                        OrderModelType = 1,
                        Price = price,
                        Quantity = quantity,
                        Side = 0,
                        SymbolIsin = isin,
                        SymbolName = symbolName,
                        TotalValue = totalValueFromForm,
                        ValidityType = 0
                    }
                };

                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // =====================================================
        // WINDOW LOADED
        // =====================================================

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await InitializeWebViewAsync();
        }

        // =====================================================
        // INITIALIZE WEBVIEW2
        // =====================================================

        private async Task InitializeWebViewAsync()
        {
            try
            {
                WriteLog(
                    "=================================");

                WriteLog(
                    "شروع InitializeWebViewAsync");

                WriteLog(
                    "=================================");

                SetStatus(
                    "در حال آماده‌سازی WebView2...");

                if (Browser.CoreWebView2 == null)
                {
                    await Browser.EnsureCoreWebView2Async();

                    WriteLog(
                        "CoreWebView2 آماده شد.");
                }
                else
                {
                    WriteLog(
                        "CoreWebView2 از قبل آماده بود.");
                }

                CoreWebView2 coreWebView =
                    Browser.CoreWebView2
                    ?? throw new InvalidOperationException(
                        "CoreWebView2 initialization failed.");

                _webViewReady = true;

                coreWebView.Settings.AreDevToolsEnabled =
                    true;

                coreWebView.Settings.AreDefaultContextMenusEnabled =
                    true;

                coreWebView.Settings.IsStatusBarEnabled =
                    true;

                coreWebView.ProcessFailed -=
                    CoreWebView2_ProcessFailed;

                coreWebView.ProcessFailed +=
                    CoreWebView2_ProcessFailed;

                EnableNetworkMonitoring();

                WriteLog(
                    "Monitoring قبل از Navigate فعال شد.");

                SetStatus(
                    "در حال ورود به EasyTrader...");

                coreWebView.Navigate(
                    EasyTraderUrl);
            }
            catch (Exception ex)
            {
                _webViewReady = false;

                WriteLog(
                    "خطا در InitializeWebViewAsync:");

                WriteLog(
                    ex.ToString());

                SetStatus(
                    "خطا در WebView2");
            }
        }

        // =====================================================
        // ENABLE NETWORK MONITORING
        // =====================================================

        private void EnableNetworkMonitoring()
        {
            if (!_webViewReady ||
                Browser.CoreWebView2 == null)
            {
                WriteLog(
                    "WebView2 هنوز آماده نیست.");

                return;
            }

            if (_monitoringEnabled)
            {
                return;
            }

            try
            {
                Browser.CoreWebView2
                    .AddWebResourceRequestedFilter(
                        "https://" + ApiHost + "/*",
                        CoreWebView2WebResourceContext.All);

                Browser.CoreWebView2
                    .WebResourceRequested +=
                    CoreWebView2_WebResourceRequested;

                Browser.CoreWebView2
                    .WebResourceResponseReceived +=
                    CoreWebView2_WebResourceResponseReceived;

                _monitoringEnabled = true;

                WriteLog("");

                WriteLog(
                    "=================================");

                WriteLog(
                    "NETWORK MONITORING فعال شد");

                WriteLog(
                    "=================================");

                WriteLog(
                    "Host: " + ApiHost);
            }
            catch (Exception ex)
            {
                WriteLog(
                    "خطا در فعال‌سازی Monitoring:");

                WriteLog(
                    ex.ToString());
            }
        }

        // =====================================================
        // REQUEST
        // =====================================================

        private void CoreWebView2_WebResourceRequested(
            object? sender,
            CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                CoreWebView2WebResourceRequest? request =
                    e.Request;

                if (request == null)
                    return;

                string url =
                    request.Uri ?? "";

                if (!IsMonitoredApiUrl(url))
                    return;

                string method =
                    request.Method ?? "";

                string urlForLog =
                    SanitizeUrl(url);

                bool important =
                    IsImportantRequest(url);

                ObserveLiveOrderRequest(
                    method,
                    url);

                ObserveAuthorizationHeader(
                    request.Headers);

                if (important)
                {
                    WriteImportant(
                        "");

                    WriteImportant(
                        "========================================");

                    WriteImportant(
                        "IMPORTANT API REQUEST");

                    WriteImportant(
                        "========================================");

                    WriteImportant(
                        "METHOD: " + method);

                    WriteImportant(
                        "URL: " + urlForLog);

                    WriteImportant(
                        "TIME: " +
                        DateTime.Now.ToString(
                            "HH:mm:ss"));

                    WriteImportant(
                        "REQUEST HEADERS: [OMITTED]");

                    WriteImportant(
                        "REQUEST BODY: [OMITTED]");
                }

                // -------------------------------------------------
                // Live Log
                // -------------------------------------------------

                WriteLog("");

                WriteLog(
                    ">>> API REQUEST");

                WriteLog(
                    method + " " + urlForLog);

                if (important)
                {
                    WriteLog(
                        "*** IMPORTANT API ***");
                }
            }
            catch (Exception ex)
            {
                WriteLog(
                    "Request Monitoring Error:");

                WriteLog(
                    ex.Message);
            }
        }

        // =====================================================
        // RESPONSE
        // =====================================================

        private void CoreWebView2_WebResourceResponseReceived(
            object? sender,
            CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                if (e?.Request == null ||
                    e.Response == null)
                    return;

                string url =
                    e.Request.Uri ?? "";

                if (!IsMonitoredApiUrl(url))
                    return;

                string urlForLog =
                    SanitizeUrl(url);

                int status =
                    e.Response.StatusCode;

                string method =
                    e.Request.Method ?? "";

                bool important =
                    IsImportantRequest(url) ||
                    status == 401 ||
                    status == 403 ||
                    status == 500;

                if (status >= 200 &&
                    status <= 299 &&
                    IsSessionRequest(url))
                {
                    _successfulSessionResponseObserved =
                        true;

                    SetStatus(
                        "پاسخ موفق نشست EasyTrader مشاهده شد.");
                }

                if (status >= 200 &&
                    status <= 299 &&
                    !method.Equals(
                        "OPTIONS",
                        StringComparison.OrdinalIgnoreCase) &&
                    IsProtectedOrderApiRequest(url))
                {
                    _successfulProtectedApiResponseObserved =
                        true;

                    SetStatus(
                        "پاسخ موفق API سفارش EasyTrader مشاهده شد.");
                }

                // -------------------------------------------------
                // Live Log
                // -------------------------------------------------

                WriteLog(
                    "<<< API RESPONSE");

                WriteLog(
                    method +
                    " " +
                    urlForLog);

                WriteLog(
                    "STATUS: " +
                    status);

                if (important)
                {
                    WriteLog(
                        "*** IMPORTANT RESPONSE ***");
                }

                // -------------------------------------------------
                // Important API
                // -------------------------------------------------

                if (important)
                {
                    WriteImportant("");

                    WriteImportant(
                        "----------------------------------------");

                    WriteImportant(
                        "IMPORTANT API RESPONSE");

                    WriteImportant(
                        "----------------------------------------");

                    WriteImportant(
                        "METHOD: " + method);

                    WriteImportant(
                        "URL: " + urlForLog);

                    WriteImportant(
                        "STATUS: " + status);

                    WriteImportant(
                        "REASON: " +
                        e.Response.ReasonPhrase);

                    WriteImportant(
                        "RESPONSE HEADERS: [OMITTED]");

                    if (status == 204 ||
                        method.Equals(
                            "OPTIONS",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        WriteImportant(
                            "RESPONSE BODY: <NONE>");
                    }
                    else
                    {
                        WriteImportant(
                            "RESPONSE BODY: [OMITTED]");
                    }

                    WriteImportant(
                        "========================================");
                }

                ObserveLiveOrderResponse(
                    method,
                    url,
                    status);
            }
            catch (Exception ex)
            {
                WriteLog(
                    "Response Monitoring Error:");

                WriteLog(
                    ex.Message);
            }
        }

        // =====================================================
        // IMPORTANT REQUEST DETECTION
        // =====================================================

        private bool IsImportantRequest(
            string url)
        {
            string u =
                url.ToLowerInvariant();

            return
                u.Contains(
                    "/easy/api/account/same-login")
                ||
                u.Contains(
                    "/easy/api/startsession")
                ||
                u.Contains(
                    "/core/api/v2/order")
                ||
                u.Contains(
                    "/core/api/order")
                ||
                u.Contains(
                    "/connect/token");
        }

        private static bool IsSessionRequest(
            string url)
        {
            return
                url.Contains(
                    "/easy/api/account/same-login",
                    StringComparison.OrdinalIgnoreCase)
                ||
                url.Contains(
                    "/easy/api/startsession",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProtectedOrderApiRequest(
            string url)
        {
            if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? uri))
            {
                return false;
            }

            string path =
                uri.AbsolutePath;

            return
                path.Equals(
                    "/core/api/order",
                    StringComparison.OrdinalIgnoreCase)
                ||
                path.StartsWith(
                    "/core/api/order/",
                    StringComparison.OrdinalIgnoreCase)
                ||
                path.Equals(
                    "/core/api/v2/order",
                    StringComparison.OrdinalIgnoreCase)
                ||
                path.StartsWith(
                    "/core/api/v2/order/",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCreateOrderRequest(
            string method,
            string url)
        {
            if (!method.Equals(
                "POST",
                StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                return false;
            }

            return
                uri.Host.Equals(
                    ApiHost,
                    StringComparison.OrdinalIgnoreCase)
                &&
                uri.AbsolutePath.Equals(
                    "/core/api/v2/order",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMonitoredApiUrl(
            string url)
        {
            return
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri)
                &&
                uri.Host.Equals(
                    ApiHost,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void ObserveAuthorizationHeader(
            CoreWebView2HttpRequestHeaders headers)
        {
            if (_authorizationHeaderObserved)
                return;

            foreach (var header in headers)
            {
                if (!header.Key.Equals(
                    "authorization",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _authorizationHeaderObserved =
                    true;

                WriteImportant("");
                WriteImportant(
                    "[AUTH] هدر احراز هویت مشاهده شد؛ " +
                    "مقدار آن خوانده یا ذخیره نشد.");

                SetStatus(
                    "درخواست احراز هویت‌شده مشاهده شد.");

                return;
            }
        }

        private void PrepareOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "LOCAL ORDER PREPARATION");
            WriteImportant(
                "========================================");

            ConfirmedOrderSnapshot? snapshot =
                _confirmedOrderSnapshot;

            if (snapshot == null)
            {
                WriteImportant(
                    "RESULT: BLOCKED");
                WriteImportant(
                    "REASON: سفارش تأییدشده‌ای در حافظه وجود ندارد.");
                WriteImportant(
                    "HTTP POST: NOT SENT");
                WriteImportant(
                    "========================================");

                PrepareOrderButton.IsEnabled =
                    false;

                return;
            }

            if (!snapshot.HasValidFingerprint())
            {
                WriteImportant(
                    "RESULT: BLOCKED");
                WriteImportant(
                    "REASON: Payload پس از تأیید تغییر کرده است.");
                WriteImportant(
                    "HTTP POST: NOT SENT");
                WriteImportant(
                    "========================================");

                ClearConfirmedOrder();

                return;
            }

            CreateOrderPayload? payload;

            try
            {
                payload =
                    JsonSerializer.Deserialize<CreateOrderPayload>(
                        snapshot.PayloadJson);
            }
            catch (JsonException)
            {
                WriteImportant(
                    "RESULT: BLOCKED");
                WriteImportant(
                    "REASON: Payload تأییدشده قابل خواندن نیست.");
                WriteImportant(
                    "HTTP POST: NOT SENT");
                WriteImportant(
                    "========================================");

                ClearConfirmedOrder();

                return;
            }

            OrderSubmissionValidationResult validation =
                OrderSubmissionValidator.Validate(
                    payload);

            if (!validation.IsValid)
            {
                WriteImportant(
                    "RESULT: BLOCKED");
                WriteImportant(
                    "REASON: " + validation.ErrorMessage);
                WriteImportant(
                    "HTTP POST: NOT SENT");
                WriteImportant(
                    "========================================");

                ClearConfirmedOrder();

                return;
            }

            bool siteApiAccessObserved =
                _authorizationHeaderObserved ||
                _successfulProtectedApiResponseObserved;

            if (!_successfulSessionResponseObserved ||
                !siteApiAccessObserved)
            {
                WriteImportant(
                    "RESULT: BLOCKED");
                WriteImportant(
                    "AUTHORIZATION HEADER PRESENCE OBSERVED: " +
                    (_authorizationHeaderObserved
                        ? "YES"
                        : "NO"));
                WriteImportant(
                    "SITE SESSION RESPONSE OBSERVED: " +
                    (_successfulSessionResponseObserved
                        ? "YES"
                        : "NO"));
                WriteImportant(
                    "PROTECTED ORDER API SUCCESS OBSERVED: " +
                    (_successfulProtectedApiResponseObserved
                        ? "YES"
                        : "NO"));
                WriteImportant(
                    "DIRECT API CREDENTIALS: NOT ACCESSED");
                WriteImportant(
                    "HTTP POST: NOT SENT");
                WriteImportant(
                    "========================================");

                SetStatus(
                    "آماده‌سازی متوقف شد؛ دسترسی طبیعی سایت هنوز مشاهده نشده است.");

                return;
            }

            WriteImportant(
                "RESULT: LOCALLY READY");
            WriteImportant(
                "PAYLOAD VALIDATION: PASSED");
            WriteImportant(
                "PAYLOAD FINGERPRINT: " +
                snapshot.ShortFingerprint);
            WriteImportant(
                "AUTHORIZATION HEADER PRESENCE OBSERVED: " +
                (_authorizationHeaderObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "SITE SESSION RESPONSE OBSERVED: YES");
            WriteImportant(
                "PROTECTED ORDER API SUCCESS OBSERVED: " +
                (_successfulProtectedApiResponseObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "LIVE SUBMISSION: REQUIRES FINAL CONFIRMATION");
            WriteImportant(
                "HTTP POST: NOT SENT");
            WriteImportant(
                "========================================");

            PrepareOrderButton.IsEnabled =
                true;

            PrepareOrderButton.Content =
                "آماده شد — بدون ارسال";

            SendLiveOrderButton.IsEnabled =
                true;

            SetStatus(
                "سفارش آماده است؛ برنامه نماد و فرم رسمی خرید را خودکار آماده می‌کند.");
        }

        /// <summary>
        /// نقطه ورود ارسال کنترل‌شده است. این متد فقط پس از اعتبارسنجی
        /// Snapshot، مشاهده نشست سایت و تأیید صریح کاربر، کنترل را به
        /// هماهنگ‌کننده زمان‌بندی می‌سپارد.
        /// </summary>
        private async void SendLiveOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // قفل هم‌زمانی مانع ایجاد دو چرخه ارسال از یک پنجره می‌شود.
            if (_scheduledOrderActive ||
                _liveSubmissionInProgress)
            {
                SetStatus(
                    "یک زمان‌بندی یا ارسال واقعی در حال اجرا است.");

                return;
            }

            SendLiveOrderButton.IsEnabled =
                true;

            OrderSession? createdSession =
                null;

            try
            {
                // Payload از Snapshot تأییدشده بازسازی و دوباره مستقل
                // اعتبارسنجی می‌شود؛ داده‌های قابل‌ویرایش UI مبنا نیستند.
                if (!TryGetValidatedConfirmedOrder(
                    out ConfirmedOrderSnapshot? snapshot,
                    out CreateOrderPayload? payload,
                    out string validationError) ||
                    snapshot == null ||
                    payload?.Order == null)
                {
                    WriteLiveSubmissionBlocked(
                        validationError);

                    ClearConfirmedOrder();

                    return;
                }

                // فقط وجود شواهد غیرحساس نشست بررسی می‌شود. مقدار Token،
                // Cookie یا Header در هیچ مرحله خوانده یا ذخیره نمی‌شود.

                bool siteApiAccessObserved =
                    _authorizationHeaderObserved ||
                    _successfulProtectedApiResponseObserved;

                if (!_successfulSessionResponseObserved ||
                    !siteApiAccessObserved)
                {
                    WriteLiveSubmissionBlocked(
                        "نشست معتبر EasyTrader هنوز تأیید نشده است.");

                    SendLiveOrderButton.IsEnabled =
                        true;

                    return;
                }

                CoreWebView2 coreWebView =
                    Browser.CoreWebView2
                    ?? throw new InvalidOperationException(
                        "WebView2 is not initialized.");

                Order order =
                    payload.Order;

                // تأیید نهایی، سفارش و بازه زمانی دقیق را به کاربر نشان می‌دهد.
                LiveOrderConfirmationWindow confirmationWindow =
                    new LiveOrderConfirmationWindow(
                        order,
                        snapshot.ShortFingerprint)
                    {
                        Owner = this
                    };

                bool confirmed =
                    confirmationWindow.ShowDialog() ==
                    true;

                if (!confirmed)
                {
                    WriteImportant("");
                    WriteImportant(
                        "========================================");
                    WriteImportant(
                        "LIVE ORDER SUBMISSION");
                    WriteImportant(
                        "========================================");
                    WriteImportant(
                        "RESULT: CANCELED BY USER");
                    WriteImportant(
                        "HTTP POST: NOT SENT");
                    WriteImportant(
                        "========================================");

                    SendLiveOrderButton.IsEnabled =
                        true;

                    SetStatus(
                        "ارسال واقعی لغو شد؛ هیچ سفارشی ارسال نشد.");

                    return;
                }

                // پس از بسته‌شدن پنجره تأیید، Snapshot دوباره تطبیق داده می‌شود
                // تا تغییر هم‌زمان سفارش نتواند وارد مسیر ارسال شود.
                if (!ReferenceEquals(
                    _confirmedOrderSnapshot,
                    snapshot) ||
                    !snapshot.HasValidFingerprint())
                {
                    WriteLiveSubmissionBlocked(
                        "سفارش تأییدشده پیش از ارسال تغییر کرده است.");

                    ClearConfirmedOrder();

                    return;
                }

                long creationSequence =
                    checked(
                        ++_nextOrderSessionSequence);

                createdSession =
                    new OrderSession(
                        creationSequence,
                        order,
                        confirmationWindow.MaxQuantityPerOrder,
                        confirmationWindow.ScheduledStartAt,
                        confirmationWindow.ScheduledEndAt,
                        snapshot);

                createdSession.SetState(
                    OrderSessionState.Waiting,
                    "به زمان‌بندی افزوده شد");

                _orderSessions.Insert(
                    0,
                    createdSession);

                SessionDataGrid.SelectedItem =
                    createdSession;

                SessionDataGrid.ScrollIntoView(
                    createdSession);

                _activeOrderSession =
                    createdSession;

                // از این نقطه به بعد، چرخه زمان‌بندی مالک کامل وضعیت ارسال است.
                await RunScheduledOrderAsync(
                    coreWebView,
                    snapshot,
                    order,
                    confirmationWindow.ScheduledStartAt,
                    confirmationWindow.ScheduledEndAt,
                    confirmationWindow.MaxQuantityPerOrder,
                    createdSession);
            }
            catch (Exception)
            {
                createdSession?.SetState(
                    OrderSessionState.Failed,
                    "خطای داخلی پیش از اجرای کامل نشست",
                    "مسیر کنترل‌شده پیش از تکمیل نشست متوقف شد.");

                ResetLiveSubmissionTracking();

                WriteLiveSubmissionBlocked(
                    "خطای داخلی در مسیر کنترل‌شده رخ داد.");

                if (_confirmedOrderSnapshot != null)
                {
                    SendLiveOrderButton.IsEnabled =
                        true;
                }
            }
        }

        /// <summary>
        /// زمان‌بند واقعی یک‌ثانیه‌ای: شروع تلاش هر slot به نتیجه UI یا HTTP
        /// slot قبلی وابسته نیست. برای جلوگیری از oversend، حجم هر تلاش پیش از
        /// شروع به صورت in-flight رزرو می‌شود و فقط در صورت CLICKED شدن به sent
        /// منتقل می‌شود؛ شکست پیش از کلیک رزرو را آزاد می‌کند.
        /// </summary>
        private async Task RunScheduledOrderAsync(
            CoreWebView2 coreWebView,
            ConfirmedOrderSnapshot snapshot,
            Order order,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            long maxQuantityPerOrder,
            OrderSession session)
        {
            ArgumentNullException.ThrowIfNull(
                session);

            if (endAt <= startAt)
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "بازه زمانی نامعتبر",
                    "ساعت پایان باید بعد از ساعت شروع باشد.");

                WriteLiveSubmissionBlocked(
                    "بازه زمانی ارسال معتبر نیست.");

                return;
            }

            if (maxQuantityPerOrder <= 0 ||
                maxQuantityPerOrder > order.Quantity)
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "سقف هر سفارش نامعتبر است",
                    "سقف هر سفارش باید مثبت و حداکثر برابر حجم کل باشد.");

                WriteLiveSubmissionBlocked(
                    "حداکثر حجم هر سفارش معتبر نیست.");

                return;
            }

            using CancellationTokenSource cancellationSource =
                new CancellationTokenSource();

            _scheduledOrderCancellation =
                cancellationSource;

            _scheduledOrderActive =
                true;

            SetScheduledOrderControls(
                true);

            long totalQuantity =
                order.Quantity;

            long sentQuantity =
                0;

            long inFlightQuantity =
                0;

            int clickedOrderCount =
                0;

            int slotNumber =
                0;

            object accountingLock =
                new object();

            System.Collections.Generic.List<Task>
                activeDispatchTasks =
                    new System.Collections.Generic.List<Task>();

            void UpdateSessionProgress(
                DateTimeOffset? nextDueAt,
                string status)
            {
                long sentSnapshot;
                long inFlightSnapshot;
                int clickedSnapshot;

                lock (accountingLock)
                {
                    sentSnapshot =
                        sentQuantity;

                    inFlightSnapshot =
                        inFlightQuantity;

                    clickedSnapshot =
                        clickedOrderCount;
                }

                session.UpdateProgress(
                    sentSnapshot,
                    inFlightSnapshot,
                    clickedSnapshot,
                    nextDueAt,
                    status);
            }

            session.SetState(
                OrderSessionState.Waiting,
                "در انتظار شروع بازه");

            UpdateSessionProgress(
                startAt,
                "در انتظار شروع بازه");

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "NON-BLOCKING CLOCK SPLIT ORDER ARMED");
            WriteImportant(
                "========================================");
            WriteImportant(
                "START: " +
                startAt.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff zzz"));
            WriteImportant(
                "END: " +
                endAt.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff zzz"));
            WriteImportant(
                "CLOCK SLOT: 1 SECOND");
            WriteImportant(
                "WAIT FOR PREVIOUS UI DISPATCH: NO");
            WriteImportant(
                "WAIT FOR PREVIOUS HTTP RESPONSE: NO");
            WriteImportant(
                "TOTAL QUANTITY: " +
                totalQuantity);
            WriteImportant(
                "MAX QUANTITY PER ORDER: " +
                maxQuantityPerOrder);
            WriteImportant(
                "OVER-SEND GUARD: SENT + IN-FLIGHT <= TOTAL");
            WriteImportant(
                "========================================");

            try
            {
                // PRE-WARM:
                // فرم رسمی خرید کمی قبل از startAt آماده می‌شود.
                // این مرحله فقط prepare است و هیچ کلیک ارسال انجام نمی‌دهد.
                DateTimeOffset preWarmAt =
                    startAt -
                    ScheduledOrderPreWarmLeadTime;

                TimeSpan preWarmWait =
                    preWarmAt -
                    DateTimeOffset.Now;

                if (preWarmWait >
                    TimeSpan.Zero)
                {
                    WriteImportant(
                        "PRE-WARM WAIT UNTIL: " +
                        preWarmAt.ToString(
                            "HH:mm:ss.fff"));

                    await Task.Delay(
                        preWarmWait,
                        cancellationSource.Token);
                }

                cancellationSource.Token
                    .ThrowIfCancellationRequested();

                if (DateTimeOffset.Now <
                    endAt)
                {
                    session.SetState(
                        OrderSessionState.PreWarming,
                        "در حال آماده‌سازی فرم رسمی");

                    string preWarmNonce =
                        Guid.NewGuid()
                            .ToString(
                                "N");

                    try
                    {
                        DateTimeOffset preWarmStartedAt =
                            DateTimeOffset.Now;

                        OfficialOrderUiBridgeResult preWarmResult =
                            await PrepareOfficialOrderFormAsync(
                                coreWebView,
                                CreateScheduledSliceOrder(
                                    order,
                                    Math.Min(
                                        totalQuantity,
                                        maxQuantityPerOrder)),
                                preWarmNonce,
                                cancellationSource.Token);

                        DateTimeOffset preWarmCompletedAt =
                            DateTimeOffset.Now;

                        WriteImportant("");
                        WriteImportant(
                            "SCHEDULE PRE-WARM STATUS: " +
                            preWarmResult.Status);
                        WriteImportant(
                            "SCHEDULE PRE-WARM DURATION MS: " +
                            (preWarmCompletedAt -
                                preWarmStartedAt)
                                .TotalMilliseconds
                                .ToString(
                                    "F1",
                                    System.Globalization.CultureInfo.InvariantCulture));

                        if (!preWarmResult.HasStatus(
                            OfficialOrderUiBridge.PreparedStatus))
                        {
                            session.SetState(
                                OrderSessionState.Failed,
                                "پیش‌آماده‌سازی ناموفق بود",
                                "فرم رسمی خرید قبل از اولین اسلات آماده نشد.");

                            WriteScheduledOrderStopped(
                                "فرم رسمی خرید قبل از شروع زمان‌بندی آماده نشد.",
                                "PRE-WARM FAILED BEFORE FIRST SLOT");

                            return;
                        }
                    }
                    finally
                    {
                        await TryClearOfficialPreparedStateAsync(
                            coreWebView,
                                preWarmNonce);
                    }

                    session.SetState(
                        OrderSessionState.Ready,
                        "فرم رسمی برای اولین اسلات آماده است");
                }

                DateTimeOffset nextSlot =
                    startAt;

                while (nextSlot <
                    endAt)
                {
                    cancellationSource.Token
                        .ThrowIfCancellationRequested();

                    TimeSpan wait =
                        nextSlot -
                        DateTimeOffset.Now;

                    if (wait >
                        TimeSpan.Zero)
                    {
                        await Task.Delay(
                            wait,
                            cancellationSource.Token);
                    }

                    cancellationSource.Token
                        .ThrowIfCancellationRequested();

                    DateTimeOffset slotStartedAt =
                        DateTimeOffset.Now;

                    if (slotStartedAt >=
                        endAt)
                    {
                        break;
                    }

                    if (!ReferenceEquals(
                        _confirmedOrderSnapshot,
                        snapshot) ||
                        !snapshot.HasValidFingerprint())
                    {
                        session.SetState(
                            OrderSessionState.Failed,
                            "Snapshot تأییدشده تغییر کرده است",
                            "اجرای نشست پیش از اسلات بعدی متوقف شد.");

                        WriteScheduledOrderStopped(
                            "سفارش تأییدشده تغییر کرده است.",
                            "STOPPED BEFORE NEXT SLOT");

                        break;
                    }

                    slotNumber++;

                    long currentQuantity;

                    lock (accountingLock)
                    {
                        long availableQuantity =
                            totalQuantity -
                            sentQuantity -
                            inFlightQuantity;

                        if (availableQuantity <= 0)
                        {
                            currentQuantity =
                                0;
                        }
                        else
                        {
                            currentQuantity =
                                Math.Min(
                                    availableQuantity,
                                    maxQuantityPerOrder);

                            inFlightQuantity =
                                checked(
                                    inFlightQuantity +
                                    currentQuantity);
                        }
                    }

                    if (currentQuantity > 0)
                    {
                        int capturedSlotNumber =
                            slotNumber;

                        long capturedQuantity =
                            currentQuantity;

                        DateTimeOffset capturedNextDueAt =
                            nextSlot +
                            ScheduledOrderRetryDelay;

                        session.SetState(
                            OrderSessionState.Running,
                            "اسلات " +
                            capturedSlotNumber +
                            " در حال اجرا است");

                        UpdateSessionProgress(
                            nextSlot,
                            "حجم " +
                            capturedQuantity +
                            " برای اسلات " +
                            capturedSlotNumber +
                            " رزرو شد");

                        Order currentOrder =
                            CreateScheduledSliceOrder(
                                order,
                                capturedQuantity);

                        WriteImportant("");
                        WriteImportant(
                            "CLOCK SLOT STARTED: " +
                            capturedSlotNumber);
                        WriteImportant(
                            "TARGET: " +
                            nextSlot.ToString(
                                "HH:mm:ss.fff"));
                        WriteImportant(
                            "ACTUAL: " +
                            slotStartedAt.ToString(
                                "HH:mm:ss.fff"));
                        WriteImportant(
                            "RESERVED QUANTITY: " +
                            capturedQuantity);

                        Task dispatchTask =
                            DispatchReservedSliceAsync(
                                coreWebView,
                                snapshot,
                                currentOrder,
                                capturedSlotNumber,
                                capturedQuantity,
                                accountingLock,
                                CancellationToken.None,
                                onClicked: quantity =>
                                {
                                    lock (accountingLock)
                                    {
                                        inFlightQuantity =
                                            checked(
                                                inFlightQuantity -
                                                quantity);

                                        sentQuantity =
                                            checked(
                                                sentQuantity +
                                                quantity);

                                        clickedOrderCount++;
                                    }

                                    UpdateSessionProgress(
                                        capturedNextDueAt,
                                        "اسلات " +
                                        capturedSlotNumber +
                                        " با کلیک رسمی ثبت شد");
                                },
                                onNotClicked: quantity =>
                                {
                                    lock (accountingLock)
                                    {
                                        inFlightQuantity =
                                            checked(
                                                inFlightQuantity -
                                                quantity);
                                    }

                                    UpdateSessionProgress(
                                        capturedNextDueAt,
                                        "اسلات " +
                                        capturedSlotNumber +
                                        " کلیک نشد؛ رزرو آزاد شد");
                                });

                        activeDispatchTasks.Add(
                            dispatchTask);
                    }
                    else
                    {
                        long sentSnapshot;
                        long inFlightSnapshot;

                        lock (accountingLock)
                        {
                            sentSnapshot =
                                sentQuantity;

                            inFlightSnapshot =
                                inFlightQuantity;
                        }

                        WriteImportant(
                            "CLOCK SLOT " +
                            slotNumber +
                            ": NO FREE QUANTITY");
                        WriteImportant(
                            "SENT: " +
                            sentSnapshot);
                        WriteImportant(
                            "IN-FLIGHT: " +
                            inFlightSnapshot);

                        UpdateSessionProgress(
                            nextSlot,
                            "حجم آزاد برای اسلات جدید وجود ندارد");
                    }

                    bool allQuantityAccounted;

                    lock (accountingLock)
                    {
                        allQuantityAccounted =
                            sentQuantity >=
                            totalQuantity;
                    }

                    if (allQuantityAccounted)
                    {
                        break;
                    }

                    nextSlot =
                        nextSlot +
                        ScheduledOrderRetryDelay;

                    // اگر event-loop دیر بیدار شد، slotهای گذشته burst نمی‌شوند.
                    DateTimeOffset nowAfterScheduling =
                        DateTimeOffset.Now;

                    while (nextSlot <=
                        nowAfterScheduling &&
                        nextSlot <
                        endAt)
                    {
                        nextSlot =
                            nextSlot +
                            ScheduledOrderRetryDelay;
                    }
                }

                if (activeDispatchTasks.Count > 0)
                {
                    WriteImportant(
                        "CLOCK WINDOW CLOSED; WAITING FOR ACTIVE UI DISPATCH TASKS.");

                    try
                    {
                        await Task.WhenAll(
                            activeDispatchTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }

                long finalSent;
                long finalInFlight;

                lock (accountingLock)
                {
                    finalSent =
                        sentQuantity;

                    finalInFlight =
                        inFlightQuantity;
                }

                WriteImportant("");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "NON-BLOCKING CLOCK SPLIT ORDER FINISHED");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "TOTAL QUANTITY: " +
                    totalQuantity);
                WriteImportant(
                    "SENT QUANTITY: " +
                    finalSent);
                WriteImportant(
                    "IN-FLIGHT QUANTITY: " +
                    finalInFlight);
                WriteImportant(
                    "UNSENT QUANTITY: " +
                    (totalQuantity -
                        finalSent -
                        finalInFlight));
                WriteImportant(
                    "CLICKED ORDER COUNT: " +
                    clickedOrderCount);
                WriteImportant(
                    "BROKER OUTCOME: VERIFY IN EASYTRADER ORDER LIST");
                WriteImportant(
                    "========================================");

                if (session.State ==
                    OrderSessionState.Failed)
                {
                    UpdateSessionProgress(
                        null,
                        session.LastStatus);
                }
                else
                {
                    UpdateSessionProgress(
                        null,
                        finalSent == totalQuantity
                            ? "کل حجم از مسیر رسمی کلیک شد"
                            : "بازه پایان یافت و بخشی از حجم باقی ماند");

                    session.SetState(
                        OrderSessionState.Completed,
                        finalSent == totalQuantity
                            ? "کل حجم از مسیر رسمی کلیک شد؛ نتیجه کارگزاری را بررسی کنید"
                            : "بازه پایان یافت؛ بخشی از حجم ارسال نشد");
                }

                SetStatus(
                    finalSent == totalQuantity
                        ? "کل حجم از طریق کلیک رسمی ارسال شد؛ نتیجه سفارش‌ها را در EasyTrader بررسی کنید."
                        : "بازه ارسال پایان یافت؛ بخشی از حجم ارسال نشد.");
            }
            catch (OperationCanceledException)
            {
                // لغو فقط از ایجاد slot جدید جلوگیری می‌کند.
                // dispatchهایی که قبلاً شروع شده‌اند باید تا نتیجه UI
                // ادامه پیدا کنند تا رزرو حجم اشتباه آزاد نشود.
                if (activeDispatchTasks.Count > 0)
                {
                    WriteImportant(
                        "CANCEL REQUESTED; WAITING FOR ALREADY-LAUNCHED UI DISPATCH TASKS.");

                    try
                    {
                        await Task.WhenAll(
                            activeDispatchTasks);
                    }
                    catch (Exception ex)
                    {
                        WriteImportant(
                            "ACTIVE DISPATCH SETTLE ERROR AFTER CANCEL: " +
                            ex.Message);
                    }
                }

                long canceledSent;
                long canceledInFlight;

                lock (accountingLock)
                {
                    canceledSent =
                        sentQuantity;

                    canceledInFlight =
                        inFlightQuantity;
                }

                WriteImportant(
                    "CANCEL FINAL SENT QUANTITY: " +
                    canceledSent);
                WriteImportant(
                    "CANCEL FINAL IN-FLIGHT QUANTITY: " +
                    canceledInFlight);

                UpdateSessionProgress(
                    null,
                    "لغو شد؛ dispatchهای شروع‌شده تعیین تکلیف شدند");

                session.SetState(
                    OrderSessionState.Canceled,
                    "توسط کاربر لغو شد");

                WriteScheduledOrderStopped(
                    "زمان‌بندی توسط کاربر لغو شد؛ slot جدید ایجاد نشد و dispatchهای قبلاً شروع‌شده تعیین تکلیف شدند.",
                    "CANCELED BY USER");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "NON-BLOCKING CLOCK ERROR: " +
                    ex.Message);

                // مانند cancellation، خطای داخلی فقط باید ایجاد slot جدید را
                // متوقف کند. dispatchهایی که قبلاً شروع شده‌اند ممکن است کلیک
                // رسمی را انجام داده باشند؛ بنابراین قبل از cleanup باید
                // نتیجه همه آنها برای حسابداری sent/in-flight تعیین تکلیف شود.
                if (activeDispatchTasks.Count > 0)
                {
                    WriteImportant(
                        "INTERNAL ERROR; WAITING FOR ALREADY-LAUNCHED UI DISPATCH TASKS.");

                    try
                    {
                        await Task.WhenAll(
                            activeDispatchTasks);
                    }
                    catch (Exception settleException)
                    {
                        WriteImportant(
                            "ACTIVE DISPATCH SETTLE ERROR AFTER INTERNAL ERROR: " +
                            settleException.Message);
                    }
                }

                long errorSent;
                long errorInFlight;

                lock (accountingLock)
                {
                    errorSent =
                        sentQuantity;

                    errorInFlight =
                        inFlightQuantity;
                }

                WriteImportant(
                    "INTERNAL ERROR FINAL SENT QUANTITY: " +
                    errorSent);
                WriteImportant(
                    "INTERNAL ERROR FINAL IN-FLIGHT QUANTITY: " +
                    errorInFlight);

                UpdateSessionProgress(
                    null,
                    "خطای داخلی؛ dispatchهای شروع‌شده تعیین تکلیف شدند");

                session.SetState(
                    OrderSessionState.Failed,
                    "خطای داخلی در اجرای نشست",
                    ex.Message);

                WriteScheduledOrderStopped(
                    "خطای داخلی رخ داد؛ slot جدید متوقف شد و dispatchهای قبلاً شروع‌شده تعیین تکلیف شدند.",
                    "STOPPED ON INTERNAL ERROR");
            }
            finally
            {
                ResetLiveSubmissionTracking();

                if (ReferenceEquals(
                    _scheduledOrderCancellation,
                    cancellationSource))
                {
                    _scheduledOrderCancellation =
                        null;
                }

                _scheduledOrderActive =
                    false;

                if (ReferenceEquals(
                    _activeOrderSession,
                    session))
                {
                    _activeOrderSession =
                        null;
                }

                ClearConfirmedOrder();

                SetScheduledOrderControls(
                    false);
            }
        }

        private async Task DispatchReservedSliceAsync(
            CoreWebView2 coreWebView,
            ConfirmedOrderSnapshot snapshot,
            Order order,
            int slotNumber,
            long reservedQuantity,
            object accountingLock,
            CancellationToken cancellationToken,
            Action<long> onClicked,
            Action<long> onNotClicked)
        {
            OfficialOrderUiBridgeResult result;

            try
            {
                result =
                    await ExecuteClockDrivenSliceAttemptAsync(
                        coreWebView,
                        snapshot,
                        order,
                        slotNumber,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // مسیر scheduler برای dispatch شروع‌شده CancellationToken.None
                // می‌فرستد؛ این شاخه فقط برای callers احتمالی دیگر باقی می‌ماند.
                onNotClicked(
                    reservedQuantity);

                throw;
            }
            catch (Exception ex)
            {
                onNotClicked(
                    reservedQuantity);

                WriteImportant(
                    "CLOCK SLOT " +
                    slotNumber +
                    " DISPATCH ERROR: " +
                    ex.Message);

                return;
            }

            if (result.HasStatus(
                OfficialOrderUiBridge.ClickedStatus))
            {
                onClicked(
                    reservedQuantity);

                WriteImportant(
                    "CLOCK SLOT " +
                    slotNumber +
                    ": CLICKED; QUANTITY COMMITTED AS SENT: " +
                    reservedQuantity);

                return;
            }

            onNotClicked(
                reservedQuantity);

            WriteImportant(
                "CLOCK SLOT " +
                slotNumber +
                ": NOT CLICKED; RESERVATION RELEASED: " +
                reservedQuantity);

            WriteImportant(
                "STATUS: " +
                result.Status);
        }

        private static Order CreateScheduledSliceOrder(
            Order source,
            long quantity)
        {
            if (quantity <= 0 ||
                quantity > source.Quantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity));
            }

            long grossValue =
                checked(
                    source.Price *
                    quantity);

            decimal commissionAmountDecimal =
                decimal.Round(
                    grossValue *
                    (decimal)source.Commission,
                    0,
                    MidpointRounding.AwayFromZero);

            long commissionAmount =
                decimal.ToInt64(
                    commissionAmountDecimal);

            long totalValue =
                checked(
                    grossValue +
                    commissionAmount);

            return new Order
            {
                Commission =
                    source.Commission,

                CreateDateTime =
                    DateTime.Now.ToString(
                        "M/d/yyyy, h:mm:ss tt",
                        System.Globalization.CultureInfo.InvariantCulture),

                OrderFrom =
                    source.OrderFrom,

                OrderModelType =
                    source.OrderModelType,

                Price =
                    source.Price,

                Quantity =
                    quantity,

                Side =
                    source.Side,

                SymbolIsin =
                    source.SymbolIsin,

                SymbolName =
                    source.SymbolName,

                TotalValue =
                    totalValue,

                ValidityType =
                    source.ValidityType
            };
        }

        private async Task<OfficialOrderUiBridgeResult>
            ExecuteClockDrivenSliceAttemptAsync(
                CoreWebView2 coreWebView,
                ConfirmedOrderSnapshot snapshot,
                Order order,
                int slotNumber,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                _confirmedOrderSnapshot,
                snapshot) ||
                !snapshot.HasValidFingerprint())
            {
                return new OfficialOrderUiBridgeResult
                {
                    Status =
                        "CONFIRMED_ORDER_CHANGED",

                    Reason =
                        "Confirmed order changed before scheduled slot."
                };
            }

            string nonce =
                Guid.NewGuid()
                    .ToString(
                        "N");

            try
            {
                string resultJson =
                    await coreWebView.ExecuteScriptAsync(
                        OfficialOrderUiBridge
                            .BuildAtomicScheduledSubmitScript(
                                order,
                                nonce));

                // بعد از ExecuteScriptAsync دیگر cancellation بررسی نمی‌شود.
                // چون JavaScript ممکن است کلیک رسمی را انجام داده باشد و
                // نتیجه باید حتماً برای حسابداری sent/in-flight پردازش شود.
                OfficialOrderUiBridgeResult result =
                    OfficialOrderUiBridge.ParseResult(
                        resultJson);

                WriteImportant(
                    "ATOMIC UI SLOT " +
                    slotNumber +
                    ": " +
                    result.Status);

                if (result.HasStatus(
                    OfficialOrderUiBridge.ClickedStatus))
                {
                    await PrimeNextScheduledOrderFormAsync(
                        coreWebView,
                        order);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "ATOMIC UI SLOT ERROR: " +
                    ex.Message);

                return new OfficialOrderUiBridgeResult
                {
                    Status =
                        "ATOMIC_UI_ERROR",

                    Reason =
                        "Atomic scheduled UI action failed before a confirmed click."
                };
            }
            finally
            {
                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    nonce);
            }
        }

        private async Task PrimeNextScheduledOrderFormAsync(
            CoreWebView2 coreWebView,
            Order order)
        {
            try
            {
                DateTimeOffset primeDeadline =
                    DateTimeOffset.Now +
                    TimeSpan.FromMilliseconds(
                        850);

                bool trustedBuyClickRequested =
                    false;

                while (DateTimeOffset.Now <
                    primeDeadline)
                {
                    string nonce =
                        Guid.NewGuid()
                            .ToString(
                                "N");

                    string prepareJson =
                        await coreWebView.ExecuteScriptAsync(
                            OfficialOrderUiBridge.BuildPrepareScript(
                                order,
                                nonce));

                    OfficialOrderUiBridgeResult prepareResult =
                        OfficialOrderUiBridge.ParseResult(
                            prepareJson);

                    if (prepareResult.HasStatus(
                        OfficialOrderUiBridge.PreparedStatus))
                    {
                        await TryClearOfficialPreparedStateAsync(
                            coreWebView,
                            nonce);

                        WriteImportant(
                            "NEXT FORM PRIME: READY");

                        return;
                    }

                    await TryClearOfficialPreparedStateAsync(
                        coreWebView,
                        nonce);

                    if (!prepareResult.HasStatus(
                        "ORDER_DIALOG_NOT_FOUND"))
                    {
                        WriteImportant(
                            "NEXT FORM PRIME STATUS: " +
                            prepareResult.Status);

                        return;
                    }

                    string ensureJson =
                        await coreWebView.ExecuteScriptAsync(
                            OfficialOrderUiBridge.BuildEnsureBuyDialogScript(
                                order));

                    OfficialOrderUiBridgeResult ensureResult =
                        OfficialOrderUiBridge.ParseResult(
                            ensureJson);

                    if (ensureResult.HasStatus(
                        OfficialOrderUiBridge.DialogOpenRequestedStatus))
                    {
                        if (!trustedBuyClickRequested &&
                            ensureResult.ClickX > 0 &&
                            ensureResult.ClickY > 0)
                        {
                            string moveJson = JsonSerializer.Serialize(new
                            {
                                type = "mouseMoved",
                                x = ensureResult.ClickX,
                                y = ensureResult.ClickY,
                                button = "none",
                                clickCount = 0
                            });

                            string downJson = JsonSerializer.Serialize(new
                            {
                                type = "mousePressed",
                                x = ensureResult.ClickX,
                                y = ensureResult.ClickY,
                                button = "left",
                                clickCount = 1
                            });

                            string upJson = JsonSerializer.Serialize(new
                            {
                                type = "mouseReleased",
                                x = ensureResult.ClickX,
                                y = ensureResult.ClickY,
                                button = "left",
                                clickCount = 1
                            });

                            await coreWebView.CallDevToolsProtocolMethodAsync(
                                "Input.dispatchMouseEvent",
                                moveJson);

                            await coreWebView.CallDevToolsProtocolMethodAsync(
                                "Input.dispatchMouseEvent",
                                downJson);

                            await coreWebView.CallDevToolsProtocolMethodAsync(
                                "Input.dispatchMouseEvent",
                                upJson);

                            trustedBuyClickRequested =
                                true;

                            WriteImportant(
                                "NEXT FORM PRIME: TRUSTED BUY CLICK REQUESTED");
                        }

                        await Task.Delay(
                            75);

                        continue;
                    }

                    if (ensureResult.HasStatus(
                        OfficialOrderUiBridge.DialogAlreadyOpenStatus))
                    {
                        await Task.Delay(
                            50);

                        continue;
                    }

                    if (ensureResult.HasStatus(
                        OfficialOrderUiBridge.SymbolSelectionRequestedStatus))
                    {
                        await Task.Delay(
                            75);

                        continue;
                    }

                    WriteImportant(
                        "NEXT FORM PRIME STATUS: " +
                        ensureResult.Status);

                    return;
                }

                WriteImportant(
                    "NEXT FORM PRIME: DEADLINE EXPIRED BEFORE READY");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "NEXT FORM PRIME ERROR: " +
                    ex.Message);
            }
        }

        private async Task<ScheduledOrderAttemptOutcome>
            ExecuteScheduledOrderAttemptAsync(
                CoreWebView2 coreWebView,
                ConfirmedOrderSnapshot snapshot,
                Order order,
                DateTimeOffset endAt,
                CancellationToken cancellationToken)
        {
            // Nonce فقط وضعیت آماده‌شده همین تلاش را به کلیک نهایی پیوند می‌دهد.
            string preparationNonce =
                Guid.NewGuid()
                    .ToString(
                        "N");

            // این مرحله نماد/ISIN را تطبیق می‌دهد و تعداد و قیمت را در فرم
            // رسمی EasyTrader قرار می‌دهد؛ هنوز هیچ POST ارسال نمی‌شود.
            OfficialOrderUiBridgeResult prepareResult =
                await PrepareOfficialOrderFormAsync(
                    coreWebView,
                    order,
                    preparationNonce,
                    cancellationToken);

            if (!prepareResult.HasStatus(
                OfficialOrderUiBridge.PreparedStatus))
            {
                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce);

                WriteImportant(
                    "RESULT: RETRYABLE BEFORE POST");
                WriteImportant(
                    "REASON: " +
                    OfficialOrderUiBridge.GetUserMessage(
                        prepareResult.Status));
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.Now >=
                endAt)
            {
                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce);

                WriteImportant(
                    "RESULT: WINDOW EXPIRED BEFORE POST");
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            if (!ReferenceEquals(
                _confirmedOrderSnapshot,
                snapshot) ||
                !snapshot.HasValidFingerprint())
            {
                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce);

                WriteImportant(
                    "RESULT: ORDER CHANGED BEFORE POST");
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.AmbiguousFailure;
            }

            // شناسه محلی، پاسخ مشاهده‌شده را به همین تلاش متصل می‌کند؛
            // این شناسه به EasyTrader ارسال نمی‌شود.
            string submissionId =
                Guid.NewGuid()
                    .ToString(
                        "N");

            TaskCompletionSource<LiveOrderNetworkObservation>
                completionSource =
                    new TaskCompletionSource<LiveOrderNetworkObservation>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

            _liveSubmissionInProgress =
                true;

            _liveOrderRequestObserved =
                false;

            _activeLiveSubmissionId =
                submissionId;

            _activeLiveSubmissionFingerprint =
                snapshot.ShortFingerprint;

            _activeLiveSubmissionCompletion =
                completionSource;

            OfficialOrderUiBridgeResult submitResult;

            try
            {
                // تنها نقطه‌ای که اجازه فعال‌کردن دکمه رسمی «ارسال خرید» را دارد.
                string submitResultJson =
                    await coreWebView.ExecuteScriptAsync(
                        OfficialOrderUiBridge.BuildSubmitScript(
                            order,
                            preparationNonce));

                submitResult =
                    OfficialOrderUiBridge.ParseResult(
                        submitResultJson);
            }
            catch (Exception)
            {
                ResetLiveSubmissionTracking();

                WriteImportant(
                    "RESULT: AMBIGUOUS AFTER SUBMIT INVOCATION");
                WriteImportant(
                    "HTTP RESPONSE: NOT CONFIRMED");

                return
                    ScheduledOrderAttemptOutcome.AmbiguousFailure;
            }

            if (!submitResult.HasStatus(
                OfficialOrderUiBridge.ClickedStatus))
            {
                ResetLiveSubmissionTracking();

                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce);

                WriteImportant(
                    "RESULT: RETRYABLE BEFORE POST");
                WriteImportant(
                    "REASON: " +
                    OfficialOrderUiBridge.GetUserMessage(
                        submitResult.Status));
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            WriteImportant(
                "OFFICIAL EASYTRADER ACTION: INVOKED ONCE");
            WriteImportant(
                "PAYLOAD FINGERPRINT: " +
                snapshot.ShortFingerprint);
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "HTTP POST: PENDING OBSERVATION");

            // WebResourceResponseReceived، completionSource همین تلاش را تکمیل می‌کند.
            LiveOrderNetworkObservation observation =
                await WaitForLiveOrderObservationAsync(
                    submissionId,
                    completionSource);

            await TryClearOfficialPreparedStateAsync(
                coreWebView,
                preparationNonce);

            if (!observation.ResponseObserved ||
                !observation.StatusCode.HasValue)
            {
                WriteImportant(
                    "RESULT: RESPONSE NOT OBSERVED");

                return
                    ScheduledOrderAttemptOutcome.AmbiguousFailure;
            }

            int status =
                observation.StatusCode.Value;

            ScheduledOrderAttemptOutcome outcome =
                ClassifyObservedHttpStatus(
                    status);

            if (outcome ==
                ScheduledOrderAttemptOutcome.Succeeded)
            {
                return outcome;
            }

            if (outcome ==
                ScheduledOrderAttemptOutcome.RetryableFailure)
            {
                WriteImportant(
                    "RESULT: DEFINITIVE HTTP FAILURE; RETRY ALLOWED");

                return outcome;
            }

            WriteImportant(
                "RESULT: AMBIGUOUS HTTP FAILURE; RETRY BLOCKED");

            return outcome;
        }

        /// <summary>
        /// پاسخ‌های قابل‌تکرار عمداً Whitelist شده‌اند. سایر وضعیت‌ها مبهم
        /// محسوب می‌شوند و برای جلوگیری از سفارش تکراری چرخه را متوقف می‌کنند.
        /// </summary>
        private static ScheduledOrderAttemptOutcome
            ClassifyObservedHttpStatus(
                int status)
        {
            if (status >= 200 &&
                status <= 299)
            {
                return
                    ScheduledOrderAttemptOutcome.Succeeded;
            }

            if (status is
                400 or
                401 or
                403 or
                404 or
                405 or
                415 or
                422 or
                429)
            {
                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            return
                ScheduledOrderAttemptOutcome.AmbiguousFailure;
        }

        /// <summary>
        /// حداکثر تا مهلت تعیین‌شده منتظر پاسخ متناظر می‌ماند. نبود پاسخ
        /// به معنی شکست قطعی نیست و نتیجه مبهم برگردانده می‌شود.
        /// </summary>
        private async Task<LiveOrderNetworkObservation>
            WaitForLiveOrderObservationAsync(
                string submissionId,
                TaskCompletionSource<LiveOrderNetworkObservation>
                    completionSource)
        {
            Task completedTask =
                await Task.WhenAny(
                    completionSource.Task,
                    Task.Delay(
                        LiveSubmissionResponseTimeout));

            if (completedTask ==
                completionSource.Task)
            {
                return await completionSource.Task;
            }

            bool requestObserved =
                _liveOrderRequestObserved;

            if (_liveSubmissionInProgress &&
                string.Equals(
                    _activeLiveSubmissionId,
                    submissionId,
                    StringComparison.Ordinal))
            {
                ResetLiveSubmissionTracking();
            }

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "CONTROLLED ORDER OBSERVATION TIMEOUT");
            WriteImportant(
                "========================================");
            WriteImportant(
                "REQUEST OBSERVED: " +
                (requestObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "HTTP RESPONSE: NOT OBSERVED WITHIN 30 SECONDS");
            WriteImportant(
                "RESULT: VERIFY MANUALLY IN EASYTRADER");
            WriteImportant(
                "========================================");

            return new LiveOrderNetworkObservation
            {
                RequestObserved =
                    requestObserved,

                ResponseObserved =
                    false
            };
        }

        private void WriteScheduledOrderStopped(
            string reason,
            string result)
        {
            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "SCHEDULED ORDER STOPPED");
            WriteImportant(
                "========================================");
            WriteImportant(
                "RESULT: " +
                result);
            WriteImportant(
                "REASON: " +
                reason);
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "========================================");

            SetStatus(
                reason);
        }

        private void SetScheduledOrderControls(
            bool isActive)
        {
            LoginButton.IsEnabled =
                !isActive;

            PreviewOrderButton.IsEnabled =
                isActive;

            PrepareOrderButton.Content =
                        "آماده‌سازی محلی";
            PrepareOrderButton.IsEnabled =
                true;

            SendLiveOrderButton.IsEnabled =
                true;

            ReloadButton.IsEnabled =
                isActive;

            CancelScheduledOrderButton.IsEnabled =
                isActive;
        }

        /// <summary>
        /// فرم رسمی خرید را پیدا یا باز می‌کند و تا آماده‌شدن آن تلاش می‌کند.
        /// خروجی PREPARED فقط آمادگی محلی فرم را نشان می‌دهد و به معنی ارسال نیست.
        /// </summary>
        private async Task<OfficialOrderUiBridgeResult>
            PrepareOfficialOrderFormAsync(
                CoreWebView2 coreWebView,
                Order order,
                string preparationNonce,
                CancellationToken cancellationToken = default)
        {
            const int maximumAttemptCount =
                20;

            SetStatus(
                "در حال انتخاب نماد و بازکردن فرم رسمی خرید...");

            for (int attempt = 0;
                attempt < maximumAttemptCount;
                attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ابتدا بررسی می‌شود شاید فرم از قبل باز و قابل‌آماده‌سازی باشد.
                string prepareResultJson =
                    await coreWebView.ExecuteScriptAsync(
                        OfficialOrderUiBridge.BuildPrepareScript(
                            order,
                            preparationNonce));

                OfficialOrderUiBridgeResult prepareResult =
                    OfficialOrderUiBridge.ParseResult(
                        prepareResultJson);

                if (prepareResult.HasStatus(
                    OfficialOrderUiBridge.PreparedStatus))
                {
                    return prepareResult;
                }

                if (!prepareResult.HasStatus(
                    "ORDER_DIALOG_NOT_FOUND"))
                {
                    return prepareResult;
                }

                // اگر فرم وجود ندارد، نماد تأییدشده انتخاب و دکمه رسمی خرید باز می‌شود.
                string ensureResultJson =
                    await coreWebView.ExecuteScriptAsync(
                        OfficialOrderUiBridge.BuildEnsureBuyDialogScript(
                            order));

                OfficialOrderUiBridgeResult ensureResult =
                    OfficialOrderUiBridge.ParseResult(
                        ensureResultJson);

                if (ensureResult.HasStatus(
                    OfficialOrderUiBridge.DialogAlreadyOpenStatus))
                {
                    await Task.Delay(
                        100,
                        cancellationToken);

                    continue;
                }

                if (ensureResult.HasStatus(
                    OfficialOrderUiBridge.DialogOpenRequestedStatus))
                {
                    if (ensureResult.ClickX > 0 &&
                        ensureResult.ClickY > 0)
                    {
                        string moveJson = JsonSerializer.Serialize(new
                        {
                            type = "mouseMoved",
                            x = ensureResult.ClickX,
                            y = ensureResult.ClickY,
                            button = "none",
                            clickCount = 0
                        });

                        string downJson = JsonSerializer.Serialize(new
                        {
                            type = "mousePressed",
                            x = ensureResult.ClickX,
                            y = ensureResult.ClickY,
                            button = "left",
                            clickCount = 1
                        });

                        string upJson = JsonSerializer.Serialize(new
                        {
                            type = "mouseReleased",
                            x = ensureResult.ClickX,
                            y = ensureResult.ClickY,
                            button = "left",
                            clickCount = 1
                        });

                        await coreWebView.CallDevToolsProtocolMethodAsync(
                            "Input.dispatchMouseEvent",
                            moveJson);

                        await coreWebView.CallDevToolsProtocolMethodAsync(
                            "Input.dispatchMouseEvent",
                            downJson);

                        await coreWebView.CallDevToolsProtocolMethodAsync(
                            "Input.dispatchMouseEvent",
                            upJson);
                    }

                    await Task.Delay(
                        400,
                        cancellationToken);

                    continue;
                }

                if (ensureResult.HasStatus(
                    OfficialOrderUiBridge.SymbolSelectionRequestedStatus))
                {
                    await Task.Delay(
                        500,
                        cancellationToken);

                    continue;
                }

                return ensureResult;
            }

            return new OfficialOrderUiBridgeResult
            {
                Status =
                    "ORDER_DIALOG_OPEN_TIMEOUT",

                Reason =
                    "Official buy dialog did not open within the allowed attempts."
            };
        }

        private bool TryGetValidatedConfirmedOrder(
            out ConfirmedOrderSnapshot? snapshot,
            out CreateOrderPayload? payload,
            out string errorMessage)
        {
            snapshot =
                _confirmedOrderSnapshot;

            payload =
                null;

            errorMessage =
                "";

            if (snapshot == null)
            {
                errorMessage =
                    "سفارش تأییدشده‌ای در حافظه وجود ندارد.";

                return false;
            }

            if (!snapshot.HasValidFingerprint())
            {
                errorMessage =
                    "اثر انگشت سفارش تأییدشده معتبر نیست.";

                return false;
            }

            TimeSpan confirmedAge =
                DateTimeOffset.UtcNow -
                snapshot.ConfirmedAtUtc;

            if (confirmedAge >
                ConfirmedOrderLifetime)
            {
                errorMessage =
                    "تأیید سفارش منقضی شده است؛ سفارش را دوباره بررسی کنید.";

                return false;
            }

            try
            {
                payload =
                    JsonSerializer.Deserialize<CreateOrderPayload>(
                        snapshot.PayloadJson);
            }
            catch (JsonException)
            {
                errorMessage =
                    "Payload تأییدشده قابل خواندن نیست.";

                return false;
            }

            OrderSubmissionValidationResult validation =
                OrderSubmissionValidator.Validate(
                    payload);

            if (!validation.IsValid)
            {
                errorMessage =
                    validation.ErrorMessage;

                return false;
            }

            return true;
        }

        private async Task TryClearOfficialPreparedStateAsync(
            CoreWebView2 coreWebView,
            string nonce)
        {
            try
            {
                await coreWebView.ExecuteScriptAsync(
                    OfficialOrderUiBridge.BuildClearScript(
                        nonce));
            }
            catch (Exception)
            {
                WriteLog(
                    "Official order form state cleanup was not confirmed.");
            }
        }

        private void WriteLiveSubmissionBlocked(
            string reason)
        {
            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "LIVE ORDER SUBMISSION");
            WriteImportant(
                "========================================");
            WriteImportant(
                "RESULT: BLOCKED");
            WriteImportant(
                "REASON: " + reason);
            WriteImportant(
                "HTTP POST: NOT SENT");
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "========================================");

            SetStatus(
                reason);
        }

        /// <summary>
        /// فقط حضور POST متناظر را علامت می‌زند. به دلیل Service Worker ممکن است
        /// Request دیده نشود؛ بنابراین نبود آن به تنهایی نتیجه سفارش را تعیین نمی‌کند.
        /// </summary>
        private void ObserveLiveOrderRequest(
            string method,
            string url)
        {
            if (!_liveSubmissionInProgress ||
                _liveOrderRequestObserved ||
                !IsCreateOrderRequest(
                    method,
                    url))
            {
                return;
            }

            _liveOrderRequestObserved =
                true;

            WriteImportant("");
            WriteImportant(
                "CONTROLLED ORDER POST OBSERVED");
            WriteImportant(
                "PAYLOAD FINGERPRINT: " +
                (_activeLiveSubmissionFingerprint ??
                    "UNKNOWN"));
            // Header و Body عمداً حذف می‌شوند تا داده احراز هویت یا مالی افشا نشود.
            WriteImportant(
                "REQUEST HEADERS: [OMITTED]");
            WriteImportant(
                "REQUEST BODY: [OMITTED]");
        }

        /// <summary>
        /// پاسخ HTTP مربوط به POST فعال را ثبت و Task منتظر همان تلاش را تکمیل می‌کند.
        /// پاسخ 2xx موفقیت HTTP است؛ نتیجه کسب‌وکاری باید در فهرست سفارش کارگزاری
        /// بررسی شود و از Body برای استخراج داده حساس استفاده نمی‌شود.
        /// </summary>
        private void ObserveLiveOrderResponse(
            string method,
            string url,
            int status)
        {
            if (!_liveSubmissionInProgress ||
                !IsCreateOrderRequest(
                    method,
                    url))
            {
                return;
            }

            string fingerprint =
                _activeLiveSubmissionFingerprint ??
                "UNKNOWN";

            bool requestObserved =
                _liveOrderRequestObserved;

            TaskCompletionSource<LiveOrderNetworkObservation>?
                completionSource =
                    _activeLiveSubmissionCompletion;

            _activeOrderSession?.SetLastHttpStatus(
                status);

            // ابتدا قفل تلاش آزاد می‌شود؛ سپس completionSource محلی نتیجه را
            // به چرخه زمان‌بندی تحویل می‌دهد.
            ResetLiveSubmissionTracking();

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "CONTROLLED ORDER RESPONSE");
            WriteImportant(
                "========================================");
            WriteImportant(
                "OFFICIAL EASYTRADER ACTION: INVOKED ONCE");
            WriteImportant(
                "HTTP STATUS: " + status);
            WriteImportant(
                "REQUEST OBSERVED: " +
                (requestObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "PAYLOAD FINGERPRINT: " +
                fingerprint);
            WriteImportant(
                "RESPONSE HEADERS: [OMITTED]");
            WriteImportant(
                "RESPONSE BODY: [OMITTED]");
            WriteImportant(
                status >= 200 &&
                status <= 299
                    ? "RESULT: HTTP RESPONSE SUCCESSFUL"
                    : "RESULT: HTTP RESPONSE FAILED");
            WriteImportant(
                "BROKER OUTCOME: VERIFY IN EASYTRADER ORDER LIST");
            WriteImportant(
                "========================================");

            SetStatus(
                "پاسخ سفارش مشاهده شد؛ نتیجه نهایی را در فهرست سفارش‌های EasyTrader بررسی کنید.");

            completionSource?.TrySetResult(
                new LiveOrderNetworkObservation
                {
                    StatusCode =
                        status,

                    RequestObserved =
                        requestObserved,

                    ResponseObserved =
                        true
                });
        }

        /// <summary>
        /// فقط وضعیت موقت مشاهده یک تلاش را پاک می‌کند؛ اطلاعات نشست سایت
        /// و هیچ Token یا Cookie در این ساختار نگهداری نمی‌شود.
        /// </summary>
        private void ResetLiveSubmissionTracking()
        {
            _liveSubmissionInProgress =
                false;

            _liveOrderRequestObserved =
                false;

            _activeLiveSubmissionId =
                null;

            _activeLiveSubmissionFingerprint =
                null;

            _activeLiveSubmissionCompletion =
                null;
        }

        private void ClearConfirmedOrder()
        {
            _confirmedOrderSnapshot =
                null;

            PrepareOrderButton.IsEnabled =
                false;

            PrepareOrderButton.Content =
                "آماده‌سازی محلی";

            SendLiveOrderButton.IsEnabled =
                false;

            if (_hasCurrentOrderSetup)
            {
                CurrentSetupStateTextBlock.Text =
                    "مقادیر حفظ شده‌اند؛ برای زمان‌بندی بعدی فرم رسمی را دوباره بخوانید و تأیید کنید.";

                CurrentSetupStateTextBlock.Foreground =
                    System.Windows.Media.Brushes.DarkOrange;
            }
        }

        private void UpdateCurrentOrderSetup(
            Order order)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            _hasCurrentOrderSetup =
                true;

            CurrentSetupSymbolTextBlock.Text =
                order.SymbolName;

            CurrentSetupIsinTextBlock.Text =
                order.SymbolIsin;

            CurrentSetupPriceTextBlock.Text =
                order.Price.ToString(
                    "N0",
                    CultureInfo.InvariantCulture);

            CurrentSetupQuantityTextBlock.Text =
                order.Quantity.ToString(
                    "N0",
                    CultureInfo.InvariantCulture);

            long grossValue =
                checked(
                    order.Price *
                    order.Quantity);

            long commissionAmount =
                checked(
                    order.TotalValue -
                    grossValue);

            CurrentSetupCommissionTextBlock.Text =
                commissionAmount.ToString(
                    "N0",
                    CultureInfo.InvariantCulture);

            CurrentSetupTotalValueTextBlock.Text =
                order.TotalValue.ToString(
                    "N0",
                    CultureInfo.InvariantCulture);

            CurrentSetupStateTextBlock.Text =
                "خوانده و تأیید شده از فرم رسمی EasyTrader";

            CurrentSetupStateTextBlock.Foreground =
                System.Windows.Media.Brushes.Teal;
        }

        private static string SanitizeUrl(
            string url)
        {
            if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? uri))
            {
                return "[INVALID URL]";
            }

            return
                uri.GetLeftPart(
                    UriPartial.Path);
        }

        // =====================================================
        // SAFE WEBVIEW2 TIMING PROBE
        // =====================================================

        /// <summary>
        /// تست فقط زمان‌بندی ExecuteScriptAsync را اندازه‌گیری می‌کند.
        /// هیچ کلیک، تغییر فرم، درخواست شبکه یا دسترسی به اطلاعات احراز هویت ندارد.
        /// </summary>
        private async void WebViewTimingTestButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_webViewTimingTestActive)
            {
                SetStatus(
                    "تست زمان‌بندی WebView2 از قبل در حال اجرا است.");

                return;
            }

            if (_scheduledOrderActive ||
                _liveSubmissionInProgress)
            {
                SetStatus(
                    "در زمان ارسال واقعی سفارش، تست زمان‌بندی اجرا نمی‌شود.");

                return;
            }

            CoreWebView2? coreWebView =
                Browser.CoreWebView2;

            if (!_webViewReady ||
                coreWebView == null)
            {
                SetStatus(
                    "WebView2 هنوز آماده نیست.");

                return;
            }

            const int probeCount =
                10;

            TimeSpan probeInterval =
                TimeSpan.FromSeconds(
                    1);

            _webViewTimingTestActive =
                true;

            WebViewTimingTestButton.IsEnabled =
                false;

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "SAFE WEBVIEW2 TIMING TEST");
            WriteImportant(
                "========================================");
            WriteImportant(
                "PROBES: 10");
            WriteImportant(
                "TARGET INTERVAL: 1000 ms");
            WriteImportant(
                "ORDER CLICK: NO");
            WriteImportant(
                "FORM CHANGE: NO");
            WriteImportant(
                "NETWORK REQUEST CREATED BY TEST: NO");
            WriteImportant(
                "TOKEN/COOKIE ACCESS: NO");
            WriteImportant(
                "========================================");

            try
            {
                DateTimeOffset testStart =
                    DateTimeOffset.Now;

                System.Collections.Generic.List<Task>
                    probeTasks =
                        new System.Collections.Generic.List<Task>();

                for (int index = 0;
                    index < probeCount;
                    index++)
                {
                    DateTimeOffset targetTime =
                        testStart +
                        TimeSpan.FromTicks(
                            probeInterval.Ticks *
                            index);

                    TimeSpan wait =
                        targetTime -
                        DateTimeOffset.Now;

                    if (wait >
                        TimeSpan.Zero)
                    {
                        await Task.Delay(
                            wait);
                    }

                    DateTimeOffset requestTime =
                        DateTimeOffset.Now;

                    Task probeTask =
                        RunWebViewTimingProbeAsync(
                            coreWebView,
                            index + 1,
                            testStart,
                            targetTime,
                            requestTime);

                    probeTasks.Add(
                        probeTask);
                }

                await Task.WhenAll(
                    probeTasks);

                WriteImportant("");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "SAFE WEBVIEW2 TIMING TEST FINISHED");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "No order action was invoked by this test.");
                WriteImportant(
                    "========================================");

                SetStatus(
                    "تست زمان‌بندی تمام شد؛ خروجی Important API را ارسال کنید.");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "WEBVIEW TIMING TEST ERROR: " +
                    ex.Message);

                SetStatus(
                    "تست زمان‌بندی WebView2 با خطا متوقف شد.");
            }
            finally
            {
                _webViewTimingTestActive =
                    false;

                WebViewTimingTestButton.IsEnabled =
                    true;
            }
        }

        private async Task RunWebViewTimingProbeAsync(
            CoreWebView2 coreWebView,
            int probeNumber,
            DateTimeOffset testStart,
            DateTimeOffset targetTime,
            DateTimeOffset requestTime)
        {
            const string harmlessScript =
                "(() => ({ dateNow: Date.now(), " +
                "performanceNow: performance.now(), " +
                "readyState: document.readyState, " +
                "origin: window.location.origin }))()";

            DateTimeOffset callStartedAt =
                DateTimeOffset.Now;

            string resultJson =
                await coreWebView.ExecuteScriptAsync(
                    harmlessScript);

            DateTimeOffset completedAt =
                DateTimeOffset.Now;

            double targetOffsetMs =
                (targetTime - testStart)
                    .TotalMilliseconds;

            double requestOffsetMs =
                (requestTime - testStart)
                    .TotalMilliseconds;

            double callStartOffsetMs =
                (callStartedAt - testStart)
                    .TotalMilliseconds;

            double completedOffsetMs =
                (completedAt - testStart)
                    .TotalMilliseconds;

            double requestJitterMs =
                (requestTime - targetTime)
                    .TotalMilliseconds;

            double executeDurationMs =
                (completedAt - callStartedAt)
                    .TotalMilliseconds;

            WriteImportant("");
            WriteImportant(
                "WEBVIEW TIMING PROBE #" +
                probeNumber);
            WriteImportant(
                "TARGET OFFSET MS: " +
                targetOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "REQUEST OFFSET MS: " +
                requestOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "CALL START OFFSET MS: " +
                callStartOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "COMPLETE OFFSET MS: " +
                completedOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "REQUEST JITTER MS: " +
                requestJitterMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "EXECUTE DURATION MS: " +
                executeDurationMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "JS RESULT: " +
                resultJson);
        }

        // =====================================================
        // EASYTRADER PREPARE-ONLY DRY-RUN TIMING PROBE
        // =====================================================

        /// <summary>
        /// مسیر واقعی پیدا کردن فرم و مقداردهی قیمت/تعداد را اندازه‌گیری می‌کند،
        /// اما BuildSubmitScript یا کلیک «ارسال خرید» را هرگز فراخوانی نمی‌کند.
        /// </summary>
        private async void OrderUiDryRunTimingButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_orderUiDryRunTimingActive)
            {
                SetStatus(
                    "Dry-Run فرم سفارش از قبل در حال اجرا است.");

                return;
            }

            if (_scheduledOrderActive ||
                _liveSubmissionInProgress ||
                _webViewTimingTestActive)
            {
                SetStatus(
                    "در زمان ارسال واقعی یا تست دیگر، Dry-Run اجرا نمی‌شود.");

                return;
            }

            CoreWebView2? coreWebView =
                Browser.CoreWebView2;

            if (!_webViewReady ||
                coreWebView == null)
            {
                SetStatus(
                    "WebView2 هنوز آماده نیست.");

                return;
            }

            if (!Uri.TryCreate(
                coreWebView.Source,
                UriKind.Absolute,
                out Uri? activeUri) ||
                !activeUri.Host.Equals(
                    "d.easytrader.ir",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "برای Dry-Run باید صفحه اصلی EasyTrader فعال باشد.");

                return;
            }

            if (!TryGetValidatedConfirmedOrder(
                out ConfirmedOrderSnapshot? snapshot,
                out CreateOrderPayload? payload,
                out string validationError) ||
                snapshot == null ||
                payload?.Order == null)
            {
                WriteImportant(
                    "DRY-RUN BLOCKED: " +
                    validationError);

                SetStatus(
                    "ابتدا سفارش را فقط به صورت محلی بررسی و تأیید کنید.");

                return;
            }

            Order order =
                payload.Order;

            const int probeCount =
                10;

            TimeSpan probeInterval =
                TimeSpan.FromSeconds(
                    1);

            _orderUiDryRunTimingActive =
                true;

            OrderUiDryRunTimingButton.IsEnabled =
                false;

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "EASYTRADER PREPARE-ONLY DRY-RUN");
            WriteImportant(
                "========================================");
            WriteImportant(
                "PROBES: 10");
            WriteImportant(
                "TARGET INTERVAL: 1000 ms");
            WriteImportant(
                "FORM FIND + VALUE SET: YES");
            WriteImportant(
                "FINAL SUBMIT CLICK: NO");
            WriteImportant(
                "ORDER POST CREATED BY DRY-RUN: NO");
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "========================================");

            string setupNonce =
                Guid.NewGuid()
                    .ToString(
                        "N");

            try
            {
                DateTimeOffset setupStartedAt =
                    DateTimeOffset.Now;

                OfficialOrderUiBridgeResult setupResult =
                    await PrepareOfficialOrderFormAsync(
                        coreWebView,
                        order,
                        setupNonce);

                DateTimeOffset setupCompletedAt =
                    DateTimeOffset.Now;

                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    setupNonce);

                WriteImportant(
                    "DRY-RUN SETUP STATUS: " +
                    setupResult.Status);
                WriteImportant(
                    "DRY-RUN SETUP DURATION MS: " +
                    (setupCompletedAt - setupStartedAt)
                        .TotalMilliseconds
                        .ToString(
                            "F1",
                            System.Globalization.CultureInfo.InvariantCulture));

                if (!setupResult.HasStatus(
                    OfficialOrderUiBridge.PreparedStatus))
                {
                    SetStatus(
                        "فرم رسمی برای Dry-Run آماده نشد.");

                    return;
                }

                DateTimeOffset testStart =
                    DateTimeOffset.Now;

                System.Collections.Generic.List<Task>
                    probeTasks =
                        new System.Collections.Generic.List<Task>();

                for (int index = 0;
                    index < probeCount;
                    index++)
                {
                    DateTimeOffset targetTime =
                        testStart +
                        TimeSpan.FromTicks(
                            probeInterval.Ticks *
                            index);

                    TimeSpan wait =
                        targetTime -
                        DateTimeOffset.Now;

                    if (wait >
                        TimeSpan.Zero)
                    {
                        await Task.Delay(
                            wait);
                    }

                    DateTimeOffset requestTime =
                        DateTimeOffset.Now;

                    string nonce =
                        Guid.NewGuid()
                            .ToString(
                                "N");

                    Task probeTask =
                        RunOrderUiPrepareDryProbeAsync(
                            coreWebView,
                            order,
                            nonce,
                            index + 1,
                            testStart,
                            targetTime,
                            requestTime);

                    probeTasks.Add(
                        probeTask);
                }

                await Task.WhenAll(
                    probeTasks);

                WriteImportant("");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "EASYTRADER PREPARE-ONLY DRY-RUN FINISHED");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "FINAL SUBMIT CLICK: NO");
                WriteImportant(
                    "ORDER POST CREATED BY DRY-RUN: NO");
                WriteImportant(
                    "========================================");

                SetStatus(
                    "Dry-Run تمام شد؛ خروجی Important API را ارسال کنید.");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "ORDER UI DRY-RUN ERROR: " +
                    ex.Message);

                SetStatus(
                    "Dry-Run فرم سفارش با خطا متوقف شد.");
            }
            finally
            {
                await TryClearOfficialPreparedStateAsync(
                    coreWebView,
                    setupNonce);

                _orderUiDryRunTimingActive =
                    false;

                OrderUiDryRunTimingButton.IsEnabled =
                    true;
            }
        }

        private async Task RunOrderUiPrepareDryProbeAsync(
            CoreWebView2 coreWebView,
            Order order,
            string nonce,
            int probeNumber,
            DateTimeOffset testStart,
            DateTimeOffset targetTime,
            DateTimeOffset requestTime)
        {
            DateTimeOffset callStartedAt =
                DateTimeOffset.Now;

            string resultJson =
                await coreWebView.ExecuteScriptAsync(
                    OfficialOrderUiBridge.BuildPrepareScript(
                        order,
                        nonce));

            DateTimeOffset completedAt =
                DateTimeOffset.Now;

            OfficialOrderUiBridgeResult result =
                OfficialOrderUiBridge.ParseResult(
                    resultJson);

            await TryClearOfficialPreparedStateAsync(
                coreWebView,
                nonce);

            double targetOffsetMs =
                (targetTime - testStart)
                    .TotalMilliseconds;

            double requestOffsetMs =
                (requestTime - testStart)
                    .TotalMilliseconds;

            double completeOffsetMs =
                (completedAt - testStart)
                    .TotalMilliseconds;

            double requestJitterMs =
                (requestTime - targetTime)
                    .TotalMilliseconds;

            double executeDurationMs =
                (completedAt - callStartedAt)
                    .TotalMilliseconds;

            WriteImportant("");
            WriteImportant(
                "ORDER UI DRY PROBE #" +
                probeNumber);
            WriteImportant(
                "STATUS: " +
                result.Status);
            WriteImportant(
                "TARGET OFFSET MS: " +
                targetOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "REQUEST OFFSET MS: " +
                requestOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "COMPLETE OFFSET MS: " +
                completeOffsetMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "REQUEST JITTER MS: " +
                requestJitterMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteImportant(
                "EXECUTE DURATION MS: " +
                executeDurationMs.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        // =====================================================
        // LOGIN
        // =====================================================

        private void LoginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_webViewReady ||
                Browser.CoreWebView2 == null)
            {
                WriteLog(
                    "WebView2 هنوز آماده نیست.");

                return;
            }

            Browser.CoreWebView2.Navigate(
                EasyTraderUrl);
        }

        // =====================================================
        // MONITORING BUTTON
        // =====================================================

        private void MonitoringButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            EnableNetworkMonitoring();

            WriteImportant("");

            WriteImportant(
                "========================================");

            WriteImportant(
                "MONITORING READY");

            WriteImportant(
                "دنبال این API ها هستیم:");

            WriteImportant(
                "1. same-login");

            WriteImportant(
                "2. startsession");

            WriteImportant(
                "3. core/api/v2/order");

            WriteImportant(
                "4. HTTP 401 / 403 / 500");

            WriteImportant(
                "========================================");
        }

        // =====================================================
        // CANCEL SCHEDULED ORDER
        // =====================================================

        private void CancelScheduledOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CancellationTokenSource? cancellationSource =
                _scheduledOrderCancellation;

            if (!_scheduledOrderActive ||
                cancellationSource == null)
            {
                SetStatus(
                    "زمان‌بندی فعالی برای لغو وجود ندارد.");

                return;
            }

            CancelScheduledOrderButton.IsEnabled =
                false;

            cancellationSource.Cancel();

            WriteImportant("");
            WriteImportant(
                "SCHEDULE CANCELLATION REQUESTED");
            WriteImportant(
                _liveSubmissionInProgress
                    ? "CURRENT ATTEMPT: WAITING FOR DEFINITIVE RESULT"
                    : "HTTP POST: NOT SENT BY CANCELLATION ACTION");

            SetStatus(
                _liveSubmissionInProgress
                    ? "لغو ثبت شد؛ پس از مشخص‌شدن نتیجه تلاش جاری، عملیات متوقف می‌شود."
                    : "در حال لغو زمان‌بندی سفارش...");
        }

        // =====================================================
        // PAUSE
        // =====================================================

        private void PauseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _pauseLog =
                !_pauseLog;

            if (_pauseLog)
            {
                PauseButton.Content =
                    "Resume Log";

                SetStatus(
                    "Live Log متوقف است؛ Monitoring ادامه دارد.");

                WriteImportant(
                    "[LIVE LOG PAUSED]");
            }
            else
            {
                PauseButton.Content =
                    "Pause Log";

                SetStatus(
                    "Live Log فعال است.");

                WriteImportant(
                    "[LIVE LOG RESUMED]");
            }
        }

        // =====================================================
        // CLEAR LIVE LOG
        // =====================================================

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LogTextBox.Clear();

            WriteImportant(
                "[Live Log پاک شد]");
        }

        // =====================================================
        // CLEAR IMPORTANT
        // =====================================================

        private void ClearImportantButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ImportantApiTextBox.Clear();
        }

        // =====================================================
        // RELOAD
        // =====================================================

        private void ReloadButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_scheduledOrderActive ||
                _liveSubmissionInProgress)
            {
                SetStatus(
                    "تا پایان زمان‌بندی یا مشخص‌شدن نتیجه ارسال، بارگذاری مجدد متوقف است.");

                return;
            }

            if (!_webViewReady ||
                Browser.CoreWebView2 == null)
                return;

            ClearConfirmedOrder();

            Browser.CoreWebView2.Reload();
        }

        // =====================================================
        // NAVIGATION STARTING
        // =====================================================

        private void Browser_NavigationStarting(
            object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            if (_scheduledOrderActive ||
                _liveSubmissionInProgress)
            {
                e.Cancel =
                    true;

                SetStatus(
                    "ناوبری تا پایان زمان‌بندی یا مشخص‌شدن نتیجه ارسال متوقف شد.");

                return;
            }

            if (_confirmedOrderSnapshot != null)
            {
                ClearConfirmedOrder();
            }

            WriteLog("");

            WriteLog(
                "NAVIGATION STARTING:");

            WriteLog(
                SanitizeUrl(
                    e.Uri ?? ""));
        }

        // =====================================================
        // NAVIGATION COMPLETED
        // =====================================================

        private void Browser_NavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            string url =
                Browser.Source?.ToString() ?? "";

            WriteLog(
                "NAVIGATION COMPLETED:");

            WriteLog(
                SanitizeUrl(
                    url));

            if (e.IsSuccess)
            {
                SetStatus(
                    "صفحه بارگذاری شد.");
            }
            else
            {
                WriteLog(
                    "Navigation Error:");

                WriteLog(
                    e.WebErrorStatus.ToString());
            }
        }

        // =====================================================
        // PROCESS FAILED
        // =====================================================

        private void CoreWebView2_ProcessFailed(
            object? sender,
            CoreWebView2ProcessFailedEventArgs e)
                {
                    _scheduledOrderCancellation?.Cancel();

                    WriteImportant(
                        "WebView2 Process Failed:");

                    WriteImportant(
                        "Kind: " +
                        e.ProcessFailedKind.ToString());

                    WriteImportant(
                        "Reason: " +
                        e.Reason.ToString());
                }

        // =====================================================
        // STATUS
        // =====================================================

        private void SetStatus(
            string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        StatusTextBlock.Text =
                            text;
                    });

                return;
            }

            StatusTextBlock.Text =
                text;
        }

        // =====================================================
        // LIVE LOG
        // =====================================================

        private void WriteLog(
            string message)
        {
            if (_pauseLog)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        WriteLog(message);
                    });

                return;
            }

            LogTextBox.AppendText(
                $"[{DateTime.Now:HH:mm:ss}] {message}" +
                Environment.NewLine);

            LogTextBox.ScrollToEnd();
        }

        // =====================================================
        // IMPORTANT LOG
        // =====================================================

        private void WriteImportant(
            string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        WriteImportant(message);
                    });

                return;
            }

            ImportantApiTextBox.AppendText(
                $"[{DateTime.Now:HH:mm:ss}] {message}" +
                Environment.NewLine);

            ImportantApiTextBox.ScrollToEnd();
        }
        // =====================================================
        // SEND REAL ORDER BUTTON
        // =====================================================

        //private async void SendOrderButton_Click(
        //    object sender,
        //    RoutedEventArgs e)
        //{
        //    try
        //    {
        //        // -------------------------------------------------
        //        // Check Access Token
        //        // -------------------------------------------------

        //        if (string.IsNullOrWhiteSpace(_accessToken))
        //        {
        //            WriteImportant(
        //                "========================================");

        //            WriteImportant(
        //                "SEND ORDER ABORTED");

        //            WriteImportant(
        //                "Access Token هنوز دریافت نشده است.");

        //            WriteImportant(
        //                "ابتدا وارد EasyTrader شوید.");

        //            WriteImportant(
        //                "========================================");

        //            return;
        //        }

        //        // -------------------------------------------------
        //        // سفارش واقعی موردنظر
        //        // -------------------------------------------------

        //        const long price = 1236664;
        //        const long quantity = 10;

        //        const int side = 0;

        //        const string symbolIsin =
        //            "IRTKLOTF0001";

        //        // نرخ کمیسیون مشاهده‌شده در سفارش واقعی
        //        const double commissionRate = 0.0012;

        //        // -------------------------------------------------
        //        // Calculate values
        //        // -------------------------------------------------

        //        long grossValue =
        //            price * quantity;

        //        long commissionAmount =
        //            (long)Math.Round(
        //                grossValue * commissionRate);

        //        long totalValue =
        //            grossValue + commissionAmount;

        //        // -------------------------------------------------
        //        // Create Payload
        //        // -------------------------------------------------

        //        CreateOrderPayload payload =
        //            new CreateOrderPayload
        //            {
        //                Order = new Order
        //                {
        //                    Commission = commissionRate,

        //                    CreateDateTime =
        //                        DateTime.Now.ToString(
        //                            "M/d/yyyy, h:mm:ss tt"),

        //                    OrderFrom = 34,

        //                    OrderModelType = 0,

        //                    Price = price,

        //                    Quantity = quantity,

        //                    Side = side,

        //                    SymbolIsin = symbolIsin,

        //                    SymbolName = "",

        //                    TotalValue = totalValue,

        //                    ValidityType = 0
        //                }
        //            };

        //        // -------------------------------------------------
        //        // Log
        //        // -------------------------------------------------

        //        WriteImportant("");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(
        //            "REAL ORDER");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(
        //            "Symbol ISIN: " +
        //            symbolIsin);

        //        WriteImportant(
        //            "Price: " +
        //            price.ToString("N0"));

        //        WriteImportant(
        //            "Quantity: " +
        //            quantity.ToString("N0"));

        //        WriteImportant(
        //            "Side: " +
        //            side);

        //        WriteImportant(
        //            "Gross Value: " +
        //            grossValue.ToString("N0"));

        //        WriteImportant(
        //            "Commission Rate: " +
        //            commissionRate);

        //        WriteImportant(
        //            "Commission Amount: " +
        //            commissionAmount.ToString("N0"));

        //        WriteImportant(
        //            "Total Value: " +
        //            totalValue.ToString("N0"));

        //        WriteImportant(
        //            "========================================");

        //        // -------------------------------------------------
        //        // Disable button during request
        //        // -------------------------------------------------

        //        SendOrderButton.IsEnabled = false;

        //        SendOrderButton.Content =
        //            "در حال ارسال...";

        //        // -------------------------------------------------
        //        // SEND
        //        // -------------------------------------------------

        //        string result =
        //            await SendOrderAsync(payload);

        //        // -------------------------------------------------
        //        // RESULT
        //        // -------------------------------------------------

        //        WriteImportant("");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(
        //            "SEND ORDER RESULT");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(result);

        //        WriteImportant(
        //            "========================================");
        //    }
        //    catch (Exception ex)
        //    {
        //        WriteImportant("");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(
        //            "SEND ORDER BUTTON ERROR");

        //        WriteImportant(
        //            "========================================");

        //        WriteImportant(
        //            ex.ToString());
        //    }
        //    finally
        //    {
        //        SendOrderButton.IsEnabled = true;

        //        SendOrderButton.Content =
        //            "ارسال سفارش تست";
        //    }
        //}

        private void LoginStatusButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "LOGIN STATUS");
            WriteImportant(
                "========================================");
            WriteImportant(
                "Authorization header presence observed: " +
                (_authorizationHeaderObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "Site session response observed: " +
                (_successfulSessionResponseObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "Protected order API success observed: " +
                (_successfulProtectedApiResponseObserved
                    ? "YES"
                    : "NO"));
            WriteImportant(
                "Direct API credentials: NOT ACCESSED");
            WriteImportant(
                "هیچ Token، Cookie، Header value یا Body خوانده یا نمایش داده نشد.");
            WriteImportant(
                "========================================");
        }
        // =====================================================
        // CLOSING
        // =====================================================

        private void MainWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            SaveWindowLayout();

            try
            {
                _scheduledOrderCancellation?.Cancel();

                if (Browser?.CoreWebView2 != null)
                {
                    Browser.CoreWebView2
                        .WebResourceRequested -=
                        CoreWebView2_WebResourceRequested;

                    Browser.CoreWebView2
                        .WebResourceResponseReceived -=
                        CoreWebView2_WebResourceResponseReceived;
                }
            }
            catch
            {
            }
        }
    }
}

