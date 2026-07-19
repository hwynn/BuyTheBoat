"""
Parses UMLet .uxf assumption charts into a structured node/edge graph.

Usage:
    python uxf_graph_tool.py <path-to-uxf> [--dump out.json]

What it does:
- UMLClass elements become "boxes" (assumption id parsed from the first line of text).
- Text elements are kept separately (process-step / annotation labels).
- Relation elements become edges; each edge's absolute endpoints are resolved to
  the nearest box by geometry (UMLet anchors relations to a shape's border, not
  its center, so a small gap between an endpoint and its box is normal --
  see CONFIDENCE_THRESHOLD below for what counts as suspicious).
- Relations with a closed polygon path (dotted "lt=." / "lt=.." style, first
  point == last point) are treated as region outlines rather than edges, and
  reported as "contains these boxes" instead.

This was built while reconstructing the dependency graph documented in
06-assumption-dependency-graph.md -- see that file for what the output means
and for the specific findings (dangling edges, process regions, test bundles).
Only assumptionChartSimpleLines.uxf and assumptionChartTests.uxf were fully
mined; rerun this against the other assumptionChart*.uxf files to extend that
work.
"""
import xml.etree.ElementTree as ET
import sys
import json
import re

CONFIDENCE_THRESHOLD = 100.0  # endpoint-to-box-center distance beyond which a match is "suspect"


def get_zoom(path):
    return float(ET.parse(path).getroot().find('zoom_level').text)


def parse_uxf(path):
    root = ET.parse(path).getroot()
    boxes, texts, relations = [], [], []

    for idx, el in enumerate(root.findall('element')):
        kind = el.find('id').text
        coords = el.find('coordinates')
        x, y = float(coords.find('x').text), float(coords.find('y').text)
        w, h = float(coords.find('w').text), float(coords.find('h').text)
        pa_el = el.find('panel_attributes')
        pa = pa_el.text if pa_el is not None and pa_el.text else ''
        aa_el = el.find('additional_attributes')
        aa = aa_el.text if aa_el is not None and aa_el.text else None

        if kind == 'UMLClass':
            first_line = pa.strip().split('\n')[0].strip()
            m = re.match(r'^([0-9][0-9A-Za-z:.]*)\b', first_line)
            boxes.append({'idx': idx, 'x': x, 'y': y, 'w': w, 'h': h, 'text': pa, 'aid': m.group(1) if m else None})
        elif kind == 'Text':
            texts.append({'idx': idx, 'x': x, 'y': y, 'w': w, 'h': h, 'text': pa})
        elif kind == 'Relation':
            points = []
            if aa:
                nums = [float(v) for v in aa.strip().split(';')]
                points = [(x + nums[i], y + nums[i + 1]) for i in range(0, len(nums), 2)]
            relations.append({'idx': idx, 'x': x, 'y': y, 'w': w, 'h': h, 'style': pa.strip(), 'points': points})

    return boxes, texts, relations


def point_in_box(px, py, box, tol=2.0):
    return (box['x'] - tol) <= px <= (box['x'] + box['w'] + tol) and (box['y'] - tol) <= py <= (box['y'] + box['h'] + tol)


def nearest_shape(px, py, shapes):
    best, best_d = None, None
    for s in shapes:
        cx, cy = s['x'] + s['w'] / 2, s['y'] + s['h'] / 2
        d = (cx - px) ** 2 + (cy - py) ** 2
        if best_d is None or d < best_d:
            best_d, best = d, s
    return (best, best_d ** 0.5) if best is not None else (None, None)


def resolve_point(px, py, shapes):
    """Returns (shape, distance, was_approximate)."""
    matches = [s for s in shapes if point_in_box(px, py, s)]
    if matches:
        matches.sort(key=lambda s: s['w'] * s['h'])
        return matches[0], 0.0, False
    shape, d = nearest_shape(px, py, shapes)
    return shape, d, True


def point_in_polygon(x, y, poly):
    inside, n, j = False, len(poly), len(poly) - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def build_graph(path):
    boxes, texts, relations = parse_uxf(path)
    edges, regions = [], []

    for r in relations:
        if len(r['points']) < 3:
            if len(r['points']) == 2:
                pass  # handled below as a normal edge
            else:
                continue
        is_closed = len(r['points']) >= 3 and r['points'][0] == r['points'][-1]
        if is_closed:
            contained = [b['aid'] for b in boxes if point_in_polygon(b['x'] + b['w'] / 2, b['y'] + b['h'] / 2, r['points'])]
            cx = sum(p[0] for p in r['points']) / len(r['points'])
            cy = sum(p[1] for p in r['points']) / len(r['points'])
            label_box, label_d = nearest_shape(cx, cy, texts)
            regions.append({
                'style': r['style'], 'contains': contained,
                'nearest_label': label_box['text'].split('\n')[0] if label_box else None,
            })
            continue
        if len(r['points']) < 2:
            continue
        p_first, p_last = r['points'][0], r['points'][-1]
        b1, d1, _ = resolve_point(p_first[0], p_first[1], boxes)
        b2, d2, _ = resolve_point(p_last[0], p_last[1], boxes)
        edges.append({
            'from': b1['aid'] if b1 else None, 'from_dist': round(d1, 1),
            'to': b2['aid'] if b2 else None, 'to_dist': round(d2, 1),
            'style': r['style'], 'confident': d1 <= CONFIDENCE_THRESHOLD and d2 <= CONFIDENCE_THRESHOLD,
        })

    return {'boxes': boxes, 'texts': texts, 'edges': edges, 'regions': regions}


if __name__ == '__main__':
    graph = build_graph(sys.argv[1])
    print(f"boxes={len(graph['boxes'])} edges={len(graph['edges'])} regions={len(graph['regions'])}")
    suspect = [e for e in graph['edges'] if not e['confident']]
    print(f"suspect edges (endpoint > {CONFIDENCE_THRESHOLD} units from any box): {len(suspect)}")
    if '--dump' in sys.argv:
        out_path = sys.argv[sys.argv.index('--dump') + 1]
        with open(out_path, 'w', encoding='utf-8') as f:
            json.dump(graph, f, indent=1)
        print(f"wrote {out_path}")
