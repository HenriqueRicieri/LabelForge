# Project status

Current source correction: `1baaa76` caches the ruler text layouts and queries
quiet-zone warnings only for selected symbols during paint. Local allocation fell
from 55,960 to 6,360 bytes at 1x, with the 70 KB budget unchanged. All 1,345 local
unit tests and 30 focused paint checks per theme passed. Sixteen before/after
ruler captures matched pixel-for-pixel. The source CI passed all nine jobs.

Installer testing previously exposed recent-file loss. `54a0be9` separates and
migrates user data; the original recent list was restored and migrated exactly.
The current `0.4.0` candidate includes that fix, text resizing and the paint
correction. Native DPI and global installer/association checks remain open.
Real-printer validation remains outside the active plan for cost reasons.
See the [validation record](RELEASE-VALIDATION.md) for evidence and remaining gates.

## Text resize follow-up (2026-09-26)

Text edge handles now apply font limits only to the dimension being resized.
They preserve the untouched height, character width and wrapping width from an
imported label, including values outside the inspector's editing range. Four unit
regressions and one canvas drag failed before the fix and pass afterward. Local
validation passed 1,330 unit tests, 34 transform checks and 31 selection-scale
checks; the Release solution build has zero warnings and errors. The focused
before/after transcripts differ only in the new regression result and summary.

This change is included in the current `1baaa76` candidate. The native DPI and
installer checks in the roadmap remain open.

## Baseline source audit (2026-09-25)

