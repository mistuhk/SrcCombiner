
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Options;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;

namespace WG.MiEditor.Core.Services.Drawing.Editing.Selection.Operation.Strategies;

public sealed class DragSelectionStrategy : FeatureSelectionStrategyBase
{
    private readonly IKeyboardModifierService keyboardModifierService;
    private readonly ILoggerService loggerService;

    private const short MAX_DRAG_TOLERANCE = 5;
    private readonly SimpleFillSymbol dragSelectionSymbol;
    private PointerGestureRecognizer? pointerGestureRecogniser;

    private Graphic? dragSelectionGraphic;
    private Point? startScreenPoint;
    private bool isDragging;

    public DragSelectionStrategy(
        IOptions<DrawingSymbolsSettings> symbolSettings, 
        IDrawingSymbolColourParser colourParser, 
        IKeyboardModifierService keyboardModifierService, 
        ILoggerService loggerService)
    {
        this.keyboardModifierService = keyboardModifierService;
        this.loggerService = loggerService;

        var settings = symbolSettings.Value.MultiFeatureSelection;
        // The drag box always draws dashed and solid-filled; only colours and width come from config.
        var outline = new SimpleLineSymbol(
            SimpleLineSymbolStyle.Dash,
            colourParser.FromSetting(settings?.Outline?.Colour),
            colourParser.SizeFromSetting(settings?.Outline?.Width, DrawingSymbolColourParserService.FallbackLineWidth));
        dragSelectionSymbol = new SimpleFillSymbol(
            SimpleFillSymbolStyle.Solid,
            colourParser.FromSetting(settings?.FillColour, DrawingSymbolColourParserService.FallbackFill),
            outline);
    }

    public override void Start(MapView mapView, GraphicsOverlay graphicsOverlay, Func<Geometry, bool, Task> onSelectionCompleteAsync)
    {
        base.Start(mapView, graphicsOverlay, onSelectionCompleteAsync);

        pointerGestureRecogniser = new PointerGestureRecognizer();
        pointerGestureRecogniser.PointerPressed += PointerGestureRecogniser_PointerPressed;
        pointerGestureRecogniser.PointerMoved += PointerGestureRecogniser_PointerMoved;
        pointerGestureRecogniser.PointerReleased += PointerGestureRecogniser_PointerReleased;

        mapView.GestureRecognizers.Add(pointerGestureRecogniser);
    }

    public override void Stop()
    {
        base.Stop();

        if (MapView != null && pointerGestureRecogniser != null)
        {
            MapView.GestureRecognizers.Remove(pointerGestureRecogniser);
        }

        ClearSelectionGraphic();
    }

    private void PointerGestureRecogniser_PointerPressed(object? sender, PointerEventArgs e)
    {
        DisableDefaultInteractions();
        startScreenPoint = e.GetPosition(MapView);
        isDragging = false;
    }

    private void PointerGestureRecogniser_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (startScreenPoint == null) return;

        var currentScreenPoint = e.GetPosition(MapView);
        if (currentScreenPoint != null)
        {
            var deltaX = startScreenPoint.Value.X - currentScreenPoint.Value.X;
            var deltaY = startScreenPoint.Value.Y - currentScreenPoint.Value.Y;
            var distance = deltaX * deltaX + deltaY * deltaY;

            if (distance > MAX_DRAG_TOLERANCE * MAX_DRAG_TOLERANCE) isDragging = true;
            if (isDragging) UpdateSelectionGraphic(currentScreenPoint.Value);
        }
    }

    private async void PointerGestureRecogniser_PointerReleased(object? sender, PointerEventArgs e)
    {
        bool wasDragging = isDragging;
        Point? startPt = startScreenPoint;

        ClearSelectionGraphic();
        EnableDefaultInteractions();
        startScreenPoint = null;
        isDragging = false;

        try
        {
            if (!wasDragging || startPt == null || MapView?.Map == null) return;

            var endScreenPointNullable = e.GetPosition(MapView);
            if (!endScreenPointNullable.HasValue) return;

            var endScreenPoint = endScreenPointNullable.Value;
            var startMapPoint = MapView.ScreenToLocation(startPt.Value);
            var endMapPoint = MapView.ScreenToLocation(endScreenPoint);

            if (startMapPoint != null && endMapPoint != null)
            {
                var isShiftPressed = keyboardModifierService.IsShiftPressed();

                var screenCorner_EndX_StartY = new Point(endScreenPoint.X, startPt.Value.Y);
                var screenCorner_StartX_EndY = new Point(startPt.Value.X, endScreenPoint.Y);
                var mapCorner_EndX_StartY = MapView.ScreenToLocation(screenCorner_EndX_StartY);
                var mapCorner_StartX_EndY = MapView.ScreenToLocation(screenCorner_StartX_EndY);

                if (mapCorner_EndX_StartY != null && mapCorner_StartX_EndY != null)
                {
                    var points = new List<MapPoint>() { startMapPoint, mapCorner_EndX_StartY, endMapPoint, mapCorner_StartX_EndY };
                    var selectionPolygon = new Polygon(points, MapView.Map.SpatialReference);

                    if (OnSelectionCompleteAsync != null)
                    {
                        await OnSelectionCompleteAsync.Invoke(selectionPolygon, isShiftPressed);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            loggerService.Error(ex, $"Error occurred during multiple feature selection: {ex.Message}");
        }
    }

    private void UpdateSelectionGraphic(Point currentScreenPoint)
    {
        if (startScreenPoint == null || MapView?.Map == null) return;

        var startMapPoint = MapView.ScreenToLocation(startScreenPoint.Value);
        var currentMapPoint = MapView.ScreenToLocation(currentScreenPoint);

        if (startMapPoint == null || currentMapPoint == null) return;

        var screenCorner_CurrentX_StartY = new Point(currentScreenPoint.X, startScreenPoint.Value.Y);
        var screenCorner_StartX_CurrentY = new Point(startScreenPoint.Value.X, currentScreenPoint.Y);

        var mapCorner_CurrentX_StartY = MapView.ScreenToLocation(screenCorner_CurrentX_StartY);
        var mapCorner_StartX_CurrentY = MapView.ScreenToLocation(screenCorner_StartX_CurrentY);

        if (mapCorner_CurrentX_StartY == null || mapCorner_StartX_CurrentY == null) return;

        var points = new List<MapPoint> { startMapPoint, mapCorner_CurrentX_StartY, currentMapPoint, mapCorner_StartX_CurrentY };
        var selectionPolygon = new Polygon(points, MapView.Map.SpatialReference);

        if (dragSelectionGraphic == null)
        {
            dragSelectionGraphic = new Graphic(selectionPolygon, dragSelectionSymbol);
            GraphicsOverlay?.Graphics.Add(dragSelectionGraphic);

            return;
        }

        dragSelectionGraphic.Geometry = selectionPolygon;
    }

    private void ClearSelectionGraphic()
    {
        if (dragSelectionGraphic != null)
        {
            GraphicsOverlay?.Graphics.Remove(dragSelectionGraphic);
            dragSelectionGraphic = null;
        }
    }
}
