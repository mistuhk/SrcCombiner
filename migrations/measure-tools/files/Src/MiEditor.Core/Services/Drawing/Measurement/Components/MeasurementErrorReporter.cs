using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Notifications;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

public interface IMeasurementErrorReporter
{
    /// <summary>Logs <paramref name="exception"/> and tells the user, without ever throwing.</summary>
    void Report(string title, Exception exception);
}

/// <summary>
/// Single place where measurement failures are logged and surfaced. Used from map event handlers,
/// where an unhandled exception would otherwise take the app down.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMeasurementErrorReporter) })]
public sealed class MeasurementErrorReporter(
    ILoggerService logger,
    INotificationManager notificationManager) : IMeasurementErrorReporter
{
    public const string GenericMessage = "Something went wrong with the measurement. Please try again.";

    public void Report(string title, Exception exception)
    {
        try
        {
            logger.Error(exception, $"{title} failed.");
        }
        catch
        {
            // Logging must never turn a handled failure into an unhandled one.
        }

        try
        {
            var message = exception is MeasurementUnavailableException ? exception.Message : GenericMessage;
            notificationManager.Show(title, message, NotificationType.Error);
        }
        catch
        {
            // As above: reporting is best effort.
        }
    }
}