Verified on 2026-09-25 in America/Sao_Paulo. Source baseline:
[`e78aa0d`](https://github.com/HenriqueRicieri/LabelForge/commit/e78aa0d38075cc43f244bb00906d7d433b1f41aa).
Its CI ran on 2026-09-26 UTC.

LabelForge is a working Windows-first desktop application. The original M0-M5
implementation is complete: designer, offline viewer, storage, printing, exports,
starter gallery and Windows packaging are present. Recent work has focused on
predictable editing and usable controls in small windows. Release validation and
distribution still need work.

## Implemented behavior

| Area | Current behavior |
| --- | --- |
| Designer | Text, linear barcodes, QR, Data Matrix, PDF417, images and shapes; groups, layers, alignment, guides, snapping, undo/redo and clipboard |
| Workflow | Labeled tools, place-and-type, repeat placement, Elements search, quick content editing, F2 and previous/next field navigation |
| Measurements | Centimeters by default, exact printer-dot option, stored printable values after editing, centered rotations, stable anchor changes, text-axis and text-block resize |
| Media | Zebra catalog, user presets, continuous stock, corner radius, multi-across layouts and printer profiles |
| Data | Configurable markers, field catalogs, function-signature completion, samples, counters and date/time sources |
| Viewer/import | Offline live preview, selected-label sizing, multiple labels, diagnostics, embedded graphic import and magnified recalls |
| Output | TCP 9100 with status readback, Windows RAW spooler, print settings, label ZPL, exact print-job ZPL, PNG and PDF |
| App | `.lfl` files, recent files, crash recovery, both themes, shortcut help, starter gallery and Velopack packaging script |

See the [README](../README.md) for the feature reference and
[changelog](../CHANGELOG.md) for changes since the last tag.

## Latest changes

| Date | Commit | Result |
| --- | --- | --- |
| 2026-09-26 | `1baaa76` | Cached ruler layouts; selected-symbol quiet-zone query; 6,360-byte local paint samples |
| 2026-09-26 | `54a0be9` | User data separated from installer cleanup; legacy migration and eight regressions |
| 2026-09-26 | `3a134c8` | Text edge resize preserves untouched imported dimensions |
| 2026-09-26 | `d63901b` | Theme-aware recovery banner; six contrast checks pass after two failures on the original dark buttons |
| 2026-09-25 | `e78aa0d` | New text and starters specify width; Auto explains the printer-default behavior |
| 2026-09-25 | `ab281bf` | Text-block wrapping width follows font resize and restores on cancellation |
| 2026-09-25 | `be97995` | Printable font sizing and text resize handles following the visual axis |
| 2026-09-25 | `4b9ecad`, `1351a07` | Dimensions and X/Y display the stored printable values after editing |
| 2026-09-25 | `8ff99d3` | Anchor changes preserve the drawn position |
| 2026-09-25 | `c8fb53d`, `201035c` | Centered, reversible rotations |
| 2026-09-24 | `e9388e7`, `5aaed97` | Predictable transform release/cancellation and preserved field settings on deselection |

## Verification

The [source CI](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36258915331)
passed all nine jobs for `1baaa76`: 1,316 public unit cases, 570 designer checks
per theme, 400 layout, 34 transform, 29 centimeter and viewer 15/36/15 checks.
Both themes measured 6,360 bytes for median/p95/max at 1x.
The preceding [`54a0be9` run](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36257098525)
failed both designer allocation checks: median 74,536 bytes, p95/max 77,288 bytes,
with the same 776 x 477-DIP viewport and overlay flags. All other jobs passed.
The correction removes repeated ruler text formatting; local samples now use
6,360 bytes, confirmed in both CI themes. G10 is complete under the unchanged
measured-scene budget. The exact runtime/pool condition behind the earlier variable totals
was not isolated. The original budget and sample method remain in place.

The earlier [`d63901b` CI](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36215947181)
passed all nine jobs on 2026-09-26: 1,297 tests, 569 designer checks per theme,
400 layout, 33 transform, 29 cm and viewer 15/36/15. Its layout count includes the
six recovery contrast checks. Local validation passed 1,326 tests and 400 layout
checks. The older audit counts below remain a dated comparison.

The [baseline CI](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36207618441)
passed all nine jobs. Counts below come from its logs.

| Check | Result |
| --- | --- |
| Release solution build | Zero warnings and errors |
| Unit/fixture tests in CI | 1,297 passed |
| Designer light / dark | 569 graded checks passed per theme |
| Workspace layout | 394 passed |
| Transform gestures | 33 passed |
| Centimeter units | 29 passed |
| Viewer size / layout / comparison | 15 / 36 / 15 passed |
| Local audit: Release solution build and tests | Zero warnings/errors; 1,326 tests passed |

Local tests include the optional private corpus; CI uses committed synthetic fixtures.
Those totals therefore differ. The harness grades explicit assertions; other transcript
lines still require inspection when behavior changes. Headless checks do not establish
native display scaling or physical printer behavior.

At the baseline, separate CI builds of the E2E harness reported 11 CA1416 warnings around
Windows registry/file-association calls. The harness is outside `LabelForge.sln`,
so a clean solution build did not cover those warnings. The current OS guard resolves
them; the fresh separate build has zero warnings and errors.

The older [F31 run](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36169382263)
failed one dark-designer allocation assertion: the 78-element scene exceeded its
70 KB budget at 1x. The next four runs passed, including the baseline. This closes
the unknown historical result; it does not explain the variability. Investigation
remains in the [roadmap](ROADMAP.md).

## Git and distribution

At the source audit, `main` and `origin/main` pointed to `e78aa0d`, the working tree
was clean, and the remote had only `main`. No open PRs or issues were listed. These
are dated observations; remaining work is recorded in the roadmap.

The original audit app declared `0.3.0`; the current release candidate is `0.4.0`.
Tags `v0.2.0` and `v0.3.0` exist; `v0.3.0` dates to
2026-07-13 and is 147 commits behind this baseline. The GitHub releases API lists
no releases. A tag, packaging code and a tested published installer are separate
deliverables.

`scripts/pack-windows.ps1` builds self-contained win-x64 Velopack packages. Its
default now comes from the app project; an override also sets the published app version.
Portable launch and reopening the 78-element synthetic label passed on `1baaa76`;
recovery evidence comes from earlier candidates.
Installer testing exposed recent-file loss and a differing Codex/Explorer association
view. The recent list was restored and migrated to the separated data directory.
The corrected installer, global association, clean install and uninstall remain open. The script
does not establish an update feed. See the [candidate record](RELEASE-VALIDATION.md)
for package hashes and the distinction between old/new candidate evidence.

## Known limits

- The UI ships in English; strings have not been extracted into localization resources.
- Windows is the current target. Linux/macOS behavior and packaging are unvalidated.
- ZPL import covers the supported model. Downloaded fonts, stored formats and
  printer-clock definitions still have gaps. Resources held only in printer memory
  cannot be recovered from a source file.
- Font 0 uses a pinned preview substitute. Fonts B/E/F/G/H and some `^FB`/`^FR`
  behavior have renderer limitations. Auto character width is an estimate; the printer
  chooses its default when width is omitted. New fields use explicit width.
- A successful network send confirms byte delivery, not physical printing. Status
  readback is a snapshot. Physical printer/scanner checks are outside the current plan
  by the maintainer's decision and do not block release.
- Native Windows display scaling at 100%, 125% and 150% remains unverified.

Recommended next work: confirm actual native DPI and complete the workflow matrix,
validate the installer and dependency notices. The paint budget correction (G10)
is confirmed in the source CI.
See [Roadmap](ROADMAP.md).
