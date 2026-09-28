# Project status

## Native workflow progress (2026-09-28)

The measured 150% Light broad editor workflow passed text/font/block edits,
rotation, anchor, clipboard, grouping, save/reopen and ZPL/PNG/PDF export;
the complete model and generated ZPL survived reloading. Dark broad/compact
captures retained them. Clear Light/Dark broad captures on `2d4b84e` measured
1.25 app/monitor scaling and retained the same 80-element model and ZPL.

With the later guide-precision fix, native Light 125% testing inserted a guide
at 248 printer dots, dragged it to 419, undid/redid to the exact positions and
saved/reopened it at 419 with matching ZPL. Monitor 2 was restored to 100%.
The complete editing/export workflow in each scale/theme/size cell remains open.
These checks use the source editor, not the unsigned `eec7557` installer.

## Ruler guide precision (2026-09-28)

Double click and the ruler menu now insert guides at the nearest printer dot.
Previously both rounded the pointer position to a whole millimeter, shifting the
saved guide by several dots. The menu shows the centimeters derived from the
exact stored dot. Guide dragging and undo behavior are unchanged.

Three focused canvas checks failed before the fix and passed afterward. All 53
transform checks, 29 centimeter checks and 1,345 local unit tests passed. The
before/after transcript differs only in those three results and its summary.
The complete Light designer scenario passed 589 checks. The Release solution
build has zero warnings and errors. The existing installer candidate does not
include this source change.

## Guide gesture reliability (2026-09-27)

Permanent horizontal and vertical guides now commit the final pointer position,
including the final decision to remove a guide on its corresponding ruler. They
retain the initial grab offset, so a nearby click does not move the guide. Escape
restores its original position without adding an undo step; a subsequent release
cannot commit the cancelled drag.

The focused harness reproduced 14 failures before the fix. Afterward, all 50
transform checks and 1,345 local unit tests passed. The complete before/after
transcripts differ only in those 14 results and the graded summary. The Release
solution build has zero warnings and errors. This source change is newer than the
packaged candidate below; the native workflow and installer gates remain open.

## Previous QA and packaging evidence

Current QA source: `adb1178` adds an isolated native measurement tool using the
production editor and styles. Local validation passed all 1,345 unit tests and
15 headless bootstrap/storage checks. [Its CI](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36276142352)
passed all nine jobs, including the new storage check.

Native Windows build 26200 recorded actual app/monitor scaling of 1.0, 1.25 and 1.5
for the 78-element synthetic label in Light/Dark and broad/compact windows: 12 cases.
Actual client dimensions are in the validation record. The complete model and ZPL
remained unchanged across all eight higher-scale captures. Original OS scales were restored.
The compact Light smoke passed inspector access, F2, text editing and save/reopen;
the complete reopened model and generated ZPL matched. The complete workflow per
combination remains open; the newer clear 125% captures are noted above. This is
developer-editor evidence;
the packaged candidate below remains `eec7557`.

Current packaging correction: `eec7557` adds 493 original native text segments
for a conservative 277-entry Windows dependency inventory, plus byte-matched
WebView2 loader notices and supplemental release-date Rust notices. Packaging
verifies the pinned vpk 1.2.0 packer and its three x86 helper inputs. Reviewed inputs
passed; changed/missing helpers, an unreviewed version and a missing Rust notice
were rejected. ZIP/nupkg CRC, app bytes, versions and all 33 notice/inventory files
passed. The Setup's embedded nupkg matched exactly. Native code sections matched
apart from resource customization and the verified bundle offset/length fields.

[CI on `eec7557`](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36266926091)
passed all nine jobs.

The existing inventory still covers 28 runtime packages and two embedded fonts.
Exact Rust compiler/standard-library provenance remains open: the upstream nightly
was unpinned and its build logs have expired. The supplemental notice snapshot
cannot establish that revision. The new unsigned candidate has not been installed;
native installation/association evidence below is from `1baaa76`.

The preceding test-only correction, `8b8eb40`, removed extra scheduling from the
loopback receiver after a CI timeout. Six focused and all 1,345 local tests passed;
[the corrected CI](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36264994459)
passed all nine jobs. Production network printing is unchanged.

Paint correction: `1baaa76` caches the ruler text layouts and queries
quiet-zone warnings only for selected symbols during paint. Local allocation fell
from 55,960 to 6,360 bytes at 1x, with the 70 KB budget unchanged. All 1,345 local
unit tests and 30 focused paint checks per theme passed. Sixteen before/after
ruler captures matched pixel-for-pixel. The source CI passed all nine jobs.

Installer testing previously exposed recent-file loss. `54a0be9` separates and
migrates user data; the original recent list was restored and migrated exactly.
The paint, runtime-notice and native-notice candidates include that fix, text resizing and the paint
correction. The `1baaa76` Setup from ordinary Explorer installed its recorded version; direct
`.lfl` opening and preservation of the synthetic recent entry passed. The
complete native workflow matrix, legacy upgrade, pristine install,
uninstall and exact Rust notice provenance
remain open.
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

This change is included in the paint, runtime-notice and native-notice candidates. The native workflow and
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
| 2026-09-26 | `adb1178` | Isolated native scaling measurements; all nine CI jobs passed; 12 measured scale/theme/window cases recorded |
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
The `1baaa76` Setup installed from ordinary Explorer into the normal per-user
directory. Windows file properties confirmed the `0.4.0+1baaa76` source version;
Explorer opened a synthetic `.lfl` directly in that installed app. The dense QA
label remained in recent files and reopened after Setup. Legacy upgrade,
pristine install and uninstall remain unverified. The script does not establish
an update feed. The `eec7557` candidate extends the runtime inventory with native
dependency/WebView2 notices and pins the packer/helper inputs. Exact Rust notice
provenance remains open; this candidate's native Setup has not been run. See the [candidate record](RELEASE-VALIDATION.md)
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
- Native scaling 1.0/1.25/1.5 is measured across 12 developer-editor layouts. The
  complete per-combination workflow remains unverified; external notifications
  partly obscured some broad captures.

Recommended next work: complete the native workflow matrix and unobscured broad captures,
validate legacy upgrade, pristine install and uninstall, and establish the native
Rust compiler/standard-library notice revision. Runtime/native crate and WebView2
notice inventories are checked; G11
remains in progress. The paint budget correction (G10)
is confirmed in the source CI.
See [Roadmap](ROADMAP.md).
