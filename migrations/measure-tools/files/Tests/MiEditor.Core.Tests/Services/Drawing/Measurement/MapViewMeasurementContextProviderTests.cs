using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.UI;
using Moq;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.MapData;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class MapViewMeasurementContextProviderTests
{
    [Fact]
    public async Task GetAsync_WhenMapIsNeverRegistered_ThrowsUnavailable_InsteadOfHanging()
    {
        var mapViewService = new Mock<IMapViewService>();
        mapViewService.Setup(s => s.GetMapViewAsync()).Returns(new TaskCompletionSource<MapView>().Task);

        var sut = new MapViewMeasurementContextProvider(mapViewService.Object, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<MeasurementUnavailableException>(() => sut.GetAsync());
    }

    [Fact]
    public async Task GetAsync_WhenMapIsNull_ThrowsUnavailable()
    {
        var mapViewService = new Mock<IMapViewService>();
        mapViewService.Setup(s => s.GetMapViewAsync()).Returns(Task.FromResult<MapView>(null!));

        var sut = new MapViewMeasurementContextProvider(mapViewService.Object);

        await Assert.ThrowsAsync<MeasurementUnavailableException>(() => sut.GetAsync());
    }

    [Fact]
    public void EnsureAttached_ReAddsAnOverlayThatWasCleared()
    {
        var overlays = new GraphicsOverlayCollection();
        var overlay = new GraphicsOverlay { Id = MapViewMeasurementContextProvider.OverlayId };

        MapViewMeasurementContextProvider.EnsureAttached(overlays, overlay);
        overlays.Clear(); // what ZoomToFeatureControl does
        MapViewMeasurementContextProvider.EnsureAttached(overlays, overlay);

        Assert.Single(overlays);
        Assert.Same(overlay, overlays[0]);
    }

    [Fact]
    public void EnsureAttached_DoesNotAddTwice()
    {
        var overlays = new GraphicsOverlayCollection();
        var overlay = new GraphicsOverlay();

        MapViewMeasurementContextProvider.EnsureAttached(overlays, overlay);
        MapViewMeasurementContextProvider.EnsureAttached(overlays, overlay);

        Assert.Single(overlays);
    }
}
