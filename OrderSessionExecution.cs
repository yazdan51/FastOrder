using System;
using System.Threading;
using System.Threading.Tasks;

namespace FastOrder
{
    internal readonly record struct OrderSessionAccountingSnapshot(
        long SentQuantity,
        long InFlightQuantity,
        int ClickedOrderCount)
    {
        public long AccountedQuantity =>
            checked(
                SentQuantity +
                InFlightQuantity);
    }

    /// <summary>
    /// Owns mutable scheduler state for exactly one immutable OrderSession.
    /// All accounting transitions are lock-protected because official UI
    /// dispatches may settle independently from the coordinator loop.
    /// </summary>
    internal sealed class OrderSessionExecution : IDisposable
    {
        private readonly object _syncRoot =
            new object();

        private readonly CancellationTokenSource _cancellationSource =
            new CancellationTokenSource();

        private readonly SemaphoreSlim _wakeSignal =
            new SemaphoreSlim(
                0,
                1);

        private long _sentQuantity;
        private long _inFlightQuantity;
        private int _clickedOrderCount;
        private int _slotNumber;
        private long _nextSliceSequence = 1;
        private int _activeDispatchCount;
        private bool _isPaused;
        private bool _cancelRequested;
        private bool _windowClosed;
        private bool _isFinalized;
        private string _cancellationReason = "";
        private bool _cancellationIsFailure;

        public OrderSessionExecution(
            OrderSession session)
        {
            ArgumentNullException.ThrowIfNull(
                session);

            Session =
                session;
        }

        public OrderSession Session
        {
            get;
        }

        public CancellationToken CancellationToken =>
            _cancellationSource.Token;

