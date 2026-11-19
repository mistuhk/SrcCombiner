
using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Drawing.Measurement.Manager;
using WG.MiEditor.MainApp.ViewModels.Measurement;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.MeasurementTools;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Measurement;

public class MeasurementToolGroupViewModelTests
{
    private readonly StrongReferenceMessenger messenger = new();
    private readonly ToolActivationCoordinator coordinator = new();
    private readonly FakeMeasureOperationManager drawing = new();
    private readonly FakeErrorReporter errors = new();

    private readonly MeasurementToolGroupViewModel sut;

    public MeasurementToolGroupViewModelTests()
    {
        var registry = new ToolDescriptorRegistry(
        [
            new DistanceMeasureToolDescriptor(),
            new AreaMeasureToolDescriptor(),
            new SheepMovementBufferMeasureToolDescriptor()
        ]);

        sut = new MeasurementToolGroupViewModel(messenger, coordinator, registry, drawing, errors);
    }

    private IconButtonControlViewModel<MeasureTool> Button(MeasureTool tool) => sut.FlyoutTools.First(t => t.Tool == tool);

    private void Click(MeasureTool tool) => Button(tool).SelectCommand.Execute(null);

    [Fact]
    public void BuildsFlyoutToolsInOrder()
    {
        Assert.Equal(
            [MeasureTool.Distance, MeasureTool.Area, MeasureTool.SheepMovementBuffer],
            sut.FlyoutTools.Select(t => t.Tool));
    }

    [Fact]
    public void AnchorShowsDefaultIcon_WhenNoToolActive()
    {
        Assert.Equal("measure.png", sut.CurrentIcon);
    }

    [Fact]
    public void SelectingDistance_ActivatesIt_DerivesAnchorIcon_AndDrivesDrawing()
    {
        var inUse = false;
        messenger.Register<MeasurementInProgressMessage>(this, (_, _) => inUse = true);

        Click(MeasureTool.Distance);

        Assert.True(Button(MeasureTool.Distance).IsActive);
        Assert.Equal("measure_line.png", sut.CurrentIcon);
        Assert.True(inUse);
        Assert.Equal(MeasureTool.Distance, drawing.LastActivated);
    }

    [Fact]
    public void SelectingATool_ClosesTheFlyout()
    {
        sut.IsFlyoutVisible = true;

        Click(MeasureTool.Area);

        Assert.False(sut.IsFlyoutVisible);
    }

    [Fact]
    public void SelectingAnotherTool_DeactivatesThePrevious()
    {
        Click(MeasureTool.Distance);

        Click(MeasureTool.Area);

        Assert.False(Button(MeasureTool.Distance).IsActive);
        Assert.True(Button(MeasureTool.Area).IsActive);
    }

    [Fact]
    public void ReclickingActiveTool_TogglesOff_ResetsAnchor_AndDeactivatesDrawing()
    {
        Click(MeasureTool.Distance);

        Click(MeasureTool.Distance);

        Assert.False(Button(MeasureTool.Distance).IsActive);
        Assert.Equal("measure.png", sut.CurrentIcon);
        Assert.True(drawing.DeactivateCount > 0);
    }

    [Fact]
    public async Task ExternalSession_DeactivatesActiveTool_AndResetsAnchor()
    {
        Click(MeasureTool.Distance);

        // A tool activating in another toolbar deactivates this controller through the
        // shared coordinator; there is no per toolbar in-use message.
        var other = new ToolActivationController<MeasureTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask);
        await coordinator.NotifyActivatedAsync(other);

        Assert.False(Button(MeasureTool.Distance).IsActive);
        Assert.Equal("measure.png", sut.CurrentIcon);
    }

    [Fact]
    public void ActivatingAMeasureTool_DeactivatesAnotherControllerOnSameCoordinator()
    {
        var otherButton = new IconButtonControlViewModel<MeasureTool>(MeasureTool.Area, "x.png", _ => { });
        var other = new ToolActivationController<MeasureTool>(
            coordinator, _ => Task.CompletedTask, () => Task.CompletedTask, [otherButton]);
        other.HandleSelected(MeasureTool.Area);

        Assert.True(otherButton.IsActive);

        Click(MeasureTool.Distance);

        Assert.False(otherButton.IsActive);
    }

    [Fact]
    public void TappingAnchor_TogglesFlyoutOpenAndClosed()
    {
        Assert.False(sut.IsFlyoutVisible);

        sut.CurrentTool.SelectCommand.Execute(null);
        Assert.True(sut.IsFlyoutVisible);

        sut.CurrentTool.SelectCommand.Execute(null);
        Assert.False(sut.IsFlyoutVisible);
    }

    [Fact]
    public void TappingAnchor_WhileToolActive_OpensFlyout_WithoutDeactivatingTool()
    {
        Click(MeasureTool.Distance);
        Assert.True(Button(MeasureTool.Distance).IsActive);

        sut.CurrentTool.SelectCommand.Execute(null);

        Assert.True(sut.IsFlyoutVisible);
        Assert.True(Button(MeasureTool.Distance).IsActive);
    }

    [Fact]
    public void ActivationFailure_RollsBackTheButtonAndAnchor_AndReportsTheError()
    {
        var failure = new MeasurementUnavailableException("The map is not ready yet.");
        drawing.ActivateFailure = failure;

        Click(MeasureTool.Distance);

        Assert.False(Button(MeasureTool.Distance).IsActive);
        Assert.Equal("measure.png", sut.CurrentIcon);
        Assert.Same(failure, Assert.Single(errors.Reported));
    }

    [Fact]
    public void ActivationFailure_TearsDownAnythingHalfStarted()
    {
        drawing.ActivateFailure = new InvalidOperationException("boom");

        Click(MeasureTool.Area);

        Assert.Equal(1, drawing.DeactivateCount);
    }

    [Fact]
    public void AfterActivationFailure_TheToolCanBeSelectedAgain()
    {
        drawing.ActivateFailure = new InvalidOperationException("boom");
        Click(MeasureTool.Distance);

        drawing.ActivateFailure = null;
        Click(MeasureTool.Distance);

        Assert.True(Button(MeasureTool.Distance).IsActive);
        Assert.Equal("measure_line.png", sut.CurrentIcon);
    }

    [Fact]
    public void DeactivationFailure_IsReported_AndTheToolStillTurnsOff()
    {
        Click(MeasureTool.Distance);
        drawing.DeactivateFailure = new InvalidOperationException("boom");

        Click(MeasureTool.Distance);

        Assert.False(Button(MeasureTool.Distance).IsActive);
        Assert.Single(errors.Reported);
    }

    private sealed class FakeErrorReporter : IMeasurementErrorReporter
    {
        public List<Exception> Reported { get; } = [];

        public void Report(string title, Exception exception) => Reported.Add(exception);
    }

    private sealed class FakeMeasureOperationManager : IMeasureOperationManager
    {
        public MeasureTool? LastActivated { get; private set; }

        public int DeactivateCount { get; private set; }

        public Exception? ActivateFailure { get; set; }

        public Exception? DeactivateFailure { get; set; }

        public Task Activate(MeasureTool tool)
        {
            if (ActivateFailure is { } failure) return Task.FromException(failure);

            LastActivated = tool;
            return Task.CompletedTask;
        }

        public Task Deactivate()
        {
            DeactivateCount++;
            return DeactivateFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }
}
