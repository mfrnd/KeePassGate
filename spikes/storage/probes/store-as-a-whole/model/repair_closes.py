"""Histories the repair of change 7 closes: wider without it, not wider with it.

Run:  uv run --no-project python -I -X utf8 repair_closes.py [COUNT]

Draws the design's random histories (its configurations C, D, E, F, I and J2, two files, seeds
of its own), with no hand deletion or edit of the authority's entry, under the store without the
repair; replays every history that fails `safety` under the store with the repair; and prints how
many the repair closes, how many it leaves, and the shortest of each, shrunk.
"""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402
import store_runs as R  # noqa: E402


def fails(ops, fl):
    return any(f[0] == 'safety' for f in M.run(ops, 2, fl, check_conv=False))


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 20000
    sys.stdout.reconfigure(line_buffering=True)
    orig, alt = R.without_authority_losses()
    M.alphabet = alt
    off = M.STORE.but(repair=False)
    for k, (tag, cfg) in enumerate([('C', 'rec trace'), ('D', 'rec skew'), ('E', 'rec trace clear'),
                                    ('F', 'rec foreign'), ('I', 'rec skew trace clear over foreign strip'),
                                    ('J2', 'synconly rec skew trace')]):
        rng = random.Random(4000 + k)
        acts, merges, faults = M.alphabet(2, cfg, M.STORE)
        closed, left = [], []
        for _ in range(n):
            ops = []
            for _ in range(rng.randint(3, 9)):
                x = rng.random()
                ops.append(rng.choice(acts if x < 0.45 else merges if x < 0.75 or not faults else faults))
            if fails(ops, off):
                (left if fails(ops, M.STORE) else closed).append(ops)
        print('%-3s %d histories: wider without the repair %d; of them closed by it %d, left wider %d'
              % (tag, n, len(closed) + len(left), len(closed), len(left)))
        if closed:
            ops = min(closed, key=len)
            changed = True
            while changed:
                changed = False
                for i in range(len(ops)):
                    c = ops[:i] + ops[i + 1:]
                    if fails(c, off) and not fails(c, M.STORE):
                        ops, changed = c, True
                        break
            print('    shortest closed: ' + ' ; '.join(' '.join(map(str, op)) for op in ops))
    M.alphabet = orig


if __name__ == '__main__':
    main()
