
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Enums;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.FeatureGuide;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Models.Notifications;

namespace WG.MiEditor.Core.Services.Drawing.Editing.MoveFeatures.Operations;


[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IMoveFeatureOperation) })]
public class MovePointOperation : IMoveFeatureOperation
{

    private bool featureSelected = false;
    private readonly IMessenger messenger;
    private readonly IMoveFeatureService moveFeatureService;
    private readonly INotificationManager notificationManager;
    private readonly IFeatureLayerService featureLayerService;
    private readonly ILoggerService loggerService;
    private readonly Symbol SelectedVertexSymbol;
    private readonly Symbol RollbackVertexSymbol;
    private string? layerName;
    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;
    private MapPoint? originalPoint;
    private ArcGISFeature? activeFeature;
    private Point? selectedScreenPoint;
    const double ClickTolerancePixels = 5;
    private const string MovePoint = "Move Point";
    public MovePointOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        IMessenger messenger,
        INotificationManager notificationManager,
        IFeatureLayerService featureLayerService,
        IMoveFeatureService moveFeatureService,
        ILoggerService loggerService)
    {
        this.messenger = messenger;
        this.moveFeatureService = moveFeatureService;
        this.notificationManager = notificationManager;
        this.featureLayerService = featureLayerService;
        this.loggerService= loggerService;     

        messenger.Register<MovePointOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));

        SelectedVertexSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.PointSelectedSymbol, "PointSelectedSymbol");
        RollbackVertexSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.SelectedVertexSymbol, "SelectedVertexSymbol");
    
    }

    public void Receive(ClearEditingGraphicMessage message)
    {
        if (graphicsOverlay == null) return;

        moveFeatureService.RemoveGraphic(graphicsOverlay, ref currentlyEditingGraphic);
    }

    public void Initialise(MapView mapView, GraphicsOverlay graphicsOverlay)
    {
        this.mapView = mapView;
        if (this.mapView == null) return;

        var geometryEditor = this.mapView.GeometryEditor;
        if (geometryEditor != null)
        {
            geometryEditor.Tool = new VertexTool
            {
                Style = new GeometryEditorStyle
                {
                    VertexSymbol = SelectedVertexSymbol,
                    SelectedVertexSymbol = SelectedVertexSymbol,
                    VertexTextSymbol = null,
                    MidVertexSymbol = null
                }
            };
        }

        mapViewInteractionOptions = moveFeatureService.GetCurrentInteractionOptions(this.mapView);
        this.graphicsOverlay = graphicsOverlay;
    }

    public void SetTargetLayer(string layerName) => this.layerName = layerName;

    public void Start()
    {
        if (mapView == null) return;

        if (mapView.GeometryEditor?.Tool is VertexTool vertexTool)
        {
            vertexTool.Style.SelectedVertexSymbol = SelectedVertexSymbol;
            vertexTool.Style.VertexSymbol = SelectedVertexSymbol;
            vertexTool.Style.VertexTextSymbol = null;
            vertexTool.Style.MidVertexSymbol = null;

        }

        mapView.GeoViewTapped += MapView_GeoViewTapped;
    }

    public void Stop()
    {
        if (mapView == null) return;

        if (mapView.GeometryEditor?.Tool is VertexTool vertexTool)
        {
            vertexTool.Style.VertexSymbol = RollbackVertexSymbol;
            vertexTool.Style.SelectedVertexSymbol = RollbackVertexSymbol;
            vertexTool.Style.MidVertexSymbol = RollbackVertexSymbol;
            vertexTool.Style.VertexTextSymbol = null;
        }

        mapView.GeoViewTapped -= MapView_GeoViewTapped;

        var geometryEditor = mapView.GeometryEditor;
        if (geometryEditor != null)
        {
            moveFeatureService.StopGeometryEditor(geometryEditor);
        }

        if (currentlyEditingGraphic != null)
        {
            currentlyEditingGraphic.IsSelected = false;
            Clear();
            currentlyEditingGraphic = null;
        }
        moveFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions);
        mapViewInteractionOptions = null;
    }

    public void Clear()
    {
        if (graphicsOverlay == null) return;

        moveFeatureService.RemoveGraphic(graphicsOverlay, ref currentlyEditingGraphic);

        var geometryEditor = mapView?.GeometryEditor;
        if (geometryEditor != null)
        {
            moveFeatureService.ClearGeometry(geometryEditor);
        }
    }
    public async Task CommitAsync()
    {
        try
        {
            this.messenger.Send(new MapLoadingMessage(true));

            if (activeFeature == null) return;

            var geometryEditor = mapView?.GeometryEditor;

            moveFeatureService.StopGeometryEditor(geometryEditor!);

            var objectId = activeFeature.Attributes["OBJECTID"];

            loggerService.Info($"ObjectId ={objectId}, After Assign: {((MapPoint)activeFeature!.Geometry!).X}, {((MapPoint)activeFeature.Geometry).Y}");

            var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();
            if (caseRequest is null)
            {
                return;
            }

            var map = mapView!.Map;
            if (map == null) return;

            var validationErrors = await moveFeatureService.ValidateAsync(map, activeFeature, caseRequest, this.layerName!, FeatureAction.Modify);

            if (validationErrors.Count != 0)
            {
                string errorMessages = "Validation Errors:\n" + string.Join("\n", validationErrors.Select(e => $"- {e.ErrorMessage}"));
                notificationManager.Show("Move Point Validation", errorMessages, NotificationType.Error);

                return;
            }

            var result = await moveFeatureService.PersistenceFeature(mapView, activeFeature, this.layerName!);

            if (result)
            {
                notificationManager.Show(MovePoint, "Canopy point feature moved successfully.", NotificationType.Success);                
                Clear();
            }
        }
        catch (Exception ex)
        {
            loggerService.Error(ex, "Exception occurred while running Move point feature.");
            var geometryEditor = mapView?.GeometryEditor;
            if (geometryEditor != null)
            {
                moveFeatureService.StopGeometryEditor(geometryEditor);
            }
            Clear();
        }
        finally
        {     
            activeFeature = null;
            featureSelected = false;

            if (mapView != null)
            {
                moveFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions);
            }
            this.messenger.Send(new MapLoadingMessage(false));
        }
    }

    private async void MapView_GeoViewTapped(object? sender, GeoViewInputEventArgs e)
    {
        try
        {
            this.messenger.Send(new MapLoadingMessage(true));

            if (mapView == null || e.Location == null)
                return;

            // SECOND TAP = move point
            if (featureSelected &&
                activeFeature != null &&
                mapView.GeometryEditor?.IsStarted == true)
            {
                var distance = GeometryEngine.Distance(originalPoint!, e.Location);

                loggerService.Info($"Clicked distance from original point: {distance:F2} m");

                var dropScreenPoint = mapView.LocationToScreen(e.Location);

               double pixelDistance = Math.Sqrt(
               Math.Pow(dropScreenPoint.X - selectedScreenPoint!.Value.X, 2) +
               Math.Pow(dropScreenPoint.Y - selectedScreenPoint.Value.Y, 2));

                if (pixelDistance <= ClickTolerancePixels)
                {
                    notificationManager.Show(MovePoint, "Drop location cannot be the same as the selected location.", NotificationType.Warning);

                    return;
                }
                activeFeature.Geometry = e.Location;

                await CommitAsync();

                return;
            }

            // FIRST TAP = select point

            var operationalLayers = mapView.Map?.OperationalLayers;

            if (operationalLayers == null)
                return;

            var featureLayer = await featureLayerService.GetFeatureLayerAsync(operationalLayers,layerName!);

            if (featureLayer == null)
                return;

            var tappedFeature = await moveFeatureService.GetFeatureFromMapPointAsync(mapView,featureLayer,e.Location);

            if (tappedFeature == null)
            {
                notificationManager.Show(MovePoint, "Select a canopy point to move", NotificationType.Warning);
                return;
            }
            if (tappedFeature.Geometry is not MapPoint)
            {
                this.notificationManager.Show("Move feature", "Selected feature is not a point", NotificationType.Error);
                return;
            }

            var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();

            if (caseRequest is null)
            {
                return;
            }

            if (tappedFeature.LoadStatus != LoadStatus.Loaded)
            {
                await tappedFeature.LoadAsync();
            }

            var validationErrors = await moveFeatureService.ValidateAsync(mapView.Map!, tappedFeature, caseRequest, this.layerName!, FeatureAction.Modify);
            if (validationErrors.Count != 0)
            {
                string errorMessages = "Validation Errors:\n" + string.Join("\n", validationErrors.Select(e => $"- {e.ErrorMessage}"));
                this.notificationManager.Show("Move Point Validation", errorMessages, NotificationType.Error);

                return;
            }
            if (tappedFeature.Geometry is not MapPoint point)
                return;
          

            activeFeature = tappedFeature;

            originalPoint = new MapPoint(
                point.X,
                point.Y,
                point.SpatialReference);

            selectedScreenPoint = mapView.LocationToScreen(originalPoint);

            moveFeatureService.StartGeometryEditor(mapView.GeometryEditor!, activeFeature.Geometry);

            featureSelected = true;

            notificationManager.Show(MovePoint, "Click the new location for this point.",NotificationType.Info);

            var objectId = activeFeature.Attributes["OBJECTID"];

            loggerService.Info($"ObjectId ={objectId}, Before Assign: {((MapPoint)activeFeature.Geometry).X}, {((MapPoint)activeFeature.Geometry).Y}");

            messenger.Send<FeatureGuideNextStepMessage>();
        }
        catch (Exception ex)
        {
            loggerService.Error(ex, "Exception occurred while running Move point feature.");
            Clear();
        }
        finally
        {
            this.messenger.Send(new MapLoadingMessage(false));   
        }
    }
}
