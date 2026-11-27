"""A store started again: the vectors held to the files as they stand on the branch. Written for
measured-started-again as compare_seal.py was for Seal again.

  uv run --no-project python -I -X utf8 compare_starts.py print VECTORS
  uv run --no-project python -I -X utf8 compare_starts.py vectors VECTORS STANDING

`print` writes the three files of `vectors.py --starts` into VECTORS, and holds what `vectors.py
--seal` prints to `recorded-seal\\vectors\\` byte for byte but the line ends, so that the switches
added for a store started again leave every earlier vector as it was. `vectors` is
compare_answers.py's: it compares the files in VECTORS with those in STANDING (the files as they
stand: `recorded-seal\\vectors\\`, or the product's `tests\\Shared`), every member added, changed or
removed, the `about` texts, and every line of the files.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import compare_answers as CA  # noqa: E402


def print_vectors(target):
    os.makedirs(target, exist_ok=True)
    held = os.path.join(HERE, 'recorded-seal', 'vectors')
    for mode, name, _ in CA.FILES:
        with open(os.path.join(held, name), 'rb') as f:
            standing = CA.lf(f.read())
        print('%s with --seal: %s' % (name, 'the same bytes as recorded-seal\\vectors but the line ends'
                                      if CA.lf(CA.vectors_py('--seal', '--' + mode)) == standing else 'DIFFERS'))
        made = subprocess.run([sys.executable, '-I', '-X', 'utf8', os.path.join(HERE, 'vectors.py'), '--starts',
                               '--' + mode], capture_output=True, check=True)
        with open(os.path.join(target, name), 'wb') as f:
            f.write(made.stdout)


def main():
    sys.stdout.reconfigure(newline='\n')
    if sys.argv[1] == 'print':
        print_vectors(sys.argv[2])
    elif sys.argv[1] == 'vectors':
        CA.vectors(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit('unknown ' + sys.argv[1])


if __name__ == '__main__':
    main()
