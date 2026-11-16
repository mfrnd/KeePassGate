"""Compares a fresh run of the models with the record: written on the branch.

Every file `run.ps1` writes into `out\\` or `critic\\out\\` is compared, line for line, with its
recorded copy of the same name under `recorded\\` or `critic\\recorded\\`, with line endings
ignored and one thing masked: the running time on the last line of `results-full.txt` and
`results-quick.txt` (`== 596 s`). `results-quick.txt` and `results-scenarios.txt` have no record;
the second is compared with the scenario part of `results-full.txt`, and the first only reported.
Then `checks\\design_histories.py`'s output, which has no record either, is held to what the design
states of each history. Exits 0 when nothing differs.

    uv run --no-project python -I -X utf8 spikes\\storage\\model\\compare.py
"""
import difflib
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TIME = re.compile(r'^== \d+ s$')


def read(path):
    with open(path, encoding='utf-8') as fh:
        return [TIME.sub('== <time> s', line.rstrip('\r\n')) for line in fh]


def compare(name, recorded, fresh):
    if not os.path.exists(fresh):
        print('%-34s not run' % name)
        return True
    a, b = read(recorded), read(fresh)
    if a == b:
        print('%-34s the same, %d lines' % (name, len(a)))
        return True
    print('%-34s DIFFERS' % name)
    sys.stdout.writelines(x + '\n' for x in difflib.unified_diff(a, b, 'recorded', 'fresh', lineterm='', n=1))
    return False


def main():
    ok = True
    ok &= compare('results-full.txt', os.path.join(HERE, 'recorded', 'results-full.txt'),
                  os.path.join(HERE, 'out', 'results-full.txt'))
    for name in ('results-critic.txt', 'results-liveness.txt', 'results-restore.txt', 'results-fixed.txt'):
        ok &= compare('critic\\' + name, os.path.join(HERE, 'critic', 'recorded', name),
                      os.path.join(HERE, 'critic', 'out', name))

    scen = os.path.join(HERE, 'out', 'results-scenarios.txt')
    if os.path.exists(scen):
        full = read(os.path.join(HERE, 'recorded', 'results-full.txt'))
        part = full[:full.index('== random histories, the design as written')]
        b = read(scen)
        print('%-34s %s' % ('results-scenarios.txt', 'the same as the record\'s scenario part' if b == part
                            else 'DIFFERS from the record\'s scenario part'))
        ok &= b == part

    quick = os.path.join(HERE, 'out', 'results-quick.txt')
    if os.path.exists(quick):
        print('%-34s no record; %d lines' % ('results-quick.txt', len(read(quick))))

    hist = os.path.join(HERE, 'out', 'design-histories.txt')
    if os.path.exists(hist):
        blocks = {}
        for block in '\n'.join(read(hist)).split('\n\n'):
            rows = block.strip().split('\n')
            blocks[rows[0][3:]] = {r[3:19].strip(): r[19:].strip() for r in rows[2:]}
        want = {}
        for name in blocks:
            if name.startswith('12.4'):
                want[name] = ('as written', lambda v: v == 'no failure')
        want['6.4 item 5, Seal again after both carriers of a standing answer went without a trace'] = (
            'as written', lambda v: v.startswith('sealed-after-a-loss') and 'renewal of c0 asked' in v)
        want['6.4 item 5, the same with deletion records'] = ('as written', lambda v: v == 'no failure')
        for name, (column, test) in want.items():
            good = test(blocks[name][column])
            ok &= good
            print('%-34s %s: %s' % ('design-histories.txt', name[:60], 'as the design states' if good
                                    else 'NOT as the design states: ' + blocks[name][column]))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
