namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>An object id the service replaced, so the journal can rewrite what it holds.</summary>
public sealed record ObjectIdRemap(string LayerName, long OldObjectId, long NewObjectId);

public sealed record ChangeApplyResult(
    bool Success,
    IReadOnlyList<ObjectIdRemap> Remaps,
    string? Error)
{
    public static ChangeApplyResult Applied(IReadOnlyList<ObjectIdRemap> remaps) => new(true, remaps, null);

    public static ChangeApplyResult Failed(string error) => new(false, [], error);
}

/// <summary>
/// Writes a list of row changes to the feature tables. Used for the inverse of an operation on
/// undo and for the operation itself on redo, which is the same code either way because a
/// change already knows how to invert itself.
/// </summary>
public interface IFeatureChangeApplier
{
    Task<ChangeApplyResult> ApplyAsync(IReadOnlyList<FeatureChange> changes);
}
