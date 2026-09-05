using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Calculations;

public sealed record PositionSizingRequest(
    PositionSide Side,
    decimal AccountSize,
    RiskInputMode RiskMode,
    decimal RiskValue,
    decimal EntryPrice,
    decimal StopPrice,
    decimal Leverage);
