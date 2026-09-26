# Development guide

## Build and run

Use the .NET 10 SDK. Windows is the current development and CI target.
From the repository root:

```powershell
dotnet restore LabelForge.sln
dotnet build LabelForge.sln --configuration Release --no-restore
dotnet test LabelForge.sln --configuration Release --no-build --verbosity minimal
dotnet run --project src/LabelForge.App
```

The solution contains Core, App and unit tests. The E2E harness and benchmark
tool live outside the solution and need separate builds.

## Code map

| Path | Responsibility |
| --- | --- |
| `src/LabelForge.Core/Model` | Documents, bounds, font metrics and element resizing |
| `src/LabelForge.Core/Editing` | Snapping, alignment, selection, spacing, z-order and undo |
| `src/LabelForge.Core/Zpl` | Our generator and ZPL readers/scanners |
| `src/LabelForge.Core/Rendering` | Renderer interface, offline adapter and pinned font |
| `src/LabelForge.Core/Io`, `Imaging`, `Export` | Storage, ZPL import/encoding, graphics and exports |
| `src/LabelForge.Core/Templating`, `Fields`, `Starters` | Markers, catalogs and density-aware starters |
| `src/LabelForge.Core/Printers`, `Printing`, `Media` | Printer/stock definitions, jobs and transports |
| `src/LabelForge.App/ViewModels` | Commands and observable editor state |
| `src/LabelForge.App/Views`, `Controls` | Layout, focus, canvas input and rendering |
| `src/LabelForge.App/Services` | Settings, recovery, clipboard and file association |
| `tests/LabelForge.Tests` | Unit tests, golden output, serialization and corpus fixtures |
| `tools/LabelForge.E2E` | Headless UI assertions and screenshots |
| `tools/LabelForge.Bench` | Rendering measurements |
| `scripts` | Packaging, catalog export and driver fixture capture |

App dependencies include Avalonia 12.1.0, AvaloniaEdit 12.0.0, CommunityToolkit.Mvvm
8.4.2 and Velopack 1.2.0. Core uses BinaryKits.Zpl.Viewer 1.3.1. Project files are
the authority for package versions; planning-era versions are historical.

## Boundaries to preserve

- Core owns document/editing math without Avalonia dependencies. The canvas maps input
  to Core operations. Menus, shortcuts and context menus share commands.
- Use CommunityToolkit observable-property/command generators and typed compiled
  bindings. View/VM names stay paired for `ViewLocator`. Pass collaborators through
  constructors without a DI container.
- Element coordinates use printer dots; document dimensions use millimeters; UI
  measurements default to centimeters. Unit switches and display normalization
  must not change the label or add undo steps.
- Layout, search, creation mode and overlays are view/session state. Element/print
  settings and document guides belong to the document.
- Cancelling a gesture restores its captured start. A press or return to the start
  creates no change. Text blocks restore font dimensions and wrapping width.
  Text edge handles apply size limits only to the resized dimension; they preserve
  untouched imported values even outside the inspector's editing range.
- `FontWidthDots = 0` omits width from ZPL; the printer chooses its default and the
  preview estimates it. New fields/starters specify width; old saved values stay intact.
  Font 0's pinned preview typeface remains a substitute.
- Preview uses our generator and `IZplRenderer`, off the UI thread, with one active
  render and the latest waiting request. Keep network rendering out of the default path.
- Marker parsing uses the shared scanner and configurable delimiters. Preserve marker
  contents through import/encoding/export. Output is UTF-8 without a BOM.
- Printing and print-job export share a builder. TCP readback is a status snapshot;
  successful byte delivery is not confirmation that a physical label printed.
- Harness stores must be injectable and use scratch paths.

## UI regressions

Build the harness separately and run the relevant scenarios:

```powershell
dotnet build tools/LabelForge.E2E --configuration Release
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- ui-layout
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- recovery-ui
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- transform-gestures
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- cm-units
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- viewer-size
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- viewer-layout
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- viewer-compare
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- designer synthetic
dotnet run --project tools/LabelForge.E2E --no-build --configuration Release -- designer dark synthetic
```

