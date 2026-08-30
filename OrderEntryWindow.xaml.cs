using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FastOrder
{
    public partial class OrderEntryWindow : Window
    {
        private const string DefaultSymbolName =
            "طلا";

        private const string DefaultSymbolIsin =
            "IRTKLOTF0001";

        private const long DefaultPrice =
            1_356_317;

        private const long DefaultQuantity =
            10;

        private const int DefaultSideIndex =
            0;

        private const double CommissionRate =
            0.0012;

        private const int DefaultOrderFrom =
            34;

        private const int DefaultOrderModelType =
            1;

        private const int DefaultValidityType =
            0;

        public CreateOrderPayload? Payload
        {
            get;
            private set;
        }

        public OrderCalculationResult? Calculation
        {
            get;
            private set;
        }

        public OrderEntryWindow()
        {
            InitializeComponent();

            SymbolNameTextBox.Text =
                DefaultSymbolName;

            IsinTextBox.Text =
                DefaultSymbolIsin;

            PriceTextBox.Text =
                DefaultPrice.ToString(
                    CultureInfo.InvariantCulture);

            QuantityTextBox.Text =
                DefaultQuantity.ToString(
                    CultureInfo.InvariantCulture);

            SideComboBox.SelectedIndex =
                DefaultSideIndex;

            SymbolNameTextBox.TextChanged +=
                Input_Changed;

            IsinTextBox.TextChanged +=
                Input_Changed;

            PriceTextBox.TextChanged +=
                Input_Changed;

            QuantityTextBox.TextChanged +=
                Input_Changed;

            SideComboBox.SelectionChanged +=
                Input_Changed;

            UpdateValidationState();
        }

        private void Input_Changed(
            object sender,
            RoutedEventArgs e)
        {
            UpdateValidationState();
        }

        private void UpdateValidationState()
        {
            if (TryBuildOrder(
                out _,
                out OrderCalculationResult? calculation,
                out string errorMessage) &&
                calculation != null)
            {
                ValidationTextBlock.Text =
                    "";

                BuildPreviewButton.IsEnabled =
                    true;

                GrossValueTextBlock.Text =
                    FormatNumber(
                        calculation.GrossValue);

                CommissionAmountTextBlock.Text =
                    FormatNumber(
                        calculation.CommissionAmount);

                TotalValueTextBlock.Text =
                    FormatNumber(
                        calculation.TotalValue);

                return;
            }

            ValidationTextBlock.Text =
                errorMessage;

            BuildPreviewButton.IsEnabled =
                false;

            GrossValueTextBlock.Text =
                "—";

            CommissionAmountTextBlock.Text =
                "—";

            TotalValueTextBlock.Text =
                "—";
        }

        private bool TryBuildOrder(
            out CreateOrderPayload? payload,
            out OrderCalculationResult? calculation,
            out string errorMessage)
        {
            payload =
                null;

            calculation =
                null;

            string symbolName =
                SymbolNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                errorMessage =
                    "نام نماد الزامی است.";

                return false;
            }

            string isin =
                IsinTextBox.Text.Trim().ToUpperInvariant();

            if (!Regex.IsMatch(
                isin,
                "^[A-Z0-9]{12}$",
                RegexOptions.CultureInvariant))
            {
                errorMessage =
                    "ISIN باید دقیقاً ۱۲ نویسه انگلیسی یا عدد باشد.";

                return false;
            }

            if (!TryParsePositiveLong(
                PriceTextBox.Text,
                out long price))
            {
                errorMessage =
                    "قیمت باید یک عدد صحیح بزرگ‌تر از صفر باشد.";

                return false;
            }

            if (!TryParsePositiveLong(
                QuantityTextBox.Text,
                out long quantity))
            {
                errorMessage =
                    "تعداد باید یک عدد صحیح بزرگ‌تر از صفر باشد.";

                return false;
            }

            if (SideComboBox.SelectedItem is not ComboBoxItem sideItem ||
                !int.TryParse(
                    sideItem.Tag?.ToString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int side) ||
                side is < 0 or > 1)
            {
                errorMessage =
                    "کد سمت سفارش معتبر نیست.";

                return false;
            }

            try
            {
                OrderCalculationResult grossCalculation =
                    OrderCalculator.Calculate(
                        price,
                        quantity,
                        0);

                decimal commissionDecimal =
                    decimal.Round(
                        grossCalculation.GrossValue *
                        (decimal)CommissionRate,
                        0,
                        MidpointRounding.AwayFromZero);

                if (commissionDecimal > long.MaxValue)
                {
                    errorMessage =
                        "مبلغ کارمزد از محدوده مجاز بزرگ‌تر است.";

                    return false;
                }

                long commissionAmount =
                    decimal.ToInt64(
                        commissionDecimal);

                calculation =
                    OrderCalculator.Calculate(
                        price,
                        quantity,
                        commissionAmount);

                payload =
                    new CreateOrderPayload
                    {
                        Order =
                            new Order
                            {
                                Commission =
                                    CommissionRate,

                                CreateDateTime =
                                    DateTime.Now.ToString(
                                        "M/d/yyyy, h:mm:ss tt",
                                        CultureInfo.InvariantCulture),

                                OrderFrom =
                                    DefaultOrderFrom,

                                OrderModelType =
                                    DefaultOrderModelType,

                                Price =
                                    price,

                                Quantity =
                                    quantity,

                                Side =
                                    side,

                                SymbolIsin =
                                    isin,

                                SymbolName =
                                    symbolName,

                                TotalValue =
                                    calculation.TotalValue,

                                ValidityType =
                                    DefaultValidityType
                            }
                    };

                errorMessage =
                    "";

                return true;
            }
            catch (OverflowException)
            {
                errorMessage =
                    "حاصل محاسبه از محدوده عددی مجاز بزرگ‌تر است.";

                return false;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                errorMessage =
                    exception.Message;

                return false;
            }
        }

        private static bool TryParsePositiveLong(
            string text,
            out long value)
        {
            string normalized =
                NormalizeNumber(text);

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
                new StringBuilder(text.Length);

            foreach (char character in text.Trim())
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

        private static string FormatNumber(
            long value)
        {
            return value.ToString(
                "N0",
                CultureInfo.InvariantCulture);
        }

        private void BuildPreviewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryBuildOrder(
                out CreateOrderPayload? payload,
                out OrderCalculationResult? calculation,
                out string errorMessage) ||
                payload == null ||
                calculation == null)
            {
                ValidationTextBlock.Text =
                    errorMessage;

                return;
            }

            Payload =
                payload;

            Calculation =
                calculation;

            DialogResult =
                true;
        }
    }
}
