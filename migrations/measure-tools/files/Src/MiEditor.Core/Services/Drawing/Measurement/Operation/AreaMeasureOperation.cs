using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Geometry;
using Microsoft.Extensions.Options;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Operation;

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMeasureOperation) })]
public sealed class AreaMeasureOperation : IMeasureOperation
{
    private const string Title = "Area Measurement";
    private const int MinVertexCount = 3;

    private readonly IMessenger messenger;
    private readonly SketchMeasurement sketch;

    public AreaMeasureOperation(
        IOptions<DrawingSymbolsSettings> symbolSettings,
        IDrawingSymbolColourParser drawingSymbolColourParser,
        IMessenger messenger,
        INotificationManager notificationManager,
        IMeasurementErrorReporter errorReporter,
        ILoggerService logger)
    {
        this.messenger = messenger;

        var resultSymbol = MeasurementSymbols.Polygon(
            symbolSettings.Value?.Polygon, drawingSymbolColourParser, logger, $"{nameof(DrawingSymbolsSettings)}.{nameof(DrawingSymbolsSettings.Polygon)}");

        sketch = new SketchMeasurement(messenger, errorReporter, new SketchMeasurementOptions(
            Title,
            GeometryType.Polygon,
            MinVertexCount,
            resultSymbol,
            OnInvalidGeometry: () => notificationManager.Show(
                Title,
                $"An area requires minimum of {MinVertexCount} points.",
                NotificationType.Error)));
    }

    public MeasureTool Tool => MeasureTool.Area;

    public void Start(MeasurementContext context)
    {
        sketch.Start(context);
        messenger.Send(new SketchingStartedMessage<MeasureTool>(Tool));
    }

    public void Stop()
    {
        if (!sketch.IsRunning) return;

        try
        {
            sketch.Stop();
        }
        finally
        {
            messenger.Send(new SketchingCompletedMessage<MeasureTool>(Tool));
        }
    }
}
