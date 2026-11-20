using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using Moq;
using System.Globalization;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.MainApp.ViewModels.Measurement;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Measurement;

public class LiveMeasurementViewModelReadingTests
{
    private readonly LiveMeasurementViewModel sut = new(
        new StrongReferenceMessenger(),
        new Mock<INotificationManager>().Object,
        Options.Create(new SheepMovementBufferSettings { MinRadius = 500, MaxRadius = 100_000 }));

    private static string Expected(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    // Regression: zero readings used to be dropped, so undoing vertices left the old value showing.
    [Fact]
    public void ZeroReading_IsShown_RatherThanKeepingTheOldValue()
    {
        sut.Receive(new SketchingStartedMessage<MeasureTool>(MeasureTool.Area));
        sut.Receive(new DrawingProgressMessage(120, 900));

        sut.Receive(new DrawingProgressMessage(0, 0));

        Assert.Equal(Expected(0, "N2"), sut.LengthValue);
        Assert.Equal(Expected(0, "N2"), sut.AreaValue);
    }

    [Fact]
    public void MissingOrNonFiniteReading_LeavesTheFieldBlank()
    {
        sut.Receive(new DrawingProgressMessage(double.NaN, null));

        Assert.Equal(string.Empty, sut.LengthValue);
        Assert.Equal(string.Empty, sut.AreaValue);
    }

    [Fact]
    public void ChangingUnits_ReformatsTheCurrentReading()
    {
        sut.Receive(new DrawingProgressMessage(1_500, 25_000));

        sut.SelectedLengthUnit = MeasurementUnit.Kilometres;
        sut.SelectedAreaUnit = MeasurementUnit.Hectares;

        Assert.Equal(Expected(1.5, "N2"), sut.LengthValue);
        Assert.Equal(Expected(2.5, "N4"), sut.AreaValue);
    }

    [Fact]
    public void NullUnitFromThePicker_FallsBackToTheDefaultUnit()
    {
        sut.Receive(new DrawingProgressMessage(1_500, 25_000));

        sut.SelectedLengthUnit = null!;

        Assert.Equal(Expected(1_500, "N2"), sut.LengthValue);
    }

    [Fact]
    public void Units_DisplayTheirLabels()
    {
        Assert.Equal(["m", "km"], sut.LengthUnits.Select(u => u.ToString()));
        Assert.Equal(["sq.m", "ha"], sut.AreaUnits.Select(u => u.ToString()));
    }
}
