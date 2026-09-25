using Esri.ArcGISRuntime.Data;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Hands out the scope that watches one operation.
/// <para>
/// Each call to Begin returns an independent scope and the recorder holds no state of its own.
/// An earlier version kept the operation in progress in a field so that nested calls could join
/// it, which was written for a multi service save two deliveries away. It was the source of two
/// defects: one exception on the commit path left that field pointing at a dead scope and
/// silently stopped all recording, and two overlapping operations recorded into one entry. When
/// a single entry spanning several services is genuinely needed, pass the scope down the call
/// chain rather than reintroducing ambient state.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditOperationRecorder) })]
public sealed class EditOperationRecorder(
    IEditHistoryJournal journal,
    IFeatureSnapshotFactory snapshotFactory,
    IFeatureStateReader stateReader,
    ICaseContext caseContext,
    IOperationFeedbackService feedback,
    ILoggerService loggerService) : IEditOperationRecorder
{
    public IEditOperationScope Begin(string operationName) =>
        new Scope(journal, snapshotFactory, stateReader, caseContext, feedback, loggerService, operationName);

    private sealed class Scope(
        IEditHistoryJournal journal,
        IFeatureSnapshotFactory snapshotFactory,
        IFeatureStateReader stateReader,
        ICaseContext caseContext,
        IOperationFeedbackService feedback,
        ILoggerService loggerService,
        string operationName) : IEditOperationScope
    {
        private readonly List<Pending> pending = [];
        private bool finished;

        public void MarkInsert(string layerName, Feature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);
            pending.Add(new Pending(FeatureChangeKind.Insert, layerName, feature, null));
        }

        public async Task CaptureUpdateBeforeAsync(string layerName, Feature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);

            var before = await snapshotFactory.CreateAsync(layerName, feature);
            pending.Add(new Pending(FeatureChangeKind.Update, layerName, feature, before));
        }

        public async Task CaptureDeleteAsync(string layerName, Feature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);

            var before = await snapshotFactory.CreateAsync(layerName, feature);
            pending.Add(new Pending(FeatureChangeKind.Delete, layerName, feature, before));
        }

        public async Task CommitAsync()
        {
            // Committing twice would record the operation twice, and a caller that retries after
            // a failure is a realistic way to reach here.
            if (finished)
            {
                loggerService.Warn(
                    $"Edit history: '{operationName}' was committed more than once. The second commit recorded nothing.");
                return;
            }

            try
            {
                var changes = new List<FeatureChange>(pending.Count);

                foreach (var item in pending)
                {
                    var change = await BuildChangeAsync(item);

                    if (change is not null)
                    {
                        changes.Add(change);
                    }
                }

                if (changes.Count == 0)
                {
                    return;
                }

                var entry = EditOperationEntry.Create(
                    operationName,
                    caseContext.CaseNo ?? string.Empty,
                    changes,
                    DateTime.UtcNow);

                if (!journal.Record(entry))
                {
                    // Told here rather than returned for the caller to relay. Every service that
                    // records would otherwise have to remember to, and the first one did not.
                    loggerService.Warn(
                        $"Edit history: '{operationName}' is larger than the whole history budget, so it cannot be undone.");

                    feedback.NotifyWarning(
                        "Undo",
                        $"{operationName} was saved, but it is too large to be undone.");
                }
            }
            finally
            {
                // Marked finished whatever happened, including on the way out of an exception, so
                // a scope that failed to record cannot be committed again.
                finished = true;
                pending.Clear();
            }
        }

        public void Dispose()
        {
            // Not committed means the operation threw or was abandoned, so nothing is recorded.
            // Nothing outside this scope was touched, so there is nothing else to put back.
            finished = true;
            pending.Clear();
        }

        private async Task<FeatureChange?> BuildChangeAsync(Pending item)
        {
            switch (item.Kind)
            {
                case FeatureChangeKind.Delete:
                    return FeatureChange.Delete(item.Before!);

                case FeatureChangeKind.Insert:
                {
                    // The row exists now, so read it back rather than trusting the object held in
                    // memory. That is also how the id assigned by the service is picked up.
                    var after = await snapshotFactory.CreateAsync(item.LayerName, item.Feature);
                    return FeatureChange.Insert(after);
                }

                case FeatureChangeKind.Update:
                {
                    var live = await stateReader.ReadAsync(item.Before!.Identity);

                    if (live is null)
                    {
                        loggerService.Warn(
                            $"Edit history: '{operationName}' updated a row in {item.LayerName} that could not be read back, so it is not recorded.");
                        return null;
                    }

                    return FeatureChange.Update(item.Before, live);
                }

                default:
                    return null;
            }
        }
    }

    private sealed record Pending(
        FeatureChangeKind Kind,
        string LayerName,
        Feature Feature,
        FeatureSnapshot? Before);
}
