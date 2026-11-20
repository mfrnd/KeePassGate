"""A single history of the model's searches, step by step, in either form, for item 2 of the report
`measured-rules-cut-parts.md`. ADDED (cut parts): a file of its own.

Run:  uv run --no-project python -I -X utf8 search_replay.py FORM JSON

JSON is a file {"cfg": tag, "nrep": n, "ops": [...], "search": "search"}, a history as
search_trace.py writes it. FORM is `rows` or `whole`, as search_trace.py takes it. Printed, at the
start and after each step: the act, whether it applied, what `crdt_model.run` finds at that step
(with no convergence check), and for each file what it serves, admits and grants; what its own
oracle grants (what was delivered to that file, the oracle of `safety`); what every decision any gate
made grants (store_runs.global_ideal, the oracle of the reports on files); whether the file is wider
than either; and the entries it holds. At the end, every failure `run` finds with the convergence
check, as the search runs it.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402
import search_trace as T  # noqa: E402
import store_runs as SR  # noqa: E402


def stable(x):
    """A value as text with every set and dict in sorted order: a set's own order follows the
    hashes of its strings, which differ from process to process."""
    if isinstance(x, (set, frozenset)):
        return '{' + ', '.join(sorted(stable(y) for y in x)) + '}'
    if isinstance(x, dict):
        items = sorted(x.items(), key=lambda kv: repr(kv[0]))
        return '{' + ', '.join('%s: %s' % (stable(k), stable(v)) for k, v in items) + '}'
    if isinstance(x, tuple):
        return '(' + ', '.join(stable(y) for y in x) + (',' if len(x) == 1 else '') + ')'
    if isinstance(x, list):
        return '[' + ', '.join(stable(y) for y in x) + ']'
    return repr(x)


def entry_text(W, eid):
    e = W.EL[eid]
    d = e.data
    if e.kind == 'ver' and isinstance(d, tuple) and d and d[0] in ('rows', 'sws', 'const', 'cap', 'pp'):
        if d[0] in ('rows', 'sws'):
            val = M.row_text(d)
        elif d[0] == 'pp':
            val = 'grants ' + (M.words_text(d[1]) or '-') + ' passes ' + (M.words_text(d[2]) or '-')
        else:
            val = '%s %s' % (d[0], ''.join(sorted(d[1])) or '-')
    else:
        val = stable(d) if d is not None else ''
    return '%s[%s %s%s%s]' % (eid, e.kind, e.obj, (' ' + val) if val else '',
                              (' after ' + ','.join(sorted(e.after))) if e.after else '')


def words(v):
    return {n: M.words_text(w) for n, w in sorted(v.items())}


def show(W, i):
    v = W.fold(W.reps[i])
    own = W.ideal(i)
    every = SR.global_ideal(W, i)
    nodes = W.reps[i].nodes
    out = ['  file %d: %s, served %s, admitted %s, words %s, renewals %s, names %s' % (
        i, v['status'], v['served'], {c: sorted(p) for c, p in sorted(v['adm'].items())}, words(v['words']),
        dict(sorted(v['renew'].items())), sorted(v['names']))]
    out.append('    its own oracle: words %s, valid %s; wider: %s' % (
        words(own['words_raw']), {c: sorted(p) for c, p in sorted(own['valid'].items())},
        M.narrower(v, own, nodes) or 'no'))
    out.append('    every decision: served %s, words %s, valid %s; wider: %s' % (
        every['served'], words(every['words_raw']), {c: sorted(p) for c, p in sorted(every['valid'].items())},
        M.narrower(v, every, nodes) or 'no'))
    rep = W.reps[i]
    held = []
    for u, (eid, mt, ok) in sorted(rep.entries.items()):
        held.append(('' if ok else 'DAMAGED ') + ('' if u == eid else 'copy %s of ' % u) + entry_text(W, eid))
    out.append('    holds: ' + ' ; '.join(held))
    if rep.dels:
        out.append('    deletion records: ' + ', '.join('%s@%d' % kv for kv in sorted(rep.dels.items())))
    return out


def main():
    sys.stdout.reconfigure(line_buffering=True)
    form, path = sys.argv[1], sys.argv[2]
    with open(path, encoding='utf-8') as fh:
        spec = json.load(fh)
    flags = T.set_form(form, spec.get('search', 'search'))
    cfg = dict((T.tag_of(n), c) for n, c in M.CONFIGS)[spec['cfg']]
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    nrep, ops = spec['nrep'], [T.tup(op) for op in spec['ops']]
    print('== %s, configuration %s, %d files: %s' % (form, spec['cfg'], nrep, ' ; '.join(T.fmt(op) for op in ops)))
    W = M.World(nrep, flags)
    print('-- the start')
    for i in range(nrep):
        for x in show(W, i):
            print(x)
    seen = 0
    for k, op in enumerate(ops):
        ok = W.step(op)
        fails = M.run(ops[:k + 1], nrep, flags, check_conv=False)
        new = fails[seen:]
        seen = len(fails)
        print('-- step %d: %s%s; run finds: %s' % (k, T.fmt(op), '' if ok else ' (does not apply)',
                                                   '; '.join('%s (%s)' % (f[0], f[2]) for f in new) or 'nothing'))
        for i in range(nrep):
            for x in show(W, i):
                print(x)
        if any(M.ends(f[0]) for f in fails):
            print('-- run ends here')
            break
    print('== every failure of run, with the convergence check: %s' % (
        '; '.join('%s at step %d (%s)' % tuple(f) for f in M.run(ops, nrep, flags)) or 'none'))


if __name__ == '__main__':
    main()
