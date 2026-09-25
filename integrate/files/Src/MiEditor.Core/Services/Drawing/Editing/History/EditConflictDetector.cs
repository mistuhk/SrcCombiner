using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Refuses an undo or a redo when the rows it would touch no longer look the way the history
/// recorded them, which means somebody else has edited them in the meantime.
/// <para>
/// The comparison is on the layer's own last changed attribute, which differs by layer, plus the
/// geometry. Both sides are produced by FeatureStateReader so they cannot drift on formatting.
/// </para>
/// <para>
/// The live rows are fetched one query per layer and key strategy rather than one per change. A
/// single feature is the same either way, but an operation touching twenty rows would otherwise
/// cost twenty sequential round trips before anything could be applied.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditConflictDetector) })]
public sealed class EditConflictDetector(IFeatureStateReader stateReader) : IEditConflictDetector
{
    public async Task<ConflictResult> CheckAsync(EditOperationEntry entry, EditHistoryDirection direction)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Changes.Count == 0)
        {
            return ConflictResult.None;
        }

        var live = await ReadLiveRowsAsync(entry);

        foreach (var change in entry.Changes)
        {
            var result = Check(change, direction, live);

            if (result.HasConflict)
            {
                return result;
            }
        }

        return ConflictResult.None;
    }

    /// <summary>
    /// Every row the entry mentions, as it stands now. Rows that have since been deleted are
    /// simply absent, which is what the checks below rely on.
    /// </summary>
    private async Task<List<FeatureSnapshot>> ReadLiveRowsAsync(EditOperationEntry entry)
    {
        var live = new List<FeatureSnapshot>();

        var byLayer = entry.Changes
            .Select(c => c.Identity)
            .GroupBy(i => i.LayerName, StringComparer.OrdinalIgnoreCase);

        foreach (var layer in byLayer)
        {
            // Grouped by key strategy as well as by layer. A keyed layer still yields a keyless
            // identity whenever the key attribute is null, and the two need different fields, so a
            // mixed batch would leave some rows unqueried and they would read as deleted.
            foreach (var sameStrategy in layer.GroupBy(i => i.HasBusinessKey))
            {
                // Distinct because an operation can touch the same row more than once, and reading
                // it twice would tell us nothing new.
                live.AddRange(await stateReader.ReadManyAsync(layer.Key, Distinct(sameStrategy)));
            }
        }

        return live;
    }

    /// <summary>
    /// Deduplicated on SameRowAs rather than on equality, because two identities can name the same
    /// row while differing on the object id, which is exactly what happens after a restore.
    /// </summary>
    private static List<FeatureIdentity> Distinct(IEnumerable<FeatureIdentity> identities)
    {
        var distinct = new List<FeatureIdentity>();

        foreach (var identity in identities)
        {
            if (!distinct.Any(seen => seen.SameRowAs(identity)))
            {
                distinct.Add(identity);
            }
        }

        return distinct;
    }

    private static ConflictResult Check(
        FeatureChange change,
        EditHistoryDirection direction,
        List<FeatureSnapshot> live)
    {
        // Undoing compares against what the operation left behind; redoing compares against what
        // it started from, because an undo has since put the row back to that.
        var expected = direction == EditHistoryDirection.Undo ? change.After : change.Before;

        // No expected state means the row is not supposed to exist at this point, so the check is
        // that nothing has appeared in its place.
        if (expected is null)
        {
            var reference = change.Reference;

            return Find(live, reference.Identity) is null
                ? ConflictResult.None
                : ConflictResult.Conflict(
                    "A feature this operation had removed has been created again by someone else.",
                    reference.Identity);
        }

        var current = Find(live, expected.Identity);

        if (current is null)
        {
            return ConflictResult.Conflict(
                "A feature this operation touched has been deleted by someone else.",
                expected.Identity);
        }

        if (current.StampValue != expected.StampValue)
        {
            return ConflictResult.Conflict(
                "A feature this operation touched has been changed by someone else.",
                expected.Identity);
        }

        return GeometryMatches(expected.GeometryJson, current.GeometryJson)
            ? ConflictResult.None
            : ConflictResult.Conflict(
                "The shape of a feature this operation touched has been changed by someone else.",
                expected.Identity);
    }

    private static FeatureSnapshot? Find(List<FeatureSnapshot> live, FeatureIdentity identity) =>
        live.FirstOrDefault(s => s.Identity.SameRowAs(identity));

    /// <summary>
    /// Compared as text first, which is the common case and costs nothing. Only when that differs
    /// is the geometry rebuilt and compared properly, because the service can return the same
    /// shape written differently and that must not read as somebody else's edit.
    /// </summary>
    private static bool GeometryMatches(string? expected, string? live)
    {
        if (string.Equals(expected, live, StringComparison.Ordinal))
        {
            return true;
        }

        if (expected is null || live is null)
        {
            return false;
        }

        var expectedGeometry = Geometry.FromJson(expected);
        var liveGeometry = Geometry.FromJson(live);

        return expectedGeometry is not null
            && liveGeometry is not null
            && GeometryEngine.Equals(expectedGeometry, liveGeometry);
    }
}
