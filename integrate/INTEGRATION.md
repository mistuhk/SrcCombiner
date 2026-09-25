# Undo / Redo, Add Point: one integration step

This folder is the whole workstream in its final state. It supersedes
`journal/`, `journal/pr-review-fix/`, `toolbar/` and `add-point/`, which are kept only as the
review history. **Do not apply those. Apply this.**

Nothing has been integrated yet, so there is no partial state to reconcile and no order to
replay. There is one before and one after.

## What it amounts to

| | Files | Added | Removed |
| --- | --- | --- | --- |
| New files | 37 | 3,469 | 0 |
| Existing files touched | 12 | 181 | 14 |
| **Total** | **49** | **3,650** | **14** |

Thirty seven of the forty nine files did not exist before, so copying them in is the whole job
for those, and nothing about them can conflict.

The twelve existing files are where the care goes, and they are small. Of their 181 added lines,
86 are in one test file; the eleven production files take **+95 / -11 between them**, and five of
those eleven take the same four lines of JSON. Fourteen lines are removed from the repository in
total, which is the real measure of how much existing behaviour this delivery disturbs.

## Fast path

From the repository root:

```
git apply migrations/undo-redo/integrate/undo-redo-add-point.patch
```

Verified: the patch applies cleanly to the pre-change state of all twelve existing files, and
the tree it produces is byte for byte identical to `files/`. If it applies, you are done and
the rest of this document is only reference.

If it rejects a hunk, one of the twelve files has moved on since this working copy was taken.
Take the new files from `files/` and make the twelve edits by hand from the sections below;
each is small enough to place by eye.

## The 37 new files

Copy from `files/` at the paths shown. Nothing here can conflict.

```
Src/MiEditor.Core/Services/Drawing/Editing/History/          23 files
Src/MiEditor.MainApp/ViewModels/Toolbar/Descriptors/StandardTools/
    UndoToolDescriptor.cs
    RedoToolDescriptor.cs
Src/MiEditor.Shared/Configuration/AppSettings/EditHistoryInfo.cs
Src/MiEditor.Shared/Messages/Toolbar/EditHistoryMessages.cs
Tests/MiEditor.Core.Tests/Services/Drawing/Editing/History/   8 files
Tests/MiEditor.MainApp.Tests/ViewModels/Toolbar/UndoRedoToolDescriptorTests.cs
Tests/MiEditor.Tests.Common/Fakes/FakeCaseContext.cs
```

None need adding to a `.csproj`. Only `MiEditor.Scripts.csproj` in this repository lists its
sources explicitly, and this delivery does not touch it; every project it does touch globs.

## The 12 existing files

### 1. `Src/MiEditor.Shared/Models/Toolbar/StandardTool.cs`

Two tokens, and both added to `All`. Order in `All` does not set button order; the descriptors do.

```diff
     public static readonly StandardTool NextView = new( nameof(NextView));
+    public static readonly StandardTool Undo = new(nameof(Undo));
+    public static readonly StandardTool Redo = new(nameof(Redo));
 
     public static readonly IEnumerable<StandardTool> All =
     [
         ...
         PreviousView,
-        NextView
+        NextView,
+        Undo,
+        Redo
     ];
```

### 2. `Src/MiEditor.Shared/Configuration/AppSettings/ApplicationSettings.cs`

Deliberately **not** `required`, and defaulted, so binaries carrying this change still read an
`appsettings.json` written before the edit history existed. That matters if the app is ever
deployed ahead of its configuration.

```diff
     public required MapViewsInfo MapViewsInfo { get; init; }
+
+    // Not required, and defaulted, so binaries carrying this can still read an appsettings
+    // file written before the edit history existed.
+    public EditHistoryInfo EditHistory { get; init; } = new();
     public required double MinTaAreaInHectares { get; init; }
```

### 3 to 7. The five `appsettings*.json`

The same block in `appsettings.json`, `.Dev.json`, `.PP.json`, `.Sys.json`, `.UAT.json`,
inside `ApplicationSettings`, after `MapViewsInfo`:

```diff
     "MapViewsInfo": {
       "MaxViewsHistory": 10
     },
+    "EditHistory": {
+      "MaxUndoDepth": 10,
+      "MaxTotalBytes": 67108864
+    },
     "MinTaAreaInHectares": 0.005,
```

`MaxTotalBytes` is 64 MB. It bounds the whole history rather than one entry, and the journal
evicts oldest first to stay inside it.

### 8. `Src/MiEditor.MainApp/MauiProgram.cs`

One line, beside the sibling settings registrations:

```diff
     builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().Logging);
+    builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().EditHistory);
```

### 9. `Src/MiEditor.MainApp/AutoScanning.cs`

One using, and one constructor argument. Everything else in the feature is found by
`[RegisterService]`; the toolbar view model is hand registered, which is why it needs this.

