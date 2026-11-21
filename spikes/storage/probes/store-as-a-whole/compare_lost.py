"""The lost versions a version settles: a run held to its record, and the vectors held to the
files the product keeps.

  uv run --no-project python -I -X utf8 compare_lost.py check OUT RECORD
  uv run --no-project python -I -X utf8 compare_lost.py print VECTORS
  uv run --no-project python -I -X utf8 compare_lost.py vectors VECTORS SHARED

`check` holds every output of a run of `model\\lost_runs.py all` in OUT to the record, line for
line, with the lines of running times masked. `print` writes the three files the product will be
held to into VECTORS, by `vectors.py --lost`, and holds every file `vectors.py` prints without
`--lost` to the same file printed with `--judge union`, `--judge object` and `--judge lost`.
`vectors` compares the files in VECTORS with those the product keeps in SHARED (its
`tests\\Shared`): every member added, every member changed, and the `about` text. It names the
files by name alone.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FILES = (('records', 'StoreRecordVectors.json', 'records'), ('meanings', 'StoreMeaningVectors.json', 'meanings'),
         ('writes', 'StoreWriteVectors.json', 'sequences'))


def masked(path):
    with open(path, encoding='utf-8') as f:
        return ['<time>\n' if line.startswith('   time ') else line for line in f]


def check(out, record):
    names = sorted(n for n in os.listdir(record) if n.endswith('.txt'))
    same = 0
    for n in names:
        a, b = masked(os.path.join(out, n)), masked(os.path.join(record, n))
        if a == b:
            same += 1
        else:
            k = next((i for i, (x, y) in enumerate(zip(a, b)) if x != y), min(len(a), len(b)))
            print('%s differs at line %d' % (n, k + 1))
    print('%d of %d files the same as the record, line for line, with the running times masked' % (same, len(names)))
    if os.path.isdir(os.path.join(out, 'vectors')) and os.path.isdir(os.path.join(record, 'vectors')):
        for mode, name, _ in FILES:
            with open(os.path.join(out, 'vectors', name), 'rb') as f:
                a = f.read()
            with open(os.path.join(record, 'vectors', name), 'rb') as f:
                b = f.read()
            print('vectors\\%s: %s' % (name, 'the same bytes as the record' if a == b else 'DIFFERS'))


def vectors_py(*args):
    return subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(HERE, 'vectors.py')] + list(args),
                          capture_output=True, check=True).stdout


def print_vectors(target):
    os.makedirs(target, exist_ok=True)
    for mode, name, _ in FILES:
        plain = vectors_py('--' + mode)
        for judge in ('union', 'object', 'lost'):
            print('%s with --judge %s: %s' % (name, judge, 'the same bytes as without it'
                                              if vectors_py('--judge', judge, '--' + mode) == plain else 'DIFFERS'))
        with open(os.path.join(target, name), 'wb') as f:
            f.write(vectors_py('--lost', '--' + mode))


def vectors(target, shared):
    for mode, name, inner in FILES:
        with open(os.path.join(target, name), encoding='utf-8') as f:
            new = json.load(f)
        with open(os.path.join(shared, name), encoding='utf-8') as f:
            old = json.load(f)
        print('== %s' % name)
        print('   members at the top: %s' % ', '.join(sorted(new)))
        for k in sorted(set(old) | set(new)):
            if k == 'about':
                continue
            if k == inner:
                added = sorted(set(new[k]) - set(old[k]))
                gone = sorted(set(old[k]) - set(new[k]))
                changed = sorted(x for x in set(old[k]) & set(new[k]) if old[k][x] != new[k][x])
                print('   %s: %d held today, %d printed; added %d, removed %d, changed %d' % (
                    k, len(old[k]), len(new[k]), len(added), len(gone), len(changed)))
                for x in added:
                    print('      added: %s' % x)
                for x in gone + changed:
                    print('      %s: %s' % ('REMOVED' if x in gone else 'CHANGED', x))
            elif old.get(k) != new.get(k):
                print('   %s: CHANGED' % k)
            else:
                print('   %s: the same' % k)
        a, b = old.get('about', ''), new.get('about', '')
        print('   about: %s' % ('the same' if a == b else 'the text held today, and %d characters added after it'
                                % (len(b) - len(a)) if b.startswith(a) else 'CHANGED'))


def main():
    sys.stdout.reconfigure(newline='\n')
    if sys.argv[1] == 'check':
        check(sys.argv[2], sys.argv[3])
    elif sys.argv[1] == 'print':
        print_vectors(sys.argv[2])
    elif sys.argv[1] == 'vectors':
        vectors(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit('unknown ' + sys.argv[1])


if __name__ == '__main__':
    main()
