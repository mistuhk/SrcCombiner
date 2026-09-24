# Undo/redo: the journal

First delivery of the undo/redo workstream. Independent of the label toggle batches, so
it can be applied at any point. Purely additive: sixteen new files, no existing file
changed, nothing registered or resolved at runtime yet. Applying it cannot change how the
application behaves.

## What this is

The foundation for undo and redo: the record types that describe a committed editing
operation, and the bounded stack that holds the last ten of them.

The idea it establishes is that **every operation is recorded as an ordered list of
primitive row deltas**, and the opposite is derived mechanically rather than hand written
per operation:

```
Insert  inverts to  Delete
Delete  inverts to  Insert, from the captured copy
Update  inverts to  Update, back to the previous values
```

Undo replays that list backwards and inverts each step. Backwards matters because child
features are removed before the parent they point at. It is also why creating an LPIS
parcel with its CRN and Found Info features is one entry and one press of undo, not
several.

Nothing here is wired up. The recording and the undo path come in later deliveries. This
one exists so the rules about ordering, eviction, identity and invalidation can be proven
on their own, before any ArcGIS type or toolbar button is involved.

## Changes

`Src/MiEditor.Core/Services/Drawing/Editing/History/` (new folder, nine files)
- `FeatureIdentity`: names one feature. Prefers a business key over OBJECTID, because a
  feature restored by an undo is given a new OBJECTID by the feature service.
- `FeatureSnapshot`: one feature at one moment. Geometry held as Esri JSON so the copy
  cannot be altered by later edits to the live feature.
- `FeatureChangeKind`, `FeatureChange`: one primitive delta and its mechanical opposite.
- `EditOperationEntry`: one user visible operation, however many features or tables it
  touched. Carries the case it belongs to.
- `IEditHistoryJournal`, `EditHistoryJournal`: the undo and redo lists. Registered
  Singleton by `[RegisterService]`.
- `IFeatureIdentityPolicy`, `FeatureIdentityPolicy`: per layer, which attribute identifies
  a feature and which records its last change. Registered Singleton.

`Src/MiEditor.Shared/Configuration/AppSettings/EditHistoryInfo.cs`
- Depth and size budget. **Deliberately not added to `ApplicationSettings` yet**, so this
  delivery touches no existing file. The toolbar delivery wires it in.

`Src/MiEditor.Shared/Messages/Toolbar/EditHistoryMessages.cs`
- `EditHistoryChangedMessage(bool CanUndo, bool CanRedo)`. No sender or recipient yet.

`Tests/MiEditor.Core.Tests/Services/Drawing/Editing/History/` (new folder, five files)
- Thirty nine tests covering the depth cap and eviction, the `undo + redo <= 10`
  invariant, redo being cleared by a new recording, OBJECTID remapping across both lists,
  invalidation sweeps, every inverse, reverse ordering, the per layer identity rules, and
  an oversized operation being flagged as not undoable.

## Two design notes worth reading

**The lists are `LinkedList`, not `Stack`.** A stack cannot evict its oldest entry, which
is exactly what a depth cap requires. Entries move between the undo and redo lists rather
than being copied, and recording something new clears redo, which gives the invariant
`undo + redo <= MaxUndoDepth`. That is asserted as a test.

**Only `LPIS_Poly` gets a business key.** The child layers carry `POLYGONID` or
`LPISPOLYID`, but those name the parent parcel and repeat across many features, so they
identify a parcel rather than a feature. `FeatureIdentityPolicy` returns null for them and
they rely on OBJECTID remapping instead. An earlier draft got this wrong, which would have
made every canopy point in a parcel compare equal to every other one. This also means the
three `DCRMarkup` layers added since need no change here: anything without a true key is
handled correctly by default.

## Contents

- `files/`: full copies of the sixteen new files, at their repository paths.
- `journal.patch`: unified diff, all additions. Apply from the repository root with
  `git apply migrations/undo-redo/journal/journal.patch`.

## Build

Not built. The projects target net8.0-windows and must be built on Windows.

Verified instead in a standalone harness, which is possible because this delivery has no
MAUI, Esri or Windows dependency. The nine production files were compiled against this
repository's real `Layers.cs` and `RegisterServiceAttribute.cs` rather than stand-ins, and
the tests were run under xUnit:

```
Passed!  - Failed: 0, Passed: 39, Skipped: 0, Total: 39
```

Still to do on Windows: build `WG.MiEditor.sln` and run `Tests/MiEditor.Core.Tests` to
confirm the files compile inside the real projects. There is no behaviour to check in the
running application, because nothing in this delivery is reachable at runtime.

## Next

`migrations/undo-redo/toolbar` adds the Undo and Redo buttons, wires `EditHistoryInfo`
into `ApplicationSettings`, and clears the history on case changes. After that, recording
is added to one operation as a vertical slice before the rest follow.
