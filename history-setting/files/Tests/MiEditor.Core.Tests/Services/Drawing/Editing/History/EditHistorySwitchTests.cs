using System.ComponentModel;
using Moq;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.UserPreferences;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.Configuration.AppSettings;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

/// <summary>
/// Two answers decide whether the history is in use, and the deployment's answer wins. That precedence
/// is the whole reason this type exists, so it is what these assert.
/// </summary>
public class EditHistorySwitchTests
{
    private readonly Mock<IUserPreferenceService> preferences = new();
    private readonly Mock<ILoggerService> logger = new();

    private bool wanted;

    public EditHistorySwitchTests() =>
        preferences.SetupGet(p => p.HistoryEnabled).Returns(() => wanted);

    private EditHistorySwitch Create(bool available) =>
        new(new EditHistoryInfo(Available: available), preferences.Object, logger.Object);

    private void UserSets(bool value)
    {
        wanted = value;
        preferences.Raise(
            p => p.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IUserPreferenceService.HistoryEnabled)));
    }

    // ---------------------------------------------------------------- the four combinations

    [Fact]
    public void Available_And_Wanted_Is_On()
    {
        wanted = true;

        Assert.True(Create(available: true).IsEnabled);
    }

    [Fact]
    public void Available_But_Not_Wanted_Is_Off()
    {
        wanted = false;

        Assert.False(Create(available: true).IsEnabled);
    }

    [Fact]
    public void Withdrawn_Is_Off_Even_When_The_User_Wants_It()
    {
        // The point of the deployment switch. One a user could override would not be a switch.
        wanted = true;

        Assert.False(Create(available: false).IsEnabled);
    }

    [Fact]
    public void Withdrawn_And_Not_Wanted_Is_Off()
    {
        wanted = false;

        Assert.False(Create(available: false).IsEnabled);
    }

    [Fact]
    public void Availability_Is_The_Deployment_Answer_Alone()
    {
        // What the settings panel asks, so it can leave the section out rather than show a switch that
        // cannot do anything. Independent of what the user wants.
        wanted = false;

        Assert.True(Create(available: true).IsAvailable);
        Assert.False(Create(available: false).IsAvailable);
    }

    // ---------------------------------------------------------------- when it reports a change

    [Fact]
    public void Wanting_It_Reports_A_Change_When_Available()
    {
        var sut = Create(available: true);
        var changes = 0;
        sut.Changed += (_, _) => changes++;

        UserSets(true);

        Assert.Equal(1, changes);
        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public void No_Longer_Wanting_It_Reports_A_Change()
    {
        wanted = true;
        var sut = Create(available: true);
        var changes = 0;
        sut.Changed += (_, _) => changes++;

        UserSets(false);

        Assert.Equal(1, changes);
        Assert.False(sut.IsEnabled);
    }

    [Fact]
    public void Wanting_It_Reports_Nothing_When_Withdrawn()
    {
        // The answer was no and remains no, so there is nothing to tell anyone. Reporting a change here
        // would have the service clear a history it never had, on a switch that did not move.
        var sut = Create(available: false);
        var changes = 0;
        sut.Changed += (_, _) => changes++;

        UserSets(true);

        Assert.Equal(0, changes);
        Assert.False(sut.IsEnabled);
    }

    [Fact]
    public void An_Unrelated_Preference_Change_Reports_Nothing()
    {
        var sut = Create(available: true);
        var changes = 0;
        sut.Changed += (_, _) => changes++;

        wanted = true;
        preferences.Raise(
            p => p.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IUserPreferenceService.ThemeChoice)));

        Assert.Equal(0, changes);
    }

    // ---------------------------------------------------------------- what it must not do

    [Fact]
    public void The_Users_Stored_Choice_Is_Read_And_Never_Written()
    {
        // Withdrawing the feature must not erase what the user had chosen, so that their choice returns
        // if it is offered again.
        wanted = true;
        var sut = Create(available: false);

        _ = sut.IsEnabled;
        UserSets(true);

        preferences.VerifySet(p => p.HistoryEnabled = It.IsAny<bool>(), Times.Never);
    }

    [Fact]
    public void Withdrawing_The_Feature_Is_Said_Once_At_Startup()
    {
        // Otherwise "undo has disappeared" arrives as a support call with nothing to point at.
        Create(available: false);

        logger.Verify(l => l.Info(It.Is<string>(m => m.Contains("EditHistory.Available is false"))), Times.Once);
    }

    [Fact]
    public void Offering_The_Feature_Says_Nothing()
    {
        Create(available: true);

        logger.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
    }
}
