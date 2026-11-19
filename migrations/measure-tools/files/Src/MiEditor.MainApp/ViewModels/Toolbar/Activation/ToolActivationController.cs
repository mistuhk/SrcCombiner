
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;

/// <summary>
/// Per toolbar collaborator that owns a toolbar's <see cref="Tools"/> collection and
/// its intra-toolbar rules: single selection, the mode versus action distinction, and
/// toggle off onreclick. A view model composes one of these rather than inheriting a
/// base class. Activation is routed to the toolbar's own backend through the dispatch
/// delegates, so the controller knows nothing about any operation service.
/// <para>
/// If the backend fails while activating a mode, the toolbar state is rolled back so the
/// UI never shows a tool as active when it is not running. Failures are passed to
/// <c>onError</c> when one is supplied; otherwise they propagate as before.
/// </para>
/// </summary>
/// <param name="coordinator">Enforces the cross-toolbar single active tool rule.</param>
/// <param name="onActivate">Routes a mode activation or fires an action to the toolbar's backend.</param>
/// <param name="onDeactivate">Routes a deactivation to the toolbar's backend.</param>
/// <param name="tools">Optional initial tool buttons.</param>
/// <param name="onError">Optional sink for backend failures (log and notify). When supplied, failures are handled and not rethrown.</param>
public sealed partial class ToolActivationController<T>(
    IToolActivationCoordinator coordinator,
    Func<T, Task> onActivate,
    Func<Task> onDeactivate,
    IEnumerable<IconButtonControlViewModel<T>>? tools = null,
    Action<Exception>? onError = null) : ObservableObject, IToolActivationController
    where T : ITool
{

    /// <summary>
    /// The tool buttons this toolbar renders. Owned by the controller.
    /// </summary>
    public ObservableCollection<IconButtonControlViewModel<T>> Tools { get; } = tools is null
            ? []
            : new ObservableCollection<IconButtonControlViewModel<T>>(tools);

    /// <summary>
    /// The currently active mode button, or null when no mode is active. Exposed so a
    /// flyout sub-toolbar host can derive an anchor icon from the active child.
    /// </summary>
    [ObservableProperty]
    public partial IconButtonControlViewModel<T>? ActiveTool { get; set; }

    /// <summary>
    /// Fire and forget entry for a button command, which is synchronous. The activation
    /// chain is awaited internally so this is not an async void; it routes to the tool
    /// overload by token, which the button supplies. Nothing escapes unobserved: any
    /// failure reaches <c>onError</c> when one is supplied.
    /// </summary>
    public void HandleSelected(T tool) => _ = HandleSelectedSafelyAsync(tool);

    /// <summary>Handles selection by tool token, resolving the owning button.</summary>
    public Task HandleSelectedAsync(T tool)
    {
        var button = Tools.FirstOrDefault(b => b.Tool.Equals(tool));
        return button is null ? Task.CompletedTask : HandleSelectedAsync(button);
    }

    /// <summary>
    /// Handles a tool button being selected. Actions fire and never set an active
    /// state. Modes stick: a new mode replaces the current one and toggling the active
    /// mode turns it off.
    /// </summary>
    public async Task HandleSelectedAsync(IconButtonControlViewModel<T> button)
    {
        if (button.Activation == ToolActivation.Action)
        {
            try
            {
                await onActivate(button.Tool);
            }
            catch (Exception ex) when (onError is not null)
            {
                onError(ex);
            }

            return;
        }

        if (ReferenceEquals(ActiveTool, button))
        {
            await ToggleOffAsync();
            return;
        }

        if (ActiveTool is not null)
        {
            ActiveTool.IsActive = false;
        }

        button.IsActive = true;
        ActiveTool = button;

        try
        {
            await coordinator.NotifyActivatedAsync(this);
            await onActivate(button.Tool);
        }
        catch (Exception ex)
        {
            await RollBackAsync(button);

            if (onError is null) throw;
            onError(ex);
        }
    }

    public async Task DeactivateExternallyAsync()
    {
        if (ActiveTool is null)
        {
            return;
        }

        ActiveTool.IsActive = false;
        ActiveTool = null;

        // A failure here must not break the other toolbar's activation that triggered it.
        try
        {
            await onDeactivate();
        }
        catch (Exception ex) when (onError is not null)
        {
            onError(ex);
        }
    }

    private async Task ToggleOffAsync()
    {
        if (ActiveTool is not null)
        {
            ActiveTool.IsActive = false;
            ActiveTool = null;
        }

        coordinator.NotifyDeactivated(this);

        try
        {
            await onDeactivate();
        }
        catch (Exception ex) when (onError is not null)
        {
            onError(ex);
        }
    }

    // Undo a failed activation: clear the active state, release the coordinator and give the
    // backend a chance to tear down anything it half started.
    private async Task RollBackAsync(IconButtonControlViewModel<T> button)
    {
        button.IsActive = false;
        if (ReferenceEquals(ActiveTool, button))
        {
            ActiveTool = null;
        }

        coordinator.NotifyDeactivated(this);

        try
        {
            await onDeactivate();
        }
        catch (Exception cleanupError)
        {
            onError?.Invoke(cleanupError);
        }
    }

    private async Task HandleSelectedSafelyAsync(T tool)
    {
        try
        {
            await HandleSelectedAsync(tool);
        }
        catch (Exception ex)
        {
            // Only reachable without an onError sink; the toolbar state has already been
            // rolled back. Matches the previous fire-and-forget behaviour.
            System.Diagnostics.Debug.WriteLine($"Tool activation failed: {ex}");
        }
    }
}
