using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FastOrder
{
    internal sealed class OrderSubmissionValidationResult
    {
        private OrderSubmissionValidationResult(
            bool isValid,
            string errorMessage)
        {
            IsValid =
                isValid;

            ErrorMessage =
                errorMessage;
        }

        public bool IsValid
        {
            get;
        }

        public string ErrorMessage
        {
            get;
        }

        public static OrderSubmissionValidationResult Valid()
        {
            return new OrderSubmissionValidationResult(
                true,
                "");
        }

        public static OrderSubmissionValidationResult Invalid(
            string errorMessage)
        {
            return new OrderSubmissionValidationResult(
                false,
                errorMessage);
        }
    }

    internal static class OrderSubmissionValidator
    {
        private const double ExpectedCommissionRate =
            0.0012;

        private const decimal ExpectedCommissionRateDecimal =
            0.0012m;

        private const int ExpectedOrderFrom =
            34;

        private const int ExpectedOrderModelType =
            1;

        private const int ExpectedValidityType =
            0;

        private const int MaximumSymbolNameLength =
            100;

        private const string ExpectedDateTimeFormat =
            "M/d/yyyy, h:mm:ss tt";

        public static OrderSubmissionValidationResult Validate(
            CreateOrderPayload? payload)
        {
            if (payload == null)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "ساختار Payload معتبر نیست.");
            }

            Order? order =
                payload.Order;

            if (order == null)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "بخش order در Payload وجود ندارد.");
            }

            if (string.IsNullOrWhiteSpace(order.SymbolName) ||
                order.SymbolName.Length > MaximumSymbolNameLength)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "نام نماد معتبر نیست.");
            }

            if (string.IsNullOrWhiteSpace(order.SymbolIsin) ||
                !Regex.IsMatch(
                    order.SymbolIsin,
                    "^[A-Z0-9]{12}$",
                    RegexOptions.CultureInvariant |
                    RegexOptions.NonBacktracking,
                    TimeSpan.FromMilliseconds(100)))
            {
                return OrderSubmissionValidationResult.Invalid(
                    "ISIN معتبر نیست.");
            }

            if (order.Price <= 0)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "قیمت باید بزرگ‌تر از صفر باشد.");
            }

            if (order.Quantity <= 0)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "تعداد باید بزرگ‌تر از صفر باشد.");
            }

            if (order.Side != 0)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "فعلاً فقط کد سمت ۰ با نمونه معتبر تطبیق داده شده است.");
            }

            if (order.OrderFrom != ExpectedOrderFrom ||
                order.OrderModelType != ExpectedOrderModelType ||
                order.ValidityType != ExpectedValidityType)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "مقادیر ثابت سفارش با نمونه معتبر مطابقت ندارند.");
            }

            if (!double.IsFinite(order.Commission) ||
                Math.Abs(
                    order.Commission - ExpectedCommissionRate) >
                0.000000000001)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "نرخ کارمزد با مقدار تأییدشده مطابقت ندارد.");
            }

            if (!DateTime.TryParseExact(
                order.CreateDateTime,
                ExpectedDateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            {
                return OrderSubmissionValidationResult.Invalid(
                    "زمان ایجاد سفارش معتبر نیست.");
            }

            try
            {
                long grossValue =
                    checked(order.Price * order.Quantity);

                decimal commissionAmountDecimal =
                    decimal.Round(
                        grossValue * ExpectedCommissionRateDecimal,
                        0,
                        MidpointRounding.AwayFromZero);

                if (commissionAmountDecimal > long.MaxValue)
                {
                    return OrderSubmissionValidationResult.Invalid(
                        "مبلغ کارمزد از محدوده مجاز بزرگ‌تر است.");
                }

                long commissionAmount =
                    decimal.ToInt64(
                        commissionAmountDecimal);

                long expectedTotalValue =
                    checked(grossValue + commissionAmount);

                if (order.TotalValue != expectedTotalValue)
                {
                    return OrderSubmissionValidationResult.Invalid(
                        "مبلغ کل سفارش با محاسبه مستقل مطابقت ندارد.");
                }
            }
            catch (OverflowException)
            {
                return OrderSubmissionValidationResult.Invalid(
                    "محاسبات سفارش از محدوده عددی مجاز خارج است.");
            }

            return OrderSubmissionValidationResult.Valid();
        }
    }
}
