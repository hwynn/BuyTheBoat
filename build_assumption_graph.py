"""[WRITES FILE] build_assumption_graph.py - regenerates 06-assumption-dependency-graph.md.

Reads assumptionChartSimpleLines.uxf directly, using only each UMLClass box's TEXT (the
requirement sets the author maintained). The chart's arrow geometry is not used. Stdlib only.

Usage:  python build_assumption_graph.py [path-to-.uxf]
"""
import sys, os, re, xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
UXF = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "assumptions", "assumptionChartSimpleLines.uxf")
OUT = os.path.join(HERE, "06-assumption-dependency-graph.md")

def load_boxes(path):
    """[READS FILE] Return the chart's UMLClass boxes as {'text': panel_attributes} dicts."""
    root = ET.parse(path).getroot()
    out = []
    for el in root.iter("element"):
        typ = el.findtext("id") or el.findtext("type") or ""
        if "UMLClass" not in typ:
            continue
        out.append({"text": el.findtext("panel_attributes") or ""})
    return out

dump = {"boxes": load_boxes(UXF)}
IDP=r'\d[\d]*(?:[bcfpn])?(?:[.:]\d+(?:[bcfpn])?)*\.a\d+'
ID=re.compile(IDP)

def norm(x): return re.sub(r'(?<=\d)[bcfpn](?=[.:])','',x).replace(':','.')
# Only b/f/p/n scope another instance (before/following/previous/next). 'c' = Current = THIS
# instance, so a 'c'-tagged requirement is an ordinary intra-instance dependency.
def is_cross(r): return bool(re.search(r'\d[bfpn][.:]',r))

def parse_box(text):
    """[CALC] Yield (id, description, req-info) per assumption declared in a box."""
    recs=[]; cur=None; assigned=False; inbrace=False; acc=''; desc={}
    for ln in text.split('\n'):
        if inbrace:
            acc+=' '+ln
            if '}' in ln:
                inbrace=False; content=acc[acc.find('{')+1:acc.rfind('}')]
                if cur and not assigned: recs[-1][2]=content; assigned=True
            continue
        s=ln.strip()
        if not s: continue
        if re.match(r'^(bg=|fg=|layer=|style=|lt=)',s): continue
        if not s.startswith('{') and not s.startswith('&') and not s.startswith('['):
            m=re.match(r'^\(?('+IDP+r')\)?',s)
            if m and (m.end()>=len(s) or s[m.end():m.end()+2].strip()[:1] in ('',':')):
                cur=m.group(1); recs.append([cur,'',None]); assigned=False
                rest=s[m.end():].lstrip(': ').strip()
                if rest and '{' not in rest: desc[cur]=rest
                continue
            if cur and cur in [r[0] for r in recs] and not desc.get(cur) and '{' not in s:
                desc[cur]=s
        if '{' in s:
            if '}' in s[s.find('{'):]:
                content=s[s.find('{')+1:s.rfind('}')]
                if cur and not assigned: recs[-1][2]=content; assigned=True
            else:
                inbrace=True; acc=s
    for r in recs: r[1]=desc.get(r[0],'')
    return recs

# build model
defined={}   # nid -> {'reqs':set,'x':set(raw cross),'mark':,'desc':,'raw_id':}
for b in dump['boxes']:
    for rid,d,content in parse_box(b['text']):
        nid=norm(rid)
        if nid in defined:
            if d and not defined[nid]['desc']: defined[nid]['desc']=d
            continue
        entry={'reqs':set(),'x':set(),'mark':None,'desc':d,'raw_id':rid}
        if content is None:
            entry['mark']='(no set)'
        else:
            ids=ID.findall(content); raw=content.strip().lower()
            if not ids:
                entry['mark']={'none':'{none}','whatever':'{whatever}'}.get(raw.strip('{} '),'{'+raw.strip()+'}') if raw else '{}'
            else:
                for r in ids:
                    if is_cross(r): entry['x'].add(r)
                    else:
                        nr=norm(r)
                        if nr!=nid: entry['reqs'].add(nr)
        defined[nid]=entry

# reverse
rev={}
for k,d in defined.items():
    for r in d['reqs']:
        rev.setdefault(r,set()).add(k)
