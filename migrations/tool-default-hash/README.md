# tool-default-hash

A default tool value no longer throws from `GetHashCode()`.

## Apply order

Step 5 of 5. Previous: `sketch-point-style`. Next: none, this is the last.

Needs first: Nothing. It touches no file the other packages change, so it applies at any point, but it is listed last to keep one order.

Full order:

1. `measure-tools`
2. `singleton-lifetimes`
3. `symbol-config-safety`
4. `sketch-point-style`
5. `tool-default-hash` (this package)

From the repository root:

    git apply migrations/tool-default-hash/tool-default-hash.patch

If it fails on a Windows checkout, retry with `--ignore-whitespace` (the patches use LF line
endings). See `../CHECKPOINT.md` for the full steps and what to do if a patch will not apply.

## The fault

"No tool selected" is a default struct, for example `AoiControl` resets with
`ActiveTool = new()`. Its `Name` is null, and `GetHashCode()` called `Name.GetHashCode()`, so using
one as a dictionary key or set member threw `NullReferenceException`. `ToString()` also returned
null. Nothing hashes a default tool today, so the fault was latent.

All four tool structs had the same code.

## Changes

| File | Change |
| --- | --- |
| `AoiTool.cs`, `EditingTool.cs`, `MeasureTool.cs`, `StandardTool.cs` in `Src/MiEditor.Shared/Models/Toolbar/` | `GetHashCode() => Name?.GetHashCode() ?? 0`; `ToString() => Name ?? string.Empty` |
| `Tests/MiEditor.MainApp.Tests/ViewModels/Toolbar/ToolDefaultValueTests.cs` | New, 4 tests |

Named tools hash and print as before. Equality was already null safe. No code depended on
`ToString()` returning null.
