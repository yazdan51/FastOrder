namespace FastOrder.ChartTools.Coordinates;

public interface IChartCoordinateMapper
{
    double HorizontalValueToX(double horizontalValue);

    double XToHorizontalValue(double x);

    double PriceToY(decimal price);

    decimal YToPrice(double y);
}
