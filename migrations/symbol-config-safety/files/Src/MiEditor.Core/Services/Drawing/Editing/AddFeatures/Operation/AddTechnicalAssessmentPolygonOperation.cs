
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Messages.Editing;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Models.MIEditorLayerConfig;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers.Abstraction;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.AddFeatures.Operation;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IAddFeatureOperation) })]
public class AddTechnicalAssessmentPolygonOperation : IAddFeatureOperation
{
    private const short minVertexCount = 3;
    private const GeometryType geometryType = GeometryType.Polygon;

    private readonly IMessenger messenger;
    private readonly IAddFeatureService addFeatureService;
    private readonly INotificationManager notificationManager;
    private readonly IGeometryAreaHelper geometryAreaHelper;
    private readonly Symbol finalGraphicSymbol;

    private bool isNewSketch;
    private string? layerName;
    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;

    public AddTechnicalAssessmentPolygonOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        IMessenger messenger,
        IAddFeatureService addFeatureService,
        INotificationManager notificationManager,
        IGeometryAreaHelper geometryAreaHelper) 
    {
        this.addFeatureService = addFeatureService;
        this.notificationManager = notificationManager;
        this.geometryAreaHelper = geometryAreaHelper;
        this.messenger = messenger;

        finalGraphicSymbol = drawingSymbolColourParser.FillSymbol(symbolSettings.Value.Polygon, "Polygon");
        this.messenger.Register<AddTechnicalAssessmentPolygonOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));
    }

    public void Receive(ClearEditingGraphicMessage message)
    {
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
    }

    public void Initialise(MapView mapView, GraphicsOverlay graphicsOverlay)
    {
        if (mapView == null) return;

        this.mapView = mapView;
        mapView.GeometryEditor!.Tool = new VertexTool
        {
            Style = new GeometryEditorStyle
            {
                VertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                SelectedVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                MidVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
                VertexTextSymbol = null
            }
        };

        mapViewInteractionOptions = addFeatureService.GetCurrentInteractionOptions(mapView);

        this.graphicsOverlay = graphicsOverlay;
    }

    public void SetTargetLayer(string layerName) => this.layerName = layerName;

    public void Start()
    {
        mapView!.GeoViewTapped += MapView_GeoViewTapped;
        mapView.GeometryEditor!.PropertyChanged += GeometryEditor_PropertyChanged;

        addFeatureService.DisableMapInteraction(mapView);
        addFeatureService.StartGeometryEditor(mapView.GeometryEditor, geometryType);

        messenger.Send(new SketchingStartedMessage<EditingTool>(EditingTool.AddPolygon));
    }

    public void Stop()
    {
        mapView!.GeoViewTapped -= MapView_GeoViewTapped;
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

    private void OnFailedCompletion(ICollection<FeatureValidationError> errors)
    {
        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions!);
        addFeatureService.RestartGeometryEditor(mapView!.GeometryEditor!, geometryType);
        isNewSketch = true;

        notificationManager.Show("Add Technical Assessment",
            addFeatureService.GetErrors(errors),
            NotificationType.Error);
    }

    private void OnSuccessfulCompletion(Feature feature)
    {
        messenger.Send(new EditAttributesMessage(layerName!, (ArcGISFeature)feature, useAlterLayer: true));
    }

    private async void MapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.Maui.GeoViewInputEventArgs e)
    {
        if (!mapView!.GeometryEditor!.IsStarted || mapView.GeometryEditor.Geometry?.GeometryType != geometryType) 
            return;

        e.Handled = true;
        addFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions!);

        var geometry = e.Location;
        if (!addFeatureService.IsValidGeometry(geometry!, minVertexCount))
        {
            addFeatureService.RestartGeometryEditor(mapView.GeometryEditor, geometryType);
            isNewSketch = true;

            return;
        }

        var resizedGeometry = this.geometryAreaHelper.ResizeGeometry(geometry);
        var feature = await addFeatureService
            .CreateFeatureFromGeometryAsync(
                mapView.Map!.OperationalLayers, 
                resizedGeometry!, 
                layerName!);

        if (feature == null)
        {
            OnFailedCompletion([new FeatureValidationError(string.Empty, "Failed to create feature.")]);
            return;
        }

        List<FeatureValidationError> validationResult = [];
        var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();
        if (caseRequest is null)
        {
            validationResult.Add(new FeatureValidationError(string.Empty, "Unable to retrieve selected case details."));
            OnFailedCompletion(validationResult);

            return;
        }
        validationResult = await this.addFeatureService.ValidateAsync(mapView.Map, feature, caseRequest, layerName!);

        if (validationResult == null || validationResult.Count == 0)
        {
            addFeatureService.CreateGraphic(graphicsOverlay!,
                geometry!, finalGraphicSymbol, ref currentlyEditingGraphic);
            OnSuccessfulCompletion(feature);

            return;
        }

        OnFailedCompletion(validationResult);
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

    public void Clear()
    {
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
        addFeatureService.ClearGeometry(mapView!.GeometryEditor!);
    }
}
