"""The parts cut from the run of rules per profile (`run.ps1 -Rows`), for the report
`measured-rules-cut-parts.md`. ADDED (cut parts): a file of its own, so that nothing of the parts it
calls changes.

Run:  uv run --no-project python -I -X utf8 cut.py (--rows VARIANT | --decided) --run TAG PART [ARGS]

`--rows VARIANT` is the configuration of `cases.py --rows VARIANT` (a rule as a record for each node
and profile); `--decided` is that of `cases.py --decided`, the store as decided with a version of a
node's whole rule, the whole-rule form. `--run TAG` puts the databases under
`work\\<prefix>r<TAG>-[<release>-]<tag>`, so that two runs of the same part can go at once.

Parts:
  deletions REL     cases.part_deletions, with keepassxc-cli REL in place of 2.8.0
  clear REL         cases.part_clear, the same
  history REL       cases.part_history, the same (its merge table replaced)
  random-rows REL COUNT SEED
                    cases.part_random_rows, the same; then the class of each file at the end of each
                    history, so that two releases can be compared history by history
  draws COUNT SEED  the histories `cases.py random COUNT SEED` draws, counted by merge program and
                    release; no file is made
  slow, dedupe, dupes, repair
                    the parts of cases.py of those names, unchanged
  growth            growth.py, unchanged, its databases under `work\\r<TAG>`

The functions copied below are cases.py's with the release a parameter; with REL 2.8.0 each prints
what cases.py prints, line for line.
"""
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402

RELEASE_PARTS = ('deletions', 'clear', 'history', 'random-rows')


