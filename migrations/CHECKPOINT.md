# Checkpoint

Point a new session at this file.

## Where things are

- Working copy: `/Users/mistuhkay/Downloads/mi-editor-net10`. Not a git checkout. Targets
  `net10.0-windows10.0.19041.0`; it cannot be built on macOS.
- `Src/` and `Tests/` already contain every package below.
- `migrations/` holds those packages for applying to another copy of the repository.

## Packages, in apply order

| Folder | What | Files | Applied here? |
| --- | --- | --- | --- |
| `measure-tools` | Measure tool resilience fixes 1 to 14; `MeasureOperationBase` replaced by composition | 45, 2 deleted | yes |
| `singleton-lifetimes` | `MapViewService` and the colour parser become `Singleton` | 2 | yes |
| `symbol-config-safety` | Bad or missing `drawingsymbols.json` entries fall back instead of crashing | 18 | yes |
| `sketch-point-style` | Sketch points use `InFieldSketchPoint`; visible colour change | 1 | yes |
| `tool-default-hash` | Default tool structs no longer throw from `GetHashCode` | 5 | yes |

Each folder has `files/` at real repository paths (the state after that package), a `.patch`
against the state before it, and a README explaining the decisions. Four files are touched by two
packages, so apply in order: `DrawingSymbolColourParserService.cs`, `AoiControlViewModel.cs`,
`MeasurementSymbols.cs` and `AddSketchPointOperation.cs`.

## How to apply

Do not apply these patches to `/Users/mistuhkay/Downloads/mi-editor-net10`; that copy already has
all five applied. Use them only on a copy without the changes.

### Order and why

| Step | Patch | Needs first | Reason |
| --- | --- | --- | --- |
| 1 | `measure-tools/measure-tools.patch` | nothing | |
| 2 | `singleton-lifetimes/singleton-lifetimes.patch` | 1 | Both change `DrawingSymbolColourParserService.cs` |
| 3 | `symbol-config-safety/symbol-config-safety.patch` | 2 | Changes `DrawingSymbolColourParserService.cs` again, plus `AoiControlViewModel.cs` and `MeasurementSymbols.cs` as patch 1 left them |
| 4 | `sketch-point-style/sketch-point-style.patch` | 3 | Changes the `MarkerSymbol(...)` call that patch 3 adds to `AddSketchPointOperation.cs` |
| 5 | `tool-default-hash/tool-default-hash.patch` | nothing | Independent; applied last by convention |

### Steps

Run from the repository root (the folder with `WG.MiEditor.sln`), with `migrations/` copied into it.
On Windows, use Git Bash or PowerShell.

Copy the `.patch` files as files (Explorer, `cp`, `robocopy`). Never paste their text into an editor
or re-save them: that converts the line endings and drops the final newline, which `git apply`
reports as `trailing whitespace` and `corrupt patch at line N`.

0. Check each copied patch is intact. In Git Bash:

       cd migrations && sha256sum -c SHA256SUMS

   In PowerShell, compare `Get-FileHash migrations\<folder>\<folder>.patch` with the line in
   `SHA256SUMS`; `Get-FileHash` prints the hash in upper case. If a hash differs, copy that patch again.

1. Check patch 1 without changing anything. Patches 2 to 4 cannot be checked this way until the
   earlier patches are applied.

       git apply --check migrations/measure-tools/measure-tools.patch

2. Apply them one at a time, in order, and stop at the first error:

       git apply migrations/measure-tools/measure-tools.patch
       git apply migrations/singleton-lifetimes/singleton-lifetimes.patch
       git apply migrations/symbol-config-safety/symbol-config-safety.patch
       git apply migrations/sketch-point-style/sketch-point-style.patch
       git apply migrations/tool-default-hash/tool-default-hash.patch

   `git apply` applies each patch completely or not at all, so a failure changes no files. Without
   git, `patch -p1 -E < migrations/<folder>/<folder>.patch` works in the same order.

3. Build the solution and run the tests.

### If a patch fails

- **`trailing whitespace` or `corrupt patch`.** The patch file was altered while it was copied.
  Check it against `SHA256SUMS` and copy it again. `migrations/.gitattributes` stops git converting
  the patches if `migrations/` is committed. If they were committed before that file was added,
  delete the patch files and copy them in again.

