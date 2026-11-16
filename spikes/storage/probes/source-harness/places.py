"""Measurement 1: every place of a KDBX file through each harness, read by the kdbx-places family.

    uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 \\
        spikes\\storage\\probes\\source-harness\\places.py [k2a] [kpdx]

The bases are the kdbx-places family's phone bases, made by its `android\\make-bases.py` into
work\\places\\bases\\ (run.ps1 does that first). Every base goes through each harness:
  save        an entry added at the root and saved (what makes a phone program write)
  unrelated   another group (kpg-tpl2) renamed and saved
  entry       the password of the entry holding the entry places edited and saved
  merge       the two pairs, clean-A-diff with clean-B-diff and clean-A-tie with clean-B-tie, each
              in both directions: the first opened, the second merged in, as the program merges
and KeePassLib 2.62's Synchronize of the same pairs beside them. The family's `android\\check.py`
then reads every file written and prints one line per place: kept, changed or gone after a save,
and whose value the result holds after a merge. Writes out\\places-<program>.txt (every line) and
out\\places-table.md (one row per place).
"""
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402

PLACES = c.HERE.parent / "kdbx-places"
CHECK = PLACES / "android" / "check.py"
BASES = c.WORK / "places" / "bases"
PW = "probe-only"
# The entry holding the entry places, by its identifier (the family's probe makes it from the GUID
# 4b50472d-0000-4000-8000-000000000002, whose bytes in KeePass's order are these).
ENTRY = "2d47504b000000408000000000000002"
PAIRS = (("clean-A-diff", "clean-B-diff"), ("clean-A-tie", "clean-B-tie"))
STORE_LAB = c.HERE / "kplib" / "bin" / "Release" / "net48" / "StoreLab.exe"


def jobs_for(program, out):
    jobs = []
    for f in sorted(BASES.glob("*.kdbx")):
        for op, steps in (("save", ["add Root phone-added"]),
                          ("unrelated", ["rename-group =kpg-tpl2 -phone"]),
                          ("entry", ["edit %s Password append -phone" % ENTRY])):
            jobs += ["job %s/%s" % (op, f.stem), "password " + PW, "open %s" % f] + steps
            jobs += ["save %s" % (out / op / f.name), "end"]
    for a, b in PAIRS:
        for x, y in ((a, b), (b, a)):
            jobs += ["job merge/%s+%s" % (x, y), "password " + PW, "open %s" % (BASES / (x + ".kdbx"))]
            if program == "k2a":
                jobs.append("add Root phone-merge")
            jobs += ["merge %s %s" % (BASES / (y + ".kdbx"), out / "merge" / ("%s+%s.kdbx" % (x, y))), "end"]
    return jobs


