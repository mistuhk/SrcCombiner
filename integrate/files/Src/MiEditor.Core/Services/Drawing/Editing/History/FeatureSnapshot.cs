namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// The state of one row at one moment. Geometry is held as Esri JSON so the snapshot stays
/// free of runtime types and cannot be mutated by later edits to the live feature.
/// </summary>
public sealed record FeatureSnapshot(
    FeatureIdentity Identity,
    string? GeometryJson,
    string? StampField,
    DateTime? StampValue,
    IReadOnlyDictionary<string, object?> Attributes)
{
    /// <summary>
    /// Approximate memory cost, used by the journal to keep the whole history inside its
    /// budget. Strings are measured directly rather than through ToString, which would
    /// allocate a string per attribute purely to read its length. Non string values are
    /// counted as a small fixed size, which is close enough to rank an entry against a
    /// budget measured in tens of megabytes.
    /// </summary>
    public long ApproximateSizeInBytes()
    {
        long total = (GeometryJson?.Length ?? 0) * 2L;

        foreach (var pair in Attributes)
        {
            total += pair.Key.Length * 2L;
            total += pair.Value switch
            {
                null => 0,
                string s => s.Length * 2L,
                _ => 8L
            };
        }

        return total;
    }
}
