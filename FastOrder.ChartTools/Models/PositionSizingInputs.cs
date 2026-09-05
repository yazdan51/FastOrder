using FastOrder.ChartTools.Calculations;

namespace FastOrder.ChartTools.Models;

public sealed record PositionSizingInputs
{
    public PositionSizingInputs(
        decimal accountSize,
        RiskInputMode riskMode,
        decimal riskValue,
        decimal leverage)
    {
        EnsurePositive(accountSize, nameof(accountSize));
        EnsurePositive(riskValue, nameof(riskValue));
        EnsurePositive(leverage, nameof(leverage));

        if (!Enum.IsDefined(riskMode))
        {
            throw new ArgumentOutOfRangeException(nameof(riskMode), riskMode, "Unknown risk input mode.");
        }

        AccountSize = accountSize;
        RiskMode = riskMode;
        RiskValue = riskValue;
        Leverage = leverage;
    }

    public decimal AccountSize { get; }

    public RiskInputMode RiskMode { get; }

    public decimal RiskValue { get; }

    public decimal Leverage { get; }

    public PositionSizingInputs WithAccountSize(decimal accountSize) =>
        new(accountSize, RiskMode, RiskValue, Leverage);

    public PositionSizingInputs WithRiskMode(RiskInputMode riskMode) =>
        new(AccountSize, riskMode, RiskValue, Leverage);

    public PositionSizingInputs WithRiskValue(decimal riskValue) =>
        new(AccountSize, RiskMode, riskValue, Leverage);

    public PositionSizingInputs WithLeverage(decimal leverage) =>
        new(AccountSize, RiskMode, RiskValue, leverage);

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
        }
    }
}
