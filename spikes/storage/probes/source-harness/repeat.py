"""The same KeePassDX merge, many times: how often it never ends.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\source-harness\\repeat.py [N] [timeout]

Two runs of the second KeePassDX record never ended on the device (cells_kpdx2.py), where other
runs of the same two copies did. The harness's job for the same merge, PHONE open with DESK
arriving, and the reverse, runs N times (default 30) in one JVM, each given `timeout` seconds
(default 30). Every file written is dumped by that record's probe and held to the record's finished
run of the same direction (mfPD-r1, mfDP-r1); every job that did not end is counted with the place
the harness found it in. Writes out\\repeat-kpdx.txt.
"""
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402
from cells_kpdx2 import RECORD  # noqa: E402


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 30
    timeout = sys.argv[2] if len(sys.argv) > 2 else "30"
    out = c.WORK / "repeat"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    base = RECORD / "base"
    jobs = []
    for d, target, source in (("PD", "PHONE", "DESK"), ("DP", "DESK", "PHONE")):
        for i in range(1, n + 1):
            name = "%s-%02d" % (d, i)
            (out / name).mkdir()
            jobs += ["job " + name, "open %s" % (base / (target + ".kdbx")), "view-group Root",
                     "merge %s %s" % (base / (source + ".kdbx"), out / name / ("kpdx-mf%s.kdbx" % d)), "end"]
    jf = out / "jobs.txt"
    jf.write_text("\n".join(jobs) + "\n", encoding="utf-8")
    os.environ["KPDX_JOB_TIMEOUT"] = timeout
    r = c.run_kpdx(["batch", jf])
    log = (r.stdout + r.stderr).splitlines()
    c.dumpdir(out, c.DUMPER2)
    lines = []
    for d in ("PD", "DP"):
        rec = c.normalize(c.read_dump(RECORD / "B" / ("mf%s-r1" % d) / ("kpdx-mf%s.kdbx.dump.txt" % d)))
        ended = same = 0
        stuck = [x for x in log if x.startswith("FAILED %s-" % d) and "TIMEOUT" in x]
        for i in range(1, n + 1):
            f = out / ("%s-%02d" % (d, i)) / ("kpdx-mf%s.kdbx.dump.txt" % d)
            if f.exists():
                ended += 1
                a, b = c.diff(c.normalize(c.read_dump(f)), rec)
                same += 1 if not a and not b else 0
        lines.append("mf%s: %d runs, %d ended and wrote a file, %d of those the record's mf%s-r1 line for line, %d did not end in %s s"
                     % (d, n, ended, same, d, len(stuck), timeout))
        lines += ["  " + x for x in stuck[:3]]
    lines += [x for x in log if x.startswith("kpdx:")]
    text = "\n".join(lines) + "\n"
    (c.OUT / "repeat-kpdx.txt").write_text(text, encoding="utf-8")
    sys.stdout.write(text)


if __name__ == "__main__":
    main()
