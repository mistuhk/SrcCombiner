using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class ToolActivationControllerErrorHandlingTests
{
    private readonly ToolActivationCoordinator coordinator = new();
    private readonly List<Exception> errors = [];

    private Exception? activateFailure;
    private Exception? deactivateFailure;
    private int deactivateCount;

    private static IconButtonControlViewModel<MeasureTool> Mode(MeasureTool tool) =>
        new(tool, "icon.png", _ => { }, activation: ToolActivation.Mode);

    private static IconButtonControlViewModel<MeasureTool> Action(MeasureTool tool) =>
        new(tool, "icon.png", _ => { }, activation: ToolActivation.Action);

    private ToolActivationController<MeasureTool> Create(
        IEnumerable<IconButtonControlViewModel<MeasureTool>> buttons,
        bool withErrorSink = true) =>
        new(
            coordinator,
            _ => activateFailure is null ? Task.CompletedTask : Task.FromException(activateFailure),
            () =>
            {
                deactivateCount++;
                return deactivateFailure is null ? Task.CompletedTask : Task.FromException(deactivateFailure);
            },
            buttons,
            withErrorSink ? errors.Add : null);

    [Fact]
    public async Task ActivationFailure_RollsBackActiveState_AndReports()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button]);
        activateFailure = new InvalidOperationException("boom");

        await sut.HandleSelectedAsync(button);

        Assert.False(button.IsActive);
        Assert.Null(sut.ActiveTool);
        Assert.Same(activateFailure, Assert.Single(errors));
        Assert.Equal(1, deactivateCount);
    }

    [Fact]
    public async Task ActivationFailure_ReleasesTheCoordinator()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button]);
        activateFailure = new InvalidOperationException("boom");
        await sut.HandleSelectedAsync(button);
        deactivateCount = 0;

        // Another toolbar activating must not try to deactivate the rolled-back controller.
        var other = new RecordingController();
        await coordinator.NotifyActivatedAsync(other);

        Assert.Equal(0, deactivateCount);
    }

    [Fact]
    public async Task ActivationFailure_WithoutErrorSink_StillRollsBack_AndRethrows()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button], withErrorSink: false);
        activateFailure = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.HandleSelectedAsync(button));

        Assert.False(button.IsActive);
        Assert.Null(sut.ActiveTool);
    }

    [Fact]
    public void HandleSelected_WithoutErrorSink_DoesNotLeakAnUnobservedException()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button], withErrorSink: false);
        activateFailure = new InvalidOperationException("boom");

        var thrown = Record.Exception(() => sut.HandleSelected(MeasureTool.Distance));

        Assert.Null(thrown);
        Assert.False(button.IsActive);
    }

    [Fact]
    public async Task ActivationAndCleanupBothFail_BothAreReported()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button]);
        activateFailure = new InvalidOperationException("activate");
        deactivateFailure = new InvalidOperationException("cleanup");

        await sut.HandleSelectedAsync(button);

        Assert.Equal(2, errors.Count);
        Assert.False(button.IsActive);
    }

    [Fact]
    public async Task ActionFailure_IsReported_AndNeverSetsActiveState()
    {
        var button = Action(MeasureTool.Distance);
        var sut = Create([button]);
        activateFailure = new InvalidOperationException("boom");

        await sut.HandleSelectedAsync(button);

        Assert.False(button.IsActive);
        Assert.Single(errors);
    }

    [Fact]
    public async Task ToggleOffFailure_IsReported_AndTheToolIsStillOff()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button]);
        await sut.HandleSelectedAsync(button);
        deactivateFailure = new InvalidOperationException("boom");

        await sut.HandleSelectedAsync(button);

        Assert.False(button.IsActive);
        Assert.Null(sut.ActiveTool);
        Assert.Single(errors);
    }

    [Fact]
    public async Task ExternalDeactivationFailure_IsReported_AndDoesNotBreakTheOtherToolbar()
    {
        var button = Mode(MeasureTool.Distance);
        var sut = Create([button]);
        await sut.HandleSelectedAsync(button);
        deactivateFailure = new InvalidOperationException("boom");

        var other = new RecordingController();
        var thrown = await Record.ExceptionAsync(() => coordinator.NotifyActivatedAsync(other));

        Assert.Null(thrown);
        Assert.False(button.IsActive);
        Assert.Single(errors);
    }

    private sealed class RecordingController : IToolActivationController
    {
        public Task DeactivateExternallyAsync() => Task.CompletedTask;
    }
}
