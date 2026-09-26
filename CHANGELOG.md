# Changelog

User-visible changes on `main` since `v0.3.0`, reconciled with source baseline
`e78aa0d` on 2026-09-25. The current candidate targets `0.4.0`; these changes have
not yet been published as a release.

## Unreleased

### Editing and workspace

- Fixed recovery buttons becoming unreadable in the dark theme. The recovery
  banner now follows the selected theme.
- Added labeled tools, a resizable/collapsible inspector with Properties, Elements
  and Data tabs, compact layouts and a separate label-setup dialog.
- Added place-and-type, repeat placement, field search, F2, quick content editing
  in Elements and previous/next field navigation.
- Kept viewer actions, label setup and shortcut help reachable in narrow windows.
- Changed physical measurements to centimeters by default, retaining exact dots
  and showing stored printable values after editing.
- Stabilized transform release/cancellation, deselection, centered/repeated rotation
  and top-left/baseline anchor changes.
- Made text handles resize height, character width or both on the visual axis.
  Text blocks resize wrapping width with horizontal/proportional font changes.
- Added independent bitmap-font height/width multiples and explicit Auto width.
  New text and starters specify width; Auto delegates it to the printer and shows
  a preview estimate.
- Added groups, directional area selection, multi-selection scaling, OS clipboard,
  layer reordering, spacing guides, draw-to-size, image drops and canvas overlays.

### Labels, data and output

- Added user media presets, corner radius, continuous stock and multi-across layouts.
- Added PDF417, ellipse, diagonal line, rounded boxes and built-in printer fonts.
- Expanded editable import, text blocks, reverse fields, serialized data and graphic
  reuse; import reports unsupported commands/resources.
- Added field catalogs, configurable markers, function-signature completion,
  counters, date/time sources and GS1/check-digit assistance.
- Added exact print-job ZPL alongside ordinary ZPL export, PDF output, print-job
  settings and media handling controls.
- Added TCP status checks, an explicit unchecked path and delivery feedback.
- Added crash recovery, file association and a starter gallery.

### Viewer and rendering

- Kept selected-label size tied to effective `^PW`/`^LL`, including exact dot counts.
- Tied comparisons to one snapshot and discarded stale responses.
- Rendered magnified `^XG` and bound imported recalls to the active download.
- Fixed bare tilde parameters in `^BX` being misread as commands.
- Improved responsiveness through bounded render work, gesture layers, drawing
  caches and direct output pixel buffers.
- Added synthetic fixtures and nine CI jobs covering build/tests, both designer
  themes, workspace, transforms, centimeters and viewer flows.

### Documentation

- Added a public index, verified status, prioritized roadmap and development guide.
  Reconciled local plans with current source and CI history.
- Removed real-printer validation from the active plan at the maintainer's request;
  retained the documented limit that physical output is unverified.

### Release preparation

- Guarded E2E shell-association checks by Windows platform, resolving 11 CA1416 warnings.
- Added viewport/theme/overlay and allocation percentile diagnostics for the paint
  budget assertion without raising its 70 KB limit.
- Set the candidate app version to `0.4.0`. Packaging reads that version by default
  and applies explicit overrides to both app and package.
- Added an output-directory option for rebuilding a candidate without replacing
  earlier packages, and included the existing project, dependency and font notices.

## Tagged versions

| Tag | Date | Reference |
| --- | --- | --- |
| `v0.3.0` | 2026-07-13 | [Source](https://github.com/HenriqueRicieri/LabelForge/tree/v0.3.0) |
| `v0.2.0` | 2026-07-10 | [Source](https://github.com/HenriqueRicieri/LabelForge/tree/v0.2.0) |

No GitHub releases were listed during the audit. See [Project status](docs/STATUS.md)
for current verification and release limits.
