
using CommunityToolkit.Mvvm.ComponentModel;
using WG.MiEditor.Core.Services.Themes;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.UserPreferences;

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IUserPreferenceService) })]
public partial class UserPreferenceService : ObservableObject, IUserPreferenceService
{
    private readonly IPreferences preferences;

    [ObservableProperty]
    private bool showFeatureGuide;

    [ObservableProperty]
    private AppThemeChoice themeChoice;

    [ObservableProperty]
    private bool historyEnabled;

    public UserPreferenceService(IPreferences preferences)
    {
        this.preferences = preferences;

        ShowFeatureGuide = preferences.Get(nameof(ShowFeatureGuide), true);
        ThemeChoice = (AppThemeChoice)preferences.Get(nameof(ThemeChoice), (int)AppThemeChoice.Auto);
        HistoryEnabled = preferences.Get(nameof(HistoryEnabled), false);
    }

    partial void OnShowFeatureGuideChanged(bool value) => preferences.Set(nameof(ShowFeatureGuide), value);

    partial void OnThemeChoiceChanged(AppThemeChoice value) => preferences.Set(nameof(ThemeChoice), (int)value);

    partial void OnHistoryEnabledChanged(bool value) => preferences.Set(nameof(HistoryEnabled), value);
}
