
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Options;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Messages.Editing;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Models.MIEditorLayerConfig;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.FeatureGuide;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.AddFeatures.Operation;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IAddFeatureOperation) })]
public class AddPolygonOperation : IAddFeatureOperation
{
    private const short minVertexCount = 3;
    private const GeometryType geometryType = GeometryType.Polygon;

    private bool isNewSketch;
    private string? layerName;
    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;

    private readonly string lpisPlyLayerName;
    private readonly Symbol finalGraphicSymbol;
    private readonly IMessenger messenger;
    private readonly IAddFeatureService addFeatureService;
    private readonly INotificationManager notificationManager;
    private readonly IGeometryClippingService geometryClippingService;

    public AddPolygonOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser, 
        IMessenger messenger,
        IAddFeatureService addFeatureService,
        INotificationManager notificationManager,
        IGeometryClippingService lpisClippingService,
        ApplicationSettings appSettings)
    {
        this.messenger = messenger;
        this.addFeatureService = addFeatureService;
        this.notificationManager = notificationManager;
        this.geometryClippingService = lpisClippingService;

        finalGraphicSymbol = drawingSymbolColourParser.FillSymbol(symbolSettings.Value.Polygon, "Polygon");
        lpisPlyLayerName = appSettings.Layers.LPIS_Poly;

        messenger.Register<AddPolygonOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));
    }

    public void Receive(ClearEditingGraphicMessage message)
    {
        if (graphicsOverlay == null) return;
        addFeatureService.RemoveGraphic(graphicsOverlay, ref currentlyEditingGraphic);
    }

    public void Initialise(MapView mapView, GraphicsOverlay graphicsOverlay)
    {
        if (mapView == null) return;

        this.mapView = mapView;
        if (mapView.GeometryEditor != null)
        {
            mapView.GeometryEditor.Tool = new VertexTool
            {
                Style = new GeometryEditorStyle
                {
                    VertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                    SelectedVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                    MidVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                    VertexTextSymbol = null
                }
            };
        }

        mapViewInteractionOptions = addFeatureService.GetCurrentInteractionOptions(mapView);
        this.graphicsOverlay = graphicsOverlay;
    }


    public void SetTargetLayer(string layerName) => this.layerName = layerName;

    public void Start()
    {
        mapView!.GeoViewDoubleTapped += MapView_GeoViewDoubleTapped;
        mapView.GeometryEditor!.PropertyChanged += GeometryEditor_PropertyChanged;

        addFeatureService.DisableMapInteraction(mapView);
        addFeatureService.StartGeometryEditor(mapView.GeometryEditor, geometryType);

        messenger.Send(new SketchingStartedMessage<EditingTool>(EditingTool.AddPolygon));
    }

    public void Stop()
    {
        mapView!.GeoViewDoubleTapped -= MapView_GeoViewDoubleTapped;
        mapView.GeometryEditor!.PropertyChanged -= GeometryEditor_PropertyChanged;

        addFeatureService.StopGeometryEditor(mapView.GeometryEditor);

        if (currentlyEditingGraphic != null)
        {
            currentlyEditingGraphic.IsSelected = false;
            Clear();
            currentlyEditingGraphic = null;
        }

        addFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions!);
        mapViewInteractionOptions = null;
    }

    private void OnSuccessfulCompletion(Feature feature)
    {
        if (string.Equals(layerName, lpisPlyLayerName, StringComparison.InvariantCultureIgnoreCase))
        {
            messenger.Send(new EditAttributesMessage(layerName!, (ArcGISFeature)feature), lpisPlyLayerName);
            return;
        }

        messenger.Send(new EditAttributesMessage(layerName!, (ArcGISFeature)feature));
    }

    private void OnFailedCompletion(ICollection<FeatureValidationError> errors)
    {
        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions!);
        addFeatureService.RestartGeometryEditor(mapView!.GeometryEditor!, geometryType);
        isNewSketch = true;

        notificationManager.Show("Add Polygon",
            addFeatureService.GetErrors(errors),
            NotificationType.Error);
    }


    private void GeometryEditor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GeometryEditor.Geometry) ||
            mapView!.GeometryEditor!.Geometry is null)
            return;

        if (isNewSketch)
        {
            addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
            isNewSketch = false;
        }

        var geometry = mapView.GeometryEditor.Geometry;
        double? length = null;
        double? area = null;

        if (geometry is Polyline line)
        {
            length = line.LengthGeodetic(LinearUnits.Meters, GeodeticCurveType.Geodesic);
        }
        else if (geometry is Polygon polygon)
        {
            length = polygon.LengthGeodetic(LinearUnits.Meters, GeodeticCurveType.Geodesic);
            area = polygon.AreaGeodetic(AreaUnits.SquareMeters, GeodeticCurveType.Geodesic);
        }

        messenger.Send(new DrawingProgressMessage(length, area));
    }

    private static bool AbortGeoViewDoubleTapped(GeometryEditor geometryEditor, GeometryType geometryType) 
        => !geometryEditor.IsStarted || geometryEditor.Geometry?.GeometryType != geometryType;

    private async void MapView_GeoViewDoubleTapped(object? sender, GeoViewInputEventArgs e)
    {
        if (AbortGeoViewDoubleTapped(mapView!.GeometryEditor!, geometryType))
            return;

        e.Handled = true;
        addFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions!);

        var geometry = mapView.GeometryEditor!.Stop();

        if (!addFeatureService.IsValidGeometry(geometry!, minVertexCount))
        {
            addFeatureService.RestartGeometryEditor(mapView.GeometryEditor, geometryType);
            isNewSketch = true;

            return;
        }

        var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();
        if (caseRequest is null)
        {
            OnFailedCompletion([new FeatureValidationError(string.Empty, "Unable to retrieve selected case details.")]);

            return;
        }

        if (IsLpisLayer())
        {
            var clippingResult = await geometryClippingService.ClipOverlappingAreasAsync(
                geometry!,
                mapView.Map!,
                caseRequest.CaseId);

            if (!clippingResult.Success)
            {
                notificationManager.Show("LPIS Clipping Error",
                    clippingResult.ErrorMessage ?? "Failed to clip overlapping areas.",
                    NotificationType.Error);

                OnFailedCompletion([]);
                return;
            }

            if (clippingResult.WasClipped)
            {
                foreach (var message in clippingResult.Messages)
                {
                    notificationManager.Show("LPIS Clipping",
                        message,
                        NotificationType.Info);
                }
            }

            geometry = clippingResult.ProcessedGeometry;
        }

        var feature = await this.addFeatureService.CreateFeatureFromGeometryAsync(
            mapView.Map!.OperationalLayers, geometry!, layerName!);

        if (feature == null)
        {
            OnFailedCompletion([new FeatureValidationError(string.Empty, "Failed to create feature.")]);
            return;
        }

        var validationResult = await this.addFeatureService.ValidateAsync(mapView.Map, feature, caseRequest, layerName!);

        if (validationResult == null || validationResult.Count == 0)
        {
            messenger.Send<FeatureGuideNextStepMessage>();

            addFeatureService.CreateGraphic(
                graphicsOverlay!,
                geometry!,
                finalGraphicSymbol,
                ref currentlyEditingGraphic);

            OnSuccessfulCompletion(feature);

            addFeatureService.RestartGeometryEditor(mapView.GeometryEditor, geometryType);
            isNewSketch = true;

            return;
        }

        OnFailedCompletion(validationResult);
    }

    public void Clear()
    {
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
        addFeatureService.ClearGeometry(mapView!.GeometryEditor!);
    }

    private bool IsLpisLayer()
    {
        return string.Equals(layerName, lpisPlyLayerName, StringComparison.InvariantCultureIgnoreCase);
    }
}
