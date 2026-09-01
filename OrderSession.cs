using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FastOrder
{
    public sealed class OrderSession : INotifyPropertyChanged
    {
        private OrderSessionState _state;
        private long _sentQuantity;
        private long _inFlightQuantity;
        private int _clickedOrderCount;
        private DateTimeOffset? _nextDueAt;
        private string _lastStatus;
        private int? _lastHttpStatus;
        private string _lastError;
        private DateTimeOffset? _completedAt;

        public OrderSession(
            long creationSequence,
            Order order,
            long maxQuantityPerOrder,
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            ConfirmedOrderSnapshot confirmedOrderSnapshot)
        {
            ArgumentNullException.ThrowIfNull(order);
            ArgumentNullException.ThrowIfNull(confirmedOrderSnapshot);

            if (creationSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(creationSequence));
            }

            if (string.IsNullOrWhiteSpace(
                order.SymbolName) ||
                string.IsNullOrWhiteSpace(
                    order.SymbolIsin))
            {
                throw new ArgumentException(
                    "Session instrument identity cannot be empty.",
                    nameof(order));
            }

            if (order.Price <= 0 ||
                order.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(order));
            }

            if (maxQuantityPerOrder <= 0 ||
                maxQuantityPerOrder > order.Quantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxQuantityPerOrder));
            }

            if (endTime <= startTime)
            {
                throw new ArgumentException(
                    "Session end time must be after its start time.",
                    nameof(endTime));
            }

            if (!confirmedOrderSnapshot.HasValidFingerprint())
            {
                throw new ArgumentException(
                    "The confirmed order snapshot fingerprint is invalid.",
                    nameof(confirmedOrderSnapshot));
            }

            SessionId =
                Guid.NewGuid();

            CreationSequence =
                creationSequence;

            SymbolName =
                order.SymbolName;

            SymbolIsin =
                order.SymbolIsin;

            Side =
                order.Side;

            Price =
                order.Price;

            TotalQuantity =
                order.Quantity;

            MaxQuantityPerOrder =
                maxQuantityPerOrder;

            StartTime =
                startTime;

            EndTime =
                endTime;

            ConfirmedOrderSnapshot =
                confirmedOrderSnapshot.CreateIndependentCopy();

            BrokerId =
                ConfirmedOrderSnapshot.BrokerId;

            BrokerDisplayName =
                BrokerProfiles.GetDisplayName(
                    BrokerId);

            CreatedAt =
                DateTimeOffset.Now;

            _state =
                OrderSessionState.Confirmed;

            _nextDueAt =
                startTime;

            _lastStatus =
                "تأیید شده";

            _lastError =
                "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid SessionId
        {
            get;
        }

        public string SessionIdDisplay =>
            SessionId.ToString("N", CultureInfo.InvariantCulture)[..8]
                .ToUpperInvariant();

        public long CreationSequence
        {
            get;
        }

        public string BrokerId
        {
            get;
        }

        public string BrokerDisplayName
        {
            get;
        }

        public string SymbolName
        {
            get;
        }

        public string SymbolIsin
        {
            get;
        }

        public int Side
        {
            get;
        }

        public string SideDisplay =>
            Side == 0
                ? "خرید"
                : "فروش";

        public long Price
        {
            get;
        }

        public string PriceDisplay =>
            Price.ToString(
                "N0",
                CultureInfo.InvariantCulture);

        public long TotalQuantity
        {
            get;
        }

        public string TotalQuantityDisplay =>
            FormatQuantity(
                TotalQuantity);

        public long MaxQuantityPerOrder
        {
            get;
        }

        public string MaxQuantityPerOrderDisplay =>
            FormatQuantity(
                MaxQuantityPerOrder);

        public DateTimeOffset StartTime
        {
            get;
        }

        public string StartTimeDisplay =>
            StartTime.ToString(
                "HH:mm:ss",
                CultureInfo.InvariantCulture);

        public DateTimeOffset EndTime
        {
            get;
        }

        public long SentQuantity
        {
            get =>
                _sentQuantity;

            private set =>
                SetField(
                    ref _sentQuantity,
                    value);
        }

        public string SentQuantityDisplay =>
            FormatQuantity(
                SentQuantity);

        public long InFlightQuantity
        {
            get =>
                _inFlightQuantity;

            private set =>
                SetField(
                    ref _inFlightQuantity,
                    value);
        }

        public string InFlightQuantityDisplay =>
            FormatQuantity(
                InFlightQuantity);

        public long RemainingQuantity =>
            Math.Max(
                0,
                TotalQuantity -
                SentQuantity -
                InFlightQuantity);

        public string RemainingQuantityDisplay =>
            FormatQuantity(
                RemainingQuantity);

        public int ClickedOrderCount
        {
            get =>
                _clickedOrderCount;

            private set =>
                SetField(
                    ref _clickedOrderCount,
                    value);
        }

        public ConfirmedOrderSnapshot ConfirmedOrderSnapshot
        {
            get;
        }

        public OrderSessionState State
        {
            get =>
                _state;

            private set
            {
                if (SetField(
                    ref _state,
                    value))
                {
                    OnPropertyChanged(
                        nameof(StateDisplay));
                }
            }
        }

        public string StateDisplay =>
            State switch
            {
                OrderSessionState.Draft => "پیش‌نویس",
                OrderSessionState.Confirmed => "تأییدشده",
                OrderSessionState.Waiting => "در انتظار",
                OrderSessionState.PreWarming => "پیش‌آماده‌سازی",
                OrderSessionState.Ready => "آماده",
                OrderSessionState.Running => "در حال اجرا",
                OrderSessionState.Paused => "متوقف موقت",
                OrderSessionState.Completed => "پایان‌یافته",
                OrderSessionState.Canceled => "لغوشده",
                OrderSessionState.Failed => "ناموفق",
                _ => State.ToString()
            };

        public DateTimeOffset? NextDueAt
        {
            get =>
                _nextDueAt;

            private set
            {
                if (SetField(
                    ref _nextDueAt,
                    value))
                {
                    OnPropertyChanged(
                        nameof(NextDueDisplay));
                }
            }
        }

        public string NextDueDisplay =>
            NextDueAt?.ToString(
                "HH:mm:ss.fff",
                CultureInfo.InvariantCulture) ??
            "—";

        public string LastStatus
        {
            get =>
                _lastStatus;

            private set =>
                SetField(
                    ref _lastStatus,
                    value);
        }

        public int? LastHttpStatus
        {
            get =>
                _lastHttpStatus;

            private set
            {
                if (SetField(
                    ref _lastHttpStatus,
                    value))
                {
                    OnPropertyChanged(
                        nameof(LastHttpStatusDisplay));
                }
            }
        }

        public string LastHttpStatusDisplay =>
            LastHttpStatus?.ToString(
                CultureInfo.InvariantCulture) ??
            "—";

        public string LastError
        {
            get =>
                _lastError;

            private set =>
                SetField(
                    ref _lastError,
                    value);
        }

        public DateTimeOffset CreatedAt
        {
            get;
        }

        public DateTimeOffset? CompletedAt
        {
            get =>
                _completedAt;

            private set =>
                SetField(
                    ref _completedAt,
                    value);
        }

        public void SetState(
            OrderSessionState state,
            string status,
            string error = "")
        {
            State =
                state;

            LastStatus =
                status ?? "";

            LastError =
                error ?? "";

            if (state is
                OrderSessionState.Completed or
                OrderSessionState.Canceled or
                OrderSessionState.Failed)
            {
                CompletedAt =
                    DateTimeOffset.Now;

                NextDueAt =
                    null;
            }
        }

        public void UpdateProgress(
            long sentQuantity,
            long inFlightQuantity,
            int clickedOrderCount,
            DateTimeOffset? nextDueAt,
            string status)
        {
            SentQuantity =
                sentQuantity;

            InFlightQuantity =
                inFlightQuantity;

            ClickedOrderCount =
                clickedOrderCount;

            NextDueAt =
                nextDueAt;

            LastStatus =
                status ?? "";

            OnPropertyChanged(
                nameof(SentQuantityDisplay));

            OnPropertyChanged(
                nameof(InFlightQuantityDisplay));

            OnPropertyChanged(
                nameof(RemainingQuantity));

            OnPropertyChanged(
                nameof(RemainingQuantityDisplay));
        }

        public void SetLastHttpStatus(
            int status)
        {
            LastHttpStatus =
                status;
        }

        private static string FormatQuantity(
            long value)
        {
            return value.ToString(
                "N0",
                CultureInfo.InvariantCulture);
        }

        private bool SetField<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = "")
        {
            if (Equals(
                field,
                value))
            {
                return false;
            }

            field =
                value;

            OnPropertyChanged(
                propertyName);

            return true;
        }

        private void OnPropertyChanged(
            string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }
}
