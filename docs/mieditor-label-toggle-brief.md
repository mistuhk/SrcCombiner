# Claude Code Brief: Mi-Editor Layer Label Toggle

You are working in the Mi-Editor repository on disk. This brief is your complete
context for the layer label toggle feature. Read it fully before making changes. If
anything here conflicts with what you find in the code, stop and report it rather
than guessing.

## 1. Context and objective

Mi-Editor is a .NET MAUI 8 Windows desktop GIS application for the Welsh Government
(LPIS land parcel case editing, Esri ArcGIS Maps SDK for .NET, MVVM). The Light /
Dark / Auto theming work is complete and merged, so this feature builds on a themed
codebase and must reuse the existing theme brushes.

Objective: add an inline control to the Layer Manager that turns a feature layer's
text labels on or off.

Product decision, already made: the inline icon beside the visibility eye is the
chosen UI. A right-click context menu option was considered and declined. Do not
build a context menu.

## 2. Scope

In scope:
- F1: an inline label toggle on individual (leaf) layer rows, greyed when the layer
  has no published label definition.
- F2: wire the toggle to `FeatureLayer.LabelsEnabled`, gated by a capability check
  on `FeatureLayer.LabelDefinitions`.

Out of scope (do not build):
- Any right-click context menu entry for labels.
- Authoring label definitions in code.
- A global "all labels" control, configurable label fields, or in-UI editing of
  label font, size or colour.
- Any theming or modification of the ArcGIS MapView.

## 3. Hard rules (do not violate)

- Inline icon only. No context menu.
- The control appears only on individual (leaf) layers, those with no expand arrow.
  Group rows never show it.
- Do not author label definitions in code. If a layer has no published definition,
  the control is greyed and inert.
- Reuse the existing theme brushes for the control's colours. Do not introduce new
  colours or hardcoded hex.
- Do not theme or otherwise touch the ArcGIS MapView.
- Do not change existing behaviour: the visibility eye, the layer tree, bindings and
  commands stay exactly as they are.
- Produce output with no AI-style artefacts: no em dashes, no en dashes, no
  decorative arrows, no excessive inline comments. Match the existing code style.
- Work in small, reviewable batches. Build after each. Do not proceed on a broken
  build. Do not invent files, types or APIs; if something needed does not exist,
  propose it and wait. Ask before any structural change.

## 4. Discovery step (do this first, before any edits)

1. Read `LayerControl.xaml` (the per-row control that carries the visibility eye),
   the layer manager control, and the layer view model. Understand how `IsVisible`
   drives the eye toggle, because the label toggle mirrors that pattern.
2. Confirm how the current tree model distinguishes a leaf layer from a group row,
   and confirm the ArcGIS SDK version in use exposes `FeatureLayer.LabelsEnabled`
   and `FeatureLayer.LabelDefinitions`.
3. Identify the theme brush keys from the merged theming work for: default icon
   colour, hover background, the on or active state, and the disabled or greyed
   state (see section 7 for the intended roles).
4. Open `docs/mieditor-theme-mockup-standalone.html` and study the Layers tab: the
   difference between leaf and group rows, the label icon beside the eye, its three
   states, and the eye and label interplay.
5. Confirm the branch and commit conventions.
6. Present the F1 plan and wait for approval before editing.

## 5. Behaviour specification (the mockup is the reference)

The mockup at `docs/mieditor-theme-mockup-standalone.html` shows the intended
placement, states and interaction in the Layers tab. Read it as code to extract
structure and the colour roles; you cannot render it, so treat the running
application as the thing to verify against, not the file.

- The label control sits on each leaf layer row, beside the visibility eye.
- Three states: enabled and off (labels available, currently hidden), enabled and
  on (labels drawing), and disabled or greyed (no label definition, inert).
- Clicking toggles the layer's labels; the state persists for the session.
- Hiding the layer with the eye also hides its labels; showing it again restores
  labels only if the label toggle is on.
- Group rows (those with an expand arrow) never show the control.

## 6. Mechanism

- Add a bindable property (for example `LabelsOn`) and a capability flag (for
  example `IsLabelToggleSupported`) to the leaf layer view model, mirroring the
  existing `IsVisible` pattern in `LayerControl.xaml`.
- Capability check: the layer is supported only if its `FeatureLayer` has one or
  more `LabelDefinitions`. If it has none, `IsLabelToggleSupported` is false and the
  control renders greyed and does nothing.
- The property sets `FeatureLayer.LabelsEnabled`. No `LabelDefinition` is created in
  code under any circumstances.
- Persist the per-layer state for the session using the same preferences mechanism
  the app already uses for layer state, if one exists.

## 7. Colour tokens (reuse the merged theme brushes)

The control must consume the existing semantic brushes from the theming work, so it
themes correctly in Light and Dark with no extra effort. The intended role mapping,
matching the visibility eye's styling:
- Default icon: the same brush the eye uses (the muted on-surface role).
- Hover background: the surface-variant role.
- On or active: background uses the primary role, icon uses the primary-on role.
- Disabled or greyed: the subtle on-surface role, at reduced opacity.

Confirm the exact brush keys during discovery and use whatever the theming work
named them. Do not define new colours.

## 8. Stories and queue

This is the queue. At the start of each session, read the checklist, take the next
unstarted item, state which and why, propose a short plan, and wait for approval
before editing. Update the checklist as you complete work. If the next step is
ambiguous or depends on a decision, stop and ask.

### F1. Inline label toggle on individual layers
Acceptance criteria:
- An enabled label toggle appears beside the eye on each leaf layer that has a label
  definition.
- On a leaf layer with no label definition, the control is shown but greyed and does
  nothing when clicked.
- Group rows show no label control.
- Toggling updates the map immediately and the state persists for the session.
- Hiding a layer hides its labels; they return only if both the layer and its label
  toggle are on.
- The control uses the theme brushes and looks correct in both themes.

### F2. Drive labelling from the published definition
Acceptance criteria:
- For a layer with one or more label definitions, on sets `LabelsEnabled` true and
  labels draw; off stops labelling.
- For a layer with no definition, the capability check reports it unsupported and
  the control stays greyed; no labelling is attempted and nothing is authored.

### Progress checklist
| Item | Status | Notes |
| --- | --- | --- |
| F1. Inline toggle on leaf layers, greyed when unsupported | done | batches 1 to 4 in migrations/; batch 4 revises the icon states, no background and a line when off |
| F2. LabelsEnabled wiring and capability check | done | shares the VM property with F1; wired in batch 1; runtime checks pass |

## 9. Definition of done

- The build passes with no new warnings from the change.
- The toggle appears only on leaf layers, is greyed on layers without a definition,
  and never appears on group rows.
- Turning it on and off draws and clears the layer's labels; state persists for the
  session; the eye and label interplay behaves as specified.
- The control themes correctly in Light and Dark using existing brushes, with no new
  colours or inline hex.
- No behavioural regression to visibility, the tree, or existing bindings.
- No AI-style artefacts in the output.

## 10. How to work and report

- Propose the plan first and wait for approval before mass edits.
- Keep diffs surgical and scoped. Do not reformat unrelated code.
- Use the repository's branch and commit conventions confirmed in discovery.
- After each batch, state what changed, the files touched, and the build result.
- On ambiguity or a missing dependency, stop and ask. Do not invent.

## 11. Reference artefacts

- Visual and behavioural reference: `docs/mieditor-theme-mockup-standalone.html`
  (the Layers tab shows the inline toggle, its states and the eye interplay).
- The theming work is merged; its semantic brushes are the colour source for this
  control.
