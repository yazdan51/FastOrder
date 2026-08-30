using System;
using System.Globalization;
using System.Windows;

namespace FastOrder
{
    public partial class LiveOrderConfirmationWindow : Window
    {
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
            DialogResult =
                true;
        }
    }
}
