
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services;
using WG.MiEditor.Core.Services.Drawing.Editing;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Applying;
using WG.MiEditor.Core.Services.Drawing.Editing.Managers;
using WG.MiEditor.Core.Services.Drawing.Editing.MergeFeature.Operation;
using WG.MiEditor.Core.Services.FeatureGuide;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Messages.Toolbar;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.FeatureGuide;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Messages.Settings;
using WG.MiEditor.Shared.Messages.ShortCuts;
using WG.MiEditor.Shared.Messages.Toolbar;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar;

// Registered as a singleton in AddToolbars (it needs the coordinator and registry), so
// no [RegisterService] attribute here, which would double-register it.
public partial class EditingLayerToolbarViewModel : ObservableRecipient,
    IRecipient<EditFeatureCompletedMessage>,
    IRecipient<EditableLayerSelectedMessage>,
    IRecipient<SelectedCaseStartEditingMessage>,
    IRecipient<SelectedCaseStopEditingMessage>,
    IRecipient<ShortCutsToolActivateMessage>,
    IRecipient<SelectedEditableLayerMessage>,
    IRecipient<EditHistoryChangedMessage>,
    IRecipient<ResetToolbarState>
{
    private readonly IEditHistorySwitch historySwitch;
    private readonly IEditingOperationsManager editingOperationsManager;
    private readonly IFeatureLayerService featureLayerService;
    private readonly IMergeFeatureService mergeFeatureService;
    private readonly IEditingUtility editingUtility;

    private readonly IFeatureGuideService featureGuideService;
    private readonly IMapDataService mapDataService;
    private readonly IToolDescriptorRegistry registry;
    private readonly IEditHistoryService editHistory;

    private readonly ApplicationSettings applicationSettings;
    private readonly ToolActivationController<EditingTool> controller;

    private string layerName = string.Empty;
    private bool isEditModeActive;
    private bool isCanopyPoint;

    [ObservableProperty]
    private bool isVisible;

    public EditingLayerToolbarViewModel(
        IMessenger messenger,
        IMergeFeatureService mergeFeatureService,
        IEditingOperationsManager editingOperationsManager,
        IFeatureLayerService featureLayerService,
        IEditingUtility editingUtility,
        IFeatureGuideService featureGuideService,
        IMapDataService mapDataService,
        IToolActivationCoordinator coordinator,
        IToolDescriptorRegistry registry,
        IEditHistoryService editHistory,
        IEditHistorySwitch historySwitch,
        ApplicationSettings applicationSettings)
        : base(messenger)
    {
        this.mergeFeatureService = mergeFeatureService;
        this.editingOperationsManager = editingOperationsManager;
        this.featureLayerService = featureLayerService;
        this.editingUtility = editingUtility;

        this.featureGuideService = featureGuideService;
        this.mapDataService = mapDataService;
        this.registry = registry;
        this.editHistory = editHistory;
        this.applicationSettings = applicationSettings;

        this.historySwitch = historySwitch;

        controller = new ToolActivationController<EditingTool>(coordinator, OnActivate, OnDeactivate);
        BuildTools(registry);

        // The setting can be turned off mid session, so the buttons follow it rather than being
        // decided once at startup.
        historySwitch.Changed += (_, _) => ApplyHistoryVisibility();

        IsVisible = false;
        IsActive = true;
    }

    public System.Collections.ObjectModel.ObservableCollection<IconButtonControlViewModel<EditingTool>> Tools => controller.Tools;

    private void BuildTools(IToolDescriptorRegistry registry)
    {
        foreach (var descriptor in registry.GetTools(ToolbarId.Editing))
        {
            var tool = (EditingTool)descriptor.Tool;
            var isHistoryTool = tool == EditingTool.Undo || tool == EditingTool.Redo;
            var startsEnabled = !isHistoryTool;

            controller.Tools.Add(new IconButtonControlViewModel<EditingTool>(
                (EditingTool)descriptor.Tool,
                descriptor.Icon,
                controller.HandleSelected,
                isEnabled: startsEnabled,
                isVisible: !isHistoryTool || historySwitch.IsEnabled,
                activation: descriptor.Activation)
            {
                ToolTip = descriptor.Tooltip
            });
        }
    }

    // The editing toolbar's dispatch. Most tools are sticky modes the controller tracks; Clear
    // Selection and Delete Feature are momentary actions, handled as early returns below.
    private async Task OnActivate(EditingTool tool)
    {
        if (tool == EditingTool.MergePolygon && isCanopyPoint)
        {
            return;
        }

        // Clear Selection is a momentary action that must NOT disturb the active drawing mode,
        // so it skips DeactivateAll and does not change the active tool.
        if (tool == EditingTool.DeSelect)
        {
            await HandleDeselectAsync();
            return;
        }

        // Delete Feature is a momentary action that resets the current tool. The controller does not
        // clear the active mode for an action, so clear it here (highlight and feature guide,
        // via the controller) after deactivating the backend, then perform the delete.
        if (tool == EditingTool.DeleteFeature)
        {
            await HandleDeleteFeatureAsync(tool);
            return;
        }

        // Sticky modes: switch to this tool. The broadcast and DeactivateAll deactivate the
        // previously active mode's backend before this one activates.
        Messenger.Send<EditingSessionInProgressMessage>();
        await editingOperationsManager.DeactivateAll(tool);

        if (tool == EditingTool.MergePolygon)
        {
            await WithMapLoadingAsync(mergeFeatureService.Merge);
            return;
        }

        if (IsSketchTool(tool))
        {
            await editingOperationsManager.ActivateTool(tool, string.Empty);
            return;
        }

        if (RequiresTargetLayer(tool))
        {
            await ActivateTargetLayerToolAsync(tool);
            return;
        }

        if (tool == EditingTool.Buffer)
        {
            await HandleBufferAsync(tool);
            return;
        }

        if (tool == EditingTool.Undo)
        {
            await editHistory.UndoAsync();
            return;
        }
        
        if (tool == EditingTool.Redo)
        {
            await editHistory.RedoAsync();
            return;
        }

        await ActivateStandardToolAsync(tool);
    }

    private async Task OnDeactivate()
    {
        Messenger.Send<CancelFeatureGuideMessage>();
        await editingOperationsManager.DeactivateAll(new());
    }

    private async Task<string> RequestTargetLayerAsync() => await Messenger.Send<SelectedEditableLayerRequestMessage>();

    private async Task WithMapLoadingAsync(Func<Task> operation)
    {
        Messenger.Send(new MapLoadingMessage(true));
        await operation();
        Messenger.Send(new MapLoadingMessage(false));
    }

    private async Task ClearSelectionsAsync()
    {
        var map = await mapDataService.GetMapAsync();
        var layers = map?.OperationalLayers;

        if (layers != null)
        {
            featureLayerService.ClearAllSelections(layers);
        }
    }

    public void Receive(EditFeatureCompletedMessage message)
    {
        Messenger.Send<FeatureGuideNextStepMessage>();
        Messenger.Send(new SketchingCompletedMessage<EditingTool>(new()));
        Messenger.Send<ClearEditingGraphicMessage>();
    }

    public void Receive(EditableLayerSelectedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.LayerName))
        {
            return;
        }

        Messenger.Send<CancelFeatureGuideMessage>();
        layerName = message.LayerName;
        _ = RenderToolbar(layerName);
    }

    public void Receive(SelectedCaseStartEditingMessage message)
    {
        isEditModeActive = true;
        _ = StartEditingAsync(message);
    }

    public void Receive(SelectedCaseStopEditingMessage message)
    {
        isEditModeActive = false;
        Messenger.Send<CancelFeatureGuideMessage>();
        layerName = string.Empty;
        _ = RenderToolbar(layerName);
    }

    public void Receive(SelectedEditableLayerMessage message)
    {
        isCanopyPoint = message.LayerName == applicationSettings.Layers.Canopy_Point;
    }

    public void Receive(ShortCutsToolActivateMessage message)
    {
        if (!isEditModeActive)
        {
            return;
        }

        _ = ActivateFromShortcutAsync(message.EditingTool);
    }

    /// <summary>
    /// Stands the active tool down for an undo or a redo, and hands back the task so the caller can
    /// wait for it. Both halves are asynchronous, and the caller is about to change the data the tool
    /// may be holding, so it needs to know when they have finished rather than only that it asked.
    /// </summary>
    public void Receive(ResetToolbarState message) => message.Reply(StandDownAsync());

    private async Task<bool> StandDownAsync()
    {
        await editingOperationsManager.DeactivateAll(null);
        await controller.DeactivateExternallyAsync();

        return true;
    }

    public void Receive(EditHistoryChangedMessage message)
    {
        SetEnabled(EditingTool.Undo, message.CanUndo);
        SetEnabled(EditingTool.Redo, message.CanRedo);
    }

    /// <summary>
    /// Undo and redo are shown only while the history is in use. Hidden rather than disabled, so a
    /// deployment or a user that has withdrawn the feature does not leave two dead buttons behind.
    /// </summary>
    private void ApplyHistoryVisibility()
    {
        var isVisible = historySwitch.IsEnabled;

        SetVisible(EditingTool.Undo, isVisible);
        SetVisible(EditingTool.Redo, isVisible);
    }

    private void SetVisible(EditingTool tool, bool isVisible)
    {
        var button = Tools.FirstOrDefault(t => t.Tool == tool);
        if (button is not null)
        {
            button.IsVisible = isVisible;
        }
    }

    private void SetEnabled(EditingTool tool, bool isEnabled)
    {
        var button = Tools.FirstOrDefault(t => t.Tool == tool);
        if (button is not null)
        {
            button.IsEnabled = isEnabled;
        }
    }

    private async Task StartEditingAsync(SelectedCaseStartEditingMessage message)
    {
        layerName = string.IsNullOrWhiteSpace(message.Status)
            ? string.Empty
            : await Messenger.Send<SelectedEditableLayerRequestMessage>();

        await RenderToolbar(layerName);
    }

    // A shortcut activates its tool: deactivate the current one first so the controller
    // activates the requested tool rather than toggling it off.
    private async Task ActivateFromShortcutAsync(EditingTool tool)
    {
        await controller.DeactivateExternallyAsync();
        await controller.HandleSelectedAsync(tool);
    }

    private async Task RenderToolbar(string layer)
    {
        await controller.DeactivateExternallyAsync();

        var hasLayer = !string.IsNullOrWhiteSpace(layer);
        IsVisible = hasLayer;
        Messenger.Send(new DrawingStateChangedMessage(IsVisible));

        if (!hasLayer)
        {
            return;
        }

        var geometryType = await editingUtility.GetLayerGeometryTypeAsync(layer);
        var scope = ToGeometryScope(geometryType);

        var visible = registry.GetTools(ToolbarId.Editing, scope)
            .Select(descriptor => (EditingTool)descriptor.Tool)
            .ToHashSet();

        foreach (var toolVm in Tools)
        {
            toolVm.IsVisible = visible.Contains(toolVm.Tool);

            if (toolVm.Tool == EditingTool.AddTechnicalAssessment)
            {
                toolVm.IsEnabled = layer.Equals(applicationSettings.Layers.Permanent_Deduction);
            }
        }
    }

    private static GeometryScopes ToGeometryScope(GeometryType geometryType) => geometryType switch
    {
        GeometryType.Point => GeometryScopes.Point,
        GeometryType.Polyline => GeometryScopes.Polyline,
        GeometryType.Polygon => GeometryScopes.Polygon,
        _ => GeometryScopes.None,
    };

    private async Task HandleDeselectAsync()
    {
        Messenger.Send<EditingSessionInProgressMessage>();
        await ClearSelectionsAsync();
    }

    private async Task HandleDeleteFeatureAsync(EditingTool tool)
    {
        Messenger.Send<EditingSessionInProgressMessage>();

        await editingOperationsManager.DeactivateAll(tool);
        await controller.DeactivateExternallyAsync();

        await WithMapLoadingAsync(async () =>
        {
            var target = await RequestTargetLayerAsync();

            if (!string.IsNullOrWhiteSpace(target))
            {
                await editingOperationsManager.ActivateTool(tool, target);
            }
        });
    }

    private static bool IsSketchTool(EditingTool tool) =>
    tool == EditingTool.ModifySketchAttribute ||
    tool == EditingTool.DeleteSketch;

    private static bool RequiresTargetLayer(EditingTool tool) =>
           tool == EditingTool.ModifyAttribute
        || tool == EditingTool.ImportFeatureToEditableLayer
        || tool == EditingTool.ImportFeatureToSketchLayer
        || tool == EditingTool.ImportSketchToEditableLayer;

    private async Task ActivateTargetLayerToolAsync(EditingTool tool)
    {
        var target = await RequestTargetLayerAsync();

        if (!string.IsNullOrWhiteSpace(target))
        {
            await editingOperationsManager.ActivateTool(tool, target);
        }
    }

    private async Task HandleBufferAsync(EditingTool tool)
    {
        var target = await RequestTargetLayerAsync();

        await editingOperationsManager.ActivateTool(tool, target);
        featureGuideService.StartGuide(tool);
    }

    private async Task ActivateStandardToolAsync(EditingTool tool)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return;
        }

        await editingOperationsManager.ActivateTool(tool, layerName);
        featureGuideService.StartGuide(tool);
    }
}
