
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;

/// <summary>
/// The single owner of the cross toolbar single active tool invariant. It holds one
/// swapped reference to the active controller rather than a collection, so there is no
/// lifetime concern beyond a controller clearing itself when it deactivates.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IToolActivationCoordinator) })]
public sealed class ToolActivationCoordinator : IToolActivationCoordinator
{
    private IToolActivationController? active;

    public async Task NotifyActivatedAsync(IToolActivationController controller)
    {
        if (active is not null && !ReferenceEquals(active, controller))
        {
            await active.DeactivateExternallyAsync();
        }

        active = controller;
    }

    public async Task DeactivateOthersAsync(IToolActivationController requester)
    {
        if (active is null || ReferenceEquals(active, requester))
        {
            return;
        }

        var previous = active;
        active = null;
        await previous.DeactivateExternallyAsync();
    }

    public void NotifyDeactivated(IToolActivationController controller)
    {
        if (ReferenceEquals(active, controller))
        {
            active = null;
        }
    }
}
