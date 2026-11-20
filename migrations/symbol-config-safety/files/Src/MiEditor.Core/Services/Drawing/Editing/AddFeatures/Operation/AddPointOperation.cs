
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
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.AddFeatures.Operation;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IAddFeatureOperation) })]
public class AddPointOperation : IAddFeatureOperation
{
    private const short minVertexCount = 1;
    private const GeometryType geometryType = GeometryType.Point;

    private bool isNewSketch;
    private string layerName=string.Empty;
    private readonly string lpisPlyLayerName;

    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;

    private readonly Symbol finalGraphicSymbol;
    private readonly IMessenger messenger;
    private readonly IAddFeatureService addFeatureService;
    private readonly INotificationManager notificationManager;

    public AddPointOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        IMessenger messenger,
        IAddFeatureService addFeatureService,
        INotificationManager notificationManager,
        ApplicationSettings appSettings)
    {
        this.messenger = messenger;
        this.addFeatureService = addFeatureService;
        this.notificationManager = notificationManager;

        finalGraphicSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.Point, "Point");
        lpisPlyLayerName = appSettings.Layers.LPIS_Poly;

        messenger.Register<AddPointOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));
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
        mapView!.GeoViewTapped += MapView_GeoViewTapped;
        geometryEditor.PropertyChanged += GeometryEditor_PropertyChanged;

        addFeatureService.DisableMapInteraction(mapView);
        addFeatureService.StartGeometryEditor(geometryEditor, geometryType);

        messenger.Send(new SketchingStartedMessage<EditingTool>(EditingTool.AddPoint));
    }

    public void Stop()
    {
        var geometryEditor = GetGeometryEditorOrThrow();
        mapView!.GeoViewTapped -= MapView_GeoViewTapped;
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

    public void Clear()
    {
        var geometryEditor = GetGeometryEditorOrThrow();
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
        addFeatureService.ClearGeometry(geometryEditor);
    }

    private GeometryEditor GetGeometryEditorOrThrow()
    {
        return mapView!.GeometryEditor
            ?? throw new InvalidOperationException(
                "GeometryEditor is not available on the MapView.");
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

        // Points don't track progress, but we still handle the isNewSketch logic
        messenger.Send(new DrawingProgressMessage(null, null));
    }

    private async void MapView_GeoViewTapped(object? sender, GeoViewInputEventArgs e)
    {
        var geometryEditor = GetGeometryEditorOrThrow();
        if (!geometryEditor.IsStarted || geometryEditor.Geometry?.GeometryType != geometryType)
            return;

        e.Handled = true;
        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions);

        var geometry = e.Location;

        if (geometry is null)
        {
            notificationManager.Show(
                "Add Point",
                "No geometry was created.",
                NotificationType.Error);

            addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);
            return;
        }

        if (!addFeatureService.IsValidGeometry(geometry, minVertexCount))
        {
            notificationManager.Show("Add Point", "No valid geometry found in sketch.", NotificationType.Error);
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
            addFeatureService.CreateGraphic(
                graphicsOverlay!,
                geometry,
                finalGraphicSymbol,
                ref currentlyEditingGraphic);

            OnSuccessfulCompletion(feature);
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

        notificationManager.Show("Add Point",addFeatureService.GetErrors(errors), NotificationType.Error);

        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions);
        addFeatureService.RestartGeometryEditor(geometryEditor, geometryType);

        isNewSketch = true;
    }
}
