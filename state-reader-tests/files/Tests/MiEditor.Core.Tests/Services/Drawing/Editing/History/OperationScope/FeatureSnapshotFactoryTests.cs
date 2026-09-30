
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.OperationScope;
using WG.MiEditor.Tests.Common.MockData;
using WG.MiEditor.Tests.Common.TestFactories;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.OperationScope;

public class FeatureSnapshotFactoryTests
{
    private readonly FeatureSnapshotFactory sut =
        new(new FeatureIdentityPolicy(AppSettingsMockData.CreateAppSettings().Layers));

    [Fact]
    public void A_Feature_Not_Yet_Added_To_A_Table_Has_No_ObjectId()
    {
        var feature = ArcGisTestFactory.CreateTable().CreateFeature();

        Assert.Equal(0, sut.ReadObjectId(feature));
    }

    [Fact]
    public async Task A_Local_Table_Assigns_A_Positive_ObjectId_On_Add()
    {
        var table = ArcGisTestFactory.CreateTable();
        var feature = table.CreateFeature();

        await table.AddFeatureAsync(feature);

        Assert.True(sut.ReadObjectId(feature) > 0, "A local table must assign a positive object id.");
    }

    [Fact]
    public async Task A_Snapshots_Attributes_Are_Found_Whatever_The_Casing()
    {
        // Attribute name casing varies across this codebase, and the applier looks values up by
        // name when writing a row back, so a case sensitive snapshot would silently drop fields.
        var table = ArcGisTestFactory.CreateTable();
        var feature = table.CreateFeature();
        feature.SetAttributeValue("Name", "Parcel A");

        var snapshot = await sut.CreateAsync("LPIS_Poly", feature);

        Assert.Equal("Parcel A", snapshot.Attributes["NAME"]);
        Assert.Equal("Parcel A", snapshot.Attributes["name"]);
    }
}
