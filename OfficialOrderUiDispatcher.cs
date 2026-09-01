using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FastOrder
{
    internal sealed class OfficialUiDispatcherStateChangedEventArgs : EventArgs
    {
        public OfficialUiDispatcherStateChangedEventArgs(
            bool isBusy,
            string operationName,
            string displayMessage,
            TimeSpan queueDelay,
            TimeSpan operationDuration,
            string failureType)
        {
            IsBusy =
                isBusy;

            OperationName =
                operationName;

            DisplayMessage =
                displayMessage;

            QueueDelay =
                queueDelay;

            OperationDuration =
                operationDuration;

            FailureType =
                failureType;
        }

        public bool IsBusy
        {
            get;
        }

        public string OperationName
        {
            get;
        }

        public string DisplayMessage
        {
            get;
        }

        public TimeSpan QueueDelay
        {
            get;
        }

        public TimeSpan OperationDuration
        {
            get;
        }

        public string FailureType
        {
            get;
        }
    }

    /// <summary>
    /// Serializes short operations against the single official EasyTrader DOM.
    /// A failed operation always releases the gate, so later sessions are not poisoned.
    /// </summary>
    internal sealed class OfficialOrderUiDispatcher
    {
        private readonly SemaphoreSlim _gate =
            new SemaphoreSlim(
                1,
                1);

        private readonly AsyncLocal<bool> _ownsGate =
            new AsyncLocal<bool>();

        private int _pendingOperationCount;

        public event EventHandler<OfficialUiDispatcherStateChangedEventArgs>?
            StateChanged;

        public int PendingOperationCount =>
            Math.Max(
                0,
                Volatile.Read(
                    ref _pendingOperationCount));

        public void VerifyAccess()
        {
            if (!_ownsGate.Value)
            {
                throw new InvalidOperationException(
                    "Official EasyTrader DOM access requires the central UI dispatcher.");
            }
        }

        public async Task<T> DispatchAsync<T>(
            string operationName,
            string displayMessage,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(
                operationName))
            {
                throw new ArgumentException(
                    "Official UI operation name cannot be empty.",
                    nameof(operationName));
            }

            if (string.IsNullOrWhiteSpace(
                displayMessage))
            {
                throw new ArgumentException(
                    "Official UI display message cannot be empty.",
                    nameof(displayMessage));
            }

            ArgumentNullException.ThrowIfNull(
                operation);

            if (_ownsGate.Value)
            {
                throw new InvalidOperationException(
                    "Nested Official UI dispatcher operations are not allowed.");
            }

            long queuedTimestamp =
                Stopwatch.GetTimestamp();

            Interlocked.Increment(
                ref _pendingOperationCount);

            try
            {
                await _gate.WaitAsync(
                    cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(
                    ref _pendingOperationCount);
            }

            TimeSpan queueDelay =
                Stopwatch.GetElapsedTime(
                    queuedTimestamp);

            long operationStartedTimestamp =
                Stopwatch.GetTimestamp();

            string failureType =
                "";

            _ownsGate.Value =
                true;

            PublishState(
                new OfficialUiDispatcherStateChangedEventArgs(
                    true,
                    operationName,
                    displayMessage,
                    queueDelay,
                    TimeSpan.Zero,
                    ""));

            try
            {
                return await operation(
                    cancellationToken);
            }
            catch (Exception ex)
            {
                failureType =
                    ex.GetType().Name;

                throw;
            }
            finally
            {
                TimeSpan operationDuration =
                    Stopwatch.GetElapsedTime(
                        operationStartedTimestamp);

                _ownsGate.Value =
                    false;

                try
                {
                    PublishState(
                        new OfficialUiDispatcherStateChangedEventArgs(
                            false,
                            operationName,
                            displayMessage,
                            queueDelay,
                            operationDuration,
                            failureType));
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        private void PublishState(
            OfficialUiDispatcherStateChangedEventArgs eventArgs)
        {
            try
            {
                StateChanged?.Invoke(
                    this,
                    eventArgs);
            }
            catch
            {
                // UI diagnostics must never block or poison the shared dispatcher.
            }
        }
    }
}
