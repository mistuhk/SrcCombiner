using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.StandardTools;

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IToolDescriptor) })]
public sealed class UndoToolDescriptor : IToolDescriptor
{
    public ToolbarId ToolbarId => ToolbarId.Standard;

    // Placeholder. There is no undo artwork yet, so this borrows the back arrow the previous
    // view button uses. Replace with undo.png when the real icon lands; nothing else changes.
    public string Icon => "previous_extent.png";

    public string Tooltip => "Undo";
    public int Order => 6;
    public GeometryScopes Geometries => GeometryScopes.All;
    public string? Group => null;
    public ToolActivation Activation => ToolActivation.Action;
    public ITool Tool => StandardTool.Undo;
}
