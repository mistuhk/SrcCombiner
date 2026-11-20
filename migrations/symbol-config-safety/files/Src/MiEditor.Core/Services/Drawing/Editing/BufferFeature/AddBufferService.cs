
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Enums;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Models.MIEditorLayerConfig;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.BufferFeature
{

    public interface IAddBufferService
    {
        Task<ArcGISFeature> CreateFeature(Geometry geometry, string targetLayerName);
        Task<List<FeatureValidationError>> RunValidationAsync(ArcGISFeature newFeature, string targetLayerName);
        Symbol GetFeatureSymbol(GeometryType geometryType, bool isSketch);
        GraphicsOverlay GetBufferOverlay(MapView mapView);
    }

    [RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IAddBufferService) })]
    public  class AddBufferService(
        IMapViewService mapViewService,
        IFeatureLayerService featureLayerService,
        IFeatureValidationService featureValidationService,
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser symbolColourParser,
        IMessenger messenger) : IAddBufferService 
    {
        private readonly DrawingSymbolsSettings drawingSymbolsSettings = symbolSettings.Value;
        protected readonly IMapViewService MapViewService = mapViewService;
        protected readonly IFeatureLayerService FeatureLayerService = featureLayerService;
        protected readonly IFeatureValidationService FeatureValidationService = featureValidationService;
        protected readonly IMessenger messenger = messenger;
        private readonly string BufferOverlayId = "BufferGraphicsOverlay";

        public async Task<ArcGISFeature> CreateFeature(Geometry geometry, string targetLayerName)
        {
            var mapView = await MapViewService.GetMapViewAsync();
            if (mapView?.Map == null)
            {
                throw new InvalidOperationException("MapView or its Map is null.");
            }

            var featureLayer = await FeatureLayerService.GetFeatureLayerAsync(mapView.Map.OperationalLayers, targetLayerName);

            if (featureLayer?.FeatureTable == null)
            {
                throw new InvalidOperationException($"Feature table for layer '{targetLayerName}' not found.");
            }

            var newFeature = (ArcGISFeature)featureLayer.FeatureTable.CreateFeature();
            newFeature.Geometry = GeometryEngine.Simplify(geometry);
            return newFeature;
        }

        public async Task<List<FeatureValidationError>> RunValidationAsync(
            ArcGISFeature newFeature,
            string targetLayerName)
        {
            ArgumentNullException.ThrowIfNull(newFeature);

            if (string.IsNullOrWhiteSpace(targetLayerName))
            {
                throw new ArgumentException("Target layer name is required.", nameof(targetLayerName));
            }

            var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();
            if (caseRequest is null)
            {
                return
                [
                    new FeatureValidationError(string.Empty,
                        "Unable to retrieve selected case details for validation.")
                ];
            }

            var mapView = await MapViewService.GetMapViewAsync() 
                ?? throw new InvalidOperationException("MapView is null.");

            if (mapView.Map == null)
            {
                throw new InvalidOperationException("MapView.Map is null.");
            }

            return await FeatureValidationService.ValidateFeatureAsync(
                mapView.Map,
                targetLayerName,
                newFeature,
                caseRequest,
                FeatureAction.Add);
        }

        public Symbol GetFeatureSymbol(GeometryType geometryType, bool isSketch)
        {
            switch (geometryType)
            {
                case GeometryType.Point:
                    var pointSettings = isSketch ? drawingSymbolsSettings.InFieldSketchPoint : drawingSymbolsSettings.Point;
                    return symbolColourParser.MarkerSymbol(pointSettings, isSketch ? "InFieldSketchPoint" : "Point");

                case GeometryType.Polyline:
                    var lineSettings = isSketch ? drawingSymbolsSettings.InFieldSketchLine : drawingSymbolsSettings.Line;
                    return symbolColourParser.LineSymbol(lineSettings, isSketch ? "InFieldSketchLine" : "Line");

                case GeometryType.Polygon:
                    var polygonSettings = isSketch ? drawingSymbolsSettings.InFieldSketchPolygon : drawingSymbolsSettings.Polygon;
                    return symbolColourParser.FillSymbol(polygonSettings, isSketch ? "InFieldSketchPolygon" : "Polygon");
                default:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 10);
            }
        }

        public GraphicsOverlay GetBufferOverlay(MapView mapView)
        {
            ArgumentNullException.ThrowIfNull(mapView);

            if (mapView.GraphicsOverlays == null)
            {
                throw new InvalidOperationException("GraphicsOverlays collection is null.");
            }

            var overlay = mapView.GraphicsOverlays.FirstOrDefault(go => go.Id == BufferOverlayId);

            if (overlay != null) return overlay;

            overlay = new GraphicsOverlay { Id = BufferOverlayId };
            mapView.GraphicsOverlays.Add(overlay);

            return overlay;
        }
    }
}
