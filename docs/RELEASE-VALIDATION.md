# Windows release candidate validation

Candidate version: `0.4.0`. Work started after documentation commit `0a9ab91`.
Real-printer validation is excluded from this batch at the maintainer's request
because hardware is currently unaffordable. It does not block release. Physical
output has not been validated; synthetic software tests retain their own coverage.

## Desktop automation diagnostic

The native matrix did not run. The automation JavaScript kernel failed before
opening LabelForge with `CreateProcessWithLogonW failed: 1056`; later retries timed
out even on a one-line runtime probe. Resetting the session did not recover it.
The installed Node executable ran normally through the shell, and Secondary Logon
was running. These observations isolate the failure to the automation launch path;
they do not establish its underlying cause or a confirmed repair.

No sandbox/security configuration was weakened to work around the failure. Native
editing at 100/125/150% scaling remains a required unexecuted check. Clean install,
upgrade and uninstall remain unexecuted too. A generated installer is not evidence
that these checks passed.

## Software and packaging evidence

| Check | Result |
| --- | --- |
| Separate E2E Release build | Passed, zero warnings/errors after Windows OS guard |
| Existing 70 KB paint allocation budget | Retained; diagnostics include viewport, theme, overlays, median/p95/max |
| Initial focused dark paint run | 30 checks passed; 1x allocation median 55,960 bytes |
| Repeated light/dark paint runs | Four passed, two per theme; 30 checks each; median/p95/max 55,960 bytes at 1x |
| Release solution and unit tests | Build: zero warnings/errors; 1,326 local tests passed |
| Candidate installer and portable archive | Generated from `450b1df`; app/package version 0.4.0; archive integrity and app bytes checked |
| Bundled notices | LICENSE, THIRD-PARTY-NOTICES.md and font OFL.txt included and byte-matched to source; full dependency notice review remains open |
| Fresh CI | Check the commit-matched result in [CI runs](https://github.com/HenriqueRicieri/LabelForge/actions/workflows/ci.yml) after publication |
| Native editing/scaling | Not executed: automation runtime launch failed |
| Install/upgrade/uninstall | Not executed |
| Physical printing/scanning | Excluded by maintainer; unverified |

## Candidate files

The recorded candidate was built from
[`450b1df`](https://github.com/HenriqueRicieri/LabelForge/commit/450b1df3f38c67de0dd6be2d3a904ab42f39d4a0)
into `artifacts/releases/candidate-0.4.0-notices`. App informational versions include
that commit; the Velopack package declares `0.4.0`. These are local unsigned artifacts,
not a published release. No update feed or upgrade from an older installation was tested.

| File | SHA-256 |
| --- | --- |
| LabelForge-win-Setup.exe | `99183056138f0095d814830db8fde36df5b86745ad0ad79e76bb1ee472e94c2f` |
| LabelForge-win-Portable.zip | `db3573a5ecb4a0dda81339a8a4c5f6bdae77efa9304f37e089acefdbe6d68ffc` |
| LabelForge-0.4.0-full.nupkg | `b137aa7747e4c36a221ed58076285246f74b840f514f6878c64390745c567a61` |

Velopack refuses to repack a version already present in the output directory. Use
`-OutputDirectory` with a fresh folder for another candidate of the same version;
do not overwrite packages distributed to users. An invalid version is rejected
before publishing.

## Remaining work

Recover a working native automation session and execute the desktop and installer
matrix. Keep allocation investigation open until failing measurements can be compared
with passing samples; repeated local passes do not explain the historical CI failure.
Publish a release only after the remaining release checks have evidence.
