
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.Messages;
using WG.MiEditor.Messages.Aoi;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Aoi;

// Registered as a singleton in AddToolbars (it needs the coordinator and registry), so
// no [RegisterService] attribute here, which would double-register it.
public partial class AoiToolbarControlViewModel : ObservableRecipient,
    IRecipient<ResetAoiToolbarActiveStateMessage>,
    IRecipient<DefineAoiMessage>
{
    private readonly ToolActivationController<AoiTool> controller;
    private readonly IToolActivationCoordinator coordinator;

    public AoiToolbarControlViewModel(
        IMessenger messenger,
        IToolActivationCoordinator coordinator,
        IToolDescriptorRegistry registry) : base(messenger)
    {
        this.coordinator = coordinator;
        controller = new ToolActivationController<AoiTool>(coordinator, OnActivate, OnDeactivate);
        BuildTools(registry);

        IsActive = true;
    }

    public ObservableCollection<IconButtonControlViewModel<AoiTool>> Tools => controller.Tools;

    [ObservableProperty]
    public partial bool ShowAoiToolbar { get; set; }

    private void BuildTools(IToolDescriptorRegistry registry)
    {
        foreach (var descriptor in registry.GetTools(ToolbarId.Aoi))
        {
            controller.Tools.Add(new IconButtonControlViewModel<AoiTool>(
                (AoiTool)descriptor.Tool,
                descriptor.Icon,
                controller.HandleSelected,
                activation: descriptor.Activation)
            {
                ToolTip = descriptor.Tooltip
            });
        }
    }

    // The AOI toolbar's dispatch. Auto AOI and Draw AOI are sticky modes; delete, save,
    // and clear are momentary actions. Every selection also refreshes the information
    // panel. Cross-toolbar deactivation now runs through the coordinator.
    private async Task OnActivate(AoiTool tool)
    {
        if (tool == AoiTool.AutoAOI)
        {
            Messenger.Send(new DefineAutoAoiMessage());
        }
        else if (tool == AoiTool.DrawAOI)
        {
            Messenger.Send(new DefineManulAoiMessage());
        }
        else if (tool == AoiTool.Save)
        {
            Messenger.Send(new SaveAoiMessage());
        }
        else if (tool == AoiTool.Delete)
        {
            // Delete is a momentary button but it arms a map-tap handler, so another toolbar's
            // mode (a measurement, an edit) must end first or the same tap would drive both.
            await coordinator.DeactivateOthersAsync(controller);
            Messenger.Send(new DeleteManualAoiMessage());
        }
        else if (tool == AoiTool.Clear)
        {
            Messenger.Send(new ClearGraphicsOverlayMessage());
        }

        Messenger.Send(new SendInformationPanelMessage(tool));
    }

    // Turning an AOI mode off (re-click, or another toolbar taking over through the coordinator)
    // must actually release the map: stop the AOI drawing session and detach its tap handlers.
    private Task OnDeactivate()
    {
        Messenger.Send(new StopAoiInteractionMessage());
        return Task.CompletedTask;
    }

    public void Receive(DefineAoiMessage message) => ShowAoiToolbar = message.IsEnable;

    public void Receive(ResetAoiToolbarActiveStateMessage message) => _ = controller.DeactivateExternallyAsync();
}
