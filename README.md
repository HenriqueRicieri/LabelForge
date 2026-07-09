# LabelForge

LabelForge is a desktop application for designing Zebra labels visually and generating ZPL (Zebra
Programming Language) code. It also works the other way around: a live viewer where you edit ZPL and
watch the rendered label update in real time, fully offline.

## Features (planned)

- Visual label designer with a canvas, editing tools, and a properties panel.
- ZPL code generation from the design, and a live offline preview.
- Labelary-style viewer: paste or edit ZPL and see it render instantly, with a diagnostics strip for
  unsupported commands.
- Support for a range of Zebra printers across 203, 300, and 600 dpi.
- Export to ZPL and PNG, and printing over the network and the Windows spooler.

## Status

Early development. The rendering stack is proven against a corpus of real labels. See docs/PLAN.md for
the implementation plan and milestones.

## Tech Stack

- C# on .NET 10.
- Avalonia UI for a cross-platform desktop shell (Windows first).
- BinaryKits.Zpl.Viewer for offline ZPL rendering, behind a swappable interface.

## Build and Run

```
dotnet build
dotnet run --project src/LabelForge.App
dotnet test
```

## License

To be determined before public release. See THIRD-PARTY-NOTICES.md for bundled dependencies.
