using System;
using System.Globalization;
using System.Windows;

namespace FastOrder
{
    public partial class OrderConfirmationWindow : Window
    {
        public OrderConfirmationWindow(
            CreateOrderPayload payload,
            OrderCalculationResult calculation,
            string payloadJson)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(calculation);

            if (payload.Order == null)
            {
                throw new ArgumentException(
                    "Order payload must contain an order.",
                    nameof(payload));
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                throw new ArgumentException(
                    "Payload JSON cannot be empty.",
                    nameof(payloadJson));
            }

            InitializeComponent();

            Order order =
                payload.Order;

            SymbolTextBlock.Text =
                order.SymbolName;

            IsinTextBlock.Text =
                order.SymbolIsin;

            SideTextBlock.Text =
                order.Side.ToString(
                    CultureInfo.InvariantCulture);

            PriceTextBlock.Text =
                FormatNumber(order.Price);

            QuantityTextBlock.Text =
                FormatNumber(order.Quantity);

            GrossValueTextBlock.Text =
                FormatNumber(calculation.GrossValue);

            CommissionTextBlock.Text =
                $"{FormatNumber(calculation.CommissionAmount)} " +
                $"({order.Commission.ToString("P4", CultureInfo.InvariantCulture)})";

            TotalValueTextBlock.Text =
                FormatNumber(calculation.TotalValue);

            OrderMetadataTextBlock.Text =
                $"{order.OrderModelType} / " +
                $"{order.OrderFrom} / " +
                $"{order.ValidityType}";

            CreatedAtTextBlock.Text =
                order.CreateDateTime;

            PayloadTextBox.Text =
                payloadJson;
        }

        private static string FormatNumber(
            long value)
        {
            return value.ToString(
                "N0",
                CultureInfo.InvariantCulture);
        }

        private void ConfirmationCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            ConfirmButton.IsEnabled =
                ConfirmationCheckBox.IsChecked == true;
        }

        private void ConfirmButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                true;
        }
    }
}
