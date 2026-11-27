"""Seal again: a run held to its record, and the vectors held to the files as they stand on the
branch. Written for measured-seal as compare_answers.py was for the standing answers.

  uv run --no-project python -I -X utf8 compare_seal.py check OUT RECORD
  uv run --no-project python -I -X utf8 compare_seal.py print VECTORS
  uv run --no-project python -I -X utf8 compare_seal.py vectors VECTORS STANDING

`check` holds every output of a run in OUT to the record, line for line, with the lines of running
times masked, and the vectors in OUT\\vectors to those of the record byte for byte but the line
ends, which a checkout on Windows writes as CRLF. `print` writes the three files of `vectors.py
--seal` into VECTORS, and holds what `vectors.py --answers` prints to `recorded-answers\\vectors\\`
byte for byte but the line ends, so that the switches added for the seal leave every earlier
vector as it was; it also prints what `vectors.py --seal` reports of A7 played with the seal on.
`vectors` is compare_answers.py's: it compares the files in VECTORS with those in STANDING (the
files as they stand: `recorded-answers\\vectors\\`, or the product's `tests\\Shared`), every member
added, changed or removed, the `about` texts, and every line of the files.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import compare_answers as CA  # noqa: E402


def print_vectors(target):
    os.makedirs(target, exist_ok=True)
    held = os.path.join(HERE, 'recorded-answers', 'vectors')
    for mode, name, _ in CA.FILES:
        with open(os.path.join(held, name), 'rb') as f:
            standing = CA.lf(f.read())
        print('%s with --answers: %s' % (name, 'the same bytes as recorded-answers\\vectors but the line ends'
                                         if CA.lf(CA.vectors_py('--answers', '--' + mode)) == standing else 'DIFFERS'))
        made = subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(HERE, 'vectors.py'), '--seal',
                               '--' + mode], capture_output=True, check=True)
        for line in made.stderr.decode('utf-8').splitlines():
            print('%s with --seal: %s' % (name, line))
        with open(os.path.join(target, name), 'wb') as f:
            f.write(made.stdout)


def main():
    sys.stdout.reconfigure(newline='\n')
    if sys.argv[1] == 'check':
        CA.check(sys.argv[2], sys.argv[3])
    elif sys.argv[1] == 'print':
        print_vectors(sys.argv[2])
    elif sys.argv[1] == 'vectors':
        CA.vectors(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit('unknown ' + sys.argv[1])


if __name__ == '__main__':
    main()
