using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Symbology;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Operation;

/// <summary>
/// Tap the map to draw a geodesic buffer of the configured radius around that point. Unlike the
/// sketch tools it never touches the geometry editor or the map's pan/zoom settings; it only
/// needs a result layer.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMeasureOperation) })]
public sealed class SheepMovementBufferOperation : IMeasureOperation
{
    private const string Title = "Sheep Movement Buffer";

    private readonly IMessenger messenger;
    private readonly IMeasurementErrorReporter errorReporter;
    private readonly Symbol circleSymbol;

    private MapView? mapView;
    private MeasurementResultLayer? results;
    private double bufferRadius;

    public SheepMovementBufferOperation(
        IMessenger messenger,
        IMeasurementErrorReporter errorReporter,
        IOptions<SheepMovementBufferSettings> bufferSettings)
    {
        this.messenger = messenger;
        this.errorReporter = errorReporter;

        // Start from a valid radius so a tap can never buffer by zero. The live measurement panel
        // replaces it with the user's value (always within the configured range) on start.
        bufferRadius = bufferSettings.Value.MinRadius;

        var circleOutline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Cyan, 2);
        circleSymbol = new SimpleFillSymbol(
            SimpleFillSymbolStyle.BackwardDiagonal,
            System.Drawing.Color.FromArgb(80, 255, 128, 128),
            circleOutline);

        messenger.Register<SheepMovementBufferOperation, RadiusChangedMessage>(
            this, (recipient, message) => recipient.bufferRadius = message.Radius);
    }

    public MeasureTool Tool => MeasureTool.SheepMovementBuffer;

    public void Start(MeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (mapView is not null) Stop();

        results = new MeasurementResultLayer(context.Overlay);
        mapView = context.MapView;
        mapView.GeoViewTapped += OnGeoViewTapped;

        // Sent after the handler is attached: the live measurement panel replies synchronously
        // with the validated radius.
        messenger.Send(new SketchingStartedMessage<MeasureTool>(Tool));
    }

    public void Stop()
    {
        if (mapView is null) return;

        var view = mapView;
        var resultLayer = results;
        mapView = null;
        results = null;

        try
        {
            CleanupSteps.RunAll(
                () => view.GeoViewTapped -= OnGeoViewTapped,
                () => resultLayer?.Clear());
        }
        finally
        {
            messenger.Send(new SketchingCompletedMessage<MeasureTool>(Tool));
        }
    }

    private void OnGeoViewTapped(object? sender, GeoViewInputEventArgs e)
    {
        var resultLayer = results;
        if (resultLayer is null || e.Location is null) return;

        // This tap belongs to the buffer tool; other map tap handlers must not act on it too.
        e.Handled = true;

        try
        {
            resultLayer.Clear();

            if (bufferRadius <= 0) return;

            var geometry = e.Location.BufferGeodetic(bufferRadius, LinearUnits.Meters);
            if (geometry is null || geometry.IsEmpty) return;

            resultLayer.Show(geometry, circleSymbol);

            var circumference = geometry.LengthGeodetic(LinearUnits.Meters, GeodeticCurveType.Geodesic);
            var area = geometry.AreaGeodetic(AreaUnits.SquareMeters, GeodeticCurveType.Geodesic);

            messenger.Send(new DrawingProgressMessage(circumference, area));
        }
        catch (Exception ex)
        {
            errorReporter.Report(Title, ex);
        }
    }
}
