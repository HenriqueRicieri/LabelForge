"""Regenerates src/LabelForge.Core/Media/zebra-media-catalog.json.

Reads the media stock database installed with ZebraDesigner 3
(C:/ProgramData/Zebra Technologies/ZebraDesigner 3/Stock.db, a plain SQLite file)
and exports the factual media specifications: part number, material line, die-cut
dimensions, and corner radius. These are physical product specifications that Zebra
also publishes in its public media catalogs; no proprietary code or artwork is copied.

The generated JSON is committed, so this script is only needed to refresh the catalog
after a ZebraDesigner update. Requires only the Python standard library.

Usage: python scripts/export_stock_catalog.py [path-to-Stock.db]
"""

import json
import re
import sqlite3
import sys
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\Zebra Technologies\ZebraDesigner 3\Stock.db"
OUTPUT = Path(__file__).resolve().parent.parent / "src" / "LabelForge.Core" / "Media" / "zebra-media-catalog.json"

# StockName looks like "10026376 (2in x 1in)"; the parenthesized part is the
# nominal size in the vendor's display unit and, for continuous rolls, carries
# the real roll length that the µm columns cap at a default page height.
NAME_RE = re.compile(r"^(?P<pn>.+?)\s*\((?P<dims>[^)]*)\)\s*$")
DIM_RE = re.compile(r"([\d.]+)\s*(mm|in)", re.IGNORECASE)


def dims_to_mm(dims_text):
    """Parses 'W x H' from the display text and returns (width_mm, height_mm) or None."""
    parts = DIM_RE.findall(dims_text)
    if len(parts) != 2:
        return None
    values = [float(v) * (25.4 if u.lower() == "in" else 1.0) for v, u in parts]
    return values[0], values[1]


def export(db_path):
    cur = sqlite3.connect(db_path).cursor()
    cur.execute(
        """SELECT StockNumber, StockType, StockName, LabelSizeX, LabelSizeY, RadiusX
           FROM Stock WHERE StockNumber IS NOT NULL AND LabelSizeX > 0"""
    )

    entries = {}
    for pn, material, name, width_um, height_um, radius_um in cur.fetchall():
        width_mm = round(width_um / 1000.0, 3)
        height_mm = round(height_um / 1000.0, 3)
        key = (pn.upper(), width_um, height_um)
        if key in entries:
            continue  # case variants of the same material line duplicate rows

        size_text = ""
        continuous = False
        match = NAME_RE.match(name or "")
        if match:
            size_text = match.group("dims").strip()
            nominal = dims_to_mm(size_text)
            # Continuous rolls state their full length in the name (e.g. 888in)
            # while the height column caps at a default page height.
            continuous = nominal is not None and nominal[1] > height_mm + 1.0

        entry = {
            "partNumber": pn,
            "material": material,
            "widthMm": width_mm,
            "heightMm": height_mm,
            "sizeText": size_text,
        }
        if radius_um:
            entry["radiusMm"] = round(radius_um / 1000.0, 3)
        if continuous:
            entry["continuous"] = True
        entries[key] = entry

    ordered = sorted(entries.values(), key=lambda e: e["partNumber"])
    lines = ",\n".join("  " + json.dumps(e, separators=(", ", ": ")) for e in ordered)
    OUTPUT.write_text("[\n" + lines + "\n]\n", encoding="utf-8", newline="\n")
    print(f"{len(ordered)} media entries -> {OUTPUT}")


if __name__ == "__main__":
    export(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DB)
