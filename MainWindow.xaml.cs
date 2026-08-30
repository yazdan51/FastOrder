
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
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
                5);

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
        private TaskCompletionSource<LiveOrderNetworkObservation>?
            _activeLiveSubmissionCompletion;
        private CancellationTokenSource? _scheduledOrderCancellation;
        private bool _scheduledOrderActive = false;

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

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void PreviewOrderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (_scheduledOrderActive ||
                    _liveSubmissionInProgress)
                {
                    SetStatus(
                        "زمان‌بندی یا نتیجه ارسال قبلی هنوز فعال است.");

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

                // از این نقطه به بعد، چرخه زمان‌بندی مالک کامل وضعیت ارسال است.
                await RunScheduledOrderAsync(
                    coreWebView,
                    snapshot,
                    order,
                    confirmationWindow.ScheduledStartAt,
                    confirmationWindow.ScheduledEndAt);
            }
            catch (Exception)
            {
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
        /// چرخه عمر زمان‌بندی را مدیریت می‌کند: انتظار تا شروع، اجرای تلاش‌ها،
        /// توقف با اولین پاسخ موفق، توقف Fail-Closed در نتیجه مبهم و پاک‌سازی.
        /// </summary>
        /// <remarks>
        /// فعال‌سازی عملی این قابلیت باید فقط در بازه و سازوکار مجاز کارگزاری
        /// انجام شود. این متد مجازبودن زمان انتخاب‌شده را از مقررات استنتاج نمی‌کند.
        /// </remarks>
        private async Task RunScheduledOrderAsync(
            CoreWebView2 coreWebView,
            ConfirmedOrderSnapshot snapshot,
            Order order,
            DateTimeOffset startAt,
            DateTimeOffset endAt)
        {
            if (endAt <= startAt)
            {
                WriteLiveSubmissionBlocked(
                    "بازه زمانی ارسال معتبر نیست.");

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

            WriteImportant("");
            WriteImportant(
                "========================================");
            WriteImportant(
                "SCHEDULED ORDER ARMED");
            WriteImportant(
                "========================================");
            WriteImportant(
                "START: " +
                startAt.ToString(
                    "yyyy-MM-dd HH:mm:ss zzz"));
            WriteImportant(
                "END: " +
                endAt.ToString(
                    "yyyy-MM-dd HH:mm:ss zzz"));
            WriteImportant(
                "RETRY DELAY: " +
                ScheduledOrderRetryDelay.TotalSeconds +
                " SECONDS");
            WriteImportant(
                "PAYLOAD FINGERPRINT: " +
                snapshot.ShortFingerprint);
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");
            WriteImportant(
                "HTTP POST: NOT SENT YET");
            WriteImportant(
                "========================================");

            try
            {
                // تا ساعت تعیین‌شده هیچ فرم یا POST سفارش ایجاد نمی‌شود.
                TimeSpan waitUntilStart =
                    startAt -
                    DateTimeOffset.Now;

                if (waitUntilStart >
                    TimeSpan.Zero)
                {
                    SetStatus(
                        "زمان‌بندی فعال است؛ در انتظار ساعت شروع.");

                    await Task.Delay(
                        waitUntilStart,
                        cancellationSource.Token);
                }

                int attemptNumber =
                    0;

                // هر دور دقیقاً یک تلاش مستقل است و فقط خطای قطعی اجازه دور بعد را می‌دهد.
                while (DateTimeOffset.Now <
                    endAt)
                {
                    cancellationSource.Token
                        .ThrowIfCancellationRequested();

                    if (!ReferenceEquals(
                        _confirmedOrderSnapshot,
                        snapshot) ||
                        !snapshot.HasValidFingerprint())
                    {
                        WriteScheduledOrderStopped(
                            "سفارش تأییدشده تغییر کرده است.",
                            "STOPPED BEFORE POST");

                        return;
                    }

                    attemptNumber++;

                    WriteImportant("");
                    WriteImportant(
                        "SCHEDULED ORDER ATTEMPT: " +
                        attemptNumber);
                    WriteImportant(
                        "TIME: " +
                        DateTimeOffset.Now.ToString(
                            "HH:mm:ss zzz"));

                    ScheduledOrderAttemptOutcome outcome =
                        await ExecuteScheduledOrderAttemptAsync(
                            coreWebView,
                            snapshot,
                            order,
                            endAt,
                            cancellationSource.Token);

                    // اولین پاسخ HTTP موفق، چرخه را بدون تلاش اضافه متوقف می‌کند.
                    if (outcome ==
                        ScheduledOrderAttemptOutcome.Succeeded)
                    {
                        WriteImportant("");
                        WriteImportant(
                            "========================================");
                        WriteImportant(
                            "SCHEDULED ORDER COMPLETED");
                        WriteImportant(
                            "========================================");
                        WriteImportant(
                            "RESULT: FIRST HTTP SUCCESS OBSERVED");
                        WriteImportant(
                            "ATTEMPTS: " +
                            attemptNumber);
                        WriteImportant(
                            "RETRY LOOP: STOPPED");
                        WriteImportant(
                            "BROKER OUTCOME: VERIFY IN EASYTRADER ORDER LIST");
                        WriteImportant(
                            "========================================");

                        SetStatus(
                            "اولین پاسخ موفق سفارش مشاهده شد؛ تلاش‌های بعدی متوقف شدند.");

                        return;
                    }

                    // Timeout، پاسخ‌های مبهم و خطاهای دارای احتمال ثبت سفارش
                    // هرگز خودکار تکرار نمی‌شوند تا سفارش تکراری ساخته نشود.
                    if (outcome ==
                        ScheduledOrderAttemptOutcome.AmbiguousFailure)
                    {
                        WriteScheduledOrderStopped(
                            "نتیجه تلاش آخر قطعی نیست؛ برای جلوگیری از سفارش تکراری ادامه متوقف شد.",
                            "VERIFY MANUALLY IN EASYTRADER");

                        return;
                    }

                    cancellationSource.Token
                        .ThrowIfCancellationRequested();

                    TimeSpan remaining =
                        endAt -
                        DateTimeOffset.Now;

                    if (remaining <=
                        TimeSpan.Zero)
                    {
                        break;
                    }

                    TimeSpan retryDelay =
                        remaining < ScheduledOrderRetryDelay
                            ? remaining
                            : ScheduledOrderRetryDelay;

                    SetStatus(
                        "تلاش قبلی خطای قطعی داشت؛ تلاش بعدی پس از وقفه انجام می‌شود.");

                    await Task.Delay(
                        retryDelay,
                        cancellationSource.Token);
                }

                WriteScheduledOrderStopped(
                    "بازه زمانی بدون دریافت پاسخ موفق پایان یافت.",
                    "WINDOW ENDED WITHOUT SUCCESS");
            }
            catch (OperationCanceledException)
            {
                WriteScheduledOrderStopped(
                    "زمان‌بندی توسط کاربر لغو شد.",
                    "CANCELED BY USER");
            }
            catch (Exception)
            {
                WriteScheduledOrderStopped(
                    "خطای داخلی رخ داد و عملیات برای جلوگیری از ارسال تکراری متوقف شد.",
                    "STOPPED ON INTERNAL ERROR");
            }
            finally
            {
                // این پاک‌سازی در تمام مسیرهای موفق، خطا، لغو و انقضای بازه اجرا می‌شود.
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

                ClearConfirmedOrder();

                SetScheduledOrderControls(
                    false);
            }
        }

        /// <summary>
        /// یک تلاش کامل را انجام می‌دهد: آماده‌سازی فرم رسمی، کنترل نهایی
        /// Snapshot، یک کلیک رسمی و انتظار برای پاسخ شبکه مرتبط.
        /// </summary>
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

