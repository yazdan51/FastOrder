using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Interaction;

public static class PositionDrawingEditor
{
    public static PositionDrawing UpdatePriceClamped(
        PositionDrawing drawing,
        PositionHandle handle,
        decimal proposedPrice,
        decimal minimumPriceIncrement)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        if (minimumPriceIncrement <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumPriceIncrement),
                minimumPriceIncrement,
                "The minimum price increment must be greater than zero.");
        }

        return handle switch
        {
            PositionHandle.Target => UpdateTargetClamped(drawing, proposedPrice, minimumPriceIncrement),
            PositionHandle.Entry => UpdateEntryClamped(drawing, proposedPrice, minimumPriceIncrement),
            PositionHandle.Stop => UpdateStopClamped(drawing, proposedPrice, minimumPriceIncrement),
            PositionHandle.StartEdge or PositionHandle.EndEdge => throw new ArgumentException(
                "A horizontal edge cannot update a price.",
                nameof(handle)),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "Unknown position handle.")
        };
    }

    public static PositionDrawing UpdatePrice(
        PositionDrawing drawing,
        PositionHandle handle,
        decimal newPrice)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        return handle switch
        {
            PositionHandle.Target => drawing.WithPrices(drawing.EntryPrice, newPrice, drawing.StopPrice),
            PositionHandle.Entry => drawing.WithPrices(newPrice, drawing.TargetPrice, drawing.StopPrice),
            PositionHandle.Stop => drawing.WithPrices(drawing.EntryPrice, drawing.TargetPrice, newPrice),
            PositionHandle.StartEdge or PositionHandle.EndEdge => throw new ArgumentException(
                "A horizontal edge cannot update a price.",
                nameof(handle)),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "Unknown position handle.")
        };
    }

    public static PositionDrawing ResizeHorizontal(
        PositionDrawing drawing,
        PositionHandle handle,
        double horizontalValue)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var range = handle switch
        {
            PositionHandle.StartEdge => new ChartHorizontalRange(horizontalValue, drawing.HorizontalRange.End),
            PositionHandle.EndEdge => new ChartHorizontalRange(drawing.HorizontalRange.Start, horizontalValue),
            PositionHandle.Target or PositionHandle.Entry or PositionHandle.Stop => throw new ArgumentException(
                "A price handle cannot resize the horizontal range.",
                nameof(handle)),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "Unknown position handle.")
        };

        return drawing.WithHorizontalRange(range);
    }

    public static PositionDrawing Move(
        PositionDrawing drawing,
        decimal priceDelta,
        double horizontalDelta)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        return drawing.Translate(priceDelta, horizontalDelta);
    }

    private static PositionDrawing UpdateTargetClamped(
        PositionDrawing drawing,
        decimal proposedPrice,
        decimal increment)
    {
        var target = drawing.Side switch
        {
            PositionSide.Long => Math.Max(proposedPrice, drawing.EntryPrice + increment),
            PositionSide.Short => Math.Clamp(
                proposedPrice,
                increment,
                drawing.EntryPrice - increment),
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        return drawing.WithPrices(drawing.EntryPrice, target, drawing.StopPrice);
    }

    private static PositionDrawing UpdateEntryClamped(
        PositionDrawing drawing,
        decimal proposedPrice,
        decimal increment)
    {
        var minimum = drawing.Side switch
        {
            PositionSide.Long => drawing.StopPrice + increment,
            PositionSide.Short => drawing.TargetPrice + increment,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        var maximum = drawing.Side switch
        {
            PositionSide.Long => drawing.TargetPrice - increment,
            PositionSide.Short => drawing.StopPrice - increment,
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        if (maximum < minimum)
        {
            throw new InvalidOperationException(
                "The existing target-to-stop distance is smaller than two minimum price increments.");
        }

        return drawing.WithPrices(
            Math.Clamp(proposedPrice, minimum, maximum),
            drawing.TargetPrice,
            drawing.StopPrice);
    }

    private static PositionDrawing UpdateStopClamped(
        PositionDrawing drawing,
        decimal proposedPrice,
        decimal increment)
    {
        var stop = drawing.Side switch
        {
            PositionSide.Long => Math.Clamp(
                proposedPrice,
                increment,
                drawing.EntryPrice - increment),
            PositionSide.Short => Math.Max(proposedPrice, drawing.EntryPrice + increment),
            _ => throw new ArgumentOutOfRangeException(nameof(drawing), drawing.Side, "Unknown position side.")
        };

        return drawing.WithPrices(drawing.EntryPrice, drawing.TargetPrice, stop);
    }
}
