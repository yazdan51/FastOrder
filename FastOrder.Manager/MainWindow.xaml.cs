using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;

namespace FastOrder.Manager
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ManagedInstanceRow>
            _instances =
                new ObservableCollection<ManagedInstanceRow>();

        private long _nextInstanceId =
            1;

        private bool _shutdownStarted;
        private bool _shutdownCompleted;

        public MainWindow()
        {
            InitializeComponent();

            InstanceGrid.ItemsSource =
                _instances;

            AddNextInstance();
            AddNextInstance();
        }

        private void NewInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                AddNextInstance();
            }
            catch (OverflowException ex)
            {
                ShowError(
                    ex);
            }
        }

        private async void StartInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedInstanceRow row =
                GetRowForButton(
                    sender);

            await RunUiOperationAsync(
                () => row.Process.StartAsync(
                    ResolveFastOrderExecutablePath()));
        }

        private async void StopInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedInstanceRow row =
                GetRowForButton(
                    sender);

            await RunUiOperationAsync(
                row.Process.StopAsync);
        }

        private async void RestartInstanceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ManagedInstanceRow row =
                GetRowForButton(
                    sender);

            await RunUiOperationAsync(
                () => row.Process.RestartAsync(
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
                        _instances.Select(
                            row =>
                                row.Process.StartAsync(
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

                foreach (ManagedInstanceRow row in
                    _instances)
                {
                    row.Process.StateChanged -=
                        Instance_StateChanged;

                    row.Process.Dispose();
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

        private void AddNextInstance()
        {
            long instanceNumber =
                _nextInstanceId;

            _nextInstanceId =
                checked(
                    instanceNumber +
                    1);

            ManagedFastOrderProcess process =
                new ManagedFastOrderProcess(
                    instanceNumber.ToString(
                        CultureInfo.InvariantCulture));

            ManagedInstanceRow row =
                new ManagedInstanceRow(
                    process);

            process.StateChanged +=
                Instance_StateChanged;

            _instances.Add(
                row);
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
                _instances.Select(
                    row =>
                        row.Process.StopAsync()));
        }

        private ManagedInstanceRow GetRowForButton(
            object sender)
        {
            if ((sender as FrameworkElement)?.DataContext is not
                ManagedInstanceRow row ||
                !_instances.Contains(
                    row))
            {
                throw new InvalidOperationException(
                    "The managed FastOrder instance could not be identified.");
            }

            return row;
        }

        private void Instance_StateChanged(
            object? sender,
            EventArgs e)
        {
            void RefreshChangedRow()
            {
                ManagedInstanceRow? row =
                    _instances.FirstOrDefault(
                        item =>
                            ReferenceEquals(
                                item.Process,
                                sender));

                row?.Refresh();
            }

            if (Dispatcher.CheckAccess())
            {
                RefreshChangedRow();

                return;
            }

            _ = Dispatcher.BeginInvoke(
                RefreshChangedRow);
        }

        private void RefreshAllRows()
        {
            foreach (ManagedInstanceRow row in
                _instances)
            {
                row.Refresh();
            }
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

        private sealed class ManagedInstanceRow :
            INotifyPropertyChanged
        {
            private bool _isRunning;
            private string _processId =
                "—";

            public ManagedInstanceRow(
                ManagedFastOrderProcess process)
            {
                Process =
                    process;

                Refresh();
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public ManagedFastOrderProcess Process { get; }

            public string InstanceId =>
                Process.InstanceId;

            public string Status =>
                _isRunning
                    ? "Running"
                    : "Stopped";

            public string ProcessId =>
                _processId;

            public bool CanStart =>
                !_isRunning;

            public bool CanStop =>
                _isRunning;

            public bool CanRestart =>
                _isRunning;

            public void Refresh()
            {
                ManagedProcessState state =
                    Process.GetState();

                string processId =
                    state.ProcessId?.ToString(
                        CultureInfo.InvariantCulture) ??
                    "—";

                if (_isRunning == state.IsRunning &&
                    string.Equals(
                        _processId,
                        processId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _isRunning =
                    state.IsRunning;

                _processId =
                    processId;

                RaisePropertyChanged(
                    nameof(Status));

                RaisePropertyChanged(
                    nameof(ProcessId));

                RaisePropertyChanged(
                    nameof(CanStart));

                RaisePropertyChanged(
                    nameof(CanStop));

                RaisePropertyChanged(
                    nameof(CanRestart));
            }

            private void RaisePropertyChanged(
                string propertyName)
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        propertyName));
            }
        }
    }
}
