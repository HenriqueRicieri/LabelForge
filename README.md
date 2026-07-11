# LabelForge

LabelForge is a desktop application for designing Zebra labels visually and generating ZPL (Zebra
Programming Language) code. It also works the other way around: a live viewer where you edit ZPL and
watch the rendered label update in real time, fully offline. No cloud service, no Labelary
dependency.

## Status

Working application under active development. The rendering stack is proven against a corpus of real
labels, and the designer, viewer, printing, and export paths are implemented and tested.

## What works today

- Visual designer: a canvas with click-to-place elements, drag, eight-handle resize, continuous
  rotation snapping to the four ZPL orientations, multi-select with marquee, copy/paste/duplicate,
  z-order, arrow-key nudge, and zoom/pan.
- Snapshot-based undo/redo that shares the save format, so a document that undoes correctly is
  guaranteed to save and reopen correctly. Related edits coalesce into one step by identity.
- Element types: text, linear barcode (Code 128, Code 39, EAN-13, UPC-A), QR code, line, box, with a
  per-type properties panel. Barcode data is validated against its symbology with a clear warning
  when it cannot be encoded.
- Live offline preview driven by our own ZPL generator through a swappable renderer, debounced and
  rendered off the UI thread.
- ZPL viewer: an editable ZPL pane with syntax highlighting, a live preview, auto-sizing from
  `^PW`/`^LL`, a selector for files with multiple `^XA` blocks, and a diagnostics strip for
  unsupported commands and engine errors. It tolerates non-ZPL template markers and comment lines.
- Printer profiles (203/300/600 dpi Zebra models) with design-time head-width and density warnings.
- Save and open the native `.lfl` project format; export ZPL, PNG, and PDF at exact physical size.
- Printing over the network (TCP 9100) and through the Windows spooler (RAW datatype, the USB path).
- Light and dark themes, a custom app icon, and Windows packaging via Velopack.

## Not yet built

- Importing an existing ZPL label back into the visual designer. Real labels can be rendered in the
  viewer, but the designer edits its own model of the five element types above; embedded graphics
  (`~DG`/`^XG`), image elements, and full ZPL-to-model import are planned, not present.
- A template and variable-data system (named variables, sample data, print regions). The viewer
  substitutes sample values for preview today; a real templating layer is future work.
- User-facing string localization. The app ships English only; strings are currently inline and will
  be extracted to resource files later.

## Tech stack

- C# on .NET 10.
- Avalonia UI 12 for a cross-platform desktop shell (Windows first), MVVM via CommunityToolkit.Mvvm.
- AvaloniaEdit with a custom ZPL syntax highlighter for the viewer.
- BinaryKits.Zpl.Viewer for offline ZPL rendering, behind a swappable `IZplRenderer` interface.
- SkiaSharp 3 for rendering and PDF export.

The label document model and the ZPL generator are our own code. Offline rendering is reused behind
an interface so it can be fixed, forked, or replaced without touching the rest of the app.

## Build and run

```
dotnet build
dotnet run --project src/LabelForge.App
dotnet test
```

## Testing

The suite covers unit behavior (unit conversion, `^FH` escaping, check digits, barcode validation),
golden ZPL generation, JSON round-trips, printer validation, and a corpus smoke test that renders a
committed set of synthetic ZPL fixtures (and, when present, a local private corpus of real labels)
without crashing. A headless Avalonia harness under `tools/LabelForge.E2E` drives the designer end to
end and captures a screenshot. CI runs build and test on every push and pull request.

## License

Released under the MIT License. See LICENSE for the full text, and THIRD-PARTY-NOTICES.md for the
licenses of bundled dependencies.
