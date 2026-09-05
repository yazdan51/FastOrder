using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FastOrder
{
    public enum ScheduledClickSide
    {
        Buy = 0,
        Sell = 1
    }

    public sealed class ScheduledClickSession : INotifyPropertyChanged
    {
        private OrderSessionState _state;
        private int _clickedCount;
        private DateTimeOffset? _nextDueAt;
        private string _lastStatus;

        internal ScheduledClickSession(
            long creationSequence,
            BrokerProfile broker,
            ScheduledClickSide side,
            int totalClickCount,
            DateTimeOffset startTime)
        {
            ArgumentNullException.ThrowIfNull(broker);

            if (creationSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(creationSequence));
            }

            if (totalClickCount is < 1 or > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(totalClickCount));
            }

            if (side is not
                (ScheduledClickSide.Buy or ScheduledClickSide.Sell))
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }

            SessionId = Guid.NewGuid();
            CreationSequence = creationSequence;
            BrokerId = broker.Id;
            BrokerDisplayName = broker.DisplayName;
            Side = side;
            TotalClickCount = totalClickCount;
            StartTime = startTime;
            _state = OrderSessionState.Confirmed;
            _nextDueAt = startTime;
            _lastStatus = "تأیید شده";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid SessionId { get; }

        public string SessionIdDisplay =>
            SessionId.ToString("N", CultureInfo.InvariantCulture)[..8]
                .ToUpperInvariant();

        public long CreationSequence { get; }

        public string BrokerId { get; }

        public string BrokerDisplayName { get; }

        public ScheduledClickSide Side { get; }

        public string SideDisplay =>
            Side == ScheduledClickSide.Buy
                ? "خرید"
                : "فروش";

        public int TotalClickCount { get; }

        public string TotalClickCountDisplay =>
            TotalClickCount.ToString(CultureInfo.InvariantCulture);

        public int ClickedCount
        {
            get => _clickedCount;
            private set
            {
                if (_clickedCount == value)
                {
                    return;
                }

                _clickedCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClickedCountDisplay));
                OnPropertyChanged(nameof(RemainingClickCount));
                OnPropertyChanged(nameof(RemainingClickCountDisplay));
            }
        }

        public string ClickedCountDisplay =>
            ClickedCount.ToString(CultureInfo.InvariantCulture);

        public int RemainingClickCount =>
            Math.Max(0, TotalClickCount - ClickedCount);

        public string RemainingClickCountDisplay =>
            RemainingClickCount.ToString(CultureInfo.InvariantCulture);

        public DateTimeOffset StartTime { get; }

        public string StartTimeDisplay =>
            StartTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        public OrderSessionState State
        {
            get => _state;
            private set
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateDisplay));
            }
        }

        public string StateDisplay =>
            State switch
            {
                OrderSessionState.Confirmed => "تأیید شده",
                OrderSessionState.Waiting => "در انتظار",
                OrderSessionState.Running => "در حال اجرا",
                OrderSessionState.Completed => "تکمیل شده",
                OrderSessionState.Canceled => "لغو شده",
                OrderSessionState.Failed => "ناموفق",
                _ => State.ToString()
            };

        public DateTimeOffset? NextDueAt
        {
            get => _nextDueAt;
            private set
            {
                if (_nextDueAt == value)
                {
                    return;
                }

                _nextDueAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NextDueDisplay));
            }
        }

        public string NextDueDisplay =>
            NextDueAt?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "—";

        public string LastStatus
        {
            get => _lastStatus;
            private set
            {
                string normalized = value ?? "";
                if (string.Equals(_lastStatus, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _lastStatus = normalized;
                OnPropertyChanged();
            }
        }

        public void UpdateProgress(
            int clickedCount,
            DateTimeOffset? nextDueAt,
            string lastStatus)
        {
            if (clickedCount < 0 || clickedCount > TotalClickCount)
            {
                throw new ArgumentOutOfRangeException(nameof(clickedCount));
            }

            ClickedCount = clickedCount;
            NextDueAt = nextDueAt;
            LastStatus = lastStatus;
        }

        public void SetState(
            OrderSessionState state,
            string lastStatus)
        {
            State = state;
            LastStatus = lastStatus;

            if (state is OrderSessionState.Completed or
                OrderSessionState.Canceled or
                OrderSessionState.Failed)
            {
                NextDueAt = null;
            }
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class ScheduledClickExecution : IDisposable
    {
        private readonly CancellationTokenSource _cancellation =
            new CancellationTokenSource();
        private int _clickedCount;
        private int _cancellationRequested;

        public ScheduledClickExecution(ScheduledClickSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public ScheduledClickSession Session { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public int ClickedCount => Volatile.Read(ref _clickedCount);

        public bool RequestCancel()
        {
            if (Interlocked.Exchange(ref _cancellationRequested, 1) != 0)
            {
                return false;
            }

            _cancellation.Cancel();
            return true;
        }

        public int CommitClicked()
        {
            int clickedCount = Interlocked.Increment(ref _clickedCount);
            if (clickedCount > Session.TotalClickCount)
            {
                throw new InvalidOperationException(
                    "Scheduled click accounting exceeded the configured click count.");
            }

            return clickedCount;
        }

        public void Dispose()
        {
            _cancellation.Dispose();
        }
    }
}
