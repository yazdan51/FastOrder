using System.Diagnostics;
using System.IO;

namespace FastOrder.Manager
{
    internal readonly record struct ManagedProcessState(
        bool IsRunning,
        int? ProcessId);

    internal sealed class ManagedFastOrderProcess : IDisposable
    {
        private static readonly TimeSpan GracefulExitTimeout =
            TimeSpan.FromSeconds(
                3);

        private readonly object _stateLock =
            new object();

        private readonly SemaphoreSlim _operationLock =
            new SemaphoreSlim(
                1,
                1);

        private Process? _ownedProcess;
        private bool _disposed;

        public ManagedFastOrderProcess(
            string instanceId)
        {
            InstanceId =
                instanceId;
        }

        public string InstanceId { get; }

        public event EventHandler? StateChanged;

        public ManagedProcessState GetState()
        {
            lock (_stateLock)
            {
                if (!IsRunningCore())
                {
                    return new ManagedProcessState(
                        false,
                        null);
                }

                return new ManagedProcessState(
                    true,
                    _ownedProcess!.Id);
            }
        }

        public async Task StartAsync(
            string executablePath)
        {
            await _operationLock.WaitAsync();

            try
            {
                ThrowIfDisposed();

                StartCore(
                    executablePath);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _operationLock.WaitAsync();

            try
            {
                ThrowIfDisposed();

                await StopCoreAsync();
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task RestartAsync(
            string executablePath)
        {
            await _operationLock.WaitAsync();

            try
            {
                ThrowIfDisposed();

                await StopCoreAsync();

                StartCore(
                    executablePath);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed =
                    true;

                ReleaseOwnedProcessCore();
            }

            _operationLock.Dispose();
        }

        private void StartCore(
            string executablePath)
        {
            bool started =
                false;

            lock (_stateLock)
            {
                if (IsRunningCore())
                {
                    return;
                }

                ReleaseOwnedProcessCore();

                if (!File.Exists(
                    executablePath))
                {
                    throw new FileNotFoundException(
                        "FastOrder.exe was not found. Build FastOrder before starting managed instances.",
                        executablePath);
                }

                ProcessStartInfo startInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            executablePath,

                        WorkingDirectory =
                            Path.GetDirectoryName(
                                executablePath) ??
                            AppContext.BaseDirectory,

                        UseShellExecute =
                            false
                    };

                startInfo.ArgumentList.Add(
                    "--instance");

                startInfo.ArgumentList.Add(
                    InstanceId);

                Process process =
                    new Process
                    {
                        StartInfo =
                            startInfo,

                        EnableRaisingEvents =
                            true
                    };

                process.Exited +=
                    OwnedProcess_Exited;

                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException(
                            "FastOrder did not start.");
                    }

                    _ownedProcess =
                        process;

                    started =
                        true;
                }
                catch
                {
                    process.Exited -=
                        OwnedProcess_Exited;

                    process.Dispose();

                    throw;
                }
            }

            if (started)
            {
                RaiseStateChanged();
            }
        }

        private async Task StopCoreAsync()
        {
            Process? process;

            lock (_stateLock)
            {
                if (!IsRunningCore())
                {
                    ReleaseOwnedProcessCore();

                    process =
                        null;
                }
                else
                {
                    process =
                        _ownedProcess;
                }
            }

            if (process == null)
            {
                RaiseStateChanged();

                return;
            }

            bool exited =
                false;

            try
            {
                if (process.CloseMainWindow())
                {
                    using CancellationTokenSource timeout =
                        new CancellationTokenSource(
                            GracefulExitTimeout);

                    try
                    {
                        await process.WaitForExitAsync(
                            timeout.Token);

                        exited =
                            true;
                    }
                    catch (OperationCanceledException)
                        when (timeout.IsCancellationRequested)
                    {
                    }
                }

                if (!exited &&
                    !process.HasExited)
                {
                    process.Kill(
                        entireProcessTree:
                            false);

                    await process.WaitForExitAsync();
                }
            }
            finally
            {
                bool processExited;

                try
                {
                    processExited =
                        process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    processExited =
                        true;
                }

                lock (_stateLock)
                {
                    if (processExited &&
                        ReferenceEquals(
                        _ownedProcess,
                        process))
                    {
                        ReleaseOwnedProcessCore();
                    }
                }

                RaiseStateChanged();
            }
        }

        private bool IsRunningCore()
        {
            if (_ownedProcess == null)
            {
                return false;
            }

            try
            {
                return !_ownedProcess.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void ReleaseOwnedProcessCore()
        {
            if (_ownedProcess == null)
            {
                return;
            }

            _ownedProcess.Exited -=
                OwnedProcess_Exited;

            _ownedProcess.Dispose();

            _ownedProcess =
                null;
        }

        private void OwnedProcess_Exited(
            object? sender,
            EventArgs e)
        {
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(
                this,
                EventArgs.Empty);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);
        }
    }
}
