using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.UI;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>
/// Turns map pan and zoom off for the lifetime of a measurement and puts back exactly what was
/// there before. The original options are captured on <see cref="Lock(MapView)"/>, not once at
/// start-up, so a release always restores the options in force when this lock was taken.
/// Lock and release are both idempotent.
/// </summary>
public sealed class MapInteractionLock
{
    private Action? restore;

    public bool IsLocked => restore is not null;

    public void Lock(MapView mapView) =>
        Lock(() => mapView.InteractionOptions, options => mapView.InteractionOptions = options);

    /// <summary>Accessor-based overload; lets the lock be exercised without a live map view.</summary>
    public void Lock(Func<MapViewInteractionOptions?> getOptions, Action<MapViewInteractionOptions?> setOptions)
    {
        if (IsLocked) return;

        var original = getOptions();
        setOptions(new MapViewInteractionOptions { IsPanEnabled = false, IsZoomEnabled = false });
        restore = () => setOptions(original);
    }

    public void Release()
    {
        if (restore is null) return;

        // Clear first so a failing restore cannot leave the lock stuck in the locked state.
        var pending = restore;
        restore = null;
        pending();
    }
}
