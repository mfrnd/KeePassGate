r"""Makes the files for running the phone programs against the places of `kdbx-places`.

    make-bases.py --kpl <Kpl.exe> --work <empty dir>

`Kpl.exe` is `probe\bin\Release\net48\Kpl.exe`, which `dotnet build probe\Kpl.csproj -c Release`
makes. Every file has the password `probe-only`, is KDBX 4.1 and, but for `aes-*`, uses Argon2d
with 16 MiB, because KeePassDX refuses Argon2 memory below 8 MiB (read in its source). Written
into --work:

    clean-base.kdbx        every place KeePassLib 2.62 writes, and no other
    clean-A-diff.kdbx      the same places with other values and older times (the A copy)
    clean-B-diff.kdbx      ... with newer times (the B copy); merge A and B both ways
    clean-A-tie.kdbx       A again ...
    clean-B-tie.kdbx       ... and B at A's times, for a merge at equal times
    aes-clean-base.kdbx    clean-base with AES-KDF
    place-<part>.kdbx      clean-base with exactly one raw place added, so that a refusal names it:
                           comment, unreferenced, trailing, generator, xml-meta, xml-other, xml-empty,
                           attrs, xml-comment, node-cd-time, hfield, ifield, eoh
    all-raw.kdbx           clean-base with every raw place but the three risky header places at
                           once: the desktop programs' base
    dumps.txt              the dump of every file above, as `places.py dump` prints it
    places.txt             one line per place: its identifier, what it is, the file holding it, and
                           the dump line of clean-base (or of its own file) that shows it

Then, for a file a phone program wrote: `check.py <file given> <file written>`.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "scripts"))
import places  # noqa: E402
import summarize  # noqa: E402

RAW_PLACE_FILE = {
    "H.comment": "comment", "I.unreferenced": "unreferenced", "T.trailing": "trailing", "M.generator": "generator",
    "H.field13": "hfield", "I.field4": "ifield", "H.eoh": "eoh",
    "G.cd.time": "node-cd-time", "E.cd.time": "node-cd-time", "XC.meta": "xml-comment",
}


def file_for(pid: str) -> str:
    if pid in RAW_PLACE_FILE:
        f = RAW_PLACE_FILE[pid]
        return f if f == "clean-base" else f"place-{f}"
    if pid.startswith("XA."):
        return "place-attrs"
    if pid.startswith("X."):
        where = pid[2:]
        return "place-xml-meta" if where in places.META_X else "place-xml-other"
    return "clean-base"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--kpl", required=True)
    ap.add_argument("--work", required=True)
    a = ap.parse_args()
    work = Path(a.work)
    if work.exists():
        shutil.rmtree(work)
    raw = work / "raw"
    for kdf, sub in (("phone", "phone"), ("aes", "aes")):
        subprocess.run([a.kpl, "make", str(raw / sub), kdf, "41"], check=True, capture_output=True)
    for name in ("base", "A-diff", "B-diff", "A-tie", "B-tie"):
        shutil.copyfile(raw / "phone" / f"{name}.kdbx", work / f"clean-{name}.kdbx")
    shutil.copyfile(raw / "aes" / "base.kdbx", work / "aes-clean-base.kdbx")
    base = str(work / "clean-base.kdbx")
    for part in places.PARTS:
        places.inject(base, str(work / f"place-{part}.kdbx"), "base", parts=(part,))
    for risky in ("hfield", "ifield", "eoh"):
        places.inject(base, str(work / f"place-{risky}.kdbx"), "base", risky)
    places.inject(base, str(work / "all-raw.kdbx"), "base")
    shutil.rmtree(raw)

    dumps = {}
    lines = []
    for f in sorted(work.glob("*.kdbx")):
        d = dict(places.Dumper(places.open_file(str(f))).run())
        dumps[f.stem] = d
        lines.append(f"== {f.name}")
        lines += [f"{p}\t{v}" for p, v in d.items()]
    (work / "dumps.txt").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    out = ["# place\twhat it is\tfile\tdump line that shows it"]
    for pid, desc, rx in summarize.COMPILED:
        f = file_for(pid)
        src = dumps.get(f, {})
        if pid.endswith(".only"):
            src = dumps["clean-A-diff"]
            f = "clean-A-diff / clean-B-diff"
        shown = [f"{p}\t{v}" for p, v in src.items() if rx.fullmatch(p)]
        out.append(f"{pid}\t{desc}\t{f}\t{shown[0] if shown else '(none: the place is absent from that file)'}")
        for extra in shown[1:]:
            out.append(f"\t\t\t{extra}")
    (work / "places.txt").write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    print(f"android bases: {len(dumps)} files, {len(summarize.COMPILED)} places, into {work}")


if __name__ == "__main__":
    main()
