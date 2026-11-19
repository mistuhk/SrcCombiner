

using Esri.ArcGISRuntime.Geometry;
using WG.MiEditor.Models;

namespace WG.MiEditor.Messages.Aoi
{
    public sealed record DefineAoiMessage(bool IsEnable);
    public sealed record DefineAutoAoiMessage();
    public sealed record DefineManulAoiMessage();
    public sealed record SaveAoiMessage();
    public sealed record ClearGraphicsOverlayMessage(bool IsClearEnable = true);
    public sealed record AoiGeometryHighlightMessage(Geometry Geometry);
    public sealed record ResetAoiToolbarActiveStateMessage();
    public sealed record DeleteManualAoiMessage();

    /// <summary>
    /// The AOI toolbar's active mode was turned off, either by the user or because another
    /// toolbar (for example a measure tool) took over. AOI must stop drawing and stop
    /// listening to map taps so it cannot interfere with the tool that took over.
    /// </summary>
    public sealed record StopAoiInteractionMessage();
}
