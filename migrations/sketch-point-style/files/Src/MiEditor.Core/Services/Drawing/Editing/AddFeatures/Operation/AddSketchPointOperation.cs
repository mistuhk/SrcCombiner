
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
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.AddFeatures.Operation;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IAddFeatureOperation) })]
public class AddSketchPointOperation : IAddFeatureOperation,
    IRecipient<ClearEditingGraphicMessage>
{
    private const short minVertexCount = 1;
    private const GeometryType geometryType = GeometryType.Point;

    private bool isNewSketch;
    private string? layerName;
    private MapView? mapView;
    private GraphicsOverlay? graphicsOverlay;
    private Graphic? currentlyEditingGraphic;
    private MapViewInteractionOptions? mapViewInteractionOptions;

    private readonly string miSketchLayer;
    private readonly Symbol finalGraphicSymbol;
    private readonly IMessenger messenger;
    private readonly IAddFeatureService addFeatureService;
    private readonly INotificationManager notificationManager;

    public AddSketchPointOperation(
        [FromKeyedServices(InfieldLayerType.Point)] InfieldLayer layerSettings,
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        IMessenger messenger,
        IAddFeatureService addFeatureService,
        INotificationManager notificationManager)
    {
        this.messenger = messenger;
        this.addFeatureService = addFeatureService;
        this.notificationManager = notificationManager;

        finalGraphicSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.InFieldSketchPoint, "InFieldSketchPoint");
        miSketchLayer = layerSettings.Name;

        messenger.Register<AddSketchPointOperation, ClearEditingGraphicMessage>(this, (r, m) => r.Receive(m));
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
        mapView!.GeoViewTapped += MapView_GeoViewTapped;
        mapView!.GeometryEditor!.PropertyChanged += GeometryEditor_PropertyChanged;

        addFeatureService.DisableMapInteraction(mapView);
        addFeatureService.StartGeometryEditor(mapView.GeometryEditor, geometryType);

        messenger.Send(new SketchingStartedMessage<EditingTool>(EditingTool.AddSketchPoint));
    }

    public void Stop()
    {
        mapView!.GeoViewTapped -= MapView_GeoViewTapped;
        mapView!.GeometryEditor!.PropertyChanged -= GeometryEditor_PropertyChanged;

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

    public void Clear()
    {
        addFeatureService.RemoveGraphic(graphicsOverlay!, ref currentlyEditingGraphic);
        addFeatureService.ClearGeometry(mapView!.GeometryEditor!);
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

        // Points don't track progress, but we still handle the isNewSketch logic
        messenger.Send(new DrawingProgressMessage(null, null));
    }    

    private async void MapView_GeoViewTapped(object? sender, GeoViewInputEventArgs e)
    {        
        if (!mapView!.GeometryEditor!.IsStarted || mapView.GeometryEditor.Geometry?.GeometryType != geometryType)
            return;

        e.Handled = true;
        addFeatureService.EnableMapInteraction(mapView, mapViewInteractionOptions!);

        var geometry = e.Location;

        if (!AbortIfGeometryIsInvalid(geometry, geometryType))
            return;

        var feature = await addFeatureService.CreateFeatureFromGeometryAsync(
            mapView!.Map!.OperationalLayers,
            geometry!,
            miSketchLayer);

        if (feature == null)
        {
            OnFailedCompletion([new FeatureValidationError(string.Empty, "Failed to create feature.")]);
            return;
        }

        if (!await AbortIfFeatureIsInvalid(feature))
            return;

        addFeatureService.CreateGraphic(
                graphicsOverlay!,
                geometry!,
                finalGraphicSymbol,
                ref currentlyEditingGraphic);

        OnSuccessfulCompletion(feature);
        return;
    }

    private void OnSuccessfulCompletion(Feature feature)
    {
        messenger.Send(new EditAttributesMessage(layerName!, (ArcGISFeature)feature, miSketchLayer));
    }

    private void OnFailedCompletion(ICollection<FeatureValidationError> errors)
    {
        notificationManager.Show("Add Point",
            addFeatureService.GetErrors(errors),
            NotificationType.Error);

        addFeatureService.EnableMapInteraction(mapView!, mapViewInteractionOptions!);
        addFeatureService.RestartGeometryEditor(mapView!.GeometryEditor!, geometryType);

        isNewSketch = true;
    }

    private bool AbortIfGeometryIsInvalid(Geometry? geometry, GeometryType geometryType)
    {
        if (!addFeatureService.IsValidGeometry(geometry!, minVertexCount))
        {
            notificationManager.Show("Add Sketch Point",
                "No valid geometry found in sketch.",
                NotificationType.Error);

            addFeatureService.RestartGeometryEditor(mapView!.GeometryEditor!, geometryType);
            return false;
        }
        return true;
    }

    private async Task<bool> AbortIfFeatureIsInvalid(Feature feature)
    {
        List<FeatureValidationError> validationResult = [];
        var caseRequest = await messenger.Send<SelectedCaseRequestMessage>();
        if (caseRequest is null)
        {
            validationResult.Add(new FeatureValidationError(string.Empty, "Unable to retrieve selected case details."));
            OnFailedCompletion(validationResult);

            return false;
        }

        validationResult = await this.addFeatureService.ValidateAsync(mapView!.Map!, feature, caseRequest, miSketchLayer);
        if (validationResult == null || validationResult.Count == 0) return true;

        OnFailedCompletion(validationResult);
        return false;
    }
}
