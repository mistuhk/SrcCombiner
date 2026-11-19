using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using Moq;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.MainApp.ViewModels.Measurement;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Measurement;

// The buffer tool draws with the last RadiusChangedMessage it received, so every outcome of
// validation must leave it holding the value the radius box shows.
public class LiveMeasurementViewModelRadiusTests
{
    private readonly StrongReferenceMessenger messenger = new();
    private readonly Mock<INotificationManager> notifications = new();
    private readonly List<double> sentRadii = [];
    private readonly LiveMeasurementViewModel sut;

    public LiveMeasurementViewModelRadiusTests()
    {
        var settings = Options.Create(new SheepMovementBufferSettings { MinRadius = 500, MaxRadius = 100_000 });
        sut = new LiveMeasurementViewModel(messenger, notifications.Object, settings);
        messenger.Register<RadiusChangedMessage>(this, (_, m) => sentRadii.Add(m.Radius));
    }

    private void Enter(string text)
    {
        sut.DefaultRadius = text;
        sut.ValidateRadiusCommand.Execute(null);
    }

    [Fact]
    public void ValidRadius_IsSentToTheBufferTool()
    {
        Enter("2000");

        Assert.Equal([2000d], sentRadii);
    }

    [Fact]
    public void NonNumericRadius_RevertsTheBox_AndResendsTheLastValidRadius()
    {
        Enter("2000");

        Enter("abc");

        Assert.Equal("2000", sut.DefaultRadius);
        Assert.Equal(2000d, sentRadii[^1]);
        notifications.Verify(n => n.Show(It.IsAny<string>(), It.IsAny<string>(), NotificationType.Error), Times.Once);
    }

    [Fact]
    public void OutOfRangeRadius_RevertsTheBox_AndResendsTheLastValidRadius()
    {
        Enter("2000");

        Enter("5");

        Assert.Equal("2000", sut.DefaultRadius);
        Assert.Equal(2000d, sentRadii[^1]);
    }

    [Fact]
    public void StartingTheBufferTool_AlwaysSendsAUsableRadius_EvenAfterBadInput()
    {
        sut.DefaultRadius = "not a number";

        sut.Receive(new SketchingStartedMessage<WG.MiEditor.Shared.Models.Toolbar.MeasureTool>(
            WG.MiEditor.Shared.Models.Toolbar.MeasureTool.SheepMovementBuffer));

        Assert.NotEmpty(sentRadii);
        Assert.InRange(sentRadii[^1], 500, 100_000);
    }

    // Pressing Enter raises Completed and then Unfocused; both run validation.
    [Fact]
    public void InvalidRadius_ValidatedTwiceByEnterThenUnfocus_ShowsOneError()
    {
        sut.DefaultRadius = "abc";

        sut.ValidateRadiusCommand.Execute(null); // Completed
        sut.ValidateRadiusCommand.Execute(null); // Unfocused

        notifications.Verify(n => n.Show(It.IsAny<string>(), It.IsAny<string>(), NotificationType.Error), Times.Once);
    }

    [Fact]
    public void ValidRadius_ValidatedTwice_IsSentOnce()
    {
        sut.DefaultRadius = "3000";

        sut.ValidateRadiusCommand.Execute(null);
        sut.ValidateRadiusCommand.Execute(null);

        Assert.Equal([3000d], sentRadii);
    }
}
