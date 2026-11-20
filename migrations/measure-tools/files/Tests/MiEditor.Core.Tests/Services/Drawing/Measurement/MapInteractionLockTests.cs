using Esri.ArcGISRuntime.UI;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class MapInteractionLockTests
{
    private readonly MapInteractionLock sut = new();
    private MapViewInteractionOptions? options;

    private void Lock() => sut.Lock(() => options, value => options = value);

    [Fact]
    public void Lock_DisablesPanAndZoom()
    {
        options = new MapViewInteractionOptions { IsPanEnabled = true, IsZoomEnabled = true };

        Lock();

        Assert.True(sut.IsLocked);
        Assert.False(options!.IsPanEnabled);
        Assert.False(options.IsZoomEnabled);
    }

    [Fact]
    public void Release_RestoresTheOriginalOptions()
    {
        var original = new MapViewInteractionOptions { IsPanEnabled = true, IsZoomEnabled = true };
        options = original;

        Lock();
        sut.Release();

        Assert.False(sut.IsLocked);
        Assert.Same(original, options);
    }

    [Fact]
    public void Release_RestoresNull_WhenTheMapHadNoExplicitOptions()
    {
        options = null;

        Lock();
        sut.Release();

        Assert.Null(options);
    }

    [Fact]
    public void LockTwice_KeepsTheFirstOriginal()
    {
        var original = new MapViewInteractionOptions();
        options = original;

        Lock();
        Lock();
        sut.Release();

        Assert.Same(original, options);
    }

    [Fact]
    public void Release_WithoutLock_DoesNothing()
    {
        var original = new MapViewInteractionOptions();
        options = original;

        sut.Release();

        Assert.Same(original, options);
    }

    // Regression: the old base class captured options once at start-up and nulled them on stop,
    // so the second session restored null and pan/zoom could be left disabled.
    [Fact]
    public void SecondSession_RestoresTheOptionsInForceWhenItStarted()
    {
        options = new MapViewInteractionOptions();
        Lock();
        sut.Release();

        var changedInBetween = new MapViewInteractionOptions { IsRotateEnabled = false };
        options = changedInBetween;

        Lock();
        sut.Release();

        Assert.Same(changedInBetween, options);
    }

    [Fact]
    public void Release_WhenRestoreThrows_DoesNotStayLocked()
    {
        var shouldThrow = false;
        sut.Lock(() => null, _ => { if (shouldThrow) throw new InvalidOperationException(); });
        shouldThrow = true;

        Assert.Throws<InvalidOperationException>(sut.Release);
        Assert.False(sut.IsLocked);
    }
}
