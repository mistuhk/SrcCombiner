using WG.MiEditor.Core.Services.Drawing.Editing.History;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class EditConflictDetectorTests
{
    private const string Child = "Canopy Point";

    private readonly RecordingStateReader reader = new();
    private readonly EditConflictDetector sut;

    public EditConflictDetectorTests() => sut = new EditConflictDetector(reader);

    // ---------------------------------------------------------------- how the rows are fetched

    [Fact]
    public async Task ManyRowsOnOneLayer_AreFetchedInOneQuery()
    {
        // The whole point of batching. Twenty rows must not cost twenty round trips.
        var changes = Enumerable.Range(1, 20)
            .Select(i => FeatureChange.Update(CreateSnapshot(i, $"{i}"), CreateSnapshot(i, $"{i}")))
            .ToList();

        await sut.CheckAsync(CreateEntry(changes: changes), EditHistoryDirection.Undo);

        var batch = Assert.Single(reader.Batches);
        Assert.Equal(20, batch.Identities.Count);
    }

    [Fact]
    public async Task RowsOnDifferentLayers_AreFetchedSeparately()
    {
        await sut.CheckAsync(CreateEntry(changes:
        [
            FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100")),
            FeatureChange.Update(CreateSnapshot(9, layer: Child), CreateSnapshot(9, layer: Child)),
        ]), EditHistoryDirection.Undo);

        Assert.Equal(2, reader.Batches.Count);
        Assert.Contains(reader.Batches, b => b.LayerName == Layer);
        Assert.Contains(reader.Batches, b => b.LayerName == Child);
    }

    [Fact]
    public async Task OneLayerYieldingBothKeyedAndKeylessRows_FetchesEachStrategySeparately()
    {
        // FeatureSnapshotFactory returns a keyless identity whenever the key attribute is null, so
        // a keyed layer yields both kinds. An earlier version put them in one batch, the keyless
        // ones were dropped from the IN clause, and they read back as deleted by somebody else.
        await sut.CheckAsync(CreateEntry(changes:
        [
            FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100")),
            FeatureChange.Update(CreateSnapshot(2, businessKey: null), CreateSnapshot(2, businessKey: null)),
        ]), EditHistoryDirection.Undo);

        Assert.Equal(2, reader.Batches.Count);
        Assert.All(reader.Batches, b =>
            Assert.True(
                b.Identities.All(i => i.HasBusinessKey) || b.Identities.All(i => !i.HasBusinessKey),
                "Every batch has to be homogeneous, because the reader rejects a mixed one."));
    }

    [Fact]
    public async Task EveryBatchSatisfiesWhatTheReaderDemandsOfIt()
    {
        // The reader throws on a batch that mixes strategies. This asserts the detector never
        // hands it one, without the test having to know how the grouping is written.
        reader.ValidateLikeTheRealReader = true;

        await sut.CheckAsync(CreateEntry(changes:
        [
            FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100")),
            FeatureChange.Update(CreateSnapshot(2, businessKey: null), CreateSnapshot(2, businessKey: null)),
            FeatureChange.Update(CreateSnapshot(3, layer: Child), CreateSnapshot(3, layer: Child)),
        ]), EditHistoryDirection.Undo);

        Assert.Equal(3, reader.Batches.Count);
    }

    [Fact]
    public async Task ARowTouchedTwiceByOneOperation_IsOnlyFetchedOnce()
    {
        await sut.CheckAsync(CreateEntry(changes:
        [
            FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100")),
            FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100")),
        ]), EditHistoryDirection.Undo);

        Assert.Single(Assert.Single(reader.Batches).Identities);
    }

    [Fact]
    public async Task AnEntryWithNoChanges_DoesNotTouchTheServiceAtAll()
    {
        var result = await sut.CheckAsync(
            EditOperationEntry.Create("Nothing", CaseKey, [], DateTime.UtcNow),
            EditHistoryDirection.Undo);

        Assert.False(result.HasConflict);
        Assert.Empty(reader.Batches);
    }

    // ---------------------------------------------------------------- what counts as a conflict

    [Fact]
    public async Task AKeylessRowStillPresent_IsNotAConflict()
    {
        // The case the batching bug broke: a keyless row on a keyed layer, alive, undo allowed.
        var snapshot = CreateSnapshot(2, businessKey: null);
        reader.Live = [snapshot];

        var result = await sut.CheckAsync(
            CreateEntry(changes: [FeatureChange.Update(snapshot, snapshot)]),
            EditHistoryDirection.Undo);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task ARowSomebodyElseDeleted_IsAConflict()
    {
        reader.Live = [];

        var result = await sut.CheckAsync(CreateEntry(), EditHistoryDirection.Undo);

        Assert.True(result.HasConflict);
        Assert.Contains("deleted by someone else", result.Reason);
    }

    [Fact]
    public async Task ARowSomebodyElseChanged_IsAConflict()
    {
        reader.Live = [CreateSnapshot(1, "100", stamp: new DateTime(2026, 1, 1))];

        var result = await sut.CheckAsync(
            CreateEntry(changes: [FeatureChange.Update(
                CreateSnapshot(1, "100", stamp: new DateTime(2025, 1, 1)),
                CreateSnapshot(1, "100", stamp: new DateTime(2025, 1, 1)))]),
            EditHistoryDirection.Undo);

        Assert.True(result.HasConflict);
        Assert.Contains("changed by someone else", result.Reason);
    }

    [Fact]
    public async Task ARowSomebodyElseRecreated_IsAConflictWhenUndoingADelete()
    {
        // Undoing a delete means putting the row back. If it is already back, somebody beat us.
        reader.Live = [CreateSnapshot(1, "100")];

        var result = await sut.CheckAsync(
            CreateEntry(changes: [FeatureChange.Delete(CreateSnapshot(1, "100"))]),
            EditHistoryDirection.Undo);

        Assert.True(result.HasConflict);
        Assert.Contains("created again by someone else", result.Reason);
    }

    [Fact]
    public async Task GeometryWrittenDifferentlyButTheSameShape_IsNotAConflict()
    {
        // The service round trips coordinates, so text inequality alone must not read as an edit.
        // With GeometryEngine stubbed this asserts that the detector consults the shape comparison
        // rather than stopping at the string, not that the real comparison is correct.
        reader.Live = [CreateSnapshot(1, "100", geometryJson: "{\"x\": 1.0, \"y\": 2.0}")];

        var result = await sut.CheckAsync(
            CreateEntry(changes: [FeatureChange.Update(
                CreateSnapshot(1, "100", geometryJson: "{\"x\":1,\"y\":2}"),
                CreateSnapshot(1, "100", geometryJson: "{\"x\":1,\"y\":2}"))]),
            EditHistoryDirection.Undo);

        Assert.False(result.HasConflict);
    }

    // ---------------------------------------------------------------- fake

    private sealed record Batch(string LayerName, IReadOnlyCollection<FeatureIdentity> Identities);

    private sealed class RecordingStateReader : IFeatureStateReader
    {
        public List<Batch> Batches { get; } = [];

        /// <summary>Null means "every row asked for is still there, unchanged".</summary>
        public List<FeatureSnapshot>? Live { get; set; }

        /// <summary>Applies the real reader's precondition, so a bad batch fails the test.</summary>
        public bool ValidateLikeTheRealReader { get; set; }

        public Task<FeatureSnapshot?> ReadAsync(FeatureIdentity identity) =>
            throw new NotSupportedException("The detector reads in batches.");

        public Task<IReadOnlyList<FeatureSnapshot>> ReadManyAsync(
            string layerName,
            IReadOnlyCollection<FeatureIdentity> identities)
        {
            if (ValidateLikeTheRealReader)
            {
                var first = identities.First();

                Assert.DoesNotContain(identities, i => i.HasBusinessKey != first.HasBusinessKey);
                Assert.DoesNotContain(identities, i => !string.Equals(
                    i.BusinessKeyField, first.BusinessKeyField, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(identities, i => !string.Equals(
                    i.LayerName, layerName, StringComparison.OrdinalIgnoreCase));
            }

            Batches.Add(new Batch(layerName, identities));

            IReadOnlyList<FeatureSnapshot> result = Live is not null
                ? [.. Live.Where(s => identities.Any(i => i.SameRowAs(s.Identity)))]
                : [.. identities.Select(i => CreateSnapshot(i.ObjectId, i.BusinessKeyValue, layer: i.LayerName))];

            return Task.FromResult(result);
        }
    }
}
