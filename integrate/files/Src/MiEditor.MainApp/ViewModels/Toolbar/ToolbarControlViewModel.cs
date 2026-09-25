
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using WG.MiEditor.Enums;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.Messages;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Messages.Toolbar;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar;

// Registered as a singleton in AddToolbars (it needs the coordinator and registry), so
// no [RegisterService] attribute here, which would double-register it.
public partial class ToolbarControlViewModel : ObservableRecipient,
    IRecipient<MapViewNavigationButtonsEnabledMessage>,
    IRecipient<EditHistoryChangedMessage>
{
    private readonly ToolActivationController<StandardTool> controller;
    private readonly IEditHistoryService editHistory;

    public ToolbarControlViewModel(
        IMessenger messenger,
        IToolActivationCoordinator coordinator,
        IToolDescriptorRegistry registry,
        IEditHistoryService editHistory) : base(messenger)
    {
        this.editHistory = editHistory;
        controller = new ToolActivationController<StandardTool>(coordinator, OnActivate, OnDeactivate);
        BuildTools(registry);

        IsActive = true;
    }

    [ObservableProperty]
    private bool isVisible = true;

    public ObservableCollection<IconButtonControlViewModel<StandardTool>> Tools => controller.Tools;

    private void BuildTools(IToolDescriptorRegistry registry)
    {
        foreach (var descriptor in registry.GetTools(ToolbarId.Standard))
        {
            var tool = (StandardTool)descriptor.Tool;
            // The view history and the edit history both start empty, so their buttons start
            // disabled and are enabled by the message each history sends.
            var startsEnabled =
                tool != StandardTool.PreviousView
                && tool != StandardTool.NextView
                && tool != StandardTool.Undo
                && tool != StandardTool.Redo;

            controller.Tools.Add(new IconButtonControlViewModel<StandardTool>(
                tool,
                descriptor.Icon,
                controller.HandleSelected,
                isEnabled: startsEnabled,
                activation: descriptor.Activation)
            {
                ToolTip = descriptor.Tooltip
            });
        }
    }

    // The standard toolbar's dispatch. Zoom in, zoom out, and Info are sticky modes,
    // refresh and the view-history (previous and next) tools are momentary actions.
    // Cross-toolbar deactivation now runs through the coordinator, the StandardToolsInUseMessage
    // broadcast is kept only for non-toolbar consumers (the information panel and the AOI control)
    // that react to a standard tool being used.
    private async Task OnActivate(StandardTool tool)
    {
        if (tool != StandardTool.PreviousView
            && tool != StandardTool.NextView
            && tool != StandardTool.Undo
            && tool != StandardTool.Redo)
        {
            Messenger.Send<StandardToolsInUseMessage>();
        }

        if (tool == StandardTool.Plus)
        {
            Messenger.Send(new ZoomMapMessage(ZoomDirection.In));
        }
        else if (tool == StandardTool.Minus)
        {
            Messenger.Send(new ZoomMapMessage(ZoomDirection.Out));
        }
        else if (tool == StandardTool.Info)
        {
            Messenger.Send(new SendInformationPanelMessage(tool));
        }
        else if (tool == StandardTool.Refresh)
        {
            Messenger.Send<RefreshMessage>();
        }
        else if (tool == StandardTool.PreviousView)
        {
            Messenger.Send(new MapViewNavigationToolMessage(MapViewType.PreviousView));
        }
        else if (tool == StandardTool.NextView)
        {
            Messenger.Send(new MapViewNavigationToolMessage(MapViewType.NextView));
        }
        else if (tool == StandardTool.Undo)
        {
            await editHistory.UndoAsync();
        }
        else if (tool == StandardTool.Redo)
        {
            await editHistory.RedoAsync();
        }
    }

    private Task OnDeactivate()
    {
        Messenger.Send<ResetMapMessage>();
        Messenger.Send(new SketchingCompletedMessage<StandardTool>(default));

        return Task.CompletedTask;
    }

    public void Receive(MapViewNavigationButtonsEnabledMessage message)
    {
        // Sent on every map navigation completion, so this only updates enablement; it
        // must not deactivate the active Info mode, which persists across map movement.
        SetEnabled(StandardTool.PreviousView, message.IsPrevEnabled);
        SetEnabled(StandardTool.NextView, message.IsNextEnabled);
    }

    public void Receive(EditHistoryChangedMessage message)
    {
        SetEnabled(StandardTool.Undo, message.CanUndo);
        SetEnabled(StandardTool.Redo, message.CanRedo);
    }

    private void SetEnabled(StandardTool tool, bool isEnabled)
    {
        var button = Tools.FirstOrDefault(t => t.Tool == tool);
        if (button is not null)
        {
            button.IsEnabled = isEnabled;
        }
    }
}
