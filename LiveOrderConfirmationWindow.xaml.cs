using System;
using System.Globalization;
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

        public LiveOrderConfirmationWindow(
            Order order,
            string shortFingerprint)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            if (string.IsNullOrWhiteSpace(
                shortFingerprint))
            {
                throw new ArgumentException(
                    "Order fingerprint cannot be empty.",
                    nameof(shortFingerprint));
            }

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

            DateTimeOffset now =
                DateTimeOffset.Now;

            DateTimeOffset endOfToday =
                new DateTimeOffset(
                    DateTime.Today
                        .AddDays(1)
                        .AddSeconds(-1));

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

            DialogResult =
                true;
        }

        private bool TryCreateSchedule(
            out DateTimeOffset startAt,
            out DateTimeOffset endAt,
            out string errorMessage)
        {
            startAt =
                default;

            endAt =
                default;

            errorMessage =
                "";

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

            DateTime startLocal =
                DateTime.SpecifyKind(
                    DateTime.Today.Add(
                        startTime.ToTimeSpan()),
                    DateTimeKind.Local);

            DateTime endLocal =
                DateTime.SpecifyKind(
                    DateTime.Today.Add(
                        endTime.ToTimeSpan()),
                    DateTimeKind.Local);

            startAt =
                new DateTimeOffset(
                    startLocal);

            endAt =
                new DateTimeOffset(
                    endLocal);

            DateTimeOffset now =
                DateTimeOffset.Now;

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
    }
}
