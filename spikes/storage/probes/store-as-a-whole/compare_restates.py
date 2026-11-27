"""What a summary entry restates of a decision a deletion record alone tells of: the cells of each form
held to the control's, the parts of Seal again and of a store started again run under a form held to
their record, and the vectors held to the files as they stand on the branch. Written for
measured-seal-restates as compare_starts.py was for a store started again.

  uv run --no-project python -I -X utf8 compare_restates.py cells OUT
  uv run --no-project python -I -X utf8 compare_restates.py against OUT
  uv run --no-project python -I -X utf8 compare_restates.py print VECTORS
  uv run --no-project python -I -X utf8 compare_restates.py vectors VECTORS STANDING

`cells` reads the CELL lines of OUT\\cells-FORM.txt, which probe/restates.py prints, and holds each
form's cells to those of the control (`all`) cell by cell: every cell whose class, standing against
its own ideal or store differs, and the counts of cells that end wider or stopped where the control
does not. `against` holds each part of OUT whose name begins with a part of Seal again or of a store
started again and ends with a form (edits-ids, starts-cells-ids and the like) to the record of that
part, `recorded-seal` or `recorded-starts`, line for line with compare.py's masks, and prints every
line that differs. `print` writes the three files of `vectors.py --restates` into VECTORS, and holds
what `vectors.py --starts` prints to `recorded-starts\\vectors\\` byte for byte but the line ends, so
that the switches added for this measurement leave every earlier vector as it was. `vectors` is
compare_answers.py's.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import compare_answers as CA  # noqa: E402
import compare as CO  # noqa: E402

FORMS = ('all', 'named', 'ids', 'hashes')
CELL = re.compile(r'^   CELL (\S+) \| (\S+) \| (.+?) \| (\d): (.*)$')


def read_cells(path):
    out = {}
    with open(path, encoding='utf-8') as f:
        for line in f:
            m = CELL.match(line.rstrip('\n'))
            if m:
                rest = m.group(5)
                verdict = rest.split('; then ')[0]
                verdict = re.sub(r' \[entries \d+, read \d+\]$', '', verdict)
                out[m.groups()[:4]] = (verdict, rest)
    return out


def cells(out):
    res = {}
    for form in FORMS:
        p = os.path.join(out, 'cells-%s.txt' % form)
        if os.path.exists(p):
            res[form] = read_cells(p)
    base = res.get('all', {})
    print('== each form\'s cells on files held to the control\'s (all), cell by cell')
    for form in FORMS[1:]:
        if form not in res:
            continue
        mine = res[form]
        missing = sorted(set(base) ^ set(mine))
        wider = stopped = narrower = 0
        diff = []
        for key in sorted(set(base) & set(mine)):
            a, b = base[key][0], mine[key][0]
            if a == b:
                continue
            diff.append(key)
            if ('WIDER' in b and 'WIDER' not in a) or ('BEYOND ITS OWN IDEAL' in b and 'BEYOND ITS OWN IDEAL' not in a):
                wider += 1
            elif 'store ok' in a and 'store ok' not in b:
                stopped += 1
            else:
                narrower += 1
        print('   form %s: %d cells, %d differ from the control; wider where the control is not %d; stopped where the'
              ' control is whole %d; otherwise differing %d; cells in one and not the other %d' % (
                  form, len(mine), len(diff), wider, stopped, narrower, len(missing)))
        for key in diff:
            print('      %s | %s | %s | into %s' % key)
            print('         all:    %s' % base[key][0])
            print('         %-7s %s' % (form + ':', mine[key][0]))


def against(out):
    names = sorted(n for n in os.listdir(out) if n.endswith('.txt'))
    print('== each part of Seal again or of a store started again run under a form, held to its record')
    for n in names:
        stem = n[:-4]
        form = stem.rsplit('-', 1)[-1]
        if form not in FORMS[1:] or stem.startswith('cells-'):
            continue
        part = stem[:-len(form) - 1]
        if part.startswith('starts-'):
            rec = os.path.join(HERE, 'recorded-starts', part[len('starts-'):] + '.txt')
        else:
            rec = os.path.join(HERE, 'recorded-seal', {'seal-bridge': 'bridge'}.get(part, part) + '.txt')
        if not os.path.exists(rec):
            print('   %s: no record %s' % (n, os.path.relpath(rec, HERE)))
            continue
        x, y = CO.lines(os.path.join(out, n)), CO.lines(rec)
        diffs = [k for k in range(max(len(x), len(y))) if k >= len(x) or k >= len(y) or x[k] != y[k]]
        print('   %s against %s: %d lines, %s' % (n, os.path.relpath(rec, HERE), len(y),
                                                 'the same' if not diffs else '%d differ' % len(diffs)))
        for k in diffs[:40]:
            print('      line %d' % (k + 1))
            print('         record: %s' % (y[k].rstrip() if k < len(y) else '(none)'))
            print('         %-7s %s' % (form + ':', x[k].rstrip() if k < len(x) else '(none)'))


def print_vectors(target):
    os.makedirs(target, exist_ok=True)
    held = os.path.join(HERE, 'recorded-starts', 'vectors')
    for mode, name, _ in CA.FILES:
        with open(os.path.join(held, name), 'rb') as f:
            standing = CA.lf(f.read())
        print('%s with --starts: %s' % (name, 'the same bytes as recorded-starts\\vectors but the line ends'
                                        if CA.lf(CA.vectors_py('--starts', '--' + mode)) == standing else 'DIFFERS'))
        made = subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(HERE, 'vectors.py'), '--restates',
                               '--' + mode], capture_output=True, check=True)
        with open(os.path.join(target, name), 'wb') as f:
            f.write(made.stdout)


def main():
    sys.stdout.reconfigure(newline='\n')
    if sys.argv[1] == 'cells':
        cells(sys.argv[2])
    elif sys.argv[1] == 'against':
        against(sys.argv[2])
    elif sys.argv[1] == 'print':
        print_vectors(sys.argv[2])
    elif sys.argv[1] == 'vectors':
        CA.vectors(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit('unknown ' + sys.argv[1])


if __name__ == '__main__':
    main()
