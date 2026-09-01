using System;
using System.Globalization;
using System.Text;
using System.Windows;

namespace FastOrder
{
    public partial class LiveOrderConfirmationWindow : Window
    {
        private static readonly string[] SupportedTimeFormats =
        {
            "H:mm",
            "HH:mm",
            "H:mm:ss",
            "HH:mm:ss"
        };

        private readonly long _totalQuantity;
        private readonly ExchangeClock _exchangeClock;

        public DateTimeOffset ScheduledStartAt
        {
            get;
            private set;
        }

        public DateTimeOffset ScheduledEndAt
        {
            get;
            private set;
        }

        public long MaxQuantityPerOrder
        {
            get;
            private set;
        }

        internal LiveOrderConfirmationWindow(
            Order order,
            string shortFingerprint,
            ExchangeClock exchangeClock)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            ArgumentNullException.ThrowIfNull(
                exchangeClock);

            if (string.IsNullOrWhiteSpace(
                shortFingerprint))
            {
                throw new ArgumentException(
                    "Order fingerprint cannot be empty.",
                    nameof(shortFingerprint));
            }

            _totalQuantity =
                order.Quantity;

            _exchangeClock =
                exchangeClock;

            InitializeComponent();

            long grossValue =
                checked(
                    order.Price *
                    order.Quantity);

            SymbolTextBlock.Text =
                order.SymbolName;

            IsinTextBlock.Text =
                order.SymbolIsin;

            SideTextBlock.Text =
                order.Side == 0
                    ? "خرید"
                    : "فروش";

            PriceTextBlock.Text =
                FormatNumber(
                    order.Price);

            QuantityTextBlock.Text =
                FormatNumber(
                    order.Quantity);

            GrossValueTextBlock.Text =
                FormatNumber(
                    grossValue);

            TotalValueTextBlock.Text =
                FormatNumber(
                    order.TotalValue);

            FingerprintTextBlock.Text =
                shortFingerprint;

            if (!_exchangeClock.TryGetReading(
                ExchangeClock.ConfirmationMaximumSampleAge,
                out ExchangeClockReading exchangeClockReading))
            {
                throw new InvalidOperationException(
                    "A fresh TSETMC exchange-clock reading is required.");
            }

            DateTimeOffset now =
                exchangeClockReading.Now;

            DateTimeOffset endOfToday =
                new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    23,
                    59,
                    59,
                    now.Offset);

            DateTimeOffset defaultStart =
                now.AddMinutes(1);

            if (defaultStart > endOfToday)
            {
                defaultStart =
                    now;
            }

            DateTimeOffset defaultEnd =
                defaultStart.AddMinutes(15);

            if (defaultEnd > endOfToday)
            {
                defaultEnd =
                    endOfToday;
            }

            StartTimeTextBox.Text =
                defaultStart.ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture);

            EndTimeTextBox.Text =
                defaultEnd.ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture);

            MaxQuantityPerOrderTextBox.Text =
                _totalQuantity.ToString(
                    CultureInfo.InvariantCulture);

            ExchangeClockTextBlock.Text =
                now.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff zzz",
                    CultureInfo.InvariantCulture) +
                " — " +
                ExchangeClock.SourceDisplayName;
        }

        private static string FormatNumber(
            long value)
        {
            return value.ToString(
                "N0",
                CultureInfo.InvariantCulture);
        }

        private void FinalConfirmationCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            SubmitButton.IsEnabled =
                FinalConfirmationCheckBox.IsChecked ==
                true;
        }

        private void SubmitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryCreateSchedule(
                out DateTimeOffset startAt,
                out DateTimeOffset endAt,
                out long maxQuantityPerOrder,
                out string errorMessage))
            {
                MessageBox.Show(
                    this,
                    errorMessage,
                    "بازه زمانی نامعتبر",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            ScheduledStartAt =
                startAt;

            ScheduledEndAt =
                endAt;

            MaxQuantityPerOrder =
                maxQuantityPerOrder;

            DialogResult =
                true;
        }

        private bool TryCreateSchedule(
            out DateTimeOffset startAt,
            out DateTimeOffset endAt,
            out long maxQuantityPerOrder,
            out string errorMessage)
        {
            startAt =
                default;

            endAt =
                default;

            maxQuantityPerOrder =
                0;

            errorMessage =
                "";

            if (!TryParsePositiveLong(
                MaxQuantityPerOrderTextBox.Text,
                out maxQuantityPerOrder))
            {
                errorMessage =
                    "حداکثر حجم هر سفارش باید یک عدد صحیح بزرگ‌تر از صفر باشد.";

                return false;
            }

            if (maxQuantityPerOrder >
                _totalQuantity)
            {
                errorMessage =
                    "حداکثر حجم هر سفارش نمی‌تواند از حجم کل بیشتر باشد.";

                return false;
            }

            if (!TimeOnly.TryParseExact(
                StartTimeTextBox.Text?.Trim(),
                SupportedTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly startTime))
            {
                errorMessage =
                    "ساعت شروع را با قالب HH:mm یا HH:mm:ss وارد کنید.";

                return false;
            }

            if (!TimeOnly.TryParseExact(
                EndTimeTextBox.Text?.Trim(),
                SupportedTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly endTime))
            {
                errorMessage =
                    "ساعت پایان را با قالب HH:mm یا HH:mm:ss وارد کنید.";

                return false;
            }

            if (!_exchangeClock.TryGetReading(
                ExchangeClock.ConfirmationMaximumSampleAge,
                out ExchangeClockReading exchangeClockReading))
            {
                errorMessage =
                    "ساعت مرکز معاملات معتبر یا تازه نیست؛ پنجره را ببندید و دوباره تلاش کنید.";

                return false;
            }

            DateTimeOffset now =
                exchangeClockReading.Now;

            startAt =
                new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    startTime.Hour,
                    startTime.Minute,
                    startTime.Second,
                    now.Offset);

            endAt =
                new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    endTime.Hour,
                    endTime.Minute,
                    endTime.Second,
                    now.Offset);

            if (endAt <= now)
            {
                errorMessage =
                    "ساعت پایان باید بعد از زمان فعلی باشد.";

                return false;
            }

            if (endAt <= startAt)
            {
                errorMessage =
                    "ساعت پایان باید بعد از ساعت شروع و در همان روز باشد.";

                return false;
            }

            if (endAt - startAt >
                TimeSpan.FromHours(8))
            {
                errorMessage =
                    "طول بازه نمی‌تواند بیشتر از ۸ ساعت باشد.";

                return false;
            }

            if (startAt < now)
            {
                startAt =
                    now;
            }

            return true;
        }

        private static bool TryParsePositiveLong(
            string? text,
            out long value)
        {
            string normalized =
                NormalizeNumber(
                    text ?? "");

            return
                long.TryParse(
                    normalized,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value)
                &&
                value > 0;
        }

        private static string NormalizeNumber(
            string text)
        {
            StringBuilder result =
                new StringBuilder(
                    text.Length);

            foreach (char character in
                text.Trim())
            {
                if (character is ',' or '٬' or '،' ||
                    char.IsWhiteSpace(character))
                {
                    continue;
                }

                result.Append(
                    character switch
                    {
                        >= '۰' and <= '۹' =>
                            (char)('0' + character - '۰'),

                        >= '٠' and <= '٩' =>
                            (char)('0' + character - '٠'),

                        _ =>
                            character
                    });
            }

            return result.ToString();
        }
    }
}
