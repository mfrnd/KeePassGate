"""The checks and comparisons of the report `measured-rules-cut-parts.md`, over a run of run-cut.ps1.
ADDED (cut parts): a file of its own beside compare.py, whose masks it uses.

Run:  uv run --no-project python -I -X utf8 compare_cut.py check RUN
      uv run --no-project python -I -X utf8 compare_cut.py second
      uv run --no-project python -I -X utf8 compare_cut.py analyze RUN

check RUN    holds what of out\\cut-RUN an earlier record holds: each part with KeePassXC 2.8.0 to
             recorded-rows\\; the extended model in the whole-rule form to recorded-decided\\; the
             replays in the whole-rule form to the column of recorded\\ with the repair undone, which
             is the store as decided; each traced search's counts, and the shortest history it
             shrinks, to that search's record. Exits 1 on a difference.
second       holds out\\cut-2 to recorded-cut\\ line for line with compare.py's masks, the traces
             out\\cut-2\\*.jsonl to out\\cut-1\\*.jsonl, and out\\cut-2\\replays\\ to
             recorded-cut\\replays\\. Exits 1 on a difference.
analyze RUN  the comparisons of the report, from out\\cut-RUN and the records beside it, written to
             out\\cut-RUN\\analysis.txt. The single histories it plays, by model\\search_replay.py,
             go to out\\cut-RUN\\replays\\.
"""
import json
import os
import re
import subprocess
import sys

FAMILY = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, FAMILY)
import compare as CMP  # noqa: E402

lines = CMP.lines
REC_ROWS = os.path.join(FAMILY, 'recorded-rows')
REC_DEC = os.path.join(FAMILY, 'recorded-decided')
REC_FIRST = os.path.join(FAMILY, 'recorded')
REC_CUT = os.path.join(FAMILY, 'recorded-cut')
RELEASES = ('2.4.2', '2.6.6', '2.7.12', '2.8.0')
SLOW = ('H3', 'H6', 'H13')
MASKS = [(re.compile(r'(KeePassXC|keepassxc-cli) \d+\.\d+\.\d+'), r'\1')]
CONFIG = re.compile(r'^(.*\S)\s+(\d) files\s+(\d+) histories  (.*)$')


def out_dir(run):
    return os.path.join(FAMILY, 'out', 'cut-' + run)


def emit(*a):
    print(*a)


# ------------------------------------------------------------------ check
def held(a, b, label, prefix=False):
    """Each line of b against a; with prefix, b but its last line against the start of a."""
    if not os.path.exists(a):
        emit('%s: not in out' % label)
        return False
    x, y = lines(a), lines(b)
    if prefix:
        y = y[:-1]
        x = x[:len(y)]
    diffs = [k for k in range(max(len(x), len(y))) if k >= len(x) or k >= len(y) or x[k] != y[k]]
    emit('%s: %d lines of the record, %s' % (label, len(y), 'the same' if not diffs else
                                             '%d differ, first at line %d' % (len(diffs), diffs[0] + 1)))
    return not diffs


def scen_results(path, last):
    """The result of each of the design's 20 replays: the last column, or the only column."""
    out, inside = [], False
    for line in lines(path):
        if line.startswith('=='):
            inside = line.startswith("== the design's 20 replays")
            continue
        if inside and line.startswith('   ') and not line.startswith('    ') and not line.startswith('   columns'):
            out.append(line.strip().split(' | ')[-1] if last else line.strip())
    return out


def sec6_results(path, name):
    return [line[13:] for line in lines(path) if line.startswith('   %-9s ' % name)]


def config_lines(path, nrep):
    return [line for line in lines(path) if CONFIG.match(line) and CONFIG.match(line).group(2) == str(nrep)]


def shortest_of_record(path, nrep):
    """For each configuration of a search's record, the shortest safety history it prints."""
    out, cur, take = {}, None, False
    for line in lines(path):
        m = CONFIG.match(line)
        if m:
            cur = (m.group(1).split()[0], m.group(2))
            take = False
            continue
        if line.startswith('    shortest '):
            take = line.startswith('    shortest safety ')
            if take and cur[1] == str(nrep):
                out[cur[0]] = {'detail': line[len('    shortest safety ('):-2], 'ops': []}
            continue
        if take and cur and cur[1] == str(nrep) and line.startswith('        '):
            out[cur[0]]['ops'].append(line.strip())
        elif not line.startswith('        '):
            take = False
    return out