# also count cross-instance dependents so "sinks" are truly top-level
revx=dict((k,set(v)) for k,v in rev.items())
for k,d in defined.items():
    for r in d['x']:
        revx.setdefault(norm(r),set()).add(k)
def undet(d): return d['mark'] in ('{whatever}','{}','(no set)','(none stated)') or d['mark']=='(no set)'
roots=sorted(k for k,d in defined.items() if not d['reqs'] and not d['x'] and d['mark']=='{none}')
undetermined=sorted(k for k,d in defined.items() if not d['reqs'] and not d['x'] and undet(d))
sinks=sorted(k for k in defined if k not in revx)
# layers (intra)
adj={k:set(r for r in d['reqs'] if r in defined) for k,d in defined.items()}
layers=[]; placed=set(); rem=dict(adj)
while rem:
    L=sorted(k for k,dep in rem.items() if dep<=placed)
    if not L: break
    layers.append(L); placed|=set(L)
    for k in L: rem.pop(k)
layer_of={k:i for i,L in enumerate(layers) for k in L}
cross=sorted({(k,r) for k,d in defined.items() for r in d['x']})

def sortkey(a):
    return tuple((int(p) if p.isdigit() else 999, p) for p in re.split(r'[.:]',re.sub(r'[a-z]','',a)) if p)
def chap(a):
    if a.startswith(('1.2.3.13.6','1.2.3.13.7')): return (19,'Ch.19 Expected/Actual pairing completeness')
    if a.startswith(('1.2.3.5','1.2.3.6','1.2.3.12','1.2.3.13','1.2.3c.12','1.2.3c.13')): return (18,'Ch.18 Derived calcs & initial-snapshot inheritance')
    if a.startswith('1.'): return (17,'Ch.17 log_pages & pattern continuity across pages')
    if a.startswith('2.'): return (1,'Ch.1 TransactionLogPage')
    if a.startswith('4.'): return (2,'Ch.2 FinancialPattern')
    if a.startswith('5.'): return (3,'Ch.3 EarMarkPattern')
    if a.startswith('6.'): return (4,'Ch.4 ActualTransaction')
    if a.startswith('7.'): return (5,'Ch.5 ExpectedTransaction')
    if a.startswith('8.'): return (6,'Ch.6 EarMarkEvent')
    if a.startswith('9.'): return (7,'Ch.7 BalanceSnapshot')
    if a.startswith('10.'): return (8,'Ch.8 FundJar')
    if a.startswith('3.12'): return (10,'Ch.10 Initial Snapshot')
    if a.startswith(('3.13.2','3.13c.2','3.13.3','3.13.4')): return (12,'Ch.12 full_amount & expected amounts')
    if a.startswith(('3.13.5','3.13c.5')): return (13,'Ch.13 Fund Jars')
    if a.startswith('3.13.6'): return (14,'Ch.14 Actual Transactions')
    if a.startswith('3.13.7'): return (15,'Ch.15 Expected Transactions')
    if a.startswith(('3.13.8','3.13c.8')): return (16,'Ch.16 Earmarks')
    if a.startswith(('3.13','3.13c.a','3.13.a')): return (11,'Ch.11 Balance Record')
    if a.startswith('3.'): return (9,'Ch.9 Page identity, bounds & top-level summary')
    return (99,'Unclassified')

def fmt(a):
    return f"`{a}`"+(f"·L{layer_of[a]}" if a in layer_of else "")

