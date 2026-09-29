# Third-Party Notices

Reviewed 2026-09-26 against the self-contained Windows x64 publish. LabelForge
redistributes the components listed below. Their original copyright, license and
vendor notice texts travel in the `licenses` directory beside the application.
[The inventory](licenses/manifest.json) records exact package versions, source
URLs and SHA-256 hashes for those texts.

`scripts/check-third-party-notices.ps1` compares that inventory with the published
`LabelForge.App.deps.json` and checks copied notice bytes. Packaging runs this check
before creating the installer; dependency/version changes require another review.
The native helpers added by Velopack have a separate inventory described below.
Their exact Rust standard-library revision remains a release review item.

## Native Windows installer helpers

The packer is pinned to Velopack 1.2.0, source commit
`f2edcbcafb81da5b3c884aaea330e225ad91d8b6`. Its default Setup, launcher stub and
Update.exe are x86 helpers, including when the application targets Windows x64.
[The native inventory](licenses/native-manifest.json) records the packer/helper
input hashes, the locked source graph and the origins of 493 original text segments
in [Velopack-NATIVE-NOTICES.txt](licenses/Velopack-NATIVE-NOTICES.txt).

The 277 entries form a conservative Windows normal/build dependency closure,
including build tools, procedural macros and workspace feature unification.
This is not a claim that every entry is linked into every executable. Original
license alternatives remain in the bundle; their presence does not change
LabelForge's license. `fs_at` and `simd_helpers` do not supply separate license
files in their crates or pinned repositories. Their original author/license
metadata is retained alongside the standard Apache/MIT texts, without inventing
an upstream copyright statement.

The x86 static WebView2 loader in `webview2-com-sys` 0.39.1 matches Microsoft's
WebView2 SDK 1.0.3800.47 byte-for-byte. Its original
[license](licenses/WebView2Loader-LICENSE.txt) and
[notice](licenses/WebView2Loader-NOTICE.txt) are included. LabelForge does not
bundle the WebView2 browser runtime.

