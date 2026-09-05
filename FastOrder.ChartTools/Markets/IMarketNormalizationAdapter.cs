namespace FastOrder.ChartTools.Markets;

public interface IMarketNormalizationAdapter
{
    decimal NormalizePrice(
        decimal price,
        SymbolMetadata symbol,
        StepRoundingMode roundingMode = StepRoundingMode.Nearest);

    decimal NormalizeQuantityDown(decimal quantity, SymbolMetadata symbol);
}
