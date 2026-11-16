"""Two more questions over random histories:
1. availability: does any history of gate acts and merges alone, with no fault, leave a file incomplete?
2. same elements, different verdicts: at the end of a history, two files holding the same set of
   element identifiers (anywhere in the tree): do they ever answer differently, and is one wider?
"""
import random
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_formal as m

n = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
rng = random.Random(11)
for label, opt in [('as written', m.Opt()), ('as written, K=3', m.Opt(K=3)), ('anywhere', m.Opt(anywhere=True))]:
    spurious = 0
    first = None
    for _ in range(n):
        nf = rng.choice((3, 4))
        hist = [m.random_step(rng, nf, m.CONFIGS['A merges only']) for _ in range(rng.randint(4, 16))]
        w = m.setup(nf, opt)
        for k, s in enumerate(hist):
            m.apply_step(w, s)
            if any(not m.read(f, opt).whole for f in w.files):
                spurious += 1
                if first is None or k + 1 < len(first):
                    first = hist[:k + 1]
                break
    print(f"{label}: merges only, {n} histories: {spurious} left a file incomplete")
    if first:
        print('   e.g. ' + '; '.join(m.fmt(s) for s in first))

    same_diff = 0
    same_wider = 0
    ex = None
    for _ in range(n):
        nf = 3
        cfg = m.CONFIGS['F everything but overwrite']
        hist = [m.random_step(rng, nf, cfg) for _ in range(rng.randint(4, 14))]
        _, w = m.run(hist, nf, opt)
        for a in range(nf):
            for b in range(a + 1, nf):
                fa, fb = w.files[a], w.files[b]
                if set(fa.entries) != set(fb.entries):
                    continue
                va, vb = m.read(fa, opt), m.read(fb, opt)
                if va.reach() != vb.reach() or va.strict() != vb.strict() or va.whole != vb.whole:
                    same_diff += 1
                    if not (m.leq(va, vb) or m.leq(vb, va)) or (va.whole and vb.whole and va.reach() != vb.reach()):
                        same_wider += 1
                        if ex is None or len(hist) < len(ex[0]):
                            ex = (hist, a, b, va.show(), vb.show())
    print(f"{label}: {n} histories with every fault but overwrite: {same_diff} pairs of files holding the "
          f"same elements answered differently; {same_wider} of them both serving and differing")
    if ex:
        print('   e.g. ' + '; '.join(m.fmt(s) for s in ex[0]))
        print(f"   file {ex[1]}: {ex[3]}")
        print(f"   file {ex[2]}: {ex[4]}")
