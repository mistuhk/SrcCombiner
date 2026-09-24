using WG.MiEditor.Core.Services.Drawing.Editing.History;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class EditHistoryJournalTests
{
    private readonly EditHistoryJournal sut = new(maxDepth: 10);

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
        sut.Record(Entry());

        Assert.True(sut.CanUndo);
        Assert.Equal(1, sut.UndoDepth);
    }

    [Fact]
    public void Record_PastTheCap_EvictsTheOldestOperation()
    {
        var oldest = Entry("Operation 0");
        sut.Record(oldest);

        for (var i = 1; i <= 10; i++)
        {
            sut.Record(Entry($"Operation {i}"));
        }

        Assert.Equal(10, sut.UndoDepth);

        var names = DrainUndoNames();
        Assert.DoesNotContain("Operation 0", names);
        Assert.Contains("Operation 10", names);
    }

    [Fact]
    public void Record_ClearsTheRedoStack()
    {
        sut.Record(Entry("first"));
        sut.PopUndoToRedo();
        Assert.True(sut.CanRedo);

        sut.Record(Entry("second"));

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
            sut.Record(Entry($"Operation {i}"));
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
        var entry = Entry("Merge");
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
        sut.Record(Entry());

        sut.DiscardUndo();

        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void RemapObjectId_RewritesTheIdAcrossBothStacks()
    {
        sut.Record(EntryTouching(42));
        sut.PopUndoToRedo();
        sut.Record(EntryTouching(42));

        sut.RemapObjectId(Layer, oldObjectId: 42, newObjectId: 77);

        Assert.Equal(77, sut.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    [Fact]
    public void RemapObjectId_LeavesOtherLayersAlone()
    {
        sut.Record(Entry(changes: [FeatureChange.Update(
            Snapshot(42, layer: "Canopy_Point"),
            Snapshot(42, layer: "Canopy_Point"))]));

        sut.RemapObjectId(Layer, oldObjectId: 42, newObjectId: 77);

        Assert.Equal(42, sut.PeekUndo()!.Changes[0].Identity.ObjectId);
    }

    [Fact]
    public void DropEntriesReferencing_SweepsBothStacks()
    {
        sut.Record(EntryTouching(1, "100"));
        sut.Record(EntryTouching(2, "200"));
        sut.PopUndoToRedo();

        var dropped = sut.DropEntriesReferencing([Identity(2, "200")]);

        Assert.Equal(1, dropped);
        Assert.Equal(0, sut.RedoDepth);
        Assert.Equal(1, sut.UndoDepth);
    }

    [Fact]
    public void DropEntriesReferencing_MatchesOnBusinessKeyNotObjectId()
    {
        sut.Record(EntryTouching(1, "100"));

        var dropped = sut.DropEntriesReferencing([Identity(999, "100")]);

        Assert.Equal(1, dropped);
        Assert.Equal(0, sut.UndoDepth);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        sut.Record(Entry());
        sut.PopUndoToRedo();
        sut.Record(Entry());

        sut.Clear();

        Assert.False(sut.CanUndo);
        Assert.False(sut.CanRedo);
    }

    [Fact]
    public void OversizedOperation_IsRecordedButFlaggedNotUndoable()
    {
        var journal = new EditHistoryJournal(maxDepth: 10, maxEntrySizeInBytes: 8);

        journal.Record(Entry(changes: [FeatureChange.Insert(Snapshot(1, "100", geometryJson: new string('x', 5000)))]));

        Assert.Equal(1, journal.UndoDepth);
        Assert.False(journal.CanUndo);
        Assert.False(journal.PeekUndo()!.IsUndoable);
        Assert.NotNull(journal.PeekUndo()!.NotUndoableReason);
    }

    [Fact]
    public void Changed_IsRaisedOnEveryMutation()
    {
        var raised = 0;
        sut.Changed += (_, _) => raised++;

        sut.Record(Entry());
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
        sut.DiscardRedoFrom();
        sut.DropEntriesReferencing([]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Constructor_RejectsANonPositiveDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistoryJournal(maxDepth: 0));
    }

    [Fact]
    public void RowsOnAKeylessLayer_AreNotTreatedAsTheSameRow()
    {
        // Two canopy points in the same parcel. Without a business key they must be
        // distinguished by object id, or dropping one would drop both.
        sut.Record(Entry(changes: [FeatureChange.Update(
            Snapshot(501, layer: "Canopy_Point"), Snapshot(501, layer: "Canopy_Point"))]));
        sut.Record(Entry(changes: [FeatureChange.Update(
            Snapshot(502, layer: "Canopy_Point"), Snapshot(502, layer: "Canopy_Point"))]));

        var dropped = sut.DropEntriesReferencing([new FeatureIdentity("Canopy_Point", 502)]);

        Assert.Equal(1, dropped);
        Assert.Equal(1, sut.UndoDepth);
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
