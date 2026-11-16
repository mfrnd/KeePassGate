"""Measurement 3: which KDBX version and key derivation each program's code writes, and what it
drops when it writes an older version.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\source-harness\\formats.py [k2a] [kpdx]

The inputs are StoreLab's `formats` (kplib\\Formats.cs states each). Every input goes through each
harness three ways:
  save    an entry added at the root and saved
  edit    the input's one entry edited (its password) and saved, which also passes the entry
          through the program's editor
  merge   the input merged with a copy of itself and saved
and through KeePassLib 2.62's own open and save (StoreLab `resave`) beside them. StoreLab's
`dumpver` reads every file written: the header's version, the key derivation and its parameters,
the cipher, and each 4.1 item. Writes out\\formats.txt.
"""
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402

STORE_LAB = c.HERE / "kplib" / "bin" / "Release" / "net48" / "StoreLab.exe"


def main():
    progs = [p for p in sys.argv[1:] if not p.startswith("-")] or ["k2a", "kpdx"]
    root = c.WORK / "formats"
    if root.exists():
        shutil.rmtree(root)
    inputs = root / "in"
    c.run([STORE_LAB, "formats", inputs])
    files = sorted(inputs.glob("*.kdbx"))
    for p in progs:
        jobs = []
        for op in ("save", "edit", "merge"):
            (root / p / op).mkdir(parents=True)
        for f in files:
            out = root / p
            jobs += ["job save/%s" % f.stem, "open %s" % f, "add Root phone-added", "save %s" % (out / "save" / f.name), "end"]
            jobs += ["job edit/%s" % f.stem, "open %s" % f, "edit =Quality Password append -phone", "save %s" % (out / "edit" / f.name), "end"]
            jobs += ["job merge/%s" % f.stem, "open %s" % f]
            if p == "k2a":
                jobs.append("add Root phone-merge")
            jobs += ["merge %s %s" % (f, out / "merge" / f.name), "end"]
        jf = root / p / "jobs.txt"
        jf.write_text("\n".join(jobs) + "\n", encoding="utf-8")
        r = c.run([c.K2A, "batch", jf]) if p == "k2a" else c.run_kpdx(["batch", jf])
        (root / p / "harness.log").write_text(r.stdout + r.stderr, encoding="utf-8")
    (root / "kplib").mkdir(parents=True)
    for f in files:
        c.run([STORE_LAB, "resave", f, root / "kplib" / f.name])
    lines = []
    subs = ["in", "kplib"] + ["%s\\%s" % (p, op) for p in progs for op in ("save", "edit", "merge")]
    for sub in subs:
        r = c.run([STORE_LAB, "dumpver", root / sub])
        for l in r.stdout.splitlines():
            lines.append(sub.replace("\\", " ") + "\t" + l)
    text = "\n".join(lines) + "\n"
    (c.OUT / "formats.txt").write_text(text, encoding="utf-8")
    sys.stdout.write(text)


if __name__ == "__main__":
    main()
