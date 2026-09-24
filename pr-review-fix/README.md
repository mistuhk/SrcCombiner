# Undo/redo journal: review fixes

Apply after `migrations/undo-redo/journal`. Eight files changed, none added or deleted.
Still purely additive to the application as a whole, since nothing in the journal is
reachable at runtime yet.

These are the fixes from a strict review of the journal delivery. One finding was a real
defect that would have disabled the feature in normal use; the rest close traps that get
more expensive to fix once the toolbar delivery builds on these types.

## 1. A large operation no longer disables undo entirely

**The defect.** `CanUndo` inspected only the top entry, and an operation over the size
budget was recorded but flagged not undoable. So one large operation made undo unavailable
for every good operation beneath it, which stayed in the journal but became unreachable.
Five edits then one complex split left the user with no undo at all, and a message that
talked only about the split.

**The fix.** The per-entry flag is gone, and with it `EditOperationEntry.IsUndoable`,
`NotUndoableReason` and `AsNotUndoable`. In its place `EditHistoryInfo.MaxTotalBytes`
bounds what the whole history may hold, across both lists, and the oldest entries are
evicted until it fits. This is how a cache has always bounded itself.

That change is worth more than fixing the symptom, because the per-entry guard bounded
nothing: ten entries each just under the limit still exceeded any sensible budget. A total
budget actually bounds memory, and it never blocks.

The budget default moved from 8MB per entry to 64MB in total. A 10,000 vertex parcel is
roughly 560KB of Esri JSON, so a split of one is about 1.7MB and ten of those is about
17MB, on a desktop application where ArcGIS Runtime already holds far more. 64MB is high
enough never to fire in normal work and low enough to stop a pathological multipart
polygon.

**The one case left.** An operation larger than the entire budget cannot be held at all.
`Record` now returns `bool` and answers `false`, leaving the history untouched, so the
caller can tell the user that this one operation cannot be undone at the moment it
happens rather than later when they press undo for an unrelated reason.

**Depends on the conflict detector.** Not recording an operation leaves a gap: undoing
past it restores state from before an edit that is still live. That is safe because the
detector compares the recorded state against the live row and refuses when they differ, so
an undo that would collide with the unrecorded operation is blocked rather than applied.
Anything that uses `Record` before the detector exists must treat a `false` return as a
reason to clear the history instead.

## 2. A malformed change is now refused at construction

`FeatureChange` accepted any combination of `Kind`, `Before` and `After`. A
`new FeatureChange(Insert, null, null)` compiled, `Reference` threw far from the
construction site, and `Inverse` turned it into a `Delete` carrying nothing to restore
from, silently. `Identity` reaches `Reference`, and `Identity` is used by
`DropEntriesReferencing` on the conflict path, so the throw landed while handling an error.

Which snapshots each kind carries is now checked in the primary constructor, so a change
that reaches any other code is known to be well formed and `Reference` no longer throws.

`Remap` used to rebuild changes with a `with` expression, which copies fields and would
have bypassed that check. It now goes through `FeatureChange.WithSnapshots`, which routes
through the validating factories. No `with` expression on a `FeatureChange` remains in the
codebase, which matters because a `with` cannot be made to re-run the check.

## 3. EditHistoryInfo is the single source of the defaults

`EditHistoryInfo` had no references anywhere, while the journal carried its own
`DefaultMaxDepth` constant and a duplicate size default on a constructor parameter. Three
copies of two numbers, with the constructor default positioned to silently win once the
settings were bound.

The journal now takes `EditHistoryInfo`, and the parameterless constructor the scanner
uses simply calls `new EditHistoryInfo()`. The type is no longer dead, the numbers live in
one place, and the toolbar delivery only has to inject the bound instance.

## 4. Thread safety, rather than an assumption about it

The journal is a singleton holding two mutable lists, with recording arriving from
whichever thread finished the edit and undo driven from the UI. The original relied on
that never overlapping, which documentation cannot enforce.

Every member now takes a lock. `Changed` is raised outside it, so a handler that calls
back into the journal cannot deadlock. At ten entries the cost is unmeasurable, and the
alternative is a class of bug that only ever appears in production.

## 5. ClearRedo, renamed and actually tested

`DiscardRedoFrom` took no argument and cleared everything, so the name implied a
positional discard that did not exist. It is now `ClearRedo`.

It was also only ever called on an empty journal, so a change making it a no-op would have
passed the whole suite. `PeekRedo` had no test at all. Both are covered now.

## 6. Sizing no longer allocates to measure

`ApproximateSizeInBytes` called `ToString()` on every attribute value purely to read its
length, allocating a string per attribute on a path that now runs during eviction. Strings
are measured directly and other values counted as a small fixed size, which is accurate
enough against a budget in tens of megabytes.

## Changes by file

| File | Change |
| --- | --- |
| `EditHistoryInfo.cs` | `MaxEntrySizeInBytes` becomes `MaxTotalBytes`, default 64MB |
| `EditHistoryJournal.cs` | Total size eviction, locking, takes `EditHistoryInfo`, `Record` returns bool, `ClearRedo`, remap through `WithSnapshots` |
| `IEditHistoryJournal.cs` | `Record` returns bool, `DiscardRedoFrom` becomes `ClearRedo`, thread safety stated |
| `EditOperationEntry.cs` | `IsUndoable`, `NotUndoableReason` and `AsNotUndoable` removed |
| `FeatureChange.cs` | Construction validated, `Reference` no longer throws, `WithSnapshots` added |
| `FeatureSnapshot.cs` | Sizing without allocation |
| `EditHistoryJournalTests.cs` | Size-guard tests replaced with eviction and refusal tests, `ClearRedo` and `PeekRedo` covered |
| `FeatureChangeTests.cs` | Construction validation covered for every kind |

## Contents

- `files/`: full copies of the eight changed files, at their repository paths.
- `pr-review-fix.patch`: unified diff against the journal delivery. Apply from the
  repository root with
  `git apply migrations/undo-redo/journal/pr-review-fix/pr-review-fix.patch`.

## Build

Not built. The projects target net8.0-windows and must be built on Windows.

Verified in the same standalone harness as the journal delivery, compiled against this
repository's real `Layers.cs`, `EditHistoryInfo.cs` and `RegisterServiceAttribute.cs`:

```
Passed!  - Failed: 0, Passed: 49, Skipped: 0, Total: 49
```

Up from 39. The ten new tests cover eviction under the total budget, refusal of an
operation larger than the budget, construction validation for all three change kinds,
`ClearRedo` against a populated stack, and `PeekRedo`.

Still to do on Windows: build `WG.MiEditor.sln` and run `Tests/MiEditor.Core.Tests`.

## Carried forward

`FeatureIdentity` has two notions of sameness: `SameRowAs` prefers the business key, while
`RemapObjectId` matches on object id alone. Both are correct for their own use, and the
combination is not transitive. That is harmless where they are used today, but worth
settling before more code depends on either.
