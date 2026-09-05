namespace FastOrder.ChartTools.Calculations;

public readonly record struct RiskRewardMetrics(
    decimal RiskPerUnit,
    decimal RewardPerUnit,
    decimal RiskPercent,
    decimal RewardPercent,
    decimal RewardToRiskRatio);
