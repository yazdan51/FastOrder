using System.Text.Json;
using FastOrder.ChartTools.Calculations;
using FastOrder.ChartTools.Interaction;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;
using FastOrder.ChartTools.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FastOrder.ChartTools.Tests;

[TestClass]
public sealed class PositionStateAndPersistenceTests
{
    private static readonly IranMarketNormalizationAdapter Normalizer = new();

    [TestMethod]
    public void AnalysisCalculator_ProducesSizingPnlAndBalanceMetrics()
    {
        var position = CreateState(
            PositionSide.Long,
            entry: 100m,
            target: 120m,
            stop: 95m,
            accountSize: 10_000m,
            riskValue: 1m,
            leverage: 2m);

        var metrics = PositionAnalysisCalculator.Calculate(position, Normalizer);

        Assert.AreEqual(100m, metrics.Sizing.RiskAmount);
        Assert.AreEqual(20m, metrics.Sizing.QuantityByRisk);
        Assert.AreEqual(200m, metrics.Sizing.QuantityByLeverage);
        Assert.AreEqual(20m, metrics.Sizing.FinalQuantity);
        Assert.AreEqual(400m, metrics.Pnl.ProfitPnl);
        Assert.AreEqual(-100m, metrics.Pnl.LossPnl);
        Assert.AreEqual(10_400m, metrics.Pnl.ProfitAccountBalance);
        Assert.AreEqual(9_900m, metrics.Pnl.StopAccountBalance);
    }

    [TestMethod]
    public void Workspace_MultiplePositionsRemainIndependentAndDeleteById()
    {
        var workspace = new PositionWorkspace();
        var longPosition = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var shortPosition = CreateState(PositionSide.Short, 200m, 180m, 210m);
        workspace.Add(longPosition);
        workspace.Add(shortPosition);

        var editedLong = longPosition.WithSizingInputs(
            longPosition.SizingInputs.WithAccountSize(25_000m));
        workspace.Update(editedLong);

        Assert.AreEqual(2, workspace.Count);
        Assert.AreEqual(25_000m, workspace.GetRequired(longPosition.Id).SizingInputs.AccountSize);
        Assert.AreEqual(10_000m, workspace.GetRequired(shortPosition.Id).SizingInputs.AccountSize);
        Assert.IsTrue(workspace.Remove(longPosition.Id));
        Assert.AreEqual(1, workspace.Count);
        Assert.AreEqual(shortPosition, workspace.GetRequired(shortPosition.Id));
    }

    [TestMethod]
    public void Workspace_ReplaceAllRejectsDuplicateIdentifiersWithoutChangingExistingState()
    {
        var workspace = new PositionWorkspace();
        var existing = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var duplicate = CreateState(PositionSide.Short, 200m, 180m, 210m);
        workspace.Add(existing);

        Assert.ThrowsExactly<ArgumentException>(
            () => workspace.ReplaceAll([duplicate, duplicate]));
        Assert.AreEqual(1, workspace.Count);
        Assert.AreEqual(existing, workspace.GetRequired(existing.Id));
    }

