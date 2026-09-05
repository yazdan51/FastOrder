using FastOrder.ChartTools.Coordinates;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Rendering;

public static class PositionGeometryMapper
{
    public static PositionDrawingGeometry Map(
        PositionDrawing drawing,
        IChartCoordinateMapper coordinateMapper)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(coordinateMapper);

        var startX = coordinateMapper.HorizontalValueToX(drawing.HorizontalRange.Start);
        var endX = coordinateMapper.HorizontalValueToX(drawing.HorizontalRange.End);

        return new PositionDrawingGeometry(
            Math.Min(startX, endX),
            Math.Max(startX, endX),
            coordinateMapper.PriceToY(drawing.TargetPrice),
            coordinateMapper.PriceToY(drawing.EntryPrice),
            coordinateMapper.PriceToY(drawing.StopPrice));
    }
}
