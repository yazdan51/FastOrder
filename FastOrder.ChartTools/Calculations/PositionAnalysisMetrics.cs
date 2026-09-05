namespace FastOrder.ChartTools.Calculations;

public readonly record struct PositionAnalysisMetrics(
    RiskRewardMetrics RiskReward,
    PositionSizingResult Sizing,
    PositionPnlMetrics Pnl);