        public bool IsPaused
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isPaused;
                }
            }
        }

        public bool CancelRequested
        {
            get
            {
                lock (_syncRoot)
                {
                    return _cancelRequested;
                }
            }
        }

        public bool WindowClosed
        {
            get
            {
                lock (_syncRoot)
                {
                    return _windowClosed;
                }
            }
        }

        public bool IsFinalized
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isFinalized;
                }
            }
        }

        public string CancellationReason
        {
            get
            {
                lock (_syncRoot)
                {
                    return _cancellationReason;
                }
            }
        }

        public bool CancellationIsFailure
        {
            get
            {
                lock (_syncRoot)
                {
                    return _cancellationIsFailure;
                }
            }
        }

        public int ActiveDispatchCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _activeDispatchCount;
                }
            }
        }

        public bool TryPause()
        {
            lock (_syncRoot)
            {
                if (_isFinalized ||
                    _cancelRequested ||
                    _windowClosed ||
                    _isPaused)
                {
                    return false;
                }

                _isPaused =
                    true;

                return true;
            }
        }

        public bool TryResume()
        {
            lock (_syncRoot)
            {
                if (_isFinalized ||
                    _cancelRequested ||
                    _windowClosed ||
                    !_isPaused)
                {
                    return false;
                }

                _isPaused =
                    false;

                return true;
            }
        }

        public bool RequestCancel(
            string reason,
            bool isFailure = false)
        {
            if (string.IsNullOrWhiteSpace(
                reason))
            {
                throw new ArgumentException(
                    "Cancellation reason cannot be empty.",
                    nameof(reason));
            }

            lock (_syncRoot)
            {
                if (_isFinalized ||
                    _cancelRequested)
                {
                    return false;
                }

                _cancelRequested =
                    true;

                _isPaused =
                    false;

                _cancellationReason =
                    reason.Trim();

                _cancellationIsFailure =
                    isFailure;
            }

            _cancellationSource.Cancel();
            Pulse();

            return true;
        }

        public void MarkWindowClosed()
        {
            lock (_syncRoot)
            {
                _windowClosed =
                    true;
            }
        }

        public ScheduledSlice CreateNextSlice(
            DateTimeOffset targetTime,
            int priority)
        {
            lock (_syncRoot)
            {
                if (_isFinalized ||
                    _cancelRequested ||
                    _windowClosed ||
                    _isPaused)
                {
                    throw new InvalidOperationException(
                        "A paused, canceled, closed, or finalized session cannot enqueue a slice.");
                }

                return new ScheduledSlice(
                    Session,
                    targetTime,
                    priority,
                    _nextSliceSequence++);
            }
        }

        public int NextSlotNumber()
        {
            lock (_syncRoot)
            {
                return checked(
                    ++_slotNumber);
            }
        }

        public bool TryReserve(
            long maximumQuantity,
            out long reservedQuantity)
        {
            if (maximumQuantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumQuantity));
            }

            lock (_syncRoot)
            {
                reservedQuantity =
                    0;

                if (_isFinalized ||
                    _cancelRequested ||
                    _windowClosed ||
                    _isPaused)
                {
                    return false;
                }

                long availableQuantity =
                    Session.TotalQuantity -
                    _sentQuantity -
                    _inFlightQuantity;

                if (availableQuantity <= 0)
                {
                    return false;
                }

                reservedQuantity =
                    Math.Min(
                        availableQuantity,
                        maximumQuantity);

                _inFlightQuantity =
                    checked(
                        _inFlightQuantity +
                        reservedQuantity);

                VerifyAccountingInvariant();

                return true;
            }
        }

        public void CommitClicked(
            long quantity)
        {
            lock (_syncRoot)
            {
                ValidateReservedQuantity(
                    quantity);

                _inFlightQuantity =
                    checked(
                        _inFlightQuantity -
                        quantity);

                _sentQuantity =
                    checked(
                        _sentQuantity +
                        quantity);

                _clickedOrderCount =
                    checked(
                        _clickedOrderCount +
                        1);

                VerifyAccountingInvariant();
            }
        }

        public void ReleaseReservation(
            long quantity)
        {
            lock (_syncRoot)
            {
                ValidateReservedQuantity(
                    quantity);

                _inFlightQuantity =
                    checked(
                        _inFlightQuantity -
                        quantity);

                VerifyAccountingInvariant();
            }
        }

        public bool ShouldScheduleAnotherSlice()
        {
            lock (_syncRoot)
            {
                return
                    !_isFinalized &&
                    !_cancelRequested &&
                    !_windowClosed &&
                    !_isPaused &&
                    _sentQuantity <
                        Session.TotalQuantity;
            }
        }

        public OrderSessionAccountingSnapshot GetAccountingSnapshot()
        {
            lock (_syncRoot)
            {
                return new OrderSessionAccountingSnapshot(
                    _sentQuantity,
                    _inFlightQuantity,
                    _clickedOrderCount);
            }
        }

        public void DispatchStarted()
        {
            lock (_syncRoot)
            {
                _activeDispatchCount =
                    checked(
                        _activeDispatchCount +
                        1);
            }
        }

        public void DispatchFinished()
        {
            lock (_syncRoot)
            {
                if (_activeDispatchCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Session dispatch accounting underflow.");
                }

                _activeDispatchCount--;
            }
        }

        public bool TryMarkFinalized()
        {
            lock (_syncRoot)
            {
                if (_isFinalized)
                {
                    return false;
                }

                _isFinalized =
                    true;

                return true;
            }
        }

        public void Pulse()
        {
            try
            {
                if (_wakeSignal.CurrentCount == 0)
                {
                    _wakeSignal.Release();
                }
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public Task<bool> WaitForWakeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout));
            }

            return _wakeSignal.WaitAsync(
                timeout,
                cancellationToken);
        }

        public void Dispose()
        {
            _cancellationSource.Dispose();
            _wakeSignal.Dispose();
        }

        private void ValidateReservedQuantity(
            long quantity)
        {
            if (quantity <= 0 ||
                quantity > _inFlightQuantity)
            {
                throw new InvalidOperationException(
                    "Session reservation settlement is invalid.");
            }
        }

        private void VerifyAccountingInvariant()
        {
            if (_sentQuantity < 0 ||
                _inFlightQuantity < 0 ||
                checked(
                    _sentQuantity +
                    _inFlightQuantity) >
                    Session.TotalQuantity)
            {
                throw new InvalidOperationException(
                    "Session sent/in-flight accounting invariant was violated.");
            }
        }
    }
}
