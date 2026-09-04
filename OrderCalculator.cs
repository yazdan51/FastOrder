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


   

   


    
}
