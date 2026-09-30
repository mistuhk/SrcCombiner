
using System.ComponentModel;
using WG.MiEditor.Core.Services.Themes;

namespace WG.MiEditor.Core.Services.UserPreferences;

/// <summary>
/// Manages loading and saving user specific application preferences.
/// </summary>
public interface IUserPreferenceService : INotifyPropertyChanged
{
    /// <summary>
    /// Gets or sets a value indicating whether the Guided Task
    /// assistant should be shown.
    /// </summary>
    bool ShowFeatureGuide { get; set; }

    /// <summary>
    /// Gets or sets the theme chosen by the user (Light, Dark or Auto).
    /// </summary>
    AppThemeChoice ThemeChoice { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user wants undo and redo.
    /// <para>
    /// Off by default. Read together with EditHistoryInfo.Available, which can withdraw the feature for
    /// the whole deployment and takes precedence; see IEditHistorySwitch, which is what everything else
    /// consults rather than reading either of these directly.
    /// </para>
    /// </summary>
    bool HistoryEnabled { get; set; }
}
