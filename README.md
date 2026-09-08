# LabelForge

LabelForge is a desktop application for designing Zebra labels visually and generating ZPL (Zebra
Programming Language) code. It also works the other way around: a live viewer where you edit ZPL and
watch the rendered label update in real time, fully offline. No cloud service, no Labelary
dependency.

## Status

Working application under active development. The rendering stack is proven against a corpus of real
labels, and the designer, viewer, printing, and export paths are implemented and tested.

## What works today

- Visual designer: an icon tool bar with click-to-place elements, drag, eight-handle resize,
  continuous rotation snapping to the four ZPL orientations, multi-select with marquee,
  copy/paste/duplicate, z-order, arrow-key nudge, and zoom/pan with scrollbars and a floating zoom
  control (50/100/200% presets, fit; 100% shows real printer dots). Ctrl and the plus or minus key
  zoom about the middle of the view, which is the half the wheel does not do, and holding Space
  and dragging pans for a mouse with no middle button.
- Canvas gestures answer to the modifiers the tools people already know use. A press does not
  become a drag until the pointer has travelled four screen pixels, so an unsteady click stays a
  click instead of shifting an element by a few dots and recording an undo step for the accident.
  Shift locks a move to one axis, Ctrl leaves a copy behind and moves the copy, and Alt drags free
  of every kind of snapping. On a resize handle Ctrl works from the middle outwards and Shift lets
  a corner's two sides go their own way. Alt-clicking picks the element under the one selected and
  keeps going down, which on a dense label is the only way to reach a field buried under two
  others. Adding to the selection happens as the button goes down, because a drag that follows
  should move everything picked; removing one waits for a release that never moved, since pressing
  on something already selected is the ordinary way to start dragging the whole group.
- Escape puts a gesture back. Mid-drag, mid-resize or mid-rotate the elements return to the state
  they were in when the gesture began and nothing is recorded, so a drag that went wrong costs
  nothing and does not need an undo afterwards. It restores from a copy taken at the start rather
  than by arithmetic, because a resize quantizes: a barcode dragged from three module widths to
  four does not come back to three by subtracting what was added.
- Dragging to the edge of the view brings the rest of the label into it. A drag used to stop where
  the viewport did, so anything further out meant letting go, panning, and picking the element up
  again. Now the view scrolls once the pointer comes within a couple of dozen pixels of an edge and
  the element keeps following it: the pointer is standing still and the label is moving underneath.
  A rotation deliberately does not do this, since turning something does not take it anywhere.
- The canvas says what is under the pointer before you commit to it: a thin outline on whatever a
  click would take, and in the corner opposite the zoom control, the pointer position in
  millimeters and dots beside the drawn width and height of the selection. Those are the bounds the
  canvas outlines, which is the only answer defined for every element type, so they can read
  differently from the properties panel's X and Y for a barcode that prints digits outside its bars
  or for a field placed by its own baseline. Both numbers are right, and they are answering
  different questions.
- Reaching an element without the mouse: Tab steps down the stacking order and Shift + Tab back up,
  both wrapping, which is the point on a label carrying sixty fields. Ctrl + A takes everything
  visible. Double-clicking an element puts the caret in its content field with the text selected,
  which is the cheap form of editing in place and the only one that keeps the rule that the canvas
  draws the renderer's own bitmap and never sets type itself.
