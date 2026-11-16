r"""One line per place: what a phone program did to it.

    check.py <file given> <file written>
        after a save: `kept`, `gone`, `changed (from -> to)`, `rewritten (fresh random)` or, for
        a path the program added to a place, `added (value)`; `-` where neither file holds it.
        A file that does not open gives UNREADABLE and the reason.

    check.py --merge <open copy given> <arriving copy given> <result written>
        after a merge: per place, `same` (both copies held the same value and the result too),
        `open` or `arriving` (whose value the result holds), `dropped`, `only-open kept`,
        `only-arriving taken` (for a key or record one copy alone holds), or `other`.

Both read the files themselves, with `scripts\kdbx_raw.py`, never through a program, and group
the dump paths into places as `scripts\summarize.py` lists them. The password is `probe-only`.
Runs as:

    uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 \
        spikes\storage\probes\kdbx-places\android\check.py given.kdbx written.kdbx
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "scripts"))
import places  # noqa: E402
import summarize  # noqa: E402


def dump(path: str) -> dict[str, str]:
    try:
        return dict(places.Dumper(places.open_file(path)).run())
    except Exception as e:  # noqa: BLE001 - an unreadable file is a result
        return {"UNREADABLE": f"{type(e).__name__}: {e}"}


def main(argv: list[str]) -> None:
    if argv[1:2] == ["--merge"]:
        o, a, r = (dump(p) for p in argv[2:5])
        if "UNREADABLE" in r:
            print(f"UNREADABLE\t{r['UNREADABLE']}")
            return
        cmp_ = dict(ln.split("\t", 1) for ln in places.cell_cmp(o, a, r))
        for pid, desc, _ in summarize.COMPILED:
            print(f"{pid}\t{summarize.cell_outcome(o, a, cmp_, pid)}\t{desc}")
        return
    before, after = dump(argv[1]), dump(argv[2])
    if "UNREADABLE" in after:
        print(f"UNREADABLE\t{after['UNREADABLE']}")
        return
    cmp_ = dict(ln.split("\t", 1) for ln in places.save_cmp(before, after))
    moved = {p: v for p, v in cmp_.items() if v != "kept"}
    for pid, desc, _ in summarize.COMPILED:
        print(f"{pid}\t{summarize.save_outcome(before, moved, pid)}\t{desc}")
    other = [p for p, v in moved.items() if v.startswith("added") and summarize.place_of(p) is None]
    print(f"(other paths added by the program: {len(other)})")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    main(sys.argv)
