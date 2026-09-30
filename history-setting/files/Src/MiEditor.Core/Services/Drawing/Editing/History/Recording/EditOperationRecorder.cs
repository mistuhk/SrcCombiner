
using Esri.ArcGISRuntime.Data;
using WG.MiEditor.Core.Services.Drawing.Editing.History.FeatureTables;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Model;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History.Recording;

/// <summary>
/// Creates a scope for recording a single operation. Used by editing services so
/// changes can be captured without those services depending on edit history.
/// </summary>
public interface IEditOperationRecorder
{
    /// <summary>
    /// Begins recording an operation. The supplied name is shown to the user when the
    /// operation is undone or redone.
    ///
    /// Each call creates an independent scope and therefore a separate history entry.
    /// Operations spanning multiple services share a single scope rather than
    /// relying on ambient state.
    /// </summary>
    IEditOperationScope Begin(string operationName);
}

/// <summary>
/// Creates scopes that record individual operations. Each scope is independent and
/// owns the operation it captures; the recorder itself maintains no ambient state.
///
/// Operations that should appear as a single history entry share and pass the
/// same scope through the call chain rather than relying on implicit coordination.
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditOperationRecorder) })]
public sealed partial class EditOperationRecorder(
    IEditHistoryJournal journal,
    IFeatureSnapshotFactory snapshotFactory,
    IFeatureStateReader stateReader,
    ICaseContext caseContext,
    IOperationFeedbackService feedback,
    ILoggerService loggerService,
    IEditHistorySwitch historySwitch) : IEditOperationRecorder
{
    /// <summary>
    /// With the history switched off this hands back a scope that does nothing, so a caller's three
    /// recording lines stay exactly as they are and cost nothing: no snapshots taken, no rows read back
    /// from the service, nothing held in memory.
    /// </summary>
    public IEditOperationScope Begin(string operationName) =>
        historySwitch.IsEnabled
            ? new EditOperationScope(journal, snapshotFactory, stateReader, caseContext, feedback, loggerService, operationName)
            : DiscardingScope.Instance;

    /// <summary>
    /// What Begin hands back when the history is off. Stateless, so one instance serves every caller.
    /// </summary>
    private sealed class DiscardingScope : IEditOperationScope
    {
        public static readonly DiscardingScope Instance = new();

        public void MarkInsert(string layerName, Feature feature) { }

        public void ResolveInsertedObjectIds(string layerName, IReadOnlyList<AppliedEdit> applied) { }

        public Task CaptureUpdateBeforeAsync(string layerName, Feature feature) => Task.CompletedTask;

        public Task CaptureDeleteAsync(string layerName, Feature feature) => Task.CompletedTask;

        public Task CommitAsync() => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed partial class EditOperationScope(
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

        public void ResolveInsertedObjectIds(string layerName, IReadOnlyList<AppliedEdit> applied)
        {
            ArgumentNullException.ThrowIfNull(applied);

            // This must be called before CommitAsync. Once commit completes, all possible ID
            // mappings have been resolved and the tracked state discarded, so late updates
            // cannot be applied.
            if (finished)
            {
                loggerService.Warn(
                    $"Edit history: '{operationName}' was told what the service applied only after it had been "
                    + "committed, so those object ids were not used. ResolveInsertedObjectIds has to run before CommitAsync.");
                return;
            }

            var inserts = pending
                .Where(p => p.Kind == FeatureChangeKind.Insert
                    && p.AppliedObjectId is null
                    && string.Equals(p.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (inserts.Count == 0)
            {
                return;
            }

            var ids = AppliedEdit.MatchInsertedIds(inserts.Count, applied);

            if (ids is null)
            {
                loggerService.Warn(
                    $"Edit history: '{operationName}' added {inserts.Count} row(s) to {layerName} but the service "
                    + "reported a different number, so their object ids cannot be matched and they are not recorded.");
                return;
            }

            for (var i = 0; i < inserts.Count; i++)
            {
                inserts[i].AppliedObjectId = ids[i];
            }
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
            // Prevents the same operation being recorded twice, which can happen if a caller
            // retries after a failure.
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
                    // Logged directly here instead of relying on callers to relay the message.
                    loggerService.Warn(
                        $"Edit history: '{operationName}' is larger than the whole history memory limit, so it cannot be undone.");

                    feedback.NotifyWarning(
                        "Undo",
                        $"{operationName} was saved, but it is too large to be undone.");
                }
            }
            finally
            {
                // Always marked complete, even when commit fails, so a scope cannot be committed
                // again after an exception.
                finished = true;
                pending.Clear();
            }
        }

        public void Dispose()
        {
            // Uncommitted operations are assumed to have failed or been abandoned, so no
            // history is recorded. Everything captured is local to this scope and can be
            // discarded safely.
            finished = true;
            pending.Clear();
        }

        /// <summary>
        /// The object ID assigned to a newly inserted row or null if unknown. Service
        /// tables obtain it from ApplyEdits results; local geodatabases assign it
        /// during insertion. Unresolved temporary IDs remain negative and are rejected
        /// rather than recorded.
        /// </summary>
        private long? ResolveInsertedObjectId(Pending item)
        {
            var objectId = item.AppliedObjectId ?? snapshotFactory.ReadObjectId(item.Feature);

            if (objectId > 0)
            {
                return objectId;
            }

            loggerService.Warn(
                $"Edit history: '{operationName}' inserted a row into {item.LayerName} whose OBJECTID is "
                + $"{objectId}, which is not a stored id, so it is not recorded."
                + (item.AppliedObjectId is null && objectId < 0
                    ? " A negative id means the row went to a service table and ResolveInsertedObjectIds was not called"
                    + " with the ApplyEdits result before CommitAsync."
                    : string.Empty));

            return null;
        }

        private async Task<FeatureChange?> BuildChangeAsync(Pending item)
        {
            switch (item.Kind)
            {
                case FeatureChangeKind.Delete:
                    return FeatureChange.Delete(item.Before!);

                case FeatureChangeKind.Insert:
                    {
                        var objectId = ResolveInsertedObjectId(item);

                        if (objectId is null)
                        {
                            return null;
                        }

                        // Read the stored row rather than the in-memory feature. ApplyEditsAsync does not
                        // update service-generated values such as object IDs and tracking fields and this
                        // keeps inserts and updates on the same read-back path.
                        var after = await stateReader.ReadAsync(new FeatureIdentity(item.LayerName, objectId.Value));

                        if (after is null)
                        {
                            loggerService.Warn(
                                $"Edit history: '{operationName}' inserted OBJECTID {objectId} into {item.LayerName}, "
                                + "but it could not be read back, so it is not recorded.");
                            return null;
                        }

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

    /// <summary>
    /// One row touched by an operation. Inserted rows do not have a final object ID when recorded, 
    /// so it is populated later by <see cref="IEditOperationScope.ResolveInsertedObjectIds(string, IReadOnlyList{AppliedEdit})"/>.
    /// </summary>
    private sealed class Pending(
        FeatureChangeKind kind,
        string layerName,
        Feature feature,
        FeatureSnapshot? before)
    {
        public FeatureChangeKind Kind { get; } = kind;

        public string LayerName { get; } = layerName;

        public Feature Feature { get; } = feature;

        public FeatureSnapshot? Before { get; } = before;

        public long? AppliedObjectId { get; set; }
    }
}
