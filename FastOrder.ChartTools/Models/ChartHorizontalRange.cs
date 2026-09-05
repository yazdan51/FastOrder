namespace FastOrder.ChartTools.Models;

public readonly record struct ChartHorizontalRange
{
    public ChartHorizontalRange(double start, double end)
    {
        if (!double.IsFinite(start))
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "The start coordinate must be finite.");
        }

        if (!double.IsFinite(end))
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "The end coordinate must be finite.");
        }

        if (end <= start)
        {
            throw new ArgumentException("The end coordinate must be greater than the start coordinate.", nameof(end));
        }

        if (!double.IsFinite(end - start))
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "The horizontal range width must be finite.");
        }

        Start = start;
        End = end;
    }

    public double Start { get; }

    public double End { get; }

    public double Width => End - Start;

    public bool IsValid =>
        double.IsFinite(Start) &&
        double.IsFinite(End) &&
        End > Start &&
        double.IsFinite(Width);

    public ChartHorizontalRange Translate(double delta)
    {
        if (!double.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "The translation must be finite.");
        }

        return new ChartHorizontalRange(Start + delta, End + delta);
    }
}
