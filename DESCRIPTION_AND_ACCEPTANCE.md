# Description and acceptance: Sliver Area browser theming

## Description
Themes the Sliver Area browser so it is readable in dark mode. The top divider line,
the vertical divider between the results grid and the layer picker, the four-column
results grid (headers and data rows), the alternating row stripes, the selected-row
highlight, the "Select layer to search sliver areas" label and the layer picker all
change colour with the app theme.

## Acceptance criteria
Check each item once in Light mode and once in Dark mode (Settings > theme).

1. Column headers (OBJECTID, POLYGONID, SHAPE AREA, and the area column) and the data
   values in every row:
   - Light mode: dark, almost black text (#222222).
   - Dark mode: near-white, light grey text (#ececec). Readable, not black-on-dark or
     invisible.

2. Alternating row stripes:
   - Light mode: rows alternate white (#ffffff) and very light grey (#f4f4f4).
   - Dark mode: rows alternate very dark grey, almost black (#1f1f1f) and a slightly
     lighter dark grey (#292929). Subtle but visible.

3. Selected row highlight:
   - Light mode: pale blue (#eef4fb).
   - Dark mode: dark navy blue (#15212c). The selected row stands out from the stripes
     in both modes.

4. The two divider lines (the thin line across the top, and the vertical line between
   the grid and the layer picker on the right):
   - Light mode: light grey (#d2d2d2).
   - Dark mode: medium grey (#4a4a4a). Visible in both modes.

5. The "Select layer to search sliver areas" label:
   - Light mode: dark, almost black text (#222222).
   - Dark mode: near-white, light grey text (#ececec).

6. The layer picker (dropdown) on the right:
   - Light mode: white box with dark text.
   - Dark mode: dark box with light text (both the closed box and the opened list).

7. In Light mode the whole browser looks the same as before this change (white/very
   light grey rows, dark text).

8. Switching theme while the browser is open updates everything at once with no
   restart, including the data-row text, stripes and selection. Nothing is left the old
   colour.

9. No behaviour change: selecting a layer, running the sliver search, and selecting a
   result row all work exactly as before.

10. The map behind/around the browser looks identical in Light and Dark mode.
