# Description and acceptance: Topological Checks browser theming

## Description
Themes the Topological Checks browser so it is readable in dark mode. The total-error
count label at the top, the thin divider line under it, the column headers, the data
rows (the nine columns of error values), the alternating row stripes and the
selected-row highlight now all change colour with the app theme. Previously the data
values were fixed black, so they were invisible in dark mode; they are now light in
dark mode.

## Acceptance criteria
Check each item once in Light mode and once in Dark mode (Settings > theme).

1. The "Total No. of Topological Error(s): N" label at the top:
   - Light mode: dark, almost black text (#222222).
   - Dark mode: near-white, light grey text (#ececec). Readable, not black-on-dark.

2. The column headers (Index, OriginFeatureId, DestinationFeatureId, ErrorType, etc.):
   - Light mode: dark, almost black text (#222222).
   - Dark mode: near-white, light grey text (#ececec).

3. The data values in every row and column:
   - Light mode: dark, almost black text (#222222).
   - Dark mode: near-white, light grey text (#ececec). This is the main fix: the values
     used to stay black and were invisible in dark mode.

4. The thin divider line under the count label:
   - Light mode: light grey (#d2d2d2).
   - Dark mode: medium grey (#4a4a4a). Visible in both modes.

5. Alternating row stripes:
   - Light mode: rows alternate white (#ffffff) and very light grey (#f4f4f4).
   - Dark mode: rows alternate very dark grey, almost black (#1f1f1f) and a slightly
     lighter dark grey (#292929). The stripes should be subtle but visible.

6. Selected row highlight:
   - Light mode: pale blue (#eef4fb).
   - Dark mode: dark navy blue (#15212c). The selected row stands out from the stripes
     in both modes.

7. In Light mode the whole browser looks the same as before this change (white/very
   light grey rows, dark text).

8. Switching theme while the browser is open updates everything at once with no
   restart, including the count label, headers, data values, stripes and selection.
   Nothing is left the old colour.

9. No behaviour change: selecting a row and tapping to zoom to the error work exactly
   as before.

10. The map behind/around the browser looks identical in Light and Dark mode.