- Bring Forward and Send Backward beside the existing Bring to Front and Send to Back, on Ctrl + ]
  and Ctrl + [, with Ctrl + Shift + Up and Down bound beside them for layouts where a bracket is
  not a key of its own. They swap with the neighbour they pass rather than assigning fresh numbers,
  so the set of z-order values in the document is the same set afterwards.
- Groups: Ctrl + G makes several elements behave as one, and Ctrl + Shift + G takes it back. A
  click selects the whole group, a drag moves it together, align and distribute treat it as one box
  that keeps its internal layout, and a single locked member holds all of it, because a group that
  half moves is not a group. Double-clicking opens a group and takes the member under the pointer,
  and Alt-clicking reaches one member without opening it. Grouping is saved with the label and
  never reaches the printer: a design generates the same bytes grouped or not.
- Cut, and paste back where it came from. Ctrl + X was missing outright and every paste cascaded
  from the last one, so there was no way to put a copy exactly where the original stood.
  Ctrl + Shift + V does that, and it deliberately does not clamp to the label, because an element
  parked on the pasteboard is outside it on purpose.
- Handles that do not promise what ZPL cannot do. The rotation handle appears only on the fields a
  printer will actually turn, which is text and the four symbol types: a box, an ellipse and an
  image carry no orientation in their commands and print identically at 0 and 90, so offering to
  turn them would be a claim the preview would then have to make good on. Ctrl + R
  is the quarter turn for the ones that do turn. The eight resize handles go while both sides of
  the selection are under about two dozen screen pixels, both sides, so a long thin line keeps the
  handles that are its only way to be resized.
- A drafting-table workspace: millimeter rulers pinned top and left (tick steps adapt to zoom, all
  conversion through the label density), and a pasteboard around the label where elements can be
  parked. Off-label content stays visible, dimmed, with an amber outline and a clear warning; at
  print time content crossing the edge is clipped like the printer would, and elements whose origin
  is off the label are skipped from the generated ZPL.
- Alignment guides: hold the mouse on a ruler for a transient guide with a live mm readout, double
  click (or right-click menu) for a permanent one. Guides drag to reposition, drop back on the
  ruler to delete, save with the document, and participate in undo.
- Snapping while dragging and resizing: to guides, label edges and center, and the edges and
  centers of other elements (smart guides), with a highlight on the matched line; Alt drags free.
  Each kind can be switched off on its own from the View menu, and the status line says which are
  on. The label's own edges and center are deliberately not on that list: they are the label
  itself, not something laid over it.
- Align and distribute tools: left/center/right and top/middle/bottom plus horizontal/vertical
  distribution with equalized gaps. What they align against is a switch rather than a rule about
  how many things happen to be selected: either the rest of the selection or the label itself, with
  each button's tooltip saying which is in force. Center on Label does both axes at once. Beside
  them, Same Width, Same Height and Same Size copy from the element picked last, resizing through
  the same clamps a handle drag obeys and saying how many elements could only land on a step, as a
  barcode does.
- An optional design grid (View > Grid) at 1, 2, 2.5, 5 or 10 mm. Drags and resizes snap to
  it alongside the guides, edges and other elements, by proximity rather than as a cage, and
  Alt still drags free of everything. Saved with the label and never printed.
- Zoom to selection (Ctrl+Shift+0, or from either right-click menu), which frames the
  element you just picked instead of leaving you to find it on a fitted view.
- Help > Keyboard and Mouse: every key and gesture the designer answers to, including the
  ones that were previously invisible, like Alt to drag without snapping and middle-drag
  to pan.
- Right-click menus on the canvas, different depending on what is under the pointer: on an
  element, copy, duplicate, delete, front and back, and the lock and do-not-print switches,
  plus align and distribute once several are selected; on bare stock, paste at the pointer,
  select all, and an insert submenu that places the new element where you clicked instead of
  asking for a second click. Right-clicking a ruler still offers guides.
- An Elements list of everything on the label, front to back, for picking one field out of
  a stack of overlapping ones on a dense design. Each row shows the element's own name when
  it has one and its type and content when it does not, and carries its visibility and lock,
  so a run of elements can be dealt with one after another.
- Snapshot-based undo/redo that shares the save format, so a document that undoes correctly is
  guaranteed to save and reopen correctly. Related edits coalesce into one step by identity.
- Element types: text, linear barcode (Code 128, Code 39, EAN-13, UPC-A, Interleaved 2 of 5 and so
  ITF-14), QR code, Data Matrix, PDF417, image, line, box, ellipse, diagonal line, with a per-type
  properties panel and positions and sizes typed in dots or millimeters. Barcode data is validated
  against its symbology with a clear warning when it cannot be encoded. A PDF417 states the shape
  its settings produce, and says so when the column count is left automatic, because that hands the
  shape to the printer.
- GS1-128 support: a payload written with `>;>8` (Code 128 subset C plus FNC1, which is how
  real labels write it) is shown broken into its application identifiers, as
  `(01)07891234567895 (3102)001234`, and structural problems are named. The one that matters
  is a variable-length value with nothing to terminate it, because that does not fail to
  scan: it swallows the fields after it and returns the wrong value from a barcode that
  looks perfect. Element footprints understand the subset escapes, so a GS1 barcode's
  outline matches its ink instead of being nearly twice too wide.
- Quiet zone checking: every symbology's standard asks for a blank margin around the symbol, and a
  barcode that takes a second pass to scan is a label that failed. The canvas draws that margin for
  the selected symbol and warns when a neighbour sits in it or when the symbol is flush with the
  edge of the stock. A design aid only, so it never changes the generated ZPL.
- Images (PNG, JPEG, BMP) are converted to the printer's 1-bit black with a selectable dither
  (threshold for logos, ordered, Floyd-Steinberg for photos) and embedded in the label as an
  inline `^GF` graphic field, so the saved file and the exported ZPL are self-contained. Place the
  same image twice and it is downloaded once as `~DG` and recalled with `^XG` instead of repeating
  the payload; a single placement stays inline and leaves the printer's memory alone.
- A ZPL file holding several labels stays open: a strip names the file and lists its
  labels with how much each one holds, so the bare printer-configuration block real files
  start with is visible as empty and the one you want is a click away. Files in the sample
  corpus hold up to twenty-seven.
- An existing ZPL label can be opened as a document: File > Import ZPL parses it back into the
  model so its text, barcodes, boxes and graphics become editable elements. ZPL states positions in
  dots and never names the printer it was written for, so the import uses the density selected in
  the designer. A file holding several labels imports the first one that has content, because real
  files routinely start with a bare printer-configuration block.
- Graphics alone can be lifted out of an existing label: Insert > Graphics from a ZPL file reads its
  `~DG` downloads and inline `^GF` fields, including the row compression Zebra drivers emit, and
  adds each one as a normal image element positioned where the source label drew it. Graphics the
  file only recalls by name are listed rather than skipped quietly, because those live in the
  printer's own memory and their bitmaps are genuinely not in the file.
- Template variables: `##MARKER##` placeholders in text and barcode data are discovered
  automatically and listed in a Variables panel. Each one is either filled at print by the
  downstream system (the preview renders an editable sample, and the exported and printed ZPL keeps
  the literal marker), a counter, or a date and time.
- Counters with a start, step, and zero padding. When the marker ends its field, the run is handed
  to the printer as `^SN` serialization plus `^PQ`: one small job produced at full speed. When the
  field cannot be expressed that way, LabelForge numbers every copy itself and sends one block per
  label, and the panel says which of the two is happening and why.
- Date and time variables with a format picker. By default the value is stamped from this PC's
  clock; with the printer-clock option the field becomes `^FC` placeholders instead, whenever the
  chosen format has an exact ZPL equivalent. The canvas always previews a real date, since the
  offline renderer has no clock.
- Job settings saved with the label: copies (`^PQ`), darkness adjust (`^MD`), and print speed
  (`^PR`), where zero means "leave the printer's default" and adds nothing to the ZPL.
- Live offline preview driven by our own ZPL generator through a swappable renderer, rendered off
  the UI thread with one render in flight and one waiting, and the one waiting is always the newest.
  A render that has finished is shown even when something newer is already queued behind it,
  because the drawing has no point at which it can be cancelled and throwing a finished answer away
  is what used to blank the canvas in the middle of a drag.
- ZPL viewer: an editable ZPL pane with syntax highlighting, a live preview, auto-sizing from
  `^PW`/`^LL`, a selector for files with multiple `^XA` blocks, and a diagnostics strip for
  unsupported commands and engine errors. It tolerates non-ZPL template markers and comment lines.
- Accented text is handled deliberately end to end. Generated labels declare `^CI28` and are written
  and sent as UTF-8 without a byte order mark. Opening a file honours its byte order mark, then
  tries UTF-8, and only then falls back to Latin-1, saying so, so a legacy CP1252 label keeps its
  accents instead of silently filling with replacement characters.
- Printer profiles (203/300/600 dpi Zebra models) with design-time head-width and density warnings.
- A built-in catalog of 797 official Zebra media specifications: search by part number, material,
  or size in the label setup bar and the label takes the exact die-cut dimensions of the stock on
  the roll.
- Continuous stock, for rolls with no gaps or die cuts. The label stops having a fixed height and
  becomes exactly as long as its content plus a trailing gap you set, so the canvas grows as you
  design and the generated `^LL` advances the roll by that much and no more. The ZPL also carries
  `^MNN` so a printer left sensing gaps stops hunting for one that is not there; nothing is emitted
  for die-cut stock, whose sensing mode belongs to whoever loaded the printer.
- Your own media presets for third-party stock the Zebra catalog does not list: save the current
  size under a name (with an optional material, corner radius, and continuous flag) and the same
  search box finds it alongside the catalog, listed first and tagged so it is never mistaken for a
  Zebra part number.
- Die-cut corner radius, taken from the picked media or set by hand. The canvas draws the label's
  real shape and shades the cut-away corners without hiding what sits in them, and the exported PDF
  is clipped to the same shape. It describes the stock, so it never changes the generated ZPL.
- Field catalogs: import the list of fields your data source provides (whatever it
  exports; the reader is tolerant of format), name it for the kind of label you are
  designing, and the text and data boxes complete `##MARKERS##` from it. Markers the
  catalog does not list are named with a suggestion, because a mistyped marker is not
  rejected by anything downstream, it simply prints as written. The marker delimiters are
  a property of the label, not of the app, so a system that writes `{{NAME}}` works too.
  Import a script file alongside and the calls it offers are completed too, read from its
  public method signatures, so `##@Abate.maturidade(COD_MATURIDADE)##` is picked rather
  than retyped. Signatures only; nothing in the file is executed.
- Crash recovery: the open document is snapshotted as you work, and if LabelForge closes
  unexpectedly the next start offers the unsaved changes back. It never restores by itself,
  and a session that closes properly leaves nothing behind, so an ordinary shutdown is
  never mistaken for a crash.
- Save and open the native `.lfl` project format (with a recent-files menu); export ZPL, PNG, and
  PDF at exact physical size.
- Two ZPL exports, because they are not the same bytes: Export ZPL writes the label the
  code pane shows, and Export Print Job writes exactly what a print would send. A run whose
  counter this PC numbers is one block per copy rather than one block and a quantity, and a
  date stamped here is taken as the job is built. Both printing and this export go through
  one builder, so a file kept as evidence cannot drift from what the printer received.
- Printing over the network (TCP 9100) and through the Windows spooler (RAW datatype, the USB path).
- Light and dark themes, a custom app icon, and Windows packaging via Velopack.

## Not yet built

- Complete ZPL-to-model import. File > Import ZPL reads a label back into the designer and covers
  every command LabelForge itself writes, so a label it generated round-trips byte for byte, and a
  foreign label comes back as far as those commands reach. What it does not model yet, mainly `^FB`
  word wrap, `^FR` reverse fields, and fonts other than the scalable font 0, is reported on import
  rather than dropped in silence.
- The full variable-data system. Samples, counters, and date/time sources exist today; per-variable
  prompts and input rules (pick lists, masks, lengths) are future work, as is storing the layout on
  the printer (`^DF`/`^XF`) so a batch sends only its data.
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

The suite covers unit behavior (unit conversion, `^FH` escaping, check digits, barcode validation,
placement classification, snapping, alignment and distribution), golden ZPL generation, JSON
round-trips, printer validation, and a corpus smoke test that renders a committed set of synthetic
ZPL fixtures (and, when present, a local private corpus of real labels) without crashing. A headless
Avalonia harness under `tools/LabelForge.E2E` drives the designer end to end, including simulated
pointer input on the rulers and snap drags, and captures screenshots in both themes. CI runs build
and test on every push and pull request.

## License

Released under the MIT License. See LICENSE for the full text, and THIRD-PARTY-NOTICES.md for the
licenses of bundled dependencies.
