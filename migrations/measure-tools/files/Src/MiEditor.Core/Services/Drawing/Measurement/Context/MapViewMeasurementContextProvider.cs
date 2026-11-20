using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.UI;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Context;

/// <summary>
/// Builds the measurement context from the registered map. The overlay is created once but its
/// attachment is checked on every call: other parts of the app clear
/// <see cref="GeoView.GraphicsOverlays"/> (for example zoom to feature), and without this check
/// measurements would carry on drawing onto an overlay that is no longer on the map.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMeasurementContextProvider) })]
public sealed class MapViewMeasurementContextProvider : IMeasurementContextProvider
{
    public const string OverlayId = "measurementOverlay";

    public static readonly TimeSpan DefaultMapReadyTimeout = TimeSpan.FromSeconds(10);

    private readonly IMapViewService mapViewService;
    private readonly TimeSpan mapReadyTimeout;
    private GraphicsOverlay? overlay;

    /// <param name="mapViewService">Source of the map view.</param>
    /// <param name="mapReadyTimeout">How long to wait for the map to be registered. Not registered in DI, so the default applies in the app.</param>
    public MapViewMeasurementContextProvider(IMapViewService mapViewService, TimeSpan? mapReadyTimeout = null)
    {
        this.mapViewService = mapViewService;
        this.mapReadyTimeout = mapReadyTimeout ?? DefaultMapReadyTimeout;
    }

    public async Task<MeasurementContext> GetAsync(CancellationToken cancellationToken = default)
    {
        MapView? mapView;

        try
        {
            // The map view service never completes if the map is never registered, so bound the
            // wait rather than leaving the tool looking active while nothing happens.
            mapView = await mapViewService.GetMapViewAsync().WaitAsync(mapReadyTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new MeasurementUnavailableException("The map is not ready yet. Please try again in a moment.", ex);
        }

        if (mapView is null)
        {
            throw new MeasurementUnavailableException("The map is not ready yet. Please try again in a moment.");
        }

        if (mapView.GeometryEditor is null)
        {
            throw new MeasurementUnavailableException("The map cannot be measured on right now. Please try again in a moment.");
        }

        var overlays = mapView.GraphicsOverlays
            ?? throw new MeasurementUnavailableException("The map cannot display measurements right now. Please try again in a moment.");

        overlay ??= new GraphicsOverlay { Id = OverlayId, RenderingMode = GraphicsRenderingMode.Dynamic };
        EnsureAttached(overlays, overlay);

        return new MeasurementContext(mapView, overlay);
    }

    /// <summary>Adds <paramref name="overlay"/> to <paramref name="overlays"/> unless it is already there.</summary>
    public static void EnsureAttached(GraphicsOverlayCollection overlays, GraphicsOverlay overlay)
    {
        if (!overlays.Contains(overlay))
        {
            overlays.Add(overlay);
        }
    }
}
