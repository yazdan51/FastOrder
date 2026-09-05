namespace FastOrder.ChartTools.Calculations;

public readonly record struct PositionSizingResult(
    decimal RiskAmount,
    decimal QuantityByRisk,
    decimal QuantityByLeverage,
    decimal RawQuantity,
    decimal FinalQuantity);
