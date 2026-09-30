
using Esri.ArcGISRuntime.Data;
using Moq;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.History.FeatureTables;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Model;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Recording;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Tests.Common.TestFactories;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.Model.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.Recording;

/// <summary>
/// Ensures inserted rows are recorded with their final object IDs. Service
/// tables retain temporary negative IDs locally, so the real ID must come from
/// ApplyEdits results.
/// </summary>
public class EditOperationRecorderTests
{
    private const string Child = "Canopy Point";
    private const long Placeholder = -107954041;

    private readonly EditHistoryJournal journal = new(new EditHistoryInfo(MaxUndoDepth: 10));
    private readonly Mock<IFeatureStateReader> stateReader = new();
    private readonly Mock<IFeatureSnapshotFactory> snapshotFactory = new();
    private readonly Mock<ILoggerService> logger = new();
    private readonly Mock<IEditHistorySwitch> historySwitch = new();

    /// <summary>The history is on unless a test says otherwise.</summary>
    private bool historyEnabled = true;

    private readonly EditOperationRecorder sut;

    /// <summary>
    /// The identities requested for read-back, in order.
    /// </summary>
    private readonly List<FeatureIdentity> asked = [];

    /// <summary>
    /// The result of a read-back operation. Null indicates the row was not found.
    /// </summary>
    private FeatureSnapshot? stored;
    private bool missing;

    public EditOperationRecorderTests()
    {
        historySwitch.Setup(h => h.IsEnabled).Returns(() => historyEnabled);

        stateReader
            .Setup(r => r.ReadAsync(It.IsAny<FeatureIdentity>()))
            .ReturnsAsync((FeatureIdentity identity) =>
            {
                asked.Add(identity);

                return missing
                    ? null
                    : stored ?? CreateSnapshot(identity.ObjectId, layer: identity.LayerName);
            });

        sut = new EditOperationRecorder(
            journal,
            snapshotFactory.Object,
            stateReader.Object,
            Mock.Of<ICaseContext>(c => c.CaseNo == CaseKey),
            Mock.Of<IOperationFeedbackService>(),
            logger.Object,
            historySwitch.Object);
    }

    /// <summary>
    /// A test feature with a caller-controlled object ID. IDs are supplied through
    /// IFeatureSnapshotFactory because real feature OBJECTIDs cannot be set directly.
    /// </summary>
    private Feature FeatureWith(long objectId)
    {
        var feature = ArcGisTestFactory.CreateTable().CreateFeature();
        snapshotFactory.Setup(f => f.ReadObjectId(feature)).Returns(objectId);

        return feature;
    }

    /// <summary>
    /// A feature with no object ID. The default mock implementation returns zero.
    /// </summary>
    private static Feature FeatureWithNoId() =>
        ArcGisTestFactory.CreateTable().CreateFeature();

    private static AppliedEdit Added(long objectId) => new(objectId, IsInsert: true, Failed: false);

    private FeatureChange TheOnlyRecordedChange() =>
        Assert.Single(journal.PeekUndo()!.Changes);

    [Fact]
    public async Task An_Insert_Online_Records_The_Id_The_Service_Assigned_And_Not_The_Placeholder()
    {
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Child, [Added(8842)]);
        await scope.CommitAsync();

        Assert.Equal(8842, TheOnlyRecordedChange().Identity.ObjectId);

