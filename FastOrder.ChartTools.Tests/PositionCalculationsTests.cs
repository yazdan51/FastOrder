using FastOrder.ChartTools.Calculations;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FastOrder.ChartTools.Tests;

[TestClass]
public sealed class PositionCalculationsTests
{
    private static readonly IranMarketNormalizationAdapter Normalizer = new();

    [TestMethod]
    public void RiskReward_Long_UsesDirectionalOffsetsAndEntryPercentages()
    {
        var drawing = CreateDrawing(PositionSide.Long, entry: 100m, target: 120m, stop: 95m);

        var result = RiskRewardCalculator.Calculate(drawing);

        Assert.AreEqual(5m, result.RiskPerUnit);
        Assert.AreEqual(20m, result.RewardPerUnit);
        Assert.AreEqual(5m, result.RiskPercent);
        Assert.AreEqual(20m, result.RewardPercent);
        Assert.AreEqual(4m, result.RewardToRiskRatio);
    }

    [TestMethod]
    public void RiskReward_Short_UsesDirectionalOffsetsAndEntryPercentages()
    {
        var drawing = CreateDrawing(PositionSide.Short, entry: 100m, target: 80m, stop: 110m);

        var result = RiskRewardCalculator.Calculate(drawing);

        Assert.AreEqual(10m, result.RiskPerUnit);
        Assert.AreEqual(20m, result.RewardPerUnit);
        Assert.AreEqual(10m, result.RiskPercent);
        Assert.AreEqual(20m, result.RewardPercent);
        Assert.AreEqual(2m, result.RewardToRiskRatio);
    }

    [TestMethod]
    public void Sizing_PercentRisk_ChoosesRiskLimitedQuantity()
    {
        var request = new PositionSizingRequest(
            PositionSide.Long,
            AccountSize: 10_000m,
            RiskInputMode.PercentOfAccount,
            RiskValue: 1m,
            EntryPrice: 100m,
            StopPrice: 95m,
            Leverage: 2m);

        var result = PositionSizingCalculator.Calculate(request, CreateSymbol(), Normalizer);

        Assert.AreEqual(100m, result.RiskAmount);
        Assert.AreEqual(20m, result.QuantityByRisk);
        Assert.AreEqual(200m, result.QuantityByLeverage);
        Assert.AreEqual(20m, result.RawQuantity);
        Assert.AreEqual(20m, result.FinalQuantity);
    }

    [TestMethod]
    public void Sizing_AbsoluteRisk_ChoosesLeverageLimitedQuantity()
    {
        var request = new PositionSizingRequest(
            PositionSide.Short,
            AccountSize: 10_000m,
            RiskInputMode.Absolute,
            RiskValue: 10_000m,
            EntryPrice: 100m,
            StopPrice: 110m,
            Leverage: 2m);

        var result = PositionSizingCalculator.Calculate(request, CreateSymbol(), Normalizer);

        Assert.AreEqual(1_000m, result.QuantityByRisk);
        Assert.AreEqual(200m, result.QuantityByLeverage);
        Assert.AreEqual(200m, result.FinalQuantity);
    }

    [TestMethod]
    public void Sizing_FinalQuantity_IsFlooredToStepAndMinimum()
    {
        var steppedSymbol = new SymbolMetadata("TEST", 1m, 3m, 3m, 1m, 1m);
        var request = new PositionSizingRequest(
            PositionSide.Long,
            AccountSize: 10_000m,
            RiskInputMode.Absolute,
            RiskValue: 100m,
            EntryPrice: 100m,
            StopPrice: 95m,
            Leverage: 2m);

        var result = PositionSizingCalculator.Calculate(request, steppedSymbol, Normalizer);

        Assert.AreEqual(20m, result.RawQuantity);
        Assert.AreEqual(18m, result.FinalQuantity);
        Assert.AreEqual(0m, Normalizer.NormalizeQuantityDown(2.99m, steppedSymbol));
    }

    [TestMethod]
    public void Pnl_Long_CalculatesProfitLossAndBalances()
    {
        var drawing = CreateDrawing(PositionSide.Long, entry: 100m, target: 120m, stop: 95m);

        var result = PositionPnlCalculator.Calculate(drawing, 1_000m, 10m, 1m, 1m);

        Assert.AreEqual(200m, result.ProfitPnl);
        Assert.AreEqual(-50m, result.LossPnl);
        Assert.AreEqual(1_200m, result.ProfitAccountBalance);
        Assert.AreEqual(950m, result.StopAccountBalance);
    }

    [TestMethod]
    public void Pnl_Short_CalculatesProfitLossAndBalances()
    {
        var drawing = CreateDrawing(PositionSide.Short, entry: 100m, target: 80m, stop: 110m);

        var result = PositionPnlCalculator.Calculate(drawing, 1_000m, 10m, 1m, 1m);

        Assert.AreEqual(200m, result.ProfitPnl);
        Assert.AreEqual(-100m, result.LossPnl);
        Assert.AreEqual(1_200m, result.ProfitAccountBalance);
        Assert.AreEqual(900m, result.StopAccountBalance);
    }

    [TestMethod]
    public void InvalidLongStop_IsRejected()
    {
        var request = new PositionSizingRequest(
            PositionSide.Long,
            AccountSize: 10_000m,
            RiskInputMode.Absolute,
            RiskValue: 100m,
            EntryPrice: 100m,
            StopPrice: 101m,
            Leverage: 1m);

        Assert.ThrowsExactly<ArgumentException>(
            () => PositionSizingCalculator.Calculate(request, CreateSymbol(), Normalizer));
    }

    private static PositionDrawing CreateDrawing(
        PositionSide side,
        decimal entry,
        decimal target,
        decimal stop) =>
        PositionDrawing.Create(side, entry, target, stop, new ChartHorizontalRange(1, 2));

    private static SymbolMetadata CreateSymbol() => new("TEST", 1m, 1m, 1m, 1m, 1m);
}
