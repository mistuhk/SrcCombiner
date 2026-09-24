namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// One user visible operation, however many rows or tables it touched. A single entry is a
/// single press of undo. The case key is stamped at record time and re-checked before an
/// undo, so history recorded against one case can never be applied against another.
/// </summary>
public sealed record EditOperationEntry(
    Guid Id,
    string OperationName,
    string CaseKey,
    DateTime RecordedAtUtc,
    IReadOnlyList<FeatureChange> Changes)
{
    public static EditOperationEntry Create(
        string operationName,
        string caseKey,
        IReadOnlyList<FeatureChange> changes,
        DateTime recordedAtUtc) =>
        new(Guid.NewGuid(), operationName, caseKey, recordedAtUtc, changes);

    /// <summary>
    /// The changes needed to reverse this operation: every delta inverted, applied in reverse
    /// order so that child rows are removed before the parents they reference.
    /// </summary>
    public IReadOnlyList<FeatureChange> InverseChanges() =>
        [.. Changes.Reverse().Select(change => change.Inverse())];

    public long ApproximateSizeInBytes() => Changes.Sum(change => change.ApproximateSizeInBytes());

    public bool References(FeatureIdentity identity) =>
        Changes.Any(change => change.Identity.SameRowAs(identity));
}