CI runs eight UI jobs alongside build-and-test. The recovery contrast checks are
included in ui-layout; recovery-ui runs just those six checks. The workflow runs on pushes
to `main` and PRs targeting `main`; failed UI logs/screenshots are kept for seven days.
Focused clipboard, selection, spacing, printer-status and canvas modes also exist;
see `Program.cs`.

Check the process exit code and graded summary. A leftover PNG is not evidence of a
pass. Only assertions routed through grading helpers affect the summary. Compare
changed behavior against its preceding transcript, normalize volatile paths/timestamps
only and inspect screenshots for layout changes. Bug regressions must fail on the old
behavior before the fix. Prefer checks that establish a contract over tests that repeat
implementation details.

Unit tests always use committed synthetic fixtures; an optional private corpus adds
local cases, so CI/local counts differ. Designer `synthetic` mode forces the public
fixture. Comparison checks inject the offline engine; tests do not require Labelary.

Native DPI evidence must record the app's actual RenderScaling alongside the
Windows scale selection and window dimensions. A Windows setting or a headless
layout pass alone does not establish coverage at 125% or 150%. Record which
workflow ran in each combination rather than applying one session to the matrix.

## Git and documentation

Validated completed improvements go directly to `main` under the maintainer's current
workflow. Use a branch or PR when requested or required by protection. Review the diff,
run appropriate checks, commit with a plain descriptive message, push and inspect CI.
Do not include generated-attribution trailers or unrelated working-tree changes.

The [documentation index](README.md) maps current public records. Local planning
files stay ignored; do not force-add them. Customer labels, private screenshots and
machine-specific context stay local. Shared fixtures, screenshots and external
renderer comparisons use synthetic labels.

Docs-only changes need content/link checks and diff review. Run the designer locally
when behavior changes. Status must distinguish inspected CI evidence from local runs.

## Windows packaging

User data lives in `%LocalAppData%/LabelForge.UserData`, outside the Velopack
installation directory `%LocalAppData%/LabelForge`. Normal startup migrates known
legacy JSON files and abandoned recovery snapshots without replacing newer data.
Live recovery sessions are left alone; close all instances before upgrading.

Before upgrading a build that stores data inside the installation directory,
back up that directory's user JSON files and recovery folder. Run the corrected
portable app once, or run its migration-only command before running Setup:

```powershell
& '<portable-folder>/current/LabelForge.App.exe' --migrate-user-data
if ($LASTEXITCODE -ne 0) { throw 'Resolve the migration errors before installing.' }
```

The command exits without opening a window. Verify that the expected user files
are present under `LabelForge.UserData`. An incoming installer cannot retroactively
protect data erased by an older executable; migration must precede that installer.
Future installs/reinstalls use the separated data directory.

The script uses a self-contained win-x64 publish with trimming disabled. Install
the Velopack CLI once if absent, then pass the intended release version:

```powershell
dotnet tool install --global vpk
powershell -ExecutionPolicy Bypass -File scripts/pack-windows.ps1 -Version x.y.z
```

Replace `x.y.z` with the chosen version. Output goes to `artifacts/publish/win-x64`
and `artifacts/releases`. Omitting `-Version` uses the app project's version, currently
`0.4.0`. An override is applied to both the app publish and package. The publish includes
the existing project, dependency and font notices. Their full review remains a release check.

Velopack rejects versions already present in its output directory. To rebuild a local
candidate, use a fresh folder with `-OutputDirectory artifacts/releases/candidate-name`.
Relative output paths resolve from the repository root; previous packages are preserved.
Record the source commit and checksums in [release validation](RELEASE-VALIDATION.md).
Building packages does not validate
installation/upgrade or configure an update feed. Follow the [roadmap](ROADMAP.md)
before describing a build as a distributed release.
