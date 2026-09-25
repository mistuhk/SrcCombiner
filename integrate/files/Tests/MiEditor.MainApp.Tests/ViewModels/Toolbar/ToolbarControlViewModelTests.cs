
using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.StandardTools;
using WG.MiEditor.Messages;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Toolbar;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class ToolbarControlViewModelTests
{
    private readonly StrongReferenceMessenger messenger = new();
    private readonly ToolActivationCoordinator coordinator = new();
    private readonly FakeEditHistoryService editHistory = new();
    private readonly ToolbarControlViewModel sut;

    public ToolbarControlViewModelTests()
    {
        var registry = new ToolDescriptorRegistry(
        [
            new ZoomInToolDescriptor(),
            new ZoomOutToolDescriptor(),
            new InfoToolDescriptor(),
            new RefreshToolDescriptor(),
            new PreviousViewToolDescriptor(),
            new NextViewToolDescriptor(),
            new UndoToolDescriptor(),
            new RedoToolDescriptor()
        ]);

        sut = new ToolbarControlViewModel(messenger, coordinator, registry, editHistory);
    }

    private IconButtonControlViewModel<StandardTool> Button(StandardTool tool) =>
        sut.Tools.First(t => t.Tool == tool);

    // The button command runs the controller, whose dispatch completes synchronously
    // here, so effects are observable immediately after Execute.
    private void Click(StandardTool tool) => Button(tool).SelectCommand.Execute(null);

    [Fact]
    public void BuildsAllStandardToolsInOrder()
    {
        Assert.Equal(
            [
                StandardTool.Plus, StandardTool.Minus, StandardTool.Info,
                StandardTool.Refresh, StandardTool.PreviousView, StandardTool.NextView,
                StandardTool.Undo, StandardTool.Redo
            ],
            sut.Tools.Select(t => t.Tool));
    }

    [Fact]
    public void PreviousAndNextViewStartDisabled()
    {
        Assert.False(Button(StandardTool.PreviousView).IsEnabled);
        Assert.False(Button(StandardTool.NextView).IsEnabled);
        Assert.True(Button(StandardTool.Plus).IsEnabled);
    }

    [Fact]
    public void InfoIsAMode_SticksActive_AndSendsInformationPanel()
    {
        SendInformationPanelMessage? sent = null;
        messenger.Register<SendInformationPanelMessage>(this, (_, m) => sent = m);

        Click(StandardTool.Info);

        Assert.True(Button(StandardTool.Info).IsActive);
        Assert.NotNull(sent);
        Assert.Equal((ITool)StandardTool.Info, sent!.Tool);
    }

    [Fact]
    public void ZoomIsAMode_DoesStick_AndSendsZoom()
    {
        ZoomMapMessage? sent = null;
        messenger.Register<ZoomMapMessage>(this, (_, m) => sent = m);

        Click(StandardTool.Plus);

        Assert.True(Button(StandardTool.Plus).IsActive);
        Assert.NotNull(sent);
        Assert.Equal(ZoomDirection.In, sent!.ZoomDirection);
    }

    [Fact]
    public void ActionWhileInfoActive_LeavesInfoActive()
    {
        Click(StandardTool.Info);

        Click(StandardTool.Refresh);

        Assert.True(Button(StandardTool.Info).IsActive);
    }

    [Fact]
    public void ZoomAction_DoesNotResetMap()
    {
        var reset = false;
        messenger.Register<ResetMapMessage>(this, (_, _) => reset = true);

        Click(StandardTool.Plus);

        Assert.False(reset);
    }

    [Fact]
    public void RefreshAction_SendsRefresh()
    {
        var refreshed = false;
        messenger.Register<RefreshMessage>(this, (_, _) => refreshed = true);

        Click(StandardTool.Refresh);

        Assert.True(refreshed);
        Assert.False(Button(StandardTool.Refresh).IsActive);
    }

    [Fact]
    public void ZoomOut_SendsZoomOut()
    {
        ZoomMapMessage? sent = null;
        messenger.Register<ZoomMapMessage>(this, (_, m) => sent = m);

        Click(StandardTool.Minus);

        Assert.Equal(ZoomDirection.Out, sent!.ZoomDirection);
    }

    [Fact]
    public void NextView_SendsNextNavigation()
    {
        MapViewNavigationToolMessage? nav = null;
        messenger.Register<MapViewNavigationToolMessage>(this, (_, m) => nav = m);

        Click(StandardTool.NextView);

        Assert.Equal(Enums.MapViewType.NextView, nav!.ViewType);
    }

    [Fact]
    public void NavigationButtonsEnabledMessage_DoesNotDeactivateActiveMode()
    {
        Click(StandardTool.Info);

        sut.Receive(new MapViewNavigationButtonsEnabledMessage(true, true));

        Assert.True(Button(StandardTool.Info).IsActive);
    }

    [Fact]
    public void ReclickingInfo_TogglesOff_AndResetsMap()
    {
        var reset = false;
        messenger.Register<ResetMapMessage>(this, (_, _) => reset = true);

        Click(StandardTool.Info);
        Click(StandardTool.Info);

        Assert.False(Button(StandardTool.Info).IsActive);
        Assert.True(reset);
    }

    [Fact]
    public void ActivatingAMode_BridgesStandardToolsInUse()
    {
        var inUse = false;
        messenger.Register<StandardToolsInUseMessage>(this, (_, _) => inUse = true);

        Click(StandardTool.Info);

        Assert.True(inUse);
    }

    [Fact]
    public void PreviousView_DoesNotBridgeStandardToolsInUse_AndSendsNavigation()
    {
        var inUse = false;
        MapViewNavigationToolMessage? nav = null;

        messenger.Register<StandardToolsInUseMessage>(this, (_, _) => inUse = true);
        messenger.Register<MapViewNavigationToolMessage>(this, (_, m) => nav = m);

        Click(StandardTool.PreviousView);

        Assert.False(inUse);
        Assert.NotNull(nav);
        Assert.Equal(Enums.MapViewType.PreviousView, nav!.ViewType);
    }

    [Fact]
    public async Task ExternalSession_DeactivatesActiveMode()
    {
        Click(StandardTool.Info);

        // A tool activating in another toolbar deactivates this controller through the
        // shared coordinator; there is no per toolbar in use message.
        var other = new ToolActivationController<StandardTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask);
        await coordinator.NotifyActivatedAsync(other);

        Assert.False(Button(StandardTool.Info).IsActive);
    }

    [Fact]
    public void NavigationButtonsEnabledMessage_TogglesPrevAndNext()
    {
        sut.Receive(new MapViewNavigationButtonsEnabledMessage(true, false));

        Assert.True(Button(StandardTool.PreviousView).IsEnabled);
        Assert.False(Button(StandardTool.NextView).IsEnabled);
    }

    [Fact]
    public void UndoAndRedoStartDisabled()
    {
        Assert.False(Button(StandardTool.Undo).IsEnabled);
        Assert.False(Button(StandardTool.Redo).IsEnabled);
    }

    [Fact]
    public void Undo_AsksTheEditHistoryToUndo()
    {
        Click(StandardTool.Undo);

        Assert.Equal(1, editHistory.UndoCalls);
        Assert.Equal(0, editHistory.RedoCalls);
    }

    [Fact]
    public void Redo_AsksTheEditHistoryToRedo()
    {
        Click(StandardTool.Redo);

        Assert.Equal(1, editHistory.RedoCalls);
        Assert.Equal(0, editHistory.UndoCalls);
    }

    [Fact]
    public void UndoAndRedo_DoNotBridgeStandardToolsInUse()
    {
        // The same rule the view history buttons follow: using them must not disturb the
        // active Info mode or the AOI control.
        var bridged = false;
        messenger.Register<StandardToolsInUseMessage>(this, (_, _) => bridged = true);

        Click(StandardTool.Undo);
        Click(StandardTool.Redo);

        Assert.False(bridged);
    }

    [Fact]
    public void EditHistoryChangedMessage_DrivesTheUndoAndRedoButtons()
    {
        sut.Receive(new EditHistoryChangedMessage(CanUndo: true, CanRedo: false));

        Assert.True(Button(StandardTool.Undo).IsEnabled);
        Assert.False(Button(StandardTool.Redo).IsEnabled);

        sut.Receive(new EditHistoryChangedMessage(CanUndo: false, CanRedo: true));

        Assert.False(Button(StandardTool.Undo).IsEnabled);
        Assert.True(Button(StandardTool.Redo).IsEnabled);
    }

    [Fact]
    public void EditHistoryChangedMessage_LeavesTheViewHistoryButtonsAlone()
    {
        sut.Receive(new MapViewNavigationButtonsEnabledMessage(IsPrevEnabled: true, IsNextEnabled: true));

        sut.Receive(new EditHistoryChangedMessage(CanUndo: false, CanRedo: false));

        Assert.True(Button(StandardTool.PreviousView).IsEnabled);
        Assert.True(Button(StandardTool.NextView).IsEnabled);
    }

    private sealed class FakeEditHistoryService : IEditHistoryService
    {
        public int UndoCalls { get; private set; }
        public int RedoCalls { get; private set; }
        public int ClearCalls { get; private set; }

        public bool CanUndo { get; set; }
        public bool CanRedo { get; set; }

        public Task UndoAsync() { UndoCalls++; return Task.CompletedTask; }
        public Task RedoAsync() { RedoCalls++; return Task.CompletedTask; }
        public void ClearHistory() => ClearCalls++;
    }
}