    [TestMethod]
    public void Selection_SelectUpdateAndDeleteRemainIsolatedByIdentifier()
    {
        var workspace = new PositionWorkspace();
        var selection = new PositionSelectionState();
        var longPosition = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var shortPosition = CreateState(PositionSide.Short, 200m, 180m, 210m);
        workspace.Add(longPosition);
        workspace.Add(shortPosition);

        selection.Select(workspace, longPosition.Id);
        var selected = selection.GetSelectedRequired(workspace, longPosition.Id);
        workspace.Update(selected.WithSizingInputs(selected.SizingInputs.WithAccountSize(25_000m)));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => selection.GetSelectedRequired(workspace, shortPosition.Id));
        Assert.AreEqual(25_000m, workspace.GetRequired(longPosition.Id).SizingInputs.AccountSize);
        Assert.AreEqual(10_000m, workspace.GetRequired(shortPosition.Id).SizingInputs.AccountSize);
        Assert.IsTrue(selection.RemoveSelected(workspace, longPosition.Id));
        Assert.IsNull(selection.SelectedId);
        Assert.AreEqual(shortPosition, workspace.GetRequired(shortPosition.Id));
    }

    [TestMethod]
    public void Selection_ReconcilePreservesExistingSelectionOrChoosesAvailablePosition()
    {
        var workspace = new PositionWorkspace();
        var selection = new PositionSelectionState();
        var first = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var second = CreateState(PositionSide.Short, 200m, 180m, 210m);
        workspace.Add(first);
        workspace.Add(second);
        selection.Select(workspace, second.Id);

        selection.Reconcile(workspace);
        Assert.AreEqual(second.Id, selection.SelectedId);

        workspace.ReplaceAll([first]);
        selection.Reconcile(workspace);
        Assert.AreEqual(first.Id, selection.SelectedId);

        workspace.ReplaceAll([]);
        selection.Reconcile(workspace);
        Assert.IsNull(selection.SelectedId);
    }

    [TestMethod]
    public void Persistence_RoundTripPreservesSourceFieldsAndOmitsDerivedMetrics()
    {
        var longPosition = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var shortPosition = CreateState(PositionSide.Short, 200m, 180m, 210m);

        var json = PositionDocumentSerializer.Serialize([longPosition, shortPosition]);
        var restored = PositionDocumentSerializer.Deserialize(json);

        Assert.AreEqual(2, restored.Count);
        CollectionAssert.AreEquivalent(
            new[] { longPosition, shortPosition },
            restored.ToArray());
        StringAssert.Contains(json, "\"version\": 1");
        StringAssert.Contains(json, "\"symbolId\": \"IR_DEMO_MOCK\"");
        StringAssert.Contains(json, "\"timeframe\": \"1m\"");
        Assert.IsFalse(json.Contains("finalQuantity", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("profitPnl", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("rewardToRiskRatio", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Persistence_RoundTripPreservesIndependentHorizontalRanges()
    {
        var first = CreateState(PositionSide.Long, 100m, 120m, 90m);
        var second = CreateState(PositionSide.Short, 200m, 180m, 210m);
        first = first.WithDrawing(first.Drawing.WithHorizontalRange(new ChartHorizontalRange(100, 220)));
        second = second.WithDrawing(second.Drawing.WithHorizontalRange(new ChartHorizontalRange(300, 480)));

        var restored = PositionDocumentSerializer.Deserialize(
            PositionDocumentSerializer.Serialize([first, second]));

        Assert.AreEqual(
            new ChartHorizontalRange(100, 220),
            restored.Single(item => item.Id == first.Id).Drawing.HorizontalRange);
        Assert.AreEqual(
            new ChartHorizontalRange(300, 480),
            restored.Single(item => item.Id == second.Id).Drawing.HorizontalRange);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"version\":1,\"positions\":[],\"unknown\":true}")]
    [DataRow("{\"version\":1,\"positions\":[],}")]
    public void Persistence_RejectsMalformedOrNonSchemaJson(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => PositionDocumentSerializer.Deserialize(json));
    }

    [TestMethod]
    public void Persistence_RejectsDuplicateIdentifiers()
    {
        var position = CreateState(PositionSide.Long, 100m, 120m, 90m);

        Assert.ThrowsExactly<JsonException>(
            () => PositionDocumentSerializer.Serialize([position, position]));
    }

    [TestMethod]
    public void Persistence_RejectsUnknownDocumentVersion()
    {
        const string json = "{\"version\":2,\"positions\":[]}";

        Assert.ThrowsExactly<NotSupportedException>(
            () => PositionDocumentSerializer.Deserialize(json));
    }

    [TestMethod]
    public void Persistence_RejectsDocumentsAboveTheWorkspaceLimit()
    {
        var positions = Enumerable.Range(0, PositionWorkspace.MaximumPositions + 1)
            .Select(_ => CreateState(PositionSide.Long, 100m, 120m, 90m))
            .ToArray();

        Assert.ThrowsExactly<ArgumentException>(
            () => PositionDocumentSerializer.Serialize(positions));
    }

    [TestMethod]
    public void SymbolMetadata_InfersPrecisionAndRejectsAnIncompatibleExplicitPrecision()
    {
        var symbol = new SymbolMetadata("TEST", 1m, 0.25m, 0.25m, 1m, 1m);

        Assert.AreEqual(2, symbol.QuantityPrecision);
        Assert.ThrowsExactly<ArgumentException>(
            () => new SymbolMetadata("TEST", 1m, 0.25m, 0.25m, 1m, 1m, quantityPrecision: 1));
    }

    [TestMethod]
    public void IranAdapter_FloorsFractionalQuantityToConfiguredStep()
    {
        var symbol = new SymbolMetadata("TEST", 5m, 0.25m, 0.25m, 1m, 1m, quantityPrecision: 2);

        var quantity = Normalizer.NormalizeQuantityDown(10.49m, symbol);

        Assert.AreEqual(10.25m, quantity);
    }

    [TestMethod]
    public void RepeatedRealtimeStyleMetricReadsDoNotMutateWorkspacePositions()
    {
        var workspace = new PositionWorkspace();
        var position = CreateState(PositionSide.Long, 100m, 120m, 90m);
        workspace.Add(position);

        for (var index = 0; index < 10_000; index++)
        {
            _ = PositionAnalysisCalculator.Calculate(workspace.GetRequired(position.Id), Normalizer);
        }

        Assert.AreEqual(position, workspace.GetRequired(position.Id));
    }

    private static PositionAnalysisState CreateState(
        PositionSide side,
        decimal entry,
        decimal target,
        decimal stop,
        decimal accountSize = 10_000m,
        decimal riskValue = 1m,
        decimal leverage = 2m)
    {
        var drawing = PositionDrawing.Create(
            side,
            entry,
            target,
            stop,
            new ChartHorizontalRange(1_700_000_000, 1_700_000_600));
        var inputs = new PositionSizingInputs(
            accountSize,
            RiskInputMode.PercentOfAccount,
            riskValue,
            leverage);
        var symbol = new SymbolMetadata(
            "IR_DEMO_MOCK",
            tickSize: 1m,
            quantityStep: 1m,
            minimumQuantity: 1m,
            pointValue: 1m,
            lotSize: 1m,
            quantityPrecision: 0);

        return new PositionAnalysisState(drawing, "1m", inputs, symbol);
    }
}
