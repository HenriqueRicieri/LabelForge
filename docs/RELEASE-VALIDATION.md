# Windows release candidate validation

Updated 2026-09-26. Candidate version: `0.4.0`. Native workflow evidence was
collected on `450b1df`; recovery contrast was validated on `d63901b`. The current
candidate includes text resize, user-data and paint corrections through
[`1baaa76`](https://github.com/HenriqueRicieri/LabelForge/commit/1baaa765b39b005f9cfbeb04f9266c924a36688f).
These are local unsigned packages. No release or update feed has been published.

Real-printer validation is excluded at the maintainer's request because hardware
is currently unaffordable. Physical printing and barcode scanning remain unverified
and do not gate this release.

## Native Windows evidence

Tests ran on Windows 11 Pro, build 26200, with synthetic labels. A fresh desktop
automation session worked after the earlier `CreateProcessWithLogonW failed: 1056`
launch failure. The cause of that earlier failure and a specific repair remain
unknown; native testing can now proceed.

| Check | Result and scope |
| --- | --- |
| Create/edit/save | Gallery label passed; 0.51 cm font-height input stored 41 dots; Auto retained zero width |
| Dense label | Opened and edited 78 elements: 26 text, 26 Code 128 and 26 QR fields |
| Group and OS clipboard | Grouped 78 elements; pasted 156 total with unique IDs and separate groups |
| Undo/redo | Saved models matched grouped/pasted states; undo restored the ungrouped baseline |
| Field focus/navigation | Native F2 and field navigation exercised, including compact inspector overlays |
| Text block, rotation, anchor | Resized wrapping width to 204 dots; saved rotated text with baseline anchor and 18/15-dot font dimensions |
| Save/reopen/ZPL | Reopened label commands/content matched exported ZPL after normalizing UIA whitespace |
| Label and print-job ZPL | Both exported; byte-identical for this one-copy job without counters or timestamps |
| PNG/PDF | PNG verified at 960 x 800; PDF had one 340 x 283-point page for 120 x 100 mm stock; rendered PDF inspected |
| Offline viewer | Rendered at 12 x 10 cm / 203 dpi; two known unsupported-command diagnostics for `^PW`/`^LL` remained |
| Portable launch | Passed on `1baaa76`, including reopening the synthetic 78-element label and viewing its rulers at 100% canvas zoom; this zoom is separate from OS DPI |
| Installed app launch and association | `1baaa76` Setup ran from ordinary Explorer; a synthetic `.lfl` opened directly in the installed app and the dense QA label reopened from recent files |
| Forced interruption/recovery | `450b1df` recovered an unsaved synthetic label after terminating only the QA process; saved model and element ID matched |
| Corrected recovery banner | `d63901b` Light/Dark screenshots showed readable buttons; replay of the same synthetic recovery fixture preserved the complete saved model |

These workflow checks span several sessions and layouts. They do not establish
every workflow at every scale/theme/window combination.

### Scaling matrix remains incomplete

Layout and field-focus checks ran in both themes at selected Windows scales of
100%, 125% and 150%. At 100%, regular captures were 1202 x 792 logical pixels and
compact captures were 722 x 792. At higher selected scales, compact captures were
722 x 632 and broad captures were maximized at 1920 x 1032.

Windows accepted the scale selections, but the app's actual `RenderScaling` was
not independently measured. The captures show reachable controls and focus in
the observed layouts; they do not prove native DPI coverage. G8 stays in progress
until actual app scaling and the full workflow matrix are recorded. The original
100% Windows scale was restored after testing.

## Recovery defect and regression

The original yellow banner used dark-theme buttons with only 1.09:1 contrast.
The fix gives the banner theme-specific background/text colors and retains normal
button styles. The focused `recovery-ui` regression grades the message and both
buttons in each theme: six checks. Before the fix, the two dark button checks
failed; after the fix, all six passed. Dark button/message contrast is now
5.59:1 / 8.87:1; Light contrast is 11.76:1 / 8.15:1. Automated assertions cover
normal button state; hover contrast is not asserted.

## Installer attempt and user-data correction

The approved Setup from `d63901b` detected installed 0.2.1, offered an upgrade and
launched 0.4.0. The observed process path was under the Codex package's LocalCache.
The original recent-files JSON disappeared from the installer directory during
that attempt. Its pre-test backup was restored and verified byte-for-byte.

`54a0be9` moves the default data paths to `LabelForge.UserData`, a sibling of the
installer directory. The original recent list was migrated and its SHA-256 still
matched the backup. Eight regression checks cover separated paths, preservation
after installer-directory removal, migration conflicts/errors and live recovery
locks. Four path checks failed before the fix; all eight pass afterward.

Before upgrading older builds, use the corrected portable or its migration-only
command and keep a backup. See [migration instructions](DEVELOPMENT.md#windows-packaging).
The incoming app cannot rescue data already deleted by an older installer.

The hook wrote `.lfl` and its open command in the registry view seen from Codex,
but opening the synthetic file in Explorer produced the Open With chooser.
This is not an association pass. The differing LocalCache and Explorer views are
consistent with [MSIX virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization);
that is an inference, not proof of a LabelForge association-code defect. The
corrected Setup was subsequently tested from ordinary Explorer below. No Windows
virtualization or security settings were changed.

The corrected portable launched successfully through Explorer. Its recent menu
was empty in that process context; this does not establish that it shared Codex's
migrated recent-files view. The earlier candidate hashes below retain the
failed-upgrade provenance.

### Corrected Setup from ordinary Explorer

The approved `1baaa76` Setup, with the SHA-256 recorded below, ran from ordinary
Explorer and launched `%LocalAppData%/LabelForge/current/LabelForge.App.exe`.
Windows file properties showed file version `0.4.0.0` and product version
`0.4.0+1baaa765b39b005f9cfbeb04f9266c924a36688f`. A synthetic `.lfl` opened
directly in that installed app from Explorer, without an Open With chooser.

The synthetic 78-element label remained in the recent menu after Setup and
reopened successfully. Explorer showed `recent-files.json` and the recovery
folder under `%LocalAppData%/LabelForge.UserData`, outside the install directory.
The separate recent-list backup visible from Codex also retained its original
SHA-256. These observations cover the known QA recent entry and separated paths;
they do not establish preservation of every settings/catalog/recovery file.

No upgrade prompt appeared, and the previous version in the native install
directory was not independently observed. This passes installation, installed
source-version verification and `.lfl` association. Legacy upgrade, a pristine
clean install and uninstall remain unverified. Installed notice bytes were not
independently compared; archive notice-byte checks remain the evidence for bundling.

## Paint allocation correction

[CI on `54a0be9`](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36257098525)
failed the 70 KB assertion in both themes: median 74,536 bytes, p95/max 77,288.
The viewport was 776 x 477 DIPs, outlines off and printer-dot grid enabled.
A preceding passing CI recorded 55,960 bytes, with a 74,536-byte maximum in Dark.
Local reruns of the unchanged focused and complete designer measured 55,960.
Disabling tiered compilation in one local test process did not change that sample.

Temporary stage profiling attributed 53,288 of 55,024 bytes to the rulers after
optimizing the selected-symbol warning query. Avalonia's cached `FormattedText`
object still [formats its lines during drawing](https://github.com/AvaloniaUI/Avalonia/blob/12.1.0/src/Avalonia.Base/Media/FormattedText.cs).
The correction caches `TextLayout` objects, bounded to 128 entries and disposed
on eviction or canvas detachment. The complete quiet-zone report is preserved;
canvas outlines query selected symbols and stop at the first warning.

Final local Light/Dark samples each recorded 6,360 bytes for median/p95/max and
passed all 30 focused checks. Seven new unit cases cover warning equivalence,
four rotations, continuous stock, frames and live changes. The full local suite
passed 1,345 tests. Sixteen ruler captures matched the old rendering pixel-for-pixel
across both themes, 8/24 dpmm and 0.05/1/8/40x zoom after pan. Profiling code was
removed before committing. The source CI passed all nine jobs.

The budget remains 70 KB. The measurement still warms 80 paints, warms five frames
at each zoom and samples 40 paints of the same 78-element scene. No runtime flags
were added to CI. The exact runtime/pool condition behind the old variable totals
was not isolated; repeated text formatting was removed from the measured path.

## Software and packaging evidence

| Check | Result |
| --- | --- |
| Release solution build | Passed, zero warnings/errors on `1baaa76` |
| Local unit tests | 1,345 passed, including eight user-data and seven selected-warning cases plus the optional private corpus |
| Full workspace layout | 400 checks passed, including six recovery checks |
| Separate E2E build | Passed; Windows association OS guard removed 11 CA1416 warnings |
| Paint allocation budget | Unchanged at 70 KB; final focused runs passed 30 checks each, median/p95/max 6,360 bytes at 1x in both themes |
| Allocation remediation | Ruler layout cache and selected-warning query validated locally; old runtime/pool variability remains unisolated |
| Fresh CI | [Source-matched run](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36258915331) passed all nine jobs: 1,316 public unit cases, 570 designer checks per theme, 400 layout, 34 transform, 29 cm and viewer 15/36/15 |
| Package integrity | ZIP/nupkg CRC checks passed; app exe/dll bytes matched publish; app and package declare 0.4.0 |
| Bundled notices | LICENSE, THIRD-PARTY-NOTICES.md and font OFL.txt byte-matched to source; full dependency notice review remains open |
| Install and `.lfl` association | Corrected Setup passed from ordinary Explorer; installed source version confirmed; synthetic file opened directly and known recent entry survived |
| Legacy upgrade/clean install/uninstall | Earlier Codex-view upgrade exposed recent-list loss; restored/migrated exactly. Native legacy upgrade, pristine install and uninstall remain unverified |

Raw screenshots, synthetic files, export comparisons, matrix entries and logs
remain in ignored local artifacts. Public records omit machine paths and user state.

## Current candidate files

Built from `1baaa765b39b005f9cfbeb04f9266c924a36688f` into
`artifacts/releases/candidate-0.4.0-paint`. App informational version is
`0.4.0+1baaa765b39b005f9cfbeb04f9266c924a36688f`. Archive CRC, published app bytes,
notices and versions passed. Native portable launch/reopen and corrected Setup
installation/association passed. Existing notices are bundled; the full dependency
audit is open.

| File | SHA-256 |
| --- | --- |
| LabelForge-win-Setup.exe | `26babbc10b816a6219f8700cce1a8b799fe41aaa8ff41cfe8b3697d303d7a943` |
| LabelForge-win-Portable.zip | `d3141582992344187cd1ff250bde247062826f0ebdbc15ad457660552c427b6b` |
| LabelForge-0.4.0-full.nupkg | `deef2ef63372308409c35571671809855018e72e1a377c220f2700697030f305` |

### Previous user-data candidate

Retained from `54a0be9a550711348f51d898babf9ff9bba6663f` into
`artifacts/releases/candidate-0.4.0-user-data`. App informational version is
`0.4.0+54a0be9a550711348f51d898babf9ff9bba6663f`. Archive CRC, published app bytes, notices and versions passed.

| File | SHA-256 |
| --- | --- |
| LabelForge-win-Setup.exe | `b152ba8e9bebcab821c2f9e6448dafb2fd5e0eee22573c8a744577878081a7cf` |
| LabelForge-win-Portable.zip | `cf06eefe14d20f2f3704c4ce619837c49e211fe41aa36b7accb9b279d9d3ebe9` |
| LabelForge-0.4.0-full.nupkg | `127eb78833c3d2b5d37604919af4572e559e730e4f7776b52dd7a727cf47b417` |

### Previous contrast candidate, upgrade attempt

The `d63901b` candidate in `artifacts/releases/candidate-0.4.0-native-qa` supplied
the recovery contrast evidence and the installer attempt above. It lacks the
separated data paths. Its setup must not be used to retest the data correction.

| Previous file | SHA-256 |
| --- | --- |
| LabelForge-win-Setup.exe | `e2d9b42540e22649e4b439877b302e17df3b1d369c8eb950dad8b5bcffc50fc5` |
| LabelForge-win-Portable.zip | `665d4865da4a42ec703fe9a966d9ea7449eb440292dfd76ec2547f2ef3097c59` |
| LabelForge-0.4.0-full.nupkg | `60072b81eaae0ad38d72a448b761b39895a628e6c671d2bad6bec99355afed92` |

The earlier `450b1df` candidate in `artifacts/releases/candidate-0.4.0-notices`
is retained for the original workflow evidence. It does not contain the contrast fix.

| Earlier file | SHA-256 |
| --- | --- |
| LabelForge-win-Setup.exe | `99183056138f0095d814830db8fde36df5b86745ad0ad79e76bb1ee472e94c2f` |
| LabelForge-win-Portable.zip | `db3573a5ecb4a0dda81339a8a4c5f6bdae77efa9304f37e089acefdbe6d68ffc` |
| LabelForge-0.4.0-full.nupkg | `b137aa7747e4c36a221ed58076285246f74b840f514f6878c64390745c567a61` |

Velopack refuses to repack a version already present in its output directory.
Use `-OutputDirectory` with a fresh folder for another local candidate. Preserve
earlier packages and never replace packages already distributed to users.

## Remaining work

1. Measure actual app scaling and finish the native workflow matrix (G8).
2. Validate legacy upgrade, pristine clean install and uninstall;
   complete the dependency notice review (G11).
3. Publish after those checks have evidence; choose a distribution/update feed
   as a separate release decision.
