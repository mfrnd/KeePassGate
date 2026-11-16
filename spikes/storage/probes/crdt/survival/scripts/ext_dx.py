"""The four KeePassDX searches of `recorded\\ext-results-dx.txt`, searched again: written on the
branch, not by the critique's agent.

The critique quotes "4 more with KeePassDX" in `out\\ext-results-dx.txt`. `model\\model_ext.py` has no
configuration with KeePassDX's merge and the agent kept no command, so neither the configurations
nor the seeds that wrote the record are known. This takes them from the record's own labels, the
merges Synchronize, Keepass2Android and KeePassDX and the faults Delete to the bin, a deletion with a
record and Seal again, as the record's first label names them, with and without the bin read for
every kind, and draws 8,000 histories of up to nine steps for each through `model_ext.search`, with
seeds chosen here. Its output is `survival\\out\\ext-results-dx-again.txt`, under a name of its own
because it is not the record's command.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\crdt\\survival\\scripts\\ext_dx.py
"""
import os
import sys
import time

SURVIVAL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(SURVIVAL, 'model'))
import model_ext as X  # noqa: E402

PROGS = ['kp_sync', 'k2a', 'dx']
FAULTS = ['bin', 'rec', 'reseal']
CONFIGS = [
    ('D  KeePassDX merges with Synchronize and K2A; bin, delete with a record, Seal again', 2, {}),
    ('D  the same, three files', 3, {}),
    ('DF the same with the bin read for every kind', 2, {'binall': True}),
    ('DF the same, three files', 3, {'binall': True}),
]


def main():
    t0 = time.time()
    lines = []
    for k, (name, nrep, kw) in enumerate(CONFIGS):
        lines += X.search(name, nrep, PROGS, FAULTS, 8000, 9, 900 + k, **kw)
    lines.append('== %.0f s' % (time.time() - t0))
    print(lines[-1])
    out = os.path.join(SURVIVAL, 'out')
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'ext-results-dx-again.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
