using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Core.Services.Drawing.Editing.Managers;
using WG.MiEditor.Core.Services.Feedback;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Shared.Messages.Map;
using WG.MiEditor.Shared.Messages.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Drives undo and redo: the safety checks in order, then the conflict check and the applier.
/// <para>
/// Scoped because the applier and the conflict check need IFeatureLayerService, which is
/// Scoped. No scope is ever created in this application, so this is a singleton in practice,
/// which is what the journal subscription relies on.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditHistoryService) })]
public sealed class EditHistoryService : IEditHistoryService
{
    private const string SpinnerKey = "EditHistory";

    private readonly IEditHistoryJournal journal;
    private readonly IEditConflictDetector conflictDetector;
    private readonly IFeatureChangeApplier applier;
    private readonly IEditingOperationsManager editingOperations;
    private readonly IEditSelectionClearer selectionClearer;
    private readonly IOperationFeedbackService feedback;
    private readonly ICaseContext caseContext;
    private readonly IMessenger messenger;
    private readonly ILoggerService loggerService;

    // Guards a second press while one is in flight. The toolbar button fires without waiting,
    // so a double click would otherwise start two of these at once.
    private bool running;

#pragma warning disable S107
    public EditHistoryService(
        IEditHistoryJournal journal,
        IEditConflictDetector conflictDetector,
        IFeatureChangeApplier applier,
        IEditingOperationsManager editingOperations,
        IEditSelectionClearer selectionClearer,
        IOperationFeedbackService feedback,
        ICaseContext caseContext,
        IMessenger messenger,
        ILoggerService loggerService)
#pragma warning restore S107
    {
        this.journal = journal;
        this.conflictDetector = conflictDetector;
        this.applier = applier;
        this.editingOperations = editingOperations;
        this.selectionClearer = selectionClearer;
        this.feedback = feedback;
        this.caseContext = caseContext;
        this.messenger = messenger;
        this.loggerService = loggerService;

        journal.Changed += OnJournalChanged;

        // The history belongs to one case. Clearing on each of these is the primary guard;
        // the case key stamped on every entry is the second, for a message that never arrives.
        messenger.Register<EditHistoryService, SelectedCaseMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, SelectedCaseStartEditingMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, SelectedCaseStopEditingMessage>(this, (r, _) => r.ClearHistory());
        messenger.Register<EditHistoryService, ExitCaseMessage>(this, (r, _) => r.ClearHistory());
    }

    public bool CanUndo => journal.CanUndo;

    public bool CanRedo => journal.CanRedo;

    public Task UndoAsync() => RunAsync(EditHistoryDirection.Undo);

    public Task RedoAsync() => RunAsync(EditHistoryDirection.Redo);

    public void ClearHistory() => journal.Clear();

    private async Task RunAsync(EditHistoryDirection direction)
    {
        if (running)
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

        // Recorded against a different case. Clearing on the lifecycle messages should already
        // have prevented this, so reaching here means one was missed.
        if (!string.Equals(entry.CaseKey, caseContext.CaseNo ?? string.Empty, StringComparison.Ordinal))
        {
            loggerService.Warn(
                $"Edit history: '{entry.OperationName}' belongs to case {entry.CaseKey} but the open case is {caseContext.CaseNo}. The history has been cleared.");
            journal.Clear();
            return;
        }

        // An editing tool may be holding the very feature that is about to change, so stand the
        // tools down before touching anything.
        await editingOperations.DeactivateAll(null);

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
            // The data no longer matches anything recorded, so keeping the history would invite
            // a later press to make it worse.
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

        // A restored row carries a new object id, so anything still selected would be pointing at
        // a row that no longer exists.
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
            // Redo entries are ordered and later ones can depend on earlier ones, so the rest of
            // the redo side goes with it.
            journal.ClearRedo();
        }

        // Anything else in either list touching the same rows is now just as unsafe.
        journal.DropEntriesReferencing(conflict.Conflicting);

        feedback.NotifyWarning(
            Title(direction),
            $"{entry.OperationName} cannot be {Past(direction).ToLowerInvariant()}. {conflict.Reason} It has been removed from your history.");
    }

    private static string Title(EditHistoryDirection direction) =>
        direction == EditHistoryDirection.Undo ? "Undo" : "Redo";

    private static string Past(EditHistoryDirection direction) =>
        direction == EditHistoryDirection.Undo ? "Undone" : "Redone";

    private void OnJournalChanged(object? sender, EventArgs e) =>
        messenger.Send(new EditHistoryChangedMessage(journal.CanUndo, journal.CanRedo));
}