def trace_safety(path):
    """The safety histories of a traced search: [(cfg, h, drawn, shrunk, step, detail)], in order."""
    out, cfg, h = [], None, None
    drawn = None
    for line in lines(path):
        m = CONFIG.match(line)
        if m:
            cfg = m.group(1).split()[0]
            continue
        if line.startswith('    history '):
            h = int(line.split()[1].rstrip(':'))
        elif line.startswith('        drawn:  '):
            drawn = line[len('        drawn:  '):].split(' ; ')
        elif line.startswith('        shrunk: '):
            body, tail = line[len('        shrunk: '):].rsplit('   (safety at step ', 1)
            step, detail = tail[:-1].split(', ', 1)
            out.append((cfg, h, drawn, body.split(' ; '), int(step), detail))
    return out


TRACE_RECORDS = {('whole', 'search'): os.path.join(REC_DEC, 'store-search.txt'),
                 ('whole', 'noauth'): os.path.join(REC_DEC, 'store-search-noauth.txt'),
                 ('rows', 'search'): os.path.join(REC_ROWS, 'store-search.txt'),
                 ('rows', 'acts'): os.path.join(REC_ROWS, 'store-search-acts.txt'),
                 ('rows', 'noauth'): os.path.join(REC_ROWS, 'store-search-noauth.txt')}


def check(run):
    o = out_dir(run)
    ok = True
    emit('== each part with KeePassXC 2.8.0, per profile, against recorded-rows')
    for p in ('deletions', 'clear', 'history'):
        ok &= held(os.path.join(o, 'b-%s-2.8.0.txt' % p), os.path.join(REC_ROWS, 'b-%s.txt' % p), 'b-%s-2.8.0' % p)
    for v in ('', '-off'):
        ok &= held(os.path.join(o, 'b-random-rows%s-2.8.0.txt' % v), os.path.join(REC_ROWS, 'b-random-rows%s.txt' % v),
                   'b-random-rows%s-2.8.0 (the lines the record holds)' % v, prefix=True)
    emit('== the extended model in the whole-rule form against recorded-decided')
    ok &= held(os.path.join(o, 'store-ext-whole.txt'), os.path.join(REC_DEC, 'store-ext.txt'), 'store-ext-whole')
    emit('== the replays in the whole-rule form against the column of recorded\\ with the repair undone')
    a = scen_results(os.path.join(o, 'store-scenarios-whole.txt'), False)
    b = scen_results(os.path.join(REC_FIRST, 'store-scenarios.txt'), True)
    emit('store-scenarios-whole: %d replays, %s' % (len(b), 'the same' if a == b else 'DIFFERS: %r against %r' % (a, b)))
    ok &= a == b
    a = sec6_results(os.path.join(o, 'store-section6-whole.txt'), 'store')
    b = sec6_results(os.path.join(REC_FIRST, 'store-section6.txt'), 'repair')
    emit('store-section6-whole: %d sequences, %s' % (len(b), 'the same' if a == b else 'DIFFERS: %d of %d' % (
        sum(x != y for x, y in zip(a, b)), len(b))))
    ok &= a == b
    emit('== each traced search against its record: the line of each configuration, and the shortest safety history')
    for (form, search), rec in sorted(TRACE_RECORDS.items()):
        for nrep in (2, 3):
            name = 'trace-%s-%s-%d' % (form, search, nrep)
            path = os.path.join(o, name + '.txt')
            if not os.path.exists(path):
                emit('%s: not in out' % name)
                ok = False
                continue
            a, b = config_lines(path, nrep), config_lines(rec, nrep)
            same_lines = a == b
            rs = shortest_of_record(rec, nrep)
            mine = {}
            for cfg, h, drawn, shrunk, step, detail in trace_safety(path):
                if cfg not in mine or len(drawn) < mine[cfg][0]:
                    mine[cfg] = (len(drawn), shrunk, detail)
            same_short = set(rs) == set(mine) and all(rs[c]['ops'] == mine[c][1] and rs[c]['detail'] == mine[c][2]
                                                      for c in rs)
            emit('%s: %d configurations, counts %s; shortest safety history of %d configurations, %s' % (
                name, len(b), 'the same' if same_lines else 'DIFFER', len(rs), 'the same' if same_short else 'DIFFER'))
            if not same_short:
                for c in sorted(set(rs) | set(mine)):
                    emit('   %s: record %r; trace %r' % (c, rs.get(c), mine.get(c)))
            ok &= same_lines and same_short
    sys.exit(0 if ok else 1)


