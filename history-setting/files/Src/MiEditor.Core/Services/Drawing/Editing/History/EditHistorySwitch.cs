using System.ComponentModel;
using WG.MiEditor.Core.Services.UserPreferences;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Configuration.AppSettings;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Whether the edit history is in use at all.
/// <para>
/// Lives at the root of the History folder rather than in one of its phases because it governs all of
/// them: recording consults it before capturing anything, the service before applying anything, and the
/// editing toolbar before showing the buttons.
/// </para>
/// </summary>
public interface IEditHistorySwitch
{
    /// <summary>Whether undo and redo are on, for this deployment and this user.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether this deployment offers the feature at all. Separate from IsEnabled because the settings
    /// panel needs it: with the feature withdrawn there is no choice to offer, so the History section is
    /// not shown rather than shown with a switch that does nothing.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Raised when the answer actually changes. Not raised when the user toggles their preference while
    /// the deployment has the feature unavailable, because the answer was, and remains, no.
    /// </summary>
    event EventHandler? Changed;
}

/// <summary>
/// Combines the two answers that decide whether the edit history is in use: whether this deployment
/// offers the feature, and whether this user wants it.
/// <para>
/// The deployment answer wins. A switch a user could override would not be a switch, and the reason for
/// having one is to be able to withdraw the feature estate wide without shipping new binaries, whether
/// during a rollout or because an undo posts a visible compensating edit that an audit may object to.
/// The user's stored preference is read, never written, so withdrawing the feature does not erase the
/// choice they had made and it comes back if the feature is offered again.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IEditHistorySwitch) })]
public sealed class EditHistorySwitch : IEditHistorySwitch
{
    private readonly EditHistoryInfo settings;
    private readonly IUserPreferenceService preferences;

    // What was last reported, so a preference change that cannot alter the answer stays silent.
    private bool reported;

    public EditHistorySwitch(
        EditHistoryInfo settings,
        IUserPreferenceService preferences,
        ILoggerService loggerService)
    {
        this.settings = settings;
        this.preferences = preferences;

        if (!settings.Available)
        {
            // Said once at startup, because "undo has disappeared" otherwise arrives as a support call
            // with nothing to point at.
            loggerService.Info(
                "Edit history: EditHistory.Available is false, so undo and redo are withdrawn in this deployment. "
                + "Nothing is recorded and the setting is not offered.");
        }

        reported = IsEnabled;

        // Both this and the preference service are singletons with the same lifetime, so there is
        // nothing to unsubscribe from and no leak to manage.
        preferences.PropertyChanged += OnPreferenceChanged;
    }

    /// <summary>
    /// Available in this deployment, and wanted by this user.
    /// </summary>
    public bool IsEnabled => IsAvailable && preferences.HistoryEnabled;

    public bool IsAvailable => settings.Available;

    public event EventHandler? Changed;

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IUserPreferenceService.HistoryEnabled))
        {
            return;
        }

        var current = IsEnabled;

        if (current == reported)
        {
            return;
        }

        reported = current;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
