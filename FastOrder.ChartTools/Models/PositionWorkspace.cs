using System.Diagnostics.CodeAnalysis;

namespace FastOrder.ChartTools.Models;

public sealed class PositionWorkspace
{
    public const int MaximumPositions = 500;
    private readonly Dictionary<Guid, PositionAnalysisState> _positions = [];

    public int Count => _positions.Count;

    public IReadOnlyList<PositionAnalysisState> Positions => [.. _positions.Values];

    public void Add(PositionAnalysisState position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (_positions.Count >= MaximumPositions)
        {
            throw new InvalidOperationException($"A workspace cannot contain more than {MaximumPositions} positions.");
        }

        if (!_positions.TryAdd(position.Id, position))
        {
            throw new ArgumentException("A position with the same identifier already exists.", nameof(position));
        }
    }

    public PositionAnalysisState GetRequired(Guid id) =>
        _positions.TryGetValue(id, out var position)
            ? position
            : throw new ArgumentException("Position was not found.", nameof(id));

    public bool TryGet(Guid id, [NotNullWhen(true)] out PositionAnalysisState? position) =>
        _positions.TryGetValue(id, out position);

    public void Update(PositionAnalysisState position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (!_positions.ContainsKey(position.Id))
        {
            throw new ArgumentException("Position was not found.", nameof(position));
        }

        _positions[position.Id] = position;
    }

    public bool Remove(Guid id) => _positions.Remove(id);

    public void ReplaceAll(IEnumerable<PositionAnalysisState> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var replacement = new Dictionary<Guid, PositionAnalysisState>();
        foreach (var position in positions)
        {
            ArgumentNullException.ThrowIfNull(position);

            if (replacement.Count >= MaximumPositions)
            {
                throw new ArgumentException(
                    $"A workspace cannot contain more than {MaximumPositions} positions.",
                    nameof(positions));
            }

            if (!replacement.TryAdd(position.Id, position))
            {
                throw new ArgumentException("Position identifiers must be unique.", nameof(positions));
            }
        }

        _positions.Clear();
        foreach (var pair in replacement)
        {
            _positions.Add(pair.Key, pair.Value);
        }
    }
}