- **Line endings in the source.** A Windows checkout with CRLF source files (git's `core.autocrlf`
  on) was tested and applies with plain `git apply`. If a hunk still fails, retry that patch with:

      git apply --3way migrations/<folder>/<folder>.patch

- **Other failures.** Do not apply part of a patch by hand. Copy that package's `files/` folder over
  the repository instead; it holds the complete changed files at their real paths. Patch 1 deletes
  `MeasureOperationBase.cs` and `MeasureToolToIconConverter.cs`. If `measure-tools` is applied by
  copying `files/`, delete those two by hand. Copy the packages in the same order as the patches.

## How the patches were made

### Current patches (regenerated 2026-11-20)

The patches are `git diff --full-index` output against the latest source, the version supplied on
2026-11-19 before any package was applied. The first set was made against older originals and
failed `git apply` on the latest `LiveMeasurementViewModel.cs`, because its extra leading blank line
shifts a hunk that must match at line 1. `patch` tolerated that, and `git apply` refuses it.

Each package was committed in turn on top of the latest source, and each patch is the diff between
consecutive commits. `files/` was rebuilt from the same commits. Tested on fresh copies of the latest
source, applying all five in order:

- `git apply --check`, then `git apply --whitespace=error`: all apply, and the result matches the
  working copy byte for byte (67 files).
- `patch -p1 -E`: same result.
- A Windows style checkout (`core.autocrlf=true`, CRLF source files): plain `git apply`,
  `--ignore-whitespace` and `--3way` all apply. The result matches apart from line endings, and no
  file ends up with mixed line endings.

`SHA256SUMS` holds the patch checksums.

### First set (superseded)

The working copy has no version control, so the pre-change originals were rebuilt and checked:

- 35 originals are byte-identical to the same file in another local copy of the project
  (`mi-editor`, `mieditor` or `undo-integrate`). Each was confirmed against output captured before
  the file was first changed, or against the reversed mechanical edit.
- 4 originals (`ToolActivationController`, `LiveMeasurementViewModel`,
  `MeasurementToolGroupViewModel`, `AoiToolbarControlViewModel`) are exact full prints of the file
  captured before it was changed.
- 2 originals (`AutoScanning`, `AoiControlViewModel`) were rebuilt hunk by hunk and checked line by
  line. Every remaining difference is a change made in this work, and every line-numbered
  original captured matches.
- `GlobalSuppressions.cs` is the current file with the 12 deleted lines re-inserted; the one line
  captured before deletion lands at its recorded line, 393.

Starting from those originals, applying all five patches in order with `patch -p1` and with
`git apply --whitespace=error` reproduces the working copy byte for byte.

If a hunk fails against another copy of the repository, that copy differs from this one in that
spot. `files/` holds the full intended file in that case.

## Re-applied to the latest source (2026-11-19)

The source was replaced with a newer version, and all five patches were applied to it again, in order,
with `patch` (no failed hunks; `git apply` failed, see above). That source differed from the originals in only two spots
(`GeometryEditor!.Stop()` in `AoiControlViewModel`, a leading blank line in
`LiveMeasurementViewModel`), and both were kept.

Compiled on macOS with `EnableWindowsTargeting`, against Esri.ArcGISRuntime 200.8.3 and
CommunityToolkit.Mvvm 8.4.2. The repository's own `Directory.Build.props` and
`Directory.Packages.props` are not in this copy, so `ImplicitUsings` and these versions were assumed.

- Core and Core.Tests: 0 errors before and after.
- MainApp and MainApp.Tests, compiled without the MAUI workload: the same 82 XAML codegen errors
  (`InitializeComponent`, x:Name fields) before and after, and no new errors.
- Every name bound in the changed views' XAML exists on its view model.
- Core.Tests run on macOS: no test that passed before fails now. 80 of the 101 new tests pass; the
  other 21 need the native Esri runtime, which only exists on Windows.

## Still to do

- Build and run the tests on Windows. The XAML compiler and MainApp.Tests have not run.
- Manual checks are listed in `measure-tools/README.md`.
- Confirm the sketch point colour change is wanted (`sketch-point-style/README.md`).
- Optional: rename `IDrawingSymbolColourParser`, which now builds whole symbols.
