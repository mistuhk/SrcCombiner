# IsSnappingSettingsVisible becomes IsEditingSettingsVisible

Two files, five renamed occurrences and a doc comment. A rename only change: apply after
`migrations/undo-redo/history-setting/`, which is what makes it worth doing.

## Why

That property gates the whole editing block, not snapping:

```
Snapping, Guide, Buffer, Split Polygon Overlap, LPIS Transparency
```

and, after `history-setting`, History as well. Binding a History card's visibility to something called
`IsSnappingSettingsVisible` is the kind of thing that misleads whoever touches the panel next, and the
name was already wrong for the four sections below snapping before any of this.

It is the complete diff, so there is nothing else to read:

```diff
-                <VerticalStackLayout IsVisible="{Binding IsSnappingSettingsVisible}" Spacing="8">
+                <VerticalStackLayout IsVisible="{Binding IsEditingSettingsVisible}" Spacing="8">

+    /// <summary>
+    /// Whether the editing sections are shown, which is every section below Theme: History, Snapping,
+    /// Guide, Buffer, Split overlap and LPIS transparency. They are available during an edit session
+    /// only. Named for snapping until the History section was added beneath the same gate.
+    /// </summary>
-    private bool isSnappingSettingsVisible;
+    private bool isEditingSettingsVisible;

-        IsSnappingSettingsVisible = false;
+        IsEditingSettingsVisible = false;
-            IsSnappingSettingsVisible = true;
+            IsEditingSettingsVisible = true;
-        IsSnappingSettingsVisible = true;
+        IsEditingSettingsVisible = true;
```

`IsSnappingEnabled` and `IsSnappingExpanded` are untouched: those really are about snapping.

## Kept separate on purpose

It would have been a two minute edit inside `history-setting`, and it is separate so that the feature's
diff is only the feature. A rename threaded through a functional change is how a reviewer stops reading
carefully.

Afterwards `IsSnappingSettingsVisible` appears nowhere in `Src/` or `Tests/`.

## Build and test state

Both files are MainApp and cannot be compiled here. There is nothing behavioural to verify: the rename is
mechanical, the five occurrences are the complete set found by search, and the XAML binding was renamed
with them. `dotnet build` is the check, and a missed occurrence would be a compile error rather than a
silent fault, except in the XAML where it would be a binding that silently resolves to nothing. That one
is in the diff above.

## How to apply

```
git apply migrations/undo-redo/editing-settings-rename/editing-settings-rename.patch
```

Verified: applies cleanly to the state `history-setting` leaves, and reproduces `files/` byte for byte.
