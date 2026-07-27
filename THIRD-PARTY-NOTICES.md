# Third-Party Notices

LabelForge bundles the following third-party components. This file records their licenses.
It must be reviewed and completed before any public release.

## Runtime dependencies

- Avalonia (UI framework). License: MIT.
- CommunityToolkit.Mvvm. License: MIT.
- BinaryKits.Zpl.Viewer and BinaryKits.Zpl.Label (offline ZPL rendering and elements). License: MIT.
- SkiaSharp and HarfBuzzSharp (2D graphics and text shaping). License: MIT.
- ZXing.Net (barcode encoding). License: Apache-2.0.

## Bundled font

- Roboto Condensed Regular, bundled as
  `src/LabelForge.Core/Rendering/Fonts/RobotoCondensed-Regular.ttf` and embedded in
  LabelForge.Core. License: SIL Open Font License 1.1, copied verbatim beside the font as
  `Rendering/Fonts/OFL.txt`. Copyright 2011 The Roboto Project Authors.

  Why it is bundled: the offline preview has to draw ZPL's scalable font 0 with something,
  because font 0 is a file resident in the printer rather than on a PC. Left to the machine,
  the renderer substituted whatever happened to be installed, and two machines drew the same
  label 22 per cent apart. Every text footprint in the designer is measured from that render,
  so the typeface is pinned and shipped. See `Rendering/PreviewFont` for the measurement that
  chose this one over the alternatives.

  OFL 1.1 permits bundling and redistribution with software, including commercially. The two
  conditions that matter here are both met: the font is not sold on its own, and the license
  travels with it. The Reserved Font Name clause applies only to modified versions; this file
  is unmodified upstream, and it must stay that way or it has to be renamed.

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
