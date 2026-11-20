
using Esri.ArcGISRuntime.Maui;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.MapData;

public interface IMapViewService
{
    void Register(MapView mapView);
    Task<MapView> GetMapViewAsync();
}

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMapViewService) })]
public class MapViewService : IMapViewService
{
    private readonly TaskCompletionSource<MapView> _mapViewTaskCompletionSource = new();

    public Task<MapView> GetMapViewAsync() => _mapViewTaskCompletionSource.Task;

    public void Register(MapView mapView) => _mapViewTaskCompletionSource.TrySetResult(mapView);
}
