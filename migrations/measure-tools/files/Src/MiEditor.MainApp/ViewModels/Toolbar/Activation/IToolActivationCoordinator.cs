
namespace WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;

/// <summary>
/// Application-level service that enforces the cross-toolbar invariant: at most one
/// toolbar holds an active tool at any time. It holds a reference to the currently
/// active controller only, deactivating the previous holder when a new one activates.
/// </summary>
public interface IToolActivationCoordinator
{
    /// <summary>
    /// Records the given controller as the active one, deactivating the previously
    /// active controller first when it is a different controller.
    /// </summary>
    Task NotifyActivatedAsync(IToolActivationController controller);

    /// <summary>
    /// Clears the active reference when the given controller is the active one, for
    /// example after a toggle-off.
    /// </summary>
    void NotifyDeactivated(IToolActivationController controller);

    /// <summary>
    /// Deactivates the active controller when it is not <paramref name="requester"/>. For
    /// momentary actions that still take over map input (for example AOI delete, which
    /// listens for a tap) and so must not run alongside another toolbar's mode.
    /// </summary>
    Task DeactivateOthersAsync(IToolActivationController requester);
}
