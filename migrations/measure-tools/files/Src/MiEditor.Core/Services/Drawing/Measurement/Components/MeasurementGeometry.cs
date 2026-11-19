using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Shared.Messages.Measurement;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>Pure geometry rules shared by the measure tools.</summary>
public static class MeasurementGeometry
{
    /// <summary>
    /// True when the geometry has at least <paramref name="minVertexCount"/> vertices in its first
    /// part. Null, empty and part-less geometries are never valid.
    /// </summary>
    public static bool HasMinimumVertices(Geometry? geometry, int minVertexCount)
    {
        if (geometry is null || geometry.IsEmpty) return false;

        var parts = geometry switch
        {
            Polyline line => line.Parts,
            Polygon polygon => polygon.Parts,
            _ => null
        };

        if (parts is null) return true;

        return parts.Count > 0 && parts[0].PointCount >= minVertexCount;
    }

    /// <summary>
    /// Geodesic length (and area, for polygons) in metres and square metres. Lines and polygons
    /// always produce a reading, zero while too few vertices exist, so the live panel never keeps
    /// showing a stale value after the user undoes vertices. Null only for null geometry or types
    /// that have no length.
    /// </summary>
    public static DrawingProgressMessage? Measure(Geometry? geometry)
    {
        switch (geometry)
        {
            case Polyline line:
                return new DrawingProgressMessage(
                    line.IsEmpty ? 0 : Finite(line.LengthGeodetic(LinearUnits.Meters, GeodeticCurveType.Geodesic)),
                    null);

            case Polygon polygon:
                return polygon.IsEmpty
                    ? new DrawingProgressMessage(0, 0)
                    : new DrawingProgressMessage(
                        Finite(polygon.LengthGeodetic(LinearUnits.Meters, GeodeticCurveType.Geodesic)),
                        Finite(polygon.AreaGeodetic(AreaUnits.SquareMeters, GeodeticCurveType.Geodesic)));

            default:
                return null;
        }
    }

    /// <summary>The zero reading for a sketch of <paramref name="geometryType"/> with no vertices.</summary>
    public static DrawingProgressMessage Empty(GeometryType geometryType) =>
        new(0, geometryType == GeometryType.Polygon ? 0 : null);

    private static double Finite(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}
