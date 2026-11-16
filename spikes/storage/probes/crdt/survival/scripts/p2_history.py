"""Survival P2's history in the design's own model, replayed: written on the branch, not by the
critique's agent.

The critique quotes the design's model, unchanged, on `revoke 0 c0; del 0 (the revocation) with a
record; merge xc 1 0; del 1 (the newest head) without a record`, and "on every single deletion after
its step 3: one command, quoted in P2". No command is quoted there and none was kept. This replays
the history through `survival\\model\\crdt_model.py`, the design's model copied unchanged, prints file
1's value after every step and what the model's own checks report, and then runs the first three
steps followed by every single deletion the model's alphabet has, in either file, with a record, with
none and with one dated before the entry. It writes `survival\\out\\p2-history.txt`.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\crdt\\survival\\scripts\\p2_history.py
"""
import os
import sys

SURVIVAL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(SURVIVAL, 'model'))
import crdt_model as M  # noqa: E402

HISTORY = [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('merge', 'xc', 1, 0),
           ('del', 1, ('head', 'new'), 0)]
SELS = [('head', 'new'), ('head', 'old'), ('ref', 'revoked'), ('ref', 'retired'),
        ('ver', 'rule:N', 'new'), ('ver', 'rule:N', 'old'), ('ver', 'rule:root', 'new'),
        ('ver', 'renew:c0', 'new'), ('rec',), ('auth',), ('padd',), ('prem',)]
KINDS = {0: 'without a record', 1: 'with a record', 2: 'with a record dated before the entry'}


def show(op):
    return ' '.join(str(x) for x in op)


def value(v):
    return '%s, admitted %s, words %s, renewals %s, names %s' % (
        v['status'], sorted(v['adm']), {n: ''.join(sorted(w)) for n, w in sorted(v['words'].items())},
        dict(v['renew']), sorted(v['names']))


def main():
    lines = ['## the history, file 1 after each step']
    W = M.World(2)
    for k, op in enumerate(HISTORY):
        done = W.step(op)
        lines.append('  %d. %-40s %s' % (k + 1, show(op), value(W.fold(W.reps[1])) if done else 'not applicable'))
    fails = M.run(HISTORY, 2)
    lines.append('  the model\'s checks: ' + ('; '.join('%s at step %d, %s' % (p, s + 1, d) for p, s, d in fails)
                                             or 'no failure'))
    lines.append('')
    lines.append('## steps 1 to 3, then one deletion')
    for f in (0, 1):
        for sel in SELS:
            for kind in (1, 0, 2):
                op = ('del', f, sel, kind)
                fails = M.run(HISTORY[:3] + [op], 2)
                lines.append('  del %d %-28s %-38s %s' % (f, sel, KINDS[kind], '; '.join(
                    '%s at step %d, %s' % (p, s + 1, d) for p, s, d in fails) or 'no failure'))
    out = os.path.join(SURVIVAL, 'out')
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'p2-history.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
