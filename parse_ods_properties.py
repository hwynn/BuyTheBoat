#!/usr/bin/env python
"""
Parse an OpenDocument Spreadsheet (.ods) file's content.xml using only the
Python standard library (zipfile + xml.etree.ElementTree). No odfpy needed.

Written 2026-07-10 to extract class documentation.ods's "Properties" sheet
(the authoritative per-class/per-property spec, cross-checked against
01-glossary-of-terms.md) — no reusable .ods parser existed in this repo
before this, unlike uxf_graph_tool.py for the .uxf charts. Kept here
alongside that script for the same reason: reusable for any of this
project's other .ods sheets that haven't been mined yet (class documentation
.ods has 16 sheets total; only "Properties" has been fully cross-checked
against the reconstructed docs as of this writing).

Usage:
    python parse_ods_properties.py <path-to-file.ods> [sheet-name-substring]

If sheet-name-substring is omitted, prints the list of sheet (table) names.
If provided (case-insensitive substring match), prints the FULL verbatim
content of the matching sheet(s), row by row, cell by cell, as plain text.
"""
import sys
import io
import zipfile
import xml.etree.ElementTree as ET

# Force UTF-8 stdout regardless of Windows console code page, so curly
# quotes/apostrophes in the source (valid UTF-8, e.g. U+2019) round-trip
# correctly instead of being mangled to '?' or replacement chars.
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', newline='')

NS = {
    'office': 'urn:oasis:names:tc:opendocument:xmlns:office:1.0',
    'table': 'urn:oasis:names:tc:opendocument:xmlns:table:1.0',
    'text': 'urn:oasis:names:tc:opendocument:xmlns:text:1.0',
}

def cell_text(cell):
    """Extract all text from a table-cell, joining multiple text:p paragraphs
    with a newline (representing manual line breaks within a cell)."""
    paras = []
    for p in cell.findall('text:p', NS):
        # get all text including nested spans (text:span), joined
        text = ''.join(p.itertext())
        paras.append(text)
    return '\n'.join(paras)

def annotation_text(cell):
    """Extract any comment/annotation text attached to a cell."""
    ann = cell.find('office:annotation', NS)
    if ann is None:
        return None
    paras = []
    for p in ann.findall('text:p', NS):
        text = ''.join(p.itertext())
        paras.append(text)
    return '\n'.join(paras) if paras else None

def main():
    if len(sys.argv) < 2:
        print("Usage: python parse_ods_properties.py <path-to-file.ods> [sheet-name-substring]")
        sys.exit(1)

    ods_path = sys.argv[1]
    sheet_filter = sys.argv[2].lower() if len(sys.argv) > 2 else None

    with zipfile.ZipFile(ods_path, 'r') as z:
        with z.open('content.xml') as f:
            tree = ET.parse(f)

    root = tree.getroot()
    tables = root.findall('.//table:table', NS)

    if sheet_filter is None:
        print(f"Total sheets found: {len(tables)}")
        for t in tables:
            name = t.get('{urn:oasis:names:tc:opendocument:xmlns:table:1.0}name')
            rows = t.findall('table:table-row', NS)
            print(f"  - {name!r}  (row elements: {len(rows)})")
        return

    matched = [t for t in tables
               if sheet_filter in (t.get('{urn:oasis:names:tc:opendocument:xmlns:table:1.0}name') or '').lower()]

    if not matched:
        print(f"No sheet matched filter {sheet_filter!r}")
        return

    for t in matched:
        name = t.get('{urn:oasis:names:tc:opendocument:xmlns:table:1.0}name')
        print("=" * 100)
        print(f"SHEET: {name}")
        print("=" * 100)

        row_idx = 0
        for row in t.findall('table:table-row', NS):
            row_repeat = int(row.get('{urn:oasis:names:tc:opendocument:xmlns:table:1.0}number-rows-repeated', '1'))

            # All direct children in document order (table-cell / covered-table-cell).
            cells = list(row)

            cell_values = []
            for c in cells:
                tag = c.tag.split('}')[-1]
                repeat = int(c.get('{urn:oasis:names:tc:opendocument:xmlns:table:1.0}number-columns-repeated', '1'))
                if tag == 'covered-table-cell':
                    # Part of a merged cell region (covered by a vertically/
                    # horizontally spanned cell above/left) -- emit blank
                    # placeholder(s) to PRESERVE column position/alignment;
                    # do NOT skip, or every later column on this row shifts
                    # left (this silently misaligned the first extraction
                    # attempt against Properties' vertically-merged class
                    # number/name cells).
                    for _ in range(repeat):
                        cell_values.append('')
                    continue
                val = cell_text(c)
                ann = annotation_text(c)
                if ann:
                    val = f"{val} [COMMENT: {ann}]" if val else f"[COMMENT: {ann}]"
                for _ in range(repeat):
                    cell_values.append(val)

            # Trim trailing empty cells (padding to sheet width)
            while cell_values and cell_values[-1] == '':
                cell_values.pop()

            is_blank_row = len(cell_values) == 0

            if is_blank_row:
                # Skip printing purely-blank rows, but advance the row
                # counter so reported row numbers still match the sheet.
                row_idx += row_repeat
                continue

            for r in range(row_repeat):
                row_idx += 1
                print(f"--- Row {row_idx} ---")
                for ci, val in enumerate(cell_values):
                    print(f"  [col {ci+1}] {val!r}")
        print()

if __name__ == '__main__':
    main()
