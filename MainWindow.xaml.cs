
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FastOrder
{
    public partial class MainWindow : Window
    {
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

        private static readonly TimeSpan ExchangeClockRefreshInterval =
            TimeSpan.FromSeconds(
                3);

        private const int DefaultScheduledSlicePriority =
            0;

        private bool _webViewReady = false;

        private bool _monitoringEnabled = false;

        // فقط نمایش Live Log متوقف می‌شود.
        // Monitoring شبکه همچنان ادامه دارد.
        private bool _pauseLog = false;
        private bool _authorizationHeaderObserved = false;
        private bool _successfulSessionResponseObserved = false;
        private bool _successfulProtectedApiResponseObserved = false;
        private ConfirmedOrderSnapshot? _currentOrderSnapshot;
        private readonly ObservableCollection<OrderSession> _orderSessions =
            new ObservableCollection<OrderSession>();
        private readonly ObservableCollection<ScheduledClickSession>
            _scheduledClickSessions =
                new ObservableCollection<ScheduledClickSession>();
        private long _nextScheduledClickSessionSequence = 0;
        private readonly object _sessionExecutionsSyncRoot =
            new object();
        private readonly Dictionary<Guid, OrderSessionExecution>
            _activeSessionExecutions =
                new Dictionary<Guid, OrderSessionExecution>();
        private readonly Dictionary<Guid, ScheduledClickExecution>
            _activeScheduledClickExecutions =
                new Dictionary<Guid, ScheduledClickExecution>();
        private bool _sessionCreationInProgress = false;
        private readonly object _scheduledClockRefreshSyncRoot =
            new object();
        private CancellationTokenSource? _scheduledClockRefreshCancellation;
        private Task? _scheduledClockRefreshTask;
        private bool _hasCurrentOrderSetup = false;
        private bool _liveSubmissionInProgress = false;
        private bool _liveOrderRequestObserved = false;
        private string? _activeLiveSubmissionId;
        private string? _activeLiveSubmissionFingerprint;
        private TaskCompletionSource<LiveOrderNetworkObservation>?
            _activeLiveSubmissionCompletion;
        private bool _scheduledOrderActive = false;

        private readonly ExchangeClock _exchangeClock =
            new ExchangeClock();

        private readonly OfficialOrderUiDispatcher _officialUiDispatcher =
            new OfficialOrderUiDispatcher();

        private readonly GlobalNextDueQueue _globalNextDueQueue =
            new GlobalNextDueQueue();

        private readonly CancellationTokenSource _applicationCancellation =
            new CancellationTokenSource();

        private readonly DispatcherTimer _exchangeClockDisplayTimer;

        private Task? _exchangeClockMaintenanceTask;

        private WindowState _lastNonMinimizedWindowState =
            WindowState.Normal;

        private bool _webViewTimingTestActive = false;

        private bool _orderUiDryRunTimingActive = false;

        private BrokerProfile _selectedBroker =
            BrokerProfiles.EasyTrader;

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

            if (App.HasExplicitInstance)
            {
                Title =
                    "FastOrder - Instance " +
                    App.InstanceId;
            }

            _selectedBroker =
                BrokerProfiles.ResolveOrDefault(
                    Properties.Settings.Default.SelectedBrokerId);

            BrokerSelectionComboBox.ItemsSource =
                BrokerProfiles.All;

            BrokerSelectionComboBox.SelectedItem =
                _selectedBroker;

            UpdateBrokerPresentation();

            _exchangeClockDisplayTimer =
                new DispatcherTimer(
                    DispatcherPriority.Background)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            250)
                };

            _exchangeClockDisplayTimer.Tick +=
                ExchangeClockDisplayTimer_Tick;

            SessionDataGrid.ItemsSource =
                _scheduledClickSessions;

            _officialUiDispatcher.StateChanged +=
                OfficialUiDispatcher_StateChanged;

            RestoreWindowLayout();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private void BrokerSelectionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (BrokerSelectionComboBox.SelectedItem is not BrokerProfile selectedBroker)
            {
                return;
            }

            if (_scheduledOrderActive ||
                _liveSubmissionInProgress)
            {
                BrokerSelectionComboBox.SelectedItem =
                    _selectedBroker;

                SetStatus(
                    "تغییر کارگزاری هنگام زمان‌بندی یا ارسال فعال مجاز نیست.");

                return;
            }

            if (string.Equals(
                selectedBroker.Id,
                _selectedBroker.Id,
                StringComparison.Ordinal))
            {
                UpdateBrokerPresentation();
                return;
            }

            ClearCurrentOrderConfirmation();
            ResetCurrentOrderSetupForBrokerSwitch();
            ResetBrokerSessionEvidence();

            _selectedBroker =
                selectedBroker;

            Properties.Settings.Default.SelectedBrokerId =
                _selectedBroker.Id;

            UpdateBrokerPresentation();

            WriteImportant("");
            WriteImportant(
                "BROKER SELECTED: " +
                _selectedBroker.DisplayName);
            WriteImportant(
                "TRUSTED ORIGINS: " +
                string.Join(
                    ", ",
                    _selectedBroker.TrustedOrigins));
            WriteImportant(
                "DIRECT API CREDENTIALS: NOT ACCESSED");

            if (_webViewReady &&
                Browser.CoreWebView2 != null)
            {
                Browser.CoreWebView2.Navigate(
                    _selectedBroker.TradingUrl);
            }
        }

        private void UpdateBrokerPresentation()
        {
            string brokerName =
                _selectedBroker.DisplayName;

            BrokerRouteTextBlock.Text =
                "مسیر رسمی " +
                brokerName;

            BrokerWebViewTitleTextBlock.Text =
                brokerName;

            LoginButton.Content =
                "ورود به " +
                brokerName;

            PreviewOrderButton.ToolTip =
                "فرم رسمی خرید نماد جاری " +
                brokerName +
                " را باز می‌کند؛ ارسال انجام نمی‌شود.";

            bool orderUiAvailable =
                _selectedBroker.SupportsOfficialOrderUiAutomation;

            PreviewOrderButton.IsEnabled =
                orderUiAvailable &&
                !_sessionCreationInProgress &&
                !_liveSubmissionInProgress;

            OrderUiDryRunTimingButton.IsEnabled =
                orderUiAvailable &&
                !_scheduledOrderActive &&
                !_liveSubmissionInProgress &&
                _currentOrderSnapshot != null;

        }

        private bool EnsureSelectedBrokerOrderUiAvailable(
            string operationName)
        {
            if (_selectedBroker.SupportsOfficialOrderUiAutomation)
            {
                return true;
            }

            string reason =
                "مسیر رسمی فرم سفارش " +
                _selectedBroker.DisplayName +
                " هنوز با DOM ورودکرده این کارگزاری تطبیق و تأیید نشده است.";

            WriteImportant("");
            WriteImportant(
                "BROKER ORDER UI BLOCKED: " +
                operationName);
            WriteImportant(
                "BROKER: " +
                _selectedBroker.DisplayName);
            WriteImportant(
                "REASON: " +
                reason);
            WriteImportant(
                "FINAL SUBMIT CLICK: NO");
            WriteImportant(
                "HTTP POST: NOT SENT");

            SetStatus(
                reason);

            return false;
        }

        private void ResetBrokerSessionEvidence()
        {
            _authorizationHeaderObserved =
                false;

            _successfulSessionResponseObserved =
                false;

            _successfulProtectedApiResponseObserved =
                false;

            ResetLiveSubmissionTracking();
        }

        private void OfficialUiDispatcher_StateChanged(
            object? sender,
            OfficialUiDispatcherStateChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        () => OfficialUiDispatcher_StateChanged(
                            sender,
                            e)));

                return;
            }

            OfficialUiDispatcherOverlay.Visibility =
                e.IsBusy
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (e.IsBusy)
            {
                OfficialUiDispatcherOverlayText.Text =
                    e.DisplayMessage;

                WriteLog(
                    "OFFICIAL UI DISPATCH ACQUIRED: " +
                    e.OperationName +
                    " | QUEUE WAIT MS: " +
                    e.QueueDelay.TotalMilliseconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture));

                return;
            }

            WriteLog(
                "OFFICIAL UI DISPATCH RELEASED: " +
                e.OperationName +
                " | DURATION MS: " +
                e.OperationDuration.TotalMilliseconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(
                    e.FailureType)
                    ? " | RESULT: COMPLETED"
                    : " | RESULT: FAILED (" +
                      e.FailureType +
                      ")"));
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

                settings.SelectedBrokerId =
                    _selectedBroker.Id;

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
            if (_sessionCreationInProgress ||
                _liveSubmissionInProgress)
            {
                SetStatus("ایجاد نشست یا عملیات ارسال قبلی هنوز فعال است.");
                return;
            }

            if (!EnsureSelectedBrokerOrderUiAvailable(
                "open-current-order-form"))
            {
                return;
            }

            if (!_webViewReady || Browser.CoreWebView2 == null)
            {
                SetStatus(
                    _selectedBroker.DisplayName +
                    " هنوز آماده نیست.");
                return;
            }

            try
            {
                ClearCurrentOrderConfirmation();

                CoreWebView2 coreWebView =
                    Browser.CoreWebView2;

                (OfficialOrderUiBridgeResult Result, bool TrustedClickFallbackUsed)
                    dispatchResult =
                        await _officialUiDispatcher.DispatchAsync(
                            "open-current-order-form",
                            "در حال بازکردن فرم رسمی خرید...",
                            async cancellationToken =>
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                string json =
                                    await coreWebView.ExecuteScriptAsync(
                                        BrokerOfficialOrderUiBridge
                                            .BuildOpenCurrentSymbolBuyDialogScript(
                                                _selectedBroker));

                                OfficialOrderUiBridgeResult result =
                                    OfficialOrderUiBridge.ParseResult(
                                        json);

                                bool trustedClickFallbackUsed =
                                    false;

                                if (result.HasStatus(
                                    OfficialOrderUiBridge.DialogOpenRequestedStatus) &&
                                    result.ClickX > 0 &&
                                    result.ClickY > 0)
                                {
                                    trustedClickFallbackUsed =
                                        true;

                                    await DispatchTrustedLeftClickAsync(
                                        coreWebView,
                                        result.ClickX,
                                        result.ClickY,
                                        cancellationToken);
                                }

                                return (
                                    Result: result,
                                    TrustedClickFallbackUsed: trustedClickFallbackUsed);
                            });

                OfficialOrderUiBridgeResult result =
                    dispatchResult.Result;

                bool trustedClickFallbackUsed =
                    dispatchResult.TrustedClickFallbackUsed;

                WriteImportant("");
                WriteImportant("========================================");
                WriteImportant(
                    "OPEN CURRENT BROKER ORDER FORM: " +
                    _selectedBroker.DisplayName);
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

                SetStatus(opened
                    ? "فرم رسمی خرید باز شد؛ قیمت و تعداد را در " +
                      _selectedBroker.DisplayName +
                      " وارد کنید، سپس «خواندن و تأیید فرم» را بزنید."
                    : "فرم نماد جاری باز نشد: " +
                      OfficialOrderUiBridge.GetUserMessage(
                          result.Status,
                          _selectedBroker.DisplayName));
            }
            catch (Exception ex)
            {
                WriteImportant("OPEN CURRENT ORDER FORM ERROR: " + ex.Message);
                SetStatus(
                    "خطا در باز کردن فرم سفارش " +
                    _selectedBroker.DisplayName +
                    ".");
            }
        }

        // =====================================================
        // WINDOW LOADED
        // =====================================================

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            _exchangeClockDisplayTimer.Start();

            _exchangeClockMaintenanceTask =
                MaintainExchangeClockAsync(
                    _applicationCancellation.Token);

            await InitializeWebViewAsync();
        }

        private async Task MaintainExchangeClockAsync(
            CancellationToken cancellationToken)
        {
            int sampleCount =
                3;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ExchangeClockReading reading =
                        await _exchangeClock.SynchronizeAsync(
                            sampleCount,
                            cancellationToken);

                    sampleCount =
                        1;

                    WriteLog(
                        "ساعت مرکز معاملات با " +
                        ExchangeClock.SourceDisplayName +
                        " همگام شد؛ RTT=" +
                        reading.RoundTripTime.TotalMilliseconds.ToString(
                            "F0",
                            CultureInfo.InvariantCulture) +
                        " ms.");
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog(
                        "همگام‌سازی ساعت مرکز معاملات ناموفق بود: " +
                        ex.Message);
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            30),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private void ExchangeClockDisplayTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (!_exchangeClock.TryGetReading(
                TimeSpan.MaxValue,
                out ExchangeClockReading reading))
            {
                ExchangeClockTextBlock.Text =
                    "TSETMC | UNAVAILABLE";

                ExchangeClockTextBlock.Foreground =
                    Brushes.DarkRed;

                return;
            }

            bool isFresh =
                reading.SampleAge <=
                ExchangeClock.SchedulerMaximumSampleAge;

            ExchangeClockTextBlock.Text =
                reading.Now.ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture) +
                " | " +
                ExchangeClock.SourceDisplayName +
                (isFresh
                    ? " | SYNC"
                    : " | STALE");

            ExchangeClockTextBlock.Foreground =
                isFresh
                    ? Brushes.DarkGreen
                    : Brushes.DarkOrange;
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

                string? webViewProfilePath =
                    null;

                if (App.HasExplicitInstance)
                {
                    webViewProfilePath =
                        System.IO.Path.Combine(
                            System.Environment.GetFolderPath(
                                System.Environment.SpecialFolder.LocalApplicationData),
                            "FastOrder",
                            "WebView2",
                            "Instance-" +
                            App.InstanceId);

                    WriteLog(
                        "MULTI-INSTANCE MODE: ENABLED");

                    WriteLog(
                        "INSTANCE ID: " +
                        App.InstanceId);

                    WriteLog(
                        "WEBVIEW2 PROFILE: " +
                        webViewProfilePath);
                }

                if (Browser.CoreWebView2 == null)
                {
                    if (App.HasExplicitInstance)
                    {
                        CoreWebView2Environment environment =
                            await CoreWebView2Environment.CreateAsync(
                                userDataFolder:
                                    webViewProfilePath);

                        await Browser.EnsureCoreWebView2Async(
                            environment);
                    }
                    else
                    {
                        await Browser.EnsureCoreWebView2Async();
                    }

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
                    "در حال ورود به " +
                    _selectedBroker.DisplayName +
                    "...");

                coreWebView.Navigate(
                    _selectedBroker.TradingUrl);
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
                System.Collections.Generic.HashSet<string> monitoredHosts =
                    new System.Collections.Generic.HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (BrokerProfile profile in BrokerProfiles.All)
                {
                    foreach (string monitoredHost in profile.MonitoredHosts)
                    {
                        if (!monitoredHosts.Add(
                            monitoredHost))
                        {
                            continue;
                        }

                        Browser.CoreWebView2
                            .AddWebResourceRequestedFilter(
                                "https://" +
                                monitoredHost +
                                "/*",
                                CoreWebView2WebResourceContext.All);
                    }
                }

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
                    "Selected broker hosts: " +
                    string.Join(
                        ", ",
                        _selectedBroker.MonitoredHosts));
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
            if (!string.Equals(
                _selectedBroker.Id,
                BrokerProfiles.EasyTraderId,
                StringComparison.Ordinal))
            {
                return false;
            }

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

        private bool IsSessionRequest(
            string url)
        {
            if (!string.Equals(
                _selectedBroker.Id,
                BrokerProfiles.EasyTraderId,
                StringComparison.Ordinal))
            {
                return false;
            }

            return
                url.Contains(
                    "/easy/api/account/same-login",
                    StringComparison.OrdinalIgnoreCase)
                ||
                url.Contains(
                    "/easy/api/startsession",
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProtectedOrderApiRequest(
            string url)
        {
            if (!string.Equals(
                _selectedBroker.Id,
                BrokerProfiles.EasyTraderId,
                StringComparison.Ordinal))
            {
                return false;
            }

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

        private bool IsCreateOrderRequest(
            string method,
            string url)
        {
            if (!string.Equals(
                _selectedBroker.Id,
                BrokerProfiles.EasyTraderId,
                StringComparison.Ordinal))
            {
                return false;
            }

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
                _selectedBroker.IsMonitoredHost(
                    uri.Host)
                &&
                uri.AbsolutePath.Equals(
                    "/core/api/v2/order",
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsMonitoredApiUrl(
            string url)
        {
            return
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri)
                &&
                _selectedBroker.IsMonitoredHost(
                    uri.Host);
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

        private async void StartScheduledClickButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!EnsureSelectedBrokerOrderUiAvailable(
                "schedule-official-order-clicks"))
            {
                return;
            }

            if (_sessionCreationInProgress ||
                _liveSubmissionInProgress)
            {
                SetStatus(
                    "تأیید یا ارسال کنترل‌شده دیگری در حال انجام است.");

                return;
            }

            ScheduledClickSide selectedSide;

            if (ScheduledClickBuyRadioButton.IsChecked == true &&
                ScheduledClickSellRadioButton.IsChecked != true)
            {
                selectedSide = ScheduledClickSide.Buy;
            }
            else if (ScheduledClickSellRadioButton.IsChecked == true &&
                ScheduledClickBuyRadioButton.IsChecked != true)
            {
                selectedSide = ScheduledClickSide.Sell;
            }
            else
            {
                SetStatus(
                    "پیش از زمان‌بندی، سمت خرید یا فروش را صریحاً انتخاب کنید.");
                return;
            }

            if (!TryParseScheduledClickCount(
                ScheduledClickCountTextBox.Text,
                out int clickCount,
                out string countError))
            {
                SetStatus(countError);
                ScheduledClickCountTextBox.Focus();
                return;
            }

            if (!TimeOnly.TryParseExact(
                ScheduledClickStartTimeTextBox.Text?.Trim(),
                "HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly requestedStartTime))
            {
                SetStatus(
                    "زمان شروع را دقیقاً با قالب HH:mm:ss وارد کنید.");
                ScheduledClickStartTimeTextBox.Focus();
                return;
            }

            if (!_webViewReady || Browser.CoreWebView2 == null)
            {
                SetStatus(
                    "صفحه رسمی " + _selectedBroker.DisplayName +
                    " هنوز آماده نیست.");
                return;
            }

            CoreWebView2 coreWebView =
                Browser.CoreWebView2;

            if (!_selectedBroker.IsTrustedPage(coreWebView.Source))
            {
                SetStatus(
                    "صفحه فعال متعلق به مسیر رسمی کارگزاری انتخاب‌شده نیست.");
                return;
            }

            _sessionCreationInProgress = true;
            SetScheduledOrderControls(_scheduledOrderActive);

            try
            {
                ExchangeClockReading reading;
                bool hasActiveExecution;

                lock (_sessionExecutionsSyncRoot)
                {
                    hasActiveExecution =
                        _activeSessionExecutions.Count > 0 ||
                        _activeScheduledClickExecutions.Count > 0;
                }

                SetStatus(
                    hasActiveExecution
                        ? "در حال اعتبارسنجی ساعت مرکز معاملات..."
                        : "در حال همگام‌سازی ساعت مرکز معاملات...");

                reading = hasActiveExecution
                    ? await _exchangeClock.ValidateAsync(
                        3,
                        _applicationCancellation.Token)
                    : await _exchangeClock.SynchronizeAsync(
                        3,
                        _applicationCancellation.Token);

                DateTime startDateTime =
                    DateTime.SpecifyKind(
                        reading.Now.Date + requestedStartTime.ToTimeSpan(),
                        DateTimeKind.Unspecified);

                DateTimeOffset startTime =
                    new DateTimeOffset(
                        startDateTime,
                        reading.Now.Offset);

                if (startTime <= reading.Now)
                {
                    SetStatus(
                        "زمان شروع باید در آینده همان روز مرکز معاملات باشد.");
                    return;
                }

                DateTimeOffset lastTarget =
                    startTime.AddSeconds(clickCount - 1L);

                string sideDisplay =
                    selectedSide == ScheduledClickSide.Buy
                        ? "خرید (BUY)"
                        : "فروش (SELL)";

                string confirmationText =
                    "کارگزاری: " + _selectedBroker.DisplayName +
                    Environment.NewLine +
                    "سمت: " + sideDisplay +
                    Environment.NewLine +
                    "تعداد کلیک: " + clickCount.ToString(
                        CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    "زمان شروع: " + startTime.ToString(
                        "HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    "آخرین اسلات: " + lastTarget.ToString(
                        "HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    "نرخ اجرا: یک کلیک رسمی " + sideDisplay +
                    " در هر ثانیه" +
                    Environment.NewLine + Environment.NewLine +
                    "FastOrder فرم رسمی سمت انتخاب‌شده را دقیقاً با مقادیر فعلی آن کلیک می‌کند." +
                    Environment.NewLine +
                    "نماد، قیمت و تعداد توسط FastOrder خوانده یا اعتبارسنجی نشده‌اند." +
                    Environment.NewLine + Environment.NewLine +
                    "آیا این نشست واقعی فعال شود؟";

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        this,
                        confirmationText,
                        "تأیید نهایی ارسال زمان‌بندی‌شده",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No,
                        MessageBoxOptions.RtlReading |
                        MessageBoxOptions.RightAlign);

                if (confirmation != MessageBoxResult.Yes)
                {
                    SetStatus("ایجاد نشست لغو شد؛ هیچ کلیکی انجام نشد.");
                    return;
                }

                ExchangeClockReading validatedReading =
                    await _exchangeClock.ValidateAsync(
                        1,
                        _applicationCancellation.Token);

                if (startTime <= validatedReading.Now)
                {
                    SetStatus(
                        "زمان شروع هنگام تأیید سپری شد؛ نشست ایجاد نشد.");
                    return;
                }

                if (!_selectedBroker.IsTrustedPage(coreWebView.Source))
                {
                    SetStatus(
                        "صفحه رسمی کارگزاری هنگام تأیید تغییر کرد؛ نشست ایجاد نشد.");
                    return;
                }

                ScheduledClickSession session =
                    new ScheduledClickSession(
                        Interlocked.Increment(
                            ref _nextScheduledClickSessionSequence),
                        _selectedBroker,
                        selectedSide,
                        clickCount,
                        startTime);

                _scheduledClickSessions.Add(session);
                SessionDataGrid.SelectedItem = session;
                SessionDataGrid.ScrollIntoView(session);

                WriteImportant("");
                WriteImportant("========================================");
                WriteImportant("SCHEDULED CLICK SESSION CREATED");
                WriteImportant("========================================");
                WriteImportant(
                    "SESSION: " + session.SessionIdDisplay);
                WriteImportant(
                    "BROKER: " + session.BrokerDisplayName);
                WriteImportant(
                    "SIDE: " +
                    (session.Side == ScheduledClickSide.Buy
                        ? "BUY"
                        : "SELL"));
                WriteImportant(
                    "CLICK COUNT: " + clickCount.ToString(
                        CultureInfo.InvariantCulture));
                WriteImportant(
                    "START TIME: " + startTime.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff zzz",
                        CultureInfo.InvariantCulture));
                WriteImportant("ORDER VALUES READ: NO");
                WriteImportant("ORDER FIELDS MODIFIED: NO");
                WriteImportant("DIRECT API CREDENTIALS: NOT ACCESSED");
                WriteImportant("========================================");

                RegisterScheduledClickSession(
                    coreWebView,
                    session);

                SetStatus(
                    "نشست کلیک زمان‌بندی‌شده ایجاد و مسلح شد.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("ایجاد نشست لغو شد؛ هیچ کلیک جدیدی انجام نشد.");
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "SCHEDULED CLICK SESSION CREATION FAILED: " + ex.Message);
                SetStatus(
                    "نشست ایجاد نشد: " + ex.Message);
            }
            finally
            {
                _sessionCreationInProgress = false;
                SetScheduledOrderControls(_scheduledOrderActive);
            }
        }

        private static bool TryParseScheduledClickCount(
            string? input,
            out int clickCount,
            out string error)
        {
            StringBuilder normalized = new StringBuilder();

            foreach (char character in input?.Trim() ?? "")
            {
                if (character is >= '\u06F0' and <= '\u06F9')
                {
                    normalized.Append(
                        (char)('0' + character - '\u06F0'));
                }
                else if (character is >= '\u0660' and <= '\u0669')
                {
                    normalized.Append(
                        (char)('0' + character - '\u0660'));
                }
                else
                {
                    normalized.Append(character);
                }
            }

            if (!int.TryParse(
                normalized.ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out clickCount))
            {
                error = "تعداد سفارش الزامی است و باید یک عدد صحیح باشد.";
                return false;
            }

            if (clickCount is < 1 or > 1000)
            {
                error = "تعداد سفارش باید بین ۱ و ۱۰۰۰ باشد.";
                return false;
            }

            error = "";
            return true;
        }

        private void RegisterScheduledClickSession(
            CoreWebView2 coreWebView,
            ScheduledClickSession session)
        {
            ArgumentNullException.ThrowIfNull(coreWebView);
            ArgumentNullException.ThrowIfNull(session);

            ScheduledClickExecution execution =
                new ScheduledClickExecution(session);

            lock (_sessionExecutionsSyncRoot)
            {
                if (!_activeScheduledClickExecutions.TryAdd(
                    session.SessionId,
                    execution))
                {
                    execution.Dispose();
                    throw new InvalidOperationException(
                        "An active scheduled-click execution already exists for this session.");
                }
            }

            RefreshScheduledOrderActivityState();
            EnsureScheduledClockRefreshActive();

            Task schedulerTask =
                RunScheduledClickSessionAsync(
                    coreWebView,
                    execution);

            _ = ObserveScheduledClickTaskAsync(
                execution,
                schedulerTask);
        }

        private async Task ObserveScheduledClickTaskAsync(
            ScheduledClickExecution execution,
            Task schedulerTask)
        {
            try
            {
                await schedulerTask;
            }
            catch (Exception ex)
            {
                if (execution.Session.State is not
                    (OrderSessionState.Completed or
                     OrderSessionState.Canceled or
                     OrderSessionState.Failed))
                {
                    execution.Session.SetState(
                        OrderSessionState.Failed,
                        "خطای پیش‌بینی‌نشده زمان‌بند: " + ex.Message);
                }

                WriteImportant(
                    "SCHEDULED CLICK UNOBSERVED ERROR: " + ex.Message);
            }
            finally
            {
                lock (_sessionExecutionsSyncRoot)
                {
                    if (_activeScheduledClickExecutions.TryGetValue(
                        execution.Session.SessionId,
                        out ScheduledClickExecution? currentExecution) &&
                        ReferenceEquals(currentExecution, execution))
                    {
                        _activeScheduledClickExecutions.Remove(
                            execution.Session.SessionId);
                    }
                }

                execution.Dispose();
                RefreshScheduledOrderActivityState();
                StopScheduledClockRefreshIfIdle();
            }
        }

        private async Task RunScheduledClickSessionAsync(
            CoreWebView2 coreWebView,
            ScheduledClickExecution execution)
        {
            ScheduledClickSession session = execution.Session;
            BrokerProfile broker =
                BrokerProfiles.ResolveOrDefault(session.BrokerId);
            string sideName =
                session.Side == ScheduledClickSide.Buy
                    ? "BUY"
                    : "SELL";

            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    execution.CancellationToken,
                    _applicationCancellation.Token);

            CancellationToken cancellationToken =
                linkedCancellation.Token;

            int skippedSlotCount = 0;

            try
            {
                if (!string.Equals(
                    broker.Id,
                    _selectedBroker.Id,
                    StringComparison.Ordinal))
                {
                    session.SetState(
                        OrderSessionState.Failed,
                        "کارگزاری نشست با صفحه فعال تطبیق ندارد.");
                    return;
                }

                session.SetState(
                    OrderSessionState.Waiting,
                    "در انتظار نخستین اسلات");

                for (int slotIndex = 0;
                    slotIndex < session.TotalClickCount;
                    slotIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    DateTimeOffset target =
                        session.StartTime.AddSeconds(slotIndex);
                    DateTimeOffset deadline =
                        target.Add(ScheduledOrderRetryDelay);

                    session.UpdateProgress(
                        execution.ClickedCount,
                        target,
                        "در انتظار اسلات " +
                        (slotIndex + 1).ToString(CultureInfo.InvariantCulture));

                    await WaitUntilExchangeTimeAsync(
                        target,
                        cancellationToken);

                    DateTimeOffset actual =
                        GetFreshExchangeTime();

                    WriteImportant("");
                    WriteImportant("CLOCK SLOT STARTED");
                    WriteImportant(
                        "SESSION: " + session.SessionIdDisplay);
                    WriteImportant(
                        "SLOT: " + (slotIndex + 1).ToString(
                            CultureInfo.InvariantCulture));
                    WriteImportant(
                        "TARGET: " + target.ToString(
                            "HH:mm:ss.fff",
                            CultureInfo.InvariantCulture));
                    WriteImportant(
                        "ACTUAL: " + actual.ToString(
                            "HH:mm:ss.fff",
                            CultureInfo.InvariantCulture));

                    if (actual >= deadline)
                    {
                        skippedSlotCount++;
                        session.UpdateProgress(
                            execution.ClickedCount,
                            slotIndex + 1 < session.TotalClickCount
                                ? session.StartTime.AddSeconds(slotIndex + 1L)
                                : null,
                            "اسلات ازدست‌رفته؛ بدون جبران فشرده");
                        WriteImportant(
                            "OFFICIAL " + sideName + " CLICKED: NO");
                        WriteImportant("RESULT: MISSED SLOT — NO BURST CATCH-UP");
                        continue;
                    }

                    session.SetState(
                        OrderSessionState.Running,
                        "در حال اجرای کلیک رسمی");

                    OfficialOrderUiBridgeResult clickResult;

                    try
                    {
                        clickResult =
                            await _officialUiDispatcher.DispatchAsync(
                                "scheduled-official-order-click:" + sideName,
                                "در حال اجرای کلیک رسمی " +
                                session.SideDisplay + "...",
                                async dispatcherCancellationToken =>
                                {
                                    dispatcherCancellationToken
                                        .ThrowIfCancellationRequested();

                                    if (!broker.IsTrustedPage(coreWebView.Source))
                                    {
                                        return new OfficialOrderUiBridgeResult
                                        {
                                            Status = "INVALID_ORIGIN",
                                            Reason = "The active page is not a trusted broker origin."
                                        };
                                    }

                                    if (GetFreshExchangeTime() >= deadline)
                                    {
                                        return new OfficialOrderUiBridgeResult
                                        {
                                            Status = "SLOT_MISSED",
                                            Reason = "The one-second slot expired before dispatch."
                                        };
                                    }

                                    string resultJson =
                                        await coreWebView.ExecuteScriptAsync(
                                            BrokerOfficialOrderUiBridge
                                                .BuildClickCurrentOfficialOrderButtonScript(
                                                    broker,
                                                    session.Side));

                                    return OfficialOrderUiBridge.ParseResult(
                                        resultJson);
                                },
                                cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        session.SetState(
                            OrderSessionState.Failed,
                            "نتیجه کلیک رسمی مبهم است؛ ادامه نشست متوقف شد.");
                        WriteImportant(
                            "OFFICIAL " + sideName +
                            " CLICK RESULT: AMBIGUOUS");
                        WriteImportant("AUTO RETRY: NO");
                        WriteImportant("REASON: " + ex.Message);
                        return;
                    }

                    if (clickResult.HasStatus(
                        OfficialOrderUiBridge.ClickedStatus))
                    {
                        int clickedCount = execution.CommitClicked();
                        int remainingClickCount =
                            session.TotalClickCount - clickedCount;
                        DateTimeOffset? nextDue =
                            slotIndex + 1 < session.TotalClickCount
                                ? session.StartTime.AddSeconds(slotIndex + 1L)
                                : null;

                        session.UpdateProgress(
                            clickedCount,
                            nextDue,
                            "کلیک رسمی " + session.SideDisplay + " انجام شد");

                        WriteImportant(
                            "OFFICIAL " + sideName + " CLICKED");
                        WriteImportant(
                            "CLICKED COUNT: " + clickedCount.ToString(
                                CultureInfo.InvariantCulture));
                        WriteImportant(
                            "REMAINING CLICK COUNT: " +
                            remainingClickCount.ToString(
                                CultureInfo.InvariantCulture));

                        continue;
                    }

                    if (clickResult.HasStatus("SLOT_MISSED"))
                    {
                        skippedSlotCount++;
                    }

                    session.UpdateProgress(
                        execution.ClickedCount,
                        slotIndex + 1 < session.TotalClickCount
                            ? session.StartTime.AddSeconds(slotIndex + 1L)
                            : null,
                        clickResult.Status + ": " + clickResult.Reason);

                    WriteImportant(
                        "OFFICIAL " + sideName + " CLICKED: NO");
                    WriteImportant("STATUS: " + clickResult.Status);
                    WriteImportant("REASON: " + clickResult.Reason);
                    WriteImportant("AUTO RETRY FOR THIS SLOT: NO");

                    if (!IsDefinitivePreClickFailure(clickResult.Status))
                    {
                        session.SetState(
                            OrderSessionState.Failed,
                            "نتیجه کلیک رسمی قابل اثبات نیست؛ ادامه نشست متوقف شد.");
                        return;
                    }

                    session.SetState(
                        OrderSessionState.Waiting,
                        "اسلات بدون کلیک پایان یافت؛ در انتظار اسلات بعدی");
                }

                session.SetState(
                    OrderSessionState.Completed,
                    "برنامه اسلات‌ها تکمیل شد؛ کلیک‌شده: " +
                    execution.ClickedCount.ToString(CultureInfo.InvariantCulture) +
                    "، بدون کلیک: " +
                    (session.TotalClickCount - execution.ClickedCount)
                        .ToString(CultureInfo.InvariantCulture) +
                    (skippedSlotCount > 0
                        ? "، اسلات ازدست‌رفته: " + skippedSlotCount.ToString(
                            CultureInfo.InvariantCulture)
                        : ""));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                session.UpdateProgress(
                    execution.ClickedCount,
                    null,
                    "لغو شد؛ کلیک‌های انجام‌شده بازگردانده نمی‌شوند");
                session.SetState(
                    OrderSessionState.Canceled,
                    "لغو شد؛ کلیک‌های انجام‌شده بازگردانده نمی‌شوند");
            }
            catch (Exception ex)
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "زمان‌بند برای جلوگیری از کلیک نامطمئن متوقف شد: " +
                    ex.Message);
                WriteImportant(
                    "SCHEDULED CLICK SESSION FAILED: " + ex.Message);
            }
        }

        private async Task WaitUntilExchangeTimeAsync(
            DateTimeOffset targetTime,
            CancellationToken cancellationToken)
        {
            TimeSpan maximumWaitChunk =
                TimeSpan.FromSeconds(1);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimeSpan remaining =
                    targetTime - GetFreshExchangeTime();

                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                await Task.Delay(
                    remaining < maximumWaitChunk
                        ? remaining
                        : maximumWaitChunk,
                    cancellationToken);
            }
        }

        private static bool IsDefinitivePreClickFailure(
            string status) =>
            status is
                "INVALID_ORIGIN" or
                "ORDER_ACTION_NOT_FOUND" or
                "ORDER_ACTION_AMBIGUOUS" or
                "ORDER_ACTION_DISABLED" or
                "SLOT_MISSED";

        private void RegisterScheduledOrderSession(
            CoreWebView2 coreWebView,
            OrderSession session)
        {
            ArgumentNullException.ThrowIfNull(
                coreWebView);

            ArgumentNullException.ThrowIfNull(
                session);

            OrderSessionExecution execution =
                new OrderSessionExecution(
                    session);

            lock (_sessionExecutionsSyncRoot)
            {
                if (!_activeSessionExecutions.TryAdd(
                    session.SessionId,
                    execution))
                {
                    execution.Dispose();

                    throw new InvalidOperationException(
                        "An active scheduler execution already exists for this session.");
                }
            }

            RefreshScheduledOrderActivityState();
            EnsureScheduledClockRefreshActive();
            PulseAllSessionSchedulers();

            Task schedulerTask =
                RunScheduledOrderAsync(
                    coreWebView,
                    execution);

            _ = ObserveScheduledOrderTaskAsync(
                execution,
                schedulerTask);
        }

        private async Task ObserveScheduledOrderTaskAsync(
            OrderSessionExecution execution,
            Task schedulerTask)
        {
            try
            {
                await schedulerTask;
            }
            catch (Exception ex)
            {
                if (execution.Session.State is not
                    (OrderSessionState.Completed or
                     OrderSessionState.Canceled or
                     OrderSessionState.Failed))
                {
                    execution.Session.SetState(
                        OrderSessionState.Failed,
                        "خطای پیش‌بینی‌نشده در زمان‌بند نشست",
                        ex.Message);
                }

                WriteImportant(
                    "SESSION SCHEDULER UNOBSERVED ERROR: " +
                    ex.Message);
            }
            finally
            {
                _globalNextDueQueue.RemoveSession(
                    execution.Session.SessionId);

                lock (_sessionExecutionsSyncRoot)
                {
                    if (_activeSessionExecutions.TryGetValue(
                        execution.Session.SessionId,
                        out OrderSessionExecution? currentExecution) &&
                        ReferenceEquals(
                            currentExecution,
                            execution))
                    {
                        _activeSessionExecutions.Remove(
                            execution.Session.SessionId);
                    }
                }

                execution.TryMarkFinalized();
                execution.Dispose();

                RefreshScheduledOrderActivityState();
                StopScheduledClockRefreshIfIdle();
                PulseAllSessionSchedulers();
            }
        }

        private bool TryGetActiveSessionExecution(
            Guid sessionId,
            out OrderSessionExecution? execution)
        {
            lock (_sessionExecutionsSyncRoot)
            {
                return _activeSessionExecutions.TryGetValue(
                    sessionId,
                    out execution);
            }
        }

        private List<OrderSessionExecution> GetActiveSessionExecutionSnapshot()
        {
            lock (_sessionExecutionsSyncRoot)
            {
                return new List<OrderSessionExecution>(
                    _activeSessionExecutions.Values);
            }
        }

        private void PulseAllSessionSchedulers()
        {
            foreach (OrderSessionExecution execution in
                GetActiveSessionExecutionSnapshot())
            {
                execution.Pulse();
            }
        }

        private void RefreshScheduledOrderActivityState()
        {
            lock (_sessionExecutionsSyncRoot)
            {
                _scheduledOrderActive =
                    _activeSessionExecutions.Count > 0 ||
                    _activeScheduledClickExecutions.Count > 0;
            }

            SetScheduledOrderControls(
                _scheduledOrderActive);
        }

        private void EnsureScheduledClockRefreshActive()
        {
            lock (_scheduledClockRefreshSyncRoot)
            {
                if (_scheduledClockRefreshTask != null)
                {
                    return;
                }

                _scheduledClockRefreshCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        _applicationCancellation.Token);

                _scheduledClockRefreshTask =
                    RefreshExchangeClockDuringScheduleAsync(
                        _scheduledClockRefreshCancellation.Token);
            }
        }

        private void StopScheduledClockRefreshIfIdle()
        {
            lock (_sessionExecutionsSyncRoot)
            {
                if (_activeSessionExecutions.Count > 0 ||
                    _activeScheduledClickExecutions.Count > 0)
                {
                    return;
                }
            }

            CancellationTokenSource? cancellationSource;
            Task? refreshTask;

            lock (_scheduledClockRefreshSyncRoot)
            {
                cancellationSource =
                    _scheduledClockRefreshCancellation;

                refreshTask =
                    _scheduledClockRefreshTask;

                _scheduledClockRefreshCancellation =
                    null;

                _scheduledClockRefreshTask =
                    null;
            }

            if (cancellationSource == null ||
                refreshTask == null)
            {
                return;
            }

            cancellationSource.Cancel();

            _ = DisposeScheduledClockRefreshAsync(
                refreshTask,
                cancellationSource);
        }

        private static async Task DisposeScheduledClockRefreshAsync(
            Task refreshTask,
            CancellationTokenSource cancellationSource)
        {
            try
            {
                await refreshTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellationSource.Dispose();
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
            OrderSessionExecution execution)
        {
            ArgumentNullException.ThrowIfNull(
                execution);

            OrderSession session =
                execution.Session;

            ArgumentNullException.ThrowIfNull(
                session);

            if (!string.Equals(
                session.BrokerId,
                _selectedBroker.Id,
                StringComparison.Ordinal))
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "کارگزاری نشست با مسیر فعال تطبیق ندارد",
                    "پیش از اجرا، همان کارگزاری ثبت‌شده در Snapshot را انتخاب کنید.");

                WriteLiveSubmissionBlocked(
                    "کارگزاری نشست با مسیر فعال تطبیق ندارد.");

                return;
            }

            if (!EnsureSelectedBrokerOrderUiAvailable(
                "run-scheduled-order"))
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "مسیر رسمی کارگزاری آماده نیست");

                return;
            }

            if (!TryGetValidatedSessionOrder(
                session,
                out ConfirmedOrderSnapshot? snapshot,
                out CreateOrderPayload? payload,
                out string snapshotError) ||
                snapshot == null ||
                payload?.Order == null)
            {
                session.SetState(
                    OrderSessionState.Failed,
                    "Snapshot مستقل نشست نامعتبر است",
                    snapshotError);

                WriteLiveSubmissionBlocked(
                    snapshotError);

                return;
            }

            Order order =
                payload.Order;

            DateTimeOffset startAt =
                session.StartTime;

            DateTimeOffset endAt =
                session.EndTime;

            long maxQuantityPerOrder =
                session.MaxQuantityPerOrder;

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

            using CancellationTokenSource scheduleLifetimeCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    execution.CancellationToken,
                    _applicationCancellation.Token);

            CancellationToken scheduleCancellationToken =
                scheduleLifetimeCancellation.Token;

            long totalQuantity =
                order.Quantity;

            System.Collections.Generic.List<Task>
                activeDispatchTasks =
                    new System.Collections.Generic.List<Task>();

            void UpdateSessionProgress(
                DateTimeOffset? nextDueAt,
                string status)
            {
                OrderSessionAccountingSnapshot accounting =
                    execution.GetAccountingSnapshot();

                session.UpdateProgress(
                    accounting.SentQuantity,
                    accounting.InFlightQuantity,
                    accounting.ClickedOrderCount,
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
                _globalNextDueQueue.RemoveSession(
                    session.SessionId);

                _globalNextDueQueue.Enqueue(
                    execution.CreateNextSlice(
                        startAt,
                        DefaultScheduledSlicePriority));

                PulseAllSessionSchedulers();

                WriteImportant(
                    "GLOBAL NEXT-DUE ENQUEUED: " +
                    session.SessionIdDisplay +
                    " @ " +
                    startAt.ToString(
                        "HH:mm:ss.fff",
                        CultureInfo.InvariantCulture));

                // PRE-WARM:
                // هر نشست فقط Snapshot مستقل خودش را آماده می‌کند. ترتیب کلیک‌ها
                // همچنان توسط صف سراسری و Dispatcher واحد تعیین می‌شود.
                DateTimeOffset preWarmAt =
                    startAt -
                    ScheduledOrderPreWarmLeadTime;

                WriteImportant(
                    "PRE-WARM WAIT UNTIL: " +
                    preWarmAt.ToString(
                        "HH:mm:ss.fff"));

                while (execution.IsPaused ||
                    GetFreshExchangeTime() < preWarmAt)
                {
                    if (execution.IsPaused)
                    {
                        await execution.WaitForWakeAsync(
                            TimeSpan.FromSeconds(1),
                            scheduleCancellationToken);

                        continue;
                    }

                    await WaitUntilExchangeTimeOrWakeAsync(
                        preWarmAt,
                        execution,
                        scheduleCancellationToken);
                }

                scheduleCancellationToken
                    .ThrowIfCancellationRequested();

                if (GetFreshExchangeTime() <
                    endAt)
                {
                    long preWarmQuantity =
                        Math.Min(
                            totalQuantity,
                            maxQuantityPerOrder);

                    Order preWarmOrder =
                        CreateScheduledSliceOrder(
                            order,
                            preWarmQuantity);

                    session.SetState(
                        OrderSessionState.PreWarming,
                        "در حال آماده‌سازی فرم رسمی");

                    string preWarmNonce =
                        Guid.NewGuid()
                            .ToString(
                                "N");

                    DateTimeOffset preWarmStartedAt =
                        GetFreshExchangeTime();

                    OfficialOrderUiBridgeResult preWarmResult =
                        await DispatchPrepareAndClearOfficialOrderFormAsync(
                            coreWebView,
                            preWarmOrder,
                            preWarmNonce,
                            "session-pre-warm:" +
                            session.SessionIdDisplay,
                            "در حال آماده‌سازی فرم " +
                            session.SymbolName +
                            "...",
                            scheduleCancellationToken);

                    DateTimeOffset preWarmCompletedAt =
                        GetFreshExchangeTime();

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

                    if (execution.IsPaused)
                    {
                        session.SetState(
                            OrderSessionState.Paused,
                            "متوقف موقت؛ فرم رسمی آماده است");
                    }
                    else
                    {
                        session.SetState(
                            OrderSessionState.Ready,
                            "فرم رسمی برای اولین اسلات آماده است");
                    }
                }

                while (true)
                {
                    if (session.State ==
                        OrderSessionState.Failed)
                    {
                        break;
                    }

                    scheduleCancellationToken
                        .ThrowIfCancellationRequested();

                    DateTimeOffset schedulerNow =
                        GetFreshExchangeTime();

                    if (schedulerNow >=
                        endAt)
                    {
                        execution.MarkWindowClosed();
                        break;
                    }

                    if (execution.IsPaused)
                    {
                        _globalNextDueQueue.RemoveSession(
                            session.SessionId);

                        await execution.WaitForWakeAsync(
                            TimeSpan.FromSeconds(1),
                            scheduleCancellationToken);

                        continue;
                    }

                    if (!_globalNextDueQueue.TryPeek(
                        out ScheduledSlice? nextDueSlice) ||
                        nextDueSlice == null)
                    {
                        OrderSessionAccountingSnapshot accounting =
                            execution.GetAccountingSnapshot();

                        if (accounting.SentQuantity >=
                            totalQuantity)
                        {
                            break;
                        }

                        await execution.WaitForWakeAsync(
                            TimeSpan.FromMilliseconds(100),
                            scheduleCancellationToken);

                        continue;
                    }

                    if (!ReferenceEquals(
                        nextDueSlice.Session,
                        session))
                    {
                        TimeSpan foreignSliceWait =
                            nextDueSlice.TargetTime -
                            schedulerNow;

                        if (foreignSliceWait <=
                            TimeSpan.Zero)
                        {
                            foreignSliceWait =
                                TimeSpan.FromMilliseconds(10);
                        }
                        else if (foreignSliceWait >
                            TimeSpan.FromSeconds(1))
                        {
                            foreignSliceWait =
                                TimeSpan.FromSeconds(1);
                        }

                        await execution.WaitForWakeAsync(
                            foreignSliceWait,
                            scheduleCancellationToken);

                        continue;
                    }

                    DateTimeOffset nextSlot =
                        nextDueSlice.TargetTime;

                    bool queueChanged =
                        await WaitUntilExchangeTimeOrWakeAsync(
                        nextSlot,
                        execution,
                        scheduleCancellationToken);

                    if (queueChanged)
                    {
                        continue;
                    }

                    scheduleCancellationToken
                        .ThrowIfCancellationRequested();

                    if (session.State ==
                        OrderSessionState.Failed)
                    {
                        break;
                    }

                    DateTimeOffset slotStartedAt =
                        GetFreshExchangeTime();

                    if (slotStartedAt >=
                        endAt)
                    {
                        break;
                    }

                    if (!_globalNextDueQueue.TryPeek(
                        out ScheduledSlice? currentHeadSlice) ||
                        !ReferenceEquals(
                            currentHeadSlice,
                            nextDueSlice))
                    {
                        continue;
                    }

                    if (!_globalNextDueQueue.TryDequeue(
                        out ScheduledSlice? dequeuedSlice) ||
                        !ReferenceEquals(
                            dequeuedSlice,
                            nextDueSlice))
                    {
                        throw new InvalidOperationException(
                            "Global next-due queue changed before the due slice was dequeued.");
                    }

                    PulseAllSessionSchedulers();

                    if (!ReferenceEquals(
                        session.ConfirmedOrderSnapshot,
                        snapshot) ||
                        !snapshot.HasValidFingerprint())
                    {
                        session.SetState(
                            OrderSessionState.Failed,
                            "Snapshot مستقل نشست نامعتبر شده است",
                            "اجرای نشست پیش از اسلات بعدی متوقف شد.");

                        WriteScheduledOrderStopped(
                            "Snapshot مستقل نشست نامعتبر شده است.",
                            "STOPPED BEFORE NEXT SLOT");

                        break;
                    }

                    int slotNumber =
                        execution.NextSlotNumber();

                    OrderSessionAccountingSnapshot accountingBeforeReservation =
                        execution.GetAccountingSnapshot();

                    bool totalAlreadySent =
                        accountingBeforeReservation.SentQuantity >=
                        totalQuantity;

                    execution.TryReserve(
                        maxQuantityPerOrder,
                        out long currentQuantity);

                    DateTimeOffset? sessionNextDueAt =
                        null;

                    if (!totalAlreadySent)
                    {
                        DateTimeOffset nextEligibleTarget =
                            nextSlot +
                            ScheduledOrderRetryDelay;

                        // اگر event-loop دیر بیدار شد، slotهای گذشته burst نمی‌شوند.
                        DateTimeOffset nowAfterScheduling =
                            GetFreshExchangeTime();

                        int skippedPastSlotCount =
                            0;

                        while (nextEligibleTarget <=
                            nowAfterScheduling &&
                            nextEligibleTarget <
                            endAt)
                        {
                            nextEligibleTarget =
                                nextEligibleTarget +
                                ScheduledOrderRetryDelay;

                            skippedPastSlotCount++;
                        }

                        if (skippedPastSlotCount > 0)
                        {
                            WriteImportant(
                                "GLOBAL NEXT-DUE MISSED SLOTS SKIPPED: " +
                                skippedPastSlotCount);
                        }

                        if (nextEligibleTarget <
                            endAt)
                        {
                            _globalNextDueQueue.Enqueue(
                                execution.CreateNextSlice(
                                    nextEligibleTarget,
                                    DefaultScheduledSlicePriority));

                            PulseAllSessionSchedulers();

                            sessionNextDueAt =
                                nextEligibleTarget;

                            WriteImportant(
                                "GLOBAL NEXT-DUE ENQUEUED: " +
                                session.SessionIdDisplay +
                                " @ " +
                                nextEligibleTarget.ToString(
                                    "HH:mm:ss.fff",
                                    CultureInfo.InvariantCulture));
                        }
                    }

                    if (currentQuantity > 0)
                    {
                        int capturedSlotNumber =
                            slotNumber;

                        long capturedQuantity =
                            currentQuantity;

                        DateTimeOffset? capturedNextDueAt =
                            sessionNextDueAt;

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

                        execution.DispatchStarted();

                        Task dispatchTask =
                            DispatchReservedSliceForExecutionAsync(
                                coreWebView,
                                execution,
                                session,
                                currentOrder,
                                capturedSlotNumber,
                                capturedQuantity,
                                CancellationToken.None,
                                onClicked: quantity =>
                                {
                                    execution.CommitClicked(
                                        quantity);

                                    if (!execution.ShouldScheduleAnotherSlice())
                                    {
                                        _globalNextDueQueue.RemoveSession(
                                            session.SessionId);

                                        PulseAllSessionSchedulers();
                                    }

                                    UpdateSessionProgress(
                                        execution.IsPaused ||
                                        execution.CancelRequested
                                            ? null
                                            : capturedNextDueAt,
                                        "اسلات " +
                                        capturedSlotNumber +
                                        " با کلیک رسمی ثبت شد");
                                },
                                onNotClicked: quantity =>
                                {
                                    execution.ReleaseReservation(
                                        quantity);

                                    UpdateSessionProgress(
                                        execution.IsPaused ||
                                        execution.CancelRequested
                                            ? null
                                            : capturedNextDueAt,
                                        "اسلات " +
                                        capturedSlotNumber +
                                        " کلیک نشد؛ رزرو آزاد شد");
                                });

                        activeDispatchTasks.Add(
                            dispatchTask);

                    }
                    else
                    {
                        OrderSessionAccountingSnapshot accounting =
                            execution.GetAccountingSnapshot();

                        WriteImportant(
                            "CLOCK SLOT " +
                            slotNumber +
                            ": NO FREE QUANTITY");
                        WriteImportant(
                            "SENT: " +
                            accounting.SentQuantity);
                        WriteImportant(
                            "IN-FLIGHT: " +
                            accounting.InFlightQuantity);

                        UpdateSessionProgress(
                            sessionNextDueAt,
                            "حجم آزاد برای اسلات جدید وجود ندارد");
                    }

                    bool allQuantityAccounted =
                        execution.GetAccountingSnapshot().SentQuantity >=
                        totalQuantity;

                    if (allQuantityAccounted)
                    {
                        _globalNextDueQueue.RemoveSession(
                            session.SessionId);

                        break;
                    }
                }

                _globalNextDueQueue.RemoveSession(
                    session.SessionId);

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

                OrderSessionAccountingSnapshot finalAccounting =
                    execution.GetAccountingSnapshot();

                long finalSent =
                    finalAccounting.SentQuantity;

                long finalInFlight =
                    finalAccounting.InFlightQuantity;

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
                    finalAccounting.ClickedOrderCount);
                WriteImportant(
                    "BROKER OUTCOME: VERIFY IN " +
                    session.BrokerDisplayName +
                    " ORDER LIST");
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
                        ? "کل حجم نشست " +
                          session.SessionIdDisplay +
                          " از طریق کلیک رسمی ارسال شد؛ نتیجه را در " +
                          session.BrokerDisplayName +
                          " بررسی کنید."
                        : "بازه ارسال پایان یافت؛ بخشی از حجم ارسال نشد.");
            }
            catch (OperationCanceledException)
            {
                _globalNextDueQueue.RemoveSession(
                    session.SessionId);

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

                OrderSessionAccountingSnapshot canceledAccounting =
                    execution.GetAccountingSnapshot();

                WriteImportant(
                    "CANCEL FINAL SENT QUANTITY: " +
                    canceledAccounting.SentQuantity);
                WriteImportant(
                    "CANCEL FINAL IN-FLIGHT QUANTITY: " +
                    canceledAccounting.InFlightQuantity);

                UpdateSessionProgress(
                    null,
                    "لغو شد؛ dispatchهای شروع‌شده تعیین تکلیف شدند");

                string cancellationReason =
                    string.IsNullOrWhiteSpace(
                        execution.CancellationReason)
                        ? "اجرای نشست متوقف شد."
                        : execution.CancellationReason;

                if (execution.CancellationIsFailure)
                {
                    session.SetState(
                        OrderSessionState.Failed,
                        "به علت خرابی زیرساخت متوقف شد",
                        cancellationReason);

                    WriteScheduledOrderStopped(
                        cancellationReason +
                        " slot جدید ایجاد نشد و dispatchهای قبلاً شروع‌شده تعیین تکلیف شدند.",
                        "STOPPED ON INFRASTRUCTURE FAILURE");
                }
                else
                {
                    session.SetState(
                        OrderSessionState.Canceled,
                        "توسط کاربر لغو شد");

                    WriteScheduledOrderStopped(
                        cancellationReason +
                        " slot جدید ایجاد نشد و dispatchهای قبلاً شروع‌شده تعیین تکلیف شدند.",
                        "CANCELED BY USER");
                }
            }
            catch (Exception ex)
            {
                _globalNextDueQueue.RemoveSession(
                    session.SessionId);

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

                OrderSessionAccountingSnapshot errorAccounting =
                    execution.GetAccountingSnapshot();

                WriteImportant(
                    "INTERNAL ERROR FINAL SENT QUANTITY: " +
                    errorAccounting.SentQuantity);
                WriteImportant(
                    "INTERNAL ERROR FINAL IN-FLIGHT QUANTITY: " +
                    errorAccounting.InFlightQuantity);

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
                int removedQueuedSliceCount =
                    _globalNextDueQueue.RemoveSession(
                        session.SessionId);

                if (removedQueuedSliceCount > 0)
                {
                    WriteImportant(
                        "GLOBAL NEXT-DUE CLEANUP REMOVED: " +
                        removedQueuedSliceCount +
                        " | SESSION: " +
                        session.SessionIdDisplay);
                }

                PulseAllSessionSchedulers();
            }
        }

        private DateTimeOffset GetFreshExchangeTime()
        {
            if (!_exchangeClock.TryGetReading(
                ExchangeClock.SchedulerMaximumSampleAge,
                out ExchangeClockReading reading))
            {
                throw new InvalidOperationException(
                    "ساعت مرکز معاملات معتبر یا تازه نیست؛ ایجاد slot جدید متوقف شد.");
            }

            return reading.Now;
        }

        private async Task<bool> WaitUntilExchangeTimeOrWakeAsync(
            DateTimeOffset targetTime,
            OrderSessionExecution execution,
            CancellationToken cancellationToken)
        {
            TimeSpan maximumWaitChunk =
                TimeSpan.FromSeconds(1);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimeSpan remaining =
                    targetTime -
                    GetFreshExchangeTime();

                if (remaining <=
                    TimeSpan.Zero)
                {
                    return false;
                }

                TimeSpan waitDuration =
                    remaining < maximumWaitChunk
                        ? remaining
                        : maximumWaitChunk;

                if (await execution.WaitForWakeAsync(
                    waitDuration,
                    cancellationToken))
                {
                    return true;
                }
            }
        }

        private async Task RefreshExchangeClockDuringScheduleAsync(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        ExchangeClockRefreshInterval,
                        cancellationToken);

                    ExchangeClockReading reading =
                        await _exchangeClock.ValidateAsync(
                            1,
                            cancellationToken);

                    WriteImportant(
                        "EXCHANGE CLOCK VALIDATED: " +
                        reading.Now.ToString(
                            "HH:mm:ss.fff",
                            CultureInfo.InvariantCulture) +
                        " | RTT MS: " +
                        reading.RoundTripTime.TotalMilliseconds.ToString(
                            "F0",
                            CultureInfo.InvariantCulture));
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteImportant(
                        "EXCHANGE CLOCK VALIDATION FAILED: " +
                        ex.Message);
                }
            }
        }

        private async Task DispatchReservedSliceForExecutionAsync(
            CoreWebView2 coreWebView,
            OrderSessionExecution execution,
            OrderSession session,
            Order order,
            int slotNumber,
            long reservedQuantity,
            CancellationToken cancellationToken,
            Action<long> onClicked,
            Action<long> onNotClicked)
        {
            try
            {
                await DispatchReservedSliceAsync(
                    coreWebView,
                    session,
                    order,
                    slotNumber,
                    reservedQuantity,
                    cancellationToken,
                    onClicked,
                    onNotClicked);
            }
            finally
            {
                execution.DispatchFinished();
                PulseAllSessionSchedulers();
            }
        }

        private async Task DispatchReservedSliceAsync(
            CoreWebView2 coreWebView,
            OrderSession session,
            Order order,
            int slotNumber,
            long reservedQuantity,
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
                        session,
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

            if (IsMandatoryPreSubmitVerificationFailure(
                result))
            {
                string verificationError =
                    OfficialOrderUiBridge.GetUserMessage(
                        result.Status,
                        session.BrokerDisplayName);

                session.SetState(
                    OrderSessionState.Failed,
                    "عدم تطبیق نهایی فرم رسمی",
                    verificationError);

                WriteScheduledOrderStopped(
                    "نشست " +
                    session.SessionIdDisplay +
                    " به دلیل عدم تطبیق نهایی فرم رسمی متوقف شد: " +
                    verificationError,
                    "MANDATORY PRE-SUBMIT VERIFICATION FAILED");
            }

            WriteImportant(
                "CLOCK SLOT " +
                slotNumber +
                ": NOT CLICKED; RESERVATION RELEASED: " +
                reservedQuantity);

            WriteImportant(
                "STATUS: " +
                result.Status);
        }

        private static bool IsMandatoryPreSubmitVerificationFailure(
            OfficialOrderUiBridgeResult result)
        {
            return
                result.HasStatus(
                    "SYMBOL_MISMATCH") ||
                result.HasStatus(
                    "INSTRUMENT_NOT_VERIFIED") ||
                result.HasStatus(
                    "ORDER_VALUES_CHANGED");
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

        private bool TryCreateGloballyNextDueOrder(
            out ScheduledSlice? scheduledSlice,
            out Order? order,
            out string errorMessage)
        {
            scheduledSlice =
                null;

            order =
                null;

            errorMessage =
                "";

            if (!_globalNextDueQueue.TryPeek(
                out scheduledSlice) ||
                scheduledSlice == null)
            {
                errorMessage =
                    "صف سراسری next-due خالی است.";

                return false;
            }

            OrderSession nextSession =
                scheduledSlice.Session;

            if (nextSession.State is
                OrderSessionState.Completed or
                OrderSessionState.Canceled or
                OrderSessionState.Failed)
            {
                errorMessage =
                    "نشست اولین slice سراسری در وضعیت پایانی قرار دارد.";

                return false;
            }

            if (!TryGetValidatedSessionOrder(
                nextSession,
                out _,
                out CreateOrderPayload? payload,
                out errorMessage) ||
                payload?.Order == null)
            {
                return false;
            }

            long nextQuantity =
                Math.Min(
                    nextSession.RemainingQuantity,
                    nextSession.MaxQuantityPerOrder);

            if (nextQuantity <= 0)
            {
                errorMessage =
                    "برای اولین slice سراسری حجم آزاد وجود ندارد.";

                return false;
            }

            order =
                CreateScheduledSliceOrder(
                    payload.Order,
                    nextQuantity);

            return true;
        }

        private async Task<OfficialOrderUiBridgeResult>
            ExecuteClockDrivenSliceAttemptAsync(
                CoreWebView2 coreWebView,
                OrderSession session,
                Order order,
                int slotNumber,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConfirmedOrderSnapshot snapshot =
                session.ConfirmedOrderSnapshot;

            if (!snapshot.HasValidFingerprint())
            {
                return new OfficialOrderUiBridgeResult
                {
                    Status =
                        "SESSION_SNAPSHOT_INVALID",

                    Reason =
                        "The session-owned confirmed order snapshot is invalid."
                };
            }

            try
            {
                OfficialOrderUiBridgeResult result =
                    await _officialUiDispatcher.DispatchAsync(
                        "scheduled-submit:" +
                        session.SessionIdDisplay +
                        ":" +
                        slotNumber,
                        "در حال ارسال " +
                        session.SymbolName +
                        "...",
                        async dispatcherCancellationToken =>
                        {
                            dispatcherCancellationToken
                                .ThrowIfCancellationRequested();

                            string nonce =
                                Guid.NewGuid()
                                    .ToString(
                                        "N");

                            try
                            {
                                string resultJson =
                                    await coreWebView.ExecuteScriptAsync(
                                        BrokerOfficialOrderUiBridge
                                            .BuildAtomicScheduledSubmitScript(
                                                _selectedBroker,
                                                order,
                                                nonce));

                                // بعد از ExecuteScriptAsync دیگر cancellation بررسی نمی‌شود.
                                // چون JavaScript ممکن است کلیک رسمی را انجام داده باشد و
                                // نتیجه باید حتماً برای حسابداری sent/in-flight پردازش شود.
                                OfficialOrderUiBridgeResult dispatchResult =
                                    OfficialOrderUiBridge.ParseResult(
                                        resultJson);

                                WriteImportant(
                                    "ATOMIC UI SLOT " +
                                    slotNumber +
                                    ": " +
                                    dispatchResult.Status);

                                return dispatchResult;
                            }
                            finally
                            {
                                await TryClearOfficialPreparedStateCoreAsync(
                                    coreWebView,
                                    nonce);
                            }
                        },
                        cancellationToken);

                if (result.HasStatus(
                    OfficialOrderUiBridge.ClickedStatus))
                {
                    await PrimeGloballyNextDueSliceAsync(
                        coreWebView);
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
        }

        private async Task PrimeGloballyNextDueSliceAsync(
            CoreWebView2 coreWebView)
        {
            try
            {
                if (!TryCreateGloballyNextDueOrder(
                    out ScheduledSlice? nextDueSlice,
                    out Order? nextOrder,
                    out string queueError) ||
                    nextDueSlice == null ||
                    nextOrder == null)
                {
                    WriteImportant(
                        "GLOBAL NEXT-DUE PRIME SKIPPED: " +
                        queueError);

                    return;
                }

                WriteImportant(
                    "GLOBAL NEXT-DUE PRIME TARGET: " +
                    nextDueSlice.Session.SessionIdDisplay +
                    " | " +
                    nextDueSlice.Session.SymbolName +
                    " @ " +
                    nextDueSlice.TargetTime.ToString(
                        "HH:mm:ss.fff",
                        CultureInfo.InvariantCulture));

                await PrimeNextScheduledOrderFormAsync(
                    coreWebView,
                    nextOrder);
            }
            catch (Exception ex)
            {
                // Prime صرفاً best-effort است. شکست آن نباید نتیجه CLICKED
                // قبلی را به failure تبدیل کند یا حسابداری sent/in-flight را بشکند.
                WriteImportant(
                    "GLOBAL NEXT-DUE PRIME ERROR: " +
                    ex.Message);
            }
        }

        private async Task PrimeNextScheduledOrderFormAsync(
            CoreWebView2 coreWebView,
            Order order)
        {
            try
            {
                await _officialUiDispatcher.DispatchAsync(
                    "prime-next-scheduled-form",
                    "در حال آماده‌سازی فرم بعدی " +
                    order.SymbolName +
                    "...",
                    async cancellationToken =>
                    {
                        await PrimeNextScheduledOrderFormCoreAsync(
                            coreWebView,
                            order,
                            cancellationToken);

                        return true;
                    });
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "NEXT FORM PRIME DISPATCH ERROR: " +
                    ex.Message);
            }
        }

        private async Task PrimeNextScheduledOrderFormCoreAsync(
            CoreWebView2 coreWebView,
            Order order,
            CancellationToken cancellationToken)
        {
            _officialUiDispatcher.VerifyAccess();

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
                    cancellationToken.ThrowIfCancellationRequested();

                    string nonce =
                        Guid.NewGuid()
                            .ToString(
                                "N");

                    string prepareJson =
                        await coreWebView.ExecuteScriptAsync(
                            BrokerOfficialOrderUiBridge.BuildPrepareScript(
                                _selectedBroker,
                                order,
                                nonce));

                    OfficialOrderUiBridgeResult prepareResult =
                        OfficialOrderUiBridge.ParseResult(
                            prepareJson);

                    if (prepareResult.HasStatus(
                        OfficialOrderUiBridge.PreparedStatus))
                    {
                        await TryClearOfficialPreparedStateCoreAsync(
                            coreWebView,
                            nonce);

                        WriteImportant(
                            "NEXT FORM PRIME: READY");

                        return;
                    }

                    await TryClearOfficialPreparedStateCoreAsync(
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
                            BrokerOfficialOrderUiBridge.BuildEnsureBuyDialogScript(
                                _selectedBroker,
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
                            await DispatchTrustedLeftClickAsync(
                                coreWebView,
                                ensureResult.ClickX,
                                ensureResult.ClickY,
                                cancellationToken);

                            trustedBuyClickRequested =
                                true;

                            WriteImportant(
                                "NEXT FORM PRIME: TRUSTED BUY CLICK REQUESTED");
                        }

                        await Task.Delay(
                            75,
                            cancellationToken);

                        continue;
                    }

                    if (ensureResult.HasStatus(
                        OfficialOrderUiBridge.DialogAlreadyOpenStatus))
                    {
                        await Task.Delay(
                            50,
                            cancellationToken);

                        continue;
                    }

                    if (ensureResult.HasStatus(
                        OfficialOrderUiBridge.SymbolSelectionRequestedStatus))
                    {
                        await Task.Delay(
                            75,
                            cancellationToken);

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
                await _officialUiDispatcher.DispatchAsync(
                    "legacy-scheduled-prepare",
                    "در حال آماده‌سازی فرم رسمی " +
                    order.SymbolName +
                    "...",
                    dispatcherCancellationToken =>
                        PrepareOfficialOrderFormCoreAsync(
                            coreWebView,
                            order,
                            preparationNonce,
                            dispatcherCancellationToken),
                    cancellationToken);

            if (!prepareResult.HasStatus(
                OfficialOrderUiBridge.PreparedStatus))
            {
                await DispatchClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce,
                    "legacy-scheduled-clear");

                WriteImportant(
                    "RESULT: RETRYABLE BEFORE POST");
                WriteImportant(
                    "REASON: " +
                    OfficialOrderUiBridge.GetUserMessage(
                        prepareResult.Status,
                        _selectedBroker.DisplayName));
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (GetFreshExchangeTime() >=
                endAt)
            {
                await DispatchClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce,
                    "legacy-scheduled-clear");

                WriteImportant(
                    "RESULT: WINDOW EXPIRED BEFORE POST");
                WriteImportant(
                    "HTTP POST: NOT SENT");

                return
                    ScheduledOrderAttemptOutcome.RetryableFailure;
            }

            if (!snapshot.HasValidFingerprint())
            {
                await DispatchClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce,
                    "legacy-scheduled-clear");

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
                    await _officialUiDispatcher.DispatchAsync(
                        "legacy-scheduled-submit",
                        "در حال ارسال " +
                        order.SymbolName +
                        "...",
                        async dispatcherCancellationToken =>
                        {
                            dispatcherCancellationToken
                                .ThrowIfCancellationRequested();

                            return await coreWebView.ExecuteScriptAsync(
                                BrokerOfficialOrderUiBridge.BuildSubmitScript(
                                    _selectedBroker,
                                    order,
                                    preparationNonce));
                        },
                        cancellationToken);

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

                await DispatchClearOfficialPreparedStateAsync(
                    coreWebView,
                    preparationNonce,
                    "legacy-scheduled-clear");

                WriteImportant(
                    "RESULT: RETRYABLE BEFORE POST");
                WriteImportant(
                    "REASON: " +
                    OfficialOrderUiBridge.GetUserMessage(
                        submitResult.Status,
                        _selectedBroker.DisplayName));
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

            await DispatchClearOfficialPreparedStateAsync(
                coreWebView,
                preparationNonce,
                "legacy-scheduled-clear");

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
            bool orderUiAvailable =
                _selectedBroker.SupportsOfficialOrderUiAutomation;

            bool orderEntryOperationAvailable =
                orderUiAvailable &&
                !_sessionCreationInProgress &&
                !_liveSubmissionInProgress;

            bool selectedSessionActive =
                SessionDataGrid.SelectedItem is ScheduledClickSession selectedSession &&
                TryGetActiveScheduledClickExecution(
                    selectedSession.SessionId,
                    out _);

            BrokerSelectionComboBox.IsEnabled =
                !isActive;

            LoginButton.IsEnabled =
                !isActive;

            StartScheduledClickButton.IsEnabled =
                orderEntryOperationAvailable;

            ScheduledClickCountTextBox.IsEnabled =
                !_sessionCreationInProgress;

            ScheduledClickStartTimeTextBox.IsEnabled =
                !_sessionCreationInProgress;

            ScheduledClickBuyRadioButton.IsEnabled =
                !_sessionCreationInProgress;

            ScheduledClickSellRadioButton.IsEnabled =
                !_sessionCreationInProgress;

            PreviewOrderButton.IsEnabled =
                orderEntryOperationAvailable;

            ReloadButton.IsEnabled =
                !isActive;

            CancelScheduledOrderButton.IsEnabled =
                selectedSessionActive;

            OrderUiDryRunTimingButton.IsEnabled =
                !isActive &&
                orderUiAvailable &&
                _currentOrderSnapshot != null;
        }

        /// <summary>
        /// فرم رسمی خرید را پیدا یا باز می‌کند و تا آماده‌شدن آن تلاش می‌کند.
        /// خروجی PREPARED فقط آمادگی محلی فرم را نشان می‌دهد و به معنی ارسال نیست.
        /// </summary>
        private Task<OfficialOrderUiBridgeResult>
            DispatchPrepareAndClearOfficialOrderFormAsync(
                CoreWebView2 coreWebView,
                Order order,
                string preparationNonce,
                string operationName,
                string displayMessage,
                CancellationToken cancellationToken = default)
        {
            return _officialUiDispatcher.DispatchAsync(
                operationName,
                displayMessage,
                async dispatcherCancellationToken =>
                {
                    try
                    {
                        return await PrepareOfficialOrderFormCoreAsync(
                            coreWebView,
                            order,
                            preparationNonce,
                            dispatcherCancellationToken);
                    }
                    finally
                    {
                        await TryClearOfficialPreparedStateCoreAsync(
                            coreWebView,
                            preparationNonce);
                    }
                },
                cancellationToken);
        }

        private Task<bool> DispatchClearOfficialPreparedStateAsync(
            CoreWebView2 coreWebView,
            string preparationNonce,
            string operationName,
            CancellationToken cancellationToken = default)
        {
            return _officialUiDispatcher.DispatchAsync(
                operationName,
                "در حال پاک‌سازی وضعیت موقت فرم رسمی...",
                async dispatcherCancellationToken =>
                {
                    dispatcherCancellationToken
                        .ThrowIfCancellationRequested();

                    await TryClearOfficialPreparedStateCoreAsync(
                        coreWebView,
                        preparationNonce);

                    return true;
                },
                cancellationToken);
        }

        private async Task<OfficialOrderUiBridgeResult>
            PrepareOfficialOrderFormCoreAsync(
                CoreWebView2 coreWebView,
                Order order,
                string preparationNonce,
                CancellationToken cancellationToken = default)
        {
            _officialUiDispatcher.VerifyAccess();

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
                        BrokerOfficialOrderUiBridge.BuildPrepareScript(
                            _selectedBroker,
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
                        BrokerOfficialOrderUiBridge.BuildEnsureBuyDialogScript(
                            _selectedBroker,
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
                        await DispatchTrustedLeftClickAsync(
                            coreWebView,
                            ensureResult.ClickX,
                            ensureResult.ClickY,
                            cancellationToken);
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

        private async Task DispatchTrustedLeftClickAsync(
            CoreWebView2 coreWebView,
            double clickX,
            double clickY,
            CancellationToken cancellationToken)
        {
            _officialUiDispatcher.VerifyAccess();

            cancellationToken.ThrowIfCancellationRequested();

            string moveJson = JsonSerializer.Serialize(new
            {
                type = "mouseMoved",
                x = clickX,
                y = clickY,
                button = "none",
                clickCount = 0
            });

            string downJson = JsonSerializer.Serialize(new
            {
                type = "mousePressed",
                x = clickX,
                y = clickY,
                button = "left",
                clickCount = 1
            });

            string upJson = JsonSerializer.Serialize(new
            {
                type = "mouseReleased",
                x = clickX,
                y = clickY,
                button = "left",
                clickCount = 1
            });

            await coreWebView.CallDevToolsProtocolMethodAsync(
                "Input.dispatchMouseEvent",
                moveJson);

            cancellationToken.ThrowIfCancellationRequested();

            await coreWebView.CallDevToolsProtocolMethodAsync(
                "Input.dispatchMouseEvent",
                downJson);

            // Once the press is dispatched, release it even if cancellation is requested.
            await coreWebView.CallDevToolsProtocolMethodAsync(
                "Input.dispatchMouseEvent",
                upJson);
        }

        private bool TryGetValidatedCurrentOrder(
            out ConfirmedOrderSnapshot? snapshot,
            out CreateOrderPayload? payload,
            out string errorMessage)
        {
            snapshot =
                _currentOrderSnapshot;

            if (!TryGetValidatedSnapshotOrder(
                snapshot,
                requireFreshConfirmation: true,
                _selectedBroker.Id,
                out payload,
                out errorMessage))
            {
                return false;
            }

            if (snapshot == null ||
                !string.Equals(
                    snapshot.BrokerId,
                    _selectedBroker.Id,
                    StringComparison.Ordinal))
            {
                payload =
                    null;

                errorMessage =
                    "Snapshot تأییدشده متعلق به کارگزاری انتخاب‌شده نیست.";

                return false;
            }

            return true;
        }

        private static bool TryGetValidatedSessionOrder(
            OrderSession session,
            out ConfirmedOrderSnapshot? snapshot,
            out CreateOrderPayload? payload,
            out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(
                session);

            snapshot =
                session.ConfirmedOrderSnapshot;

            if (!TryGetValidatedSnapshotOrder(
                snapshot,
                requireFreshConfirmation: false,
                session.BrokerId,
                out payload,
                out errorMessage) ||
                payload?.Order == null)
            {
                return false;
            }

            if (snapshot == null ||
                !string.Equals(
                snapshot.BrokerId,
                session.BrokerId,
                StringComparison.Ordinal))
            {
                payload =
                    null;

                errorMessage =
                    "کارگزاری Snapshot مستقل با هویت نشست تطبیق ندارد.";

                return false;
            }

            Order order =
                payload.Order;

            if (!string.Equals(
                order.SymbolName,
                session.SymbolName,
                StringComparison.Ordinal) ||
                !string.Equals(
                    order.SymbolIsin,
                    session.SymbolIsin,
                    StringComparison.Ordinal) ||
                order.Side != session.Side ||
                order.Price != session.Price ||
                order.Quantity != session.TotalQuantity)
            {
                payload =
                    null;

                errorMessage =
                    "مقادیر Snapshot مستقل با هویت نشست تطبیق ندارد.";

                return false;
            }

            return true;
        }

        private static bool TryGetValidatedSnapshotOrder(
            ConfirmedOrderSnapshot? snapshot,
            bool requireFreshConfirmation,
            string brokerId,
            out CreateOrderPayload? payload,
            out string errorMessage)
        {

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

            if (requireFreshConfirmation &&
                confirmedAge >
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
                OrderSubmissionValidator.ValidateForBroker(
                    payload,
                    brokerId);

            if (!validation.IsValid)
            {
                errorMessage =
                    validation.ErrorMessage;

                return false;
            }

            return true;
        }

        private async Task TryClearOfficialPreparedStateCoreAsync(
            CoreWebView2 coreWebView,
            string nonce)
        {
            _officialUiDispatcher.VerifyAccess();

            try
            {
                await coreWebView.ExecuteScriptAsync(
                    BrokerOfficialOrderUiBridge.BuildClearScript(
                        _selectedBroker,
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

        private void ClearCurrentOrderConfirmation()
        {
            _currentOrderSnapshot =
                null;

            if (_hasCurrentOrderSetup)
            {
                CurrentSetupStateTextBlock.Text =
                    "مقادیر حفظ شده‌اند؛ برای زمان‌بندی بعدی فرم رسمی را دوباره بخوانید و تأیید کنید.";

                CurrentSetupStateTextBlock.Foreground =
                    System.Windows.Media.Brushes.DarkOrange;
            }
        }

        private void ResetCurrentOrderSetupForBrokerSwitch()
        {
            _hasCurrentOrderSetup =
                false;

            CurrentSetupSymbolTextBlock.Text =
                "—";

            CurrentSetupIsinTextBlock.Text =
                "—";

            CurrentSetupPriceTextBlock.Text =
                "—";

            CurrentSetupQuantityTextBlock.Text =
                "—";

            CurrentSetupCommissionTextBlock.Text =
                "—";

            CurrentSetupTotalValueTextBlock.Text =
                "—";

            CurrentSetupStateTextBlock.Text =
                "برای کارگزاری انتخاب‌شده هنوز سفارشی خوانده و تأیید نشده است.";

            CurrentSetupStateTextBlock.Foreground =
                Brushes.SlateGray;
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

            if (!EnsureSelectedBrokerOrderUiAvailable(
                "order-ui-dry-run"))
            {
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

            if (!_selectedBroker.IsTrustedPage(
                coreWebView.Source))
            {
                SetStatus(
                    "برای Dry-Run باید صفحه رسمی " +
                    _selectedBroker.DisplayName +
                    " فعال باشد.");

                return;
            }

            if (!TryGetValidatedCurrentOrder(
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
                    await DispatchPrepareAndClearOfficialOrderFormAsync(
                        coreWebView,
                        order,
                        setupNonce,
                        "dry-run-setup",
                        "در حال آماده‌سازی Dry-Run فرم رسمی...");

                DateTimeOffset setupCompletedAt =
                    DateTimeOffset.Now;

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
            (string ResultJson, DateTimeOffset CallStartedAt, DateTimeOffset CompletedAt)
                dispatchResult =
                    await _officialUiDispatcher.DispatchAsync(
                        "dry-run-probe:" +
                        probeNumber,
                        "در حال اجرای Dry-Run فرم رسمی...",
                        async cancellationToken =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            DateTimeOffset callStartedAt =
                                DateTimeOffset.Now;

                            try
                            {
                                string resultJson =
                                    await coreWebView.ExecuteScriptAsync(
                                        BrokerOfficialOrderUiBridge.BuildPrepareScript(
                                            _selectedBroker,
                                            order,
                                            nonce));

                                DateTimeOffset completedAt =
                                    DateTimeOffset.Now;

                                return (
                                    ResultJson: resultJson,
                                    CallStartedAt: callStartedAt,
                                    CompletedAt: completedAt);
                            }
                            finally
                            {
                                await TryClearOfficialPreparedStateCoreAsync(
                                    coreWebView,
                                    nonce);
                            }
                        });

            string resultJson =
                dispatchResult.ResultJson;

            DateTimeOffset callStartedAt =
                dispatchResult.CallStartedAt;

            DateTimeOffset completedAt =
                dispatchResult.CompletedAt;

            OfficialOrderUiBridgeResult result =
                OfficialOrderUiBridge.ParseResult(
                    resultJson);

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
                _selectedBroker.TradingUrl);
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
                "MONITORING READY: " +
                _selectedBroker.DisplayName);

            if (string.Equals(
                _selectedBroker.Id,
                BrokerProfiles.EasyTraderId,
                StringComparison.Ordinal))
            {
                WriteImportant(
                    "Observed routes: same-login, startsession, core/api/v2/order");
            }
            else
            {
                WriteImportant(
                    "Pishro route discovery: STRUCTURAL PROBE ONLY");
                WriteImportant(
                    "ORDER API CONTRACT: NOT ASSUMED");
            }

            WriteImportant(
                "HTTP errors observed: 401 / 403 / 500");

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
            if (SessionDataGrid.SelectedItem is not
                ScheduledClickSession selectedSession)
            {
                SetStatus(
                    "ابتدا یک نشست فعال را از جدول انتخاب کنید.");

                return;
            }

            RequestScheduledClickCancellation(
                selectedSession,
                "لغو نشست توسط کاربر درخواست شد.");
        }

        private void CancelScheduledClickSessionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not
                ScheduledClickSession session)
            {
                SetStatus("نشست انتخاب‌شده معتبر نیست.");
                return;
            }

            RequestScheduledClickCancellation(
                session,
                "لغو نشست توسط کاربر درخواست شد.");
        }

        private bool TryGetActiveScheduledClickExecution(
            Guid sessionId,
            out ScheduledClickExecution? execution)
        {
            lock (_sessionExecutionsSyncRoot)
            {
                return _activeScheduledClickExecutions.TryGetValue(
                    sessionId,
                    out execution);
            }
        }

        private List<ScheduledClickExecution>
            GetActiveScheduledClickExecutionSnapshot()
        {
            lock (_sessionExecutionsSyncRoot)
            {
                return new List<ScheduledClickExecution>(
                    _activeScheduledClickExecutions.Values);
            }
        }

        private void RequestScheduledClickCancellation(
            ScheduledClickSession session,
            string reason)
        {
            if (!TryGetActiveScheduledClickExecution(
                session.SessionId,
                out ScheduledClickExecution? execution) ||
                execution == null ||
                !execution.RequestCancel())
            {
                SetStatus("این نشست دیگر فعال یا قابل لغو نیست.");
                return;
            }

            session.UpdateProgress(
                execution.ClickedCount,
                null,
                "لغو درخواست شد؛ کلیک در حال اجرا تعیین تکلیف می‌شود");

            WriteImportant("");
            WriteImportant(
                "SCHEDULED CLICK CANCELLATION REQUESTED: " +
                session.SessionIdDisplay);
            WriteImportant("NEW OFFICIAL ORDER CLICKS: BLOCKED");
            WriteImportant("ALREADY CLICKED ORDERS: NOT UNDONE");

            SetStatus(
                "لغو نشست " + session.SessionIdDisplay +
                " ثبت شد؛ کلیک‌های انجام‌شده بازگردانده نمی‌شوند.");

            SetScheduledOrderControls(_scheduledOrderActive);
        }

        private void SessionDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            SetScheduledOrderControls(
                _scheduledOrderActive);
        }

        private void PauseOrderSessionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryGetSessionFromActionButton(
                sender,
                out OrderSession? session) ||
                session == null ||
                !TryGetActiveSessionExecution(
                    session.SessionId,
                    out OrderSessionExecution? execution) ||
                execution == null)
            {
                SetStatus(
                    "این نشست دیگر فعال نیست.");

                return;
            }

            if (!execution.TryPause())
            {
                SetStatus(
                    "نشست در وضعیت قابل مکث نیست.");

                return;
            }

            _globalNextDueQueue.RemoveSession(
                session.SessionId);

            OrderSessionAccountingSnapshot accounting =
                execution.GetAccountingSnapshot();

            session.SetState(
                OrderSessionState.Paused,
                "به درخواست کاربر متوقف موقت شد");

            session.UpdateProgress(
                accounting.SentQuantity,
                accounting.InFlightQuantity,
                accounting.ClickedOrderCount,
                null,
                "مکث؛ dispatchهای شروع‌شده تعیین تکلیف می‌شوند");

            PulseAllSessionSchedulers();

            WriteImportant(
                "SESSION PAUSED: " +
                session.SessionIdDisplay);

            SetStatus(
                "نشست " +
                session.SessionIdDisplay +
                " متوقف موقت شد؛ نشست‌های دیگر ادامه دارند.");
        }

        private void ResumeOrderSessionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryGetSessionFromActionButton(
                sender,
                out OrderSession? session) ||
                session == null ||
                !TryGetActiveSessionExecution(
                    session.SessionId,
                    out OrderSessionExecution? execution) ||
                execution == null)
            {
                SetStatus(
                    "این نشست دیگر فعال نیست.");

                return;
            }

            DateTimeOffset now;

            try
            {
                now =
                    GetFreshExchangeTime();
            }
            catch (Exception ex)
            {
                WriteImportant(
                    "SESSION RESUME BLOCKED: " +
                    ex.Message);

                SetStatus(
                    "ساعت معتبر مرکز معاملات در دسترس نیست؛ نشست در حالت مکث باقی ماند.");

                return;
            }

            if (now >=
                session.EndTime)
            {
                execution.MarkWindowClosed();
                execution.Pulse();

                SetStatus(
                    "بازه این نشست پایان یافته است؛ زمان‌بند آن را بدون اسلات جدید می‌بندد.");

                return;
            }

            if (!execution.TryResume())
            {
                SetStatus(
                    "نشست در وضعیت قابل ادامه نیست.");

                return;
            }

            DateTimeOffset nextTarget =
                GetNextFutureSessionTarget(
                    session.StartTime,
                    now);

            if (nextTarget >=
                session.EndTime)
            {
                execution.TryPause();
                execution.MarkWindowClosed();
                execution.Pulse();

                SetStatus(
                    "اسلات آینده‌ای داخل بازه نشست باقی نمانده است؛ زمان‌بند آن را می‌بندد.");

                return;
            }

            try
            {
                _globalNextDueQueue.RemoveSession(
                    session.SessionId);

                _globalNextDueQueue.Enqueue(
                    execution.CreateNextSlice(
                        nextTarget,
                        DefaultScheduledSlicePriority));
            }
            catch (Exception ex)
            {
                execution.TryPause();

                WriteImportant(
                    "SESSION RESUME QUEUE ERROR: " +
                    ex.Message);

                SetStatus(
                    "ادامه نشست به علت خطای صف انجام نشد؛ نشست در حالت مکث باقی ماند.");

                return;
            }

            OrderSessionAccountingSnapshot accounting =
                execution.GetAccountingSnapshot();

            session.SetState(
                OrderSessionState.Waiting,
                "از اولین اسلات آینده ادامه یافت");

            session.UpdateProgress(
                accounting.SentQuantity,
                accounting.InFlightQuantity,
                accounting.ClickedOrderCount,
                nextTarget,
                "ادامه از " +
                nextTarget.ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture));

            PulseAllSessionSchedulers();

            WriteImportant(
                "SESSION RESUMED: " +
                session.SessionIdDisplay +
                " @ " +
                nextTarget.ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture));

            SetStatus(
                "نشست " +
                session.SessionIdDisplay +
                " از اولین اسلات آینده ادامه یافت.");
        }

        private void CancelOrderSessionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryGetSessionFromActionButton(
                sender,
                out OrderSession? session) ||
                session == null)
            {
                SetStatus(
                    "نشست انتخاب‌شده معتبر نیست.");

                return;
            }

            RequestSessionCancellation(
                session,
                "لغو نشست توسط کاربر درخواست شد.");
        }

        private void RequestSessionCancellation(
            OrderSession session,
            string reason)
        {
            if (!TryGetActiveSessionExecution(
                session.SessionId,
                out OrderSessionExecution? execution) ||
                execution == null ||
                !execution.RequestCancel(
                    reason))
            {
                SetStatus(
                    "این نشست دیگر فعال یا قابل لغو نیست.");

                return;
            }

            _globalNextDueQueue.RemoveSession(
                session.SessionId);

            OrderSessionAccountingSnapshot accounting =
                execution.GetAccountingSnapshot();

            session.UpdateProgress(
                accounting.SentQuantity,
                accounting.InFlightQuantity,
                accounting.ClickedOrderCount,
                null,
                "لغو درخواست شد؛ dispatchهای شروع‌شده تعیین تکلیف می‌شوند");

            PulseAllSessionSchedulers();

            WriteImportant("");
            WriteImportant(
                "SESSION CANCELLATION REQUESTED: " +
                session.SessionIdDisplay);
            WriteImportant(
                execution.ActiveDispatchCount > 0
                    ? "CURRENT UI DISPATCH: WAITING FOR DEFINITIVE RESULT"
                    : "NEW OFFICIAL SUBMIT CLICK: BLOCKED");

            SetStatus(
                "لغو نشست " +
                session.SessionIdDisplay +
                " ثبت شد؛ نشست‌های دیگر ادامه دارند.");

            SetScheduledOrderControls(
                _scheduledOrderActive);
        }

        private static bool TryGetSessionFromActionButton(
            object sender,
            out OrderSession? session)
        {
            session =
                (sender as FrameworkElement)?.DataContext as OrderSession;

            return session != null;
        }

        private static DateTimeOffset GetNextFutureSessionTarget(
            DateTimeOffset startTime,
            DateTimeOffset now)
        {
            if (now < startTime)
            {
                return startTime;
            }

            long intervalTicks =
                ScheduledOrderRetryDelay.Ticks;

            long elapsedTicks =
                (now - startTime).Ticks;

            long elapsedIntervals =
                elapsedTicks /
                intervalTicks;

            return startTime.AddTicks(
                checked(
                    (elapsedIntervals + 1) *
                    intervalTicks));
        }

        private void CancelAllActiveSessionExecutions(
            string reason,
            bool isFailure)
        {
            foreach (OrderSessionExecution execution in
                GetActiveSessionExecutionSnapshot())
            {
                if (execution.RequestCancel(
                    reason,
                    isFailure))
                {
                    _globalNextDueQueue.RemoveSession(
                        execution.Session.SessionId);
                }
            }

            foreach (ScheduledClickExecution execution in
                GetActiveScheduledClickExecutionSnapshot())
            {
                if (execution.RequestCancel())
                {
                    execution.Session.UpdateProgress(
                        execution.ClickedCount,
                        null,
                        reason);
                }
            }

            PulseAllSessionSchedulers();
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

            ClearCurrentOrderConfirmation();

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

            if (_currentOrderSnapshot != null)
            {
                ClearCurrentOrderConfirmation();
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
            CancelAllActiveSessionExecutions(
                "پردازش WebView2 از کار افتاد؛ مسیر رسمی کارگزاری دیگر قابل اتکا نیست.",
                isFailure: true);

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
                "LOGIN STATUS: " +
                _selectedBroker.DisplayName);
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
                _exchangeClockDisplayTimer.Stop();

                _applicationCancellation.Cancel();

                CancelAllActiveSessionExecutions(
                    "برنامه در حال بسته‌شدن است.",
                    isFailure: false);

                _officialUiDispatcher.StateChanged -=
                    OfficialUiDispatcher_StateChanged;

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
