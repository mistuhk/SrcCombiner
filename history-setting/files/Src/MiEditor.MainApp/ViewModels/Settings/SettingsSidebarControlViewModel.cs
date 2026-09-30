
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Mapping;
using System.Collections.ObjectModel;
using System.ComponentModel;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services;
using WG.MiEditor.Core.Services.Drawing.Editing;
using WG.MiEditor.Core.Services.Drawing.Editing.SplitFeature;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Themes;
using WG.MiEditor.Core.Services.UserPreferences;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Messages.LayerManager;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Settings;
using LayerItem = WG.MiEditor.Shared.Models.Settings.LayerItem;

namespace WG.MiEditor.MainApp.ViewModels.Settings;

[RegisterService(ServiceLifetime.Singleton)]
public partial class SettingsSidebarControlViewModel
    : ObservableRecipient,
    IRecipient<SelectedEditableLayerMessage>,
    IRecipient<SelectedCaseStartEditingMessage>,
    IRecipient<MapDataChangedMessage>,
    IRecipient<SelectedBufferSettingsMessage>,
    IRecipient<SelectedLayerMessage>,
    IRecipient<SelectedSplitOverlapSettingsMessage>,
    IRecipient<ExitCaseMessage>

{
    private readonly Layers layers;
    private readonly IEditingUtility editingUtility;
    private readonly IMapDataService mapService;
    private readonly IThemeService themeService;

    private readonly string infieldSketchPointLayerName;
    private readonly string infieldSketchLineLayerName;
    private readonly string infieldSketchPolyLayerName;

    private bool IsStartEditingOn = false;

    [ObservableProperty]
    private string? selectedLayer;

    public SettingsSidebarControlViewModel(
        IMessenger messenger,
        IUserPreferenceService preferences,
        IEditingUtility editingUtility,
        IMapDataService mapService,
        IBufferSettingsService bufferSettingsService,
        ISplitOverlapSettingsService splitOverlapSettingsService,
        ILpisSettingsService lpisSettingsService,
        IThemeService themeService,
        IEditHistorySwitch historySwitch,
        Layers layers)
        : base(messenger)
    {
        IsActive = true;

        this.layers = layers;
        this.editingUtility = editingUtility;
        this.mapService = mapService;

        this.themeService = themeService;
        this.themeService.ThemeChanged += OnThemeChanged;

        // The deployment can withdraw the feature, in which case there is no choice to offer and the
        // section is left out rather than shown with a switch that cannot do anything.
        IsHistoryAvailable = historySwitch.IsAvailable;
        IsHistoryEnabled = preferences.HistoryEnabled;

        this.Preferences = preferences;
        this.BufferSettingsService = bufferSettingsService;
        this.OverlapSettingsService = splitOverlapSettingsService;
        this.LpisSettingsService = lpisSettingsService;

        infieldSketchPointLayerName = layers.InFieldSketchPoint;
        infieldSketchLineLayerName = layers.InFieldSketchLine;
        infieldSketchPolyLayerName = layers.InFieldSketchPoly;
    }


    public ObservableCollection<LayerItem> DefaultLayers { get; set; } = [];
    public ObservableCollection<LayerItem> OptionalLayers { get; set; } = [];

    [ObservableProperty]
    private bool isGuideExpanded = true;

    [ObservableProperty]
    private bool isBufferExpanded = true;

    [ObservableProperty]
    private bool isSnappingExpanded = true;

    [ObservableProperty]
    private bool isOverlapExpanded = true;

    [ObservableProperty]
    private bool isTransparencyExpanded = true;

    [ObservableProperty]
    private bool isThemeExpanded = true;

    [ObservableProperty]
    private bool isHistoryExpanded = true;

    /// <summary>Whether this deployment offers undo and redo at all. Hides the whole section when not.</summary>
    [ObservableProperty]
    private bool isHistoryAvailable;

    /// <summary>The user's own choice, written straight through to the stored preference.</summary>
    [ObservableProperty]
    private bool isHistoryEnabled;

    partial void OnIsHistoryEnabledChanged(bool value) => Preferences.HistoryEnabled = value;

    [RelayCommand]
    private void ToggleHistory()
    {
        IsHistoryExpanded = !IsHistoryExpanded;
    }

    partial void OnIsSnappingEnabledChanged(bool value)
    {
        HandleSnappingToggle(value);
    }


    /// <summary>
    /// Whether the editing sections are shown, which is every section below Theme: History, Snapping,
    /// Guide, Buffer, Split overlap and LPIS transparency. They are available during an edit session
    /// only. Named for snapping until the History section was added beneath the same gate.
    /// </summary>
    [ObservableProperty]
    private bool isEditingSettingsVisible;

    [ObservableProperty]
    private bool isSnappingEnabled;

    public IUserPreferenceService Preferences { get; }

    public IBufferSettingsService BufferSettingsService { get; }

    public ISplitOverlapSettingsService OverlapSettingsService { get; }

    public ILpisSettingsService LpisSettingsService { get; }

    public bool IsLightSelected => themeService.CurrentChoice == AppThemeChoice.Light;

    public bool IsDarkSelected => themeService.CurrentChoice == AppThemeChoice.Dark;

    public bool IsAutoSelected => themeService.CurrentChoice == AppThemeChoice.Auto;

    public string FollowingSystemText => $"Following system: {themeService.ResolvedTheme}";

    [RelayCommand]
    private void SelectLightTheme() => themeService.SetTheme(AppThemeChoice.Light);

    [RelayCommand]
    private void SelectDarkTheme() => themeService.SetTheme(AppThemeChoice.Dark);

    [RelayCommand]
    private void SelectAutoTheme() => themeService.SetTheme(AppThemeChoice.Auto);

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshThemeSelection();

    private void RefreshThemeSelection()
    {
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsDarkSelected));
        OnPropertyChanged(nameof(IsAutoSelected));
        OnPropertyChanged(nameof(FollowingSystemText));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsThemeExpanded = !IsThemeExpanded;
    }

    [RelayCommand]
    private void ToggleGuide()
    {
        IsGuideExpanded = !IsGuideExpanded;
    }

    [RelayCommand]
    private void ToggleOverlap()
    {
        IsOverlapExpanded = !IsOverlapExpanded;
    }

    [RelayCommand]
    private void ToggleBuffer()
    {
        IsBufferExpanded = !IsBufferExpanded;
    }

    [RelayCommand]
    private void ToggleSnapping()
    {
        IsSnappingExpanded = !IsSnappingExpanded;
    }

    [RelayCommand]
    private void ToggleTransparency()
    {
        IsTransparencyExpanded = !IsTransparencyExpanded;
    }

    private void HandleSnappingToggle(bool isEnabled)
    {
        this.Messenger.Send(new ToggleSnapSettingMessage(isEnabled));
    }

    [RelayCommand]
    private void CloseSidebar() => this.Messenger.Send<ToggleSettingsSidebarMessage>();

    private void LoadOptionalLayers(IEnumerable<Layer> layers)
    {
        AddFeatureLayersRecursive(layers);
        SortLayersByName();
    }

    private void AddFeatureLayersRecursive(IEnumerable<Layer> layers)
    {
        foreach (var layer in layers)
        {
            switch (layer)
            {
                case GroupLayer groupLayer:
                    AddFeatureLayersRecursive(groupLayer.Layers);
                    break;

                case FeatureLayer featureLayer:
                    var item = new LayerItem
                    {
                        Name = featureLayer.Name,
                        IsSelected = false
                    };

                    item.PropertyChanged += OnLayerItemPropertyChanged;
                    OptionalLayers.Add(item);
                    break;
            }
        }
    }

    private void SortLayersByName()
    {
        var sorted = OptionalLayers.OrderBy(item => item.Name).ToList();
        OptionalLayers.Clear();

        foreach (var item in sorted)
        {
            OptionalLayers.Add(item);
        }
    }

    private void OnLayerItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerItem.IsSelected) && sender is LayerItem item)
        {
            if (item.IsSelected)
            {
                IsSnappingEnabled = true;
                this.Messenger.Send(new ToggleSnapSettingMessage(true));
                this.Messenger.Send(new SnappingLayersMessage(item));
            }
            else
            {
                this.Messenger.Send(new SnappingLayersMessage(item));
            }

            SyncLayers(item);
        }
    }

    private void SyncLayers(LayerItem item)
    {
        var operationalLayer = OptionalLayers.FirstOrDefault(layer => layer.Name == item.Name);
        if (operationalLayer != null)
        {
            operationalLayer.IsSelected = item.IsSelected;
        }

        var defaultLayer = DefaultLayers.FirstOrDefault(layer => layer.Name == item.Name);
        if (defaultLayer != null)
        {
            defaultLayer.IsSelected = item.IsSelected;
        }
    }

    private async Task LoadDefaultLayers()
    {
        DefaultLayers.Clear();

        ToggleEnableSnapping();

        var defaultLayerItemList = new List<LayerItem>
        {
            CreateLayerItem(layers.LPIS_Poly),
            CreateLayerItem(this.SelectedLayer!),
            CreateLayerItem(infieldSketchPointLayerName)
        };

        var selectedLayerType = await editingUtility.GetLayerGeometryTypeAsync(this.SelectedLayer!);

        if (string.Equals(selectedLayerType.ToString(), "POLYLINE", StringComparison.OrdinalIgnoreCase))
        {
            defaultLayerItemList.Add(CreateLayerItem(infieldSketchLineLayerName));
        }
        else if (!string.Equals(selectedLayerType.ToString(), "POINT", StringComparison.OrdinalIgnoreCase))
        {
            defaultLayerItemList.Add(CreateLayerItem(infieldSketchLineLayerName));
            defaultLayerItemList.Add(CreateLayerItem(infieldSketchPolyLayerName));
        }

        PopulateDefaultLayersForSnapping(defaultLayerItemList);
        UpdateOptionalLayersStatus(defaultLayerItemList);

    }

    private static LayerItem CreateLayerItem(string name) => new()
    {
        Name = name,
        IsSelected = true
    };

    private void ToggleEnableSnapping()
    {
        IsSnappingEnabled = true;
    }

    private void PopulateDefaultLayersForSnapping(List<LayerItem> layerItemList)
    {
        foreach (var item in layerItemList)
        {
            DefaultLayers.Add(item);
            item.PropertyChanged += OnLayerItemPropertyChanged;
            this.Messenger.Send(new SnappingLayersMessage(item));
        }
    }

    private void UpdateOptionalLayersStatus(List<LayerItem> defaultLayers)
    {
        foreach (var item in defaultLayers)
        {
            var layerName = OptionalLayers.FirstOrDefault(layer => layer.Name == item.Name);
            if (layerName != null)
            {
                layerName.IsSelected = item.IsSelected;
            }
        }
    }

    public void Receive(SelectedBufferSettingsMessage message)
    {
        if (message.IsEnable)
        {
            IsSnappingExpanded = false;
            IsGuideExpanded = false;
            IsOverlapExpanded = false;
            IsBufferExpanded = message.IsEnable;
            IsOverlapExpanded = false;
            IsTransparencyExpanded = false;
        }
        else
        {
            IsBufferExpanded = message.IsEnable;
            this.BufferSettingsService.SetDefaultBufferSettings(string.Empty);
        }
    }

    public void Receive(SelectedLayerMessage message)
    {
        _ = this.BufferSettingsService.SetDefaultBufferSettings(message.SelectedLayer);
    }

    public void Receive(SelectedSplitOverlapSettingsMessage message)
    {
        if (message.IsEnable)
        {
            IsSnappingExpanded = false;
            IsBufferExpanded = false;
            IsGuideExpanded = false;
            IsBufferExpanded = false;
            IsOverlapExpanded = message.IsEnable;
            IsTransparencyExpanded = false;
        }
        else
        {
            IsOverlapExpanded = message.IsEnable;
        }
    }

    public void Receive(ExitCaseMessage message)
    {
        IsStartEditingOn = false;
        IsEditingSettingsVisible = false;
        this.Messenger.Send(new ToggleSnapSettingMessage(false));
    }

    public async void Receive(MapDataChangedMessage message)
    {
        OptionalLayers.Clear();
        DefaultLayers.Clear();

        _ = LoadDefaultLayers();

        var map = await this.mapService.GetMapAsync();
        LoadOptionalLayers(map.OperationalLayers);

        if (IsStartEditingOn)
        {
            IsEditingSettingsVisible = true;
            this.Messenger.Send(new ToggleSnapSettingMessage(true));
        }
    }

    public void Receive(SelectedEditableLayerMessage message)
    {
        this.SelectedLayer = message!.LayerName!;
        _ = LoadDefaultLayers();
    }

    public async void Receive(SelectedCaseStartEditingMessage message)
    {
        IsStartEditingOn = true;
        OptionalLayers.Clear();
        DefaultLayers.Clear();

        IsEditingSettingsVisible = true;
        this.Messenger.Send(new ToggleSnapSettingMessage(true));

        var map = await this.mapService.GetMapAsync();
        LoadOptionalLayers(map.OperationalLayers);

        _ = LoadDefaultLayers();
        await LpisSettingsService.SetDefaultValue();
    }
}
