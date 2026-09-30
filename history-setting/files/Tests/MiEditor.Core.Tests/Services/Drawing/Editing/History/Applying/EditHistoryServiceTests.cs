
using CommunityToolkit.Mvvm.Messaging;
using Moq;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Applying;
using WG.MiEditor.Core.Services.Drawing.Editing.History.FeatureTables;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Model;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Toolbar;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.Model.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.Applying;

public class EditHistoryServiceTests
{
    private const string OpenCase = CaseKey;
    private const string PointLayer = "Canopy Point";

    private readonly StrongReferenceMessenger messenger = new();
    private readonly EditHistoryJournal journal = new(new EditHistoryInfo(MaxUndoDepth: 10));

    private readonly Mock<IEditConflictDetector> detector = new();
    private readonly Mock<IFeatureChangeApplier> applier = new();
    private readonly Mock<IEditSelectionClearer> selectionClearer = new();
    private readonly Mock<IEditHistorySwitch> historySwitch = new();
    private readonly Mock<IOperationFeedbackService> feedback = new();

    private readonly List<EditHistoryChangedMessage> published = [];
    private readonly EditHistoryService sut;

    /// <summary>The change lists handed to the applier, in order.</summary>
    private readonly List<IReadOnlyList<FeatureChange>> applied = [];

    /// <summary>The history is on unless a test says otherwise.</summary>
    private bool historyEnabled = true;

    private ConflictResult conflict = ConflictResult.None;
    private IReadOnlyList<ObjectIdRemap> nextRemaps = [];
    private ChangeApplyResult? nextResult;

    /// <summary>Set to hold the applier mid call, for the reentrancy test.</summary>
    private TaskCompletionSource? gate;

