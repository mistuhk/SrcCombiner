
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Options;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Aoi;
using WG.MiEditor.Core.Services.Aoi.Validation;
using WG.MiEditor.Core.Services.Drawing;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Core.Services.Overlay;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.Aoi;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Models;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Aoi;

[RegisterService(ServiceLifetime.Scoped)]
public partial class AoiControlViewModel : ObservableRecipient,
        IRecipient<ClearGraphicsOverlayMessage>,
        IRecipient<DefineAoiMessage>,
        IRecipient<DefineManulAoiMessage>,
        IRecipient<DeleteManualAoiMessage>,
        IRecipient<DefineAutoAoiMessage>,
        IRecipient<SaveAoiMessage>,
        IRecipient<StopAoiInteractionMessage>,
        IRecipient<MeasurementInProgressMessage>
{
    private readonly IAreaOfInterestService areaOfInterestService;
    private readonly IOperationFeedbackService feedbackService;
    private readonly ILoggerService loggerService;
    private readonly IMapViewService mapViewService;
    private readonly IAoiValidator aoiValidator;
    private readonly IOverlayService overlayService;

    private readonly ApplicationSettings appSettings;
    private readonly Symbol selectedVertexSymbol;
    private readonly Symbol midVertexSymbol;

    private Geometry? tempAoiGeometry = null;
    private MapView? mapView = null!;
    private List<Geometry> AoiGeometrys = [];

    // The map's geometry editor is shared with the measure and editing tools. Each tool assigns
    // its own VertexTool, so this identifies whether the running session is the AOI drawing.
    private VertexTool? aoiVertexTool;

    public AoiControlViewModel(
        IMessenger messenger,
        IAreaOfInterestService areaOfInterestService,
        IOperationFeedbackService feedbackService,
        ILoggerService loggerService,
        IMapViewService mapViewService,
        IAoiValidator aoiValidator,
        IOverlayService overlayService,
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        ApplicationSettings appSettings) : base(messenger)
    {
        IsActive = true;

        this.areaOfInterestService = areaOfInterestService;
        this.feedbackService = feedbackService;
        this.loggerService = loggerService;
        this.mapViewService = mapViewService;

        this.aoiValidator = aoiValidator;
        this.overlayService = overlayService;
        this.appSettings = appSettings;

        selectedVertexSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.SelectedVertexSymbol, "SelectedVertexSymbol");
        midVertexSymbol = drawingSymbolColourParser.MarkerSymbol(symbolSettings.Value.MidVertexSymbol, "MidVertexSymbol");
    }

    [ObservableProperty]
    public partial AoiTool ActiveTool { get; set; }

    public async void Receive(DefineAutoAoiMessage message)
    {
        StopGeometryEditor();

        ActiveTool = AoiTool.AutoAOI;

        this.Messenger.Send(new MapLoadingMessage(true));

        await GetAreaOfInterest();

        this.Messenger.Send(new MapLoadingMessage(false));
    }

    public void Receive(DefineManulAoiMessage message)
    {
        StopGeometryEditor();

        ActiveTool = AoiTool.DrawAOI;

        if (this.mapView?.GeometryEditor != null)
        {
            aoiVertexTool = new VertexTool
            {
                Style = new GeometryEditorStyle
                {
                    VertexSymbol = selectedVertexSymbol,
                    SelectedVertexSymbol = selectedVertexSymbol,
                    MidVertexSymbol = midVertexSymbol,
                    VertexTextSymbol = null,
                }
            };

            // Any other tool's session should already have been ended through the activation
            // coordinator; this only clears a stale one so Start cannot throw.
            if (this.mapView.GeometryEditor.IsStarted) this.mapView.GeometryEditor.Stop();

            this.mapView.GeometryEditor.Tool = aoiVertexTool;
            this.mapView.GeoViewDoubleTapped += OnMapDoubleTapped;

            this.mapView.GeometryEditor.Start(GeometryType.Polygon);
        }
    }

    public void Receive(StopAoiInteractionMessage message) => StopAoiInteraction();

    // A measurement is starting. AoiControl also resets the tool, but only while it is loaded;
    // this makes sure AOI stops drawing and listening to taps regardless.
    public void Receive(MeasurementInProgressMessage message) => StopAoiInteraction();

    private void StopAoiInteraction()
    {
        StopGeometryEditor();
        ActiveTool = new();
    }

    public async void Receive(SaveAoiMessage message)
    {
        StopGeometryEditor();

        ActiveTool = AoiTool.Save;

        this.Messenger.Send(new MapLoadingMessage(true));

        await SaveAreaOfInterest();

        this.Messenger.Send(new MapLoadingMessage(false));
    }

    public void Receive(DeleteManualAoiMessage message)
    {
        StopGeometryEditor();

        if (this.mapView != null)
        {
            this.mapView.GeoViewTapped += OnMapTapped;
        }

        ActiveTool = AoiTool.Delete;
    }

    public void Receive(ClearGraphicsOverlayMessage message)
    {
        ResetAoiState(message.IsClearEnable);
    }

    public async void Receive(DefineAoiMessage message)
    {
        this.mapView = await this.mapViewService.GetMapViewAsync();
        if (this.mapView == null)
        {
            this.loggerService.Warn("Map view is not available. Please try again.");
            return;
        }

        StopGeometryEditor();

        AoiGeometrys?.Clear();

        tempAoiGeometry = null;

        Messenger.Send(new ResetAoiToolbarActiveStateMessage());
    }

    private async void OnMapTapped(object? sender, GeoViewInputEventArgs e)
    {
        // Another tool (for example the sheep movement buffer) has already consumed this tap.
        if (e.Handled) return;

        try
        {
            switch (ActiveTool.Name)
            {
                case "DrawAOI":
                    break;
                case "Delete":
                    var result = await this.areaOfInterestService.DeleteSelectedAoiAsync(e.Location!, AoiGeometrys);
                    if (result != null)
                    {
                        AoiGeometrys = result;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            this.loggerService.Error(ex, $"Error occurred in {nameof(OnMapTapped)}.");
        }
    }

    private async void OnMapDoubleTapped(object? sender, GeoViewInputEventArgs e)
    {
        try
        {
            if (this.mapView == null) return;

            if (ActiveTool.Equals(AoiTool.DrawAOI) && IsAoiDrawing())
            {

                e.Handled = true;

                var finalGeometry = this.mapView.GeometryEditor!.Stop();

                var validationResult = this.aoiValidator.IsValidPolygon(finalGeometry!);

                if (!validationResult.IsValid)
                {
                    this.feedbackService.NotifyError("AOI", validationResult.ErrorMessage);
                    RestartGeometryEditor();
                    return;
                }
                this.Messenger.Send(new MapLoadingMessage(true));

                await GetAreaOfInterest(finalGeometry);

                this.Messenger.Send(new MapLoadingMessage(false));

                RestartGeometryEditor();
            }
        }
        catch (Exception ex)
        {
            this.loggerService.Error(ex, $"Error occurred in {nameof(OnMapDoubleTapped)}.");
        }
    }

    private void ResetAoiState(bool IsClearEnable)
    {
        if (IsClearEnable)
        {
            AoiGeometrys?.Clear();
            tempAoiGeometry = null;

            var aoiOverlay = this.mapView?.GraphicsOverlays?.FirstOrDefault(go => go.Id == "aoihighlightOverlay");

            if (aoiOverlay == null)
            {
                this.feedbackService.Notify("AOI", "Please define AOI using Auto AOI or Draw AOI tool first.", NotificationType.Info);
            }

            if (mapView?.GraphicsOverlays != null && aoiOverlay != null)
            {
                mapView.GraphicsOverlays.Remove(aoiOverlay);
            }
            Messenger.Send(new ResetAoiToolbarActiveStateMessage());
        }
    }

    private void StopGeometryEditor()
    {
        if (this.mapView != null)
        {
            // Only end the AOI's own drawing session; never stop a measurement or edit in progress.
            if (IsAoiDrawing())
            {
                this.mapView.GeometryEditor!.ClearGeometry();
                this.mapView.GeometryEditor.Stop();
            }

            this.mapView.GeoViewTapped -= OnMapTapped;
            this.mapView.GeoViewDoubleTapped -= OnMapDoubleTapped;
        }
    }

    private async Task GetAreaOfInterest(Geometry? geometry = null)
    {
        try
        {
            // Request AOI geometry from the service
            var aoiResponse = await areaOfInterestService.GetAoiGeometry(geometry!);

            // Handle empty or invalid response
            if (!HandleAoiResponseErrors(aoiResponse))
                return;

            tempAoiGeometry = Geometry.FromJson(aoiResponse.Geometry);

            // Handle null geometry
            if (!HandleNullGeometry())
                return;

            // Validate geometry and handle errors
            if (!await ValidateAndHandleAoiGeometry(tempAoiGeometry!, geometry))
                return;

            // Merge or add geometry to AOI list
            MergeOrAddAoiGeometry(tempAoiGeometry!);

            // Highlight the updated AOI geometry
            Messenger.Send(new AoiGeometryHighlightMessage(GeometryEngine.Union(AoiGeometrys)));
        }
        catch (Exception ex)
        {
            this.feedbackService.NotifyError("AOI", "Error in finding AOI.Please check the application logs for more details");

            this.loggerService.Error(ex, $"Error occurred in {nameof(GetAreaOfInterest)}.");
        }
        finally
        {
            this.loggerService.Info($"{nameof(GetAreaOfInterest)} Process completed");
        }
    }

    /// <summary>
    /// Checks if AOI geometry exists and notifies if missing.
    /// </summary>
    private bool HasAoiGeometry()
    {
        if (AoiGeometrys == null || AoiGeometrys.Count == 0)
        {
            feedbackService.NotifyWarning("AOI", "Please define AOI using Auto AOI or Draw AOI tool first.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validates the union of AOI geometries and handles errors.
    /// </summary>
    private async Task<bool> ValidateAndHandleAoiUnion()
    {
        var (isValid, errorMessage) = await aoiValidator.Validate(GeometryEngine.Union(AoiGeometrys));

        if (isValid && string.IsNullOrEmpty(errorMessage))
            return true;

        if (errorMessage.Equals("AOIEXISTS", StringComparison.OrdinalIgnoreCase))
        {
            feedbackService.NotifyError("AOI", $"AOI already exists for this case, Please exit the case.");
        }
        else
        {
            feedbackService.NotifyError("AOI", $"{appSettings.ErrorMessages.AoiExistMessage}\n({errorMessage})");
        }

        tempAoiGeometry = null;
        AoiGeometrys?.Clear();
        return false;
    }

    /// <summary>
    /// Handles errors in AOI version creation.
    /// </summary>
    private bool HandleAoiVersionErrors(ApiBaseResponseModel? aoiResponse, CaseModel caseModel)
    {
        if (aoiResponse?.HasErrors == true)
        {
            feedbackService.NotifyError("AOI", $"Failed Aoi version creation for {caseModel.CaseId}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Saves the AOI feature and handles errors.
    /// </summary>
    private async Task<bool> SaveAoiFeature(Geometry mergedGeometry, CaseModel caseModel)
    {
        bool isSuccess = await areaOfInterestService.SaveAoiFeatureAsync(mergedGeometry);

        if (!isSuccess)
        {
            feedbackService.NotifyError("AOI", $"Failed Aoi creation for {caseModel.CaseId}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Updates the case model, sends notifications and messages.
    /// </summary>
    private void UpdateCaseAndNotify(CaseModel caseModel, Geometry mergedGeometry)
    {
        caseModel.Geometry = mergedGeometry;

        Messenger.Send(new ZoomToLayerMessage(mergedGeometry.Extent));
        Messenger.Send(new ClearGraphicsOverlayMessage());
        Messenger.Send(new ClearMapOverlaysMessage());

        feedbackService.NotifySuccess("AOI", $"AOI created sucessfully for {caseModel.CaseId}");

        Messenger.Send(new DefineAoiMessage(false));
        Messenger.Send(new SelectedCaseMessage(caseModel));
    }

    /// <summary>
    /// Handles errors in the AOI response. Returns false if errors were found and handled.
    /// </summary>
    private bool HandleAoiResponseErrors(AreaOfInterestResponse? aoiResponse)
    {
        if (string.IsNullOrEmpty(aoiResponse?.Geometry))
        {
            if (aoiResponse?.Messages != null && aoiResponse.Messages.Count > 0)
            {
                feedbackService.Notify("AOI", string.Join("\n", aoiResponse.Messages), NotificationType.Info);
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// Handles the case where the geometry is null. Returns false if handled.
    /// </summary>
    private bool HandleNullGeometry()
    {
        if (tempAoiGeometry == null)
        {
            feedbackService.Notify("AOI", "No features found for the given CRN.", NotificationType.Info);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validates the AOI geometry and handles errors. Returns false if validation failed and was handled.
    /// </summary>
    private async Task<bool> ValidateAndHandleAoiGeometry(Geometry geometry, Geometry? originalGeometry)
    {
        var (isValid, errorMessage) = await aoiValidator.Validate(geometry);

        if (isValid && string.IsNullOrEmpty(errorMessage))
            return true;

        // Highlight invalid geometry
        Messenger.Send(new AoiGeometryHighlightMessage(geometry));

        // Show error notification and dialog
        feedbackService.NotifyError("AOI", $"{appSettings.ErrorMessages.AoiExistMessage}\n({errorMessage})");
        await feedbackService.AlertAsync(
            originalGeometry == null ? "Auto AOI Creation" : "Draw AOI Creation",
            $"{appSettings.ErrorMessages.AoiExistMessage}\n({errorMessage})",
            "Ok"
        );

        tempAoiGeometry = null;

        // Restore previous AOI highlight or remove overlay
        if (AoiGeometrys != null && AoiGeometrys.Count > 0)
        {
            Messenger.Send(new AoiGeometryHighlightMessage(GeometryEngine.Union(AoiGeometrys)));
        }
        else
        {
            var aoiOverlay = mapView?.GraphicsOverlays?.FirstOrDefault(go => go.Id == "aoihighlightOverlay");
            if (aoiOverlay != null)
                mapView!.GraphicsOverlays!.Remove(aoiOverlay);
        }
        return false;
    }

    /// <summary>
    /// Merges the new AOI geometry with any intersecting geometries, or adds it if no intersection.
    /// </summary>
    private void MergeOrAddAoiGeometry(Geometry newGeometry)
    {
        // Find intersecting geometries
        var intersectingGeometries = AoiGeometrys.Where(existing => GeometryEngine.Intersects(existing, newGeometry)).ToList();

        if (intersectingGeometries.Count != 0)
        {
            var geometriesToMerge = new List<Geometry>(intersectingGeometries) { newGeometry };
            var mergedGeometry = GeometryEngine.Union(geometriesToMerge);

            foreach (var geo in intersectingGeometries)
                AoiGeometrys.Remove(geo);

            AoiGeometrys.Add(mergedGeometry);
        }
        else
        {
            AoiGeometrys.Add(newGeometry);
        }
    }

    private void RestartGeometryEditor()
    {
        var editor = this.mapView?.GeometryEditor;

        // The session may have been handed to another tool while the AOI request was in flight.
        if (editor is null || aoiVertexTool is null || !ReferenceEquals(editor.Tool, aoiVertexTool) || !ActiveTool.Equals(AoiTool.DrawAOI))
        {
            return;
        }

        editor.ClearGeometry();
        if (!editor.IsStarted) editor.Start(GeometryType.Polygon);
    }

    private bool IsAoiDrawing()
    {
        var editor = this.mapView?.GeometryEditor;
        return editor is not null && editor.IsStarted && aoiVertexTool is not null && ReferenceEquals(editor.Tool, aoiVertexTool);
    }
    private async Task SaveAreaOfInterest()
    {
        try
        {
            this.overlayService.Show();
            this.loggerService.Info($"{nameof(SaveAreaOfInterest)} Process started");

            if (!HasAoiGeometry())
                return;

            if (!await ValidateAndHandleAoiUnion())
                return;

            var aoiResponse = await areaOfInterestService.CreatAoiVersion();
            var caseModel = await Messenger.Send<SelectedCaseRequestMessage>();

            if (HandleAoiVersionErrors(aoiResponse, caseModel))
                return;

            var mergedGeometry = GeometryEngine.Union(AoiGeometrys);

            if (!await SaveAoiFeature(mergedGeometry, caseModel))
                return;

            UpdateCaseAndNotify(caseModel, mergedGeometry);
        }
        catch (Exception ex)
        {
            this.feedbackService.NotifyError("AOI", "Error in Saving AOI.Please check the application logs for more details");
            this.loggerService.Error(ex, $"Error occurred in {nameof(SaveAreaOfInterest)}.");
        }
        finally
        {
            AoiGeometrys.Clear();
            tempAoiGeometry = null;
            this.loggerService.Info($"{nameof(SaveAreaOfInterest)} Process completed");
            this.overlayService.Hide();
        }
    }
}