def check(args):
    r = subprocess.run([sys.executable, "-I", "-X", "utf8", str(CHECK)] + [str(a) for a in args],
                       capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        return {"(check failed)": r.stderr.strip().splitlines()[-1] if r.stderr.strip() else "exit %d" % r.returncode}
    res = {}
    for line in r.stdout.splitlines():
        p = line.split("\t")
        if len(p) >= 2 and not line.startswith("("):
            res[p[0]] = p[1]
            DESC.setdefault(p[0], p[2] if len(p) > 2 else "")
    return res


# The places in the order the family's check.py prints them, with its description of each.
DESC = {}


def run_program(program):
    out = c.WORK / "places" / "runs" / program
    if out.exists():
        shutil.rmtree(out)
    for d in ("save", "unrelated", "entry", "merge"):
        (out / d).mkdir(parents=True)
    jobs_file = out / "jobs.txt"
    jobs_file.write_text("\n".join(jobs_for(program, out)) + "\n", encoding="utf-8")
    r = c.run([c.K2A, "batch", jobs_file]) if program == "k2a" else c.run_kpdx(["batch", jobs_file])
    (out / "harness.log").write_text(r.stdout + r.stderr, encoding="utf-8")
    failed = [x for x in (r.stdout + r.stderr).splitlines() if x.startswith("FAILED") or x.startswith(program + ":")]
    if program == "kpdx":
        # KeePassDX logs the reader's reason and throws without it: each base it did not open is
        # opened again with its log on, and the first exception line it logs is kept.
        refused = sorted({x.split(":")[0].split("/")[-1] for x in failed if x.startswith("FAILED save/")})
        for stem in refused:
            jf = out / ("open-%s.txt" % stem)
            jf.write_text("job %s\npassword %s\nopen %s\nend\n" % (stem, PW, BASES / (stem + ".kdbx")), encoding="utf-8")
            os.environ["KPDX_LOG"] = "1"
            rr = c.run_kpdx(["batch", jf])
            del os.environ["KPDX_LOG"]
            why = [x for x in (rr.stdout + rr.stderr).splitlines() if "Exception" in x and not x.startswith("\tat") and not x.startswith("FAILED")]
            failed.append("reason %s: %s" % (stem, why[0].strip() if why else "(none logged)"))
    (c.OUT / ("places-%s-harness.txt" % program)).write_text("\n".join(failed) + "\n", encoding="utf-8")
    results = {}
    for op in ("save", "unrelated", "entry"):
        for f in sorted(BASES.glob("*.kdbx")):
            got = out / op / f.name
            results[(op, f.stem)] = check([f, got]) if got.exists() else {"(no file)": "the program did not open the file, or wrote none"}
    for a, b in PAIRS:
        for x, y in ((a, b), (b, a)):
            got = out / "merge" / ("%s+%s.kdbx" % (x, y))
            results[("merge", x + "+" + y)] = check(["--merge", BASES / (x + ".kdbx"), BASES / (y + ".kdbx"), got]) if got.exists() else {"(no file)": "the program did not open the file, or wrote none"}
    return results


def run_keepasslib():
    out = c.WORK / "places" / "runs" / "kplib"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    results = {}
    for a, b in PAIRS:
        for x, y in ((a, b), (b, a)):
            got = out / ("%s+%s.kdbx" % (x, y))
            c.run([STORE_LAB, "sync", BASES / (x + ".kdbx"), BASES / (y + ".kdbx"), got, PW])
            results[("merge", x + "+" + y)] = check(["--merge", BASES / (x + ".kdbx"), BASES / (y + ".kdbx"), got])
    return results


def file_for(pid, res):
    """The base a place is measured on: clean-base when it holds the place, else the first of the
    one-place files that does (a place a merge alone shows, `*.only`, has none)."""
    if res.get(("save", "clean-base"), {}).get(pid, "-") != "-":
        return "clean-base"
    for (op, f), v in sorted(res.items()):
        if op == "save" and f.startswith("place-") and v.get(pid, "-") != "-":
            return f
    return None


def main():
    progs = [p for p in sys.argv[1:] if not p.startswith("-")] or ["k2a", "kpdx"]
    results_by_program = {p: run_program(p) for p in progs}
    results_by_program["kplib"] = run_keepasslib()
    for p, res in results_by_program.items():
        lines = []
        for (op, f), verdicts in sorted(res.items()):
            for pid, v in verdicts.items():
                lines.append("%s\t%s\t%s\t%s" % (op, f, pid, v))
        (c.OUT / ("places-%s.txt" % p)).write_text("\n".join(lines) + "\n", encoding="utf-8")
    merges = ["clean-A-diff+clean-B-diff", "clean-B-diff+clean-A-diff", "clean-A-tie+clean-B-tie", "clean-B-tie+clean-A-tie"]
    head = "| Place | What it is | Base |"
    sep = "| --- | --- | --- |"
    for p in progs:
        head += " %s save | %s unrelated | %s entry edit | %s merge A+B, B+A, tie A+B, B+A |" % (p, p, p, p)
        sep += " --- | --- | --- | --- |"
    head += " KeePassLib merge A+B, B+A, tie A+B, B+A |"
    sep += " --- |"
    md = ["# The places of the format, by program", "", head, sep]
    for pid, desc in DESC.items():
        f = file_for(pid, results_by_program[progs[0]])
        cells = [pid, desc, f or "the pairs"]
        for p in progs:
            res = results_by_program[p]
            for op in ("save", "unrelated", "entry"):
                r = res.get((op, f), {}) if f else {}
                cells.append("not opened" if "(no file)" in r else r.get(pid, "-"))
            cells.append("; ".join(res.get(("merge", m), {}).get(pid, "-") for m in merges))
        cells.append("; ".join(results_by_program["kplib"].get(("merge", m), {}).get(pid, "-") for m in merges))
        md.append("| " + " | ".join(x.replace("|", "/") for x in cells) + " |")
    (c.OUT / "places-table.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print("\n".join(md))


if __name__ == "__main__":
    main()
