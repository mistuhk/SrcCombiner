
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing;

public interface IFeatureHighlightService
{
    Task HighlightFeatureAsync(Feature feature);
    Task ClearHighlightAsync();
}

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IFeatureHighlightService) })]
public class FeatureHighlightService(IMapViewService mapViewService,
    IDrawingSymbolColourParser symbolColourParser,
    IOptions<DrawingSymbolsSettings> symbolSettings) : IFeatureHighlightService
{
    private readonly IMapViewService mapViewService = mapViewService;
    private readonly IDrawingSymbolColourParser symbolColourParser = symbolColourParser;
    private readonly DrawingSymbolsSettings symbolSettings = symbolSettings.Value;

    private GraphicsOverlay? highlightOverlay;
    private Graphic? currentHighlightGraphic;
    private bool isInitialised = false;

    private async Task EnsureInitialisedAsync()
    {
        if (isInitialised) return;

        var mapView = await mapViewService.GetMapViewAsync() ?? throw new InvalidOperationException("MapView is null.");
        if (mapView.GraphicsOverlays == null)
        {
            throw new InvalidOperationException("GraphicsOverlays collection is null.");
        }

        highlightOverlay = new GraphicsOverlay
        {
            Id = "HighlightOverlay",
            RenderingMode = GraphicsRenderingMode.Dynamic
        };

        mapView.GraphicsOverlays.Add(highlightOverlay);

        isInitialised = true;
    }

    public async Task HighlightFeatureAsync(Feature feature)
    {
        await EnsureInitialisedAsync();

        if (feature?.Geometry == null)
        {
            await ClearHighlightAsync();
            return;
        }

        var newGeometry = feature.Geometry;
        var newSymbol = CreateHighlightSymbol(newGeometry);

        if (currentHighlightGraphic == null)
        {
            currentHighlightGraphic = new Graphic(newGeometry, newSymbol);
            highlightOverlay?.Graphics.Add(currentHighlightGraphic);
        }
        else
        {
            currentHighlightGraphic.Geometry = newGeometry;
            currentHighlightGraphic.Symbol = newSymbol;
        }
    }

    public async Task ClearHighlightAsync()
    {
        await EnsureInitialisedAsync();
        if (currentHighlightGraphic != null)
        {
            highlightOverlay?.Graphics.Remove(currentHighlightGraphic);
            currentHighlightGraphic = null;
        }
    }

    private Symbol CreateHighlightSymbol(Geometry geometry)
    {
        switch (geometry.GeometryType)
        {
            case GeometryType.Point:
                var pointSettings = symbolSettings.Point;
                return symbolColourParser.MarkerSymbol(pointSettings, "Point");

            case GeometryType.Polyline:
                var lineSettings = symbolSettings.Line;
                return symbolColourParser.LineSymbol(lineSettings, "Line");

            case GeometryType.Polygon:
                var polygonSettings = symbolSettings.Polygon;
                return symbolColourParser.FillSymbol(polygonSettings, "Polygon");

            default:
                return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.Red, 12);
        }
    }
}
