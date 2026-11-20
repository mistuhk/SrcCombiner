
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.Drawing.Editing.VertexManagement.Manager;
using WG.MiEditor.Core.Services.Drawing.Editing.VertexManagement.Operation.Persistence;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Models.MIEditorLayerConfig;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Models.Toolbar;
namespace WG.MiEditor.Core.Services.Drawing.Editing.Vertex_Management.Operation
{
    public abstract class VertexManagementOperationBase : IVertexManagementOperation
    {
        protected readonly IMessenger Messenger;
        protected readonly IFeatureLayerService FeatureLayerService;
        private readonly IVertexEditingPersistenceStrategyFactory persistenceStrategyFactory;
        private readonly IFeatureValidationService validationService;
        private readonly IFeatureSelectionState featureSelectionState;
        protected MapView? MapView { get; private set; }
        protected MapViewInteractionOptions? DefaultMapViewInteractionOptions { get; private set; }
        protected PolygonBuilder? PolygonBuilder { get; private set; }
        protected const short MAX_DRAG_TOLERANCE = 5;
        protected readonly Symbol PointGraphicSymbol;
        protected readonly Symbol LineGraphicSymbol;
        protected abstract EditingTool Tool { get; }
        protected readonly Symbol SelectedVertexSymbol;
        protected readonly Symbol MidVertexSymbol;

        protected VertexManagementOperationBase(
            IMessenger messenger,
            IFeatureLayerService featureLayerService,
            IVertexEditingPersistenceStrategyFactory persistenceStrategyFactory,
            IFeatureValidationService validationService,
            IOptions<DrawingSymbolsSettings> symbolSettings,
            IDrawingSymbolColourParser drawingSymbolColourParser, IFeatureSelectionState featureSelectionState)
        {
            Messenger = messenger;
            FeatureLayerService = featureLayerService;
            this.persistenceStrategyFactory = persistenceStrategyFactory;
            this.validationService = validationService;
            this.featureSelectionState= featureSelectionState;
            var settings = symbolSettings.Value;
            SelectedVertexSymbol = drawingSymbolColourParser.MarkerSymbol(settings.SelectedVertexSymbol, "SelectedVertexSymbol");
            MidVertexSymbol = drawingSymbolColourParser.MarkerSymbol(settings.MidVertexSymbol, "MidVertexSymbol");
            PointGraphicSymbol = drawingSymbolColourParser.MarkerSymbol(settings.VertexPoint, "VertexPoint");
            LineGraphicSymbol = drawingSymbolColourParser.LineSymbol(settings.Line, "Line");
        }

        public virtual void Initialise(MapView mapView)
        {
            MapView = mapView;
            if (MapView != null)
            {
                DefaultMapViewInteractionOptions = MapView.InteractionOptions;
            }
        }
        protected GeometryEditor GeometryEditor
        {
            get
            {
                if (MapView is null)
                {
                    throw new InvalidOperationException("MapView has not been initialised.");
                }

                return MapView.GeometryEditor
                    ?? throw new InvalidOperationException("GeometryEditor is not available on the MapView.");
            }
        }
        public virtual void Start()
        {
            MapView!.GeoViewTapped += MapView_GeoViewTapped;
            GeometryEditor.Tool = new VertexTool
            {
                Style = new GeometryEditorStyle
                {
                    VertexSymbol = PointGraphicSymbol,
                    SelectedVertexSymbol = PointGraphicSymbol,
                    VertexTextSymbol = null,
                    MidVertexSymbol = null,

                },
                Configuration = new InteractionConfiguration
                {
                    AllowVertexSelection = true,
                    AllowMovingSelectedElement = true,
                    RequireSelectionBeforeMove = true,
                    AllowMidVertexSelection = true,
                    AllowGeometrySelection=false                    

                },

            };
        }
        public virtual void Stop(bool isGraphicsOverlayRequired)
        {
            MapView!.GeoViewTapped -= MapView_GeoViewTapped;

            if (GeometryEditor?.IsStarted == true) GeometryEditor.Stop();

            DefaultMapViewInteractionOptions = null;

            var geometryEditor = MapView.GeometryEditor;
            if (geometryEditor is null)
            {
                return;
            }

            geometryEditor.Tool = new VertexTool
            {
                Style = new GeometryEditorStyle
                {
                    VertexSymbol = SelectedVertexSymbol,
                    SelectedVertexSymbol = SelectedVertexSymbol,
                    MidVertexSymbol = MidVertexSymbol,
                    VertexTextSymbol = null,
                }
            };

            if (!isGraphicsOverlayRequired) ClearGraphicsOverlay();
        }
        public void ClearGraphicsOverlay()
        {
            var graphicsOverlays = MapView?.GraphicsOverlays;

            if (graphicsOverlays is null)
            {
                return;
            }

            var vertexOverlay = graphicsOverlays.FirstOrDefault(o => o.Id == "VertexOverlay");
            if (vertexOverlay != null)
            {
                graphicsOverlays.Remove(vertexOverlay);
            }
            if (this.featureSelectionState != null) this.featureSelectionState.SelectedFeature = null;
        }

