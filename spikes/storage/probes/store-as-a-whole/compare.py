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
ADDED (copies extended): with --reading-LEVEL, `out/reading-LEVEL` to `recorded-reading-LEVEL`.
ADDED (owed forms): with --owed, `out/owed` to `recorded-owed`.
ADDED (renewal answers): with --answers, `out/answers` to `recorded-answers`, and the three files of
`out/answers/vectors` to those of `recorded-answers/vectors` byte for byte but the line ends.
ADDED (seal again): with --seal, `out/seal` to `recorded-seal`, the vectors as for --answers.
ADDED (started again): with --starts, `out/starts` to `recorded-starts`.
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


def classes(path):
    """ADDED (rules per profile): the class of every cell of b-deletions, b-clear or b-history, keyed
    by its section, its operation where it has one, and its label."""
    out, sec, op = {}, None, None
    for line in lines(path):
        if line.startswith('## '):
            sec, op = line[3:].split(';')[0], None
            continue
        if sec is None:
            continue
        if line.startswith('      ') and not line.startswith('       ') and op is not None:
            label, rest = line[6:36].strip(), line[36:]
            if label not in ('copy 0 as it is', 'Synchronize into 1', 'Synchronize into 0', 'KeePassXC 2.8.0 into 1',
                             'KeePassXC 2.8.0 into 0', 'copy 0, the gate writes again'):
                continue
            rest = rest.replace('cannot write; ', '')
        elif line.startswith('   ') and not line.startswith('    ') and ': ' in line:
            label, rest = line[3:].split(': ', 1)
            if label.startswith('C ') or label.startswith('the gate wrote'):
                continue
            if rest and not rest.split(' ')[0] in ('as', 'narrower:', 'refuses:', 'WIDER:', 'not', 'Seal', 'cannot'):
                op = label
                continue
        else:
            continue
        c = rest.split(' {')[0].split(' [')[0].split(' (')[0].split(' |')[0].split(':')[0].strip()
        out[(sec, op, label)] = c
    return out


def against_decided(out_dir, rec_dir):
    """ADDED (rules per profile): each cell of b-deletions, b-clear and b-history per profile against
    the same cell of the store as decided's record, the whole-rule form."""
    for name in ('b-deletions.txt', 'b-clear.txt', 'b-history.txt'):
        a, b = os.path.join(out_dir, name), os.path.join(rec_dir, name)
        if not os.path.exists(a):
            continue
        x, y = classes(a), classes(b)
        common = sorted(set(x) & set(y), key=str)
        pairs = {}
        for k in common:
            pairs[(y[k], x[k])] = pairs.get((y[k], x[k]), 0) + 1
        print('%s: %d cells in both, %d per profile only, %d whole-rule only; whole-rule -> per profile: %s' % (
            name, len(common), len(set(x) - set(y)), len(set(y) - set(x)),
            ', '.join('%s -> %s %d' % (p, q, n) for (p, q), n in sorted(pairs.items()))))
        for k in common:
            if x[k] != y[k]:
                print('   %s | %s | %s: whole-rule %s, per profile %s' % (k[0], k[1] or '-', k[2], y[k], x[k]))
        tally = {}
        for k in set(x) - set(y):
            tally[x[k]] = tally.get(x[k], 0) + 1
        if tally:
            print('   per profile only: ' + ', '.join('%s %d' % kv for kv in sorted(tally.items())))


def main():
    global OUT, REC
    if sys.argv[1:] == ['--rows-against-decided']:      # ADDED (rules per profile)
        against_decided(os.path.join(OUT, 'rows'), os.path.join(FAMILY, 'recorded-decided'))
        sys.exit(0)
    # ADDED (two readings): the last two; ADDED (copies extended): --reading-LEVEL
    if sys.argv[1:] in (['--decided'], ['--rewrite'], ['--copies']) or sys.argv[1:2] in (
            ['--reading-nothing'], ['--reading-profile'], ['--reading-verb'], ['--reading-cap'],
            ['--reading-cap-items'], ['--rows'], ['--owed'], ['--s34'],
            ['--answers'], ['--seal'], ['--starts']):     # ADDED (rules per profile): --rows; (owed forms): --owed; (S34 forms): --s34; (renewal answers): --answers; (seal again): --seal; (started again): --starts
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
    if os.path.isdir(os.path.join(REC, 'vectors')):     # ADDED (renewal answers): the vectors, byte for byte
        for name in sorted(os.listdir(os.path.join(REC, 'vectors'))):
            a, b = os.path.join(OUT, 'vectors', name), os.path.join(REC, 'vectors', name)
            same = os.path.exists(a) and open(a, 'rb').read().replace(b'\r\n', b'\n') == \
                open(b, 'rb').read().replace(b'\r\n', b'\n')
            print('vectors/%s: %s' % (name, 'the same bytes but the line ends' if same else 'DIFFERS'))
            bad += not same
    design = os.path.join(BRANCH, 'spikes', 'storage', 'model', 'recorded', 'results-full.txt')
    mine = os.path.join(OUT, 'design-full.txt')
    if os.path.exists(mine):
        x, y = lines(mine)[:-1], lines(design)[:-1]
        print('design-full.txt against the design\'s record: %s' % ('the same but the last line' if x == y else 'DIFFERS'))
        bad += x != y
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