[Rust library notices](licenses/Rust-COPYRIGHT-library.html),
[MIT](licenses/Rust-LICENSE-MIT.txt) and [Apache](licenses/Rust-LICENSE-APACHE.txt)
come from the official 2026-06-03 nightly distribution, the Velopack release date.
This is supplemental coverage, not proof of the compiler used for the prebuilt
helpers. The [upstream Windows build workflow at the reviewed commit](https://github.com/velopack/velopack/blob/f2edcbcafb81da5b3c884aaea330e225ad91d8b6/.github/workflows/build-rust.yml)
uses an unpinned `nightly-x86_64-pc-windows-msvc` toolchain and `build-std` for
the x86 helpers. Its [successful 2026-06-03 Windows job](https://github.com/velopack/velopack/actions/runs/26919411102/job/79416485334)
has an expired HTTP 410 log; the build artifacts had one-day retention.
The published vpk 1.2.0 assets do not include helper PDBs. The pinned binaries
contain nightly `rust-src` paths but no compiler commit/date string found in
their printable bytes. Establishing the actual compiler/library revision and
reconciling its notices remains open. A rebuild with a pinned toolchain or a
verifiable upstream build record would close it. The Rust 1.98.1 tools used for
metadata analysis are unrelated to that original compilation.

`scripts/check-velopack-notices.ps1` rejects changes to the pinned packer or any
of the three helper inputs. Packaging calls the verified packer entry point
directly. The runtime notice check also verifies every original native text
segment and the supplemental notice files before packing.

## Runtime packages

The license column describes each package's own license. Vendor notice files also
retain terms for code incorporated by Avalonia, Skia/HarfBuzz, CommunityToolkit and
.NET. In particular, MIT licenses for the graphics wrappers do not replace the
notices for their native libraries.

| Component | Version | License | Included texts |
| --- | --- | --- | --- |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.1 | MIT | [DotNet-LICENSE.txt](licenses/DotNet-LICENSE.txt), [DotNet-NOTICES.txt](licenses/DotNet-NOTICES.txt) |
| Avalonia | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause | [ANGLE-LICENSE.txt](licenses/ANGLE-LICENSE.txt) |
| Avalonia.AvaloniaEdit | 12.0.0 | MIT | [AvaloniaEdit-LICENSE.txt](licenses/AvaloniaEdit-LICENSE.txt) |
| Avalonia.Desktop | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Fonts.Inter | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt), [Inter-OFL.txt](licenses/Inter-OFL.txt) |
| Avalonia.FreeDesktop | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.FreeDesktop.AtSpi | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.HarfBuzz | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Native | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Remote.Protocol | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Skia | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Themes.Fluent | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.Win32 | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| Avalonia.X11 | 12.1.0 | MIT | [Avalonia-LICENSE.txt](licenses/Avalonia-LICENSE.txt), [Avalonia-NOTICE.txt](licenses/Avalonia-NOTICE.txt) |
| BinaryKits.Zpl.Label | 3.3.1 | MIT | [BinaryKits-LICENSE.txt](licenses/BinaryKits-LICENSE.txt) |
| BinaryKits.Zpl.Viewer | 1.3.1 | MIT | [BinaryKits-LICENSE.txt](licenses/BinaryKits-LICENSE.txt) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [CommunityToolkit-LICENSE.txt](licenses/CommunityToolkit-LICENSE.txt), [CommunityToolkit-NOTICES.txt](licenses/CommunityToolkit-NOTICES.txt) |
| HarfBuzzSharp | 8.3.1.3 | MIT | [HarfBuzzSharp-LICENSE.txt](licenses/HarfBuzzSharp-LICENSE.txt) |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | [HarfBuzzSharp-LICENSE.txt](licenses/HarfBuzzSharp-LICENSE.txt), [HarfBuzzSharp-NOTICES.txt](licenses/HarfBuzzSharp-NOTICES.txt) |
| MicroCom.Runtime | 0.11.6 | MIT | [MicroCom-LICENSE.txt](licenses/MicroCom-LICENSE.txt) |
| SixLabors.ImageSharp | 3.1.12 | Apache-2.0 | [Apache-2.0.txt](licenses/Apache-2.0.txt), [ImageSharp-UPSTREAM-LICENSE.txt](licenses/ImageSharp-UPSTREAM-LICENSE.txt) |
| SkiaSharp | 3.119.4 | MIT | [SkiaSharp-LICENSE.txt](licenses/SkiaSharp-LICENSE.txt) |
| SkiaSharp.HarfBuzz | 3.119.1 | MIT | [SkiaSharp-LICENSE.txt](licenses/SkiaSharp-LICENSE.txt) |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | [SkiaSharp-LICENSE.txt](licenses/SkiaSharp-LICENSE.txt), [SkiaSharp-NOTICES.txt](licenses/SkiaSharp-NOTICES.txt) |
| Tmds.DBus.Protocol | 0.94.1 | MIT | [Tmds.DBus-LICENSE.txt](licenses/Tmds.DBus-LICENSE.txt) |
| Velopack | 1.2.0 | MIT | [Velopack-LICENSE.txt](licenses/Velopack-LICENSE.txt) |
| ZXing.Net | 0.16.11 | Apache-2.0 | [ZXing-LICENSE.txt](licenses/ZXing-LICENSE.txt), [ZXing-COPYRIGHT.txt](licenses/ZXing-COPYRIGHT.txt) |

## Bundled fonts

- Roboto Condensed Regular supplies the offline preview substitute for ZPL font 0.
  The unmodified font is embedded in LabelForge.Core. Copyright 2011 The Roboto
  Project Authors; [SIL Open Font License 1.1](licenses/RobotoCondensed-OFL.txt).
  The same license is retained beside the source font and as `OFL.txt` in the publish.
- Inter 3.019 is embedded by Avalonia.Fonts.Inter. Its font metadata identifies
  Copyright 2020 The Inter Project Authors. The upstream [OFL 1.1 text](licenses/Inter-OFL.txt)
  includes the 2016-2020 copyright notice and Inter name information.

## ImageSharp 3.1.12

LabelForge redistributes this version under Apache-2.0. It is pulled transitively
by BinaryKits, and LabelForge is MIT-licensed. Both match eligibility criteria in
[the version's upstream terms](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE).
The [Apache-2.0 text](licenses/Apache-2.0.txt) and unchanged upstream eligibility
terms are included. Re-review any version, dependency-graph or distribution change;
this record does not assess a different ImageSharp major version.

## Packaging review still open

Velopack 1.2.0's managed library and its MIT text are covered above. Its separate
Windows Setup, stub and Update.exe contain Rust dependencies. The upstream
[Windows binary declarations](https://github.com/velopack/velopack/blob/1.2.0/src/bins/Cargo.toml)
and [lockfile](https://github.com/velopack/velopack/blob/1.2.0/Cargo.lock) need a
binary-specific notice review before public release. Build tools such as signtool,
WiX, zstd.exe and Linux AppImage helpers are not application runtime packages;
review their terms separately if they are ever redistributed.
