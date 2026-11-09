
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.UI;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.AttributeInfo;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.MainApp.ViewModels;
using WG.MiEditor.Messages;
using WG.MiEditor.Messages.Toolbar;
using WG.MiEditor.Models;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Measurement;
using MapView = Esri.ArcGISRuntime.Maui.MapView;

namespace WG.MiEditor.MainApp.Controls.Map;

[RegisterService(ServiceLifetime.Transient)]
public partial class InfoAttributePanel : ContentView,
    IRecipient<LayersInformationMessage>,
    IRecipient<ShowInformationPanelMessage>,
    IRecipient<EditingSessionInProgressMessage>,
    IRecipient<MeasurementInProgressMessage>
{
    private const string INFO_NODE_BACKGROUND_BRUSH = "ToolButtonActiveBrush";
    private const string INOF_GRAPHIC_OVERLAY = "infoGraphisOverly";
    private const string LPIS_LAYER_NAME = "LPIS";

    private readonly IMessenger messenger;
    private readonly IMapViewService mapViewService;
    private readonly IInfoAttributeHelper infoAttributeHelper;
    private readonly InfoAttributeViewModel viewModel;
    private readonly ApplicationSettings appSettings;
    private readonly ILoggerService loggerService;

    private GraphicsOverlay highlightOverlay = null!;
    private MapView mapView = null!;
    private Dictionary<string, Border> borderMap = [];
    private bool lpisNodeExpanded = false;

    public InfoAttributePanel()
    : this(Application.Current!.Handler!.MauiContext!.Services.GetService<IMessenger>()!,
          Application.Current!.Handler!.MauiContext.Services.GetService<ApplicationSettings>()!,
          Application.Current!.Handler!.MauiContext.Services.GetService<IMapViewService>()!,
          Application.Current!.Handler!.MauiContext!.Services.GetService<InfoAttributeViewModel>()!,
          Application.Current!.Handler!.MauiContext!.Services.GetService<ILoggerService>()!,
          Application.Current!.Handler!.MauiContext!.Services.GetService<IInfoAttributeHelper>()!)
    {
    }

    public InfoAttributePanel(IMessenger messenger,
        ApplicationSettings applicationSettings,
        IMapViewService mapViewService,
        InfoAttributeViewModel viewModel,
        ILoggerService loggerService,
        IInfoAttributeHelper infoAttributeHelper)
    {
        InitializeComponent();
        this.messenger = messenger;
        this.mapViewService = mapViewService;
        this.viewModel = viewModel;
        BindingContext = this.viewModel;
        appSettings = applicationSettings;
        this.loggerService = loggerService;
        this.infoAttributeHelper = infoAttributeHelper;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Receive(LayersInformationMessage message)
        => MainThread.BeginInvokeOnMainThread(() => _ = GetLayersInfo(message.Point, message.MapPoint));

    public void Receive(ShowInformationPanelMessage message) => Reset();

    public void Receive(EditingSessionInProgressMessage message) => Reset();

    public void Receive(MeasurementInProgressMessage message) => Reset();

    private void OnLoaded(object? sender, EventArgs e)
    {
        this.messenger.RegisterAll(this);
        _ = GetMapViewAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e) => this.messenger.UnregisterAll(this);

    private async Task GetMapViewAsync() => this.mapView = await this.mapViewService.GetMapViewAsync();

    private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        this.viewModel.PanUpdatedCommand.Execute(e);
    }

    private async Task GetLayersInfo(Point point, MapPoint mapPoint)
    {
        this.loggerService.Info("GetLayersInfo started");

        if (IsBusy()) return;

        try
        {
            BeginLoading();
            ShowInfoPanel();

            var featureGroups = await FetchLayerFeatures(mapPoint, point);
            await ShowAttributeInfo(featureGroups);

            this.loggerService.Info("GetLayersInfo completed successfully");
        }
        catch (Exception ex)
        {
            this.loggerService.Error(ex, "Error in GetLayersInfo.");
            throw;
        }
        finally
        {
            EndLoading();
        }
    }

    private bool IsBusy() => this.viewModel.IsLoading;

    private void BeginLoading()
    {
        this.viewModel.ResetInfoPanel();
        this.viewModel.IsLoading = true;
    }

    private void EndLoading() => this.viewModel.IsLoading = false;

    private void ShowInfoPanel() => this.messenger.Send(new ShowInformationPanelMessage(true));

    private async Task<List<FeatureNode>?> FetchLayerFeatures(MapPoint mapPoint, Point point)
        => await this.viewModel.GetLayersAttributeInfo(mapPoint, point);

    private async Task ShowAttributeInfo(List<FeatureNode>? featureGroups)
    {
        if (featureGroups == null) return;

        // Clear previous UI and state
        ResetPanelsAndState();

        // Ensure highlight overlay is ready
        EnsureHighlightOverlay();

        // If no features, show message and exit early
        if (IsFeatureGroupsEmpty(featureGroups))
        {
            ShowNoFeaturesMessage();
            return;
        }

        // Highlight and display all feature groups
        bool lpisFound = await HighlightAndDisplayFeatureGroups(featureGroups);

        // If no LPIS group found, expand and select the first group
        if (!lpisFound)
        {
            await ExpandAndSelectFirstGroup(featureGroups);
        }
    }

    /// <summary>
    /// Clears UI panels and resets state for a new attribute info display.
    /// </summary>
    private void ResetPanelsAndState()
    {
        AttributeInfoFramePanel.Clear();
        FeatureListPanel.Clear();
        FeatureListPanel.Children.Clear();
        lpisNodeExpanded = false;
    }

    /// <summary>
    /// Checks if the feature groups list is null or empty.
    /// </summary>
    private static bool IsFeatureGroupsEmpty(List<FeatureNode> featureGroups)
    {
        return featureGroups == null || featureGroups.Count == 0;
    }

    /// <summary>
    /// Highlights and displays all feature groups, returns true if LPIS group is found.
    /// </summary>
    private async Task<bool> HighlightAndDisplayFeatureGroups(List<FeatureNode> featureGroups)
    {
        bool lpisFound = false;

        foreach (var group in featureGroups)
        {
            HighlightGroupParent(group);
            HighlightGroupChildren(group);

            var expander = await CreateFeatureGroupExpander(group);

            if (IsLpisGroup(group))
            {
                lpisFound = true;
            }

            FeatureListPanel.Children.Add(expander);
        }

        return lpisFound;
    }

    /// <summary>
    /// Highlights the parent feature of a group if it has geometry.
    /// </summary>
    private void HighlightGroupParent(FeatureNode group)
    {
        if (group == null)
        {
            return;
        }

        var parent = group.Parent;
        if (parent == null || parent.Geometry == null)
        {
            return;
        }

        var parentGraphic = infoAttributeHelper.CreateGraphic(parent, System.Drawing.Color.Cyan);
        if (parentGraphic == null)
        {
            return;
        }

        highlightOverlay.Graphics.Add(parentGraphic);
    }

    /// <summary>
    /// Recursively highlights all children of a group.
    /// </summary>
    private void HighlightGroupChildren(FeatureNode group)
    {
        foreach (var node in group.Children)
        {
            HighlightFeatureNodeRecursive(node);
        }
    }

    /// <summary>
    /// Checks if the group is an LPIS group.
    /// </summary>
    private bool IsLpisGroup(FeatureNode group)
    {
        return string.Equals(group.LayerName, this.appSettings.Layers.LPIS_Poly, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExpandAndSelectFirstGroup(List<FeatureNode> featureGroups)
    {
        if (FeatureListPanel.Children.Count == 0 || !(FeatureListPanel.Children[0] is Expander firstExpander)) return;

        firstExpander.IsExpanded = true;

        var firstGroup = featureGroups[0];
        if (firstGroup.Children.Count == 0) return;

        var firstChild = firstGroup.Children[0];

        if (borderMap.TryGetValue(firstChild.DisplayId, out var selectedBorder))
        {
            selectedBorder.Background = (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH];
        }

        await LoadAttributesToRightPanel(firstChild.Parent, firstChild.LayerName, firstChild.DisplayId);

        HighlightFeature(firstChild.Parent);

    }

    private void EnsureHighlightOverlay()
    {
        var existingOverlay = mapView.GraphicsOverlays?.FirstOrDefault(o => o.Id == INOF_GRAPHIC_OVERLAY);
        if (existingOverlay != null)
        {
            mapView!.GraphicsOverlays!.Remove(existingOverlay);
        }

        highlightOverlay = new GraphicsOverlay { Id = INOF_GRAPHIC_OVERLAY };

        mapView!.GraphicsOverlays!.Add(highlightOverlay);
    }

    private void ShowNoFeaturesMessage()
    {
        AttributeInfoFramePanel.Children.Add(new Label
        {
            Text = "No Features Found",
            HorizontalOptions = LayoutOptions.Center,
            FontAttributes = FontAttributes.Bold
        });
    }

    /// <summary>
    /// Creates an Expander for a feature group, including its children and LPIS-specific logic.
    /// </summary>
    private async Task<Expander> CreateFeatureGroupExpander(FeatureNode group)
    {
        // Create the group label and main border
        var label = this.infoAttributeHelper.CreateLabel($"{group.LayerName} ({group.Children.Count})");
        var mainBorder = this.infoAttributeHelper.CreateStyledBorder(label);

        // Create the expander with a placeholder content stack
        var contentStack = new StackLayout { Padding = new Thickness(20, 0, 0, 0) };
        var expander = this.infoAttributeHelper.CreateExpander(mainBorder, contentStack);

        // Apply LPIS-specific UI logic if needed
        ApplyLpisGroupStylingIfNeeded(group, expander, mainBorder);

        // Wire up property change event for background color
        AttachExpanderBackgroundToggle(expander, mainBorder);

        // Add all child expanders to the content stack
        await AddChildExpandersAsync(group, contentStack);

        return expander;
    }

    /// <summary>
    /// Applies LPIS-specific styling and expansion logic to the expander.
    /// </summary>
    private void ApplyLpisGroupStylingIfNeeded(FeatureNode group, Expander expander, Border mainBorder)
    {
        if (IsLpisGroup(group))
        {
            expander.IsExpanded = true;
            mainBorder.Background = (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH];
        }
    }

    /// <summary>
    /// Attaches a property changed event to toggle the background color of the main border.
    /// </summary>
    private static void AttachExpanderBackgroundToggle(Expander expander, Border mainBorder)
    {
        expander.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Expander.IsExpanded))
            {
                mainBorder.Background = expander.IsExpanded
                    ? (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH]
                    : Colors.Transparent;
            }
        };
    }

    /// <summary>
    /// Adds child expanders for each child node in the group.
    /// </summary>
    private async Task AddChildExpandersAsync(FeatureNode group, StackLayout contentStack)
    {
        foreach (var childNode in group.Children)
        {
            var childExpander = await CreateFeatureNodeExpanderRecursive(childNode);
            contentStack.Children.Add(childExpander);
        }
    }

    /// <summary>
    /// Recursively creates an Expander for a FeatureNode and its children, with gesture and highlight logic.
    /// </summary>
    private async Task<Expander> CreateFeatureNodeExpanderRecursive(FeatureNode node)
    {
        // Create the label and border for this node
        var label = this.infoAttributeHelper.CreateLabel(node.DisplayId, padding: 2);
        var border = CreateNodeBorder(node, label);

        // Special handling for LPIS node: expand and highlight only once
        await HandleLpisNodeHighlightingAsync(node, border);

        // If node has no children, return a simple expander
        if (node.Children == null || node.Children.Count == 0)
            return CreateLeafExpander(border);

        // Otherwise, create expanders or borders for all children
        var childStack = await CreateChildStackForNode(node.Children);

        // Create the main expander for this node
        var mainExpander = this.infoAttributeHelper.CreateExpander(border, childStack, new Thickness(5));

        // Expand LPIS node only once
        if (IsLpisNode(node) && !lpisNodeExpanded)
        {
            mainExpander.IsExpanded = true;
            lpisNodeExpanded = true;
        }
        return mainExpander;
    }

    /// <summary>
    /// Creates a Border for a node and attaches tap gesture for attribute loading and highlighting.
    /// </summary>
    private Border CreateNodeBorder(FeatureNode node, Label label)
    {
        var border = new Border
        {
            Content = label,
            BackgroundColor = Colors.Transparent
        };

        if (node.Parent != null)
        {
            borderMap[node.DisplayId] = border;
            border.GestureRecognizers.Add(this.infoAttributeHelper.CreateTapGesture(async () =>
            {
                // Clear all highlights
                foreach (var f in borderMap.Values)
                    f.Background = Colors.Transparent;

                // Highlight selected
                border.Background = (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH];
                await LoadAttributesToRightPanel(node.Parent, node.LayerName, node.DisplayId);
                HighlightFeature(node.Parent);
            }));
        }

        return border;
    }

    /// <summary>
    /// Handles special highlighting and attribute loading for LPIS nodes.
    /// </summary>
    private async Task HandleLpisNodeHighlightingAsync(FeatureNode node, Border border)
    {
        if (node.Parent != null && IsLpisNode(node) && !lpisNodeExpanded)
        {
            border.Background = (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH];
            await LoadAttributesToRightPanel(node.Parent, node.LayerName, node.DisplayId);
            HighlightFeature(node.Parent);
        }
    }

    /// <summary>
    /// Returns true if the node is an LPIS node.
    /// </summary>
    private static bool IsLpisNode(FeatureNode node) =>
        string.Equals(node.LayerName, LPIS_LAYER_NAME, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a simple expander for a leaf node.
    /// </summary>
    private static Expander CreateLeafExpander(Border border)
    {
        return new Expander
        {
            Header = new Border
            {
                Padding = 5,
                Content = border
            },
            Content = null
        };
    }

    /// <summary>
    /// Creates a stack of child expanders or borders for the given children.
    /// </summary>
    private async Task<StackLayout> CreateChildStackForNode(IEnumerable<FeatureNode> children)
    {
        var childStack = new StackLayout();

        foreach (var child in children)
        {
            var childHeader = CreateChildHeader(child);
            var childBorder = this.infoAttributeHelper.CreateStyledBorder(childHeader, margin: 5);

            AttachChildTapGesture(child, childBorder);

            if (HasChildren(child))
            {
                var childExpander = await CreateChildExpanderAsync(child, childBorder);
                childStack.Children.Add(childExpander);
            }
            else
            {
                childStack.Children.Add(childBorder);
            }
        }

        return childStack;
    }

    /// <summary>
    /// Attaches a tap gesture to the child border for attribute loading and highlighting.
    /// </summary>
    private void AttachChildTapGesture(FeatureNode child, Border childBorder)
    {
        childBorder.GestureRecognizers.Add(this.infoAttributeHelper.CreateTapGesture(async () =>
        {
            if (child.Parent != null)
            {
                await LoadAttributesToRightPanel(child.Parent, child.LayerName, child.DisplayId);
                HighlightFeature(child.Parent);
            }
        }));
    }

    /// <summary>
    /// Returns true if the node has children.
    /// </summary>
    private static bool HasChildren(FeatureNode node)
    {
        return node.Children != null && node.Children.Count != 0;
    }

    /// <summary>
    /// Creates an expander for a child node with its nested children.
    /// </summary>
    private async Task<Expander> CreateChildExpanderAsync(FeatureNode child, Border childBorder)
    {
        var childStack = await CreateChildStackRecursive(child.Children);
        var childExpander = this.infoAttributeHelper.CreateExpander(childBorder, childStack);

        if (childBorder.Content is StackLayout stackLayout)
        {
            AttachExpanderIconToggle(stackLayout, childExpander);
        }

        return childExpander;
    }

    /// <summary>
    /// Creates a header for a child node, including an icon and label.
    /// </summary>
    private StackLayout CreateChildHeader(FeatureNode child)
    {
        var icon = new Image { Source = "layer_collapsed.png", VerticalOptions = LayoutOptions.Center };
        var childLabel = this.infoAttributeHelper.CreateLabel(child.LayerName, margin: new Thickness(2, 0, 0, 0));
        return new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Children = { icon, childLabel }
        };
    }

    /// <summary>
    /// Attaches icon toggle logic to an expander.
    /// </summary>
    private static void AttachExpanderIconToggle(StackLayout header, Expander expander)
    {
        if (header.Children.FirstOrDefault() is Image icon)
        {
            expander.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Expander.IsExpanded))
                {
                    icon.Source = expander.IsExpanded ? "layer_expanded.png" : "layer_collapsed.png";
                }
            };
        }
    }

    private async Task<StackLayout> CreateChildStackRecursive(IEnumerable<FeatureNode> children)
    {
        var stack = new StackLayout { Padding = new Thickness(20, 0, 0, 0) };

        foreach (var child in children)
        {
            var label = this.infoAttributeHelper.CreateLabel(child.DisplayId, padding: 2);

            var border = new Border
            {
                Content = label,
                BackgroundColor = Colors.Transparent
            };
            borderMap[child.DisplayId] = border;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                // Clear all highlights
                foreach (var f in borderMap.Values)
                    f.Background = Colors.Transparent;

                // Highlight selected
                border.Background = (Brush)Application.Current!.Resources[INFO_NODE_BACKGROUND_BRUSH];
                if (child.Parent != null)
                {
                    await LoadAttributesToRightPanel(child.Parent, child.LayerName, child.DisplayId);
                    HighlightFeature(child.Parent);
                }
            };
            border.GestureRecognizers.Add(tapGesture);
            Microsoft.Maui.Controls.View container = border;

            if (child.Children != null && child.Children.Count != 0)
            {
                var nestedStack = await CreateChildStackRecursive(child.Children);
                this.infoAttributeHelper.CreateExpander(border, nestedStack);
            }
            else
            {
                stack.Children.Add(container);
            }
        }
        return stack;
    }

    private async Task LoadAttributesToRightPanel(ArcGISFeature? feature, string layerName, string displayId)
    {
        if (feature == null) return;

        AttributeInfoFramePanel.Children.Clear();

        var attributeSection = await this.viewModel.LoadAttributesToRightPanel(feature, layerName, displayId);
        if (attributeSection != null)
        {
            AttributeInfoFramePanel.Children.Add(attributeSection);
        }
    }

    private void HighlightFeatureNodeRecursive(FeatureNode node)
    {
        if (node == null)
        {
            return;
        }

        var parent = node.Parent;
        if (parent?.Geometry != null)
        {
            var graphic = infoAttributeHelper.CreateGraphic(parent, System.Drawing.Color.LightBlue);
            if (graphic != null)
            {
                highlightOverlay.Graphics.Add(graphic);
            }
        }

        if (node.Children == null)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            HighlightFeatureNodeRecursive(child);
        }
    }

    private void HighlightFeature(ArcGISFeature? feature)
    {
        if (feature == null || feature.Geometry == null)
        {
            return;
        }

        highlightOverlay.Graphics.Clear();

        var graphic = infoAttributeHelper.CreateGraphic(feature, System.Drawing.Color.Cyan);
        if (graphic == null)
        {
            return;
        }

        highlightOverlay.Graphics.Add(graphic);
    }

    private void Reset()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            highlightOverlay?.Graphics.Clear();
            AttributeInfoFramePanel.Clear();
            FeatureListPanel.Clear();
            borderMap.Clear();
        });
    }
}
