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
    /// Approximate memory cost, used by the journal size guard. Attribute values are counted
    /// by their string length, which is close enough to rank an entry against a byte budget.
    /// </summary>
    public long ApproximateSizeInBytes()
    {
        long total = (GeometryJson?.Length ?? 0) * 2L;

        foreach (var pair in Attributes)
        {
            total += pair.Key.Length * 2L;
            total += (pair.Value?.ToString()?.Length ?? 0) * 2L;
        }

        return total;
    }
}