# ------------------------------------------------------------------ second
def second():
    o1, o2 = out_dir('1'), out_dir('2')
    bad = 0
    for name in sorted(os.listdir(REC_CUT)):
        if not name.endswith('.txt') or name == 'times.txt':
            continue
        a, b = os.path.join(o2, name), os.path.join(REC_CUT, name)
        if not os.path.exists(a):
            emit('%s: not in out\\cut-2' % name)
            bad += 1
            continue
        x, y = lines(a), lines(b)
        diffs = [k for k in range(max(len(x), len(y))) if k >= len(x) or k >= len(y) or x[k] != y[k]]
        emit('%s: %d lines, %s' % (name, len(y), 'the same' if not diffs else '%d differ, first at line %d' % (
            len(diffs), diffs[0] + 1)))
        bad += bool(diffs)
    for name in sorted(os.listdir(o1)):
        if name.endswith('.jsonl'):
            with open(os.path.join(o1, name), 'rb') as f1, open(os.path.join(o2, name), 'rb') as f2:
                same = f1.read() == f2.read()
            emit('%s: %s' % (name, 'the same' if same else 'DIFFERS'))
            bad += not same
    r1, r2 = os.path.join(REC_CUT, 'replays'), os.path.join(o2, 'replays')
    for name in sorted(os.listdir(r1)):
        x, y = lines(os.path.join(r2, name)), lines(os.path.join(r1, name))
        emit('replays\\%s: %s' % (name, 'the same' if x == y else 'DIFFERS'))
        bad += x != y
    sys.exit(1 if bad else 0)


# ------------------------------------------------------------------ analyze
KX = re.compile(r'KeePassXC \d+\.\d+\.\d+')
HIST_LABELS = ('copy 0 as it is', 'Synchronize into 1', 'Synchronize into 0', 'KeePassXC into 1', 'KeePassXC into 0',
               'copy 0, the gate writes again')


def classes(path):
    """compare.classes, with the release taken out of a cell's label so that releases compare."""
    out, sec, op = {}, None, None
    for line in lines(path):
        if line.startswith('## '):
            sec, op = line[3:].split(';')[0], None
            continue
        if sec is None:
            continue
        if line.startswith('      ') and not line.startswith('       ') and op is not None:
            label, rest = KX.sub('KeePassXC', line[6:36].strip()), line[36:]
            if label not in HIST_LABELS:
                continue
            rest = rest.replace('cannot write; ', '')
        elif line.startswith('   ') and not line.startswith('    ') and ': ' in line:
            label, rest = line[3:].split(': ', 1)
            label = KX.sub('KeePassXC', label)
            if label.startswith('C ') or label.startswith('the gate wrote'):
                continue
            if rest and rest.split(' ')[0] not in ('as', 'narrower:', 'refuses:', 'WIDER:', 'not', 'Seal', 'cannot'):
                op = label
                continue
        else:
            continue
        c = rest.split(' {')[0].split(' [')[0].split(' (')[0].split(' |')[0].split(':')[0].strip()
        out[(sec, op, label)] = c
    return out


def tally(d):
    t = {}
    for v in d.values():
        t[v] = t.get(v, 0) + 1
    return ', '.join('%s %d' % kv for kv in sorted(t.items()))


def key_text(k):
    return '%s | %s | %s' % (k[0], k[1] or '-', k[2])


def releases(o):
    for p in ('deletions', 'clear', 'history'):
        emit('== b-%s: every cell by KeePassXC release, per profile and in the whole-rule form' % p)
        rows = {rel: classes(os.path.join(o, 'b-%s-%s.txt' % (p, rel))) for rel in RELEASES}
        whole = {rel: classes(os.path.join(o, 'b-%s-%s-whole.txt' % (p, rel))) for rel in RELEASES[:3]}
        whole['2.8.0'] = classes(os.path.join(REC_DEC, 'b-%s.txt' % p))
        for rel in RELEASES:
            emit('   %-7s per profile %d cells: %s' % (rel, len(rows[rel]), tally(rows[rel])))
            emit('   %-7s whole-rule  %d cells: %s%s' % (rel, len(whole[rel]), tally(whole[rel]),
                                                     ' (recorded-decided)' if rel == '2.8.0' else ''))
            for form, d in (('per profile', rows[rel]), ('whole-rule', whole[rel])):
                xc = {k: c for k, c in d.items() if k[2].startswith('KeePassXC')}
                emit('   %-7s %s, the %d cells merged by KeePassXC: %s' % (rel, form, len(xc), tally(xc)))
        for rel in RELEASES:
            x, y = rows[rel], whole[rel]
            common = sorted(set(x) & set(y), key=str)
            pairs = {}
            for k in common:
                pairs[(y[k], x[k])] = pairs.get((y[k], x[k]), 0) + 1
            emit('   %s, whole-rule -> per profile over %d cells in both (%d per profile only): %s' % (
                rel, len(common), len(set(x) - set(y)),
                ', '.join('%s -> %s %d' % (a, b, n) for (a, b), n in sorted(pairs.items()))))
            for k in common:
                if x[k] != y[k]:
                    emit('      %s: whole-rule %s, per profile %s' % (key_text(k), y[k], x[k]))
            only = {k: x[k] for k in set(x) - set(y)}
            if only:
                emit('      per profile only: %s' % tally(only))
        for form, d in (('per profile', rows), ('whole-rule', whole)):
            common = set.intersection(*(set(v) for v in d.values()))
            diff = [k for k in sorted(common, key=str) if len(set(d[rel][k] for rel in RELEASES)) > 1]
            emit('   %s: cells whose class differs between the four releases: %d of %d' % (form, len(diff), len(common)))
            for k in diff:
                emit('      %s: %s' % (key_text(k), ', '.join('%s %s' % (rel, d[rel][k]) for rel in RELEASES)))
            for rel in RELEASES:
                w = [k for k, c in d[rel].items() if c == 'WIDER']
                if w:
                    emit('   %s %s, WIDER: %s' % (form, rel, '; '.join(key_text(k) for k in sorted(w, key=str))))


