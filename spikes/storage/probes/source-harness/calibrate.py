"""Calibration: the emulator's recorded cells, made again by the harnesses and held to the record.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\source-harness\\calibrate.py [k2a] [kpdx] [-v]

Each cell is a script for a harness (the steps the emulator flow took, as far as a step changes
the object model or writes the file), the record's dumps of the files the program left, and the
kind of cell. The harness writes work\\calibrate\\<program>\\<cell>\\, the dumper dumps it, and every
dump is compared with the record after common.normalize.

A cell may carry variants: the same steps with a step of the app's surrounding code placed
otherwise (a touch made by the screen while a save runs on another thread). A cell agrees when its
own script agrees; "agrees as <variant>" names the variant that agrees when the script does not.
Prints one line per cell and the counts per kind, and writes the same to out\\calibrate-<program>.txt.
"""
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402
import cells_android as cells  # noqa: E402

VERBOSE = "-v" in sys.argv


def _compare(d, files):
    verdicts = []
    for fname, rec in files:
        got = d / (fname + ".dump.txt")
        if not got.exists():
            verdicts.append((fname, "missing", [], []))
            continue
        a = c.normalize(c.read_dump(got))
        b = c.normalize(c.read_dump(rec))
        only_h, only_r = c.diff(a, b)
        verdicts.append((fname, "agrees" if not only_h and not only_r else "differs", only_h, only_r))
    return verdicts


def run_program(program, cell_list):
    outdir = c.WORK / "calibrate" / program
    if outdir.exists():
        shutil.rmtree(outdir)
    outdir.mkdir(parents=True)
    jobs = []
    for cell in cell_list:
        runs = [(cell.name, cell.script)] + [("%s~%d" % (cell.name, i + 1), s) for i, (_, s) in enumerate(cell.variants)]
        for name, script in runs:
            d = outdir / name
            d.mkdir(parents=True, exist_ok=True)
            jobs.append("job " + name)
            jobs += [line.format(base=c.BASE, out=d, work=c.WORK) for line in script]
            jobs.append("end")
    jobs_file = outdir / "jobs.txt"
    jobs_file.write_text("\n".join(jobs) + "\n", encoding="utf-8")
    if program == "k2a":
        r = c.run([c.K2A, "batch", jobs_file])
    else:
        # A job of the calibration is given a minute of its own processor time, where a finished
        # merge takes under two seconds.
        os.environ.setdefault("KPDX_JOB_TIMEOUT", "60")
        r = c.run_kpdx(["batch", jobs_file])
    (outdir / "harness.log").write_text(r.stdout + r.stderr, encoding="utf-8")
    c.dumpdir(outdir)
    # The second KeePassDX record names its nodes by its own probe's aliases: its cells' files
    # are dumped again by that probe (dumper2\, cells_kpdx2.py).
    for cell in cell_list:
        if cell.name.startswith("r2-") and any((outdir / cell.name).glob("*.kdbx")):
            c.dumpdir(outdir / cell.name, c.DUMPER2)
    lines, kinds = [], {}
    log = (outdir / "harness.log").read_text(encoding="utf-8").splitlines()
    for cell in cell_list:
        verdicts = _compare(outdir / cell.name, cell.files)
        ok = all(v[1] == "agrees" for v in verdicts)
        if not cell.files:
            # A cell with no file is a run whose program never ended: it agrees when the
            # harness's job did not end either and wrote nothing.
            timed_out = any(x.startswith("FAILED %s: TIMEOUT" % cell.name) for x in log)
            wrote = any((outdir / cell.name).glob("*.kdbx"))
            ok = timed_out and not wrote
            if not ok:
                verdicts = [("(no file expected)", "the harness's job ended" if not timed_out else "a file was written", [], [])]
        as_variant = None
        if not ok:
            for i, (label, _) in enumerate(cell.variants):
                if all(v[1] == "agrees" for v in _compare(outdir / ("%s~%d" % (cell.name, i + 1)), cell.files)):
                    as_variant = label
                    break
        k = kinds.setdefault(cell.kind, [0, 0, 0])
        k[0] += 1
        k[1] += 1 if ok else 0
        k[2] += 1 if as_variant else 0
        verdict = "agrees" if ok else ("agrees as variant: " + as_variant if as_variant else "DIFFERS")
        lines.append("%-6s %-40s %-18s %s" % (program, cell.name, cell.kind, verdict))
        if not ok:
            for fname, v, oh, orr in verdicts:
                if v != "agrees":
                    lines.append("    %s: %s, %d lines only in the harness, %d only in the record" % (fname, v, len(oh), len(orr)))
                    if VERBOSE or len(oh) + len(orr) <= 40:
                        lines += ["      harness  " + x for x in oh]
                        lines += ["      record   " + x for x in orr]
    lines.append("")
    lines += ["harness: " + x for x in log if x.startswith("FAILED") or x.startswith(program + ":")]
    lines.append("")
    total = [0, 0, 0]
    for kind, (n, ok, var) in sorted(kinds.items()):
        lines.append("%-6s %-18s %3d of %3d agree%s" % (program, kind, ok, n, ("; %d more as a variant" % var) if var else ""))
        total = [total[0] + n, total[1] + ok, total[2] + var]
    lines.append("%-6s %-18s %3d of %3d agree%s" % (program, "all", total[1], total[0], ("; %d more as a variant" % total[2]) if total[2] else ""))
    text = "\n".join(lines) + "\n"
    (c.OUT / ("calibrate-%s.txt" % program)).write_text(text, encoding="utf-8")
    sys.stdout.write(text)


def main():
    progs = [p for p in sys.argv[1:] if not p.startswith("-")] or ["k2a", "kpdx"]
    for p in progs:
        run_program(p, cells.cells(p))


if __name__ == "__main__":
    main()
