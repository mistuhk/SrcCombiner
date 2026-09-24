using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Keeps the most recent operations available for undo and redo. Backed by linked lists
/// rather than a stack because the oldest entry has to be evicted once a cap is reached,
/// which a stack cannot do. Entries move between the two lists rather than being copied, and
/// recording clears redo, so the combined depth never exceeds the cap.
/// <para>
/// Two caps apply: how many operations are kept, and how much they may hold in total. The
/// oldest are evicted until both are satisfied, so memory stays bounded without any single
/// operation being singled out and refused. Only an operation larger than the entire budget
/// cannot be held, and Record reports that rather than recording something unusable.
/// </para>
/// <para>
/// Recording happens on whichever thread finished the edit, while undo is driven from the
/// UI, so every member takes a lock. Contention is not a concern at this size, and the cost
/// of getting it wrong once the callers exist is far higher than the cost of the lock.
/// </para>
/// </summary>
[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IEditHistoryJournal) })]
public sealed class EditHistoryJournal : IEditHistoryJournal
{
    private readonly LinkedList<EditOperationEntry> undo = new();
    private readonly LinkedList<EditOperationEntry> redo = new();
    private readonly object gate = new();
    private readonly int maxDepth;
    private readonly long maxTotalBytes;

    /// <summary>
    /// Used by the convention scanner until the settings are bound. EditHistoryInfo owns the
    /// default values, so they are not repeated here.
    /// </summary>
    public EditHistoryJournal()
        : this(new EditHistoryInfo())
    {
    }

    public EditHistoryJournal(EditHistoryInfo settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxUndoDepth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxTotalBytes, 1);

        maxDepth = settings.MaxUndoDepth;
        maxTotalBytes = settings.MaxTotalBytes;
    }

    public event EventHandler? Changed;

    public bool CanUndo
    {
        get { lock (gate) { return undo.Count > 0; } }
    }

    public bool CanRedo
    {
        get { lock (gate) { return redo.Count > 0; } }
    }

    public int UndoDepth
    {
        get { lock (gate) { return undo.Count; } }
    }

    public int RedoDepth
    {
        get { lock (gate) { return redo.Count; } }
    }

    public bool Record(EditOperationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (gate)
        {
            // Nothing is gained by evicting the whole history to make room for one operation
            // that still would not fit, so leave the history alone and let the caller say so.
            if (entry.ApproximateSizeInBytes() > maxTotalBytes)
            {
                return false;
            }

            redo.Clear();
            undo.AddLast(entry);

            while (undo.Count > maxDepth)
            {
                undo.RemoveFirst();
            }

            // The entry just added is known to fit on its own, so this always terminates with
            // at least that one entry still in the list.
            while (undo.Count > 1 && TotalSize() > maxTotalBytes)
            {
                undo.RemoveFirst();
            }
        }

        RaiseChanged();

        return true;
    }

    public EditOperationEntry? PeekUndo()
    {
        lock (gate) { return undo.Last?.Value; }
    }

    public EditOperationEntry? PeekRedo()
    {
        lock (gate) { return redo.Last?.Value; }
    }

    public EditOperationEntry? PopUndoToRedo()
    {
        EditOperationEntry? entry;

        lock (gate)
        {
            entry = RemoveLast(undo);
            if (entry is null)
            {
                return null;
            }

            redo.AddLast(entry);
        }

        RaiseChanged();

        return entry;
    }

    public EditOperationEntry? PopRedoToUndo()
    {
        EditOperationEntry? entry;

        lock (gate)
        {
            entry = RemoveLast(redo);
            if (entry is null)
            {
                return null;
            }

            undo.AddLast(entry);
        }

        RaiseChanged();

        return entry;
    }

    public EditOperationEntry? DiscardUndo()
    {
        EditOperationEntry? entry;

        lock (gate)
        {
            entry = RemoveLast(undo);
        }

        if (entry is not null)
        {
            RaiseChanged();
        }

        return entry;
    }

    public void ClearRedo()
    {
        lock (gate)
        {
            if (redo.Count == 0)
            {
                return;
            }

            redo.Clear();
        }

        RaiseChanged();
    }

    public void RemapObjectId(string layerName, long oldObjectId, long newObjectId)
    {
        if (oldObjectId == newObjectId)
        {
            return;
        }

        bool remapped;

        lock (gate)
        {
            remapped = Remap(undo, layerName, oldObjectId, newObjectId);
            remapped |= Remap(redo, layerName, oldObjectId, newObjectId);
        }

        if (remapped)
        {
            RaiseChanged();
        }
    }

    public int DropEntriesReferencing(IReadOnlyCollection<FeatureIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        if (identities.Count == 0)
        {
            return 0;
        }

        int dropped;

        lock (gate)
        {
            dropped = Drop(undo, identities) + Drop(redo, identities);
        }

        if (dropped > 0)
        {
            RaiseChanged();
        }

        return dropped;
    }

    public void Clear()
    {
        lock (gate)
        {
            if (undo.Count == 0 && redo.Count == 0)
            {
                return;
            }

            undo.Clear();
            redo.Clear();
        }

        RaiseChanged();
    }

    /// <summary>
    /// Recomputed rather than tracked incrementally. Entries are added and removed at six
    /// points across two lists, and a running total would have to be kept correct at every
    /// one of them. At ten entries the walk costs nothing, so the simpler code that is
    /// obviously right is the better trade.
    /// </summary>
    private long TotalSize()
    {
        long total = 0;

        foreach (var entry in undo)
        {
            total += entry.ApproximateSizeInBytes();
        }

        foreach (var entry in redo)
        {
            total += entry.ApproximateSizeInBytes();
        }

        return total;
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

                if (ReferenceEquals(before, change.Before) && ReferenceEquals(after, change.After))
                {
                    changes.Add(change);
                    continue;
                }

                entryChanged = true;
                changes.Add(change.WithSnapshots(before, after));
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

    // Raised outside the lock so a handler that calls back into the journal cannot deadlock.
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