def random_rows_ops(count, seed, rel):
    """The histories cut.part_random_rows draws (the same generator, the same lists in the same order)."""
    import random
    rng = random.Random(seed)
    out = []
    for h in range(count):
        nrep = 2 if rng.random() < 0.6 else 3
        R = range(nrep)
        acts = []
        for r in R:
            for n in ('root', 'N'):
                acts += [('switch', r, n, x) for x in ('deny', 'inherit')]
                acts += [('row', r, n, p, x) for p in ('pf', 'pg') for x in ('rw', 'r', '', 'off')]
        merges = [m for d in R for s in R if d != s for m in
                  [('merge', 'kp_sync', d, s), ('merge', 'kp_other', d, s, 'OverwriteIfNewer'),
                   ('merge', 'xc', d, s, rel)]]
        ops = []
        for _ in range(rng.randint(4, 10)):
            ops.append(rng.choice(acts if rng.random() < 0.6 else merges))
        out.append((nrep, ops))
    return out


def random_rows(o):
    emit('== b-random-rows: the same 300 histories with each KeePassXC release in its merges')
    for v in ('', '-off'):
        ends, heads = {}, {}
        for rel in RELEASES:
            ls = lines(os.path.join(o, 'b-random-rows%s-%s.txt' % (v, rel)))
            heads[rel] = [x for x in ls[:3]]
            at = ls.index([x for x in ls if x.startswith('== the class of each file')][0])
            ends[rel] = {int(x.split(':')[0]): x.split(': ', 1)[1].split(', ') for x in ls[at + 1:]
                         if x.split(':')[0].strip().isdigit()}
            found = [x for x in ls[3:at] if x.startswith('## ') or x.startswith('   ')]
            emit('   removal %s, %s: %s | %s | %s; listed: %d lines' % (
                'off' if v else 'as decided', rel, heads[rel][0].split(': ', 1)[1].rsplit(',', 1)[0],
                heads[rel][1].strip(), heads[rel][2].strip(), len(found)))
            for x in found:
                emit('      ' + x)
        drawn = random_rows_ops(300, 11, '2.8.0')
        mismatch = [h for h in range(300) if len(ends['2.8.0'][h]) != drawn[h][0]]
        emit('   the draws replayed here hold the number of files of every history the part printed: %s'
            % ('yes, 300 of 300' if not mismatch else 'NO: %r' % mismatch[:10]))
        diff = [h for h in range(300) if len(set(tuple(ends[rel][h]) for rel in RELEASES)) > 1]
        emit('   removal %s: histories whose files end in another class under another release: %d of 300' % (
            'off' if v else 'as decided', len(diff)))
        for h in diff:
            emit('      history %d: %s' % (h, '; '.join('%s %s' % (rel, ', '.join(ends[rel][h])) for rel in RELEASES)))
            emit('         ' + ' ; '.join(' '.join(map(str, op)) for op in drawn[h][1]))


