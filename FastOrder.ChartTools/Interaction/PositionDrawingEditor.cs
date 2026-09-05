using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Interaction;

public static class PositionDrawingEditor
{
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
}
