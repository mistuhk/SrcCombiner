
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;


namespace WG.MiEditor.Core.Services.Drawing.Editing.MoveFeatures.Persistence;

[RegisterService(ServiceLifetime.Scoped)]
public class CanopyPointPersistenceStrategy(
    ILpisParcelService lpisParcelService, 
    IMapViewService mapViewService,
    IUserContext userContext,
    IMaxUseAreaService maxUseAreaService, 
    ILoggerService loggerService)
    : IMoveFeaturePersistenceStrategy
{
    public async Task<bool> PersistMoveFeaturesAsync(
    Feature feature,
    FeatureLayer featureLayer,
    string targetLayerName)
    {
        ArgumentNullException.ThrowIfNull(feature);

        ArgumentNullException.ThrowIfNull(featureLayer);

        if (featureLayer.FeatureTable == null)
        {
            throw new InvalidOperationException("FeatureLayer.FeatureTable is null.");
        }

        if (string.IsNullOrWhiteSpace(targetLayerName))
        {
            throw new ArgumentException("Target layer name is required.", nameof(targetLayerName));
        }

        loggerService.Info($"Save feature: {((MapPoint)feature!.Geometry!).X}, {((MapPoint)feature.Geometry).Y}");

        await MapAttributesAsync(feature);

        await featureLayer.FeatureTable.UpdateFeatureAsync(feature);

        if (featureLayer.FeatureTable is ServiceFeatureTable serviceFeatureTable)
        {
            await ApplyEditsOrThrow(serviceFeatureTable, targetLayerName);
        }

        await maxUseAreaService.UpdateMaxUseAreaFromFeatureAsync(feature);
     
        return true;
    }

    /// <summary>
    /// Sends the edit and raises whatever the service rejected, following
    /// DeleteFeatureService.ApplyEditsOrThrow.
    /// <para>
    /// This used to log each result and return regardless, so a move the service refused still
    /// reported success. That mattered little while nothing was watching; now the edit history records
    /// the move, and a rejected one would leave an undo offering to put the point back somewhere it
    /// never left.
    /// </para>
    /// </summary>
    private async Task ApplyEditsOrThrow(ServiceFeatureTable serviceFeatureTable, string layerName)
    {
        loggerService.Info($"Attempting ApplyEdits for a move on online table '{layerName}'.");
        var results = await serviceFeatureTable.ApplyEditsAsync();

        var errors = results?.Where(r => r.CompletedWithErrors).ToList();
        if (errors != null && errors.Count != 0)
        {
            var error = errors[0].Error ?? new Exception($"ApplyEdits failed for table '{layerName}'.");
            loggerService.Error(error, $"ApplyEdits failed for a move on table '{layerName}'.");
            throw error;
        }
        loggerService.Info($"ApplyEdits successful for moving a feature in '{layerName}'.");
    }

    private async Task MapAttributesAsync(Feature updateFeature)
    {
        ArgumentNullException.ThrowIfNull(updateFeature);

        if (updateFeature.Attributes == null)
        {
            throw new InvalidOperationException("Feature attributes collection is null.");
        }

        var mapView = await mapViewService.GetMapViewAsync() ?? throw new InvalidOperationException("MapView is null.");
        if (mapView.Map == null)
        {
            throw new InvalidOperationException("MapView.Map is null.");
        }

        if (mapView.Map.OperationalLayers == null)
        {
            throw new InvalidOperationException("OperationalLayers collection is null.");
        }

        if (updateFeature.Geometry == null)
        {
            throw new InvalidOperationException("Feature geometry is null.");
        }

        updateFeature.SetAttributeValue("MODIFIEDBY", userContext.UserId.ToString());
        updateFeature.SetAttributeValue("MODIFIEDDT", DateTime.Now);

        var attributes = await lpisParcelService.GetLpisParcelAttributes(updateFeature.Geometry);

        updateFeature.SetAttributeValue("LPISPOLYID", attributes.LpisPolyId);
    }
}
