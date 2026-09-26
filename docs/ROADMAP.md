# Roadmap

Proposed work order after the verified `e78aa0d` baseline, reviewed 2026-09-25.
The original feature milestones are implemented. The next batch should establish
reliable use on the target desktop and printers, then make that version easy to install.
This documentation audit did not perform native/hardware validation or publish a release.

## 1. Validate native editing on Windows [P1, TODO]

Run the app at 100%, 125% and 150% display scaling, in both themes and regular/compact
windows. Exercise a synthetic dense label through creation, save/reopen and export.
Include font height/width, Auto, text blocks, rotated text, anchor changes, groups,
undo/redo, clipboard and field navigation. Headless checks cover these contracts but
cannot establish native focus, pointer feel or OS scaling.

Complete when a matrix records commit, OS, scaling, theme, window size and pass/fail
evidence; controls remain reachable; reopened documents and ZPL preserve the edits.
Reproduce discovered defects in focused regressions before fixing them. Local backlog: G8.

## 2. Validate physical printing and status [P1, TODO]

Print synthetic labels through TCP 9100 and, where hardware is available, the Windows
RAW spooler. Cover available 8/12/24 dpmm printers and record missing combinations.
Check size, accents, explicit/Auto widths, rotation, text blocks, graphics, barcode
scanning, counters and dates. Compare exported print-job bytes with the sent job.
Exercise pause, paper-out and head-open where supported.

Complete when tested printer/driver/stock combinations have recorded output and
scan/readback evidence, limitations are documented, and uncertain sends give clear
guidance before retrying. A TCP write or rendered image does not establish physical
output. Local backlog: G9.

## 3. Investigate CI variability and warnings [P1, TODO]

Compare the dark-designer allocation failure at `1351a07` with passing runs. Determine
whether scene state, warmup, runtime or actual allocation growth explains the 70 KB
budget crossings. Preserve a meaningful regression assertion; changing the budget
needs measurements. Address CA1416 around E2E Windows registry/file-association calls
with appropriate platform guards or annotations.

Complete when the failure has a reproducible explanation or a bounded documented
measurement method, the check still catches a regression, and a fresh harness build
resolves the 11 known platform warnings. Local backlog: G10.

## 4. Prepare the next Windows release [P1, TODO]

Choose a version from the changes since `v0.3.0`, align app/package versions and build
an installer from a recorded commit. The packaging default `0.1.0` must not silently
identify the next build; pass an explicit version until the process is aligned.

Test clean install, upgrade, `.lfl` launch, uninstall, save/reopen, recovery and the
offline viewer. Review bundled licenses, write installation/update instructions and
prepare release notes from the changelog.

Complete when the candidate passes the native/printer checks above, all CI jobs pass,
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
from CI alone when acceptance requires native UI, an installer or hardware.
