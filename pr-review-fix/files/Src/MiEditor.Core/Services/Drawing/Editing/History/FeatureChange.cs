namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// One primitive row delta. Every committed operation is recorded as an ordered list of
/// these, which is what lets a single generic applier reverse merge, split, delete and the
/// multi table creates without an inverse handler per operation family.
/// <para>
/// Which snapshots a change carries is fixed by its kind: an insert has only an after, a
/// delete has only a before, an update has both. That pairing is checked on construction,
/// so a change that reaches any other code is known to be well formed and Reference,
/// Identity and Inverse can rely on it.
/// </para>
/// </summary>
public sealed record FeatureChange(
    FeatureChangeKind Kind,
    FeatureSnapshot? Before,
    FeatureSnapshot? After)
{
    // A field initialiser runs inside the primary constructor, so this validates every
    // direct construction. Build changes through the factories below rather than with a
    // with expression, which copies fields and would not re-run this.
    private readonly bool wellFormed = Validate(Kind, Before, After);

    public static FeatureChange Insert(FeatureSnapshot after) => new(FeatureChangeKind.Insert, null, after);

    public static FeatureChange Update(FeatureSnapshot before, FeatureSnapshot after) =>
        new(FeatureChangeKind.Update, before, after);

    public static FeatureChange Delete(FeatureSnapshot before) => new(FeatureChangeKind.Delete, before, null);

    /// <summary>
    /// The snapshot describing the row as it should exist after this change was applied, or
    /// the pre-edit snapshot for a delete, which is the only thing left to identify it by.
    /// </summary>
    public FeatureSnapshot Reference => After ?? Before!;

    public FeatureIdentity Identity => Reference.Identity;

    /// <summary>
    /// The compensating change. An insert is undone by deleting, a delete by inserting the
    /// captured row, and an update by writing the previous state back.
    /// </summary>
    public FeatureChange Inverse() => Kind switch
    {
        FeatureChangeKind.Insert => Delete(After!),
        FeatureChangeKind.Delete => Insert(Before!),
        FeatureChangeKind.Update => Update(After!, Before!),
        _ => throw new InvalidOperationException($"Unsupported change kind: {Kind}.")
    };

    /// <summary>
    /// The same change with its snapshots replaced, used when an object id is rewritten.
    /// Routed through the factories so the result is validated like any other change.
    /// </summary>
    public FeatureChange WithSnapshots(FeatureSnapshot? before, FeatureSnapshot? after) => Kind switch
    {
        FeatureChangeKind.Insert => Insert(after!),
        FeatureChangeKind.Delete => Delete(before!),
        FeatureChangeKind.Update => Update(before!, after!),
        _ => throw new InvalidOperationException($"Unsupported change kind: {Kind}.")
    };

    public long ApproximateSizeInBytes() =>
        (Before?.ApproximateSizeInBytes() ?? 0) + (After?.ApproximateSizeInBytes() ?? 0);

    private static bool Validate(FeatureChangeKind kind, FeatureSnapshot? before, FeatureSnapshot? after)
    {
        var ok = kind switch
        {
            FeatureChangeKind.Insert => before is null && after is not null,
            FeatureChangeKind.Delete => before is not null && after is null,
            FeatureChangeKind.Update => before is not null && after is not null,
            _ => false
        };

        return ok
            ? true
            : throw new ArgumentException(
                $"A {kind} change carries the wrong snapshots. Insert needs an after, delete needs a before, update needs both.");
    }
}
