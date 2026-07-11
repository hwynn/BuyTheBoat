#!/usr/bin/env python
"""Extract deallocation test vectors from DeallocationProof.ods (sheet
DeallTest_2) as C# literals for DeallocationCalculatorTests.

Written 2026-07-10 for Step 1 of MyMoneyForecast/planning/07 (the pure
deallocation function). Companion to parse_ods_properties.py / uxf_graph_tool.py
— stdlib-only (zipfile + ElementTree), no odfpy.

Unlike parse_ods_properties.py, this reads the FULL-PRECISION office:value
attribute for numeric cells (not the rounded display text), so mined
expected-outputs match exact decimal math to float precision. It emits one C#
`new(...)` ProofVector literal per DeallTest_2 column that is a valid
deallocation scenario (sensible allocation == 1 AND Deallocation == 1),
combining er+ei into the single `Existing` earmark the function takes.

Usage:
    python extract_deallocation_vectors.py DeallocationProof.ods

Paste the output into the MinedVectors array in
MyMoneyForecast/tests/MyMoneyForecast.Domain.Tests/DeallocationCalculatorTests.cs.
"""
import sys
import io
import zipfile
import xml.etree.ElementTree as ET

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', newline='')

TNS = 'urn:oasis:names:tc:opendocument:xmlns:table:1.0'
ONS = 'urn:oasis:names:tc:opendocument:xmlns:office:1.0'
NS = {'office': ONS, 'table': TNS,
      'text': 'urn:oasis:names:tc:opendocument:xmlns:text:1.0'}

SHEET = 'DeallTest_2'


def q(name, attr):
    return '{%s}%s' % (name, attr)


def cell_value(c):
    """Full-precision numeric value if present, else stripped text, else ''."""
    vt = c.get(q(ONS, 'value-type'))
    if vt in ('float', 'percentage', 'currency'):
        v = c.get(q(ONS, 'value'))
        if v is not None:
            return v  # exact stored value as a string
    paras = [''.join(p.itertext()) for p in c.findall('text:p', NS)]
    return '\n'.join(paras).strip()


def sheet_grid(ods_path, sheet_name):
    with zipfile.ZipFile(ods_path, 'r') as z:
        with z.open('content.xml') as f:
            tree = ET.parse(f)
    root = tree.getroot()
    table = next((t for t in root.findall('.//table:table', NS)
                  if t.get(q(TNS, 'name')) == sheet_name), None)
    if table is None:
        raise SystemExit('sheet not found: ' + sheet_name)

    rows = {}
    for row in table.findall('table:table-row', NS):
        cells = []
        for c in row:
            tag = c.tag.split('}')[-1]
            repeat = min(int(c.get(q(TNS, 'number-columns-repeated'), '1')), 60)
            if tag == 'covered-table-cell':
                cells.extend([''] * repeat)
                continue
            cells.extend([cell_value(c)] * repeat)
        if not cells:
            continue
        label = cells[0]
        if label and label not in rows:
            rows[label] = cells
    return rows


def num(rows, label, col):
    cells = rows.get(label, [])
    if col >= len(cells) or cells[col] in ('', None):
        return 0.0
    return float(cells[col])


def main():
    if len(sys.argv) < 2:
        print('Usage: python extract_deallocation_vectors.py <DeallocationProof.ods>')
        sys.exit(1)

    rows = sheet_grid(sys.argv[1], SHEET)
    ncols = len(rows['c'])

    def arr(xs):
        return ', '.join('%gm' % x for x in xs)

    print('// Auto-extracted from DeallocationProof.ods sheet %s' % SHEET)
    print('// (columns where sensible allocation==1 AND Deallocation==1).')

    kept = 0
    for col in range(1, ncols):
        if num(rows, 'sensible allocation', col) != 1 or num(rows, 'Deallocation', col) != 1:
            continue
        kept += 1
        er = [num(rows, 'er%d' % j, col) for j in range(1, 5)]
        ei = [num(rows, 'ei%d' % j, col) for j in range(1, 5)]
        existing = [er[j] + ei[j] for j in range(4)]
        f = [num(rows, 'f%d' % j, col) for j in range(1, 5)]
        ap = [num(rows, 'ap%d' % j, col) for j in range(1, 5)]
        p = [num(rows, 'p%d' % j, col) for j in range(1, 5)]
        b = [num(rows, 'b%d' % j, col) for j in range(1, 5)]
        fb = [num(rows, 'Fb%d' % j, col) for j in range(1, 5)]
        debt = 'false' if num(rows, 'Not Debt', col) == 1 else 'true'
        print('        new(Col: %d, C: %gm, F: [%s], Existing: [%s], Ap: [%s], '
              'Au: %gm, P: [%s], B: [%s], Fb: [%s], DebtRemainder: %gm, InDebt: %s),' % (
                  col, num(rows, 'c', col), arr(f), arr(existing), arr(ap),
                  num(rows, 'au', col), arr(p), arr(b), arr(fb),
                  num(rows, 'Nb5', col), debt))

    print('// %d vectors.' % kept)


if __name__ == '__main__':
    main()
