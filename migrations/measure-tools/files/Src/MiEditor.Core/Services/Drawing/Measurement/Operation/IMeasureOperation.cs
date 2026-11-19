using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Operation;

public interface IMeasureOperation
{
    /// <summary>The toolbar tool this operation implements.</summary>
    MeasureTool Tool { get; }

    /// <summary>
    /// Starts a measurement session against <paramref name="context"/>. If this throws, the
    /// operation must have left nothing attached to the map.
    /// </summary>
    void Start(MeasurementContext context);

    /// <summary>
    /// Ends the session. Safe to call when not started. Every teardown step runs even if an
    /// earlier one fails; failures are rethrown once teardown is complete.
    /// </summary>
    void Stop();
}
