using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Keeps the most recent operations available for undo and redo. Backed by linked lists
/// rather than a stack because the oldest entry has to be evicted once the cap is reached,
/// which a stack cannot do. Entries move between the two lists rather than being copied, and
/// recording clears redo, so the combined depth never exceeds the cap.
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IEditHistoryJournal) })]
public sealed class EditHistoryJournal : IEditHistoryJournal
{
    public const int DefaultMaxDepth = 10;

    private readonly LinkedList<EditOperationEntry> undo = new();
    private readonly LinkedList<EditOperationEntry> redo = new();
    private readonly int maxDepth;
    private readonly long maxEntrySizeInBytes;

    public EditHistoryJournal()
        : this(DefaultMaxDepth)
    {
    }

    public EditHistoryJournal(int maxDepth, long maxEntrySizeInBytes = 8L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);

        this.maxDepth = maxDepth;
        this.maxEntrySizeInBytes = maxEntrySizeInBytes;
    }

    public event EventHandler? Changed;

    public bool CanUndo => undo.Last?.Value.IsUndoable == true;

    public bool CanRedo => redo.Count > 0;

    public int UndoDepth => undo.Count;

    public int RedoDepth => redo.Count;

    public void Record(EditOperationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var toRecord = entry.IsUndoable && entry.ApproximateSizeInBytes() > maxEntrySizeInBytes
            ? entry.AsNotUndoable("The operation is too large to undo.")
            : entry;

        undo.AddLast(toRecord);

        while (undo.Count > maxDepth)
        {
            undo.RemoveFirst();
        }

        redo.Clear();

        RaiseChanged();
    }

    public EditOperationEntry? PeekUndo() => undo.Last?.Value;

    public EditOperationEntry? PeekRedo() => redo.Last?.Value;

    public EditOperationEntry? PopUndoToRedo()
    {
        var entry = RemoveLast(undo);
        if (entry is null)
        {
            return null;
        }

        redo.AddLast(entry);
        RaiseChanged();

        return entry;
    }

    public EditOperationEntry? PopRedoToUndo()
    {
        var entry = RemoveLast(redo);
        if (entry is null)
        {
            return null;
        }

        undo.AddLast(entry);
        RaiseChanged();

        return entry;
    }

    public EditOperationEntry? DiscardUndo()
    {
        var entry = RemoveLast(undo);
        if (entry is not null)
        {
            RaiseChanged();
        }

        return entry;
    }

    public void DiscardRedoFrom()
    {
        if (redo.Count == 0)
        {
            return;
        }

        redo.Clear();
        RaiseChanged();
    }

    public void RemapObjectId(string layerName, long oldObjectId, long newObjectId)
    {
        if (oldObjectId == newObjectId)
        {
            return;
        }

        var remapped = Remap(undo, layerName, oldObjectId, newObjectId);
        remapped |= Remap(redo, layerName, oldObjectId, newObjectId);

        if (remapped)
        {
            RaiseChanged();
        }
    }

    public int DropEntriesReferencing(IReadOnlyCollection<FeatureIdentity> identities)
    {
        if (identities.Count == 0)
        {
            return 0;
        }

        var dropped = Drop(undo, identities) + Drop(redo, identities);

        if (dropped > 0)
        {
            RaiseChanged();
        }

        return dropped;
    }

    public void Clear()
    {
        if (undo.Count == 0 && redo.Count == 0)
        {
            return;
        }

        undo.Clear();
        redo.Clear();

        RaiseChanged();
    }

    private static EditOperationEntry? RemoveLast(LinkedList<EditOperationEntry> list)
    {
        var last = list.Last;
        if (last is null)
        {
            return null;
        }

        list.RemoveLast();

        return last.Value;
    }

    private static bool Remap(
        LinkedList<EditOperationEntry> list,
        string layerName,
        long oldObjectId,
        long newObjectId)
    {
        var changed = false;

        for (var node = list.First; node is not null; node = node.Next)
        {
            var entry = node.Value;
            var changes = new List<FeatureChange>(entry.Changes.Count);
            var entryChanged = false;

            foreach (var change in entry.Changes)
            {
                var before = RemapSnapshot(change.Before, layerName, oldObjectId, newObjectId);
                var after = RemapSnapshot(change.After, layerName, oldObjectId, newObjectId);

                entryChanged |= !ReferenceEquals(before, change.Before) || !ReferenceEquals(after, change.After);
                changes.Add(change with { Before = before, After = after });
            }

            if (entryChanged)
            {
                node.Value = entry with { Changes = changes };
                changed = true;
            }
        }

        return changed;
    }

    private static FeatureSnapshot? RemapSnapshot(
        FeatureSnapshot? snapshot,
        string layerName,
        long oldObjectId,
        long newObjectId)
    {
        if (snapshot is null
            || snapshot.Identity.ObjectId != oldObjectId
            || !string.Equals(snapshot.Identity.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
        {
            return snapshot;
        }

        return snapshot with { Identity = snapshot.Identity with { ObjectId = newObjectId } };
    }

    private static int Drop(LinkedList<EditOperationEntry> list, IReadOnlyCollection<FeatureIdentity> identities)
    {
        var dropped = 0;
        var node = list.First;

        while (node is not null)
        {
            var next = node.Next;

            if (identities.Any(identity => node.Value.References(identity)))
            {
                list.Remove(node);
                dropped++;
            }

            node = next;
        }

        return dropped;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
