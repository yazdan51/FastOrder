using FastOrder.ChartTools.Creation;
using FastOrder.ChartTools.Coordinates;
using FastOrder.ChartTools.Interaction;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;
using FastOrder.ChartTools.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FastOrder.ChartTools.Tests;

[TestClass]
public sealed class MarketAndInteractionTests
{
    private static readonly IranMarketNormalizationAdapter Normalizer = new();

    [TestMethod]
    public void IranAdapter_NormalizesPriceUsingSuppliedTick()
    {
        var symbol = new SymbolMetadata("TEST", 5m, 1m, 1m, 1m, 1m);

        Assert.AreEqual(100m, Normalizer.NormalizePrice(102m, symbol, StepRoundingMode.Down));
        Assert.AreEqual(105m, Normalizer.NormalizePrice(102.5m, symbol, StepRoundingMode.Nearest));
        Assert.AreEqual(105m, Normalizer.NormalizePrice(101m, symbol, StepRoundingMode.Up));
    }

    [TestMethod]
    public void IranAdapter_NormalizesQuantityDownUsingSuppliedStep()
    {
        var symbol = new SymbolMetadata("TEST", 1m, 5m, 5m, 1m, 1m);

        Assert.AreEqual(10m, Normalizer.NormalizeQuantityDown(12.9m, symbol));
        Assert.AreEqual(0m, Normalizer.NormalizeQuantityDown(4.99m, symbol));
    }

    [TestMethod]
    public void PocFactory_CreatesDocumentedOneAndTwoPercentLongDefaults()
    {
        var symbol = new SymbolMetadata("TEST", 1m, 1m, 1m, 1m, 1m);

        var drawing = PositionDrawingFactory.CreatePocDefault(
            PositionSide.Long,
            100m,
            new ChartHorizontalRange(1, 2),
            symbol,
            Normalizer);

        Assert.AreEqual(100m, drawing.EntryPrice);
        Assert.AreEqual(99m, drawing.StopPrice);
        Assert.AreEqual(102m, drawing.TargetPrice);
    }

    [TestMethod]
    public void PocFactory_CreatesDocumentedOneAndTwoPercentShortDefaults()
    {
        var symbol = new SymbolMetadata("TEST", 1m, 1m, 1m, 1m, 1m);

        var drawing = PositionDrawingFactory.CreatePocDefault(
            PositionSide.Short,
            100m,
            new ChartHorizontalRange(1, 2),
            symbol,
            Normalizer);

        Assert.AreEqual(100m, drawing.EntryPrice);
        Assert.AreEqual(101m, drawing.StopPrice);
        Assert.AreEqual(98m, drawing.TargetPrice);
    }

    [TestMethod]
    public void Editor_ClampsCrossingTargetToOneTickFromEntry()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Long,
            100m,
            110m,
            90m,
            new ChartHorizontalRange(1, 2));

        var updated = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Target,
            80m,
            minimumPriceIncrement: 5m);

        Assert.AreEqual(105m, updated.TargetPrice);
        Assert.AreEqual(100m, updated.EntryPrice);
        Assert.AreEqual(90m, updated.StopPrice);
    }

    [TestMethod]
    public void Editor_ClampsEveryLongPriceHandleWithoutCrossingAdjacentLevels()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Long,
            100m,
            120m,
            80m,
            new ChartHorizontalRange(1, 2));

        var target = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Target,
            50m,
            minimumPriceIncrement: 5m);
        var entryBelowStop = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Entry,
            50m,
            minimumPriceIncrement: 5m);
        var entryAboveTarget = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Entry,
            150m,
            minimumPriceIncrement: 5m);
        var stop = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Stop,
            150m,
            minimumPriceIncrement: 5m);

        Assert.AreEqual(105m, target.TargetPrice);
        Assert.AreEqual(85m, entryBelowStop.EntryPrice);
        Assert.AreEqual(115m, entryAboveTarget.EntryPrice);
        Assert.AreEqual(95m, stop.StopPrice);
    }

    [TestMethod]
    public void Editor_ClampsEveryShortPriceHandleWithoutCrossingAdjacentLevels()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Short,
            100m,
            80m,
            120m,
            new ChartHorizontalRange(1, 2));

        var target = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Target,
            150m,
            minimumPriceIncrement: 5m);
        var entryBelowTarget = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Entry,
            50m,
            minimumPriceIncrement: 5m);
        var entryAboveStop = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Entry,
            150m,
            minimumPriceIncrement: 5m);
        var stop = PositionDrawingEditor.UpdatePriceClamped(
            drawing,
            PositionHandle.Stop,
            50m,
            minimumPriceIncrement: 5m);

        Assert.AreEqual(95m, target.TargetPrice);
        Assert.AreEqual(85m, entryBelowTarget.EntryPrice);
        Assert.AreEqual(115m, entryAboveStop.EntryPrice);
        Assert.AreEqual(105m, stop.StopPrice);
    }

    [TestMethod]
    public void Editor_MovePreservesIdentityAndTranslatesPriceAndTimeAnchors()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Long,
            100m,
            120m,
            80m,
            new ChartHorizontalRange(10, 20));

        var moved = PositionDrawingEditor.Move(drawing, priceDelta: 15m, horizontalDelta: 3);

        Assert.AreEqual(drawing.Id, moved.Id);
        Assert.AreEqual(115m, moved.EntryPrice);
        Assert.AreEqual(135m, moved.TargetPrice);
        Assert.AreEqual(95m, moved.StopPrice);
        Assert.AreEqual(13d, moved.HorizontalRange.Start);
        Assert.AreEqual(23d, moved.HorizontalRange.End);
    }

    [TestMethod]
    public void GeometryMapper_RecomputesPixelsFromStablePriceAndTimeAnchors()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Long,
            100m,
            120m,
            80m,
            new ChartHorizontalRange(10, 20));

        var initial = PositionGeometryMapper.Map(
            drawing,
            new TestCoordinateMapper(horizontalScale: 2d, priceScale: 1d));
        var zoomed = PositionGeometryMapper.Map(
            drawing,
            new TestCoordinateMapper(horizontalScale: 4d, priceScale: 0.5d));

        Assert.AreEqual(20d, initial.LeftX);
        Assert.AreEqual(40d, initial.RightX);
        Assert.AreEqual(120d, initial.TargetY);
        Assert.AreEqual(40d, zoomed.LeftX);
        Assert.AreEqual(80d, zoomed.RightX);
        Assert.AreEqual(60d, zoomed.TargetY);
        Assert.AreEqual(10d, drawing.HorizontalRange.Start);
        Assert.AreEqual(100m, drawing.EntryPrice);
    }

    [TestMethod]
    public void PositionDrawing_RejectsInvalidLongOrdering()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PositionDrawing.Create(
            PositionSide.Long,
            100m,
            99m,
            90m,
            new ChartHorizontalRange(1, 2)));
    }

    private sealed class TestCoordinateMapper(
        double horizontalScale,
        double priceScale) : IChartCoordinateMapper
    {
        public double HorizontalValueToX(double horizontalValue) => horizontalValue * horizontalScale;

        public double XToHorizontalValue(double x) => x / horizontalScale;

        public double PriceToY(decimal price) => (double)price * priceScale;

        public decimal YToPrice(double y) => (decimal)(y / priceScale);
    }
}
