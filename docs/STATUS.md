# Project status

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
| 2026-09-25 | `e78aa0d` | New text and starters specify width; Auto explains the printer-default behavior |
| 2026-09-25 | `ab281bf` | Text-block wrapping width follows font resize and restores on cancellation |
| 2026-09-25 | `be97995` | Printable font sizing and text resize handles following the visual axis |
| 2026-09-25 | `4b9ecad`, `1351a07` | Dimensions and X/Y display the stored printable values after editing |
| 2026-09-25 | `8ff99d3` | Anchor changes preserve the drawn position |
| 2026-09-25 | `c8fb53d`, `201035c` | Centered, reversible rotations |
| 2026-09-24 | `e9388e7`, `5aaed97` | Predictable transform release/cancellation and preserved field settings on deselection |

## Verification

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

Separate CI builds of the E2E harness report 11 existing CA1416 warnings around
Windows registry/file-association calls. The harness is outside `LabelForge.sln`,
so a clean solution build does not cover those warnings.

The older [F31 run](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36169382263)
failed one dark-designer allocation assertion: the 78-element scene exceeded its
70 KB budget at 1x. The next four runs passed, including the baseline. This closes
the unknown historical result; it does not explain the variability. Investigation
remains in the [roadmap](ROADMAP.md).

## Git and distribution

At the source audit, `main` and `origin/main` pointed to `e78aa0d`, the working tree
was clean, and the remote had only `main`. No open PRs or issues were listed. These
are dated observations; remaining work is recorded in the roadmap.

The app declares `0.3.0`. Tags `v0.2.0` and `v0.3.0` exist; `v0.3.0` dates to
2026-07-13 and is 147 commits behind this baseline. The GitHub releases API lists
no releases. A tag, packaging code and a tested published installer are separate
deliverables.

`scripts/pack-windows.ps1` builds self-contained win-x64 Velopack packages. Its
default package version is still `0.1.0`; pass the intended release version explicitly.
Installation, upgrade, file association and recovery need fresh validation before the
next release. The script does not establish an update feed.

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
  readback is a snapshot. Printer and scanner checks remain a release gate.
- Native Windows display scaling at 100%, 125% and 150% remains unverified.

Recommended next work: native editing validation, physical output checks, CI reliability,
then a tested Windows release. See [Roadmap](ROADMAP.md).
