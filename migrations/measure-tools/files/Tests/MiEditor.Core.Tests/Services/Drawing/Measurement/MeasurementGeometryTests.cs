using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class MeasurementGeometryTests
{
    private static readonly SpatialReference Wgs84 = SpatialReferences.Wgs84;

    private static Polyline Line(int points) =>
        new(Enumerable.Range(0, points).Select(i => new MapPoint(-3.2 + i * 0.01, 55.95, Wgs84)), Wgs84);

    private static Polygon Square(int points) =>
        new(new[]
        {
            new MapPoint(-3.20, 55.95, Wgs84),
            new MapPoint(-3.19, 55.95, Wgs84),
            new MapPoint(-3.19, 55.96, Wgs84),
            new MapPoint(-3.20, 55.96, Wgs84)
        }.Take(points), Wgs84);

    [Fact]
    public void HasMinimumVertices_NullOrEmpty_IsFalse()
    {
        Assert.False(MeasurementGeometry.HasMinimumVertices(null, 2));
        Assert.False(MeasurementGeometry.HasMinimumVertices(new Polyline(Array.Empty<MapPoint>(), Wgs84), 2));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void HasMinimumVertices_Polyline_NeedsTwoPoints(int points, bool expected)
    {
        Assert.Equal(expected, MeasurementGeometry.HasMinimumVertices(Line(points), 2));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void HasMinimumVertices_Polygon_NeedsThreePoints(int points, bool expected)
    {
        Assert.Equal(expected, MeasurementGeometry.HasMinimumVertices(Square(points), 3));
    }

    [Fact]
    public void Measure_Polyline_ReportsLengthOnly()
    {
        var progress = MeasurementGeometry.Measure(Line(2));

        Assert.NotNull(progress);
        Assert.True(progress!.Length > 0);
        Assert.Null(progress.Area);
    }

    [Fact]
    public void Measure_Polygon_ReportsLengthAndArea()
    {
        var progress = MeasurementGeometry.Measure(Square(4));

        Assert.NotNull(progress);
        Assert.True(progress!.Length > 0);
        Assert.True(progress.Area > 0);
    }

    [Fact]
    public void Measure_Null_ReturnsNull()
    {
        Assert.Null(MeasurementGeometry.Measure(null));
    }

    // Regression: readings used to be suppressed at zero, so undoing vertices left a stale value.
    [Fact]
    public void Measure_TooFewVertices_ReportsZero_NotNothing()
    {
        var line = MeasurementGeometry.Measure(Line(1));
        Assert.NotNull(line);
        Assert.Equal(0, line!.Length);

        var polygon = MeasurementGeometry.Measure(new Polygon(Array.Empty<MapPoint>(), Wgs84));
        Assert.NotNull(polygon);
        Assert.Equal(0, polygon!.Length);
        Assert.Equal(0, polygon.Area);
    }

    [Fact]
    public void Empty_HasNoAreaForLines_AndZeroAreaForPolygons()
    {
        Assert.Null(MeasurementGeometry.Empty(GeometryType.Polyline).Area);
        Assert.Equal(0, MeasurementGeometry.Empty(GeometryType.Polygon).Area);
        Assert.Equal(0, MeasurementGeometry.Empty(GeometryType.Polyline).Length);
    }
}
