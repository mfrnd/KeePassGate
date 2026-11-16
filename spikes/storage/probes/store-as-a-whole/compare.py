"""Holds a fresh out of the family store-as-a-whole to its recorded copy, line by line.

Run:  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/compare.py

Masked before comparing, because they depend on the machine and the moment and on nothing a probe
decides: running times (a part's last line, the first line of `b-random.txt`, `times.txt`), the
read's milliseconds and the file's size in `b-growth.txt` (a protected string is written under a
stream KeePassLib draws at random at each save, so the compressed file varies by some bytes), and
the time KeePassLib's Delete
Duplicate Entries gives the records it writes, which is the moment it runs. `design-full.txt` is held to the design's own record,
spikes/storage/model/recorded/results-full.txt, but for its last line. Exits 0 when nothing differs.

ADDED (decided store): with --decided, `out/decided` is held to `recorded-decided` the same way.
ADDED (two readings): with --rewrite or --copies, `out/rewrite` or `out/copies` to `recorded-rewrite`
or `recorded-copies`.
"""
import os
import re
import sys

FAMILY = os.path.dirname(os.path.abspath(__file__))
BRANCH = os.path.normpath(os.path.join(FAMILY, '..', '..', '..', '..'))
OUT, REC = os.path.join(FAMILY, 'out'), os.path.join(FAMILY, 'recorded')
MASKS = [(re.compile(r'^== \d+ s$'), '== N s'),
         (re.compile(r', \d+ s$'), ', N s'),
         (re.compile(r'\d+\.\d ms'), 'N ms'),
         (re.compile(r'file 2\d{7}\b'), 'file <the moment>'),
         (re.compile(r'file +\d+ bytes'), 'file N bytes')]


def lines(path):
    with open(path, encoding='utf-8') as fh:
        out = []
        for line in fh.read().splitlines():
            for rx, rep in MASKS:
                line = rx.sub(rep, line)
            out.append(line)
        return out


def main():
    global OUT, REC
    if sys.argv[1:] in (['--decided'], ['--rewrite'], ['--copies']):     # ADDED (two readings): the last two
        name = sys.argv[1][2:]
        OUT, REC = os.path.join(OUT, name), os.path.join(FAMILY, 'recorded-' + name)
    bad = 0
    for name in sorted(os.listdir(REC)):
        if not name.endswith('.txt') or name == 'times.txt':
            continue
        a, b = os.path.join(OUT, name), os.path.join(REC, name)
        if not os.path.exists(a):
            print('%s: not in out' % name)
            bad += 1
            continue
        x, y = lines(a), lines(b)
        diffs = [k for k in range(max(len(x), len(y))) if k >= len(x) or k >= len(y) or x[k] != y[k]]
        print('%s: %d lines, %s' % (name, len(y), 'the same' if not diffs else '%d differ, first at line %d' % (len(diffs), diffs[0] + 1)))
        bad += bool(diffs)
    design = os.path.join(BRANCH, 'spikes', 'storage', 'model', 'recorded', 'results-full.txt')
    mine = os.path.join(OUT, 'design-full.txt')
    if os.path.exists(mine):
        x, y = lines(mine)[:-1], lines(design)[:-1]
        print('design-full.txt against the design\'s record: %s' % ('the same but the last line' if x == y else 'DIFFERS'))
        bad += x != y
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
