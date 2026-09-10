# Description and acceptance: Attribute Error browser theming

## Description
Themes the Attribute Error browser chrome (the health-checks list, the details grid,
the dividers and the by-passable status border). Backgrounds, dividers, row stripes,
selection and all label text now follow the app theme via AppThemeBinding, so the
browser is readable in dark theme. The status border on the details heading turns the
Error role when a check is not by-passable and Border strong otherwise.

## Acceptance criteria
1. In dark theme the browser panel and its grid/list backgrounds are dark (Surface), not white.
2. In dark theme every header and data-row value is readable (light text on the dark surface); nothing is black-on-dark or invisible.
3. Alternating row stripes are visible and subtle in both themes (odd = Surface variant, even = Surface).
4. The selected row highlight is visible and distinct in both themes (Subform background).
5. The divider line(s) between panes are visible in both themes.
6. In light theme the browser looks as it did before this change.
7. Switching Light / Dark / Auto at runtime updates the whole browser with no restart, including the virtualised data-row text (which is themed inline per label for this reason).
8. No behaviour change: selection, tap-to-zoom, commands, and any pickers/date pickers behave as before.
9. The map surface is unchanged in both themes.
10. When a health check is not by-passable the details heading border reads as the
    error red in both themes; when it is by-passable the border is a neutral strong
    border.
11. In dark theme, a by-passable health-check value reads as light On surface text (not
    black); a not-by-passable value reads as the themed Error red. Both follow a live
    theme switch (normal text is inline-themed; error via a DataTrigger). The
    BooleanToColorConverter has been retired and its class deleted.
