
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Core.Services.Drawing.Measurement.Manager;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Measurement;

// Registered as a singleton in AddToolbars (it needs the coordinator and registry), so
// no [RegisterService] attribute here, which would double-register it. The measurement
// tools live behind an anchor button that opens a flyout; the anchor's icon is the active
// child's icon, or the default measure icon when none is active.
public partial class MeasurementToolGroupViewModel : ObservableRecipient
{
    private const string ErrorTitle = "Measurement";

    private readonly IMeasureOperationManager drawingManager;
    private readonly ToolActivationController<MeasureTool> controller;
    private readonly IconButtonControlViewModel<MeasureTool> defaultTool;
    private IconButtonControlViewModel<MeasureTool> currentFace;

    public MeasurementToolGroupViewModel(
        IMessenger messenger,
        IToolActivationCoordinator coordinator,
        IToolDescriptorRegistry registry,
        IMeasureOperationManager drawingManager,
        IMeasurementErrorReporter errorReporter) : base(messenger)
    {
        this.drawingManager = drawingManager;

        // A failed activation is rolled back by the controller (the button does not stay lit)
        // and reported here, rather than vanishing in the fire-and-forget button command.
        controller = new ToolActivationController<MeasureTool>(
            coordinator,
            OnActivate,
            OnDeactivate,
            onError: ex => errorReporter.Report(ErrorTitle, ex));
        BuildTools(registry);

        // The anchor's click toggles the flyout (this is the reliable open path for touch,
        // where the anchor ImageButton consumes the tap that the group gesture would want).
        defaultTool = new IconButtonControlViewModel<MeasureTool>(default, "measure.png", _ => ToggleFlyout());
        currentFace = defaultTool;
        controller.PropertyChanged += OnControllerPropertyChanged;

        IsActive = true;
    }

    public ObservableCollection<IconButtonControlViewModel<MeasureTool>> FlyoutTools => controller.Tools;

    [ObservableProperty]
    public partial bool IsFlyoutVisible { get; set; }

    // The anchor's icon: a stand-in that mirrors the active child's icon and highlight but
    // whose click toggles the flyout rather than the tool. When nothing is active it is the
    // default measure icon. It is a separate object from the flyout child so the anchor and
    // the child can carry different click behaviour.
    public IconButtonControlViewModel<MeasureTool> CurrentTool => currentFace;

    public string CurrentIcon => currentFace.Icon;

    private void BuildTools(IToolDescriptorRegistry registry)
    {
        foreach (var descriptor in registry.GetTools(ToolbarId.Measure))
        {
            controller.Tools.Add(new IconButtonControlViewModel<MeasureTool>(
                (MeasureTool)descriptor.Tool,
                descriptor.Icon,
                controller.HandleSelected,
                activation: descriptor.Activation)
            {
                ToolTip = descriptor.Tooltip
            });
        }
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolActivationController<MeasureTool>.ActiveTool))
        {
            currentFace = controller.ActiveTool is { } active
                ? new IconButtonControlViewModel<MeasureTool>(active.Tool, active.Icon, _ => ToggleFlyout())
                {
                    IsActive = true,
                    ToolTip = active.ToolTip
                }
                : defaultTool;

            OnPropertyChanged(nameof(CurrentTool));
            OnPropertyChanged(nameof(CurrentIcon));

            // Selecting a child (or being deactivated) closes the flyout; the two-way
            // IsOpen binding on FlyoutBehavior carries this to the view, which matters on
            // touch where there is no pointer exit to close it.
            IsFlyoutVisible = false;
        }
    }

    private void ToggleFlyout() => IsFlyoutVisible = !IsFlyoutVisible;

    // A measure tool is a sticky mode. Cross-toolbar deactivation runs through the coordinator,
    // the MeasurementInProgress broadcast is kept only for non-toolbar consumers (the map
    // control and the information panel). The measurement drawing itself runs on the operation manager.
    private async Task OnActivate(MeasureTool tool)
    {
        Messenger.Send<MeasurementInProgressMessage>();
        await drawingManager.Activate(tool);
    }

    private Task OnDeactivate() => drawingManager.Deactivate();
}
