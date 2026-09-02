using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FastOrder.Manager
{
    public partial class MainWindow : Window
    {
        private readonly IReadOnlyDictionary<string, ManagedFastOrderProcess>
            _instances;

        private bool _shutdownStarted;
        private bool _shutdownCompleted;

        public MainWindow()
        {
            InitializeComponent();

            ManagedFastOrderProcess instance1 =
                new ManagedFastOrderProcess(
                    "1");

            ManagedFastOrderProcess instance2 =
                new ManagedFastOrderProcess(
                    "2");

            _instances =
                new Dictionary<string, ManagedFastOrderProcess>(
                    StringComparer.Ordinal)
                {
                    [instance1.InstanceId] =
                        instance1,

                    [instance2.InstanceId] =
                        instance2
                };

            foreach (ManagedFastOrderProcess instance in
                _instances.Values)
            {
                instance.StateChanged +=
                    Instance_StateChanged;
            }

            RefreshAllRows();
        }

        private async void StartInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedFastOrderProcess instance =
                GetInstanceForButton(
                    sender);

            await RunUiOperationAsync(
                () => instance.StartAsync(
                    ResolveFastOrderExecutablePath()));
        }

        private async void StopInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedFastOrderProcess instance =
                GetInstanceForButton(
                    sender);

            await RunUiOperationAsync(
                instance.StopAsync);
        }

        private async void RestartInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedFastOrderProcess instance =
                GetInstanceForButton(
                    sender);

            await RunUiOperationAsync(
                () => instance.RestartAsync(
                    ResolveFastOrderExecutablePath()));
        }

        private async void StartAllButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RunUiOperationAsync(
                async () =>
                {
                    string executablePath =
                        ResolveFastOrderExecutablePath();

                    await Task.WhenAll(
                        _instances.Values.Select(
                            instance =>
                                instance.StartAsync(
                                    executablePath)));
                });
        }

        private async void StopAllButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RunUiOperationAsync(
                StopAllAsync);
        }

        private async void MainWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (_shutdownCompleted)
            {
                return;
            }

            e.Cancel =
                true;

            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted =
                true;

            ManagerControls.IsEnabled =
                false;

            try
            {
                await StopAllAsync();

                foreach (ManagedFastOrderProcess instance in
                    _instances.Values)
                {
                    instance.StateChanged -=
                        Instance_StateChanged;

                    instance.Dispose();
                }

                _shutdownCompleted =
                    true;

                Close();
            }
            catch (Exception ex)
            {
                _shutdownStarted =
                    false;

                ManagerControls.IsEnabled =
                    true;

                ShowError(
                    ex);
            }
        }

        private async Task RunUiOperationAsync(
            Func<Task> operation)
        {
            ManagerControls.IsEnabled =
                false;

            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                ShowError(
                    ex);
            }
            finally
            {
                if (!_shutdownStarted)
                {
                    ManagerControls.IsEnabled =
                        true;
                }

                RefreshAllRows();
            }
        }

        private Task StopAllAsync()
        {
            return Task.WhenAll(
                _instances.Values.Select(
                    instance =>
                        instance.StopAsync()));
        }

        private ManagedFastOrderProcess GetInstanceForButton(
            object sender)
        {
            string? instanceId =
                (sender as FrameworkElement)?.Tag as string;

            if (instanceId == null ||
                !_instances.TryGetValue(
                    instanceId,
                    out ManagedFastOrderProcess? instance))
            {
                throw new InvalidOperationException(
                    "The managed FastOrder instance could not be identified.");
            }

            return instance;
        }

        private void Instance_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (Dispatcher.CheckAccess())
            {
                RefreshAllRows();

                return;
            }

            _ = Dispatcher.BeginInvoke(
                RefreshAllRows);
        }

        private void RefreshAllRows()
        {
            RefreshRow(
                _instances["1"],
                Instance1StatusText,
                Instance1PidText,
                Instance1StartButton,
                Instance1StopButton,
                Instance1RestartButton);

            RefreshRow(
                _instances["2"],
                Instance2StatusText,
                Instance2PidText,
                Instance2StartButton,
                Instance2StopButton,
                Instance2RestartButton);
        }

        private static void RefreshRow(
            ManagedFastOrderProcess instance,
            TextBlock statusText,
            TextBlock processIdText,
            Button startButton,
            Button stopButton,
            Button restartButton)
        {
            ManagedProcessState state =
                instance.GetState();

            statusText.Text =
                state.IsRunning
                    ? "Running"
                    : "Stopped";

            processIdText.Text =
                state.ProcessId?.ToString() ??
                "—";

            startButton.IsEnabled =
                !state.IsRunning;

            stopButton.IsEnabled =
                state.IsRunning;

            restartButton.IsEnabled =
                state.IsRunning;
        }

        private static string ResolveFastOrderExecutablePath()
        {
            string alongsideManager =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "FastOrder.exe");

            if (File.Exists(
                alongsideManager))
            {
                return alongsideManager;
            }

            DirectoryInfo targetFrameworkDirectory =
                new DirectoryInfo(
                    AppContext.BaseDirectory);

            DirectoryInfo? configurationDirectory =
                targetFrameworkDirectory.Parent;

            DirectoryInfo? binDirectory =
                configurationDirectory?.Parent;

            DirectoryInfo? managerProjectDirectory =
                binDirectory?.Parent;

            DirectoryInfo? repositoryDirectory =
                managerProjectDirectory?.Parent;

            if (configurationDirectory != null &&
                repositoryDirectory != null)
            {
                string repositoryBuild =
                    Path.Combine(
                        repositoryDirectory.FullName,
                        "bin",
                        configurationDirectory.Name,
                        targetFrameworkDirectory.Name,
                        "FastOrder.exe");

                if (File.Exists(
                    repositoryBuild))
                {
                    return repositoryBuild;
                }
            }

            throw new FileNotFoundException(
                "FastOrder.exe was not found beside the Manager or in the matching repository build output.");
        }

        private void ShowError(
            Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "FastOrder Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
