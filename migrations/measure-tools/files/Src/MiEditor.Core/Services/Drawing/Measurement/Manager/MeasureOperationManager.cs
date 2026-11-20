using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Drawing.Measurement.Operation;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Manager;

/// <summary>
/// Routes the active measure tool to its operation. Activation and deactivation are serialised so
/// rapid taps cannot interleave, a fresh context (with the overlay re-attached if something
/// removed it) is fetched on every activation, and the current operation is forgotten before it
/// is stopped so a failing stop can never wedge the manager.
/// <para>
/// Failures propagate to the caller, which owns rolling back the toolbar state and telling the
/// user. Continuations deliberately stay on the caller's (UI) context because operations touch
/// the map view.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IMeasureOperationManager) })]
public sealed class MeasureOperationManager : IMeasureOperationManager
{
    private readonly IMeasurementContextProvider contextProvider;
    private readonly IReadOnlyDictionary<MeasureTool, IMeasureOperation> operations;
    private readonly SemaphoreSlim gate = new(1, 1);

    private IMeasureOperation? current;

    public MeasureOperationManager(IMeasurementContextProvider contextProvider, IEnumerable<IMeasureOperation> measureOperations)
    {
        this.contextProvider = contextProvider;
        operations = measureOperations.ToDictionary(operation => operation.Tool);
    }

    public async Task Activate(MeasureTool tool)
    {
        if (!operations.TryGetValue(tool, out var next))
        {
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "No measure operation is registered for this tool.");
        }

        await gate.WaitAsync();
        try
        {
            if (ReferenceEquals(current, next)) return;

            StopCurrent();

            var context = await contextProvider.GetAsync();
            next.Start(context);
            current = next;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task Deactivate()
    {
        await gate.WaitAsync();
        try
        {
            StopCurrent();
        }
        finally
        {
            gate.Release();
        }
    }

    private void StopCurrent()
    {
        var operation = current;
        current = null;
        operation?.Stop();
    }
}
