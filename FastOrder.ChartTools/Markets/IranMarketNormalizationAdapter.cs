namespace FastOrder.ChartTools.Markets;

public sealed class IranMarketNormalizationAdapter : IMarketNormalizationAdapter
{
    public decimal NormalizePrice(
        decimal price,
        SymbolMetadata symbol,
        StepRoundingMode roundingMode = StepRoundingMode.Nearest)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return NormalizeToStep(price, symbol.TickSize, roundingMode, nameof(price));
    }

    public decimal NormalizeQuantityDown(decimal quantity, SymbolMetadata symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity cannot be negative.");
        }

        var normalized = NormalizeToStep(
            quantity,
            symbol.QuantityStep,
            StepRoundingMode.Down,
            nameof(quantity));

        return normalized < symbol.MinimumQuantity ? 0m : normalized;
    }

    private static decimal NormalizeToStep(
        decimal value,
        decimal step,
        StepRoundingMode roundingMode,
        string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value cannot be negative.");
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be greater than zero.");
        }

        var units = value / step;
        var roundedUnits = roundingMode switch
        {
            StepRoundingMode.Down => decimal.Floor(units),
            StepRoundingMode.Nearest => decimal.Round(units, 0, MidpointRounding.AwayFromZero),
            StepRoundingMode.Up => decimal.Ceiling(units),
            _ => throw new ArgumentOutOfRangeException(nameof(roundingMode), roundingMode, "Unknown rounding mode.")
        };

        return checked(roundedUnits * step);
    }
}
