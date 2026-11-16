"""Compares a fresh run with the record, file by file.

    compare.py <recorded-dir> <out-dir>

Every file of the record is held to the fresh file of the same name line for line, line endings
aside. Two files hold times, which differ between any two runs: in `xc-times.tsv` the seconds of
each line are left out and the rest is compared, and `phases.txt` is printed side by side and not
compared.
"""

import pathlib
import sys


def lines(path):
    return path.read_text(encoding="utf-8").splitlines()


def main(recorded, out):
    recorded, out = pathlib.Path(recorded), pathlib.Path(out)
    differ = 0
    for r in sorted(p for p in recorded.iterdir() if p.is_file()):
        o = out / r.name
        if not o.exists():
            print(f"{r.name}: not in the fresh run")
            differ += 1
            continue
        if r.name == "phases.txt":
            for a, b in zip(lines(r), lines(o)):
                print(f"{r.name}: recorded {a!r}, this run {b!r}")
            continue
        a, b = lines(r), lines(o)
        if r.name == "xc-times.tsv":
            a = [line.rsplit("\t", 1)[0] for line in a]
            b = [line.rsplit("\t", 1)[0] for line in b]
        if a == b:
            print(f"{r.name}: the same, {len(a)} lines" + (" (the seconds left out)" if r.name == "xc-times.tsv" else ""))
            continue
        differ += 1
        shown = 0
        for i in range(max(len(a), len(b))):
            x = a[i] if i < len(a) else "<none>"
            y = b[i] if i < len(b) else "<none>"
            if x != y:
                print(f"{r.name}:{i + 1}: recorded {x[:160]!r}")
                print(f"{r.name}:{i + 1}: this run {y[:160]!r}")
                shown += 1
                if shown == 5:
                    break
        print(f"{r.name}: differs")
    print("every compared file the same" if differ == 0 else f"{differ} files differ")
    return 1 if differ else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