    public EditHistoryServiceTests()
    {
        detector
            .Setup(d => d.CheckAsync(It.IsAny<EditOperationEntry>(), It.IsAny<EditHistoryDirection>()))
            .ReturnsAsync(() => conflict);

        applier
            .Setup(a => a.ApplyAsync(It.IsAny<IReadOnlyList<FeatureChange>>()))
            .Returns(async (IReadOnlyList<FeatureChange> changes) =>
            {
                if (gate is not null)
                {
                    await gate.Task;
                }

                applied.Add(changes);

                return nextResult ?? ChangeApplyResult.Applied(nextRemaps);
            });

        selectionClearer.Setup(c => c.ClearAsync()).Returns(Task.CompletedTask);
        historySwitch.Setup(h => h.IsEnabled).Returns(() => historyEnabled);

        // Undo and redo execute through RunAsync, so it must call the supplied delegate.
        // Exceptions are not swallowed, as doing so would hide test failures.
        feedback
            .Setup(f => f.RunAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task>>(),
                It.IsAny<SpinnerType>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(async (string _, Func<Task> operation, SpinnerType _, string _, string _) =>
            {
                await operation();
                return true;
            });

        sut = new EditHistoryService(
            journal, 
            detector.Object, 
            applier.Object, 
            selectionClearer.Object,
            historySwitch.Object,
            feedback.Object, 
            Mock.Of<ICaseContext>(c => c.CaseNo == OpenCase), 
            messenger, 
            Mock.Of<ILoggerService>());

        messenger.Register<EditHistoryChangedMessage>(this, (_, m) => published.Add(m));
    }

    [Fact]
    public void With_Nothing_Recorded_Neither_Undo_Nor_Redo_Is_Available()
    {
        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public async Task With_The_History_Off_An_Undo_Is_Refused()
    {
        // Refused rather than trusting the buttons to be hidden, so a keyboard shortcut added later
        // cannot leave the feature live while it looks switched off.
        journal.Record(CreateEntry());
        historyEnabled = false;

        await sut.UndoAsync();

        Assert.Empty(applied);
    }

    [Fact]
    public void With_The_History_Off_Nothing_Is_Offered_Even_With_Entries_Recorded()
    {
        journal.Record(CreateEntry());
        historyEnabled = false;

        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void Switching_The_History_Off_Discards_What_Was_Recorded()
    {
        // Keeping it would offer, on a later switch back on, an undo of operations the officer may have
        // built on since.
        journal.Record(CreateEntry());
        journal.Record(CreateEntry());

        historyEnabled = false;
        historySwitch.Raise(h => h.Changed += null, EventArgs.Empty);

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
    }

    [Fact]
    public void Switching_The_History_On_Discards_Nothing()
    {
        journal.Record(CreateEntry());

        historySwitch.Raise(h => h.Changed += null, EventArgs.Empty);

        Assert.Equal(1, journal.UndoDepth);
    }

    [Fact]
    public void Recording_An_Operation_Tells_The_Toolbar_What_Is_Available()
    {
        journal.Record(CreateEntry());

        var message = Assert.Single(published);
        Assert.True(message.CanUndo);
        Assert.False(message.CanRedo);
    }

    [Fact]
    public void Clear_History_Empties_The_Journal_And_Tells_The_Toolbar()
    {
        journal.Record(CreateEntry());
        published.Clear();

        sut.ClearHistory();

        Assert.Equal(0, journal.UndoDepth);
        Assert.False(Assert.Single(published).CanUndo);
    }

    [Fact]
    public void Every_Case_Lifecycle_Message_Clears_The_History()
    {
        // Sent as concrete types because messenger dispatch is based on static type.
        // Sending them as object would reach no handlers.
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
        }
    }

    [Fact]
    public async Task Undo_Applies_The_Opposite_Of_What_Was_Recorded()
    {
        journal.Record(CreateEntry("Add Canopy Point",
            [FeatureChange.Insert(CreateSnapshot(501, layer: PointLayer))]));

        await sut.UndoAsync();

        var change = Assert.Single(Assert.Single(applied));
        Assert.Equal(FeatureChangeKind.Delete, change.Kind);
        Assert.Equal(501, change.Identity.ObjectId);
    }

    [Fact]
    public async Task Undo_Asks_The_Editing_Toolbar_To_Drop_Its_Active_Tool()
    {
        var asked = false;
        messenger.Register<ResetToolbarState>(this, (_, _) => asked = true);

        // A vertex tool could be holding the very feature that is about to be deleted.
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.True(asked);
    }

    [Fact]
    public async Task Undo_Waits_For_The_Toolbar_To_Stand_Down_Before_Writing()
    {
        // An editing tool may be holding the very feature the undo is about to change, and standing
        // it down is asynchronous. Asking is not enough: the write has to wait for the answer, or it
        // races the deactivation it just requested.
        var standDown = new TaskCompletionSource<bool>();
        messenger.Register<ResetToolbarState>(this, (_, m) => m.Reply(standDown.Task));
        journal.Record(CreateEntry());

        var undo = sut.UndoAsync();

        Assert.Empty(applied);

        standDown.SetResult(true);
        await undo;

        Assert.Single(applied);
    }

    [Fact]
    public async Task Undo_Proceeds_When_Nothing_Answers_The_Toolbar_Request()
    {
        // The editing toolbar need not exist, and no undo may hang or throw on that account.
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.Single(applied);
    }

    [Fact]
    public async Task Undo_Moves_The_Entry_To_The_Redo_Cache()
    {
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(1, journal.RedoDepth);
        Assert.True(sut.CanRedo);
    }

    [Fact]
    public async Task Undo_Clears_The_Selection_And_Says_What_It_Undid()
    {
        journal.Record(CreateEntry("Add Canopy Point"));

        await sut.UndoAsync();

        selectionClearer.Verify(c => c.ClearAsync(), Times.Once);
        feedback.Verify(f => f.NotifySuccess("Undo", "Undone: Add Canopy Point"), Times.Once);
    }

    [Fact]
    public async Task Undo_Rewrites_The_ObjectId_The_Service_Replaced()
    {
        // Canopy Point has no unique identifier, so when a restored row receives a new object
        // ID, remapping is the only way to keep history pointing at the correct row.
        journal.Record(CreateEntry(changes:
            [FeatureChange.Update(CreateSnapshot(501, layer: PointLayer), CreateSnapshot(501, layer: PointLayer))]));

        journal.Record(CreateEntry(changes:
            [FeatureChange.Delete(CreateSnapshot(501, layer: PointLayer))]));

        nextRemaps = [new ObjectIdRemap(PointLayer, 501, 777)];

        await sut.UndoAsync();

        Assert.Equal(777, journal.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    [Fact]
    public async Task Redo_Applies_The_Operation_Forwards_And_Moves_It_Back()
    {
        journal.Record(CreateEntry("Add Canopy Point",
            [FeatureChange.Insert(CreateSnapshot(501, layer: PointLayer))]));

        await sut.UndoAsync();
        applied.Clear();

        await sut.RedoAsync();

        var change = Assert.Single(Assert.Single(applied));
        Assert.Equal(FeatureChangeKind.Insert, change.Kind);
        Assert.Equal(1, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
    }

    [Fact]
    public async Task Undo_With_Nothing_Recorded_Does_Not_Disturb_The_Toolbar()
    {
        var asked = false;
        messenger.Register<ResetToolbarState>(this, (_, _) => asked = true);
        await sut.UndoAsync();

        Assert.False(asked);
    }

    [Fact]
    public async Task Undo_Of_An_Entry_From_Another_Case_Clears_The_History_Rather_Than_Applying_It()
    {
        journal.Record(CreateEntry(caseKey: "CASE-999"));

        await sut.UndoAsync();

        Assert.Empty(applied);
        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Undo_When_Someone_Else_Changed_The_Data_Refuses_And_Drops_The_Entry()
    {
        journal.Record(CreateEntry("Add Canopy Point"));
        conflict = ConflictResult.Conflict("Someone else changed it.", CreateIdentity(501, layer: PointLayer));

        await sut.UndoAsync();

        Assert.Empty(applied);
        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
        feedback.Verify(
            f => f.NotifyWarning("Undo", It.Is<string>(m => m.Contains("removed from your history"))),
            Times.Once);
    }

    [Fact]
    public async Task Undo_When_A_Conflict_Names_Rows_Drops_Every_Other_Entry_Touching_Them()
    {
        journal.Record(CreateEntryTouching(501));
        journal.Record(CreateEntryTouching(501));
        conflict = ConflictResult.Conflict("Someone else changed it.", CreateIdentity(501));

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Undo_When_The_Write_Fails_Clears_The_History_And_Asks_For_A_Reload()
    {
        journal.Record(CreateEntry());
        nextResult = ChangeApplyResult.Failed("the service said no");

        var reloaded = false;
        messenger.Register<RefreshMessage>(this, (_, _) => reloaded = true);

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
        Assert.True(reloaded);

        feedback.Verify(
            f => f.NotifyError(It.IsAny<string>(), It.Is<string>(m => m.Contains("the service said no"))),
            Times.Once);
    }

    [Fact]
    public async Task Undo_Does_Not_Run_Twice_At_Once()
    {
        // The toolbar button fires without waiting, so a double click arrives as two calls.
        journal.Record(CreateEntry());
        journal.Record(CreateEntry());

        gate = new TaskCompletionSource();

        var first = sut.UndoAsync();
        var second = sut.UndoAsync();

        Assert.True(second.IsCompleted);

        gate.SetResult();
        await first;

        Assert.Single(applied);
    }

    [Fact]
    public async Task Redo_Also_Asks_The_Editing_Toolbar_To_Drop_Its_Active_Tool()
    {
        journal.Record(CreateEntry());
        await sut.UndoAsync();

        var asked = false;
        messenger.Register<ResetToolbarState>(this, (_,_) => asked = true);

        await sut.RedoAsync();
        Assert.True(asked);
    }
}
