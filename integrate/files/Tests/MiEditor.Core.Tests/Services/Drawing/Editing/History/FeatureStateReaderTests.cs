using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Moq;
using WG.MiEditor.Core.Services;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Tests.Common.TestFactories;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

/// <summary>
/// The reader's own job is the where clause it builds and the batches it refuses, so that is what
/// these assert. Mapping a returned row into a snapshot belongs to FeatureSnapshotFactory, which
/// needs a real ServiceFeatureTable and is verified by running the app.
/// </summary>
public class FeatureStateReaderTests
{
    private const string Child = "Canopy Point";

    private readonly Mock<IFeatureLayerService> featureLayerService = new();
    private readonly FeatureStateReader sut;

    private string lastWhereClause = string.Empty;
    private int layerLookups;

    public FeatureStateReaderTests()
    {
        var layer = new FeatureLayer(ArcGisTestFactory.CreateTable()) { Name = Layer };

        featureLayerService
            .Setup(s => s.GetFeatureLayerAsync(It.IsAny<string>()))
            .Returns(() =>
            {
                layerLookups++;
                return Task.FromResult<FeatureLayer?>(LayerExists ? layer : null);
            });

        // Returning nothing found is enough: every assertion here is about the question asked,
        // not the answer. An ArcGISFeature cannot be constructed outside a real table anyway.
        featureLayerService
            .Setup(s => s.GetQueryFeaturesAsync(It.IsAny<FeatureLayer>(), It.IsAny<QueryParameters>()))
            .Returns((FeatureLayer _, QueryParameters query) =>
            {
                lastWhereClause = query.WhereClause;
                return Task.FromResult<List<ArcGISFeature>?>([]);
            });

        featureLayerService
            .Setup(s => s.GetArcGISFeatureAsync(It.IsAny<FeatureLayer>(), It.IsAny<QueryParameters>()))
            .Returns((FeatureLayer _, QueryParameters query) =>
            {
                lastWhereClause = query.WhereClause;
                return Task.FromResult<ArcGISFeature?>(null);
            });

        sut = new FeatureStateReader(
            featureLayerService.Object,
            Mock.Of<IFeatureSnapshotFactory>(),
            Mock.Of<ILoggerService>());
    }

    private bool LayerExists { get; set; } = true;

    // ---------------------------------------------------------------- the clause it builds

    [Fact]
    public async Task AKeyedBatch_IsQueriedOnTheBusinessKey()
    {
        await sut.ReadManyAsync(Layer, [CreateIdentity(1, "100"), CreateIdentity(2, "200")]);

        Assert.Equal("POLYGONID IN (100, 200)", lastWhereClause);
    }

    [Fact]
    public async Task AKeylessBatch_IsQueriedOnTheObjectId()
    {
        await sut.ReadManyAsync(Child, [CreateIdentity(7, layer: Child), CreateIdentity(8, layer: Child)]);

        Assert.Equal("OBJECTID IN (7, 8)", lastWhereClause);
    }

    [Fact]
    public async Task ANonNumericKey_IsQuotedAndItsApostrophesEscaped()
    {
        await sut.ReadManyAsync(Layer, [new FeatureIdentity(Layer, 1, "POLYGONID", "O'Brien")]);

        Assert.Equal("POLYGONID IN ('O''Brien')", lastWhereClause);
    }

    [Fact]
    public async Task EveryIdentityInAKeyedBatch_ReachesTheClause()
    {
        // The defect this guards: an earlier version filtered the batch while building the clause,
        // so some identities were never asked about and their absence read as somebody's deletion.
        var identities = Enumerable.Range(1, 5).Select(i => CreateIdentity(i, $"{i}00")).ToList();

        await sut.ReadManyAsync(Layer, identities);

        Assert.All(identities, i => Assert.Contains(i.BusinessKeyValue!, lastWhereClause));
    }

    // ---------------------------------------------------------------- what it refuses

    [Fact]
    public async Task ABatchMixingKeyedAndKeylessIdentities_IsRefused()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadManyAsync(Layer, [CreateIdentity(1, "100"), CreateIdentity(2)]));

        Assert.Contains("all keyed or all keyless", error.Message);
    }

    [Fact]
    public async Task ABatchMixingTwoBusinessKeyFields_IsRefused()
    {
        // Not reachable through FeatureIdentityPolicy today, which maps one field per layer. The
        // guard is here because the IN clause can only name the first field, and would silently
        // query the wrong column for the rest.
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadManyAsync(Layer,
            [
                new FeatureIdentity(Layer, 1, "POLYGONID", "100"),
                new FeatureIdentity(Layer, 2, "LPISPOLYID", "200"),
            ]));

        Assert.Contains("one business key field", error.Message);
    }

    [Fact]
    public async Task ABadBatch_IsRefusedBeforeTheLayerIsEvenLookedUp()
    {
        // A caller mistake must not be reported as whichever environment failure came first.
        LayerExists = false;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadManyAsync(Layer, [CreateIdentity(1, "100"), CreateIdentity(2)]));

        Assert.Equal(0, layerLookups);
    }

    [Fact]
    public async Task AMissingLayer_IsAFailureRatherThanAnEmptyResult()
    {
        // An empty result cannot be told apart from "somebody deleted these rows", and the
        // conflict detector would report exactly that to the officer.
        LayerExists = false;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadManyAsync(Layer, [CreateIdentity(1, "100")]));

        Assert.Contains(Layer, error.Message);
    }

    [Fact]
    public async Task AnEmptyBatch_AsksTheServiceNothing()
    {
        var result = await sut.ReadManyAsync(Layer, []);

        Assert.Empty(result);
        Assert.Equal(0, layerLookups);
    }

    // ---------------------------------------------------------------- the single row read

    [Fact]
    public async Task ASingleKeyedRow_IsQueriedOnTheBusinessKey()
    {
        await sut.ReadAsync(CreateIdentity(1, "100"));

        Assert.Equal("POLYGONID = 100", lastWhereClause);
    }

    [Fact]
    public async Task ASingleKeylessRow_IsQueriedOnTheObjectId()
    {
        await sut.ReadAsync(CreateIdentity(7, layer: Child));

        Assert.Equal("OBJECTID = 7", lastWhereClause);
    }

    [Fact]
    public async Task ASingleRowOnAMissingLayer_IsNullRatherThanAFailure()
    {
        // Deliberately unlike ReadManyAsync. The recorder calls this and wants to skip the row
        // rather than fail the save it is recording.
        LayerExists = false;

        Assert.Null(await sut.ReadAsync(CreateIdentity(1, "100")));
    }
}
