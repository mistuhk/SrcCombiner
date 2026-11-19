using WG.MiEditor.Shared.Models.Toolbar;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Toolbar;

// "No tool selected" is represented by a default tool value (e.g. AoiControl resets with
// ActiveTool = new()). Its Name is null, and GetHashCode used to throw NullReferenceException,
// so putting one in a dictionary or HashSet crashed.
public class ToolDefaultValueTests
{
    [Fact]
    public void DefaultTools_CanBeHashed_AndPrinted()
    {
        AssertSafe(new AoiTool());
        AssertSafe(new MeasureTool());
        AssertSafe(new EditingTool());
        AssertSafe(new StandardTool());
    }

    [Fact]
    public void DefaultTool_EqualsOnlyItself()
    {
        Assert.Equal(new AoiTool(), new AoiTool());
        Assert.NotEqual(new AoiTool(), AoiTool.DrawAOI);
        Assert.Equal(new AoiTool().GetHashCode(), new AoiTool().GetHashCode());
    }

    [Fact]
    public void DefaultTool_WorksAsASetMember()
    {
        var set = new HashSet<AoiTool> { new(), AoiTool.DrawAOI };

        Assert.Contains(new AoiTool(), set);
        Assert.Contains(AoiTool.DrawAOI, set);
    }

    [Fact]
    public void NamedTools_StillHashByName()
    {
        Assert.Equal(AoiTool.DrawAOI.Name.GetHashCode(), AoiTool.DrawAOI.GetHashCode());
        Assert.Equal("DrawAOI", AoiTool.DrawAOI.ToString());
    }

    private static void AssertSafe<T>(T tool) where T : struct
    {
        _ = tool.GetHashCode();
        Assert.Equal(string.Empty, tool.ToString());
    }
}
