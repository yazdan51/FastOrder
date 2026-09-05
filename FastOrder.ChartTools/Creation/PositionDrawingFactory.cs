using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Creation;

public static class PositionDrawingFactory
{
    private const decimal PocStopFraction = 0.01m;
    private const decimal PocTargetFraction = 0.02m;

    public static PositionDrawing CreatePocDefault(
        PositionSide side,
        decimal entryPrice,
        ChartHorizontalRange horizontalRange,
        SymbolMetadata symbol,
        IMarketNormalizationAdapter normalizationAdapter)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(normalizationAdapter);

        var entry = normalizationAdapter.NormalizePrice(
            entryPrice,
            symbol,
            StepRoundingMode.Nearest);

        if (entry <= symbol.TickSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryPrice),
                entryPrice,
                "Entry must be greater than one tick for the PoC defaults.");
        }

        decimal stop;
        decimal target;

        switch (side)
        {
            case PositionSide.Long:
                stop = normalizationAdapter.NormalizePrice(
                    entry * (1m - PocStopFraction),
                    symbol,
                    StepRoundingMode.Down);
                target = normalizationAdapter.NormalizePrice(
                    entry * (1m + PocTargetFraction),
                    symbol,
                    StepRoundingMode.Up);
                stop = Math.Min(stop, entry - symbol.TickSize);
                target = Math.Max(target, entry + symbol.TickSize);
                break;

            case PositionSide.Short:
                stop = normalizationAdapter.NormalizePrice(
                    entry * (1m + PocStopFraction),
                    symbol,
                    StepRoundingMode.Up);
                target = normalizationAdapter.NormalizePrice(
                    entry * (1m - PocTargetFraction),
                    symbol,
                    StepRoundingMode.Down);
                stop = Math.Max(stop, entry + symbol.TickSize);
                target = Math.Min(target, entry - symbol.TickSize);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown position side.");
        }

        return PositionDrawing.Create(side, entry, target, stop, horizontalRange);
    }
}
