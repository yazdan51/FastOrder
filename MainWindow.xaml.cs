
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
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
        private static readonly TimeSpan ScheduledOrderRetryDelay =
            TimeSpan.FromSeconds(
                1);

        private static readonly TimeSpan ExchangeClockRefreshInterval =
            TimeSpan.FromSeconds(
                3);

        private const string ScheduledClickTimeFormat =
            "HH:mm:ss.fff";

        private const double LayoutSplitterThickness =
            6;

        private bool _webViewReady = false;

        private bool _monitoringEnabled = false;

        // فقط نمایش Live Log متوقف می‌شود.
        // Monitoring شبکه همچنان ادامه دارد.
        private bool _pauseLog = false;
        private bool _authorizationHeaderObserved = false;
        private bool _successfulSessionResponseObserved = false;
        private bool _successfulProtectedApiResponseObserved = false;
        private readonly ObservableCollection<ScheduledClickSession>
            _scheduledClickSessions =
                new ObservableCollection<ScheduledClickSession>();
        private long _nextScheduledClickSessionSequence = 0;
        private readonly object _sessionExecutionsSyncRoot =
            new object();
        private readonly Dictionary<Guid, ScheduledClickExecution>
            _activeScheduledClickExecutions =
                new Dictionary<Guid, ScheduledClickExecution>();
        private bool _sessionCreationInProgress = false;
        private readonly object _scheduledClockRefreshSyncRoot =
            new object();
        private CancellationTokenSource? _scheduledClockRefreshCancellation;
        private Task? _scheduledClockRefreshTask;
        private bool _scheduledOrderActive = false;

        private readonly ExchangeClock _exchangeClock =
            new ExchangeClock();

        private readonly OfficialOrderUiDispatcher _officialUiDispatcher =
            new OfficialOrderUiDispatcher();

        private readonly CancellationTokenSource _applicationCancellation =
            new CancellationTokenSource();

        private readonly DispatcherTimer _exchangeClockDisplayTimer;

        private Task? _exchangeClockMaintenanceTask;

        private WindowState _lastNonMinimizedWindowState =
            WindowState.Normal;

        private bool _webViewTimingTestActive = false;

        private BrokerProfile _selectedBroker =
            BrokerProfiles.EasyTrader;

        private double _controlPanelMinimumWidth;
        private double _controlPanelMaximumWidth;
        private double _brokerWebViewMinimumWidth;
        private double _mainAreaMinimumHeight;
        private double _sessionAreaMinimumHeight;
        private double _sessionAreaMaximumHeight;
        private double _logAreaMinimumHeight;
        private double _logAreaMaximumHeight;
        private double _liveNetworkLogMinimumWidth;
        private double _importantApiMinimumWidth;
        private double _expandedControlPanelWidth;
        private double _expandedSessionAreaHeight;
        private double _expandedLogAreaHeight;
        private double _liveNetworkLogWidthShare =
            1.65 / 2.65;

        public MainWindow()
        {
            InitializeComponent();

            DateTime initialScheduledClickStartTime =
                DateTime.Now.AddMinutes(
                    1);

            ScheduledClickStartTimeTextBox.Text =
                new TimeOnly(
                    initialScheduledClickStartTime.Hour,
                    initialScheduledClickStartTime.Minute,
                    0)
                .ToString(
                    ScheduledClickTimeFormat,
                    CultureInfo.InvariantCulture);

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

            InitializeCollapsiblePanelLayout();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private void ScheduledClickSideRadioButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DateTimeOffset currentTime =
                DateTimeOffset.Now;

            if (_exchangeClock.TryGetReading(
                TimeSpan.MaxValue,
                out ExchangeClockReading reading))
            {
                currentTime =
                    reading.Now;
            }

            DateTimeOffset refreshedStartTime =
                currentTime.AddMinutes(
                    1);

            ScheduledClickStartTimeTextBox.Text =
                new TimeOnly(
                    refreshedStartTime.Hour,
                    refreshedStartTime.Minute,
                    0)
                .ToString(
                    ScheduledClickTimeFormat,
                    CultureInfo.InvariantCulture);
        }

        private void BrokerSelectionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (BrokerSelectionComboBox.SelectedItem is not BrokerProfile selectedBroker)
            {
                return;
            }

            if (_scheduledOrderActive)
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

        private void InitializeCollapsiblePanelLayout()
        {
            _controlPanelMinimumWidth =
                ControlPanelColumn.MinWidth;

            _controlPanelMaximumWidth =
                ControlPanelColumn.MaxWidth;

            _brokerWebViewMinimumWidth =
                BrokerWebViewColumn.MinWidth;

            _mainAreaMinimumHeight =
                MainAreaRow.MinHeight;

            _sessionAreaMinimumHeight =
                SessionAreaRow.MinHeight;

            _sessionAreaMaximumHeight =
                SessionAreaRow.MaxHeight;

            _logAreaMinimumHeight =
                LogAreaRow.MinHeight;

            _logAreaMaximumHeight =
                LogAreaRow.MaxHeight;

            _liveNetworkLogMinimumWidth =
                LiveNetworkLogColumn.MinWidth;

            _importantApiMinimumWidth =
                ImportantApiColumn.MinWidth;

            _expandedControlPanelWidth =
                NormalizeLayoutDimension(
                    ControlPanelColumn.Width.Value,
                    300,
                    _controlPanelMinimumWidth,
                    _controlPanelMaximumWidth);

            _expandedSessionAreaHeight =
                NormalizeLayoutDimension(
                    SessionAreaRow.Height.Value,
                    150,
                    _sessionAreaMinimumHeight,
                    _sessionAreaMaximumHeight);

            _expandedLogAreaHeight =
                NormalizeLayoutDimension(
                    LogAreaRow.Height.Value,
                    190,
                    _logAreaMinimumHeight,
                    _logAreaMaximumHeight);

            double logWidthStarTotal =
                LiveNetworkLogColumn.Width.Value +
                ImportantApiColumn.Width.Value;

            if (LiveNetworkLogColumn.Width.IsStar &&
                ImportantApiColumn.Width.IsStar &&
                logWidthStarTotal > 0)
            {
                _liveNetworkLogWidthShare =
                    LiveNetworkLogColumn.Width.Value /
                    logWidthStarTotal;
            }

            Expander[] collapsiblePanels =
            {
                ControlPanelExpander,
                BrokerWebViewExpander,
                SessionPanelExpander,
                LiveNetworkLogExpander,
                ImportantApiExpander
            };

            foreach (Expander panel in collapsiblePanels)
            {
                panel.Expanded +=
                    CollapsiblePanel_ExpansionChanged;

                panel.Collapsed +=
                    CollapsiblePanel_ExpansionChanged;
            }

            UpdateCollapsiblePanelLayout();
        }

        private void CollapsiblePanel_ExpansionChanged(
            object sender,
            RoutedEventArgs e)
        {
            if (!ReferenceEquals(
                    e.OriginalSource,
                    sender) ||
                sender is not Expander panel)
            {
                return;
            }

            if (!panel.IsExpanded)
            {
                CaptureExpandedPanelDimensions(
                    panel);
            }

            UpdateCollapsiblePanelLayout();
        }

        private void CaptureExpandedPanelDimensions(
            Expander collapsingPanel)
        {
            bool mainPanelWidthCanBeCaptured =
                ControlPanelGridSplitter.Visibility ==
                Visibility.Visible &&
                ControlPanelColumn.ActualWidth > 0;

            if (mainPanelWidthCanBeCaptured &&
                (ReferenceEquals(
                    collapsingPanel,
                    ControlPanelExpander) ||
                 ReferenceEquals(
                    collapsingPanel,
                    BrokerWebViewExpander)))
            {
                _expandedControlPanelWidth =
                    NormalizeLayoutDimension(
                        ControlPanelColumn.ActualWidth,
                        _expandedControlPanelWidth,
                        _controlPanelMinimumWidth,
                        _controlPanelMaximumWidth);
            }

            if (ReferenceEquals(
                    collapsingPanel,
                    SessionPanelExpander) &&
                SessionAreaRow.Height.IsAbsolute &&
                SessionAreaRow.ActualHeight > 0)
            {
                _expandedSessionAreaHeight =
                    NormalizeLayoutDimension(
                        SessionAreaRow.ActualHeight,
                        _expandedSessionAreaHeight,
                        _sessionAreaMinimumHeight,
                        _sessionAreaMaximumHeight);
            }

            bool isLogPanel =
                ReferenceEquals(
                    collapsingPanel,
                    LiveNetworkLogExpander) ||
                ReferenceEquals(
                    collapsingPanel,
                    ImportantApiExpander);

            if (!isLogPanel)
            {
                return;
            }

            if (LogAreaRow.Height.IsAbsolute &&
                LogAreaRow.ActualHeight > 0)
            {
                _expandedLogAreaHeight =
                    NormalizeLayoutDimension(
                        LogAreaRow.ActualHeight,
                        _expandedLogAreaHeight,
                        _logAreaMinimumHeight,
                        _logAreaMaximumHeight);
            }

            if (LogPanelGridSplitter.Visibility !=
                Visibility.Visible)
            {
                return;
            }

            double combinedLogWidth =
                LiveNetworkLogColumn.ActualWidth +
                ImportantApiColumn.ActualWidth;

            if (combinedLogWidth > 0)
            {
                _liveNetworkLogWidthShare =
                    Math.Clamp(
                        LiveNetworkLogColumn.ActualWidth /
                        combinedLogWidth,
                        0.2,
                        0.8);
            }
        }

        private void UpdateCollapsiblePanelLayout()
        {
            UpdateMainPanelColumns();
            UpdateLogPanelColumns();
            UpdateMainPanelRows();
        }

        private void UpdateMainPanelColumns()
        {
            bool controlPanelExpanded =
                ControlPanelExpander.IsExpanded;

            bool brokerWebViewExpanded =
                BrokerWebViewExpander.IsExpanded;

            ControlPanelColumn.MinWidth =
                controlPanelExpanded
                    ? _controlPanelMinimumWidth
                    : 0;

            BrokerWebViewColumn.MinWidth =
                brokerWebViewExpanded
                    ? _brokerWebViewMinimumWidth
                    : 0;

            if (controlPanelExpanded &&
                brokerWebViewExpanded)
            {
                ControlPanelColumn.MaxWidth =
                    _controlPanelMaximumWidth;

                ControlPanelColumn.Width =
                    new GridLength(
                        _expandedControlPanelWidth);

                BrokerWebViewColumn.Width =
                    new GridLength(
                        1,
                        GridUnitType.Star);

                SetColumnSplitterVisibility(
                    MainAreaSplitterColumn,
                    ControlPanelGridSplitter,
                    true);

                return;
            }

            SetColumnSplitterVisibility(
                MainAreaSplitterColumn,
                ControlPanelGridSplitter,
                false);

            if (controlPanelExpanded)
            {
                ControlPanelColumn.MaxWidth =
                    double.PositiveInfinity;

                ControlPanelColumn.Width =
                    new GridLength(
                        1,
                        GridUnitType.Star);

                BrokerWebViewColumn.Width =
                    GridLength.Auto;

                return;
            }

            ControlPanelColumn.MaxWidth =
                double.PositiveInfinity;

            ControlPanelColumn.Width =
                GridLength.Auto;

            BrokerWebViewColumn.Width =
                brokerWebViewExpanded
                    ? new GridLength(
                        1,
                        GridUnitType.Star)
                    : GridLength.Auto;
        }

        private void UpdateLogPanelColumns()
        {
            bool liveNetworkLogExpanded =
                LiveNetworkLogExpander.IsExpanded;

            bool importantApiExpanded =
                ImportantApiExpander.IsExpanded;

            LiveNetworkLogColumn.MinWidth =
                liveNetworkLogExpanded
                    ? _liveNetworkLogMinimumWidth
                    : 0;

            ImportantApiColumn.MinWidth =
                importantApiExpanded
                    ? _importantApiMinimumWidth
                    : 0;

            if (liveNetworkLogExpanded &&
                importantApiExpanded)
            {
                LiveNetworkLogColumn.Width =
                    new GridLength(
                        _liveNetworkLogWidthShare,
                        GridUnitType.Star);

                ImportantApiColumn.Width =
                    new GridLength(
                        1 - _liveNetworkLogWidthShare,
                        GridUnitType.Star);

                SetColumnSplitterVisibility(
                    LogPanelSplitterColumn,
                    LogPanelGridSplitter,
                    true);

                return;
            }

            SetColumnSplitterVisibility(
                LogPanelSplitterColumn,
                LogPanelGridSplitter,
                false);

            LiveNetworkLogColumn.Width =
                liveNetworkLogExpanded
                    ? new GridLength(
                        1,
                        GridUnitType.Star)
                    : GridLength.Auto;

            ImportantApiColumn.Width =
                importantApiExpanded
                    ? new GridLength(
                        1,
                        GridUnitType.Star)
                    : GridLength.Auto;
        }

        private void UpdateMainPanelRows()
        {
            bool mainAreaExpanded =
                ControlPanelExpander.IsExpanded ||
                BrokerWebViewExpander.IsExpanded;

            bool sessionAreaExpanded =
                SessionPanelExpander.IsExpanded;

            bool logAreaExpanded =
                LiveNetworkLogExpander.IsExpanded ||
                ImportantApiExpander.IsExpanded;

            if (mainAreaExpanded)
            {
                SetExpandedRow(
                    MainAreaRow,
                    _mainAreaMinimumHeight,
                    double.PositiveInfinity,
                    _mainAreaMinimumHeight,
                    true);

                SetPanelRow(
                    SessionAreaRow,
                    sessionAreaExpanded,
                    _sessionAreaMinimumHeight,
                    _sessionAreaMaximumHeight,
                    _expandedSessionAreaHeight,
                    false);

                SetPanelRow(
                    LogAreaRow,
                    logAreaExpanded,
                    _logAreaMinimumHeight,
                    _logAreaMaximumHeight,
                    _expandedLogAreaHeight,
                    false);
            }
            else if (sessionAreaExpanded)
            {
                SetCollapsedRow(
                    MainAreaRow);

                SetExpandedRow(
                    SessionAreaRow,
                    _sessionAreaMinimumHeight,
                    _sessionAreaMaximumHeight,
                    _expandedSessionAreaHeight,
                    true);

                SetPanelRow(
                    LogAreaRow,
                    logAreaExpanded,
                    _logAreaMinimumHeight,
                    _logAreaMaximumHeight,
                    _expandedLogAreaHeight,
                    false);
            }
            else if (logAreaExpanded)
            {
                SetCollapsedRow(
                    MainAreaRow);

                SetCollapsedRow(
                    SessionAreaRow);

                SetExpandedRow(
                    LogAreaRow,
                    _logAreaMinimumHeight,
                    _logAreaMaximumHeight,
                    _expandedLogAreaHeight,
                    true);
            }
            else
            {
                SetCollapsedRow(
                    MainAreaRow);

                SetCollapsedRow(
                    SessionAreaRow);

                SetCollapsedRow(
                    LogAreaRow);
            }

            SetRowSplitterVisibility(
                MainSessionSplitterRow,
                MainSessionGridSplitter,
                mainAreaExpanded &&
                sessionAreaExpanded);

            SetRowSplitterVisibility(
                SessionLogSplitterRow,
                SessionLogGridSplitter,
                sessionAreaExpanded &&
                logAreaExpanded);
        }

        private static void SetPanelRow(
            RowDefinition row,
            bool isExpanded,
            double minimumHeight,
            double maximumHeight,
            double preferredHeight,
            bool fillsAvailableSpace)
        {
            if (isExpanded)
            {
                SetExpandedRow(
                    row,
                    minimumHeight,
                    maximumHeight,
                    preferredHeight,
                    fillsAvailableSpace);
            }
            else
            {
                SetCollapsedRow(
                    row);
            }
        }

        private static void SetExpandedRow(
            RowDefinition row,
            double minimumHeight,
            double maximumHeight,
            double preferredHeight,
            bool fillsAvailableSpace)
        {
            row.MinHeight =
                minimumHeight;

            row.MaxHeight =
                fillsAvailableSpace
                    ? double.PositiveInfinity
                    : maximumHeight;

            row.Height =
                fillsAvailableSpace
                    ? new GridLength(
                        1,
                        GridUnitType.Star)
                    : new GridLength(
                        NormalizeLayoutDimension(
                            preferredHeight,
                            minimumHeight,
                            minimumHeight,
                            maximumHeight));
        }

        private static void SetCollapsedRow(
            RowDefinition row)
        {
            row.MinHeight =
                0;

            row.MaxHeight =
                double.PositiveInfinity;

            row.Height =
                GridLength.Auto;
        }

        private static void SetColumnSplitterVisibility(
            ColumnDefinition splitterColumn,
            GridSplitter splitter,
            bool isVisible)
        {
            splitterColumn.Width =
                new GridLength(
                    isVisible
                        ? LayoutSplitterThickness
                        : 0);

            splitter.Visibility =
                isVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private static void SetRowSplitterVisibility(
            RowDefinition splitterRow,
            GridSplitter splitter,
            bool isVisible)
        {
            splitterRow.Height =
                new GridLength(
                    isVisible
                        ? LayoutSplitterThickness
                        : 0);

            splitter.Visibility =
                isVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

                double controlPanelWidthToSave =
                    ControlPanelExpander.IsExpanded &&
                    BrokerWebViewExpander.IsExpanded
                        ? ControlPanelColumn.ActualWidth
                        : _expandedControlPanelWidth;

                settings.ControlPanelWidth =
                    NormalizeLayoutDimension(
                        controlPanelWidthToSave,
                        _expandedControlPanelWidth,
                        _controlPanelMinimumWidth,
                        _controlPanelMaximumWidth);

                double logAreaHeightToSave =
                    LogAreaRow.Height.IsAbsolute &&
                    (LiveNetworkLogExpander.IsExpanded ||
                     ImportantApiExpander.IsExpanded)
                        ? LogAreaRow.ActualHeight
                        : _expandedLogAreaHeight;

                settings.LogAreaHeight =
                    NormalizeLayoutDimension(
                        logAreaHeightToSave,
                        _expandedLogAreaHeight,
                        _logAreaMinimumHeight,
                        _logAreaMaximumHeight);

                settings.SelectedBrokerId =
                    _selectedBroker.Id;

                settings.Save();
            }
            catch
            {
                // Layout persistence must never prevent a safe application shutdown.
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
                    string.Empty;

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
                    CultureInfo.InvariantCulture);

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

            if (_sessionCreationInProgress)
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
                ScheduledClickTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly requestedStartTime))
            {
                SetStatus(
                    "زمان شروع را دقیقاً با قالب HH:mm:ss.fff وارد کنید.");
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
                        ScheduledClickTimeFormat,
                        CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    "آخرین اسلات: " + lastTarget.ToString(
                        ScheduledClickTimeFormat,
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

        private void RefreshScheduledOrderActivityState()
        {
            lock (_sessionExecutionsSyncRoot)
            {
                _scheduledOrderActive =
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
                if (_activeScheduledClickExecutions.Count > 0)
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

        private void SetScheduledOrderControls(
            bool isActive)
        {
            bool orderUiAvailable =
                _selectedBroker.SupportsOfficialOrderUiAutomation;

            bool orderEntryOperationAvailable =
                orderUiAvailable &&
                !_sessionCreationInProgress;

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

            ReloadButton.IsEnabled =
                !isActive;

            CancelScheduledOrderButton.IsEnabled =
                selectedSessionActive;
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

            if (_scheduledOrderActive)
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

        private void CancelAllActiveSessionExecutions(
            string reason)
        {
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
            if (_scheduledOrderActive)
            {
                SetStatus(
                    "تا پایان زمان‌بندی یا مشخص‌شدن نتیجه ارسال، بارگذاری مجدد متوقف است.");

                return;
            }

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
            if (_scheduledOrderActive)
            {
                e.Cancel =
                    true;

                SetStatus(
                    "ناوبری تا پایان زمان‌بندی یا مشخص‌شدن نتیجه ارسال متوقف شد.");

                return;
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
                "پردازش WebView2 از کار افتاد؛ مسیر رسمی کارگزاری دیگر قابل اتکا نیست.");

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
                    "برنامه در حال بسته‌شدن است.");

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
