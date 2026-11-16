"""The recorded emulator cells, as harness scripts.

A cell names the steps the emulator flow took that change the object model or write the file, in
the harness's script language (k2a\\Driver.cs and kpdx\\Driver.kt read the same one), and the
record's dumps it is held to. {base}, {out} and {work} are filled in by calibrate.py.
"""
from collections import namedtuple

import common as c

Cell = namedtuple("Cell", "name kind script files variants", defaults=((),))
R = c.RECORDED_ANDROID


def _k2a_first():
    runs = R / "runs" / "k2a"
    out = []
    for run, base in (("save-argon", "S-argon"), ("save-aes", "S-aes"), ("save-aes-nocd", "S-aes-nocd"),
                      ("save-aes-plain", "S-aes-plain")):
        out.append(Cell("first-" + run, "save", [
            "open {base}/%s.kdbx" % base,
            "add Root droid-added",
            "save {out}/k2a-%s.kdbx" % run,
        ], [("k2a-%s.kdbx" % run, runs / run / ("k2a-%s.kdbx.dump.txt" % run))]))
    out.append(Cell("first-edit-e1", "edit", [
        "open {base}/S-argon.kdbx",
        "view E1",
        "edit E1 Password append -phone",
        "save {out}/k2a-edit-e1.kdbx",
    ], [("k2a-edit-e1.kdbx", runs / "edit-e1" / "k2a-edit-e1.kdbx.dump.txt")]))
    out.append(Cell("first-edit-carriers", "edit", [
        "open {base}/S-argon.kdbx",
        "view R1",
        "edit R1 Title append -phone",
        "save {out}/k2a-edit-carriers.kdbx",
        "view R1",
        "view Gm",
        "edit Gm Title append -phone",
        "save {out}/k2a-edit-carriers.kdbx",
    ], [("k2a-edit-carriers.kdbx", runs / "edit-carriers" / "k2a-edit-carriers.kdbx.dump.txt")]))
    out.append(Cell("first-rename-g", "edit", [
        "open {base}/S-argon.kdbx",
        "rename-group G -phone",
        "save {out}/k2a-rename-g.kdbx",
    ], [("k2a-rename-g.kdbx", runs / "rename-g" / "k2a-rename-g.kdbx.dump.txt")]))
    out.append(Cell("first-delete", "delete", [
        "open {base}/S-argon.kdbx",
        "recycle E2",
        "save {out}/mid-recycled.kdbx",
        "purge E2",
        "save {out}/k2a-delete.kdbx",
    ], [("mid-recycled.kdbx", runs / "delete" / "mid-recycled.kdbx.dump.txt"),
        ("k2a-delete.kdbx", runs / "delete" / "k2a-delete.kdbx.dump.txt")]))
    out.append(Cell("first-move", "move", [
        "open {base}/S-argon.kdbx",
        "move-entry E1 N",
        "save {out}/mid-moved-e1.kdbx",
        "move-group N Root",
        "save {out}/k2a-move.kdbx",
    ], [("mid-moved-e1.kdbx", runs / "move" / "mid-moved-e1.kdbx.dump.txt"),
        ("k2a-move.kdbx", runs / "move" / "k2a-move.kdbx.dump.txt")]))
    for d, target, arriving in (("CP", "C", "P"), ("PC", "P", "C")):
        for r in (1, 2, 3):
            run = "merge-%s-r%d" % (d, r)
            out.append(Cell("first-" + run, "merge", [
                "open {base}/%s.kdbx" % target,
                "add Root droid-merge",
                "merge {base}/%s.kdbx {out}/k2a-%s.kdbx" % (arriving, run),
            ], [("k2a-%s.kdbx" % run, runs / run / ("k2a-%s.kdbx.dump.txt" % run))]))
    return out


def _headers(log_dir):
    """The first line of every step log: run name and the flow's parameters."""
    out = []
    for log in sorted(log_dir.glob("*.log")):
        first = log.read_text(encoding="utf-8").splitlines()[0]
        words = first.split()
        params = dict(w.split("=", 1) for w in words if "=" in w and not w.startswith("("))
        out.append((log.stem, params))
    return out


