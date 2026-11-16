"""What every script of this family shares: the paths, running a harness and the dumper, and the
normalized comparison of a harness's dump with a recorded dump.

The normalization is the whole of what is set aside when a harness's file is held to the emulator's:
  - the first line, which names the file and its hash (salts and IVs change every save);
  - an identifier the program made at random (`~` and eight hex digits), written `~new`;
  - a time from the moment of the run, any time on or after NOW_CUTOFF that is not inside a quoted
    value, written `<now>`, in a line and in a history item's key;
then the lines are sorted again, since a masked time can move a line.
"""
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
STORAGE = HERE.parent.parent
ANDROID = STORAGE / "android"
RECORDED_ANDROID = ANDROID / "recorded"
WORK = HERE / "work"
OUT = HERE / "out"
BASE = WORK / "base"

DUMPER = HERE / "dumper" / "bin" / "Release" / "net48" / "StoreProbe.exe"
DUMPER2 = HERE / "dumper2" / "bin" / "Release" / "net48" / "StoreProbeDX.exe"
K2A = HERE / "k2a" / "bin" / "Release" / "net10.0" / "K2aHarness.exe"
KPDX_DIR = HERE / "kpdx"

NOW_CUTOFF = "2026-11-15T12:00:00Z"
TIME = re.compile(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ")
RANDOM_ID = re.compile(r"~[0-9a-f]{8}\b")


def run(cmd, **kw):
    r = subprocess.run([str(c) for c in cmd], capture_output=True, text=True, encoding="utf-8", **kw)
    if r.returncode != 0:
        sys.stderr.write(r.stdout[-4000:] + r.stderr[-4000:])
    return r


def run_kpdx(args):
    """The KeePassDX harness, through the launcher kpdx\\build.ps1 writes. A batch whose job did not
    end leaves the JVM with code 3 and `RESUME <n>`; it is started again from job n, and the
    outputs of every start are joined."""
    args = [str(a) for a in args]
    outs, errs = [], []
    while True:
        r = run(["cmd.exe", "/c", KPDX_DIR / "bin" / "harness.cmd"] + args)
        outs.append(r.stdout)
        errs.append(r.stderr)
        resume = [l for l in r.stdout.splitlines() if l.startswith("RESUME ")]
        if r.returncode != 3 or not resume or args[0] != "batch":
            r.stdout, r.stderr = "".join(outs), "".join(errs)
            return r
        args = args[:2] + [resume[-1].split()[1]]


def dumpdir(directory, dumper=DUMPER):
    """<file>.dump.txt beside every .kdbx under the directory, by the emulator measurement's probe."""
    r = run([dumper, "dumpdir", directory])
    if r.returncode != 0:
        raise SystemExit("dumper failed on " + str(directory))


def read_dump(path):
    return Path(path).read_text(encoding="utf-8").splitlines()


def _mask_times(line):
    res = []
    pos = 0
    for m in TIME.finditer(line):
        start = m.start()
        # count unescaped quotes before the match
        seg = line[:start]
        n = 0
        k = 0
        while k < len(seg):
            if seg[k] == "\\":
                k += 2
                continue
            if seg[k] == '"':
                n += 1
            k += 1
        inside = n % 2 == 1
        res.append(line[pos:start])
        if not inside and m.group(0) >= NOW_CUTOFF:
            res.append("<now>")
        else:
            res.append(m.group(0))
        pos = m.end()
    res.append(line[pos:])
    return "".join(res)


def normalize(lines, keep_first=False):
    body = lines if keep_first else [l for l in lines if not l.startswith("# file ")]
    out = []
    for l in body:
        l = RANDOM_ID.sub("~new", l)
        l = _mask_times(l)
        out.append(l)
    out.sort()
    return out


def diff(a, b):
    """Lines only in a, lines only in b, counting repeated lines."""
    from collections import Counter
    ca, cb = Counter(a), Counter(b)
    return sorted((ca - cb).elements()), sorted((cb - ca).elements())