o=[]
o.append("# Assumption Dependency Graph")
o.append("")
o.append("""The directed dependency graph of the original design's assumptions, built from the requirement sets (`{...}`) each assumption carries in `assumptionChartSimpleLines.uxf` — the chart the author maintained as the authoritative record. Each assumption lists the other assumptions that must already hold before it can. Verbatim assumption text and the raw `{...}` blocks are in [05-assumption-chart-full-text.md](05-assumption-chart-full-text.md); this document is the *structure* — what depends on what, and in what order things become true.

**Shape of the graph:** 154 assumptions parsed from the 118 boxes (boxes bundle several assumptions). Every assumption named as a prerequisite is itself defined — the graph is self-contained. It is a directed acyclic graph: every assumption places into a topological layer, once the deliberate **cross-instance couplings** (a page or snapshot depending on its *previous*/*next*/*before*/*following* instance) are set aside. Those couplings aren't cycles — they are the cascade that recomputes each instance from the one before it.

## How to read this
- **Requires** = this assumption's own `{...}` set (what must already hold). **Required by** = the reverse (what breaks if this changes).
- `·L`n = the assumption's topological layer (L0 = axioms with no prerequisites; higher = later). This is the order to make things true — and to write/verify them — in.
- **⇄ cross-instance** lists couplings to another page/snapshot instance, via the `b`/`c`/`f`/`p`/`n` scope grammar: `p`=previous, `n`=next, `b`=all-before, `f`=all-following, `c`=current. These are the cascade edges — they make the model a forward recomputation rather than a static check.
- Scope suffixes are normalized to the base id for graph structure (so `1.2.3c.11.a4`, `1.2.3b.11.a4` are one node); the raw scoped form is preserved in the cross-instance list.
""")
o.append(f"- Counts: **{len(defined)}** assumptions · **{len(roots)}** root axioms · **{len(undetermined)}** `{{whatever}}`/undetermined · **{len(sinks)}** top-level sinks · **{len(cross)}** cross-instance couplings · **{len(layers)}** layers.")
o.append("")
o.append("---")
o.append("")
o.append("## The graph, by chapter")
o.append("")
groups={}
for k in defined: groups.setdefault(chap(k),[]).append(k)
for ch in sorted(groups):
    o.append(f"### {ch[1]}")
    o.append("")
    for a in sorted(groups[ch],key=sortkey):
        d=defined[a]
        desc=(' — '+d['desc']) if d['desc'] else ''
        o.append(f"- **{fmt(a)}**{desc}")
        if d['reqs']:
            o.append(f"  - requires: {', '.join(fmt(r) for r in sorted(d['reqs'],key=sortkey))}")
        elif d['mark']:
            o.append(f"  - requires: *{d['mark']}* (no prerequisites)")
        else:
            o.append(f"  - requires: *(none stated)*")
        if d['x']:
            o.append(f"  - ⇄ cross-instance: {', '.join('`'+r+'`' for r in sorted(d['x']))}")
        dep=rev.get(a)
        if dep:
            o.append(f"  - required by: {', '.join(fmt(r) for r in sorted(dep,key=sortkey))}")
        else:
            o.append(f"  - required by: *(nothing — a top-level goal)*")
    o.append("")
o.append("---")
o.append("")
o.append("## Topological layers (write/verify order)")
o.append("")
o.append("L0 has no prerequisites; each later layer depends only on earlier ones (cross-instance couplings excluded, since they point at *other* instances). This is the clean recomputation of the stratification the author's `stratifyAssumptions.py`/`findLongestPath.py` scripts were after (see [03 App. A](03-assumptions-glossary.md#appendix-a-reduction--merge-candidates)).")
o.append("")
for i,L in enumerate(layers):
    o.append(f"- **L{i}** ({len(L)}): {', '.join('`'+a+'`' for a in sorted(L,key=sortkey))}")
o.append("")
o.append("## Root axioms (genuine `{none}` leaves — no prerequisites)")
o.append("")
o.append(', '.join('`'+a+'`' for a in sorted(roots,key=sortkey)))
o.append("")
o.append("## Undetermined-prerequisite nodes (`{whatever}` / no set)")
o.append("")
o.append("The author marked these with `{whatever}` (or left no set) — a placeholder meaning the prerequisites were never pinned down (the requirement-set analogue of the `?...?` markers used elsewhere for values that were left for later). They are **not** axioms — treat their prerequisites as open. They land at L0 in the layering only because no set was recorded:")
o.append("")
o.append(', '.join('`'+a+'`' for a in sorted(undetermined,key=sortkey)))
o.append("")
o.append("## Top-level sinks (nothing depends on them)")
o.append("")
o.append("The ultimate goals plus a few addendum sub-rules that no other assumption cites:")
o.append("")
o.append(', '.join('`'+a+'`' for a in sorted(sinks,key=sortkey)))
o.append("")
o.append("## Cross-instance cascade couplings (the `b`/`c`/`f`/`p`/`n` edges)")
o.append("")
o.append("These are dependencies on a *different* page or snapshot instance — the spine of the forward cascade. `dependent  ⇄  raw-scoped-prerequisite`:")
o.append("")
for k,r in sorted(cross,key=lambda t:(sortkey(t[0]),t[1])):
    o.append(f"- `{k}`  ⇄  `{r}`")
