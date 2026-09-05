using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Interaction;

public sealed class PositionSelectionState
{
    public Guid? SelectedId { get; private set; }

    public void Select(PositionWorkspace workspace, Guid? positionId)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (positionId is Guid id && !workspace.TryGet(id, out _))
        {
            throw new ArgumentException("The selected position was not found.", nameof(positionId));
        }

        SelectedId = positionId;
    }

    public PositionAnalysisState GetSelectedRequired(PositionWorkspace workspace, Guid requestedPositionId)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (SelectedId != requestedPositionId)
        {
            throw new InvalidOperationException("The requested position is not selected.");
        }

        return workspace.GetRequired(requestedPositionId);
    }

    public bool RemoveSelected(PositionWorkspace workspace, Guid requestedPositionId)
    {
        _ = GetSelectedRequired(workspace, requestedPositionId);
        var removed = workspace.Remove(requestedPositionId);
        if (removed)
        {
            SelectedId = null;
        }

        return removed;
    }

    public void Reconcile(PositionWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (SelectedId is Guid selectedId && workspace.TryGet(selectedId, out _))
        {
            return;
        }

        SelectedId = workspace.Positions.Count > 0 ? workspace.Positions[0].Id : null;
    }
}
