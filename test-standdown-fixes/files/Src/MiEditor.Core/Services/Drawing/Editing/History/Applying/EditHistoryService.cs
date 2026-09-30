
using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Core.Services.Drawing.Editing.History.FeatureTables;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Model;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Shared.Messages.Editing;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History.Applying;

/// <summary>
/// The entry point for undo and redo. Coordinates operation sequencing, validation
/// and application while hiding the underlying history and reversal mechanics from
/// the UI.
/// </summary>
public interface IEditHistoryService
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    Task UndoAsync();
    Task RedoAsync();

    /// <summary>
    /// Clears all recorded history. Called when the active case changes, because
    /// history must never be carried between cases.
    /// </summary>
    void ClearHistory();
}

/// <summary>
/// Orchestrates undo and redo by running validation, conflict checks and change
/// application in the correct order.
///
/// Scoped because the conflict checker and applier depend on
/// IFeatureLayerService. In practice this behaves as a singleton because the
/// application creates only one scope, which the journal subscription relies on.
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditHistoryService) })]
public sealed class EditHistoryService : IEditHistoryService
{
    private const string SpinnerKey = "EditHistory";

    private readonly IEditHistoryJournal journal;
    private readonly IEditConflictDetector conflictDetector;
    private readonly IFeatureChangeApplier applier;
    private readonly IEditSelectionClearer selectionClearer;
    private readonly IEditHistorySwitch historySwitch;
    private readonly IOperationFeedbackService feedback;
    private readonly ICaseContext caseContext;
    private readonly IMessenger messenger;
    private readonly ILoggerService loggerService;

    // Prevents a second undo or redo starting while one is already in progress. The
    // toolbar does not wait for completion, so a double-click could otherwise run
    // multiple operations concurrently.
    private bool running;

#pragma warning disable S107
    public EditHistoryService(
        IEditHistoryJournal journal,
        IEditConflictDetector conflictDetector,
        IFeatureChangeApplier applier,
        IEditSelectionClearer selectionClearer,
        IEditHistorySwitch historySwitch,
        IOperationFeedbackService feedback,
        ICaseContext caseContext,
        IMessenger messenger,
        ILoggerService loggerService)
#pragma warning restore S107
    {
        this.journal = journal;
        this.conflictDetector = conflictDetector;
        this.applier = applier;
        this.selectionClearer = selectionClearer;
        this.historySwitch = historySwitch;
        this.feedback = feedback;
        this.caseContext = caseContext;
        this.messenger = messenger;
        this.loggerService = loggerService;

        journal.Changed += OnJournalChanged;

        // Switching the feature off discards what was recorded. Keeping it would offer, on a later
        // switch back on, an undo of operations the officer may have built on since, with only the
        // conflict check between them and a surprise.
        historySwitch.Changed += OnSwitchChanged;

        // History is scoped to a single case. Clearing on each case change is the primary
        // safeguard; the case key recorded with each entry is a fallback for a missed event.
        messenger.Register<EditHistoryService, SelectedCaseMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, SelectedCaseStartEditingMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, SelectedCaseStopEditingMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, ExitCaseMessage>(this, (r, _) => r.ClearHistory());
    }

    public bool CanUndo => historySwitch.IsEnabled && journal.CanUndo;

    public bool CanRedo => historySwitch.IsEnabled && journal.CanRedo;

    public Task UndoAsync() => RunAsync(EditHistoryDirection.Undo);

    public Task RedoAsync() => RunAsync(EditHistoryDirection.Redo);

    public void ClearHistory() => journal.Clear();

    private async Task RunAsync(EditHistoryDirection direction)
    {
        if (running)
        {
            return;
        }

        // Refused rather than relied on the buttons being hidden, so that a keyboard shortcut added
        // later cannot leave the feature live while it looks switched off.
        if (!historySwitch.IsEnabled)
        {
            return;
        }

        running = true;

        try
        {
            await feedback.RunAsync(
                SpinnerKey,
                () => ApplyAsync(direction),
                errorTitle: Title(direction),
                errorContext: Title(direction));
        }
        finally
        {
            running = false;
        }
    }

