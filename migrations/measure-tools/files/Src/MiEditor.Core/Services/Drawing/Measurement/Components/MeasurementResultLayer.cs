using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>
/// Owns the single result graphic a measurement shows on an overlay. It only ever removes the
/// graphic it added itself, so it never disturbs anything else on the overlay.
/// </summary>
public sealed class MeasurementResultLayer(GraphicsOverlay overlay)
{
    private Graphic? current;

    public bool HasResult => current is not null;

    /// <summary>Replaces the current result. Null or empty geometries just clear it.</summary>
    public void Show(Geometry? geometry, Symbol symbol)
    {
        Clear();

        if (geometry is null || geometry.IsEmpty) return;

        current = new Graphic(geometry, symbol);
        overlay.Graphics.Add(current);
    }

    public void Clear()
    {
        if (current is null) return;

        var graphic = current;
        current = null;
        overlay.Graphics.Remove(graphic);
    }
}
