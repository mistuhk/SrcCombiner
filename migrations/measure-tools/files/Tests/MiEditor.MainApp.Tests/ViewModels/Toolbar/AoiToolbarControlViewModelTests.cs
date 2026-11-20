
using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.MainApp.ViewModels.Aoi;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.AoiTools;
using WG.MiEditor.Messages;
using WG.MiEditor.Messages.Aoi;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class AoiToolbarControlViewModelTests
{
    private readonly StrongReferenceMessenger messenger = new();
    private readonly ToolActivationCoordinator coordinator = new();
    private readonly AoiToolbarControlViewModel sut;

    public AoiToolbarControlViewModelTests()
    {
        var registry = new ToolDescriptorRegistry(
        [
            new AutoAoiToolDescriptor(),
            new DrawAoiToolDescriptor(),
            new DeleteAoiToolDescriptor(),
            new SaveAoiToolDescriptor(),
            new ClearAoiToolDescriptor()
        ]);

        sut = new AoiToolbarControlViewModel(messenger, coordinator, registry);
    }

    private IconButtonControlViewModel<AoiTool> Button(AoiTool tool) => sut.Tools.First(t => t.Tool == tool);

    // The button command runs the controller, whose dispatch completes synchronously
    // here, so effects are observable immediately after Execute.
    private void Click(AoiTool tool) => Button(tool).SelectCommand.Execute(null);

    [Fact]
    public void BuildsAoiToolsInOrder()
    {
        Assert.Equal(
            [AoiTool.AutoAOI, AoiTool.DrawAOI, AoiTool.Delete, AoiTool.Save, AoiTool.Clear],
            sut.Tools.Select(t => t.Tool));
    }

    [Fact]
    public void AutoAoiIsAMode_SticksActive_AndSendsDefineAutoAoi()
    {
        var sent = false;
        messenger.Register<DefineAutoAoiMessage>(this, (_, _) => sent = true);

        Click(AoiTool.AutoAOI);

        Assert.True(Button(AoiTool.AutoAOI).IsActive);
        Assert.True(sent);
    }

    [Fact]
    public void SelectingDrawAoi_DeactivatesAutoAoi()
    {
        Click(AoiTool.AutoAOI);

        Click(AoiTool.DrawAOI);

        Assert.False(Button(AoiTool.AutoAOI).IsActive);
        Assert.True(Button(AoiTool.DrawAOI).IsActive);
    }

    [Fact]
    public void ReclickingAutoAoi_TogglesOff()
    {
        Click(AoiTool.AutoAOI);

        Click(AoiTool.AutoAOI);

        Assert.False(Button(AoiTool.AutoAOI).IsActive);
    }

    [Fact]
    public void SaveIsAnAction_DoesNotStick_AndSendsSave()
    {
        var saved = false;
        messenger.Register<SaveAoiMessage>(this, (_, _) => saved = true);

        Click(AoiTool.Save);

        Assert.False(Button(AoiTool.Save).IsActive);
        Assert.True(saved);
    }

    [Fact]
    public void ActionWhileModeActive_LeavesModeActive()
    {
        Click(AoiTool.AutoAOI);

        Click(AoiTool.Save);

        Assert.True(Button(AoiTool.AutoAOI).IsActive);
    }

    [Fact]
    public void EachSelection_SendsInformationPanel()
    {
        SendInformationPanelMessage? sent = null;
        messenger.Register<SendInformationPanelMessage>(this, (_, m) => sent = m);

        Click(AoiTool.DrawAOI);

        Assert.NotNull(sent);
        Assert.Equal(AoiTool.DrawAOI, sent!.Tool);
    }

    [Fact]
    public void DefineAoiMessage_TogglesToolbarVisibility()
    {
        sut.Receive(new DefineAoiMessage(true));
        Assert.True(sut.ShowAoiToolbar);

        sut.Receive(new DefineAoiMessage(false));
        Assert.False(sut.ShowAoiToolbar);
    }

    [Fact]
    public void ActivatingAoiMode_DeactivatesAnotherControllerOnSameCoordinator()
    {
        var otherButton = new IconButtonControlViewModel<AoiTool>(AoiTool.DrawAOI, "x.png", _ => { });
        var other = new ToolActivationController<AoiTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask, [otherButton]);

        other.HandleSelected(AoiTool.DrawAOI);
        Assert.True(otherButton.IsActive);

        Click(AoiTool.AutoAOI);

        Assert.False(otherButton.IsActive);
    }

    [Fact]
    public void TogglingOffAnAoiMode_TellsAoiToStopInteracting()
    {
        var stopped = 0;
        messenger.Register<StopAoiInteractionMessage>(this, (_, _) => stopped++);

        Click(AoiTool.DrawAOI);
        Click(AoiTool.DrawAOI);

        Assert.Equal(1, stopped);
    }

    [Fact]
    public void AnotherToolbarTakingOver_TellsAoiToStopInteracting()
    {
        var stopped = 0;
        messenger.Register<StopAoiInteractionMessage>(this, (_, _) => stopped++);
        Click(AoiTool.DrawAOI);

        var otherButton = new IconButtonControlViewModel<MeasureTool>(MeasureTool.Area, "x.png", _ => { });
        var other = new ToolActivationController<MeasureTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask, [otherButton]);
        other.HandleSelected(MeasureTool.Area);

        Assert.False(Button(AoiTool.DrawAOI).IsActive);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Delete_EndsAnotherToolbarsActiveMode_BeforeArmingTheMapTap()
    {
        var order = new List<string>();
        var otherButton = new IconButtonControlViewModel<MeasureTool>(MeasureTool.Area, "x.png", _ => { });
        var other = new ToolActivationController<MeasureTool>(
            coordinator,
            _ => Task.CompletedTask,
            () => { order.Add("measure stopped"); return Task.CompletedTask; },
            [otherButton]);
        other.HandleSelected(MeasureTool.Area);
        messenger.Register<DeleteManualAoiMessage>(this, (_, _) => order.Add("delete armed"));

        Click(AoiTool.Delete);

        Assert.False(otherButton.IsActive);
        Assert.Equal(["measure stopped", "delete armed"], order);
    }

    [Fact]
    public void Delete_LeavesTheAoiToolbarsOwnModeActive()
    {
        Click(AoiTool.AutoAOI);

        Click(AoiTool.Delete);

        Assert.True(Button(AoiTool.AutoAOI).IsActive);
    }
}