        protected GraphicsOverlay NodeOverlay => GetOrCreateOverlay();

        private GraphicsOverlay GetOrCreateOverlay()
        {

            var graphicsOverlays = MapView?.GraphicsOverlays
                   ?? throw new InvalidOperationException("MapView or GraphicsOverlays is not available.");

            var existing = graphicsOverlays.FirstOrDefault(o => o.Id == "VertexOverlay");

            if (existing != null) return existing;

            var overlay = new GraphicsOverlay { Id = "VertexOverlay" };

            graphicsOverlays.Add(overlay);

            return overlay;
        }
  
        protected async void MapView_GeoViewTapped(object? sender, GeoViewInputEventArgs e)
        {
           
            EnableDefaultInteractionOptions();

            await HandleTapAsync(e);
        }
        protected abstract Task HandleTapAsync(GeoViewInputEventArgs tappedPoint);

        protected void StartEditingGeometry(Geometry geometry)
        {
            if (geometry is Polygon polygon && !GeometryEditor.IsStarted)
            {
                GeometryEditor.Start(polygon);
            }

        }
        protected void StopEditingGeometry()
        {
            if (GeometryEditor?.IsStarted == true) GeometryEditor.Stop();
     
        }

        protected static async Task<ArcGISFeature?> GetFeatureFromMapPointAsync(MapView mapView, FeatureLayer featureLayer, MapPoint mapPoint)
        {
            var screenPoint = mapView.LocationToScreen(mapPoint);

            var identifyResult = await mapView.IdentifyLayerAsync(featureLayer, screenPoint, tolerance: 10, returnPopupsOnly: false);

            if (identifyResult.GeoElements.Count > 0 && identifyResult.GeoElements[0] is ArcGISFeature feature)
            {
                return feature;
            }

            return null;
        }

        public void ViewPolygonVertex(Geometry geometry)
        {
            NodeOverlay.Graphics.Clear();

            NodeOverlay.Graphics.Add(new Graphic(geometry, LineGraphicSymbol));

            if (geometry is Polygon polygon)
            {
                PolygonBuilder = new PolygonBuilder(polygon);

                foreach (var part in polygon.Parts)
                {
                    foreach (var point in part.Points)
                    {
                        NodeOverlay.Graphics.Add(new Graphic(point, PointGraphicSymbol));
                    }
                }
            }
        }

        protected void DisableDefaultInteractionOptions()
        {
            if (MapView != null)
            {
                MapView.InteractionOptions = new MapViewInteractionOptions { IsPanEnabled = false, IsZoomEnabled = false };
            }
        }
        protected void EnableDefaultInteractionOptions()
        {
            if (MapView != null)
            {
                MapView.InteractionOptions = DefaultMapViewInteractionOptions;
            }
        }

        protected virtual async Task<bool> PersistenceFeature(ArcGISFeature arcGISFeature)
        {
            var targetLayer = await this.Messenger.Send<SelectedEditableLayerRequestMessage>();

            var persistenceStrategy = persistenceStrategyFactory.CreateStrategy(targetLayer);

            var map = MapView!.Map;

            if (map is null)
            {
                return false;
            }

            var featureLayer = await this.FeatureLayerService.GetFeatureLayerAsync(map.OperationalLayers, targetLayer);

            if (featureLayer is null)
            {
                return false;
            }

            return await persistenceStrategy.PersistVertexEditingFeaturesAsync(
                 arcGISFeature,
                 featureLayer,
                 map.OperationalLayers,
                 targetLayer);
        }

        public virtual async Task<List<FeatureValidationError>> ValidateAsync(Feature feature)
        {

            var caseRequest = await this.Messenger.Send<SelectedCaseRequestMessage>();

            var targetLayer = await this.Messenger.Send<SelectedEditableLayerRequestMessage>();

            return await this.validationService.ValidateFeatureAsync(
                MapView!.Map!,
                targetLayer,
                feature,
                caseRequest,
                Enums.FeatureAction.Modify);
        }
    }
}
