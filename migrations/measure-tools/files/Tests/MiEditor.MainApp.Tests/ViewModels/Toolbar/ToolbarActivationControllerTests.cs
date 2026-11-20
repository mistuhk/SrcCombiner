
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class ToolActivationControllerTests
{
    private readonly RecordingCoordinator coordinator = new();
    private readonly List<EditingTool> activated = [];
    private int deactivateCount;

    private ToolActivationController<EditingTool> CreateController(
        params IconButtonControlViewModel<EditingTool>[] tools) =>
        new(
            coordinator,
            tool => { activated.Add(tool); return Task.CompletedTask; },
            () => { deactivateCount++; return Task.CompletedTask; },
            tools
        );

    private static IconButtonControlViewModel<EditingTool> Mode(EditingTool tool) =>
        new(tool, "icon.png", _ => { }, activation: ToolActivation.Mode);

    private static IconButtonControlViewModel<EditingTool> Action(EditingTool tool) =>
        new(tool, "icon.png", _ => { }, activation: ToolActivation.Action);

    [Fact]
    public async Task ActivatingMode_SetsActiveTool_AndInvokesOnActivate()
    {
        var select = Mode(EditingTool.Select);
        var controller = CreateController(select);

        await controller.HandleSelectedAsync(select);

        Assert.True(select.IsActive);
        Assert.Same(select, controller.ActiveTool);
        Assert.Equal(new[] { EditingTool.Select }, activated);
        Assert.Single(coordinator.Activated);
    }

    [Fact]
    public async Task ActivatingSecondMode_ClearsFirst_AndMovesActiveTool()
    {
        var select = Mode(EditingTool.Select);
        var addPoint = Mode(EditingTool.AddPoint);
        var controller = CreateController(select, addPoint);

        await controller.HandleSelectedAsync(select);
        await controller.HandleSelectedAsync(addPoint);

        Assert.False(select.IsActive);
        Assert.True(addPoint.IsActive);
        Assert.Same(addPoint, controller.ActiveTool);
        Assert.Equal(new[] { EditingTool.Select, EditingTool.AddPoint }, activated);

        // Switching modes within one controller notifies the coordinator per activation
        // and never deactivates this controller's own handler.
        Assert.Equal(2, coordinator.Activated.Count);
        Assert.All(coordinator.Activated, c => Assert.Same(controller, c));
        Assert.Equal(0, deactivateCount);
    }

    [Fact]
    public async Task ActivatingAction_InvokesOnActivate_ButSetsNoActiveState()
    {
        var refresh = Action(EditingTool.DeleteFeature);
        var controller = CreateController(refresh);

        await controller.HandleSelectedAsync(refresh);

        Assert.False(refresh.IsActive);
        Assert.Null(controller.ActiveTool);
        Assert.Equal(new[] { EditingTool.DeleteFeature }, activated);
        Assert.Empty(coordinator.Activated);
        Assert.Equal(0, deactivateCount);
    }

    [Fact]
    public async Task ReclickingActiveMode_TogglesOff()
    {
        var select = Mode(EditingTool.Select);
        var controller = CreateController(select);

        await controller.HandleSelectedAsync(select);
        await controller.HandleSelectedAsync(select);

        Assert.False(select.IsActive);
        Assert.Null(controller.ActiveTool);
        Assert.Equal(1, deactivateCount);
        Assert.Single(coordinator.Deactivated);
    }

    [Fact]
    public async Task ReactivatingAfterToggleOff_ActivatesAgain_AndReNotifiesCoordinator()
    {
        var select = Mode(EditingTool.Select);
        var controller = CreateController(select);

        await controller.HandleSelectedAsync(select);
        await controller.HandleSelectedAsync(select); // toggle off
        await controller.HandleSelectedAsync(select); // reactivate

        Assert.True(select.IsActive);
        Assert.Same(select, controller.ActiveTool);
        Assert.Equal(2, coordinator.Activated.Count);
        Assert.Single(coordinator.Deactivated);
    }

    [Fact]
    public async Task DeactivateExternally_ClearsState_AndInvokesOnDeactivate()
    {
        var select = Mode(EditingTool.Select);
        var controller = CreateController(select);
        await controller.HandleSelectedAsync(select);

        await controller.DeactivateExternallyAsync();

        Assert.False(select.IsActive);
        Assert.Null(controller.ActiveTool);
        Assert.Equal(1, deactivateCount);
    }

    [Fact]
    public async Task DeactivateExternally_WhenNothingActive_DoesNothing()
    {
        var controller = CreateController(Mode(EditingTool.Select));

        await controller.DeactivateExternallyAsync();

        Assert.Equal(0, deactivateCount);
    }

    private sealed class RecordingCoordinator : IToolActivationCoordinator
    {
        public List<IToolActivationController> Activated { get; } = new();
        public List<IToolActivationController> Deactivated { get; } = new();

        public Task NotifyActivatedAsync(IToolActivationController controller)
        {
            Activated.Add(controller);
            return Task.CompletedTask;
        }

        public void NotifyDeactivated(IToolActivationController controller) =>
            Deactivated.Add(controller);

        public Task DeactivateOthersAsync(IToolActivationController requester) => Task.CompletedTask;
    }
}
