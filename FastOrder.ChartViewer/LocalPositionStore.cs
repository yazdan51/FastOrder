using System.IO;
using System.Text;
using FastOrder.ChartTools.Models;
using FastOrder.ChartTools.Persistence;

namespace FastOrder.ChartViewer;

internal sealed class LocalPositionStore
{
    private readonly string _filePath;

    public LocalPositionStore(string? rootPath = null)
    {
        var storageRoot = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastOrder",
            "ChartViewer");
        _filePath = Path.Combine(storageRoot, "positions.v1.json");
    }

    public string FilePath => _filePath;

    public async Task SaveAsync(
        IEnumerable<PositionAnalysisState> positions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var directory = Path.GetDirectoryName(_filePath) ??
            throw new InvalidOperationException("The local position storage path has no directory.");
        Directory.CreateDirectory(directory);

        var json = PositionDocumentSerializer.Serialize(positions);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<IReadOnlyList<PositionAnalysisState>?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(_filePath);
        if (fileInfo.Length > PositionDocumentSerializer.MaximumDocumentLength * 4L)
        {
            throw new InvalidDataException("The saved position document is too large.");
        }

        var json = await File.ReadAllTextAsync(_filePath, Encoding.UTF8, cancellationToken);
        return PositionDocumentSerializer.Deserialize(json);
    }
}
