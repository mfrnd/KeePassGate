"""The standing answers to a renewal: a run held to its record, and the vectors held to the files as
they stand on the branch.

  uv run --no-project python -I -X utf8 compare_answers.py check OUT RECORD
  uv run --no-project python -I -X utf8 compare_answers.py print VECTORS
  uv run --no-project python -I -X utf8 compare_answers.py vectors VECTORS STANDING

`check` holds every output of a run in OUT to the record, line for line, with the lines of running
times masked, and the vectors in OUT\\vectors to those of the record byte for byte but the line
ends, which a checkout on Windows writes as CRLF. `print` writes the three files of `vectors.py
--answers` into VECTORS, and holds what `vectors.py --lost` prints to `recorded-lost\\vectors\\`
byte for byte but the line ends, so that the switches added for the answers leave every earlier
vector as it was. `vectors` compares the files in VECTORS with those in STANDING (the files
as they stand: `recorded-lost\\vectors\\`, or the product's `tests\\Shared`): every member added,
changed or removed, the `about` texts, and every line of the files, a line of STANDING that is not
in VECTORS in the same order being a change. It names the files by name alone.
"""
import difflib     # cspell:ignore autojunk
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FILES = (('records', 'StoreRecordVectors.json', 'records'), ('meanings', 'StoreMeaningVectors.json', 'meanings'),
         ('writes', 'StoreWriteVectors.json', 'sequences'))
TIMES = re.compile(r'(^   time \d+ s$|^== \d+ s$)')


def lf(data):
    """The bytes of a file with its line ends as the repository holds them."""
    return data.replace(b'\r\n', b'\n')


def masked(path):
    with open(path, encoding='utf-8') as f:
        return ['<time>\n' if TIMES.match(line.rstrip('\n')) else line for line in f]


def check(out, record):
    names = sorted(n for n in os.listdir(record) if n.endswith('.txt') and n != 'times.txt')
    same = 0
    for n in names:
        if not os.path.exists(os.path.join(out, n)):
            print('%s: not in the run' % n)
            continue
        a, b = masked(os.path.join(out, n)), masked(os.path.join(record, n))
        if a == b:
            same += 1
        else:
            k = next((i for i, (x, y) in enumerate(zip(a, b)) if x != y), min(len(a), len(b)))
            print('%s differs at line %d' % (n, k + 1))
    print('%d of %d files the same as the record, line for line, with the running times masked' % (same, len(names)))
    for mode, name, _ in FILES:
        a, b = os.path.join(out, 'vectors', name), os.path.join(record, 'vectors', name)
        if os.path.exists(a) and os.path.exists(b):
            with open(a, 'rb') as f, open(b, 'rb') as g:
                print('vectors\\%s: %s' % (name, 'the same bytes as the record but the line ends'
                                           if lf(f.read()) == lf(g.read()) else 'DIFFERS'))


def vectors_py(*args):
    return subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(HERE, 'vectors.py')] + list(args),
                          capture_output=True, check=True).stdout


def print_vectors(target):
    os.makedirs(target, exist_ok=True)
    lost = os.path.join(HERE, 'recorded-lost', 'vectors')
    for mode, name, _ in FILES:
        with open(os.path.join(lost, name), 'rb') as f:
            standing = lf(f.read())
        print('%s with --lost: %s' % (name, 'the same bytes as recorded-lost\\vectors but the line ends'
                                      if lf(vectors_py('--lost', '--' + mode)) == standing else 'DIFFERS'))
        with open(os.path.join(target, name), 'wb') as f:
            f.write(vectors_py('--answers', '--' + mode))


def vectors(target, standing):
    for mode, name, inner in FILES:
        with open(os.path.join(target, name), encoding='utf-8') as f:
            text_new = f.read()
        with open(os.path.join(standing, name), encoding='utf-8') as f:
            text_old = f.read().replace('\r\n', '\n')
        new, old = json.loads(text_new), json.loads(text_old)
        print('== %s' % name)
        print('   members at the top: %s' % ', '.join(sorted(new)))
        for k in sorted(set(old) | set(new)):
            if k == 'about':
                continue
            if k not in old:
                print('   %s: added, %d entries' % (k, len(new[k])))
            elif k == inner:
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
        # line by line: every line held today is kept, in order, but the about line, which grows
        added = removed = 0
        changed_lines = []
        for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, text_old.splitlines(), text_new.splitlines(),
                                                          autojunk=False).get_opcodes():
            if op == 'insert':
                added += j2 - j1
            elif op in ('delete', 'replace'):
                removed += i2 - i1
                added += j2 - j1
                changed_lines += [line.strip()[:60] for line in text_old.splitlines()[i1:i2]]
        print('   lines: %d added; %d held today replaced or removed%s' % (
            added, removed, (': ' + '; '.join(changed_lines)) if changed_lines else ''))


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