def cells_of_merges(path, hids):
    """b-merges' cells: (history, program, into) -> outcome text without the bridge note."""
    out, hid, label = {}, None, None
    for line in lines(path):
        if line.startswith('## '):
            hid = line[3:].split(':')[0]
            continue
        if hid not in hids or not line.startswith('   ') or line.startswith('   history'):
            continue
        lab, rest = line[3:25].strip(), line[26:]
        if lab:
            label = lab
        if rest.startswith('into '):
            if hid.startswith('T'):
                out[(hid, label, 'both')] = rest
            else:
                d, o = rest[5], rest[8:]
                out[(hid, label, d)] = o.split(' [entries')[0]
    return out


def slow(o):
    emit('== b-slow: the KeePassXC cells of H3, H6, H13 and T2 under the slow key derivation, per profile')
    path = os.path.join(o, 'b-slow.txt')
    for x in lines(path):
        if x.startswith('== tally') or x.startswith('== every cell'):
            emit('   ' + x)
    a = cells_of_merges(path, SLOW + ('T2',))
    b = cells_of_merges(os.path.join(REC_ROWS, 'b-merges.txt'), SLOW + ('T2',))
    b = {k: v for k, v in b.items() if k in a}
    same = [k for k in a if a[k] == b.get(k)]
    emit('   cells: %d; the same outcome, braces included, as the same cell of recorded-rows\\b-merges.txt under'
        ' the fast derivation: %d' % (len(a), len(same)))
    for k in sorted(a):
        if a[k] != b.get(k):
            emit('      %s %s into %s: slow %s; fast %s' % (k[0], k[1], k[2], a[k], b.get(k)))
    t = {}
    for k, v in a.items():
        if k[2] != 'both':
            c = v.split(' {')[0].split(':')[0]
            t[c] = t.get(c, 0) + 1
    emit('   classes of the 24 cells of H3, H6, H13: %s' % ', '.join('%s %d' % kv for kv in sorted(t.items())))


OUTCOME = re.compile(r'^\s+(file \d|read|Seal again): (.*?) \|')


def outcome_classes(path):
    out, sec, n = [], None, 0
    for line in lines(path):
        if line.startswith('## '):
            sec, n = line[3:], 0
            continue
        if line.lstrip().startswith(('repair', 'no repair', 'import with')) and ' [' in line:
            sec = (sec.split(' || ')[0] if sec else '') + ' || ' + line.strip().split(' [')[0]
            continue
        m = OUTCOME.match(line)
        if m:
            out.append(('%s | %s %d' % (sec, m.group(1), n), m.group(2).split(' {')[0].split(':')[0]))
            n += 1
    return out


def line_diff(a, b, label, show=12):
    x, y = lines(a), lines(b)
    diffs = [k for k in range(max(len(x), len(y))) if k >= len(x) or k >= len(y) or x[k] != y[k]]
    emit('   %s: %d lines per profile, %d in the whole-rule record; %d lines differ' % (label, len(x), len(y), len(diffs)))
    for k in diffs[:show]:
        emit('      line %d, whole-rule: %s' % (k + 1, y[k] if k < len(y) else '(none)'))
        emit('      line %d, per profile: %s' % (k + 1, x[k] if k < len(x) else '(none)'))
    if len(diffs) > show:
        emit('      and %d lines more' % (len(diffs) - show))


def others(o):
    emit('== b-dedupe, b-dupes, b-repair: each outcome per profile against the same outcome of recorded-decided')
    for p in ('dedupe', 'dupes', 'repair'):
        x = outcome_classes(os.path.join(o, 'b-%s.txt' % p))
        y = outcome_classes(os.path.join(REC_DEC, 'b-%s.txt' % p))
        keys = [k for k, _ in y]
        dx, dy = dict(x), dict(y)
        pairs = {}
        for k in keys:
            pairs[(dy[k], dx.get(k))] = pairs.get((dy[k], dx.get(k)), 0) + 1
        emit('   b-%s: %d outcomes in the record, %d per profile; whole-rule -> per profile: %s' % (
            p, len(y), len(x), ', '.join('%s -> %s %d' % (a, b, n) for (a, b), n in sorted(pairs.items(), key=str))))
        for k in keys:
            if dy[k] != dx.get(k):
                emit('      %s: whole-rule %s, per profile %s' % (k, dy[k], dx.get(k)))
        line_diff(os.path.join(o, 'b-%s.txt' % p), os.path.join(REC_DEC, 'b-%s.txt' % p), 'b-%s, line by line' % p)
    emit('== b-growth per profile against recorded-decided')
    line_diff(os.path.join(o, 'b-growth.txt'), os.path.join(REC_DEC, 'b-growth.txt'), 'b-growth', show=10)
    emit('== b-random: the merges its histories draw')
    for x in lines(os.path.join(o, 'b-random-draws.txt')):
        if x.startswith('== the merges'):
            emit('   ' + x[3:])


