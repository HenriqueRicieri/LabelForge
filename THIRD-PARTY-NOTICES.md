# Third-Party Notices

LabelForge bundles the following third-party components. This file records their licenses.
It must be reviewed and completed before any public release.

## Runtime dependencies

- Avalonia (UI framework). License: MIT.
- CommunityToolkit.Mvvm. License: MIT.
- BinaryKits.Zpl.Viewer and BinaryKits.Zpl.Label (offline ZPL rendering and elements). License: MIT.
- SkiaSharp and HarfBuzzSharp (2D graphics and text shaping). License: MIT.
- ZXing.Net (barcode encoding). License: Apache-2.0.

## SixLabors.ImageSharp license position

SixLabors.ImageSharp 3.1.12 is on the runtime path, pulled transitively by
BinaryKits.Zpl.Label and BinaryKits.Zpl.Viewer. LabelForge does not reference it directly.

ImageSharp 3.x uses the Six Labors Split License 1.0. That license grants Apache-2.0
(free) terms to, among others:

- open source or source-available projects (LabelForge is MIT),
- transitive or indirect dependencies installed by third parties (how BinaryKits pulls
  ImageSharp here),
- for-profit entities under 1M USD annual gross revenue, and non-profits or charities.

A paid commercial license is required only when all of the following hold: ImageSharp is a
direct package dependency, in closed-source for-profit software, from an organization over
1M USD annual gross revenue.

Assessment: because ImageSharp is a transitive dependency here, and because this repository
is open source, LabelForge falls under the free-use terms. Keep it transitive: do not add a
direct SixLabors.ImageSharp package reference. If a closed-source commercial distribution by
an organization over the revenue threshold is ever planned, either confirm eligibility with
Six Labors in writing or fork BinaryKits to drop ImageSharp (used only for image conversion,
replaceable with the SkiaSharp already in the dependency tree). This is a licensing note,
not legal advice.

## Notes

- Labelary API: used only as an optional online compare mode, never bundled and never required for
  core functionality. Its public API is free for commercial use with usage limits.
