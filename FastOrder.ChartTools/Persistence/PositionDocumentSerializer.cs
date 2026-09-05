using System.Text.Json;
using System.Text.Json.Serialization;
using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Persistence;

public static class PositionDocumentSerializer
{
    public const int CurrentVersion = 1;
    public const int MaximumDocumentLength = 1_048_576;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IEnumerable<PositionAnalysisState> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var items = positions
            .Select(ToDocumentItem)
            .OrderBy(item => item.Id)
            .ToArray();
        if (items.Length > PositionWorkspace.MaximumPositions)
        {
            throw new ArgumentException(
                $"A position document cannot contain more than {PositionWorkspace.MaximumPositions} positions.",
                nameof(positions));
        }

        ValidateUniqueIdentifiers(items);

        var document = new PositionDocument
        {
            Version = CurrentVersion,
            Positions = items
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        if (json.Length > MaximumDocumentLength)
        {
            throw new ArgumentException(
                $"Position document exceeds the {MaximumDocumentLength}-character limit.",
                nameof(positions));
        }

        return json;
    }

    public static IReadOnlyList<PositionAnalysisState> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("A position document is required.", nameof(json));
        }

        if (json.Length > MaximumDocumentLength)
        {
            throw new ArgumentException(
                $"Position document exceeds the {MaximumDocumentLength}-character limit.",
                nameof(json));
        }

        var document = JsonSerializer.Deserialize<PositionDocument>(json, SerializerOptions) ??
            throw new JsonException("Position document could not be read.");

        if (document.Version != CurrentVersion)
        {
            throw new NotSupportedException(
                $"Position document version {document.Version} is not supported. Expected {CurrentVersion}.");
        }

        var items = document.Positions ?? throw new JsonException("Position document requires a positions array.");
        if (items.Count > PositionWorkspace.MaximumPositions)
        {
            throw new JsonException(
                $"Position document cannot contain more than {PositionWorkspace.MaximumPositions} positions.");
        }

        ValidateUniqueIdentifiers(items);
        return items.Select(FromDocumentItem).ToArray();
    }

    private static PositionDocumentItem ToDocumentItem(PositionAnalysisState position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var drawing = position.Drawing;
        var inputs = position.SizingInputs;
        var symbol = position.SymbolMetadata;

        return new PositionDocumentItem
        {
            Id = drawing.Id,
            SymbolId = symbol.Symbol,
            Timeframe = position.Timeframe,
            Side = drawing.Side,
            EntryPrice = drawing.EntryPrice,
            StopPrice = drawing.StopPrice,
            TargetPrice = drawing.TargetPrice,
            StartTime = drawing.HorizontalRange.Start,
            EndTime = drawing.HorizontalRange.End,
            AccountSize = inputs.AccountSize,
            RiskMode = inputs.RiskMode,
            RiskValue = inputs.RiskValue,
            Leverage = inputs.Leverage,
            TickSize = symbol.TickSize,
            QuantityStep = symbol.QuantityStep,
            MinimumQuantity = symbol.MinimumQuantity,
            QuantityPrecision = symbol.QuantityPrecision,
            PointValue = symbol.PointValue,
            LotSize = symbol.LotSize
        };
    }

    private static PositionAnalysisState FromDocumentItem(PositionDocumentItem item)
    {
        if (item.Id == Guid.Empty)
        {
            throw new JsonException("Every saved position requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(item.SymbolId) || item.SymbolId.Trim().Length > 64)
        {
            throw new JsonException("Every saved position requires a symbol id of at most 64 characters.");
        }

        if (string.IsNullOrWhiteSpace(item.Timeframe))
        {
            throw new JsonException("Every saved position requires a timeframe.");
        }

        try
        {
            var drawing = new PositionDrawing(
                item.Id,
                item.Side,
                item.EntryPrice,
                item.TargetPrice,
                item.StopPrice,
                new ChartHorizontalRange(item.StartTime, item.EndTime));
            var inputs = new PositionSizingInputs(
                item.AccountSize,
                item.RiskMode,
                item.RiskValue,
                item.Leverage);
            var symbol = new SymbolMetadata(
                item.SymbolId,
                item.TickSize,
                item.QuantityStep,
                item.MinimumQuantity,
                item.PointValue,
                item.LotSize,
                item.QuantityPrecision);

            return new PositionAnalysisState(drawing, item.Timeframe, inputs, symbol);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new JsonException($"Saved position {item.Id} is invalid.", exception);
        }
    }

    private static void ValidateUniqueIdentifiers(IEnumerable<PositionDocumentItem> items)
    {
        var identifiers = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new JsonException("Position document cannot contain null items.");
            }

            if (!identifiers.Add(item.Id))
            {
                throw new JsonException($"Position id {item.Id} is duplicated.");
            }
        }
    }
}