def model_parts(o):
    emit('== store-ext, the extended model: per profile against the whole-rule form of this run')
    line_diff(os.path.join(o, 'store-ext.txt'), os.path.join(o, 'store-ext-whole.txt'), 'store-ext', show=40)
    emit('== the replays: per profile against the whole-rule form of this run')
    line_diff(os.path.join(o, 'store-scenarios.txt'), os.path.join(o, 'store-scenarios-whole.txt'), 'store-scenarios',
              show=20)
    line_diff(os.path.join(o, 'store-section6.txt'), os.path.join(o, 'store-section6-whole.txt'), 'store-section6',
              show=60)


def load_trace(path):
    out = {}
    with open(path, encoding='utf-8') as fh:
        for line in fh:
            d = json.loads(line)
            out[(d['cfg'], d['nrep'], d['h'])] = (d['ops'], [tuple(f) for f in d['fails']])
    return out


def item2(o):
    emit('== item 2: the search of the store as decided, per profile against the whole-rule form, history by'
        ' history (20,000 histories a configuration)')
    rep_dir = os.path.join(o, 'replays')
    os.makedirs(rep_dir, exist_ok=True)
    for nrep in (2, 3):
        R = load_trace(os.path.join(o, 'trace-rows-search-%d.jsonl' % nrep))
        W = load_trace(os.path.join(o, 'trace-whole-search-%d.jsonl' % nrep))
        keys = sorted(set(R) | set(W), key=lambda k: (k[0], k[2]))
        props = {}
        for k in keys:
            a = sorted(set(f[0] for f in W.get(k, (None, []))[1]))
            b = sorted(set(f[0] for f in R.get(k, (None, []))[1]))
            for p in set(a) | set(b):
                if (p in a) != (p in b):
                    props.setdefault(p, {}).setdefault('whole only' if p in a else 'per profile only', []).append(k)
        emit('   %d files: histories with a property counted, whole-rule %d, per profile %d; histories in which a'
            ' property is counted in a form alone, by property:' % (nrep, len(W), len(R)))
        for p in sorted(props):
            emit('      %s: %s' % (p, ', '.join('%s %d' % (s, len(v)) for s, v in sorted(props[p].items()))))
        others = sorted(set(k for p, v in props.items() if p != 'safety' for ks in v.values() for k in ks),
                        key=lambda k: (k[0], k[2]))
        if others:
            emit('   %d files: where the forms part in each history in which a property other than safety is counted in'
                 ' a form alone (replays in replays\\):' % nrep)
        for k in others:
            ops = (R.get(k) or W.get(k))[0]
            applied = {}
            for form in ('whole', 'rows'):
                txt = replay(rep_dir, 'o-%s-%d-%d' % (k[0], nrep, k[2]), k[0], nrep, ops, form)
                applied[form] = {int(x.split()[2].rstrip(':')): 'does not apply' not in x.split('; run finds')[0]
                                 for x in lines(txt) if x.startswith('-- step')}
            parting = [s for s in sorted(set(applied['whole']) & set(applied['rows']))
                       if applied['whole'][s] != applied['rows'][s]]
            emit('      %s/%d #%d: whole-rule %s; per profile %s; %s' % (
                k[0], nrep, k[2], ', '.join(sorted(set(f[0] for f in W.get(k, (None, []))[1]))) or 'nothing',
                ', '.join(sorted(set(f[0] for f in R.get(k, (None, []))[1]))) or 'nothing',
                'the forms part at step %d (%s), which applies in the %s form alone' % (
                    parting[0], ' '.join(map(str, ops[parting[0]])),
                    'whole-rule' if applied['whole'][parting[0]] else 'per-profile') if parting
                else 'every step applies alike in both forms'))
        sk = sorted(set(props.get('safety', {}).get('whole only', [])) | set(props.get('safety', {}).get('per profile only', [])))
        both = [k for k in keys if k in R and k in W and
                [f for f in R[k][1] if f[0] == 'safety'] and [f for f in W[k][1] if f[0] == 'safety'] and
                [f for f in R[k][1] if f[0] == 'safety'] != [f for f in W[k][1] if f[0] == 'safety']]
        emit('   %d files: histories that fail safety in a form alone: %d; that fail it in both at another step or'
            ' file: %d' % (nrep, len(sk), len(both)))
        for k in sk + both:
            ops = (R.get(k) or W.get(k))[0]
            emit('   ## configuration %s, %d files, history %d: %s' % (k[0], nrep, k[2], ' ; '.join(
                ' '.join(map(str, op)) for op in ops)))
            emit('      whole-rule: %s' % ('; '.join('%s at step %d (%s)' % f for f in W.get(k, (None, []))[1]) or 'nothing counted'))
            emit('      per profile: %s' % ('; '.join('%s at step %d (%s)' % f for f in R.get(k, (None, []))[1]) or 'nothing counted'))
            applied = {}
            for form in ('whole', 'rows'):
                txt = replay(rep_dir, 'h-%s-%d-%d' % (k[0], nrep, k[2]), k[0], nrep, ops, form)
                steps = [x for x in lines(txt) if x.startswith('-- step')]
                applied[form] = {int(x.split()[2].rstrip(':')): 'does not apply' not in x.split('; run finds')[0]
                                 for x in steps}
                emit('      replay %s: %d steps played; %s' % (form, len(steps), lines(txt)[-1]))
                wider_every = [x.strip() for x in lines(txt) if x.strip().startswith('every decision:') and
                               not x.rstrip().endswith('wider: no')]
                emit('         wider than every decision any gate made, at any step: %s' % (
                    '; '.join(sorted(set(w.split('wider: ')[1] for w in wider_every))) or 'never'))
            parting = [s for s in sorted(set(applied['whole']) & set(applied['rows']))
                       if applied['whole'][s] != applied['rows'][s]]
            if parting:
                s = parting[0]
                emit('      the forms part at step %d (%s): it applies in the %s form alone' % (
                    s, ' '.join(map(str, ops[s])), 'whole-rule' if applied['whole'][s] else 'per-profile'))
            # the control: where the forms part at a rule act, that act taken out and both forms played
            # again
            drop = parting[:1] if parting and ops[parting[0]][0] == 'rule' else []
            if drop:
                rest = [op for s, op in enumerate(ops) if s not in drop]
                ends = []
                for form in ('whole', 'rows'):
                    txt = replay(rep_dir, 'h-%s-%d-%d-control' % (k[0], nrep, k[2]), k[0], nrep, rest, form)
                    ends.append(lines(txt)[-1].split(': ', 1)[1])
                emit('      control, the rule act%s at step %s taken out: whole-rule %s; per profile %s; %s' % (
                    '' if len(drop) == 1 else 's', ', '.join(map(str, drop)), ends[0], ends[1],
                    'the same' if ends[0] == ends[1] else 'DIFFERENT'))


