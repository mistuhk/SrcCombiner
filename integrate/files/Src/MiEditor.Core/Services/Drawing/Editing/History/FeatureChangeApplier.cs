using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Logging.Utility.Abstraction;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Writes row changes back to the feature tables.
/// <para>
/// All the local edits are made first and each touched table is then sent once, mirroring what
/// the forward operations do. Unlike them, the result of every send is inspected: a service
/// that rejects an edit reports it there rather than throwing, and an undo that silently
/// half succeeded would leave the journal describing a state the data is not in.
/// </para>
/// <para>
/// Inserting a row returns a new object id, because the service will not reuse the old one.
/// Those are reported back so the journal can rewrite what it holds.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IFeatureChangeApplier) })]
public sealed class FeatureChangeApplier(
    IFeatureLayerService featureLayerService,
    IFeatureSnapshotFactory snapshotFactory,
    INetWorkAndIdentityChecker networkChecker,
    ILoggerService loggerService) : IFeatureChangeApplier
{
    private const string ObjectIdAttribute = "OBJECTID";

    public async Task<ChangeApplyResult> ApplyAsync(IReadOnlyList<FeatureChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return ChangeApplyResult.Applied([]);
        }

        // The layer rather than the table, because finding an existing row needs the layer too
        // and resolving it twice per change is a wasted round trip.
        var touched = new Dictionary<string, FeatureLayer>(StringComparer.OrdinalIgnoreCase);
        var inserted = new List<PendingInsert>();

        try
        {
            foreach (var change in changes)
            {
                var layerName = change.Identity.LayerName;
                var layer = await ResolveLayerAsync(layerName, touched);

                if (layer?.FeatureTable is null)
                {
                    return ChangeApplyResult.Failed($"Layer '{layerName}' was not found.");
                }

                await ApplyOneAsync(change, layer, inserted);
            }

            foreach (var pair in touched)
            {
                var error = await SendAsync(pair.Key, pair.Value.FeatureTable!);

                if (error is not null)
                {
                    return ChangeApplyResult.Failed(error);
                }
            }

            // Only now does an inserted row have an object id, so the remaps are resolved here
            // rather than at the point of insertion.
            var remaps = inserted
                .Select(i => new ObjectIdRemap(i.LayerName, i.OldObjectId, ReadObjectId(i.Feature)))
                .Where(r => r.NewObjectId != 0 && r.NewObjectId != r.OldObjectId)
                .ToList();

            return ChangeApplyResult.Applied(remaps);
        }
        catch (Exception ex)
        {
            loggerService.Error(ex, $"Edit history: applying changes failed. {ex.Message}");
            return ChangeApplyResult.Failed(ex.Message);
        }
    }

    private async Task<FeatureLayer?> ResolveLayerAsync(string layerName, Dictionary<string, FeatureLayer> touched)
    {
        if (touched.TryGetValue(layerName, out var known))
        {
            return known;
        }

        var featureLayer = await featureLayerService.GetFeatureLayerAsync(layerName);

        if (featureLayer?.FeatureTable is not null)
        {
            touched[layerName] = featureLayer;
        }

        return featureLayer;
    }

    private async Task ApplyOneAsync(FeatureChange change, FeatureLayer layer, List<PendingInsert> inserted)
    {
        switch (change.Kind)
        {
            case FeatureChangeKind.Insert:
                inserted.Add(await InsertAsync(change, layer));
                break;

            case FeatureChangeKind.Delete:
                await DeleteAsync(change, layer);
                break;

            case FeatureChangeKind.Update:
                await UpdateAsync(change, layer);
                break;

            default:
                throw new InvalidOperationException($"Unsupported change kind: {change.Kind}.");
        }
    }

    private async Task<PendingInsert> InsertAsync(FeatureChange change, FeatureLayer layer)
    {
        var table = layer.FeatureTable!;
        var snapshot = change.After!;
        var feature = table.CreateFeature();

        if (snapshot.GeometryJson is not null)
        {
            feature.Geometry = Geometry.FromJson(snapshot.GeometryJson);
        }

        foreach (var pair in snapshotFactory.WritableAttributes(snapshot))
        {
            if (table.Fields.Any(f => f.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                feature.SetAttributeValue(pair.Key, pair.Value);
            }
        }

        await table.AddFeatureAsync(feature);

        return new PendingInsert(layer.Name, snapshot.Identity.ObjectId, feature);
    }

    private async Task DeleteAsync(FeatureChange change, FeatureLayer layer)
    {
        var snapshot = change.Before!;
        var existing = await FindAsync(layer, snapshot.Identity);

        if (existing is null)
        {
            loggerService.Warn(
                $"Edit history: nothing to delete for {layer.Name} {Describe(snapshot.Identity)}, so the change is treated as already applied.");
            return;
        }

        await layer.FeatureTable!.DeleteFeatureAsync(existing);
    }

    private async Task UpdateAsync(FeatureChange change, FeatureLayer layer)
    {
        var snapshot = change.After!;
        var existing = await FindAsync(layer, snapshot.Identity);

        if (existing is null)
        {
            throw new InvalidOperationException(
                $"Cannot update {layer.Name} {Describe(snapshot.Identity)} because it no longer exists.");
        }

        if (snapshot.GeometryJson is not null)
        {
            existing.Geometry = Geometry.FromJson(snapshot.GeometryJson);
        }

        foreach (var pair in snapshotFactory.WritableAttributes(snapshot))
        {
            if (existing.Attributes.ContainsKey(pair.Key))
            {
                existing.SetAttributeValue(pair.Key, pair.Value);
            }
        }

        await layer.FeatureTable!.UpdateFeatureAsync(existing);
    }

    private async Task<ArcGISFeature?> FindAsync(FeatureLayer layer, FeatureIdentity identity)
    {
        var where = identity.HasBusinessKey
            ? $"{identity.BusinessKeyField} = {Literal(identity.BusinessKeyValue!)}"
            : $"{ObjectIdAttribute} = {identity.ObjectId}";

        return await featureLayerService.GetArcGISFeatureAsync(layer, new QueryParameters { WhereClause = where });
    }

    /// <summary>
    /// Sends one table and reports anything the service rejected. The forward operations mostly
    /// ignore this result; an undo cannot afford to, because a rejected edit means the history
    /// and the data have diverged.
    /// </summary>
    private async Task<string?> SendAsync(string layerName, FeatureTable table)
    {
        if (table is not ServiceFeatureTable serviceTable || !networkChecker.IsConnected)
        {
            // Offline, the edit stays in the local geodatabase and is reconciled on sync, which
            // is exactly what the forward operations do.
            return null;
        }

        var results = await serviceTable.ApplyEditsAsync();
        var rejected = results?.FirstOrDefault(r => r.CompletedWithErrors);

        if (rejected is null)
        {
            return null;
        }

        var message = rejected.Error?.Message ?? "the service rejected the edit without saying why";
        loggerService.Error(rejected.Error ?? new InvalidOperationException(message),
            $"Edit history: applying changes to '{layerName}' was rejected. {message}");

        return $"{layerName}: {message}";
    }

    /// <summary>One inserted row, held until the table is sent and its id is known.</summary>
    private sealed record PendingInsert(string LayerName, long OldObjectId, Feature Feature);

    private static long ReadObjectId(Feature feature) =>
        feature.Attributes.TryGetValue(ObjectIdAttribute, out var value) && value is not null
            ? Convert.ToInt64(value)
            : 0;

    private static string Describe(FeatureIdentity identity) =>
        identity.HasBusinessKey
            ? $"{identity.BusinessKeyField} {identity.BusinessKeyValue}"
            : $"{ObjectIdAttribute} {identity.ObjectId}";

    private static string Literal(string value) =>
        long.TryParse(value, out var number)
            ? number.ToString()
            : $"'{value.Replace("'", "''")}'";
}
