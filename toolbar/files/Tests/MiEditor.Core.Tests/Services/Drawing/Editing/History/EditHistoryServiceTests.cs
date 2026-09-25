using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.Messages.Toolbar;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class EditHistoryServiceTests
{
    private readonly StrongReferenceMessenger messenger = new();
    private readonly EditHistoryJournal journal = new(new EditHistoryInfo(MaxUndoDepth: 10));
    private readonly EditHistoryService sut;

    private readonly List<EditHistoryChangedMessage> published = [];

    public EditHistoryServiceTests()
    {
        sut = new EditHistoryService(journal, messenger);
        messenger.Register<EditHistoryChangedMessage>(this, (_, m) => published.Add(m));
    }

    [Fact]
    public void WithNothingRecorded_NeitherUndoNorRedoIsAvailable()
    {
        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void AvailabilityFollowsTheJournal()
    {
        journal.Record(CreateEntry());

        Assert.True(sut.CanUndo);
        Assert.False(sut.CanRedo);

        journal.PopUndoToRedo();

        Assert.False(sut.CanUndo);
        Assert.True(sut.CanRedo);
    }

    [Fact]
    public void RecordingAnOperation_TellsTheToolbarWhatIsAvailable()
    {
        journal.Record(CreateEntry());

        var message = Assert.Single(published);
        Assert.True(message.CanUndo);
        Assert.False(message.CanRedo);
    }

    [Fact]
    public void ClearHistory_EmptiesTheJournalAndTellsTheToolbar()
    {
        journal.Record(CreateEntry());
        published.Clear();

        sut.ClearHistory();

        Assert.Equal(0, journal.UndoDepth);
        Assert.False(Assert.Single(published).CanUndo);
    }

    [Fact]
    public void EveryCaseLifecycleMessage_ClearsTheHistory()
    {
        // Sent as their concrete types on purpose. The messenger dispatches by the static type
        // of the argument, so sending these as object would register a different message type
        // and reach nothing.
        AssertClears(() => messenger.Send(new SelectedCaseMessage(null)));
        AssertClears(() => messenger.Send(new SelectedCaseStartEditingMessage("Available")));
        AssertClears(() => messenger.Send(new SelectedCaseStopEditingMessage()));
        AssertClears(() => messenger.Send(new ExitCaseMessage()));

        void AssertClears(Action send)
        {
            journal.Record(CreateEntry());
            Assert.True(sut.CanUndo);

            send();

            Assert.False(sut.CanUndo);
            Assert.Equal(0, journal.UndoDepth);
        }
    }

    [Fact]
    public async Task UndoAsync_DoesNothingWhileThereIsNothingToUndo()
    {
        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
    }

    [Fact]
    public async Task UndoAsync_DoesNotMoveAnEntryBeforeTheInverseCanBeApplied()
    {
        // Applying the inverse arrives with the recording work. Until it does, the entry must
        // stay where it is: moving it to the redo side would claim an operation had been
        // reversed while the data still holds it.
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.Equal(1, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
    }

    [Fact]
    public async Task RedoAsync_DoesNotMoveAnEntryEither()
    {
        journal.Record(CreateEntry());
        journal.PopUndoToRedo();

        await sut.RedoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(1, journal.RedoDepth);
    }
}
