namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

public enum EditHistoryDirection { Undo, Redo }

/// <summary>
/// The outcome of checking whether an operation can still be reversed. Conflicting names the
/// rows that had moved on, so the journal can drop every entry that touches them rather than
/// only the one in play.
/// </summary>
public sealed record ConflictResult(
    bool HasConflict,
    string Reason,
    IReadOnlyList<FeatureIdentity> Conflicting)
{
    public static ConflictResult None { get; } = new(false, string.Empty, []);

    public static ConflictResult Conflict(string reason, params FeatureIdentity[] conflicting) =>
        new(true, reason, conflicting);
}

/// <summary>
/// Compares the data as it stands now against what was recorded, so an undo cannot quietly
/// overwrite somebody else's work.
/// </summary>
public interface IEditConflictDetector
{
    Task<ConflictResult> CheckAsync(EditOperationEntry entry, EditHistoryDirection direction);
}
