
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.Text.Json;
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

        private bool _webViewReady = false;

        private bool _monitoringEnabled = false;

        // فقط نمایش Live Log متوقف می‌شود.
        // Monitoring شبکه همچنان ادامه دارد.
        private bool _pauseLog = false;
        private bool _authorizationHeaderObserved = false;
        private bool _successfulSessionResponseObserved = false;
        private bool _successfulProtectedApiResponseObserved = false;
        private ConfirmedOrderSnapshot? _confirmedOrderSnapshot;
        private bool _liveSubmissionInProgress = false;
        private bool _liveOrderRequestObserved = false;
        private string? _activeLiveSubmissionId;
        private string? _activeLiveSubmissionFingerprint;
        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void PreviewOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (_liveSubmissionInProgress)
                {
                    SetStatus(
                        "نتیجه ارسال قبلی هنوز در حال بررسی است.");

                    return;
                }

                ClearConfirmedOrder();

                OrderEntryWindow entryWindow =
                    new OrderEntryWindow
                    {
                        Owner = this
                    };

                if (entryWindow.ShowDialog() !=
                    true)
                {
                    SetStatus(
                        "ورود اطلاعات سفارش لغو شد؛ ارسال انجام نشد.");

                    return;
                }

                CreateOrderPayload payload =
                    entryWindow.Payload
                    ?? throw new InvalidOperationException(
                        "Validated order payload was not created.");

                OrderCalculationResult calculation =
                    entryWindow.Calculation
                    ?? throw new InvalidOperationException(
                        "Validated order calculation was not created.");

                string json =
                    JsonSerializer.Serialize(
                        payload,
                        new JsonSerializerOptions
                        {
                            Encoder =
                                System.Text.Encodings.Web
                                    .JavaScriptEncoder
                                    .UnsafeRelaxedJsonEscaping,

                            WriteIndented = true
                        });

                OrderConfirmationWindow confirmationWindow =
                    new OrderConfirmationWindow(
                        payload,
                        calculation,
                        json)
                    {
                        Owner = this
                    };

                bool confirmed =
                    confirmationWindow.ShowDialog() ==
                    true;

                if (confirmed)
                {
                    _confirmedOrderSnapshot =
                        ConfirmedOrderSnapshot.Create(
                            json);

                    PrepareOrderButton.IsEnabled =
                        true;

                    PrepareOrderButton.Content =
                        "آماده‌سازی محلی";
                }
                else
                {
                    ClearConfirmedOrder();
                }

                WriteImportant("");

                WriteImportant(
                    "========================================");

                WriteImportant(
                    "LOCAL ORDER CONFIRMATION");

                WriteImportant(
                    "========================================");

                WriteImportant(
                    "ارسال HTTP انجام نشد.");

                WriteImportant(
                    "RESULT: " +
                    (confirmed
                        ? "CONFIRMED"
                        : "CANCELED"));

                WriteImportant(
                    "========================================");

                SetStatus(
                    confirmed
                        ? "اطلاعات سفارش محلی تأیید شد؛ ارسال انجام نشد."
                        : "تأیید سفارش لغو شد؛ ارسال انجام نشد.");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "Payload Preview Error:");

                WriteImportant(
                    ex.ToString());
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

        private async void SendLiveOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_liveSubmissionInProgress)
            {
                SetStatus(
                    "یک ارسال واقعی در حال بررسی است.");

                return;
            }

            SendLiveOrderButton.IsEnabled =
                true;

            string? preparationNonce =
                null;

            try
            {
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

                preparationNonce =
                    Guid.NewGuid()
                        .ToString(
                            "N");

                OfficialOrderUiBridgeResult prepareResult =
                    await PrepareOfficialOrderFormAsync(
                        coreWebView,
                        order,
                        preparationNonce);

                if (!prepareResult.HasStatus(
                    OfficialOrderUiBridge.PreparedStatus))
                {
                    WriteLiveSubmissionBlocked(
                        OfficialOrderUiBridge.GetUserMessage(
                            prepareResult.Status));

                    await TryClearOfficialPreparedStateAsync(
                        coreWebView,
                        preparationNonce);

                    SendLiveOrderButton.IsEnabled =
                        true;

                    return;
                }

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
                    await TryClearOfficialPreparedStateAsync(
                        coreWebView,
                        preparationNonce);

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

                if (!ReferenceEquals(
                    _confirmedOrderSnapshot,
                    snapshot) ||
                    !snapshot.HasValidFingerprint())
                {
                    await TryClearOfficialPreparedStateAsync(
                        coreWebView,
                        preparationNonce);

                    WriteLiveSubmissionBlocked(
                        "سفارش تأییدشده پیش از ارسال تغییر کرده است.");

                    ClearConfirmedOrder();

                    return;
                }

                string submissionId =
                    Guid.NewGuid()
                        .ToString(
                            "N");

                _liveSubmissionInProgress =
                    true;

                _liveOrderRequestObserved =
                    false;

                _activeLiveSubmissionId =
                    submissionId;

                _activeLiveSubmissionFingerprint =
                    snapshot.ShortFingerprint;

                string submitResultJson =
                    await coreWebView.ExecuteScriptAsync(
                        OfficialOrderUiBridge.BuildSubmitScript(
                            order,
                            preparationNonce));

                OfficialOrderUiBridgeResult submitResult =
                    OfficialOrderUiBridge.ParseResult(
                        submitResultJson);

                if (!submitResult.HasStatus(
                    OfficialOrderUiBridge.ClickedStatus))
                {
                    ResetLiveSubmissionTracking();

                    await TryClearOfficialPreparedStateAsync(
                        coreWebView,
                        preparationNonce);

                    WriteLiveSubmissionBlocked(
                        OfficialOrderUiBridge.GetUserMessage(
                            submitResult.Status));

                    SendLiveOrderButton.IsEnabled =
                        true;

                    return;
                }

                ClearConfirmedOrder();

                bool responseStillPending =
                    _liveSubmissionInProgress &&
                    string.Equals(
                        _activeLiveSubmissionId,
                        submissionId,
                        StringComparison.Ordinal);

                WriteImportant("");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "LIVE ORDER SUBMISSION");
                WriteImportant(
                    "========================================");
                WriteImportant(
                    "OFFICIAL EASYTRADER ACTION: INVOKED ONCE");
                WriteImportant(
                    "PAYLOAD FINGERPRINT: " +
                    snapshot.ShortFingerprint);
                WriteImportant(
                    "DIRECT API CREDENTIALS: NOT ACCESSED");
                WriteImportant(
                    responseStillPending
                        ? "HTTP POST: PENDING OBSERVATION"
                        : "HTTP RESPONSE: ALREADY OBSERVED");
                WriteImportant(
                    "========================================");

                SetStatus(
                    responseStillPending
                        ? "دکمه رسمی ارسال یک‌بار فعال شد؛ در انتظار پاسخ شبکه."
                        : "پاسخ شبکه مشاهده شد؛ فهرست سفارش‌های EasyTrader را بررسی کنید.");

                if (responseStillPending)
                {
                    _ = WatchLiveSubmissionTimeoutAsync(
                        submissionId);
                }
            }
            catch (Exception)
            {
                ResetLiveSubmissionTracking();

                if (preparationNonce != null &&
                    Browser.CoreWebView2 != null)
                {
                    await TryClearOfficialPreparedStateAsync(
                        Browser.CoreWebView2,
                        preparationNonce);
                }

                WriteLiveSubmissionBlocked(
                    "خطای داخلی در مسیر کنترل‌شده رخ داد.");

                if (_confirmedOrderSnapshot != null)
                {
                    SendLiveOrderButton.IsEnabled =
                        true;
                }
            }
        }

        private async Task<OfficialOrderUiBridgeResult>
            PrepareOfficialOrderFormAsync(
                CoreWebView2 coreWebView,
                Order order,
                string preparationNonce)
        {
            const int maximumAttemptCount =
                12;

            SetStatus(
                "در حال انتخاب نماد و بازکردن فرم رسمی خرید...");

            for (int attempt = 0;
                attempt < maximumAttemptCount;
                attempt++)
            {
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
                        100);

                    continue;
                }

                if (ensureResult.HasStatus(
                    OfficialOrderUiBridge.DialogOpenRequestedStatus))
                {
                    await Task.Delay(
                        250);

                    continue;
                }

                if (ensureResult.HasStatus(
                    OfficialOrderUiBridge.SymbolSelectionRequestedStatus))
                {
                    await Task.Delay(
                        500);

                    continue;
                }

                return ensureResult;
            }

            WriteImportant(
                "RESULT: AMBIGUOUS HTTP FAILURE; RETRY BLOCKED");

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
            WriteImportant(
                "REQUEST HEADERS: [OMITTED]");
            WriteImportant(
                "REQUEST BODY: [OMITTED]");
        }

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
        }

        private async Task WatchLiveSubmissionTimeoutAsync(
            string submissionId)
        {
            await Task.Delay(
                LiveSubmissionResponseTimeout);

            if (!_liveSubmissionInProgress ||
                !string.Equals(
                    _activeLiveSubmissionId,
                    submissionId,
                    StringComparison.Ordinal))
            {
                return;
            }

            string fingerprint =
                _activeLiveSubmissionFingerprint ??
                "UNKNOWN";

            bool requestObserved =
                _liveOrderRequestObserved;

            ResetLiveSubmissionTracking();

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
                "PAYLOAD FINGERPRINT: " +
                fingerprint);
            WriteImportant(
                "HTTP RESPONSE: NOT OBSERVED WITHIN 30 SECONDS");
            WriteImportant(
                "RESULT: VERIFY MANUALLY IN EASYTRADER");
            WriteImportant(
                "========================================");

            SetStatus(
                "پاسخ سفارش در مهلت مقرر مشاهده نشد؛ فهرست سفارش‌ها را دستی بررسی کنید.");
        }

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
            if (_liveSubmissionInProgress)
            {
                SetStatus(
                    "تا مشخص‌شدن نتیجه ارسال، بارگذاری مجدد متوقف است.");

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
            if (_liveSubmissionInProgress)
            {
                e.Cancel =
                    true;

                SetStatus(
                    "ناوبری تا مشخص‌شدن نتیجه ارسال متوقف شد.");

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
            try
            {
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

