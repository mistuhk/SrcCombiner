namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// What the Undo and Redo buttons talk to. It owns the sequencing and the safety checks and
/// keeps the journal, the conflict check and the applier behind it, so the toolbar knows
/// nothing about how an operation is reversed.
/// </summary>
public interface IEditHistoryService
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    Task UndoAsync();
    Task RedoAsync();

    /// <summary>
    /// Forgets everything recorded. Called when the case changes, because history recorded
    /// against one case must never be applied against another.
    /// </summary>
    void ClearHistory();
}
