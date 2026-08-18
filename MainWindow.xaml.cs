
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FastOrder
{
    public partial class MainWindow : Window
    {
        private const string EasyTraderUrl =
            "https://d.easytrader.ir/";

        private const string ApiHost =
            "api-mts.orbis.easytrader.ir";

        private bool _webViewReady = false;

        private bool _monitoringEnabled = false;

        // فقط نمایش Live Log متوقف می‌شود.
        // Monitoring شبکه همچنان ادامه دارد.
        private bool _pauseLog = false;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
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

                _webViewReady = true;

                Browser.CoreWebView2.Settings.AreDevToolsEnabled =
                    true;

                Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled =
                    true;

                Browser.CoreWebView2.Settings.IsStatusBarEnabled =
                    true;

                Browser.CoreWebView2.ProcessFailed -=
                    CoreWebView2_ProcessFailed;

                Browser.CoreWebView2.ProcessFailed +=
                    CoreWebView2_ProcessFailed;

                EnableNetworkMonitoring();

                WriteLog(
                    "Monitoring قبل از Navigate فعال شد.");

                SetStatus(
                    "در حال ورود به EasyTrader...");

                Browser.CoreWebView2.Navigate(
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
                if (e?.Request == null)
                    return;

                string url =
                    e.Request.Uri ?? "";

                if (!url.Contains(
                    ApiHost,
                    StringComparison.OrdinalIgnoreCase))
                    return;

                string method =
                    e.Request.Method ?? "";

                bool important =
                    IsImportantRequest(url);

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
                        "URL: " + url);

                    WriteImportant(
                        "TIME: " +
                        DateTime.Now.ToString(
                            "HH:mm:ss"));

                    WriteImportant(
                        "REQUEST HEADERS:");

                    foreach (var header in e.Request.Headers)
                    {
                        string name =
                            header.Key ?? "";

                        string value =
                            header.Value ?? "";

                        value =
                            SanitizeHeader(
                                name,
                                value);

                        WriteImportant(
                            name + ": " + value);
                    }

                    if (e.Request.Content != null)
                    {
                        string body =
                            ReadRequestStream(
                                e.Request.Content);

                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            WriteImportant(
                                "REQUEST BODY:");

                            WriteImportant(
                                Sanitize(body));
                        }
                    }
                }

                // -------------------------------------------------
                // Live Log
                // -------------------------------------------------

                WriteLog("");

                WriteLog(
                    ">>> API REQUEST");

                WriteLog(
                    method + " " + url);

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

        private async void CoreWebView2_WebResourceResponseReceived(
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

                if (!url.Contains(
                    ApiHost,
                    StringComparison.OrdinalIgnoreCase))
                    return;

                int status =
                    e.Response.StatusCode;

                string method =
                    e.Request.Method ?? "";

                bool important =
                    IsImportantRequest(url) ||
                    status == 401 ||
                    status == 403 ||
                    status == 500;

                // -------------------------------------------------
                // Live Log
                // -------------------------------------------------

                WriteLog(
                    "<<< API RESPONSE");

                WriteLog(
                    method +
                    " " +
                    url);

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
                        "URL: " + url);

                    WriteImportant(
                        "STATUS: " + status);

                    WriteImportant(
                        "REASON: " +
                        e.Response.ReasonPhrase);

                    WriteImportant(
                        "RESPONSE HEADERS:");

                    foreach (var header in e.Response.Headers)
                    {
                        string name =
                            header.Key ?? "";

                        string value =
                            header.Value ?? "";

                        value =
                            SanitizeHeader(
                                name,
                                value);

                        WriteImportant(
                            name + ": " + value);
                    }

                    try
                    {
                        Stream stream =
                            await e.Response.GetContentAsync();

                        if (stream != null)
                        {
                            string body =
                                await ReadResponseStream(
                                    stream);

                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                WriteImportant(
                                    "RESPONSE BODY:");

                                WriteImportant(
                                    Sanitize(body));
                            }
                        }
                    }
                    catch (Exception bodyEx)
                    {
                        WriteImportant(
                            "Response Body قابل خواندن نبود:");

                        WriteImportant(
                            bodyEx.Message);
                    }

                    WriteImportant(
                        "========================================");
                }
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

        // =====================================================
        // SANITIZE HEADER
        // =====================================================

        private string SanitizeHeader(
            string name,
            string value)
        {
            if (name.Equals(
                "authorization",
                StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED]";
            }

            if (name.Equals(
                "cookie",
                StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED]";
            }

            if (name.Equals(
                "set-cookie",
                StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED]";
            }

            return value;
        }

        // =====================================================
        // SANITIZE BODY
        // =====================================================

        private string Sanitize(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string result =
                text;

            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"(?i)(Bearer\s+)[A-Za-z0-9\-_\.]+",
                    "$1[REDACTED]");

            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"(?i)(""access_token""\s*:\s*"")[^""]+",
                    "$1[REDACTED]");

            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"(?i)(""id_token""\s*:\s*"")[^""]+",
                    "$1[REDACTED]");

            return result;
        }

        // =====================================================
        // REQUEST BODY
        // =====================================================

        private string ReadRequestStream(
            Stream stream)
        {
            try
            {
                if (stream == null)
                    return "";

                if (stream.CanSeek)
                    stream.Position = 0;

                using StreamReader reader =
                    new StreamReader(
                        stream,
                        Encoding.UTF8,
                        true,
                        4096,
                        true);

                string result =
                    reader.ReadToEnd();

                if (stream.CanSeek)
                    stream.Position = 0;

                return result;
            }
            catch
            {
                return "";
            }
        }

        // =====================================================
        // RESPONSE BODY
        // =====================================================

        private async Task<string> ReadResponseStream(
            Stream stream)
        {
            if (stream == null)
                return "";

            using StreamReader reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8);

            return await reader.ReadToEndAsync();
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
            if (!_webViewReady ||
                Browser.CoreWebView2 == null)
                return;

            Browser.CoreWebView2.Reload();
        }

        // =====================================================
        // NAVIGATION STARTING
        // =====================================================

        private void Browser_NavigationStarting(
            object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            WriteLog("");

            WriteLog(
                "NAVIGATION STARTING:");

            WriteLog(
                e.Uri);
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
                url);

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

