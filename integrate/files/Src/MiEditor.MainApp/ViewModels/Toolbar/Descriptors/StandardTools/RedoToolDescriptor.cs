using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.StandardTools;

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IToolDescriptor) })]
public sealed class RedoToolDescriptor : IToolDescriptor
{
    public ToolbarId ToolbarId => ToolbarId.Standard;

    // Placeholder, as with UndoToolDescriptor. Replace with redo.png when it lands.
    public string Icon => "next_extent.png";

    public string Tooltip => "Redo";
    public int Order => 7;
    public GeometryScopes Geometries => GeometryScopes.All;
    public string? Group => null;
    public ToolActivation Activation => ToolActivation.Action;
    public ITool Tool => StandardTool.Redo;
}
