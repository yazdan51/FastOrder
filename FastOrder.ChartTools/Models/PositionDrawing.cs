namespace FastOrder.ChartTools.Models;

public sealed record PositionDrawing
{
    public PositionDrawing(
        Guid id,
        PositionSide side,
        decimal entryPrice,
        decimal targetPrice,
        decimal stopPrice,
        ChartHorizontalRange horizontalRange)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A position drawing must have a non-empty identifier.", nameof(id));
        }

        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown position side.");
        }

        ValidatePrices(side, entryPrice, targetPrice, stopPrice);

        if (!horizontalRange.IsValid)
        {
            throw new ArgumentException("A position drawing requires a valid horizontal range.", nameof(horizontalRange));
        }

        Id = id;
        Side = side;
        EntryPrice = entryPrice;
        TargetPrice = targetPrice;
        StopPrice = stopPrice;
        HorizontalRange = horizontalRange;
    }

    public Guid Id { get; }

    public PositionSide Side { get; }

    public decimal EntryPrice { get; }

    public decimal TargetPrice { get; }

    public decimal StopPrice { get; }

    public ChartHorizontalRange HorizontalRange { get; }

    public static PositionDrawing Create(
        PositionSide side,
        decimal entryPrice,
        decimal targetPrice,
        decimal stopPrice,
        ChartHorizontalRange horizontalRange) =>
        new(Guid.NewGuid(), side, entryPrice, targetPrice, stopPrice, horizontalRange);

    public PositionDrawing WithPrices(decimal entryPrice, decimal targetPrice, decimal stopPrice) =>
        new(Id, Side, entryPrice, targetPrice, stopPrice, HorizontalRange);

    public PositionDrawing WithHorizontalRange(ChartHorizontalRange horizontalRange) =>
        new(Id, Side, EntryPrice, TargetPrice, StopPrice, horizontalRange);

    public PositionDrawing Translate(decimal priceDelta, double horizontalDelta) =>
        new(
            Id,
            Side,
            checked(EntryPrice + priceDelta),
            checked(TargetPrice + priceDelta),
            checked(StopPrice + priceDelta),
            HorizontalRange.Translate(horizontalDelta));

    private static void ValidatePrices(
        PositionSide side,
        decimal entryPrice,
        decimal targetPrice,
        decimal stopPrice)
    {
        EnsurePositive(entryPrice, nameof(entryPrice));
        EnsurePositive(targetPrice, nameof(targetPrice));
        EnsurePositive(stopPrice, nameof(stopPrice));

        var hasValidOrdering = side switch
        {
            PositionSide.Long => targetPrice > entryPrice && entryPrice > stopPrice,
            PositionSide.Short => stopPrice > entryPrice && entryPrice > targetPrice,
            _ => false
        };

        if (!hasValidOrdering)
        {
            throw new ArgumentException(
                side == PositionSide.Long
                    ? "A long position requires TargetPrice > EntryPrice > StopPrice."
                    : "A short position requires StopPrice > EntryPrice > TargetPrice.");
        }
    }

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Prices must be greater than zero.");
        }
    }
}
