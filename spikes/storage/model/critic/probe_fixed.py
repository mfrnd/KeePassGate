"""Every counterexample replayed under P1's change (count elements wherever they sit), and under
P1's change with no window, to show which remain."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_formal as m

for name, title, nf, opt, hist in m.ce_list():
    for label, o in (('P1 change', m.Opt(anywhere=True, K=opt.K, rotate=opt.rotate,
                                          xc_bin_shared=opt.xc_bin_shared)),
                     ('P1 and P2 changes', m.Opt(anywhere=True, K=None, rotate=opt.rotate,
                                                  xc_bin_shared=opt.xc_bin_shared))):
        found, _ = m.run(hist, nf, o)
        print(f"{name:18} {label:18} {sorted(found) if found else 'no failure'}")
