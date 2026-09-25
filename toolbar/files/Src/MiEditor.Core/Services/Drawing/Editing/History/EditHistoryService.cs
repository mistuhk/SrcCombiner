using CommunityToolkit.Mvvm.Messaging;
using WG.MiEditor.Messages.CaseManagement;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Messages.Toolbar;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Drives undo and redo. At this stage it owns two things that are complete in their own
/// right: telling the toolbar whether undo and redo are available, and forgetting the
/// history when the case changes.
/// <para>
/// Reversing an operation needs the conflict check and the applier, which arrive with the
/// recording work. Until then nothing records, so the journal is always empty, CanUndo is
/// always false and the buttons stay disabled. UndoAsync and RedoAsync therefore guard and
/// return rather than pretending to act: moving an entry between the lists without
/// reversing anything would leave the journal describing a state the data is not in.
/// </para>
/// <para>
/// Scoped because the applier and the conflict check will need IFeatureLayerService, which
/// is Scoped. No scope is ever created in this application, so this is a singleton in
/// practice, which is what the journal subscription below relies on.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditHistoryService) })]
public sealed class EditHistoryService : IEditHistoryService
{
    private readonly IEditHistoryJournal journal;
    private readonly IMessenger messenger;

    public EditHistoryService(IEditHistoryJournal journal, IMessenger messenger)
    {
        this.journal = journal;
        this.messenger = messenger;

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

    public Task UndoAsync()
    {
        if (!journal.CanUndo)
        {
            return Task.CompletedTask;
        }

        // Deliberately does not pop. Applying the inverse arrives with the recording work,
        // and until it does, popping would tell the journal an operation had been reversed
        // when the data still holds it.
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        if (!journal.CanRedo)
        {
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public void ClearHistory() => journal.Clear();

    private void OnJournalChanged(object? sender, EventArgs e) =>
        messenger.Send(new EditHistoryChangedMessage(journal.CanUndo, journal.CanRedo));
}
