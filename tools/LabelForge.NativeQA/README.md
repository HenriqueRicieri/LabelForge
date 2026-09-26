# Native Windows QA

This tool opens the production editor on Avalonia's native desktop backend. It
uses the production styles, window, view models, rendering and Windows manifest.
A separate panel exposes `TopLevel.RenderScaling`, monitor scaling and the actual
client size in DIPs. It can select Light/Dark, request regular/compact sizes and
record the current state.

Build with `dotnet build tools/LabelForge.NativeQA -c Release`, then open
`bin/Release/net10.0/LabelForge.NativeQA.exe` through the normal desktop. No installer
or file-association hook runs. Media, catalogs, preferences, recovery and recent
files use a new timestamped `native-qa` directory beside the tool. Clipboard
operations use the real OS clipboard when explicitly exercised.

Each record contains platform, source version, time, render/monitor scaling,
client size, theme and element/selection counts. Matching `.lfl` and `.zpl` files
capture model and generated output. Record again after layout/rendering settles.
Keep native screenshots and accessibility observations with the record they verify.
These files belong in ignored QA artifacts, not in a public release.

Use a synthetic label and normal editor controls for edits, focus, save/reopen and
exports. Record actual scaling after each OS change, and restore the original OS
scale afterward. A layout capture does not establish every editing workflow at
that scale/theme/window combination. Canvas zoom is independent of display DPI.

`dotnet run --project tools/LabelForge.NativeQA -c Release -- --self-check` runs
15 bootstrap/storage checks on the headless backend, including production styles,
scratch recent-file persistence/removal and preservation of the user's recent file.
Its records are explicitly marked `headless-self-check`. It does not validate
native DPI or OS pointer/focus behavior. CI runs this check in build-and-test.
