using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI.Editing;
using System.ComponentModel;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>Configuration for one kind of sketched measurement.</summary>
/// <param name="Title">User-facing name, used for error notifications.</param>
/// <param name="GeometryType">Geometry the editor sketches.</param>
/// <param name="MinVertexCount">Vertices required before a sketch can be completed.</param>
/// <param name="ResultSymbol">Symbol for the completed measurement.</param>
/// <param name="OnInvalidGeometry">Called when the user completes a sketch with too few vertices.</param>
public sealed record SketchMeasurementOptions(
    string Title,
    GeometryType GeometryType,
    int MinVertexCount,
    Symbol ResultSymbol,
    Action OnInvalidGeometry);

/// <summary>
/// A geometry-editor driven measurement: the user taps vertices, live length/area is broadcast as
/// <see cref="Shared.Messages.Measurement.DrawingProgressMessage"/>, and a double-tap completes the
/// sketch, leaving the result on the map until the next sketch begins.
/// <para>
/// Pan and zoom are locked only while a sketch has at least one vertex, so dragging a vertex
/// cannot move the map and the completing double-tap cannot zoom it. Before the first vertex and
/// between measurements the map is free to pan and zoom.
/// </para>
/// <para>
/// Composed from <see cref="MapInteractionLock"/> (pan/zoom) and <see cref="MeasurementResultLayer"/>
/// (result graphic). Every session is fully torn down by <see cref="Stop"/>, even when individual
/// steps fail, and a failed <see cref="Start"/> leaves nothing attached.
/// </para>
/// </summary>
public sealed class SketchMeasurement(
    IMessenger messenger,
    IMeasurementErrorReporter errorReporter,
    SketchMeasurementOptions options)
{
    private readonly MapInteractionLock interactionLock = new();

    private MapView? mapView;
    private GeometryEditor? editor;
    private MeasurementResultLayer? results;

    // The map has one geometry editor shared by every drawing tool, and each tool assigns its own
    // VertexTool instance when it starts. Comparing against ours tells us whether the running
    // session is this measurement's, so we never react to, or stop, another tool's drawing.
    private VertexTool? ownTool;

    // Set when a sketch completes. The result stays visible until the user places the first
    // vertex of the next sketch, at which point only the result graphic is removed.
    private bool clearResultOnNextVertex;

    // True while a double-tap is stopping and restarting the editor. The editor raises geometry
    // changes during that, which must not be mistaken for the user undoing their vertices.
    private bool completingSketch;

    public bool IsRunning => mapView is not null;

    public void Start(MeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsRunning) Stop();

        var geometryEditor = context.MapView.GeometryEditor
            ?? throw new MeasurementUnavailableException("The map cannot be measured on right now. Please try again in a moment.");

        mapView = context.MapView;
        editor = geometryEditor;
        results = new MeasurementResultLayer(context.Overlay);

        try
        {
            ownTool = CreateVertexTool();

            // Anything still drawing should have been stopped by its own toolbar through the
            // activation coordinator; this only clears a stale session so Start cannot throw.
            if (geometryEditor.IsStarted) geometryEditor.Stop();

            geometryEditor.Tool = ownTool;
            geometryEditor.PropertyChanged += OnEditorPropertyChanged;
            context.MapView.GeoViewDoubleTapped += OnDoubleTapped;

            geometryEditor.Start(options.GeometryType);
        }
        catch
        {
            try { Stop(); } catch { /* the original failure is the one worth reporting */ }
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        var view = mapView!;
        var geometryEditor = editor!;
        var resultLayer = results;
        var owned = OwnsSession(geometryEditor);

        mapView = null;
        editor = null;
        results = null;
        ownTool = null;
        clearResultOnNextVertex = false;

        CleanupSteps.RunAll(
            () => view.GeoViewDoubleTapped -= OnDoubleTapped,
            () => geometryEditor.PropertyChanged -= OnEditorPropertyChanged,
            () => { if (owned) geometryEditor.Stop(); },
            () => resultLayer?.Clear(),
            interactionLock.Release);
    }

    private void OnDoubleTapped(object? sender, GeoViewInputEventArgs e)
    {
        var geometryEditor = editor;
        var current = geometryEditor?.Geometry;
        if (geometryEditor is null || !OwnsSession(geometryEditor) || current is null || current.IsEmpty || current.GeometryType != options.GeometryType)
        {
            // No sketch of ours in progress: leave the double-tap to the map (so it can zoom) or
            // to whichever tool is drawing.
            return;
        }

        e.Handled = true;

        completingSketch = true;
        try
        {
            var sketch = geometryEditor.Stop();

            // The sketch is finished: free the map so the user can pan or zoom before the next one.
            interactionLock.Release();

            if (!MeasurementGeometry.HasMinimumVertices(sketch, options.MinVertexCount))
            {
                options.OnInvalidGeometry();
                messenger.Send(MeasurementGeometry.Empty(options.GeometryType));
            }
            else
            {
                results?.Show(sketch, options.ResultSymbol);
                clearResultOnNextVertex = true;
            }

            geometryEditor.Start(options.GeometryType);
        }
        catch (Exception ex)
        {
            errorReporter.Report(options.Title, ex);
        }
        finally
        {
            completingSketch = false;
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GeometryEditor.Geometry)) return;

        try
        {
            var view = mapView;
            var geometryEditor = editor;
            if (view is null || geometryEditor is null) return;

            if (!OwnsSession(geometryEditor))
            {
                // Another tool has taken the editor: do not measure its drawing or hold the map.
                interactionLock.Release();
                return;
            }

            var geometry = geometryEditor.Geometry;

            if (geometry is null || geometry.IsEmpty)
            {
                // No vertices (new sketch, or the user undid them all): leave the map free.
                interactionLock.Release();

                // If the user undid their vertices, reset the live readings to zero. Straight after
                // a completed sketch the panel keeps showing that result until the next one starts.
                if (!clearResultOnNextVertex && !completingSketch)
                {
                    messenger.Send(MeasurementGeometry.Empty(options.GeometryType));
                }

                return;
            }

            // First vertex of a sketch: lock until it is completed. Idempotent for later vertices.
            interactionLock.Lock(view);

            if (clearResultOnNextVertex)
            {
                // Remove the previous result only. The editor's geometry is left alone: it now
                // holds the vertex the user just placed.
                clearResultOnNextVertex = false;
                results?.Clear();
            }

            if (MeasurementGeometry.Measure(geometry) is { } progress)
            {
                messenger.Send(progress);
            }
        }
        catch (Exception ex)
        {
            errorReporter.Report(options.Title, ex);
        }
    }

    private bool OwnsSession(GeometryEditor geometryEditor) =>
        ownTool is not null && geometryEditor.IsStarted && ReferenceEquals(geometryEditor.Tool, ownTool);

    private static VertexTool CreateVertexTool() => new()
    {
        Style = new GeometryEditorStyle
        {
            VertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
            SelectedVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
            SelectedMidVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6),
            MidVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Square, System.Drawing.Color.Red, 6),
            VertexTextSymbol = null
        }
    };
}
