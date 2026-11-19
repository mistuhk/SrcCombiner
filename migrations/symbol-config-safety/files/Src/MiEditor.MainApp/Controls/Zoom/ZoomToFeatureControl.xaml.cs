
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Options;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Enums;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.MainApp.ViewModels.Zoom;
using WG.MiEditor.Models;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Models.Notifications;
using MapView = Esri.ArcGISRuntime.Maui.MapView;

namespace WG.MiEditor.MainApp.Controls.Zoom;

[RegisterService(ServiceLifetime.Transient)]
#pragma warning disable S107
public partial class ZoomToFeatureControl : ContentView,
    IRecipient<ZoomToFeatureMessage>,
    IRecipient<ClearMapOverlaysMessage>,
    IRecipient<ZoomToFeatureWithFlash>,
    IRecipient<SwitchMapModeMessage>

{
    private readonly IMessenger messenger;
    private readonly INotificationManager notificationManager;
    private readonly IMapViewService _mapViewService;
    private readonly ILoggerService _loggerService;
    private readonly ICaseContext _caseContext;

    private readonly ApplicationSettings appSettings;
    private readonly ZoomToFeatureViewModel _vmZoomToFeature;    
    private readonly DrawingSymbolsSettings drawingSymbolsSettings;

    private MapType currentMapType = MapType.ViewModeMap;
    private MapView? arcMapView;

    public ZoomToFeatureControl() : this(Application.Current!.Handler!.MauiContext!.Services.GetService<IMessenger>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<INotificationManager>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<ApplicationSettings>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<IMapViewService>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<ILoggerService>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<ICaseContext>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<ZoomToFeatureViewModel>()!,
        Application.Current!.Handler!.MauiContext.Services.GetService<IOptions<DrawingSymbolsSettings>>()!)
    { }


    public ZoomToFeatureControl(IMessenger messenger,
        INotificationManager notificationManager,
        ApplicationSettings applicationSettings,
        IMapViewService mapViewService,
        ILoggerService loggerService,
        ICaseContext caseContext,
        ZoomToFeatureViewModel vmZoomToFeature,
        IOptions<DrawingSymbolsSettings> symbolSettings)
#pragma warning restore S107
    {
        InitializeComponent();
        BindingContext = vmZoomToFeature;
        _vmZoomToFeature = vmZoomToFeature;
        _mapViewService = mapViewService;
        _loggerService = loggerService;
        _caseContext = caseContext;

        this.appSettings = applicationSettings;
        this.messenger = messenger;
        this.notificationManager = notificationManager;
        drawingSymbolsSettings = symbolSettings.Value;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region Subscription Methods

    public async void Receive(ZoomToFeatureWithFlash message)
    {
        FeatType featType = message.Geometry.GeometryType switch
        {
            GeometryType.Polyline => FeatType.Line,
            GeometryType.Point => FeatType.Point,
            _ => FeatType.Polygon
        };

        var flashFeatureColor = message.FlashFeatureColor == null ? System.Drawing.ColorTranslator.FromHtml("#00FF00") : message.FlashFeatureColor;

        await Task.WhenAll(
            this.ZoomToGeometry(message.Geometry),
            this.FlashFeature(message.Geometry, featType, 2, 800, flashFeatureColor));
    }

    public void Receive(ZoomToFeatureMessage message)
        => MainThread.BeginInvokeOnMainThread(
            async () =>
            {
                this.messenger.Send(new MapLoadingMessage(true));
                await ZoomToFeature(message);
                this.messenger.Send(new MapLoadingMessage(false));
            });

    public void Receive(ClearMapOverlaysMessage message)
    {
        this.ClearMapOverlays();
    }

    public void Receive(SwitchMapModeMessage message) => this.currentMapType = message.MapType;
    #endregion

    #region Load and Unload Methods
    private void OnLoaded(object? sender, EventArgs e)
    {
        this.messenger.RegisterAll(this);
        _ = GetMapViewAsync()
            .ContinueWith(continuation => this.arcMapView = continuation.Result);
    }

    private void OnUnloaded(object? sender, EventArgs e) => this.messenger.UnregisterAll(this);

    #endregion

    #region ZoomToFeature overloads Methods
    private async Task ZoomToFeature(FeatureInfo? featureInfo)
    {
        try
        {
            if (featureInfo == null) return;

            Geometry? geometry = featureInfo.Geometry ?? await this._vmZoomToFeature.GetFeatureGeometry(featureInfo);

            if (geometry == null) return;

            List<Task> tasks = [this.ZoomToGeometry(geometry)];
            if (featureInfo.ZoomToWithFlash)
            {
                FeatType featType = geometry.GeometryType switch
                {
                    GeometryType.Polyline => FeatType.Line,
                    GeometryType.Point => FeatType.Point,
                    _ => FeatType.Polygon
                };

                var flashFeatureColor = System.Drawing.ColorTranslator.FromHtml("#00FF00");
                tasks.Add(this.FlashFeature(geometry, featType, 2, 800, flashFeatureColor));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToFeature)}.");
        }
    }
    private async Task ZoomToFeature(ZoomToFeatureMessage zoomFeature)
    {
        try
        {
            if (arcMapView == null)
            {
                this.notificationManager.Show("Zoom To Feature", "MapView is not initialized.", NotificationType.Error);
                return;
            }

            switch (zoomFeature.ZoomToFeatureType)
            {
                case ZoomToFeatureType.Coord:
                    await ZoomToCord((double)zoomFeature.XCord!, (double)zoomFeature.YCord!);
                    break;

                case ZoomToFeatureType.OSNG:
                    await ZoomToOSNG(zoomFeature.OSSheet!, zoomFeature.NGField!);
                    break;

                case ZoomToFeatureType.CRN:
                    await ZoomToCRN(zoomFeature.CRN!);
                    break;

                case ZoomToFeatureType.Feature:
                    await this.ZoomToFeature(zoomFeature.FeatureInfo);
                    break;

                case ZoomToFeatureType.Postcode:
                    await this.ZoomToPostcode(zoomFeature.Postcode!);
                    break;
                default:
                    this.notificationManager.Show("Zoom To Feature", "Input provided is not a valid CRN, Osng, XY or Postcode", NotificationType.Error);
                    return;
            }

            this.messenger.Send(new SetZoomScaleMessage(arcMapView.MapScale));
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToFeature)}.");
        }
    }
    #endregion

    #region Other Zoom Methods    
    private async Task ZoomToPostcode(string postcode)
    {
        try
        {
            if (postcode == null) return;

            Geometry? geometry = await this._vmZoomToFeature.GetPostcodeAsync(postcode);

            if (geometry != null)
            {
                await ZoomToGeometry(geometry);
                HighlightGeometry(geometry);
            }
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToFeature)}.");
        }
    }    

    private async Task ZoomToCord(double xCord, double yCord, bool isOSNGCall = false)
    {
        try
        {
            if(arcMapView == null || arcMapView.Map == null || arcMapView.Map.SpatialReference == null)
            {
                this.notificationManager.Show("Zoom To Coordinates", "Map spatial reference is not defined.", NotificationType.Error);
                return;
            }

            MapPoint mapPoint = new(xCord, yCord, arcMapView.Map.SpatialReference);
            MapPoint projectedPoint = (MapPoint)GeometryEngine.Project(mapPoint, arcMapView.Map.SpatialReference);
            var fullExtent = arcMapView.Map?.InitialViewpoint?.TargetGeometry.Extent;

            if (fullExtent == null)
            {
                this.notificationManager.Show("Zoom To Coordinates", "Invalid Extent", NotificationType.Error);
                return;
            }

            bool isInside = GeometryEngine.Contains(fullExtent, projectedPoint);
            if (!isInside)
            {
                string errorMessage = isOSNGCall ? "OSNG values or format are invalid. Please refer to format reference: XX1111:1111." : "The selected location falls outside the map's projection area";
                
                this.notificationManager.Show("Zoom To Feature", errorMessage, NotificationType.Error);

                return;                
            }

            if (ShouldEnforceCaseParcel(isOSNGCall, currentMapType) &&
                await IsOutsideCaseParcelAsync(mapPoint))
            {
                this.notificationManager.Show(
                    "Zoom To Coordinates",
                    $"The XY coordinates: '{xCord},{yCord}' are outside the current CRN.",
                    NotificationType.Info);

                return;
            }

            var zoomScale = ResolveCoordinateZoomScale(
                isOSNGCall,
                appSettings.CoordinateZoomScale,
                this.arcMapView.MapScale);

            await arcMapView.SetViewpointCenterAsync(mapPoint, zoomScale);

            var highlightOverlay = arcMapView.GraphicsOverlays!.FirstOrDefault(go => go.Id == "highlightOverlay");
            highlightOverlay?.Graphics.Clear();

            await FlashFeature(mapPoint, FeatType.Point);
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToCord)}.");
        }
    }
    
    private async Task ZoomToOSNG(string ossSheet, string ngField)
    {
        try
        {
            if (string.IsNullOrEmpty(ossSheet) || string.IsNullOrEmpty(ngField))
            {
                this.notificationManager.Show("Zoom To OSNG", "Invalid OSNG input", NotificationType.Error);
                return;
            }

            if (arcMapView == null )
            {
                this.notificationManager.Show("Zoom To OSNG", "Issue map", NotificationType.Error);
                return;
            }

            var (geometry, msg) = await _vmZoomToFeature.GetLpisGeometryByOSNG(ossSheet, ngField, appSettings.Layers.LPIS_Poly);

            if (geometry != null)
            {
                await arcMapView.SetViewpointGeometryAsync(geometry, 50);

                await FlashFeature(geometry, FeatType.Polygon, 2);
            }
            else
            {
                if(string.IsNullOrEmpty(msg))
                {
                    var xyCord = _vmZoomToFeature.ConvertOSNGtoXY(ossSheet, ngField);

                    if (xyCord.Split(",").Length == 2)
                    {
                        var xCord = Convert.ToDouble(xyCord.Split(',')[0]);
                        var yCord = Convert.ToDouble(xyCord.Split(',')[1]);

                        await ZoomToCord(xCord, yCord, isOSNGCall: true);
                    }
                }
                else
                {
                    this.notificationManager.Show("Zoom To OSNG", msg, NotificationType.Error);
                    return;
                }
            }           
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToOSNG)}.");
        }
    }

    private async Task ZoomToGeometry(Geometry geometry)
    {
        try
        {
            if (arcMapView?.GraphicsOverlays == null)
            {
                this.notificationManager.Show("Zoom To Geometry", "MapView or GraphicsOverlays is not initialized.", NotificationType.Error);
                return;
            }

            arcMapView.GraphicsOverlays.Clear();

            if (geometry == null) return;

            var envelope = geometry.Extent;

            var padding = 10;

            var expandedEnvelope = _vmZoomToFeature.ExpandEnvelope(envelope!, padding);

            if (expandedEnvelope is null)
                return;

            await arcMapView.SetViewpointGeometryAsync(expandedEnvelope);
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToGeometry)}.");
        }
    }

    private async Task ZoomToCRN(string crn)
    {
        try
        {
            var (combinedGeometry, msg, geometryList) = await this._vmZoomToFeature.GetCrnZoomToGeometryAsync(arcMapView!.Map!, crn);
            if (combinedGeometry == null)
            {
                this.notificationManager.Show("CRN search ", msg, NotificationType.Error);
                return;
            }

            await ZoomToGeometry(combinedGeometry);

            geometryList?.ForEach(HighlightGeometry);

        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ZoomToCRN)}.");
        }
    }

    #endregion

    #region Other support Methods
    private async Task<MapView> GetMapViewAsync() => await this._mapViewService.GetMapViewAsync();
    
    private async Task FlashFeature(
        Geometry geometry,
        FeatType featType,
        int flashFreq = 4,
        int flashingTime = 1000,
        System.Drawing.Color? flashFeatureColor = null)
    {
        try
        {
            var flashOverlay = new GraphicsOverlay();
            var flashGraph = new Graphic();

            // A missing FlashFeature section falls back to the same green used for flash messages.
            var argbSetting = drawingSymbolsSettings.FlashFeature?.Argb;

            flashFeatureColor ??= argbSetting is null
                ? System.Drawing.Color.Lime
                : System.Drawing.Color.FromArgb(argbSetting.A, argbSetting.R, argbSetting.G, argbSetting.B);

            if (featType == FeatType.Polygon)
            {

                var flashSymbol = new SimpleFillSymbol
                {
                    Style = SimpleFillSymbolStyle.Solid,
                    Color = (System.Drawing.Color)flashFeatureColor,
                };

                flashGraph = new Graphic(geometry, flashSymbol);
            }
            else if (featType == FeatType.Point)
            {
                var markerFlashSymbol = new SimpleMarkerSymbol
                {
                    Style = SimpleMarkerSymbolStyle.Cross,
                    Color = (System.Drawing.Color)flashFeatureColor,
                    Size = 21
                };

                flashGraph = new Graphic(geometry, markerFlashSymbol);
            }
            else if (featType == FeatType.Line)
            {
                var flashSymbol = new SimpleLineSymbol
                {
                    Style = SimpleLineSymbolStyle.Solid,
                    Width = 5,
                    Color = (System.Drawing.Color)flashFeatureColor,
                };
                flashGraph = new Graphic(geometry, flashSymbol);
            }

            flashOverlay.Graphics.Add(flashGraph);
            arcMapView?.GraphicsOverlays?.Add(flashOverlay);


            for (int i = 0; i < flashFreq; i++)
            {
                flashGraph.IsVisible = !flashGraph.IsVisible;
                await Task.Delay(flashingTime);
            }

            flashOverlay.Graphics.Remove(flashGraph);
            arcMapView?.GraphicsOverlays?.Remove(flashOverlay);
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(FlashFeature)}.");
        }
    }

    private void HighlightGeometry(Geometry geometry)
    {
        try
        {
            if (arcMapView?.GraphicsOverlays == null) return;

            var highlightOverlay = arcMapView.GraphicsOverlays.FirstOrDefault(go => go.Id == "highlightOverlay");
            if (highlightOverlay == null)
            {
                highlightOverlay = new GraphicsOverlay { Id = "highlightOverlay" };
                arcMapView.GraphicsOverlays.Add(highlightOverlay);
            }


            var PolygonSymbol = new SimpleFillSymbol
            {
                Color = System.Drawing.Color.Transparent,

                Outline = new SimpleLineSymbol
                {
                    Color = System.Drawing.Color.Blue,
                    Width = 2
                }
            };

            var PolylineSymbol = new SimpleLineSymbol
            {
                Color = System.Drawing.Color.Blue,
                Width = 2
            };

            Graphic? graphic = null;

            if (geometry is Polygon polygon)
            {
                graphic = new Graphic(polygon, PolygonSymbol);
                graphic.Attributes.Add("goe", "geom");
            }
            else if (geometry is Polyline polyline)
            {
                graphic = new Graphic(polyline, PolylineSymbol);
            }

            if (graphic != null)
            {
                highlightOverlay.Graphics.Add(graphic);
            }

        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(HighlightGeometry)}.");
        }

    }

    private void ClearMapOverlays()
    {
        try
        {
            this.arcMapView?.GraphicsOverlays?.Clear();
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(ClearMapOverlays)}.");
        }
    }

    public static double ResolveCoordinateZoomScale(
        bool isOsNgCall,
        double coordinateZoomScale,
        double currentMapScale)
        => isOsNgCall ? currentMapScale : coordinateZoomScale;

    private static bool ShouldEnforceCaseParcel(bool isOsNgCall, MapType currentMapType)
        => !isOsNgCall && currentMapType == MapType.EditModeMap;

    private async Task<bool> IsOutsideCaseParcelAsync(MapPoint mapPoint)
    {
        try
        {
            var caseGeometry = (await _caseContext.GetSelectedCase())?.Geometry;
            if (caseGeometry == null) return false;

            var comparablePoint = caseGeometry.SpatialReference != null
                ? (MapPoint)GeometryEngine.Project(mapPoint, caseGeometry.SpatialReference)
                : mapPoint;

            return !GeometryEngine.Contains(caseGeometry, comparablePoint);
        }
        catch (Exception ex)
        {
            _loggerService.Error(ex, $"Error occurred in {nameof(IsOutsideCaseParcelAsync)}");
            return false;
        }
    }
    #endregion


}
