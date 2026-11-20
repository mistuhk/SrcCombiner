
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class ToolActivationCoordinatorTests
{
    private readonly ToolActivationCoordinator coordinator = new();

    [Fact]
    public async Task ActivatingSecondController_DeactivatesFirst()
    {
        var first = new RecordingController();
        var second = new RecordingController();

        await coordinator.NotifyActivatedAsync(first);
        await coordinator.NotifyActivatedAsync(second);

        Assert.Equal(1, first.DeactivateCount);
        Assert.Equal(0, second.DeactivateCount);
    }

    [Fact]
    public async Task ActivatingSecondController_ClearsFirstActiveTool_EndToEnd()
    {
        var firstSelect = new IconButtonControlViewModel<EditingTool>(
            EditingTool.Select, "icon.png", _ => { }, activation: ToolActivation.Mode);
        var first = new ToolActivationController<EditingTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask, new[] { firstSelect });

        var secondPlus = new IconButtonControlViewModel<StandardTool>(
            StandardTool.Plus, "icon.png", _ => { }, activation: ToolActivation.Mode);
        var second = new ToolActivationController<StandardTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask, new[] { secondPlus });

        await first.HandleSelectedAsync(firstSelect);
        await second.HandleSelectedAsync(secondPlus);

        Assert.False(firstSelect.IsActive);
        Assert.Null(first.ActiveTool);
        Assert.True(secondPlus.IsActive);
        Assert.Same(secondPlus, second.ActiveTool);
    }

    [Fact]
    public async Task ReactivatingSameController_DoesNotDeactivateIt()
    {
        var controller = new RecordingController();

        await coordinator.NotifyActivatedAsync(controller);
        await coordinator.NotifyActivatedAsync(controller);

        Assert.Equal(0, controller.DeactivateCount);
    }

    [Fact]
    public async Task NotifyDeactivated_ForActiveController_ClearsReference()
    {
        var first = new RecordingController();
        var second = new RecordingController();

        await coordinator.NotifyActivatedAsync(first);
        coordinator.NotifyDeactivated(first);

        // With the reference cleared, activating a second controller deactivates nobody.
        await coordinator.NotifyActivatedAsync(second);

        Assert.Equal(0, first.DeactivateCount);
    }

    [Fact]
    public async Task NotifyDeactivated_ForNonActiveController_LeavesActiveUntouched()
    {
        var first = new RecordingController();
        var stale = new RecordingController();

        await coordinator.NotifyActivatedAsync(first);
        coordinator.NotifyDeactivated(stale);

        // first is still the active holder, so a new activation must deactivate it.
        await coordinator.NotifyActivatedAsync(new RecordingController());

        Assert.Equal(1, first.DeactivateCount);
    }

    [Fact]
    public async Task DeactivateOthers_DeactivatesTheActiveController_AndClearsIt()
    {
        var active = new RecordingController();
        var requester = new RecordingController();
        await coordinator.NotifyActivatedAsync(active);

        await coordinator.DeactivateOthersAsync(requester);

        Assert.Equal(1, active.DeactivateCount);

        // Nothing is active any more, so a later activation deactivates nobody.
        await coordinator.NotifyActivatedAsync(new RecordingController());
        Assert.Equal(1, active.DeactivateCount);
    }

    [Fact]
    public async Task DeactivateOthers_LeavesTheRequesterActive()
    {
        var requester = new RecordingController();
        await coordinator.NotifyActivatedAsync(requester);

        await coordinator.DeactivateOthersAsync(requester);

        Assert.Equal(0, requester.DeactivateCount);
    }

    [Fact]
    public async Task DeactivateOthers_WhenNothingActive_IsANoOp()
    {
        var thrown = await Record.ExceptionAsync(() => coordinator.DeactivateOthersAsync(new RecordingController()));

        Assert.Null(thrown);
    }

    private sealed class RecordingController : IToolActivationController
    {
        public int DeactivateCount { get; private set; }

        public Task DeactivateExternallyAsync()
        {
            DeactivateCount++;
            return Task.CompletedTask;
        }
    }
}
