
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Editing;

namespace WG.MiEditor.Core.Services.Drawing.Editing.ImportFeature;

/// <summary>
/// Manages the shared import graphics overlay and temporary feature graphic creation.
/// Also handles cleanup of the overlay in response to <c>ClearEditingGraphicMessage</c>.
/// Extracted from ImportStrategyBase to support composition over inheritance.
/// </summary>
public interface IImportGraphicsService
{
    /// <summary>
    /// Returns the existing import <see cref="GraphicsOverlay"/> from the active map view,
    /// creating and registering it if it does not yet exist.
    /// </summary>
    Task<GraphicsOverlay> GetOrCreateImportOverlayAsync();

    /// <summary>
    /// Creates a temporary <see cref="Graphic"/> for the given geometry using the appropriate
    /// drawing symbol based on geometry type and whether it is a sketch feature.
    /// </summary>
    /// <param name="geometry">The geometry to represent visually.</param>
    /// <param name="isSketch">
    /// When <c>true</c>, uses the in-field sketch symbol settings;
    /// otherwise uses the standard feature symbol settings.
    /// </param>
    Graphic CreateTempFeatureGraphic(Geometry geometry, bool isSketch);
}


/// <summary>
/// Registered as a singleton so that the shared import overlay and its
/// <see cref="ClearEditingGraphicMessage"/> subscription persist for the lifetime
/// of the application, independent of any scoped import strategy.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IImportGraphicsService) })]
public sealed class ImportGraphicsService : IImportGraphicsService, IRecipient<ClearEditingGraphicMessage>
{
    private const string ImportOverlayId = "ImportGraphicsOverlay";

    private readonly IMapViewService mapViewService;
    private readonly DrawingSymbolsSettings drawingSymbolsSettings;
    private readonly IDrawingSymbolColourParser symbolColourParser;

    public ImportGraphicsService(
        IMessenger messenger,
        IMapViewService mapViewService,
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser symbolColourParser)
    {
        this.mapViewService = mapViewService;
        this.drawingSymbolsSettings = symbolSettings.Value;
        this.symbolColourParser = symbolColourParser;

        // Self-register for ClearEditingGraphicMessage without requiring ObservableRecipient.
        messenger.RegisterAll(this);
    }

    public async Task<GraphicsOverlay> GetOrCreateImportOverlayAsync()
    {
        var mapView = await mapViewService.GetMapViewAsync() ?? throw new InvalidOperationException("MapView is null.");
        if (mapView.GraphicsOverlays == null)
        {
            throw new InvalidOperationException("GraphicsOverlays collection is null.");
        }

        var overlay = mapView.GraphicsOverlays
            .FirstOrDefault(go => go.Id == ImportOverlayId);

        if (overlay == null)
        {
            overlay = new GraphicsOverlay { Id = ImportOverlayId };
            mapView.GraphicsOverlays.Add(overlay);
        }

        return overlay;
    }

    public Graphic CreateTempFeatureGraphic(Geometry geometry, bool isSketch)
    {
        var symbol = GetFeatureSymbol(geometry.GeometryType, isSketch);
        return new Graphic(geometry, symbol);
    }

    public async void Receive(ClearEditingGraphicMessage message)
    {
        var mapView = await mapViewService.GetMapViewAsync() ?? throw new InvalidOperationException("MapView is null.");
        if (mapView.GraphicsOverlays == null)
        {
            throw new InvalidOperationException("GraphicsOverlays collection is null.");
        }

        var overlay = mapView.GraphicsOverlays
            .FirstOrDefault(go => go.Id == ImportOverlayId);

        if (overlay != null)
        {
            mapView.GraphicsOverlays.Remove(overlay);
        }
    }

    private Symbol GetFeatureSymbol(GeometryType geometryType, bool isSketch)
    {
        switch (geometryType)
        {
            case GeometryType.Point:
                var pointSettings = isSketch
                    ? drawingSymbolsSettings.InFieldSketchPoint
                    : drawingSymbolsSettings.Point;

                return symbolColourParser.MarkerSymbol(pointSettings, isSketch ? "InFieldSketchPoint" : "Point");

            case GeometryType.Polyline:
                var lineSettings = isSketch
                    ? drawingSymbolsSettings.InFieldSketchLine
                    : drawingSymbolsSettings.Line;

                return symbolColourParser.LineSymbol(lineSettings, isSketch ? "InFieldSketchLine" : "Line");

            case GeometryType.Polygon:
                var polygonSettings = isSketch
                    ? drawingSymbolsSettings.InFieldSketchPolygon
                    : drawingSymbolsSettings.Polygon;

                return symbolColourParser.FillSymbol(polygonSettings, isSketch ? "InFieldSketchPolygon" : "Polygon");

            default:
                return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 10);
        }
    }
}
