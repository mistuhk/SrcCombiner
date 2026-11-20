namespace WG.MiEditor.Core.Services.Drawing.Measurement.Context;

/// <summary>
/// Supplies a ready-to-use <see cref="MeasurementContext"/>. Implementations guarantee that the
/// returned overlay is attached to the returned map, or throw
/// <see cref="MeasurementUnavailableException"/>.
/// </summary>
public interface IMeasurementContextProvider
{
    Task<MeasurementContext> GetAsync(CancellationToken cancellationToken = default);
}
