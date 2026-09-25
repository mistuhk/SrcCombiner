using CommunityToolkit.Mvvm.Messaging;
using Moq;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.Managers;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Toolbar;
using WG.MiEditor.Shared.Models.Toolbar;
using WG.MiEditor.Tests.Common;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class EditHistoryServiceTests
{
    private const string OpenCase = CaseKey;
    private const string PointLayer = "Canopy Point";

    private readonly StrongReferenceMessenger messenger = new();
    private readonly EditHistoryJournal journal = new(new EditHistoryInfo(MaxUndoDepth: 10));
    private readonly FakeConflictDetector detector = new();
    private readonly FakeApplier applier = new();
    private readonly FakeEditingOperations editingOperations = new();
    private readonly FakeSelectionClearer selectionClearer = new();
    private readonly FakeOperationFeedbackService feedback = new() { ExecuteOperations = true };
    private readonly FakeCaseContext caseContext = new() { CaseNo = OpenCase };

    private readonly List<EditHistoryChangedMessage> published = [];
    private readonly EditHistoryService sut;

    public EditHistoryServiceTests()
    {
        sut = new EditHistoryService(
            journal, detector, applier, editingOperations, selectionClearer,
            feedback, caseContext, messenger, Mock.Of<ILoggerService>());

        messenger.Register<EditHistoryChangedMessage>(this, (_, m) => published.Add(m));
    }

    // ---------------------------------------------------------------- availability

    [Fact]
    public void WithNothingRecorded_NeitherUndoNorRedoIsAvailable()
    {
        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
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
        // Sent as their concrete types on purpose. The messenger dispatches on the static type of
        // the argument, so sending these as object would reach nothing.
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

    // ---------------------------------------------------------------- undo, the happy path

    [Fact]
    public async Task Undo_AppliesTheOppositeOfWhatWasRecorded()
    {
        journal.Record(CreateEntry("Add Canopy Point",
            [FeatureChange.Insert(CreateSnapshot(501, layer: PointLayer))]));

        await sut.UndoAsync();

        var change = Assert.Single(Assert.Single(applier.Applied));
        Assert.Equal(FeatureChangeKind.Delete, change.Kind);
        Assert.Equal(501, change.Identity.ObjectId);
    }

    [Fact]
    public async Task Undo_StandsTheEditingToolsDownFirst()
    {
        // A vertex tool could be holding the very feature that is about to be deleted.
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.Equal(1, editingOperations.DeactivateCalls);
    }

    [Fact]
    public async Task Undo_MovesTheEntryToTheRedoSide()
    {
        journal.Record(CreateEntry());

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(1, journal.RedoDepth);
        Assert.True(sut.CanRedo);
    }

    [Fact]
    public async Task Undo_ClearsTheSelectionAndSaysWhatItUndid()
    {
        journal.Record(CreateEntry("Add Canopy Point"));

        await sut.UndoAsync();

        Assert.Equal(1, selectionClearer.Calls);
        Assert.Contains(("Undo", "Undone: Add Canopy Point"), feedback.SuccessNotifications);
    }

    [Fact]
    public async Task Undo_RewritesTheObjectIdTheServiceReplaced()
    {
        // A restored row gets a new id, and Canopy Point has no business key to fall back on, so
        // the rewrite is the only thing keeping other entries pointing at the right row.
        journal.Record(CreateEntry(changes:
            [FeatureChange.Update(CreateSnapshot(501, layer: PointLayer), CreateSnapshot(501, layer: PointLayer))]));
        journal.Record(CreateEntry(changes:
            [FeatureChange.Delete(CreateSnapshot(501, layer: PointLayer))]));

        applier.NextRemaps = [new ObjectIdRemap(PointLayer, 501, 777)];

        await sut.UndoAsync();

        Assert.Equal(777, journal.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    // ---------------------------------------------------------------- redo

    [Fact]
    public async Task Redo_AppliesTheOperationForwardsAndMovesItBack()
    {
        journal.Record(CreateEntry("Add Canopy Point",
            [FeatureChange.Insert(CreateSnapshot(501, layer: PointLayer))]));
        await sut.UndoAsync();
        applier.Applied.Clear();

        await sut.RedoAsync();

        var change = Assert.Single(Assert.Single(applier.Applied));
        Assert.Equal(FeatureChangeKind.Insert, change.Kind);
        Assert.Equal(1, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public async Task Undo_WithNothingRecorded_DoesNothing()
    {
        await sut.UndoAsync();

        Assert.Empty(applier.Applied);
        Assert.Equal(0, editingOperations.DeactivateCalls);
    }

    [Fact]
    public async Task Undo_OfAnEntryFromAnotherCase_ClearsTheHistoryRatherThanApplyingIt()
    {
        journal.Record(CreateEntry(caseKey: "CASE-999"));

        await sut.UndoAsync();

        Assert.Empty(applier.Applied);
        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Undo_WhenSomeoneElseChangedTheData_RefusesAndDropsTheEntry()
    {
        journal.Record(CreateEntry("Add Canopy Point"));
        detector.Result = ConflictResult.Conflict("Someone else changed it.", CreateIdentity(501, layer: PointLayer));

        await sut.UndoAsync();

        Assert.Empty(applier.Applied);
        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
        Assert.Contains(feedback.WarningNotifications,
            w => w.Title == "Undo" && w.Message.Contains("removed from your history"));
    }

    [Fact]
    public async Task Undo_WhenAConflictNamesRows_DropsEveryOtherEntryTouchingThem()
    {
        journal.Record(CreateEntryTouching(501));
        journal.Record(CreateEntryTouching(501));
        detector.Result = ConflictResult.Conflict("Someone else changed it.", CreateIdentity(501));

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Undo_WhenTheWriteFails_ClearsTheHistoryAndAsksForAReload()
    {
        journal.Record(CreateEntry());
        applier.NextResult = ChangeApplyResult.Failed("the service said no");

        var reloaded = false;
        messenger.Register<RefreshMessage>(this, (_, _) => reloaded = true);

        await sut.UndoAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Equal(0, journal.RedoDepth);
        Assert.True(reloaded);
        Assert.Contains(feedback.ErrorNotifications, e => e.Message.Contains("the service said no"));
    }

    [Fact]
    public async Task Undo_DoesNotRunTwiceAtOnce()
    {
        // The toolbar button fires without waiting, so a double click arrives as two calls.
        journal.Record(CreateEntry());
        journal.Record(CreateEntry());

        var gate = new TaskCompletionSource();
        applier.Gate = gate.Task;

        var first = sut.UndoAsync();
        var second = sut.UndoAsync();

        Assert.True(second.IsCompleted);

        gate.SetResult();
        await first;

        Assert.Single(applier.Applied);
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeConflictDetector : IEditConflictDetector
    {
        public ConflictResult Result { get; set; } = ConflictResult.None;

        public Task<ConflictResult> CheckAsync(EditOperationEntry entry, EditHistoryDirection direction) =>
            Task.FromResult(Result);
    }

    private sealed class FakeApplier : IFeatureChangeApplier
    {
        public List<IReadOnlyList<FeatureChange>> Applied { get; } = [];
        public IReadOnlyList<ObjectIdRemap> NextRemaps { get; set; } = [];
        public ChangeApplyResult? NextResult { get; set; }
        public Task? Gate { get; set; }

        public async Task<ChangeApplyResult> ApplyAsync(IReadOnlyList<FeatureChange> changes)
        {
            if (Gate is not null)
            {
                await Gate;
            }

            Applied.Add(changes);

            return NextResult ?? ChangeApplyResult.Applied(NextRemaps);
        }
    }

    private sealed class FakeEditingOperations : IEditingOperationsManager
    {
        public int DeactivateCalls { get; private set; }

        public Task ActivateTool(EditingTool tool, string targetLayerName) => Task.CompletedTask;

        public Task DeactivateAll(EditingTool? tool)
        {
            DeactivateCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSelectionClearer : IEditSelectionClearer
    {
        public int Calls { get; private set; }

        public Task ClearAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
