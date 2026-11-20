
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
public class AddLineOperation : IAddFeatureOperation
{
    private const short minVertexCount = 2;
    private const GeometryType geometryType = GeometryType.Polyline;

    private bool isNewSketch;
    private string layerName = string.Empty;
    private readonly string lpisPlyLayerName;

    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;

    private readonly Symbol finalGraphicSymbol;
    private readonly IMessenger messenger;
    private readonly IAddFeatureService addFeatureService;
    private readonly INotificationManager notificationManager;

    public AddLineOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser parser,
        IMessenger messenger,
        IAddFeatureService addFeatureService,
        INotificationManager notificationManager,
        ApplicationSettings appSettings)
    {
        this.messenger = messenger;
        this.addFeatureService = addFeatureService;
        this.notificationManager = notificationManager;

        finalGraphicSymbol = parser.LineSymbol(symbolSettings.Value.Line, "Line");
        lpisPlyLayerName = appSettings.Layers.LPIS_Poly;

        messenger.Register<AddLineOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));
    }

    public void Receive(ClearEditingGraphicMessage message)
    {
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
    }

    public void Initialise(MapView mapView, GraphicsOverlay graphicsOverlay)
    {
       
        ArgumentNullException.ThrowIfNull(mapView);

        this.mapView = mapView;

        var geometryEditor = GetGeometryEditorOrThrow();

        geometryEditor.Tool = new VertexTool
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

        var geometryEditor = GetGeometryEditorOrThrow();

        mapView!.GeoViewDoubleTapped += MapView_GeoViewDoubleTapped;
        geometryEditor.PropertyChanged += GeometryEditor_PropertyChanged;

        addFeatureService.DisableMapInteraction(mapView);
        addFeatureService.StartGeometryEditor(geometryEditor, geometryType);

        messenger.Send(new SketchingStartedMessage<EditingTool>(EditingTool.AddLine));
    }

    public void Stop()
    {
        var geometryEditor = GetGeometryEditorOrThrow();

        mapView!.GeoViewDoubleTapped -= MapView_GeoViewDoubleTapped;
        geometryEditor.PropertyChanged -= GeometryEditor_PropertyChanged;

        addFeatureService.StopGeometryEditor(geometryEditor);

        if (currentlyEditingGraphic != null)
        {
            currentlyEditingGraphic.IsSelected = false;
            Clear();
            currentlyEditingGraphic = null;
        }

        addFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions);
        mapViewInteractionOptions = null;
    }

    private GeometryEditor GetGeometryEditorOrThrow()
    {
        return mapView!.GeometryEditor
            ?? throw new InvalidOperationException(
                "GeometryEditor is not available on the MapView.");
    }

    public void Clear()
    {
        var geometryEditor = GetGeometryEditorOrThrow();

        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
        addFeatureService.ClearGeometry(geometryEditor);
    }

    private void GeometryEditor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var geometryEditor = GetGeometryEditorOrThrow();

        if (e.PropertyName != nameof(GeometryEditor.Geometry) ||
            geometryEditor.Geometry is null)
            return;

        if (isNewSketch)
        {
            addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
            isNewSketch = false;
        }

        var geometry = geometryEditor.Geometry;
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

    private async void MapView_GeoViewDoubleTapped(object? sender, GeoViewInputEventArgs e)
    {
        var geometryEditor = GetGeometryEditorOrThrow();

        if (!geometryEditor.IsStarted || geometryEditor.Geometry?.GeometryType != geometryType)
            return;

        e.Handled = true;
        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions);

        var geometry = geometryEditor.Stop();


        if (geometry is null)
        {
            notificationManager.Show(
                "Add Line",
                "No geometry was created.",
                NotificationType.Error);

            addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);
            return;
        }


        if (!addFeatureService.IsValidGeometry(geometry, minVertexCount))
        {
            notificationManager.Show("Add Line",
                $"A line requires a minimum of {minVertexCount} points.",
                NotificationType.Error);

            addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);
            return;
        }

        var feature = await addFeatureService.CreateFeatureFromGeometryAsync(
            mapView!.Map!.OperationalLayers,
            geometry,
            layerName);

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

        validationResult = await this.addFeatureService.ValidateAsync(mapView.Map, feature, caseRequest, layerName);

        if (validationResult == null || validationResult.Count == 0)
        {
            messenger.Send<FeatureGuideNextStepMessage>();

            addFeatureService.CreateGraphic(
                graphicsOverlay!,
                geometry,
                finalGraphicSymbol,
                ref currentlyEditingGraphic);

            OnSuccessfulCompletion(feature);

            addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);
            isNewSketch = true;

            return;
        }

        OnFailedCompletion(validationResult);
    }

    private void OnSuccessfulCompletion(Feature feature)
    {
        if (string.Equals(layerName, lpisPlyLayerName, StringComparison.InvariantCultureIgnoreCase))
        {
            messenger.Send(new EditAttributesMessage(layerName, (ArcGISFeature)feature), lpisPlyLayerName);
            return;
        }

        messenger.Send(new EditAttributesMessage(layerName, (ArcGISFeature)feature));
    }

    private void OnFailedCompletion(ICollection<FeatureValidationError> errors)
    {
        var geometryEditor = GetGeometryEditorOrThrow();

        notificationManager.Show("Add Line",
            addFeatureService.GetErrors(errors),
            NotificationType.Error);

        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions);
        addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);
        isNewSketch = true;
    }
}
