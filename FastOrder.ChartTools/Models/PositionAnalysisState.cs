using FastOrder.ChartTools.Markets;

namespace FastOrder.ChartTools.Models;

public sealed record PositionAnalysisState
{
    public PositionAnalysisState(
        PositionDrawing drawing,
        string timeframe,
        PositionSizingInputs sizingInputs,
        SymbolMetadata symbolMetadata)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(sizingInputs);
        ArgumentNullException.ThrowIfNull(symbolMetadata);

        if (string.IsNullOrWhiteSpace(timeframe))
        {
            throw new ArgumentException("A timeframe is required.", nameof(timeframe));
        }

        var normalizedTimeframe = timeframe.Trim();
        if (normalizedTimeframe.Length > 32)
        {
            throw new ArgumentException("Timeframe cannot exceed 32 characters.", nameof(timeframe));
        }

        Drawing = drawing;
        Timeframe = normalizedTimeframe;
        SizingInputs = sizingInputs;
        SymbolMetadata = symbolMetadata;
    }

    public Guid Id => Drawing.Id;

    public PositionDrawing Drawing { get; }

    public string Timeframe { get; }

    public PositionSizingInputs SizingInputs { get; }

    public SymbolMetadata SymbolMetadata { get; }

    public PositionAnalysisState WithDrawing(PositionDrawing drawing) =>
        new(drawing, Timeframe, SizingInputs, SymbolMetadata);

    public PositionAnalysisState WithSizingInputs(PositionSizingInputs sizingInputs) =>
        new(Drawing, Timeframe, sizingInputs, SymbolMetadata);

    public PositionAnalysisState WithSymbolMetadata(SymbolMetadata symbolMetadata) =>
        new(Drawing, Timeframe, SizingInputs, symbolMetadata);
}
