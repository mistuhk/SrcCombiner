namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Clears whatever is selected on the map after the history has changed a row.
/// <para>
/// A one method seam over IFeatureLayerService, which is otherwise a wide ArcGIS aware
/// interface. The history needs one call from it, and depending on the whole thing would make
/// the undo orchestration untestable for no benefit. It also states the dependency honestly:
/// the history has no business knowing about layer collections.
/// </para>
/// </summary>
public interface IEditSelectionClearer
{
    Task ClearAsync();
}