# cases.part_deletions with the release a parameter
def part_deletions(host, rel):
    print('== B.5: each kind of record deleted by hand at copy 0, each way; then what the read rule')
    print('   answers; then Seal again (without the mark and the witness) when the store is')
    print('   incomplete, and what the store then holds; then a merge with copy 1, a stale copy that')
    print('   still holds what was deleted, both ways, by Synchronize and by KeePassXC %s.' % rel)
    hist = [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
            ('rule', 0, 'N', 'none'), ('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0)]
    print('   history: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
    for sel in C.KINDS:
        for way, words in C.WAYS:
            row = []
            for mi, (d, s, prog) in enumerate(((0, 1, 'Synchronize'), (1, 0, 'Synchronize'),
                                                (0, 1, rel), (1, 0, rel))):
                b = B.Bridge(host, 2, 'del')
                ops = list(hist)
                if way == 'bin':
                    ops.append(('bin', 0, sel))
                elif way == 'rec':
                    ops.append(('del', 0, sel, 1))
                elif way == 'none':
                    ops.append(('del', 0, sel, 0))
                else:
                    ops += [('bin', 0, sel), ('empty', 0)]
                for k, op in enumerate(ops):
                    if not b.step(k, op):
                        b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
                o1, r1 = B.outcome(b, 0)
                sealed = ''
                if r1['status'] != 'ok':
                    k = len(ops)
                    if b.step(k, ('reseal', 0)):
                        o2, r2 = B.outcome(b, 0)
                        sealed = '%s (holds %d elements, %d heads)' % (o2, r2['counts']['elements'], r2['counts']['heads'])
                    else:
                        sealed = 'Seal again does not apply'
                    ops.append(('reseal', 0))
                k = len(ops)
                b.step(k, C.merge_op(prog, d, s))
                o3, r3 = B.outcome(b, d)
                if mi == 0:
                    row.append('after the deletion: %s' % o1)
                    row.append('Seal again: %s' % (sealed or 'not needed'))
                row.append('%s into %d: %s [%s]' % ('Synchronize' if prog == 'Synchronize' else 'KeePassXC ' + rel, d, o3,
                                                    C.c_note(b)))
                for x in b.disagree:
                    row.append('   C entries, step %d, file %d: %s' % (x[0], x[1], '; '.join(x[2])))
                for x in b.readdiff:
                    row.append('   C read, step %d, file %d: %s' % x)
                if o3.startswith('WIDER') or o1.startswith('WIDER'):
                    row.append('   ' + B.view(r3))
                    row += ['   ' + x for x in b.log]
            print('## %s, %s' % (' '.join(sel), words))
            for x in row:
                print('   ' + x)


# cases.part_clear with the release a parameter
def part_clear(host, rel):
    merges = [(0, 1, 'Synchronize'), (1, 0, 'Synchronize'), (0, 1, 'OverwriteIfNewer'), (0, 1, rel), (1, 0, rel)]
    print('== Added from the desktop measurement: every deletion record removed at once (Database')
    print('   Maintenance, Deleted objects information, Delete), then the read, then a merge with a stale')
    print('   copy that still holds what was deleted: Synchronize both ways, Overwrite if newer into 0,')
    print('   KeePassXC %s both ways. No Seal again before the merge.' % rel)
    hist = [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
            ('rule', 0, 'N', 'none'), ('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0)]
    print('## after each kind of deletion of B.5; history: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
    for sel in C.KINDS:
        for way, words in C.WAYS:
            dele = {'bin': [('bin', 0, sel)], 'rec': [('del', 0, sel, 1)], 'none': [('del', 0, sel, 0)],
                    'empty': [('bin', 0, sel), ('empty', 0)]}[way]
            row = []
            for mi, (d, s, prog) in enumerate(merges):
                b = C.run_ops(host, 2, 'clr', hist + dele + [('clear', 0), C.merge_op(prog, d, s)])
                if mi == 0:
                    b0 = C.run_ops(host, 2, 'clr0', hist + dele + [('clear', 0)])
                    row.append('after the deletion and the clearing: %s' % B.outcome(b0, 0)[0])
                o, r = B.outcome(b, d)
                row.append('%s into %d: %s [%s]' % (prog if prog in B.METHODS else 'KeePassXC ' + prog, d, o, C.c_note(b)))
                row += C.c_lines(b)
                if o.startswith('WIDER'):
                    row.append('   ' + B.view(r))
                    row += ['   ' + x for x in b.log]
            print('## %s, %s, then every record removed' % (' '.join(sel), words))
            for x in row:
                print('   ' + x)
    print('## after a prune: copy 1 taken before the act, the act at copy 0, every record removed there')
    for pid, what, pre, act in C.PRUNES:
        row = []
        base = pre + [('merge', 'kp_sync', 1, 0)] + act
        b0 = C.run_ops(host, 2, 'clr0', base + [('clear', 0)])
        row.append('after the clearing: %s' % B.outcome(b0, 0)[0])
        for d, s, prog in merges:
            b = C.run_ops(host, 2, 'clr', base + [('clear', 0), C.merge_op(prog, d, s)])
            o, r = B.outcome(b, d)
            row.append('%s into %d: %s [%s]' % (prog if prog in B.METHODS else 'KeePassXC ' + prog, d, o, C.c_note(b)))
            row += C.c_lines(b)
            if o.startswith('WIDER'):
                row.append('   ' + B.view(r))
                row += ['   ' + x for x in b.log]
        print('## %s: %s; history: %s' % (pid, what, ' ; '.join(' '.join(map(str, op)) for op in base)))
        for x in row:
            print('   ' + x)


# cases.part_history with its merge table replaced
def part_history(host, rel):
    C.HIST_MERGES = [('Synchronize into 1', 1, 0, 'Synchronize'), ('Synchronize into 0', 0, 1, 'Synchronize'),
                     ('KeePassXC %s into 1' % rel, 1, 0, rel), ('KeePassXC %s into 0' % rel, 0, 1, rel)]
    C.part_history(host)


# cases.part_random_rows with the release a parameter, and the class of each file at the end of each
# history printed after it
def part_random_rows(host, count, seed, rel):
    rng = random.Random(seed)
    t0 = time.time()
    outs, beyonds, lines, nsteps, ndis, nread = {}, 0, [], 0, 0, 0
    ends = []
    for h in range(count):
        nrep = 2 if rng.random() < 0.6 else 3
        R = range(nrep)
        acts = []
        for r in R:
            for n in ('root', 'N'):
                acts += [('switch', r, n, x) for x in ('deny', 'inherit')]
                acts += [('row', r, n, p, x) for p in ('pf', 'pg') for x in C.ROW_SPECS]
        merges = [m for d in R for s in R if d != s for m in
                  [('merge', 'kp_sync', d, s), ('merge', 'kp_other', d, s, 'OverwriteIfNewer'),
                   ('merge', 'xc', d, s, rel)]]
        ops = []
        for _ in range(rng.randint(4, 10)):
            ops.append(rng.choice(acts if rng.random() < 0.6 else merges))
        b = B.Bridge(host, nrep, 'rrw')
        found = C.play_counting(b, ops)
        nsteps += len([x for x in b.log if x[:2].strip().isdigit() and 'does not apply' not in x])
        mine = []
        for i in R:
            o, r = B.outcome(b, i)
            outs[B.cat(o)] = outs.get(B.cat(o), 0) + 1
            mine.append(B.cat(o))
        ends.append('   %3d: %s' % (h, ', '.join(mine)))
        ndis += len(b.disagree)
        nread += len(b.readdiff)
        if found or b.disagree or b.readdiff:
            lines.append('## history %d (%d files): %s' % (h, nrep, ' ; '.join(' '.join(map(str, op)) for op in ops)))
            for k, op, w in found:
                beyonds += 1
                lines.append('   BEYOND BOTH COPIES at step %d (%s): %s' % (k, ' '.join(map(str, op)), w))
            lines += C.c_lines(b)
    print('== random histories of rows and switches, the removal written as %s: %d histories, steps %d,'
          ' entry disagreements %d, read differences %d, %.0f s' % (B.ROWS, count, nsteps, ndis, nread, time.time() - t0))
    print('   files at the end against every decision: ' + ', '.join('%s %d' % kv for kv in sorted(outs.items())))
    print('   merges that gave a letter neither copy gave just before: %d' % beyonds)
    for x in lines:
        print(x)
    print('== the class of each file at the end of each history; KeePassXC %s in every merge of that program' % rel)
    for x in ends:
        print(x)


# The histories cases.part_random draws, counted by what merges them; no file is made. Its draws use
# nothing but its own generator, so the count is of the histories the part plays.
def part_draws(count, seed):
    rng = random.Random(seed)
    n, by = 0, {}
    for h in range(count):
        nrep = 2 if rng.random() < 0.6 else 3
        acts, merges, faults = C.alphabet(nrep)
        ops = []
        for _ in range(rng.randint(4, 9)):
            x = rng.random()
            ops.append(rng.choice(acts if x < 0.4 else merges if x < 0.7 else faults))
        for op in ops:
            if op[0] == 'merge':
                n += 1
                key = 'KeePassXC ' + op[4] if op[1] == 'xc' else op[4] if op[1] == 'kp_other' else op[1]
                by[key] = by.get(key, 0) + 1
    print('== the merges `cases.py random %d %d` draws: %d, by program: %s'
          % (count, seed, n, ', '.join('%s %d' % kv for kv in sorted(by.items()))))


def main():
    args = sys.argv[1:]
    if args[0] == '--rows':
        B.rows(args[1])
        args = args[2:]
    elif args[0] == '--decided':
        B.decide()
        args = args[1:]
    else:
        raise SystemExit('cut.py (--rows VARIANT | --decided) --run TAG PART [ARGS]')
    if args[0] != '--run':
        raise SystemExit('cut.py: --run TAG')
    run, args = args[1], args[2:]
    part = args[0]
    rel = args[1] if part in RELEASE_PARTS else None
    if rel is not None and rel not in B.RELEASES:
        raise SystemExit('cut.py: no KeePassXC release ' + rel)
    B.PREFIX = B.PREFIX + 'r%s-%s' % (run, rel.replace('.', '') + '-' if rel else '')
    sys.stdout.reconfigure(line_buffering=True)
    if part == 'draws':
        part_draws(int(args[1]), int(args[2]))
        return
    if part == 'growth':
        import growth as G
        B.WORK = os.path.join(B.WORK, 'r' + run)
        sys.argv = sys.argv[:1]
        G.main()
        return
    host = kp.Host()
    t0 = time.time()
    try:
        if part == 'deletions':
            part_deletions(host, rel)
        elif part == 'clear':
            part_clear(host, rel)
        elif part == 'history':
            part_history(host, rel)
        elif part == 'random-rows':
            if not B.ROWS:
                raise SystemExit('cut.py: random-rows needs --rows')
            part_random_rows(host, int(args[2]), int(args[3]), rel)
        elif part == 'slow':
            C.part_merges(host, 'slow', only_xc=True)
        elif part == 'dedupe':
            C.part_dedupe(host)
        elif part == 'dupes':
            C.part_dupes(host)
        elif part == 'repair':
            C.part_repair(host)
        else:
            raise SystemExit('cut.py: unknown part ' + part)
    finally:
        host.close()
    for x in B.moved():
        print(x)
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
