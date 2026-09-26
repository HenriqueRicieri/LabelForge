# Roadmap

Work order after the verified `e78aa0d` baseline, updated 2026-09-26.
The original feature milestones are implemented. The next batch should establish
reliable use on the target desktop, then make that version easy to install.
Native validation is now in progress. No release has been published.

Scope update, 2026-09-25: the maintainer removed real-printer validation because
hardware is currently unaffordable. G9 is outside the active plan and is no longer
a release gate. Physical output remains unverified; software transport/status tests
continue using fake printers and synthetic jobs.

## 1. Validate native editing on Windows [P1, IN PROGRESS]

Run the app at 100%, 125% and 150% display scaling, in both themes and regular/compact
windows. Exercise a synthetic dense label through creation, save/reopen and export.
Include font height/width, Auto, text blocks, rotated text, anchor changes, groups,
undo/redo, clipboard and field navigation. Headless checks cover these contracts but
cannot establish native focus, pointer feel or OS scaling.

Complete when a matrix records commit, OS, scaling, theme, window size and pass/fail
evidence; controls remain reachable; reopened documents and ZPL preserve the edits.
Reproduce discovered defects in focused regressions before fixing them. Local backlog: G8.

A fresh automation session worked; the earlier failure's cause remains unknown.
Native dense editing, grouping, clipboard, undo/redo, text-block transforms,
save/reopen, exports and offline viewing were exercised. Layout checks covered both
themes and selected 100/125/150% Windows scales; actual app RenderScaling and every
workflow combination remain unverified. A recovery banner contrast defect was
reproduced and fixed. See the [validation record](RELEASE-VALIDATION.md).

## 2. Reduce paint allocation and resolve warnings [P1, DONE 2026-09-26]

The allocation check now passes with substantial margin under its original
70 KB budget. The documented 78-element scene, warmup and 40-frame sampling
remain unchanged; platform guards resolve the 11 CA1416 warnings. Local backlog: G10.

The Windows association block has an OS guard and the separate harness builds
without warnings. CI `54a0be9` failed in both themes at a 74,536-byte median and
77,288-byte p95/max. Profiling placed 97% of local paint allocation in rulers:
cached `FormattedText` objects still reformatted their lines when drawn.

`1baaa76` caches bounded `TextLayout` objects, disposes them on eviction/detachment,
and avoids the complete quiet-zone report during selected-symbol painting.
Local samples dropped from 55,960 to 6,360 bytes; 30 checks per theme and all
1,345 unit tests passed. Ruler pixels matched across 16 before/after captures.
The 70 KB assertion, warmup and 40-frame sampling are unchanged. Source CI
passed all nine jobs; both themes measured 6,360 bytes for median/p95/max. The exact prior runtime/pool variation was not isolated.

## 3. Prepare the next Windows release [P1, IN PROGRESS]

Choose a version from the changes since `v0.3.0`, align app/package versions and build
an installer from a recorded commit. The candidate version is now `0.4.0`.
Packaging reads the app version by default; an explicit override applies to both
the published app and the Velopack package.

Test clean install, upgrade, `.lfl` launch, uninstall, save/reopen, recovery and the
offline viewer. Review bundled licenses, write installation/update instructions and
prepare release notes from the changelog.

The current `1baaa76` candidate passed integrity/version/notice-byte checks,
portable launch and reopening the dense synthetic label. It includes the paint,
text resize and user-data separation fixes after the `d63901b`
installer removed recent files in Codex's view. The original recent list was
restored/migrated exactly. Explorer showed an Open With chooser despite the
association in Codex's registry view. Repeat the corrected installation from
ordinary Explorer; global upgrade/association, clean install and uninstall remain
open. Earlier candidates supply dense editing/export/recovery evidence. Full
notice review remains open.

Complete when the candidate passes the native checks above, all CI jobs pass,
versions agree, and installer smoke results/checksums are recorded. Publishing the
release and choosing a distribution/update feed are separate actions after that evidence
exists. Local backlog: G11.

## Later, when a workflow requires it

| Work | Decision | Trigger to revisit |
| --- | --- | --- |
| Localization (F5) | Deferred; English-only UI | A target audience/language and resources to extract/maintain strings |
| More ZPL import coverage | Supported-model import works; full ZPL coverage remains out of scope | Representative input loses a needed command/resource |
| Variable input rules (C2) | Deferred; no per-variable operator prompt | Standalone printing needs pick lists, masks or required values |
| Stored formats `^DF`/`^XF` (D2) | Deferred; ordinary jobs/downstream substitution work | Measured batch-transfer cost or a required printer-memory workflow |
| Font audit (D3) | Deferred; supported fonts are resident 0 and A-H | Downloaded/optional fonts enter the model |
| Downscaled underlay cache (H8) | Measured and deferred; saving did not justify another cache | Native workloads show a significant bottleneck |
| Cross-platform support | Planned, unvalidated | Windows release baseline is established and target OS checks/packaging are funded |

Prioritize a reproduced editing/printing problem or a documented operator workflow
before adding features to match another label editor.

## Record completion

For each item, record the problem, commit, relevant regression results and remaining
limits. Update [status](STATUS.md), [changelog](../CHANGELOG.md) and roadmap when their
conclusions change. Local planning records retain detailed history. Do not close work
from CI alone when acceptance requires native UI or an installer.
