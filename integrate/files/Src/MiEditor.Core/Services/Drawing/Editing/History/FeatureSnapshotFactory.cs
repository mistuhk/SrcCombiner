using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IFeatureSnapshotFactory) })]
public sealed class FeatureSnapshotFactory(IFeatureIdentityPolicy identityPolicy) : IFeatureSnapshotFactory
{
    private const string ObjectIdAttribute = "OBJECTID";
    private const string GlobalIdAttribute = "GLOBALID";
    private const string ShapeAttribute = "SHAPE";
    private const string ShapePrefix = "SHAPE_";

    public async Task<FeatureSnapshot> CreateAsync(string layerName, Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (feature is ArcGISFeature arcGisFeature && arcGisFeature.LoadStatus != LoadStatus.Loaded)
        {
            await arcGisFeature.LoadAsync();
        }

        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in feature.Attributes)
        {
            attributes[pair.Key] = pair.Value;
        }

        var stampField = identityPolicy.StampField(layerName);

        return new FeatureSnapshot(
            BuildIdentity(layerName, attributes),
            feature.Geometry?.ToJson(),
            stampField,
            ReadStamp(attributes, stampField),
            attributes);
    }

    public IReadOnlyDictionary<string, object?> WritableAttributes(FeatureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var writable = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in snapshot.Attributes)
        {
            if (IsServiceOwned(pair.Key))
            {
                continue;
            }

            writable[pair.Key] = pair.Value;
        }

        return writable;
    }

    /// <summary>
    /// Mirrors the exclusions the split persistence already applies when it copies attributes
    /// onto a new feature. The service owns these and rejects writes to them.
    /// </summary>
    private static bool IsServiceOwned(string fieldName) =>
        fieldName.Equals(ObjectIdAttribute, StringComparison.OrdinalIgnoreCase)
        || fieldName.Equals(GlobalIdAttribute, StringComparison.OrdinalIgnoreCase)
        || fieldName.Equals(ShapeAttribute, StringComparison.OrdinalIgnoreCase)
        || fieldName.StartsWith(ShapePrefix, StringComparison.OrdinalIgnoreCase);

    private FeatureIdentity BuildIdentity(string layerName, IReadOnlyDictionary<string, object?> attributes)
    {
        var objectId = ReadLong(attributes, ObjectIdAttribute);
        var businessKeyField = identityPolicy.BusinessKeyField(layerName);

        if (businessKeyField is null
            || !attributes.TryGetValue(businessKeyField, out var businessKeyValue)
            || businessKeyValue is null)
        {
            return new FeatureIdentity(layerName, objectId);
        }

        return new FeatureIdentity(layerName, objectId, businessKeyField, businessKeyValue.ToString());
    }

    private static long ReadLong(IReadOnlyDictionary<string, object?> attributes, string field) =>
        attributes.TryGetValue(field, out var value) && value is not null
            ? Convert.ToInt64(value)
            : 0;

    private static DateTime? ReadStamp(IReadOnlyDictionary<string, object?> attributes, string field) =>
        attributes.TryGetValue(field, out var value) && value is not null
            ? Convert.ToDateTime(value)
            : null;
}
