namespace FastOrder.ChartTools.Calculations;

public readonly record struct PositionPnlMetrics(
    decimal ProfitPnl,
    decimal LossPnl,
    decimal ProfitAccountBalance,
    decimal StopAccountBalance);
