using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors.StandardTools;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

public class UndoRedoToolDescriptorTests
{
    private readonly UndoToolDescriptor undo = new();
    private readonly RedoToolDescriptor redo = new();

    [Fact]
    public void BothSitOnTheStandardToolbar()
    {
        Assert.Equal(ToolbarId.Standard, undo.ToolbarId);
        Assert.Equal(ToolbarId.Standard, redo.ToolbarId);
    }

    [Fact]
    public void BothAreOneShotActionsRatherThanStickyModes()
    {
        // A mode would stay highlighted and would take part in the single active tool rule,
        // which is wrong for a button that performs something and finishes.
        Assert.Equal(ToolActivation.Action, undo.Activation);
        Assert.Equal(ToolActivation.Action, redo.Activation);
    }

    [Fact]
    public void TheyFollowTheViewHistoryButtonsInOrder()
    {
        Assert.Equal(6, undo.Order);
        Assert.Equal(7, redo.Order);
    }

    [Fact]
    public void BothShowOnEveryGeometryType()
    {
        Assert.Equal(GeometryScopes.All, undo.Geometries);
        Assert.Equal(GeometryScopes.All, redo.Geometries);
    }

    [Fact]
    public void NeitherBelongsToAFlyoutGroup()
    {
        Assert.Null(undo.Group);
        Assert.Null(redo.Group);
    }

    [Fact]
    public void EachCarriesItsOwnTool()
    {
        Assert.Equal(StandardTool.Undo, undo.Tool);
        Assert.Equal(StandardTool.Redo, redo.Tool);
    }

    [Fact]
    public void TooltipsNameTheAction()
    {
        Assert.Equal("Undo", undo.Tooltip);
        Assert.Equal("Redo", redo.Tooltip);
    }

    [Fact]
    public void EachNamesAnIcon()
    {
        // Placeholder artwork for now, so this asserts only that an icon is named. The two
        // currently borrow the view history arrows, which is recorded in the descriptors.
        Assert.False(string.IsNullOrWhiteSpace(undo.Icon));
        Assert.False(string.IsNullOrWhiteSpace(redo.Icon));
    }

    [Theory]
    [InlineData(typeof(UndoToolDescriptor))]
    [InlineData(typeof(RedoToolDescriptor))]
    public void BothRegisterThemselvesForDiscovery(Type descriptor)
    {
        // The registry is built from every IToolDescriptor the scanner finds, so a missing
        // attribute means the button simply never appears.
        var attribute = descriptor.GetCustomAttributes(typeof(RegisterServiceAttribute), false)
            .Cast<RegisterServiceAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(ServiceLifetime.Singleton, attribute!.Lifetime);
        Assert.Contains(typeof(IToolDescriptor), attribute.As!);
    }
}