o.append("")
o.append("---")
o.append("")
o.append("""## Notes on the chart's notation and coverage

- **Parenthesized requirement IDs** in a `{...}` set (e.g. `(9.5.a1)`) are redundant — already implied transitively by another member of the set — so they're folded in as ordinary prerequisites with no special marking. Dropping them would not change reachability.
- **A second `{...}` block** on some boxes is a reduced restatement of the first (a strict subset once scope suffixes are normalized). This graph uses the first (full) block.
- **`3:13.a9`** (with a stray colon) is the same assumption as `3.13.a9`, and is merged.
- **`3.4.a1` ("expired cannot be None")** is not in the chart — `expired` simply shields old pages from cascades — so it is absent here.
- **`10.1.a1`/`10.2.a1`** appear in `a03.txt`'s `3.10.a3` requirement list but were never given a definition and are dropped from the chart's `3.10.a3` box, so they are absent here.
- **Assumptions that live only in the chart** (not the `.txt` files): `10.4.a2`/`10.4.a3` (milestone rules for a jar tied to a repeated expected transaction), `3.13.a10` (the normal-day implicit earmark), and the month-boundary rules `2.1.a2`/`2.2.a2` (their text sits unlabeled in the `2.1.a1`/`2.2.a1` boxes; the tests chart is where they get IDs).
- **The cross-instance scope tags** (`b`/`c`/`f`/`p`/`n`) come from the box text's own scoped IDs — the same information the chart draws as orange (previous) and red (next) arrows.
- Where a box's `{...}` set differs from the same assumption's set in the `.txt` files, this graph uses the **chart set** (the author's authoritative record); the `.txt` variants are reproduced verbatim in [03-assumptions-glossary.md](03-assumptions-glossary.md).
- A few bundled sub-rules carry **no set of their own** in the chart (`1.2.3.12.5.a4`–`a7`, `1.2.3.12.3.a3`, `3.13.6.3.a1`, `3.13.7.1.a1`); they show as sinks with "(none stated)" — addenda, not missing data.

---

## Process regions — the cascade steps

On top of the dependency graph, the author drew dotted outlines grouping related assumptions, each labelled with a plain-language note. Each region is one step of the cascade update — the workflow that walks a page's balance record forward, making each cluster of assumptions true in turn. The regions (boxes named by their first assumption; a box may bundle more):

| Cascade step (region label) | Assumptions in the step |
|---|---|
| Finance patterns in this page are perfect | `1.2.3.10.a1`, `1.2.3c.10.a4`, `3.10.a1`, `3.10.a2`, `3.11.2.a1` |
| Earmark patterns in this page are perfect | `1.2.3c.11.a1`, `1.2.3c.11.a2`, `1.2.3c.11.a3`, `3.11.1.a1`, `3.13.1.a1`, `3.11.2.a1`, `1.2.3c.11.a4` *(a fine-dotted outer group wraps this step and the finance-pattern step above)* |
| Loop over pages and fix patterns | contains the two pattern steps above, plus the fund-jar and deallocation work below |
| New events created, old events removed | `3.13.8.a2`, `3.9.a1`, `7.1.a1`, `3.13.6.a1`, and the assumptions above them in the same column |
| The whole page is cleaned of obsolete events and balance snapshots | `3.13.a2`, `1.2.3c.13.a3`, `3.13.a3` |
| This balance snapshot has full_amount | `3.13c.2.a1`, `3.13c.2.a2`, `3.13c.2.a3`, `1.2.3.12.2.a1`, `3.13.a4` |
| If this is a deallocation day, the implicit earmarks are made | `3.13c.a6`, `3.13c.a7`, `3.13c.8.4.a2`, `3.13c.a8` |
| All\\* earmarks created on this page *(\\* except implicit earmarks, added later on deallocation days)* | `3.13.8.5.a1`, `3.13c.8.a4`, `3.13.8.a6`, `3.13.8.3.a1`, `3.13.8.a3`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13.8.a7` |
| Transactions are paired | `3.13.a5`, `6.4.a1`, `1.2.3.13.a1`, `1.2.3.13.6.a1`, `1.2.3.13.a2` |
| Initial snapshot is good except for expected amount | `3.12.1.a1`, `3.12.a1`, `9.1.a1`, `3.13.2.a4`, `9.8.2.a1`, `9.8.6.a1`, `1.2.3c.12.a1` |
| Loop over all pages, updating expected_amount / expected_free_amount on initial snapshots | the cross-page rollup, ending with `current_unpaid_expected`, `current_free_amount`, `current_safety_cushion` and each jar's `milestone_amount` (the consciously-applied calculations, run last) |
| Fund jar validity at the initial snapshot | `3.12.5.a1`, `9.5.1.a1`, `9.5.a1` |

## The author's test-bundling plan

The project was meant to be testing-first. A sibling chart (`assumptionChartTests.uxf`) grouped assumptions into test bundles — one test function verifying a related cluster together — with a colour code for the order to write them in: **green** = no unmet dependencies (write first), **grey** = intermediate, **pink** = end-of-chain completeness checks (write last). It covers 64 of the assumptions in 24 bundles:

| Test | Bundles | Order |
|---|---|---|
| `2.T1` | `2.1.a1`, `2.2.a1` + month-boundary `2.1.a2`/`2.2.a2` | |
| `4.T1` | `4.1.a1`, `4.2.a1` | |
| `5.T1` | `5.1.a1`, `5.2.a1`, `5.3.a1` | green |
| `6.T1` | `6.1.a1`, `6.5.a1`, `6.6.a1` | green |
| `7.T01` | `7.1.a1`, `7.6.a1`, `7.7.a1`, `7.8.a1` | green |
| `7.T02` | `7.3.a1`, `7.4.a1`, `7.4.a2`, `7.5.a1`, `7.5.a2` | |
| `8.T1` | `8.1.a1`–`8.5.a1` | |
| `9.T01` | `9.7.6.a1` | |
| `9.T02` | `9.6.2.a1` | |
| `10.T1` | `10.3.a1`, `10.4.a1` + `10.4.a2`/`10.4.a3` | |
| `1.T01` | `1.2.3.a1`, `1.1.a1` (requires `2.T1`) | |
| `1.T02` | finance-pattern cross-page continuity (requires `4.T1`, `1.T01`) | |
| `1.T04` | earmark-pattern cross-page continuity (requires `1.T02`, `3.T04`) | grey |
| `3.T01` | `3.2.a1`, `3.2.a2`, `3.3.a1`, `3.3.a2`, `3.5.a1`, `3.8.a1`, `3.9.a1` | green |
| `3.T02` | `3.10.a1`, `3.10.a2` (requires `3.T01`) | |
| `3.T03` | `3.13.7.6.a1`, `3.13.7.a2` (requires `1.T02`) | |
| `3.T04` | `3.11.2.a1`, `3.11.2.a2` (requires `5.T1`) | |
| `3.T05` | `3.13.1.a1` (requires `3.T01`) | |
| `3.T06` | `3.13.8.a1`, `3.13.8.a2`, `3.13.8.4.a1`, `3.13.8.a6` | |
| `3.T07` | `3.13.8.3.a1`, `3.13.8.a3` (requires `1.2.3c.11.a4`) | |
| `3.T08` | `3.13.8.5.a1`, `3.13c.8.a4`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13.8.1.a2` | |
| `3.T09` | `3.13.8.a7` (requires `3.T06`, `3.T07`, `3.T08`) | pink |
| `3.T10` | `3.13.6.a1`, `3.13.6.a2`, `3.13.6.3.a1` | pink |
| `3.T11` | `3.13.7.a1`, `3.13.7.1.a1`, `3.13.7.a3` (requires `1.2.3c.10.a4`, `3.T03`) | |

A companion file at the project root, `things to test.txt`, holds the worked *scenarios* those tests would simulate (e.g. saving toward a boat, with variations for early/late and paired/unpaired purchases), and `class documentation.ods` supplies numeric examples to check results against.
""")
open(OUT,'w',encoding='utf-8').write('\n'.join(o))
print("wrote",OUT,"-",len(o),"lines")
print("assumptions",len(defined),"roots",len(roots),"sinks",len(sinks),"cross",len(cross),"layers",len(layers))
