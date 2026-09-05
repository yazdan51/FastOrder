using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Calculations;

public static class RiskRewardCalculator
{
    public static RiskRewardMetrics Calculate(PositionDrawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var riskPerUnit = drawing.Side switch
        {
            PositionSide.Long => drawing.EntryPrice - drawing.StopPrice,
            PositionSide.Short => drawing.StopPrice - drawing.EntryPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        var rewardPerUnit = drawing.Side switch
        {
            PositionSide.Long => drawing.TargetPrice - drawing.EntryPrice,
            PositionSide.Short => drawing.EntryPrice - drawing.TargetPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        return new RiskRewardMetrics(
            riskPerUnit,
            rewardPerUnit,
            riskPerUnit / drawing.EntryPrice * 100m,
            rewardPerUnit / drawing.EntryPrice * 100m,
            rewardPerUnit / riskPerUnit);
    }
}
