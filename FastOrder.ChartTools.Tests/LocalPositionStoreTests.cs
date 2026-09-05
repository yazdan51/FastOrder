using System.Text.Json;
using FastOrder.ChartTools.Calculations;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;
using FastOrder.ChartViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FastOrder.ChartTools.Tests;

[TestClass]
public sealed class LocalPositionStoreTests
{
    private string? _testDirectory;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "FastOrder.ChartViewer.Tests",
            Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_testDirectory is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(_testDirectory);
        var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FastOrder.ChartViewer.Tests"));
        if (fullPath.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFileReturnsEmptyWorkspace()
    {
        var store = CreateStore();

        var loaded = await store.LoadAsync();

        Assert.IsFalse(store.Exists);
        Assert.AreEqual(0, loaded.Count);
    }

    [TestMethod]
    public async Task LoadAsync_CorruptJsonFailsWithoutReplacingCallerState()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_testDirectory!);
        await File.WriteAllTextAsync(store.FilePath, "{");
        var workspace = new PositionWorkspace();
        var existing = CreateState();
        workspace.Add(existing);

        await Assert.ThrowsExactlyAsync<JsonException>(() => store.LoadAsync());

        Assert.AreEqual(existing, workspace.GetRequired(existing.Id));
    }

    [TestMethod]
    public async Task SaveAsync_FailedSerializationPreservesExistingFileAndRemovesTemporaryFiles()
    {
        var store = CreateStore();
        var existing = CreateState();
        await store.SaveAsync([existing]);

        await Assert.ThrowsExactlyAsync<JsonException>(() => store.SaveAsync([existing, existing]));
        var loaded = await store.LoadAsync();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(existing, loaded[0]);
        Assert.AreEqual(0, Directory.GetFiles(_testDirectory!, "*.tmp").Length);
    }

    private LocalPositionStore CreateStore() => new(_testDirectory);

    private static PositionAnalysisState CreateState()
    {
        var drawing = PositionDrawing.Create(
            PositionSide.Long,
            100m,
            120m,
            90m,
            new ChartHorizontalRange(100, 220));
        var inputs = new PositionSizingInputs(10_000m, RiskInputMode.PercentOfAccount, 1m, 2m);
        var symbol = new SymbolMetadata("IR_DEMO_MOCK", 1m, 1m, 1m, 1m, 1m, 0);
        return new PositionAnalysisState(drawing, "1m", inputs, symbol);
    }
}
