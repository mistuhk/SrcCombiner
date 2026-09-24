using WG.MiEditor.Core.Services.Drawing.Editing.History;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class FeatureIdentityTests
{
    [Fact]
    public void RowsWithTheSameBusinessKey_AreTheSameRowEvenAfterTheObjectIdChanges()
    {
        var deleted = new FeatureIdentity("LPIS_Poly", 42, "POLYGONID", "10432");
        var restored = new FeatureIdentity("LPIS_Poly", 87, "POLYGONID", "10432");

        Assert.True(deleted.SameRowAs(restored));
    }

    [Fact]
    public void WithoutABusinessKey_RowsAreDistinguishedByObjectId()
    {
        var first = new FeatureIdentity("Canopy_Point", 501);
        var second = new FeatureIdentity("Canopy_Point", 502);

        Assert.False(first.SameRowAs(second));
        Assert.True(first.SameRowAs(new FeatureIdentity("Canopy_Point", 501)));
    }

    [Fact]
    public void RowsOnDifferentLayers_AreNeverTheSameRow()
    {
        var parcel = new FeatureIdentity("LPIS_Poly", 42, "POLYGONID", "10432");
        var crn = new FeatureIdentity("LPIS_CRNs", 42, "POLYGONID", "10432");

        Assert.False(parcel.SameRowAs(crn));
    }

    [Fact]
    public void AForeignKeyWouldCollapseDistinctRows()
    {
        // This is why FeatureIdentityPolicy hands out a business key only for LPIS_Poly.
        // If a parent reference such as LPISPOLYID were used as the key, these two separate
        // canopy points would compare equal and one undo would clear both from the history.
        var pointA = new FeatureIdentity("Canopy_Point", 501, "LPISPOLYID", "10432");
        var pointB = new FeatureIdentity("Canopy_Point", 502, "LPISPOLYID", "10432");

        Assert.True(pointA.SameRowAs(pointB));
    }

    [Fact]
    public void MixedKeyPresence_FallsBackToObjectId()
    {
        var keyed = new FeatureIdentity("LPIS_Poly", 42, "POLYGONID", "10432");
        var unkeyed = new FeatureIdentity("LPIS_Poly", 42);

        Assert.True(keyed.SameRowAs(unkeyed));
    }
}
