using Esri.ArcGISRuntime.Data;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Converts between a live ArcGIS feature and the stored copy the history keeps. This and
/// FeatureChangeApplier are the only types in the history that touch ArcGIS objects, which is
/// what keeps the journal, the entries and the identity rules testable on their own.
/// </summary>
public interface IFeatureSnapshotFactory
{
    /// <summary>Copies a feature into a snapshot, loading it first if it is not loaded.</summary>
    Task<FeatureSnapshot> CreateAsync(string layerName, Feature feature);

    /// <summary>
    /// The attributes from a snapshot that may be written back to a table. The object id and
    /// the global id are left out, because the service assigns both and refuses writes to
    /// them, as does anything shape derived.
    /// </summary>
    IReadOnlyDictionary<string, object?> WritableAttributes(FeatureSnapshot snapshot);
}