def replay(rep_dir, name, cfg, nrep, ops, form):
    """search_replay.py over a history; answers the path of what it printed."""
    spec = os.path.join(rep_dir, name + '.json')
    with open(spec, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'cfg': cfg, 'nrep': nrep, 'ops': ops, 'search': 'search'}, fh)
    txt = os.path.join(rep_dir, '%s-%s.txt' % (name, form))
    with open(txt, 'w', encoding='utf-8', newline='\n') as fh:
        subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(FAMILY, 'model', 'search_replay.py'),
                        form, spec], stdout=fh, check=True)
    return txt


AUTH = re.compile(r"^(del \d \('auth',\) \d|edit \d \('auth',\))$")
HEAD = re.compile(r"^(del \d \('head', '(new|old)'\) \d|edit \d \('head', '(new|old)'\))$")
REFUSALS = (("('ref', 'revoked')", 'a revocation'), ("('ref', 'retired')", 'a retirement'),
            ("('prem',)", 'a name removal'), ("('ver', 'renew:", 'a renewal answer'), ("('ver', 'rule:", 'a rule version'))


def kind_of(shrunk, detail):
    acts = set(op.split()[0] for op in shrunk)
    auth = any(AUTH.match(op) for op in shrunk)
    head = any(HEAD.match(op) for op in shrunk)
    ref = sorted(set(name for op in shrunk if op.split()[0] in ('del', 'edit') for sel, name in REFUSALS if sel in op))
    rows = bool(acts & {'row', 'switch', 'drop'})
    if auth and 'create' in acts:
        k = 'the authority\'s entry deleted or edited, then Create (a store key lost)'
    elif head and ref:
        k = '%s deleted with a summary entry' % ' and '.join(ref)
    else:
        k = 'other: ' + ', '.join(sorted(acts))
    what = detail.split(': ', 1)[1] if ': ' in detail else detail
    what = re.sub(r'c\d+', 'c', re.sub(r'(node \S+) grants .*', r'\1 grants', what))
    return k, what, rows


