using System.Runtime.ExceptionServices;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>
/// Runs a sequence of teardown steps so that one failing step never prevents the rest from
/// running. A single failure is rethrown with its original stack; several are aggregated.
/// </summary>
public static class CleanupSteps
{
    public static void RunAll(params Action[] steps)
    {
        List<Exception>? errors = null;

        foreach (var step in steps)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is null) return;

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        throw new AggregateException("One or more measurement clean-up steps failed.", errors);
    }
}
