namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Reads a row's current state so the conflict check has something to compare against, and so
/// a freshly written row can be recorded from the table rather than from the object held in
/// memory. Both sides of any later comparison then come from the same code.
/// </summary>
public interface IFeatureStateReader
{
    /// <summary>
    /// The row as it stands now, or null when it no longer exists. Resolved by business key
    /// where the layer has one, otherwise by object id.
    /// </summary>
    Task<FeatureSnapshot?> ReadAsync(FeatureIdentity identity);

    /// <summary>
    /// The rows that still exist, out of those asked for, in one query per layer. Rows that have
    /// been deleted are simply absent from the result.
    /// <para>
    /// Every identity passed must be on the same layer and agree on which field identifies it,
    /// because they are fetched with one IN clause and it can only name one field. Group by layer
    /// and by HasBusinessKey first. Throws when a batch disagrees, rather than querying part of it.
    /// </para>
    /// <para>
    /// Throws when the layer cannot be resolved. An absent row and an absent layer look the same in
    /// an empty result, and the caller would report the second as somebody else having deleted the
    /// data. ReadAsync returns null for either, because its caller wants to skip rather than fail.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<FeatureSnapshot>> ReadManyAsync(
        string layerName,
        IReadOnlyCollection<FeatureIdentity> identities);
}
