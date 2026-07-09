# Third-Party Notices

LabelForge bundles the following third-party components. This file records their licenses.
It must be reviewed and completed before any public release.

## Runtime dependencies

- Avalonia (UI framework). License: MIT.
- CommunityToolkit.Mvvm. License: MIT.
- BinaryKits.Zpl.Viewer and BinaryKits.Zpl.Label (offline ZPL rendering and elements). License: MIT.
- SkiaSharp and HarfBuzzSharp (2D graphics and text shaping). License: MIT.
- ZXing.Net (barcode encoding). License: Apache-2.0.

## Needs a decision before public release

- SixLabors.ImageSharp (pulled transitively by BinaryKits.Zpl.Label; confirmed on the runtime path).
  ImageSharp 3.x uses the Six Labors Split License: free for open source, or for organizations under a
  revenue threshold, otherwise a paid commercial license is required. Action: confirm eligibility, or
  fork BinaryKits to remove the ImageSharp dependency (it is used only for image conversion and can be
  replaced with SkiaSharp).

## Notes

- Labelary API: used only as an optional online compare mode, never bundled and never required for
  core functionality. Its public API is free for commercial use with usage limits.