    private async Task ApplyAsync(EditHistoryDirection direction)
    {
        var entry = direction == EditHistoryDirection.Undo ? journal.PeekUndo() : journal.PeekRedo();

        if (entry is null)
        {
            return;
        }

        // Recorded for a different case. Case-change events should have cleared the
        // history already, so reaching this point indicates a missed notification.

        if (!string.Equals(entry.CaseKey, caseContext.CaseNo ?? string.Empty, StringComparison.Ordinal))
        {
            loggerService.Warn(
                $"Edit history: '{entry.OperationName}' belongs to case {entry.CaseKey} but the open case is {caseContext.CaseNo}. The history has been cleared.");
            journal.Clear();
            return;
        }

        // Editing tools may still hold references to features that are about to change, so they are
        // stood down before the operation is applied. Awaited rather than merely asked for: standing
        // a tool down is asynchronous, and without waiting the applier could write while a tool
        // still holds the feature. This requires a recipient that replies, which in the app is the
        // editing toolbar and in a test is a handler the test registers.
        await messenger.Send<ResetToolbarState>();

        var conflict = await conflictDetector.CheckAsync(entry, direction);

        if (conflict.HasConflict)
        {
            Refuse(direction, entry, conflict);
            return;
        }

        var changes = direction == EditHistoryDirection.Undo ? entry.InverseChanges() : entry.Changes;
        var result = await applier.ApplyAsync(changes);

        if (!result.Success)
        {
            // The recorded state no longer matches the data, so retaining the history risks
            // making a later undo or redo cause further damage.
            feedback.NotifyError(
                Title(direction),
                $"{entry.OperationName} could not be {Past(direction).ToLowerInvariant()}. The edit history has been cleared and the map will reload. {result.Error}");

            journal.Clear();
            messenger.Send(new RefreshMessage());
            return;
        }

        foreach (var remap in result.Remaps)
        {
            journal.RemapObjectId(remap.LayerName, remap.OldObjectId, remap.NewObjectId);
        }

        if (direction == EditHistoryDirection.Undo)
        {
            journal.PopUndoToRedo();
        }
        else
        {
            journal.PopRedoToUndo();
        }

        // Restored rows receive new object IDs, so any existing selection would point to
        // rows that no longer exist.
        await selectionClearer.ClearAsync();

        feedback.NotifySuccess(Title(direction), $"{Past(direction)}: {entry.OperationName}");
    }

    private void Refuse(EditHistoryDirection direction, EditOperationEntry entry, ConflictResult conflict)
    {
        loggerService.Info(
            $"Edit history: '{entry.OperationName}' was not {Past(direction).ToLowerInvariant()} because {conflict.Reason}");

        if (direction == EditHistoryDirection.Undo)
        {
            journal.DiscardUndo();
        }
        else
        {
            // Later redo entries may depend on earlier ones, so invalidating one requires
            // discarding the remaining redo history as well.
            journal.ClearRedo();
        }

        // Any other history entries touching the same rows are now unsafe and must be removed.
        journal.DropEntriesReferencing(conflict.Conflicting);

        feedback.NotifyWarning(
            Title(direction),
            $"{entry.OperationName} cannot be {Past(direction).ToLowerInvariant()}. {conflict.Reason} It has been removed from your history.");
    }

    private static string Title(EditHistoryDirection direction) =>
        direction == EditHistoryDirection.Undo ? "Undo" : "Redo";

    private static string Past(EditHistoryDirection direction) =>
        direction == EditHistoryDirection.Undo ? "Undone" : "Redone";

    private void OnSwitchChanged(object? sender, EventArgs e)
    {
        if (!historySwitch.IsEnabled)
        {
            ClearHistory();
        }
    }

    private void OnJournalChanged(object? sender, EventArgs e) =>
        messenger.Send(new EditHistoryChangedMessage(journal.CanUndo, journal.CanRedo));
}
