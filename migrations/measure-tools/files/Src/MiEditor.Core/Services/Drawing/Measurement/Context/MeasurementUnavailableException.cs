namespace WG.MiEditor.Core.Services.Drawing.Measurement.Context;

/// <summary>
/// Raised when a measurement cannot start because the map is not in a usable state. The
/// message is written for the user and is safe to show in a notification.
/// </summary>
public sealed class MeasurementUnavailableException : Exception
{
    public MeasurementUnavailableException(string message) : base(message) { }

    public MeasurementUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
