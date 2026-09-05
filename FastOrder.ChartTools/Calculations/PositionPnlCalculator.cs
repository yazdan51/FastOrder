using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Calculations;

public static class PositionPnlCalculator
{
    public static PositionPnlMetrics Calculate(
        PositionDrawing drawing,
        decimal accountSize,
        decimal quantity,
        decimal pointValue,
        decimal lotSize)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        EnsureNonNegative(accountSize, nameof(accountSize));
        EnsureNonNegative(quantity, nameof(quantity));
        EnsurePositive(pointValue, nameof(pointValue));
        EnsurePositive(lotSize, nameof(lotSize));

        var multiplier = checked(quantity * pointValue * lotSize);

        var profitPnl = drawing.Side switch
        {
            PositionSide.Long => (drawing.TargetPrice - drawing.EntryPrice) * multiplier,
            PositionSide.Short => (drawing.EntryPrice - drawing.TargetPrice) * multiplier,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        var lossPnl = drawing.Side switch
        {
            PositionSide.Long => (drawing.StopPrice - drawing.EntryPrice) * multiplier,
            PositionSide.Short => (drawing.EntryPrice - drawing.StopPrice) * multiplier,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        return new PositionPnlMetrics(
            profitPnl,
            lossPnl,
            checked(accountSize + profitPnl),
            checked(accountSize + lossPnl));
    }

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
        }
    }

    private static void EnsureNonNegative(decimal value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value cannot be negative.");
        }
    }
}
