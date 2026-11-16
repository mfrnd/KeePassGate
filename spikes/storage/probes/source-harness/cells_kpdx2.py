"""KeePassDX's merge cells of the second emulator measurement (2026-11-16), as harness scripts.

That measurement's record was not yet carried on the branch when the harness ran: its working
directory, `android-keepassdx\\` in the session's scratch directory, held the bases (`work\\base\\`),
the dumps of every file the program left (`work\\B\\kpdx\\<run>\\`) and the step logs
(`out\\B\\kpdx\\`). A copy of those three, frozen when the calibration ran, is what this module reads,
from the directory named by the environment variable KPDX_RECORD2 (default work\\kpdx-record2\\,
with `base\\`, `B\\<run>\\` and `logs\\`).

Each run's first log line names its target (the copy open) and its source (the arriving copy):
  mf<XY>    "Merge from ...": the target open, the source merged in, written at once
  extm<XY>  the external-change dialog, OK, "Merge data": the same merge
  exts<XY>  the same dialog, OK, "Save data": the copy in memory written over the file
  extr<XY>  the same dialog, OK, "Reload data": nothing written, unless an entry was added after
            the reload (the log types its title), which writes the source with that entry
"""
import os
import re
from pathlib import Path

import common as c
from cells_android import Cell

RECORD = Path(os.environ.get("KPDX_RECORD2", str(c.WORK / "kpdx-record2")))


def cells():
    out = []
    logs = RECORD / "logs"
    if not logs.exists():
        return out
    for log in sorted(logs.glob("*.log")):
        text = log.read_text(encoding="utf-8", errors="replace").splitlines()
        m = re.search(r" B kpdx (\S+) (r\d+) target=(\S+) source=(\S+)", text[0])
        if not m:
            continue
        op, r, target, source = m.groups()
        run = "%s-%s" % (op, r)
        dump = RECORD / "B" / run / ("kpdx-%s.kdbx.dump.txt" % op)
        if not dump.exists():
            continue
        name = "kpdx-%s.kdbx" % op
        base = RECORD / "base"
        files = [(name, dump)]
        # The group screen shows the root once the database is open, or once it is reloaded,
        # and that touch is in every file of this record (TouchGroupRunnable).
        if op.startswith("mf") or op.startswith("extm"):
            script = ["open %s" % (base / (target + ".kdbx")), "view-group Root",
                      "merge %s {out}/%s" % (base / (source + ".kdbx"), name)]
            kind = "r2-merge"
            # A run whose file is a base unchanged is one whose merge never ended on the device
            # (that measurement's notes: the progress dialog still up after 90 s, the process then
            # stopped); its cell agrees when the harness's merge does not end either.
            body = [l for l in dump.read_text(encoding="utf-8").splitlines() if not l.startswith("# file ")]
            for b in (target, source):
                bd = base / (b + ".kdbx.dump.txt")
                if bd.exists() and body == [l for l in bd.read_text(encoding="utf-8").splitlines() if not l.startswith("# file ")]:
                    kind, files = "r2-merge-did-not-end", []
        elif op.startswith("exts"):
            script = ["open %s" % (base / (target + ".kdbx")), "view-group Root", "save {out}/%s" % name]
            kind = "r2-save-data"
        else:
            titles = [re.search(r"type \^Title\$ (\S+)", l).group(1) for l in text if re.search(r"type \^Title\$ (\S+)", l)]
            if not titles:
                continue  # reload alone writes nothing
            script = ["open %s" % (base / (source + ".kdbx")), "view-group Root", "add Root %s" % titles[-1],
                      "save {out}/%s" % name]
            kind = "r2-reload-add"
        out.append(Cell("r2-" + run, kind, script, files))
    return out
