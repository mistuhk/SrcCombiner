namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// The bounded undo and redo stacks. Holds no Esri types and performs no input or output, so
/// the ordering, eviction and invalidation rules are unit testable on their own.
/// Every member is safe to call from any thread.
/// </summary>
public interface IEditHistoryJournal
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    int UndoDepth { get; }
    int RedoDepth { get; }

    /// <summary>
    /// Records a new operation, evicting the oldest entries past the depth or size budget and
    /// clearing redo. Returns false, leaving the history untouched, when the operation on its
    /// own is larger than the whole budget and so cannot be held at all. The caller is
    /// expected to tell the user that this one operation cannot be undone.
    /// </summary>
    bool Record(EditOperationEntry entry);

    EditOperationEntry? PeekUndo();
    EditOperationEntry? PeekRedo();

    /// <summary>Moves the most recent entry from the undo stack to the redo stack.</summary>
    EditOperationEntry? PopUndoToRedo();

    /// <summary>Moves the most recent entry from the redo stack back to the undo stack.</summary>
    EditOperationEntry? PopRedoToUndo();

    /// <summary>Discards the most recent undo entry without offering it for redo.</summary>
    EditOperationEntry? DiscardUndo();

    /// <summary>Discards every redo entry.</summary>
    void ClearRedo();

    /// <summary>
    /// Rewrites a stale object id across both stacks after an inverse insert was assigned a
    /// new one by the service.
    /// </summary>
    void RemapObjectId(string layerName, long oldObjectId, long newObjectId);

    /// <summary>Drops every entry in either stack that touches any of the given rows.</summary>
    int DropEntriesReferencing(IReadOnlyCollection<FeatureIdentity> identities);

    void Clear();

    event EventHandler? Changed;
}