# What each overwrite flow leaves for the harness, from the flow's own comment (scripts\flows):
#   fg, bg, qu, setting: OLD stays open; the arriving copy is put over the file; an entry is added
#       and saved; the save's question answered ANSWER (none: no question, the hash was the same,
#       or the check was off): overwrite and none write the open copy, merge merges the file in,
#       cancel writes nothing
#   setting-reopen: lock, close, reopen OLD, then the replacement, then as above
#   close, openclose: the database is closed while the file is replaced, reopened (the arriving
#       copy), then an entry is added and saved
#   sync: "Synchronize database..." of a local file is CheckDatabaseForChanges, which reloads the
#       changed file (TriggerReload); then an entry is added and saved
#   look: nothing is saved
def _k2a_overwrite():
    rec = R / "overwrite"
    out = []
    for run, p in _headers(rec / "logs" / "runs" / "k2a"):
        flow = p.get("flow", "")
        arriving, entry, answer = p.get("ARRIVING"), p.get("ENTRY"), p.get("ANSWER", "none")
        dump = rec / "runs" / "k2a" / run / ("k2a-%s.kdbx.dump.txt" % run)
        name = "k2a-%s.kdbx" % run
        if flow in ("k2a-ow-look.flow",) or answer == "cancel":
            continue  # nothing written: the file is the arriving copy, no harness step
        if flow in ("k2a-ow-close.flow", "k2a-ow-openclose.flow", "k2a-ow-sync.flow"):
            script = ["open {base}/%s.kdbx" % arriving, "add Root %s" % entry, "save {out}/%s" % name]
            kind = "reopen"
        elif answer == "merge":
            script = ["open {base}/OLD.kdbx", "add Root %s" % entry, "merge {base}/%s.kdbx {out}/%s" % (arriving, name)]
            kind = "merge"
        else:
            script = ["open {base}/OLD.kdbx", "add Root %s" % entry, "save {out}/%s" % name]
            kind = "overwrite"
        out.append(Cell("ow-" + run, "ow-" + kind, script, [(name, dump)]))
    return out


# The synchronize record: the phone's edits are those of k2a-sy-pw-<X>.flow (the entry shown, its
# password appended -phone and saved, the entry shown again on return) and k2a-ow-edit.flow (an
# entry added at the root); a run's own SNEW is made from the phone's file by the probe's newsync,
# as mksnew.sh made it; the synchronize or the save then merges the server's file in or writes over
# it, as the run's answer was. Cases 1, 2 and 8 write nothing of the program's (a reload, a copy of
# the server's file into the cache) and have no cell.
def _pw(x, outname):
    return ["view %s" % x, "edit %s Password append -phone" % x, "save {out}/%s" % outname, "view %s" % x]


def _k2a_synchronize():
    rec = R / "k2a-synchronize"
    out = []
    probe = str(c.DUMPER)
    for run, p in _headers(rec / "logs" / "sync"):
        flow = p.get("flow", "")
        runs = rec / "runs" / run
        if flow in ("k2a-sy-s1.flow", "k2a-sy-s2.flow", "k2a-sy-s8.flow"):
            continue
        files = []
        variants = []
        if flow in ("k2a-sy-s3a.flow", "k2a-sy-s3b.flow", "k2a-sy-s3c.flow"):
            # the save of the edit is what reaches the server, by upload or synchronize
            script = ["open {base}/SOLD.kdbx", "view P2", "edit P2 Password append -phone", "save {out}/server.kdbx"]
            kind = "sy-phone-only"
        elif flow in ("k2a-sy-s4.flow", "k2a-sy-s4b.flow"):
            script = ["open {base}/SOLD.kdbx"]
            for x in ("P2", "Q1", "Q2", "Q3"):
                script += _pw(x, "phone-before-sync.kdbx")
            script += ["add Root %s" % p.get("ENTRY", "phone-new"), "save {out}/phone-before-sync.kdbx",
                       "exec %s | newsync | {base}/SOLD.kdbx | {out}/SNEW.kdbx | {out}/phone-before-sync.kdbx" % probe]
            if p.get("SANSWER") == "merge":
                script += ["merge {out}/SNEW.kdbx {out}/server.kdbx"]
                kind = "sy-both-merge"
            else:
                script += ["save {out}/server.kdbx"]
                kind = "sy-both-overwrite"
            files.append(("phone-before-sync.kdbx", runs / "phone-before-sync.kdbx.dump.txt"))
            files.append(("SNEW.kdbx", runs / "SNEW.kdbx.dump.txt"))
        else:  # s5, s6, s7, s9: SNEW-fixed on the server, P2 edited and saved
            # The entry is shown again when the editor returns (EntryActivity, Touch(false)) on the
            # UI thread while the save runs on a worker thread; whether the write sees that touch
            # is a race, which the variant names.
            head = ["open {base}/SOLD.kdbx", "view P2", "edit P2 Password append -phone"]
            if p.get("ANSWER") == "overwrite":
                last = "save {out}/server.kdbx"
                kind = "sy-save-overwrite"
            else:  # merge, or Always merge on conflict
                last = "merge {base}/SNEW-fixed.kdbx {out}/server.kdbx"
                kind = "sy-save-merge"
            script = head + [last]
            variants = [("re-shown entry written", head + ["view P2", last])]
        for f in ("server", "cache", "cache-after-sync-cache-copy"):
            d = runs / ("%s.kdbx.dump.txt" % f)
            if d.exists():
                files.append(("server.kdbx", d))
        out.append(Cell("sy-" + run, kind, script, files, tuple(variants)))
    return out


def cells(program):
    if program == "k2a":
        return _k2a_first() + _k2a_overwrite() + _k2a_synchronize()
    import cells_kpdx
    import cells_kpdx2
    return cells_kpdx.cells() + cells_kpdx2.cells()
