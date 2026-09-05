using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Calculations;

public static class PositionAnalysisCalculator
{
    public static PositionAnalysisMetrics Calculate(
        PositionAnalysisState position,
        IMarketNormalizationAdapter normalizationAdapter)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(normalizationAdapter);

        var inputs = position.SizingInputs;
        var drawing = position.Drawing;
        var symbol = position.SymbolMetadata;
        var riskReward = RiskRewardCalculator.Calculate(drawing);
        var sizing = PositionSizingCalculator.Calculate(
            new PositionSizingRequest(
                drawing.Side,
                inputs.AccountSize,
                inputs.RiskMode,
                inputs.RiskValue,
                drawing.EntryPrice,
                drawing.StopPrice,
                inputs.Leverage),
            symbol,
            normalizationAdapter);
        var pnl = PositionPnlCalculator.Calculate(
            drawing,
            inputs.AccountSize,
            sizing.FinalQuantity,
            symbol.PointValue,
            symbol.LotSize);

        return new PositionAnalysisMetrics(riskReward, sizing, pnl);
    }
}
