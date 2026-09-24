namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// One primitive row delta. Every committed operation is recorded as an ordered list of
/// these, which is what lets a single generic applier reverse merge, split, delete and the
/// multi table creates without an inverse handler per operation family.
/// </summary>
public sealed record FeatureChange(
    FeatureChangeKind Kind,
    FeatureSnapshot? Before,
    FeatureSnapshot? After)
{
    public static FeatureChange Insert(FeatureSnapshot after) => new(FeatureChangeKind.Insert, null, after);

    public static FeatureChange Update(FeatureSnapshot before, FeatureSnapshot after) =>
        new(FeatureChangeKind.Update, before, after);

    public static FeatureChange Delete(FeatureSnapshot before) => new(FeatureChangeKind.Delete, before, null);

    /// <summary>
    /// The snapshot describing the row as it should exist after this change was applied, or
    /// the pre-edit snapshot for a delete, which is the only thing left to identify it by.
    /// </summary>
    public FeatureSnapshot Reference => After ?? Before
        ?? throw new InvalidOperationException("A feature change must carry a before or an after snapshot.");

    public FeatureIdentity Identity => Reference.Identity;

    /// <summary>
    /// The compensating change. An insert is undone by deleting, a delete by inserting the
    /// captured row, and an update by writing the previous state back.
    /// </summary>
    public FeatureChange Inverse() => Kind switch
    {
        FeatureChangeKind.Insert => new FeatureChange(FeatureChangeKind.Delete, After, null),
        FeatureChangeKind.Delete => new FeatureChange(FeatureChangeKind.Insert, null, Before),
        FeatureChangeKind.Update => new FeatureChange(FeatureChangeKind.Update, After, Before),
        _ => throw new InvalidOperationException($"Unsupported change kind: {Kind}.")
    };

    public long ApproximateSizeInBytes() =>
        (Before?.ApproximateSizeInBytes() ?? 0) + (After?.ApproximateSizeInBytes() ?? 0);
}
