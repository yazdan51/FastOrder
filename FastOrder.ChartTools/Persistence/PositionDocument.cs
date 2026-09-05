using FastOrder.ChartTools.Calculations;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Persistence;

public sealed record PositionDocument
{
    public int Version { get; init; }

    public IReadOnlyList<PositionDocumentItem>? Positions { get; init; }
}

public sealed record PositionDocumentItem
{
    public Guid Id { get; init; }

    public string? SymbolId { get; init; }

    public string? Timeframe { get; init; }

    public PositionSide Side { get; init; }

    public decimal EntryPrice { get; init; }

    public decimal StopPrice { get; init; }

    public decimal TargetPrice { get; init; }

    public double StartTime { get; init; }

    public double EndTime { get; init; }

    public decimal AccountSize { get; init; }

    public RiskInputMode RiskMode { get; init; }

    public decimal RiskValue { get; init; }

    public decimal Leverage { get; init; }

    public decimal TickSize { get; init; }

    public decimal QuantityStep { get; init; }

    public decimal MinimumQuantity { get; init; }

    public int QuantityPrecision { get; init; }

    public decimal PointValue { get; init; }

    public decimal LotSize { get; init; }
}
