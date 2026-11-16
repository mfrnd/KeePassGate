"""Compares a fresh run of this family's probes with the record: written on the branch.

Every file in `survival\\out\\` that has a recorded copy of the same name in `survival\\recorded\\`
is compared line for line, line endings ignored, with only these masked:

- the running time a run prints: `, 354 s` at the end of the first line of `bridge-random-*.txt`,
  and the last line `== 123 s` of `ext-results.txt`;
- in `facts.txt`, section F5, the deletion records that `PwDatabase.DeleteDuplicateEntries` dates
  at the moment of the run (`@27544930`, seconds after 2026-01-01), and the identifier of the user
  entry it deletes, which `PwEntry` draws at random (`aaa85ae6`); the records of such a line are
  sorted after masking, since the random identifier decided where that one stood.

`security-and-fit\\out\\counts.txt` is compared by what it counts, since the reconstructed script
prints the same numbers in its own words; `survival\\out\\ext-results-dx-again.txt` with the record
it searches again, and `survival\\out\\p2-history.txt` with what survival P2 states. Exits 0 when
nothing differs.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\crdt\\compare.py
"""
import difflib
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SURVIVAL = os.path.join(HERE, 'survival')
SECURITY = os.path.join(HERE, 'security-and-fit')


def lines(path):
    with open(path, encoding='utf-8') as fh:
        return [x.rstrip('\r\n') for x in fh]


def mask(name, text):
    out = []
    for line in text:
        if name.startswith('bridge-random-'):
            line = re.sub(r', \d+ s$', ', <time> s', line)
        if name == 'ext-results.txt':
            line = re.sub(r'^== \d+ s$', '== <time> s', line)
        if name == 'facts.txt' and '| records: ' in line:
            head, records = line.split('| records: ')
            items = []
            for item in records.split(', '):
                item = re.sub(r'^[0-9a-f]{8}@', '<user entry>@', item)
                item = re.sub(r'@\d{7,}$', '@<now>', item)
                items.append(item)
            line = head + '| records: ' + ', '.join(sorted(items))
        out.append(line)
    return out


def same(label, a, b):
    if a == b:
        print('%-40s the same, %d lines' % (label, len(a)))
        return True
    print('%-40s DIFFERS' % label)
    sys.stdout.writelines(x + '\n' for x in difflib.unified_diff(a, b, 'recorded', 'fresh', lineterm='', n=1))
    return False


def counts(path):
    """What counts.txt counts, whichever words it uses: lines per file, the files mentioning
    CustomData, the lines matching the recycle bin's names per file, and the model's line."""
    text = lines(path)
    per_file = {}
    for x in text:
        m = re.match(r'^\s*(\d+)\s+((?:Bridge|Authority|Acl|Ui)\\\S+\.cs)$', x)
        if m:
            per_file[m.group(2)] = int(m.group(1))
    mentioning = [int(m.group(1)) for x in text for m in [re.search(r'mentioning "CustomData": (\d+)', x)] if m]
    bin_line = [x for x in text if '(' in x and 'total)' in x][0]
    bins = dict((f, int(c)) for f, c in re.findall(r'([\w\\]+\.cs) (\d+)', bin_line))
    total = int(re.search(r'\((\d+) total\)', bin_line).group(1))
    model = 'if any(not ok for u, (eid, mt, ok) in rep.entries.items() if u == eid)' in '\n'.join(text)
    return per_file, mentioning[0], bins, total, model


def main():
    ok = True
    for name in sorted(os.listdir(os.path.join(SURVIVAL, 'recorded'))):
        fresh = os.path.join(SURVIVAL, 'out', name)
        if name == 'ext-results-dx.txt':
            continue    # no command wrote it that the branch has; ext-results-dx-again.txt below
        if not os.path.exists(fresh):
            print('%-40s not run' % ('survival\\' + name))
            continue
        ok &= same('survival\\' + name, mask(name, lines(os.path.join(SURVIVAL, 'recorded', name))),
                   mask(name, lines(fresh)))

    again = os.path.join(SURVIVAL, 'out', 'ext-results-dx-again.txt')
    if os.path.exists(again):
        b = [x for x in lines(again) if not x.startswith('== ')]
        ok &= same('survival\\ext-results-dx-again.txt', lines(os.path.join(SURVIVAL, 'recorded', 'ext-results-dx.txt')), b)

    p2 = os.path.join(SURVIVAL, 'out', 'p2-history.txt')
    if os.path.exists(p2):
        t = lines(p2)
        widening = [x for x in t if x.startswith('  del ') and 'no failure' not in x]
        good = ("admitted []" in t[3] and "admitted ['c0']" in t[4] and 'safety at step 4' in t[5]
                and 'one-loss at step 4' in t[5] and len(widening) == 1 and "('head', 'new')" in widening[0]
                and 'without a record' in widening[0])
        ok &= good
        print('%-40s %s' % ('survival\\p2-history.txt', 'as survival P2 states' if good else 'NOT as survival P2 states'))

    fresh = os.path.join(SECURITY, 'out', 'counts.txt')
    if os.path.exists(fresh):
        a, b = counts(os.path.join(SECURITY, 'recorded', 'counts.txt')), counts(fresh)
        for label, x, y in zip(('lines per file', 'files mentioning CustomData', 'recycle bin lines per file',
                                'recycle bin lines in all', 'the model line quoted'), a, b):
            good = x == y
            ok &= good
            print('%-40s %s: %s' % ('security-and-fit\\counts.txt', label, 'the same' if good else 'DIFFERS: %r against %r' % (x, y)))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
