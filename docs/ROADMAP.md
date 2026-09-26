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

## 2. Investigate CI variability and warnings [P1, IN PROGRESS]

Compare the dark-designer allocation failure at `1351a07` with passing runs. Determine
whether scene state, warmup, runtime or actual allocation growth explains the 70 KB
budget crossings. Preserve a meaningful regression assertion; changing the budget
needs measurements. Address CA1416 around E2E Windows registry/file-association calls
with appropriate platform guards or annotations.

Complete when the failure has a reproducible explanation or a bounded documented
measurement method, the check still catches a regression, and a fresh harness build
resolves the 11 known platform warnings. Local backlog: G10.

The Windows association block now has an OS guard and the separate harness builds
without warnings. Paint logs now report viewport, theme, overlays and allocation
median/p95/max at 1x. The 70 KB threshold is unchanged. Historical variability is
not yet explained; keep the investigation open until comparable failing data exists.

## 3. Prepare the next Windows release [P1, IN PROGRESS]

Choose a version from the changes since `v0.3.0`, align app/package versions and build
an installer from a recorded commit. The candidate version is now `0.4.0`.
Packaging reads the app version by default; an explicit override applies to both
the published app and the Velopack package.

Test clean install, upgrade, `.lfl` launch, uninstall, save/reopen, recovery and the
offline viewer. Review bundled licenses, write installation/update instructions and
prepare release notes from the changelog.

The `d63901b` candidate passed integrity/version/notice-byte checks and native
portable recovery. The earlier `450b1df` candidate supplies dense editing/export
evidence. Installed 0.2.1 remains unchanged; upgrade, clean install, native
association and uninstall are pending. Full notice review remains open.

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