        // And it never looked at the feature, which still carries the placeholder.
        snapshotFactory.Verify(f => f.ReadObjectId(It.IsAny<Feature>()), Times.Never);
    }

    [Fact]
    public async Task An_Insert_Online_With_ResolveInsertedObjectIds_Forgotten_Is_Refused_Rather_Than_Recorded_Wrong()
    {
        // Without the check, the placeholder ID would be recorded and the user's
        // own insert would later appear deleted.
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("ResolveInsertedObjectIds"))), Times.Once);
    }

    [Fact]
    public async Task The_Recorded_State_Is_The_Stored_Row_Not_The_Feature_Held_In_Memory()
    {
        // The service does not write back computed tracking fields, so the in-memory
        // feature may be stale in more than just its object ID.
        stored = CreateSnapshot(8842, geometryJson: "{\"stored\":true}", layer: Child);
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Child, [Added(8842)]);
        await scope.CommitAsync();

        Assert.Equal("{\"stored\":true}", TheOnlyRecordedChange().After!.GeometryJson);
        Assert.Equal(8842, Assert.Single(asked).ObjectId);
    }

    [Fact]
    public async Task A_Row_That_Cannot_Be_Read_Back_Is_Not_Recorded()
    {
        missing = true;
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Child, [Added(8842)]);
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task An_Insert_Offline_Mode_Takes_The_Id_From_The_Feature()
    {
        // A local geodatabase assigns the final id during AddFeatureAsync, so it is already on the
        // feature and there is no ApplyEdits to report anything.
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(517));
        await scope.CommitAsync();

        Assert.Equal(517, TheOnlyRecordedChange().Identity.ObjectId);
    }

    [Fact]
    public async Task An_Insert_With_No_Id_At_All_Is_Not_Recorded()
    {
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWithNoId());
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Several_Inserts_Are_Matched_To_The_Results_In_Order()
    {
        using var scope = sut.Begin("Import Canopy Points");

        scope.MarkInsert(Child, FeatureWith(-1));
        scope.MarkInsert(Child, FeatureWith(-2));
        scope.ResolveInsertedObjectIds(Child, [Added(11), Added(22)]);
        await scope.CommitAsync();

        Assert.Equal([11, 22], journal.PeekUndo()!.Changes.Select(c => c.Identity.ObjectId));
    }

    [Fact]
    public async Task Results_For_Updates_And_Deletes_Are_Ignored_When_Matching()
    {
        // One ApplyEdits covers every pending edit on the table, so the list is mixed.
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Child,
        [
            new AppliedEdit(5, IsInsert: false, Failed: false),
            Added(8842),
            new AppliedEdit(6, IsInsert: false, Failed: false),
        ]);
        await scope.CommitAsync();

        Assert.Equal(8842, TheOnlyRecordedChange().Identity.ObjectId);
    }

    [Fact]
    public async Task When_The_Counts_Disagree_Nothing_Is_Recorded_Rather_Than_Guessed()
    {
        // Pairing is by order alone, so differing counts make any match a guess. A
        // wrong ID could send a later undo to the wrong row.
        using var scope = sut.Begin("Import Canopy Points");

        scope.MarkInsert(Child, FeatureWith(-1));
        scope.MarkInsert(Child, FeatureWith(-2));
        scope.ResolveInsertedObjectIds(Child, [Added(11)]);
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("cannot be matched"))), Times.Once);
    }

    [Fact]
    public async Task An_Add_The_Service_Rejected_Is_Not_Matched()
    {
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Child, [new AppliedEdit(0, IsInsert: true, Failed: true)]);
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Results_For_Another_Layer_Do_Not_Resolve_This_One()
    {
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(Placeholder));
        scope.ResolveInsertedObjectIds(Layer, [Added(8842)]);
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Each_Layer_Is_Resolved_From_Its_Own_Send()
    {
        // One scope can span several tables, and each is sent separately with its own result list.
        using var scope = sut.Begin("Create LPIS parcel");

        scope.MarkInsert(Layer, FeatureWith(-1));
        scope.MarkInsert(Child, FeatureWith(-2));
        scope.ResolveInsertedObjectIds(Layer, [Added(100)]);
        scope.ResolveInsertedObjectIds(Child, [Added(200)]);
        await scope.CommitAsync();

        Assert.Equal([100, 200], journal.PeekUndo()!.Changes.Select(c => c.Identity.ObjectId));
    }

    [Fact]
    public void A_Scope_Never_Committed_Records_Nothing()
    {
        using (var scope = sut.Begin("Add Canopy Point"))
        {
            scope.MarkInsert(Child, FeatureWith(517));
        }

        Assert.Equal(0, journal.UndoDepth);
    }

    [Fact]
    public async Task Committing_Twice_Records_Once_And_Says_So()
    {
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(517));
        await scope.CommitAsync();
        await scope.CommitAsync();

        Assert.Equal(1, journal.UndoDepth);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("more than once"))), Times.Once);
    }

    [Fact]
    public async Task Resolve_Inserted_ObjectIds_After_Committing_Says_So_Rather_Than_Doing_Nothing_Quietly()
    {
        // Calling these in the wrong order loses the ID mapping. A later warning would
        // then imply ResolveInsertedObjectIds was never called, when it was simply too late.
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(517));
        await scope.CommitAsync();
        scope.ResolveInsertedObjectIds(Child, [Added(8842)]);

        Assert.Equal(517, TheOnlyRecordedChange().Identity.ObjectId);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("only after it had been committed"))), Times.Once);
    }

    [Fact]
    public async Task A_Layer_Sent_Twice_Resolves_Each_Send_Against_What_It_Added()
    {
        // A table may be submitted more than once. Each call matches only inserts still
        // awaiting IDs, so counts are validated per submission.
        using var scope = sut.Begin("Import Canopy Points");

        scope.MarkInsert(Child, FeatureWith(-1));
        scope.ResolveInsertedObjectIds(Child, [Added(11)]);

        scope.MarkInsert(Child, FeatureWith(-2));
        scope.ResolveInsertedObjectIds(Child, [Added(22)]);

        await scope.CommitAsync();

        Assert.Equal([11, 22], journal.PeekUndo()!.Changes.Select(c => c.Identity.ObjectId));
    }

    [Fact]
    public async Task With_The_History_Off_Nothing_Is_Recorded_And_Nothing_Is_Read()
    {
        // Not merely unrecorded: no snapshot taken and no row read back, because paying nothing for a
        // feature that is switched off is the point of the switch.
        historyEnabled = false;
        using var scope = sut.Begin("Add Canopy Point");

        scope.MarkInsert(Child, FeatureWith(517));
        scope.ResolveInsertedObjectIds(Child, [Added(8842)]);
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
        Assert.Empty(asked);
        snapshotFactory.Verify(f => f.ReadObjectId(It.IsAny<Feature>()), Times.Never);
    }

    [Fact]
    public async Task With_The_History_Off_An_Update_Capture_Takes_No_Snapshot()
    {
        historyEnabled = false;
        using var scope = sut.Begin("Edit Vertex");

        await scope.CaptureUpdateBeforeAsync(Child, FeatureWithNoId());
        await scope.CommitAsync();

        Assert.Equal(0, journal.UndoDepth);
        snapshotFactory.Verify(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<Feature>()), Times.Never);
    }

    [Fact]
    public void Each_Begin_Is_Independent()
    {
        // Deliberately no ambient state. A shared-scope design allowed a commit failure
        // to stop recording for the rest of the session.
        Assert.NotSame(sut.Begin("One"), sut.Begin("Two"));
    }
}