```diff
+using WG.MiEditor.Core.Services.Drawing.Editing.History;
```

```diff
     services.AddSingleton(sp => new ToolbarControlViewModel(
         sp.GetRequiredService<IMessenger>(),
         sp.GetRequiredService<IToolActivationCoordinator>(),
-        sp.GetRequiredService<IToolDescriptorRegistry>()));
+        sp.GetRequiredService<IToolDescriptorRegistry>(),
+        sp.GetRequiredService<IEditHistoryService>()));
```

### 10. `Src/MiEditor.MainApp/ViewModels/Toolbar/ToolbarControlViewModel.cs`

+33 / -7. The constructor takes `IEditHistoryService`, `OnActivate` gains an Undo and a Redo
branch and becomes async, the two new tools join the starts-disabled set, and the class becomes
`IRecipient<EditHistoryChangedMessage>` alongside its existing navigation recipient. Take this
one from `files/` if the patch rejects it: it is the largest of the twelve and the easiest to
get subtly wrong by hand.

### 11. `Src/MiEditor.Core/Services/Drawing/Editing/Attributes/AttributionModelService.cs`

+29 / -2, and the only change to existing editing code in the whole delivery. Three recording
lines wrapped around the existing body, plus a private helper naming the operation. Control
flow, return values and error handling are untouched.

```csharp
using var editScope = editRecorder.Begin(OperationNameFor(mode, targetLayer));
...
editScope.MarkInsert(targetLayer, feature);     // before AddFeatureAsync
...
await editScope.CommitAsync();                  // after ApplyEditsAsync
```

`MarkInsert` runs before the row is sent, because the object id does not exist yet; `CommitAsync`
runs after, because that is when the service has assigned it. Create modes only (`New`, `Import`,
`ImportSketch`); `Modify` records nothing, which is why undo is Add Point only for now.

### 12. `Tests/MiEditor.MainApp.Tests/ViewModels/Toolbar/ToolbarControlViewModelTests.cs`

+86 / -3. Additive, except that the constructor calls gain an argument. The existing
Previous / Next View tests are unchanged.

## Nothing is deleted or moved

An additive copy is sufficient. No file in the repository is removed or renamed by this
delivery.

## Build and test state

Run on a `net10.0` harness that compiles the Esri free part of the feature against this
repository's real `Layers`, `EditHistoryInfo`, `ApplicationSettings`, `IEditingOperationsManager`,
`IOperationFeedbackService`, `ICaseContext`, `SpinnerType` and real message declarations:

```
92 passed, 0 failed, 0 warnings
```

Each of the four most recent review fixes was checked by reverting it and confirming the suite
goes red, so the tests are known to be load bearing rather than merely present.

**What has never been compiled.** Every project in `WG.MiEditor.sln` targets
`net8.0-windows10.0.19041.0`, which will not build on the machine this was written on. These
five types reference ArcGIS objects and are outside the harness:

- `FeatureSnapshotFactory`
- `FeatureChangeApplier`
- `EditOperationRecorder`
- `FeatureStateReader` (its logic is tested against stubbed ArcGIS types; the real signatures are not)
- `EditConflictDetector` (same)

plus the `AttributionModelService` change. Expect to fix compile errors in these on the first
Windows build. The collaborator signatures were copied by hand from
`Src/MiEditor.Core/Services/FeatureLayerService.cs` lines 22, 37 and 38, so they should be
right, but "should be" is the accurate word.

**First thing to do after integrating:** `dotnet build WG.MiEditor.sln`. That single command is
worth more than any further review of this code.

## Then check on Windows, against a real case

1. Draw a Canopy Point. Undo. The point goes. Redo. It comes back.
2. Both buttons disabled on a fresh case, and after Exit Case.
3. Previous View and Next View still navigate the map extent exactly as before.
4. Double click Undo quickly. Only one undo happens.
5. Activate a vertex tool on a feature, then press Undo. The tool stands down, no crash.
6. Edit the same parcel from a second session, then undo. Refused with a warning naming the
   operation, rather than overwriting the other officer.
7. Switch case and come back. History empty.
8. The map surface looks identical in light and dark, per the standing theming rule.

## Known and accepted for this delivery

- **Undo posts a compensating edit, not a rollback.** Every edit path calls `ApplyEditsAsync`
  immediately, so the original write is already on the server. Anyone watching the service sees
  two edits, not zero. Worth confirming with WG that this is acceptable for `LPIS_Poly` audit.
- **The Undo and Redo icons are placeholders** (`previous_extent.png` and `next_extent.png`), so
  four toolbar buttons currently share two arrow images and differ only by tooltip. One line per
  descriptor to change. Release blocker.
- **Add Point only.** Delete, vertex, move, buffer, merge, split and attribute modify record
  nothing yet, so their buttons stay dark. That is the intended state, not a defect.
