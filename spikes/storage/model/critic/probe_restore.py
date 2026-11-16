"""A backup restored and written to again, with no other fault: does the restore's new writing
(new nonces, repeated heights) ever undo or widen anything once the newer copy is merged back?
Exhaustive over a small set of gate acts after the restore, and every merge back."""
import itertools
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_formal as m

fs = frozenset
acts_before = [('gate', 0, 'rule', 'N', 'r'), ('gate', 0, 'revoke', 'c0'), ('gate', 0, 'prem', 'q'),
               ('gate', 0, 'renew', 'c1', 'forever'), ('gate', 0, 'rule', 'root', 'none')]
acts_after = [('gate', 0, 'rule', 'N', 'rw'), ('gate', 0, 'pair', 'new', fs({'p', 'q'})), ('gate', 0, 'padd', 'q'),
              ('gate', 0, 'renew', 'c1', 'allowed'), ('gate', 0, 'rule', 'root', 'rA'), ('gate', 0, 'recreate')]
merges = ['sync', 'k2a', 'kpo', 'xc', 'dx']
total = fails = 0
for opt in (m.Opt(), m.Opt(K=3)):
    for a in acts_before:
        for b1, b2 in itertools.product(acts_after, repeat=2):
            for mg in merges:
                for direction in ((0, 1), (1, 0)):
                    hist = [('over', 2, 0), a, ('sync', 0, 1), ('over', 0, 2), b1, b2, (mg,) + direction]
                    found, w = m.run(hist, 3, opt)
                    total += 1
                    bad = {c for c in found if c in ('wider-than-evidence', 'revocation-undone')}
                    # the overwritten file is judged against what is physically in it after the merge back
                    if bad:
                        fails += 1
                        if fails <= 5:
                            print('FAIL', opt.K, '; '.join(m.fmt(s) for s in hist), sorted(bad))
print(f"restore, two gate acts, one merge back: {total} histories, {fails} with a failure against IE "
      f"or a revocation undone")
