using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class EditHistoryJournalTests
{
    private readonly EditHistoryJournal sut = new(new EditHistoryInfo(MaxUndoDepth: 10));

    [Fact]
    public void NewJournal_OffersNeitherUndoNorRedo()
    {
        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
        Assert.Equal(0, sut.UndoDepth);
    }

    [Fact]
    public void Record_MakesTheOperationUndoable()
    {
        sut.Record(CreateEntry());

        Assert.True(sut.CanUndo);
        Assert.Equal(1, sut.UndoDepth);
    }

    [Fact]
    public void Record_PastTheCap_EvictsTheOldestOperation()
    {
        var oldest = CreateEntry("Operation 0");
        sut.Record(oldest);

        for (var i = 1; i <= 10; i++)
        {
            sut.Record(CreateEntry($"Operation {i}"));
        }

        Assert.Equal(10, sut.UndoDepth);

        var names = DrainUndoNames();
        Assert.DoesNotContain("Operation 0", names);
        Assert.Contains("Operation 10", names);
    }

    [Fact]
    public void Record_ClearsTheRedoStack()
    {
        sut.Record(CreateEntry("first"));
        sut.PopUndoToRedo();
        Assert.True(sut.CanRedo);

        sut.Record(CreateEntry("second"));

        Assert.False(sut.CanRedo);
        Assert.Equal(0, sut.RedoDepth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void CombinedDepth_NeverExceedsTheCap(int operations)
    {
        for (var i = 0; i < operations; i++)
        {
            sut.Record(CreateEntry($"Operation {i}"));
        }

        for (var i = 0; i < operations; i++)
        {
            sut.PopUndoToRedo();
            Assert.True(sut.UndoDepth + sut.RedoDepth <= 10);
        }

        Assert.Equal(operations, sut.RedoDepth);
        Assert.Equal(0, sut.UndoDepth);
    }

    [Fact]
    public void PopUndoToRedo_ThenPopRedoToUndo_RestoresTheSameEntry()
    {
        var entry = CreateEntry("Merge");
        sut.Record(entry);

        var undone = sut.PopUndoToRedo();
        var redone = sut.PopRedoToUndo();

        Assert.Equal(entry.Id, undone!.Id);
        Assert.Equal(entry.Id, redone!.Id);
        Assert.Equal(1, sut.UndoDepth);
        Assert.Equal(0, sut.RedoDepth);
    }

    [Fact]
    public void PopUndoToRedo_OnAnEmptyJournal_ReturnsNull()
    {
        Assert.Null(sut.PopUndoToRedo());
        Assert.Null(sut.PopRedoToUndo());
    }

    [Fact]
    public void DiscardUndo_RemovesTheEntryWithoutOfferingItForRedo()
    {
        sut.Record(CreateEntry());

        sut.DiscardUndo();

        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void RemapObjectId_RewritesTheIdAcrossBothStacks()
    {
        sut.Record(CreateEntryTouching(42));
        sut.PopUndoToRedo();
        sut.Record(CreateEntryTouching(42));

        sut.RemapObjectId(Layer, oldObjectId: 42, newObjectId: 77);

        Assert.Equal(77, sut.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    [Fact]
    public void RemapObjectId_LeavesOtherLayersAlone()
    {
        sut.Record(CreateEntry(changes: [FeatureChange.Update(
            CreateSnapshot(42, layer: "Canopy_Point"),
            CreateSnapshot(42, layer: "Canopy_Point"))]));

        sut.RemapObjectId(Layer, oldObjectId: 42, newObjectId: 77);

        Assert.Equal(42, sut.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    [Fact]
    public void DropEntriesReferencing_SweepsBothStacks()
    {
        sut.Record(CreateEntryTouching(1, "100"));
        sut.Record(CreateEntryTouching(2, "200"));
        sut.PopUndoToRedo();

        var dropped = sut.DropEntriesReferencing([CreateIdentity(2, "200")]);

        Assert.Equal(1, dropped);
        Assert.Equal(0, sut.RedoDepth);
        Assert.Equal(1, sut.UndoDepth);
    }

    [Fact]
    public void DropEntriesReferencing_MatchesOnBusinessKeyNotObjectId()
    {
        sut.Record(CreateEntryTouching(1, "100"));

        var dropped = sut.DropEntriesReferencing([CreateIdentity(999, "100")]);

        Assert.Equal(1, dropped);
        Assert.Equal(0, sut.UndoDepth);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        sut.Record(CreateEntry());
        sut.PopUndoToRedo();
        sut.Record(CreateEntry());

        sut.Clear();

        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void AnOperationLargerThanTheWholeBudget_IsRefusedAndLeavesTheHistoryIntact()
    {
        var journal = new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 10, MaxTotalBytes: 2000));
        journal.Record(CreateEntry("Earlier work"));

        var recorded = journal.Record(CreateEntry("Enormous", changes:
            [FeatureChange.Insert(CreateSnapshot(1, "100", geometryJson: new string('x', 5000)))]));

        Assert.False(recorded);
        Assert.Equal(1, journal.UndoDepth);
        Assert.True(journal.CanUndo);
        Assert.Equal("Earlier work", journal.PeekUndo()!.OperationName);
    }

    [Fact]
    public void ALargeOperationEvictsOlderOnesRatherThanBlockingUndo()
    {
        var large = CreateEntry("Large", changes:
            [FeatureChange.Insert(CreateSnapshot(1, "100", geometryJson: new string('x', 2500)))]);

        // Room for the large operation and a couple of small ones, but not for five as well,
        // so recording the large one has to evict rather than refuse.
        var budget = large.ApproximateSizeInBytes() + (CreateEntry().ApproximateSizeInBytes() * 2);
        var journal = new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 10, MaxTotalBytes: budget));

        for (var i = 0; i < 5; i++)
        {
            journal.Record(CreateEntry($"Small {i}"));
        }

        Assert.Equal(5, journal.UndoDepth);

        var recorded = journal.Record(large);

        Assert.True(recorded);
        Assert.True(journal.CanUndo);
        Assert.Equal("Large", journal.PeekUndo()!.OperationName);
        Assert.True(journal.UndoDepth < 6);
    }

    [Fact]
    public void Changed_IsRaisedOnEveryMutation()
    {
        var raised = 0;
        sut.Changed += (_, _) => raised++;

        sut.Record(CreateEntry());
        sut.PopUndoToRedo();
        sut.PopRedoToUndo();
        sut.Clear();

        Assert.Equal(4, raised);
    }

    [Fact]
    public void Changed_IsNotRaisedWhenNothingActuallyChanges()
    {
        var raised = 0;
        sut.Changed += (_, _) => raised++;

        sut.Clear();
        sut.ClearRedo();
        sut.DropEntriesReferencing([]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Constructor_RejectsANonPositiveDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 0)));
    }

    [Fact]
    public void RowsOnAKeylessLayer_AreNotTreatedAsTheSameRow()
    {
        // Two canopy points in the same parcel. Without a business key they must be
        // distinguished by object id, or dropping one would drop both.
        sut.Record(CreateEntry(changes: [FeatureChange.Update(
            CreateSnapshot(501, layer: "Canopy_Point"), CreateSnapshot(501, layer: "Canopy_Point"))]));
        sut.Record(CreateEntry(changes: [FeatureChange.Update(
            CreateSnapshot(502, layer: "Canopy_Point"), CreateSnapshot(502, layer: "Canopy_Point"))]));

        var dropped = sut.DropEntriesReferencing([new FeatureIdentity("Canopy_Point", 502)]);

        Assert.Equal(1, dropped);
        Assert.Equal(1, sut.UndoDepth);
    }

    [Fact]
    public void ClearRedo_EmptiesAPopulatedRedoStack()
    {
        sut.Record(CreateEntry());
        sut.PopUndoToRedo();
        Assert.Equal(1, sut.RedoDepth);

        sut.ClearRedo();

        Assert.Equal(0, sut.RedoDepth);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void PeekRedo_ReturnsTheEntryThatRedoWouldReapply()
    {
        var entry = CreateEntry("Merge");
        sut.Record(entry);
        sut.PopUndoToRedo();

        Assert.Equal(entry.Id, sut.PeekRedo()!.Id);
        Assert.Equal(1, sut.RedoDepth);
    }

    [Fact]
    public void Record_ReturnsTrueWhenTheOperationIsKept()
    {
        Assert.True(sut.Record(CreateEntry()));
    }

    [Fact]
    public void AnOperationTooLargeToRecord_StillClearsRedo()
    {
        // An operation happened, so nothing on the redo side can be reached from the state the
        // data is now in. Whether the operation was small enough to keep is a separate matter.
        var journal = new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 10, MaxTotalBytes: 3000));
        journal.Record(CreateEntry("Something"));
        journal.PopUndoToRedo();
        Assert.Equal(1, journal.RedoDepth);

        var recorded = journal.Record(CreateEntry("Enormous", changes:
            [FeatureChange.Insert(CreateSnapshot(1, "100", geometryJson: new string('x', 5000)))]));

        Assert.False(recorded);
        Assert.Equal(0, journal.RedoDepth);
        Assert.False(journal.CanRedo);
    }

    [Fact]
    public void AnOperationTooLargeToRecord_TellsListenersWhenItClearedRedo()
    {
        var journal = new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 10, MaxTotalBytes: 3000));
        journal.Record(CreateEntry("Something"));
        journal.PopUndoToRedo();

        var raised = 0;
        journal.Changed += (_, _) => raised++;

        journal.Record(CreateEntry("Enormous", changes:
            [FeatureChange.Insert(CreateSnapshot(1, "100", geometryJson: new string('x', 5000)))]));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AnOperationTooLargeToRecord_SaysNothingWhenThereWasNoRedoToClear()
    {
        var journal = new EditHistoryJournal(new EditHistoryInfo(MaxUndoDepth: 10, MaxTotalBytes: 3000));

        var raised = 0;
        journal.Changed += (_, _) => raised++;

        var recorded = journal.Record(CreateEntry("Enormous", changes:
            [FeatureChange.Insert(CreateSnapshot(1, "100", geometryJson: new string('x', 5000)))]));

        Assert.False(recorded);
        Assert.Equal(0, raised);
    }

    private List<string> DrainUndoNames()
    {
        var names = new List<string>();

        while (sut.PeekUndo() is not null)
        {
            names.Add(sut.DiscardUndo()!.OperationName);
        }

        return names;
    }
}
