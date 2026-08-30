using System.Text.Json.Serialization;

namespace FastOrder
{
    public class Order
    {
        [JsonPropertyName("commission")]
        public double Commission { get; set; }

        [JsonPropertyName("createDateTime")]
        public string CreateDateTime { get; set; } = "";

        [JsonPropertyName("orderFrom")]
        public int OrderFrom { get; set; }

        [JsonPropertyName("orderModelType")]
        public int OrderModelType { get; set; }

        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonPropertyName("side")]
        public int Side { get; set; }

        [JsonPropertyName("symbolIsin")]
        public string SymbolIsin { get; set; } = "";

        [JsonPropertyName("symbolName")]
        public string SymbolName { get; set; } = "";

        [JsonPropertyName("totalValue")]
        public long TotalValue { get; set; }

        [JsonPropertyName("validityType")]
        public int ValidityType { get; set; }
    }


    public class CreateOrderPayload
    {
        [JsonPropertyName("order")]
        public Order Order { get; set; } = new();
    }


    public class OrderCalculationResult
    {
        public long GrossValue { get; set; }

        public long CommissionAmount { get; set; }

        public long TotalValue { get; set; }

        public double CommissionRate { get; set; }
    }


    public static class OrderCalculator
    {
        public static OrderCalculationResult Calculate(
            long price,
            long quantity,
            long commissionAmount)
        {
            if (price <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(price),
                    "Price must be greater than zero.");
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "Quantity must be greater than zero.");
            }

            if (commissionAmount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commissionAmount),
                    "Commission amount cannot be negative.");
            }

            long grossValue =
                checked(price * quantity);

            long totalValue =
                checked(grossValue + commissionAmount);

            double commissionRate =
                grossValue == 0
                    ? 0
                    : (double)commissionAmount / grossValue;

            return new OrderCalculationResult
            {
                GrossValue = grossValue,

                CommissionAmount =
                    commissionAmount,

                TotalValue =
                    totalValue,

                CommissionRate =
                    commissionRate
            };
        }
    }
}