def item3(o):
    emit('== item 3: every safety failure of the model\'s searches, each history shrunk as `search` shrinks its'
        ' shortest, by kind')
    for (form, search) in sorted(TRACE_RECORDS):
        kinds, short_kinds, total, nrows = {}, set(), 0, 0
        for nrep in (2, 3):
            path = os.path.join(o, 'trace-%s-%s-%d.txt' % (form, search, nrep))
            hs = trace_safety(path)
            shortest = {}
            for cfg, h, drawn, shrunk, step, detail in hs:
                if cfg not in shortest or len(drawn) < len(shortest[cfg][2]):
                    shortest[cfg] = (cfg, h, drawn)
            n_fail = sum(int(re.search(r'safety x(\d+)', x).group(1)) for x in config_lines(path, nrep)
                         if 'safety x' in x)
            total += n_fail
            for cfg, h, drawn, shrunk, step, detail in hs:
                k, what, rows = kind_of(shrunk, detail)
                kinds.setdefault((k, what), []).append('%s/%d #%d%s' % (cfg, nrep, h, ' (shortest)' if shortest[cfg][1] == h else ''))
                nrows += rows
                if shortest[cfg][1] == h:
                    short_kinds.add((k, what))
        nh = sum(len(v) for v in kinds.values())
        emit('   %s, %s: %d failures of safety counted, in %d histories; %d of them shrink to a history with an act'
            ' on a row or on the switch' % (form, search, total, nh, nrows))
        for (k, what), v in sorted(kinds.items()):
            emit('      %s; wider: %s; %d histories%s: %s' % (k, what, len(v), '' if (k, what) in short_kinds
                                                             else ', NOT SHOWN BY A SHORTEST HISTORY', ', '.join(v)))
    emit('   every history that shrinks to a kind named "other", or holds an act on a row or on the switch; for such a'
         ' history of a kind other than a store key lost, the same history with each act on a row made the act on the'
         ' node\'s whole rule with the same letters, replayed in the whole-rule form (replays in replays\\):')
    rep_dir = os.path.join(o, 'replays')
    os.makedirs(rep_dir, exist_ok=True)
    for (form, search) in sorted(TRACE_RECORDS):
        for nrep in (2, 3):
            for cfg, h, drawn, shrunk, step, detail in trace_safety(os.path.join(o, 'trace-%s-%s-%d.txt' % (form, search, nrep))):
                k, what, rows = kind_of(shrunk, detail)
                if not (k.startswith('other') or rows):
                    continue
                emit('      %s %s %s/%d #%d: %s (%s)' % (form, search, cfg, nrep, h, ' ; '.join(shrunk), detail))
                if k.startswith('the authority'):
                    continue
                ops = [parse_op(x) for x in shrunk]
                if any(op[0] in ('switch', 'drop') for op in ops):
                    emit('         no whole-rule counterpart: the history holds an act on the switch')
                    continue
                whole = [('rule', op[1], op[2], {'rw': 'rw', 'r': 'r', '': 'none', 'off': 'inherit'}[op[4]])
                         if op[0] == 'row' else op for op in ops]
                name = 'k-%s-%s-%d-%d' % (search, cfg, nrep, h)
                a = lines(replay(rep_dir, name, cfg, nrep, ops, 'rows'))[-1].split(': ', 1)[1]
                b = lines(replay(rep_dir, name + '-whole-rule', cfg, nrep, whole, 'whole'))[-1].split(': ', 1)[1]
                emit('         per profile: %s; whole-rule, %s: %s' % (a, ' ; '.join(' '.join(map(str, op)) for op in whole), b))


def parse_op(s):
    """An act as search_trace.py prints it, back into the tuple it was."""
    import ast
    out = []
    for t in re.findall(r"\(.*?\)|\S+", s):
        out.append(ast.literal_eval(t) if t.startswith('(') else int(t) if t.isdigit() else t)
    if out[0] == 'row' and len(out) == 4:     # a row emptied prints its empty letters as nothing
        out.append('')
    return tuple(out)


def analyze(run):
    o = out_dir(run)
    path = os.path.join(o, 'analysis.txt')
    stdout = sys.stdout
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        sys.stdout = fh
        try:
            releases(o)
            random_rows(o)
            slow(o)
            others(o)
            model_parts(o)
            item2(o)
            item3(o)
        finally:
            sys.stdout = stdout
    print('wrote ' + path)


def main():
    sys.stdout.reconfigure(line_buffering=True)
    if sys.argv[1] == 'check':
        check(sys.argv[2])
    elif sys.argv[1] == 'second':
        second()
    elif sys.argv[1] == 'analyze':
        analyze(sys.argv[2])
    else:
        raise SystemExit('compare_cut.py check RUN | second | analyze RUN')


if __name__ == '__main__':
    main()
