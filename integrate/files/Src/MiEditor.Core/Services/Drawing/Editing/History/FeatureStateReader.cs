using Esri.ArcGISRuntime.Data;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IFeatureStateReader) })]
public sealed class FeatureStateReader(
    IFeatureLayerService featureLayerService,
    IFeatureSnapshotFactory snapshotFactory,
    ILoggerService loggerService) : IFeatureStateReader
{
    public async Task<FeatureSnapshot?> ReadAsync(FeatureIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var featureLayer = await featureLayerService.GetFeatureLayerAsync(identity.LayerName);

        if (featureLayer is null)
        {
            loggerService.Warn($"Edit history: layer '{identity.LayerName}' was not found, so the row could not be read.");
            return null;
        }

        var feature = await featureLayerService.GetArcGISFeatureAsync(
            featureLayer,
            new QueryParameters { WhereClause = WhereClauseFor(identity) });

        return feature is null
            ? null
            : await snapshotFactory.CreateAsync(identity.LayerName, feature);
    }

    public async Task<IReadOnlyList<FeatureSnapshot>> ReadManyAsync(
        string layerName,
        IReadOnlyCollection<FeatureIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        if (identities.Count == 0)
        {
            return [];
        }

        // Checked before the layer is resolved. A malformed batch is a caller mistake, and
        // validating after the lookup would report it as whichever failure the lookup hit first.
        RequireOneKeyField(identities);

        var featureLayer = await featureLayerService.GetFeatureLayerAsync(layerName)
            ?? throw new InvalidOperationException(
                $"Layer '{layerName}' was not found, so the rows the edit history recorded against it cannot be read.");

        var features = await featureLayerService.GetQueryFeaturesAsync(
            featureLayer,
            new QueryParameters { WhereClause = WhereClauseFor(identities) });

        if (features is null || features.Count == 0)
        {
            return [];
        }

        var snapshots = new List<FeatureSnapshot>(features.Count);

        foreach (var feature in features)
        {
            snapshots.Add(await snapshotFactory.CreateAsync(layerName, feature));
        }

        return snapshots;
    }

    /// <summary>
    /// A business key identifies the row across a delete and re-insert, so it is preferred.
    /// Without one the object id is all there is, and it only holds until the row is
    /// re-created, which is why the journal rewrites it when that happens.
    /// </summary>
    private static string WhereClauseFor(FeatureIdentity identity) =>
        identity.HasBusinessKey
            ? $"{identity.BusinessKeyField} = {Literal(identity.BusinessKeyValue!)}"
            : $"OBJECTID = {identity.ObjectId}";

    /// <summary>
    /// A batch is queried with a single IN clause, which can only name one field, so every identity
    /// in it has to agree on which field that is.
    /// <para>
    /// It is tempting to assume this follows from the layer, but it does not:
    /// FeatureSnapshotFactory returns a keyless identity whenever the key attribute is null, so one
    /// keyed layer can yield both kinds. Silently dropping the odd ones out of the clause would
    /// leave them unqueried, and the caller would read that absence as somebody else having
    /// deleted them.
    /// </para>
    /// </summary>
    private static void RequireOneKeyField(IReadOnlyCollection<FeatureIdentity> identities)
    {
        var first = identities.First();

        if (identities.Any(i => i.HasBusinessKey != first.HasBusinessKey))
        {
            throw new ArgumentException(
                "A batch must be either all keyed or all keyless, because the two are queried on " +
                "different fields. Group by HasBusinessKey before calling.",
                nameof(identities));
        }

        if (first.HasBusinessKey
            && identities.Any(i => !string.Equals(i.BusinessKeyField, first.BusinessKeyField, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"A batch must share one business key field, and this one names both " +
                $"'{first.BusinessKeyField}' and others. Group by BusinessKeyField before calling.",
                nameof(identities));
        }
    }

    /// <summary>
    /// One clause covering every identity asked for. Follows the IN clause pattern already used in
    /// FeatureLayerService. Assumes RequireOneKeyField has already passed, so reading the field off
    /// the first identity is safe.
    /// </summary>
    private static string WhereClauseFor(IReadOnlyCollection<FeatureIdentity> identities)
    {
        var first = identities.First();

        if (!first.HasBusinessKey)
        {
            return $"OBJECTID IN ({string.Join(", ", identities.Select(i => i.ObjectId))})";
        }

        var keys = identities.Select(i => Literal(i.BusinessKeyValue!));

        return $"{first.BusinessKeyField} IN ({string.Join(", ", keys)})";
    }

    private static string Literal(string value) =>
        long.TryParse(value, out var number)
            ? number.ToString()
            : $"'{value.Replace("'", "''")}'";
}
