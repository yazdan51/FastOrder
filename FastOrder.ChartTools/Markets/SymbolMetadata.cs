namespace FastOrder.ChartTools.Markets;

public sealed record SymbolMetadata
{
    public SymbolMetadata(
        string symbol,
        decimal tickSize,
        decimal quantityStep,
        decimal minimumQuantity,
        decimal pointValue,
        decimal lotSize,
        int? quantityPrecision = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("A symbol is required.", nameof(symbol));
        }

        EnsurePositive(tickSize, nameof(tickSize));
        EnsurePositive(quantityStep, nameof(quantityStep));
        EnsurePositive(minimumQuantity, nameof(minimumQuantity));
        EnsurePositive(pointValue, nameof(pointValue));
        EnsurePositive(lotSize, nameof(lotSize));

        var resolvedQuantityPrecision = quantityPrecision ?? GetDecimalPlaces(quantityStep);
        if (resolvedQuantityPrecision is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityPrecision),
                resolvedQuantityPrecision,
                "Quantity precision must be between 0 and 28.");
        }

        if (decimal.Round(quantityStep, resolvedQuantityPrecision) != quantityStep)
        {
            throw new ArgumentException(
                "Quantity step cannot contain more decimal places than quantity precision.",
                nameof(quantityPrecision));
        }

        Symbol = symbol.Trim();
        TickSize = tickSize;
        QuantityStep = quantityStep;
        MinimumQuantity = minimumQuantity;
        PointValue = pointValue;
        LotSize = lotSize;
        QuantityPrecision = resolvedQuantityPrecision;
    }

    public string Symbol { get; }

    public decimal TickSize { get; }

    public decimal QuantityStep { get; }

    public decimal MinimumQuantity { get; }

    public decimal PointValue { get; }

    public decimal LotSize { get; }

    public int QuantityPrecision { get; }

    public SymbolMetadata WithSizing(
        decimal pointValue,
        decimal lotSize,
        int quantityPrecision) =>
        new(
            Symbol,
            TickSize,
            QuantityStep,
            MinimumQuantity,
            pointValue,
            lotSize,
            quantityPrecision);

    private static int GetDecimalPlaces(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
        }
    }
}
