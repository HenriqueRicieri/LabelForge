# Windows release candidate validation

Updated 2026-09-26. Candidate version: `0.4.0`. Native workflow evidence was
collected on `450b1df`; the recovery contrast fix and replacement candidate use
[`d63901b`](https://github.com/HenriqueRicieri/LabelForge/commit/d63901b773c57d04fbbca2c749f8a6b5ec8f5012).
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
| Portable launch | Passed for both recorded candidates |
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

## Software and packaging evidence

| Check | Result |
| --- | --- |
| Release solution build | Passed, zero warnings/errors on `d63901b` |
| Local unit tests | 1,326 passed, including the optional private corpus |
| Full workspace layout | 400 checks passed, including six recovery checks |
| Separate E2E build | Passed; Windows association OS guard removed 11 CA1416 warnings |
| Paint allocation budget | Unchanged at 70 KB; four earlier focused runs passed 30 checks each, median/p95/max 55,960 bytes at 1x |
| Historical allocation failure | Still unexplained; local passes do not close G10 |
| Fresh CI | [Commit-matched run](https://github.com/HenriqueRicieri/LabelForge/actions/runs/36215947181) passed all nine jobs: 1,297 tests, 569 designer checks per theme, 400 layout, 33 transform, 29 cm, viewer 15/36/15 |
| Package integrity | ZIP/nupkg CRC checks passed; app exe/dll bytes matched publish; app and package declare 0.4.0 |
| Bundled notices | LICENSE, THIRD-PARTY-NOTICES.md and font OFL.txt byte-matched to source; full dependency notice review remains open |
| Install/upgrade/association/uninstall | Not executed; existing installed 0.2.1 was preserved |

Raw screenshots, synthetic files, export comparisons, matrix entries and logs
remain in ignored local artifacts. Public records omit machine paths and user state.

## Current candidate files

Built from `d63901b773c57d04fbbca2c749f8a6b5ec8f5012` into
`artifacts/releases/candidate-0.4.0-native-qa`. App informational version is
`0.4.0+d63901b773c57d04fbbca2c749f8a6b5ec8f5012`.

| File | SHA-256 |
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
2. Compare a failing allocation sample with passing diagnostics (G10).
3. Validate upgrade, clean install, Windows `.lfl` association and uninstall;
   complete the dependency notice review (G11).
4. Publish after those checks have evidence; choose a distribution/update feed
   as a separate release decision.
