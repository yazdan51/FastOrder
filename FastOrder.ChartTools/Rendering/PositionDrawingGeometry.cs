namespace FastOrder.ChartTools.Rendering;

public readonly record struct PositionDrawingGeometry
{
    public PositionDrawingGeometry(
        double leftX,
        double rightX,
        double targetY,
        double entryY,
        double stopY)
    {
        EnsureFinite(leftX, nameof(leftX));
        EnsureFinite(rightX, nameof(rightX));
        EnsureFinite(targetY, nameof(targetY));
        EnsureFinite(entryY, nameof(entryY));
        EnsureFinite(stopY, nameof(stopY));

        if (rightX <= leftX)
        {
            throw new ArgumentException("The rendered right edge must be greater than the left edge.", nameof(rightX));
        }

        LeftX = leftX;
        RightX = rightX;
        TargetY = targetY;
        EntryY = entryY;
        StopY = stopY;
    }

    public double LeftX { get; }

    public double RightX { get; }

    public double TargetY { get; }

    public double EntryY { get; }

    public double StopY { get; }

    private static void EnsureFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Rendered coordinates must be finite.");
        }
    }
}
