using WG.MiEditor.Core.Services.Drawing.Measurement.Components;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class CleanupStepsTests
{
    [Fact]
    public void RunAll_RunsEveryStep_WhenNoneFail()
    {
        var ran = new List<int>();

        CleanupSteps.RunAll(() => ran.Add(1), () => ran.Add(2), () => ran.Add(3));

        Assert.Equal([1, 2, 3], ran);
    }

    [Fact]
    public void RunAll_RunsLaterSteps_AfterAFailure_AndRethrowsIt()
    {
        var ran = new List<int>();

        var ex = Assert.Throws<InvalidOperationException>(() => CleanupSteps.RunAll(
            () => ran.Add(1),
            () => throw new InvalidOperationException("first"),
            () => ran.Add(3)));

        Assert.Equal("first", ex.Message);
        Assert.Equal([1, 3], ran);
    }

    [Fact]
    public void RunAll_AggregatesMultipleFailures()
    {
        var ex = Assert.Throws<AggregateException>(() => CleanupSteps.RunAll(
            () => throw new InvalidOperationException("a"),
            () => throw new ArgumentException("b")));

        Assert.Equal(2, ex.InnerExceptions.Count);
    }
}
