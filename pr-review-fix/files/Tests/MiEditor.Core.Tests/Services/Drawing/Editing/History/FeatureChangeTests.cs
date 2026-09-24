using WG.MiEditor.Core.Services.Drawing.Editing.History;
using static WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.EditHistoryTestData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

public class FeatureChangeTests
{
    [Fact]
    public void Insert_InvertsToDeleteOfTheInsertedRow()
    {
        var after = CreateSnapshot(5, "500");

        var inverse = FeatureChange.Insert(after).Inverse();

        Assert.Equal(FeatureChangeKind.Delete, inverse.Kind);
        Assert.Same(after, inverse.Before);
        Assert.Null(inverse.After);
    }

    [Fact]
    public void Delete_InvertsToInsertOfTheCapturedRow()
    {
        var before = CreateSnapshot(5, "500");

        var inverse = FeatureChange.Delete(before).Inverse();

        Assert.Equal(FeatureChangeKind.Insert, inverse.Kind);
        Assert.Same(before, inverse.After);
        Assert.Null(inverse.Before);
    }

    [Fact]
    public void Update_InvertsToUpdateBackToThePreviousState()
    {
        var before = CreateSnapshot(5, "500", geometryJson: "before");
        var after = CreateSnapshot(5, "500", geometryJson: "after");

        var inverse = FeatureChange.Update(before, after).Inverse();

        Assert.Equal(FeatureChangeKind.Update, inverse.Kind);
        Assert.Same(after, inverse.Before);
        Assert.Same(before, inverse.After);
    }

    [Fact]
    public void Inverse_AppliedTwice_ReturnsTheOriginalChange()
    {
        var original = FeatureChange.Update(CreateSnapshot(1, "100", geometryJson: "a"), CreateSnapshot(1, "100", geometryJson: "b"));

        var roundTripped = original.Inverse().Inverse();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void InverseChanges_AreAppliedInReverseOrderSoChildrenGoBeforeParents()
    {
        var parent = FeatureChange.Insert(CreateSnapshot(1, "100"));
        var childA = FeatureChange.Insert(CreateSnapshot(2, "100", layer: "LPIS_CRNs"));
        var childB = FeatureChange.Insert(CreateSnapshot(3, "100", layer: "LPIS_CRNs"));

        var inverse = CreateEntry(changes: [parent, childA, childB]).InverseChanges();

        Assert.Equal(3, inverse.Count);
        Assert.Equal(3, inverse[0].Identity.ObjectId);
        Assert.Equal(2, inverse[1].Identity.ObjectId);
        Assert.Equal(1, inverse[2].Identity.ObjectId);
        Assert.All(inverse, change => Assert.Equal(FeatureChangeKind.Delete, change.Kind));
    }

    [Theory]
    [InlineData(FeatureChangeKind.Insert)]
    [InlineData(FeatureChangeKind.Delete)]
    [InlineData(FeatureChangeKind.Update)]
    public void AChangeCarryingTheWrongSnapshots_IsRefusedAtConstruction(FeatureChangeKind kind)
    {
        Assert.Throws<ArgumentException>(() => new FeatureChange(kind, null, null));
    }

    [Fact]
    public void AnInsertCarryingABeforeSnapshot_IsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new FeatureChange(FeatureChangeKind.Insert, CreateSnapshot(1, "100"), CreateSnapshot(1, "100")));
    }

    [Fact]
    public void ADeleteCarryingAnAfterSnapshot_IsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new FeatureChange(FeatureChangeKind.Delete, CreateSnapshot(1, "100"), CreateSnapshot(1, "100")));
    }

    [Fact]
    public void WithSnapshots_KeepsTheKindAndValidatesTheResult()
    {
        var original = FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100"));
        var moved = original.WithSnapshots(CreateSnapshot(2, "100"), CreateSnapshot(2, "100"));

        Assert.Equal(FeatureChangeKind.Update, moved.Kind);
        Assert.Equal(2, moved.Identity.ObjectId);
    }

    [Fact]
    public void Reference_FallsBackToTheBeforeSnapshotForADelete()
    {
        var before = CreateSnapshot(9, "900");

        Assert.Same(before, FeatureChange.Delete(before).Reference);
    }
}
