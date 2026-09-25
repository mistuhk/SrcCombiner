using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Tests.Common.MockData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class FeatureIdentityPolicyTests
{
    private readonly Layers layers = CreateLayers();
    private readonly FeatureIdentityPolicy sut;

    public FeatureIdentityPolicyTests() => sut = new FeatureIdentityPolicy(layers);

    [Fact]
    public void LpisParcel_UsesPolygonIdAsItsBusinessKey()
    {
        Assert.Equal("POLYGONID", sut.BusinessKeyField(layers.LPIS_Poly));
    }

    [Fact]
    public void LpisParcel_StampsLatestModNotModifiedDate()
    {
        Assert.Equal("LATESTMOD", sut.StampField(layers.LPIS_Poly));
    }

    [Fact]
    public void OtherLayers_StampModifiedDate()
    {
        Assert.Equal("MODIFIEDDT", sut.StampField(layers.Canopy_Point));
    }

    [Fact]
    public void ChildTablesOfAParcel_HaveNoBusinessKey()
    {
        // POLYGONID on these layers references the parent parcel and repeats across many
        // rows, so it identifies a parcel rather than a row.
        Assert.Null(sut.BusinessKeyField(layers.LPIS_CRNs));
        Assert.Null(sut.BusinessKeyField(layers.Found_Info));
    }

    [Fact]
    public void DeductionAndCanopyLayers_HaveNoBusinessKey()
    {
        // LPISPOLYID is a parent reference assigned from the containing parcel, so many
        // canopy points share one value. These layers rely on object id remapping.
        Assert.Null(sut.BusinessKeyField(layers.Canopy_Point));
        Assert.Null(sut.BusinessKeyField(layers.Permanent_Deduction));
        Assert.Null(sut.BusinessKeyField(layers.Canopy_Poly));
    }

    [Fact]
    public void UnknownLayer_HasNoBusinessKeyRatherThanAGuessedOne()
    {
        Assert.Null(sut.BusinessKeyField("Some_Unmapped_Layer"));
    }

    [Fact]
    public void LayerMatching_IsCaseInsensitive()
    {
        Assert.Equal("POLYGONID", sut.BusinessKeyField(layers.LPIS_Poly.ToUpperInvariant()));
    }

    [Fact]
    public void UnconfiguredLayerName_DoesNotMatchEverything()
    {
        var blank = AppSettingsMockData.CreateAppSettings().Layers;

        Assert.Null(new FeatureIdentityPolicy(blank).BusinessKeyField(string.Empty));
    }

    private static Layers CreateLayers()
    {
        var layers = AppSettingsMockData.CreateAppSettings().Layers;

        layers.LPIS_Poly = "LPIS_Poly";
        layers.LPIS_CRNs = "LPIS_CRNs";
        layers.Found_Info = "Found_Info";
        layers.DeclaredInfo = "DeclaredInfo";
        layers.Canopy_Point = "Canopy_Point";
        layers.Canopy_Poly = "Canopy_Poly";
        layers.Permanent_Deduction = "Permanent_Deduction";
        layers.Tech_Perm_Ddn = "Tech_Perm_Ddn";
        layers.Published_Habitat = "Published_Habitat";

        return layers;
    }
}
