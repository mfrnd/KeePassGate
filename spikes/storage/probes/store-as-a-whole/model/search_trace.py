"""Items 2 and 3 of the report `measured-rules-cut-parts.md`: the model's searches of the store as
decided, traced history by history. ADDED (cut parts): a file of its own, so that nothing of
crdt_model.py or store_runs.py changes.

`crdt_model.search` prints, for each property, its count and the shortest history that failed it.
This draws the same histories from the same generators (the configurations of crdt_model.CONFIGS and
the seeds of store_runs.py) and runs each through the same `crdt_model.run`, so it counts what
`search` counts; it keeps every history in which a property was counted, and shrinks each history
that failed `safety` as `search` shrinks its shortest.

Run:  uv run --no-project python -I -X utf8 search_trace.py FORM SEARCH NREP JSONL [COUNT]

COUNT histories a configuration, 20,000 by default as `search` draws. A JSON line per history in
which a property was counted goes to JSONL. Printed: for each configuration the line `search`
prints; then each history that failed `safety`, its index, what failed, the history, and the history
shrunk with what it fails. search_replay.py plays a single history step by step.

FORM    rows: a rule as a record for each node and profile, a removed row written as decided
        (`store_runs.py --rows empty`); whole: a version of a node's whole rule (`store_runs.py
        --decided`).
SEARCH  search: store_runs.py's `search`; acts: the same with the acts on a row and on the switch in
        the alphabet (rows only); noauth: `search-noauth`, with no hand deletion or edit of the
        authority's entry; per profile with the acts on a row and on the switch, as `run.ps1 -Rows`
        runs it.
"""
import json
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402
import store_runs as SR  # noqa: E402


def tup(x):
    return tuple(tup(y) for y in x) if isinstance(x, list) else x


def fmt(op):
    return ' '.join(str(x) for x in op)


def set_form(form, search):
    if form == 'rows':
        M.ROWS, M.PP = 'empty', True
        M.ROW_ACTS = search in ('acts', 'noauth')
    elif form == 'whole':
        if search == 'acts':
            raise SystemExit('search_trace.py: the acts on a row exist per profile only')
    else:
        raise SystemExit('search_trace.py: FORM is rows or whole')
    if search == 'noauth':
        M.alphabet = SR.without_authority_losses()[1]
    elif search not in ('search', 'acts'):
        raise SystemExit('search_trace.py: SEARCH is search, acts or noauth')
    return M.STORE.but(repair=False)


def tag_of(name):
    return name.split()[0]


def trace(name, cfg, nrep, count, length, seed, flags, fh):
    rng = random.Random(seed)
    acts, merges, faults = M.alphabet(nrep, cfg, flags)
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    counts, safety = {}, []
    for h in range(count):
        ops = []
        for _ in range(rng.randint(3, length)):
            x = rng.random()
            pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            ops.append(rng.choice(pool))
        once, kept = set(), []
        for f in M.run(ops, nrep, flags):
            if not M.ends(f[0]):
                if f[0] in once:
                    continue
                once.add(f[0])
            counts[f[0]] = counts.get(f[0], 0) + 1
            kept.append(list(f))
        if kept:
            fh.write(json.dumps({'cfg': tag_of(name), 'nrep': nrep, 'h': h, 'ops': ops, 'fails': kept}) + '\n')
        if any(f[0] == 'safety' for f in kept):
            safety.append((h, ops, [f for f in kept if f[0] == 'safety']))
    print('%-66s %d files %6d histories  %s' % (name, nrep, count, 'no failure' if not counts else ', '.join(
        '%s x%d' % kv for kv in sorted(counts.items()))))
    for h, ops, fs in safety:
        small = M.shrink(ops, nrep, flags, 'safety')
        detail = [x for x in M.run(small, nrep, flags) if x[0] == 'safety'][0]
        print('    history %d: %s' % (h, '; '.join('safety at step %d (%s)' % (f[1], f[2]) for f in fs)))
        print('        drawn:  ' + ' ; '.join(fmt(op) for op in ops))
        print('        shrunk: ' + ' ; '.join(fmt(op) for op in small) + '   (safety at step %d, %s)' % (detail[1], detail[2]))


def main():
    sys.stdout.reconfigure(line_buffering=True)
    form, search, nrep, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    count = int(sys.argv[5]) if len(sys.argv) > 5 else 20000
    flags = set_form(form, search)
    print('== the store as decided, %s; the search %s, %d files; every history traced' % (
        'a rule as a record for each node and profile, a removed row written as empty' if form == 'rows'
        else 'a version of a node\'s whole rule', search, nrep))
    t0 = time.time()
    with open(out, 'w', encoding='utf-8', newline='\n') as fh:
        for k, (name, cfg) in enumerate(M.CONFIGS):
            trace(name, cfg, nrep, count, 9, 100 + k + 10 * nrep, flags, fh)
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
