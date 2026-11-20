"""The model's parts cut from the run of rules per profile (`run.ps1 -Rows`): the extended model of the
survival critique and the replays, on the store as decided, in either form. ADDED (cut parts): a file
of its own, so that nothing of store_runs.py changes.

Run:  uv run --no-project python -I -X utf8 cut_runs.py FORM PART [COUNT]

FORM  `rows`: a rule as a record for each node and profile, a removed row written as decided
      (crdt_model.ROWS 'empty', words per profile), as `store_runs.py --rows empty` runs it;
      `whole`: a version of a node's whole rule, as `store_runs.py --decided` runs it.
PART  `ext [COUNT]`: store_runs.part_ext, 8,000 histories a cell by default, as run.ps1 gives it;
      `scenarios`: the design's 20 replays (store_runs.part_scenarios);
      `section6`: the sequences of EVALUATION.md section 6 (store_runs.part_section6).

The store is the store as decided (no repair after a Synchronize). The replays are run in the column
of that store alone: the first record's columns with a decision undone hold the repair, which reads
no row (`World.repair`). Two things are put between the parts and the model's answer, both of which
leave the whole-rule form as it prints:
- a replay's predicate reads a node's words as letters (`frozenset('r')`); per profile, the words
  of a node that gives every profile the same letters are given to it as those letters, and the
  words of a node that gives the profiles different letters as they are, so that the predicate
  fails there as it would on letters that are not its own;
- the view of section 6 prints a node's words as `words_text` does, which in the whole-rule form is
  the text the first record printed.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402
import store_runs as SR  # noqa: E402


def plain(v):
    """A view whose words are letters wherever every profile holds the same letters."""
    if not M.PP:
        return v
    words = {}
    for n, w in v['words'].items():
        per = {p: frozenset(x[-1] for x in w if x.startswith(p + '.')) for p in M.PROFS}
        words[n] = per[M.PROFS[0]] if len(set(per.values())) == 1 else w
    return dict(v, words=words)


def view(W, i):
    v = W.fold(W.reps[i])
    return 'file %d: %s, served %s, admitted %s, words %s, renewals %s, names %s' % (
        i, v['status'], v['served'], sorted(v['adm']), {n: M.words_text(w) for n, w in sorted(v['words'].items())},
        dict(v['renew']), sorted(v['names']))


def main():
    form, part = sys.argv[1], sys.argv[2]
    n = int(sys.argv[3]) if len(sys.argv) > 3 else (8000 if part == 'ext' else 20000)
    if form == 'rows':
        M.ROWS, M.PP = 'empty', True
        print('== the store as decided, a rule stored as a record for each node and profile, a removed row'
              ' written as empty')
    elif form == 'whole':
        print('== the store as decided: no repair after a Synchronize')
    else:
        raise SystemExit('cut_runs.py rows|whole PART [COUNT]')
    SR.STORE = M.STORE.but(repair=False)
    SR.DECISIONS = []
    SR.view = view
    M.SCENARIOS = [(name, src, nrep, ops, i, (lambda v, ok=ok: ok(plain(v))))
                   for name, src, nrep, ops, i, ok in M.SCENARIOS]
    sys.stdout.reconfigure(line_buffering=True)
    t0 = time.time()
    {'ext': SR.part_ext, 'scenarios': SR.part_scenarios, 'section6': SR.part_section6}[part](n)
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
