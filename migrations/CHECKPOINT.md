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

Apply from the repository root:

    patch -p1 -E < migrations/<folder>/<folder>.patch
    # or
    git apply migrations/<folder>/<folder>.patch

Deletions are in the `measure-tools` patch only. If copying `files/` instead, delete
`MeasureOperationBase.cs` and `MeasureToolToIconConverter.cs` by hand.

## How the patches were made

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

## Still to do

- Build and run the tests on Windows. 157 tests pass in a macOS scratch project that stubs the Esri
  and MAUI types; the Esri dependent code and tests were not compiled.
- Manual checks are listed in `measure-tools/README.md`.
- Confirm the sketch point colour change is wanted (`sketch-point-style/README.md`).
- Optional: rename `IDrawingSymbolColourParser`, which now builds whole symbols.
