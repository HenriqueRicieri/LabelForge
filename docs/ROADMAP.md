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
themes and selected 100/125/150% Windows scales; those older captures did not measure
actual app scaling. The `adb1178` native tool now records RenderScaling/monitor scaling
1.0, 1.25 and 1.5 in both themes and broad/compact windows: 12 cases with actual client
dimensions. Compact Light editing, F2 and save/reopen passed with identical reopened
model/ZPL; all eight higher-scale captures retained those values. The full workflow
per combination remains open. A recovery banner contrast defect was reproduced and
fixed. See the [validation record](RELEASE-VALIDATION.md).

The `adb1178` editor was also exercised at measured 150% in a Light broad window:
text creation, font height/width and Auto, a three-line block, 90-degree rotation,
baseline anchor, undo/redo, OS clipboard, groups, save/reopen and ZPL/PNG/PDF export
passed. The complete reopened model and generated/exported ZPL matched. A repeated
Dark broad capture had no external notification, and the same model/ZPL remained
unchanged in Dark broad and compact windows. On the later `2d4b84e` source, a native
100% ruler guide moved from 192 to 361 dots and survived undo/redo and save/reopen.
Clear Light and Dark broad captures on that source measured 1.25 app/monitor scale,
1200 x 780 client DIPs, and retained the same model and ZPL. Next, repeat the
remaining editing/export checks per cell; keep each result tied to its source
build and actual display scale.

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

The `1baaa76` paint candidate passed integrity/version/notice-byte checks,
portable launch and reopening the dense synthetic label. It includes the paint,
text resize and user-data separation fixes after the `d63901b`
installer removed recent files in Codex's view. The original recent list was
restored/migrated exactly. The corrected Setup ran from ordinary Explorer and
installed the recorded `0.4.0+1baaa76` version in the normal per-user directory.
Explorer opened a synthetic `.lfl` directly in the installed app; the dense QA
label survived in recent files and reopened after Setup. The previous version
in that native directory was not independently observed, so legacy upgrade and
pristine install remain unverified. Uninstall and exact Rust notice provenance are also
open. Earlier candidates supply dense editing/export/recovery evidence.

The preceding `12847fc` candidate includes 22 original license/notice texts, inventoried
against 28 published runtime packages and both embedded fonts. Packaging verifies
exact dependency versions and notice bytes before running Velopack. The positive
check and three negative cases passed; ZIP/nupkg contents matched the sources.
This closes the app runtime/font inventory work. Native Setup/stub/Update.exe
dependency review continued in `eec7557`: it adds 493 original text segments for
277 conservative Windows dependency entries and byte-matched WebView2 loader
notices. Input hashes pin the vpk packer and three x86 helpers; negative checks
reject changed/missing inputs and unreviewed versions. The new ZIP/nupkg carry
33 verified notice/inventory files, and Setup embeds the exact reviewed nupkg.
Only the exact Rust compiler/standard-library provenance remains open in the native
notice review; release-date Rust notices are supplemental. The new candidate has
not been installed.

A loopback receive timeout in the packaging-source CI was addressed by test-only
`8b8eb40`, removing extra scheduling without changing production transport. The
following CI passed all nine jobs; all 1,345 local unit cases also passed.

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
