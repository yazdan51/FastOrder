namespace FastOrder.ChartTools.Markets;

public sealed record SymbolMetadata
{
    public SymbolMetadata(
        string symbol,
        decimal tickSize,
        decimal quantityStep,
        decimal minimumQuantity,
        decimal pointValue,
        decimal lotSize)
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

        Symbol = symbol.Trim();
        TickSize = tickSize;
        QuantityStep = quantityStep;
        MinimumQuantity = minimumQuantity;
        PointValue = pointValue;
        LotSize = lotSize;
    }

    public string Symbol { get; }

    public decimal TickSize { get; }

    public decimal QuantityStep { get; }

    public decimal MinimumQuantity { get; }

    public decimal PointValue { get; }

    public decimal LotSize { get; }

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
        }
    }
}
