# LabelForge 0.4.0 release notes (draft)

This is a draft for review. Version 0.4.0 has not been published, signed or
connected to an update feed. The latest local unsigned candidate was built from
[`b24f79b`](https://github.com/HenriqueRicieri/LabelForge/commit/b24f79b481caad546d2f68d3104bd0ae562692e7).
See the [validation record](RELEASE-VALIDATION.md) for package checksums and
the checks still needed before distribution.
It includes the `6276448` Auto-width font correction. The previously installed
candidate was built from `cbacfd4` and remains installed.

## What changed since v0.3.0

- The designer now has labeled creation tools, an inspector that can collapse in
  small windows, a separate label-setup dialog, field search and quick editing.
  F2, previous/next field navigation and repeat placement reduce trips between
  the label and inspector.
- Text and object editing is more predictable. Centimeters are the default
  display unit, with exact printer dots available. Text handles resize on the
  visual axis; wrapping width, rotation, anchors, guides, groups and undo/redo
  preserve their stored values more consistently. Guide insertion now uses the
  nearest printer dot.
- Label setup supports user media presets, continuous stock, corner radius and
  multi-across layouts. The designer adds PDF417, more shapes, built-in printer
  fonts, field catalogs, counters, date/time sources and GS1/check-digit help.
- Output now includes a PDF export and a separate exact print-job ZPL export.
  TCP printing reports delivery and can read a printer-status snapshot. The
  offline ZPL viewer handles more imported graphics and uses the label's
  effective size.
- Crash recovery, recent files, `.lfl` file association and a starter gallery
  are included. User data now lives outside the installer directory, so normal
  installer cleanup cannot remove it. The recovery banner is readable in both
  themes, and ruler paint uses less memory.

The [changelog](../CHANGELOG.md) has the complete user-visible list.

## Installing when a release is available

The Windows package is self-contained for win-x64. A versioned upgrade from
0.3.0 was not validated for this release. If replacing an older local build
that stored user data inside `%LocalAppData%/LabelForge`, close LabelForge,
back up its user JSON files and recovery folder, and run the corrected portable
app's migration-only command. Confirm the expected files are under
`%LocalAppData%/LabelForge.UserData` before running Setup. The
[development guide](DEVELOPMENT.md#windows-packaging) gives the command and
explains why this step must precede an older installer's cleanup. Keep the backup
until your labels and settings reopen successfully.

After installation, open a `.lfl` from Explorer and check that LabelForge starts.
The candidate is currently unsigned; verify its checksum against the
[validation record](RELEASE-VALIDATION.md#latest-candidate-files) before using
it. There is no automatic update feed yet.

## Validation and current limits

The previously installed candidate's 0.4.0 in-place repair, installed-file
comparison, `.lfl` association, save/export and offline viewer smoke checks passed. A
Windows uninstall/reinstall cycle also preserved user data, restored the app
registration and opened `.lfl` directly. Its source CI passed all nine jobs.
The newer `b24f79b` package passed integrity checks and source CI 9/9 but awaits
native installation. A risk-based native check covers measured 100/125/150%
layouts and targeted high-scale compact editing and guide gestures. An
exhaustive workflow in every scale/theme/window cell remains unverified. A
pristine user-profile install
still needs native evidence. Versioned upgrade was waived, not validated.
Exact Rust compiler/standard-library provenance for the native packaging
helpers also remains open.

Real-printer and barcode-scan validation was removed from this release plan at
the maintainer's request because hardware is currently unaffordable. Physical
output is unverified. The synthetic tests do not establish that a particular
printer or scanner will accept a label.
