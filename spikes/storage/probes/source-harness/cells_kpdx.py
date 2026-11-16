"""The recorded KeePassDX 4.5.8 cells of the first emulator measurement, as harness scripts.

Each script follows its flow under spikes\\storage\\android\\scripts\\flows\\kpdx-*.flow, step by step
as far as a step changes the object model or writes the file: the root group is shown when the
database opens and every group the flow goes into is shown (TouchGroupRunnable), an entry selected
by a long press is not shown, and every action saves at once (auto-save on). A group's touch moves
only its access time, which common.normalize sets aside with every time of the run. The `-src`
dump a merge cell's record holds is the copy pushed beside the target, which the program reads and
does not write; it is not a cell.
"""
import common as c
from cells_android import Cell

R = c.RECORDED_ANDROID / "runs" / "kpdx"


def _dump(run, name=None):
    name = name or "kpdx-%s.kdbx" % run
    return (name, R / run / (name + ".dump.txt"))


def cells():
    out = []
    for run, base in (("save-argon", "S-argon"), ("save-aes", "S-aes"), ("save-aes-nocd", "S-aes-nocd"),
                      ("save-aes-plain", "S-aes-plain")):
        out.append(Cell("first-" + run, "save", [
            "open {base}/%s.kdbx" % base,
            "view-group Root",
            "add Root droid-added",
            "save {out}/kpdx-%s.kdbx" % run,
        ], [_dump(run)]))
    # The flow meant to append to the password; its log records the text landing in the title
    # ("appended '-phone': 'E1-phone'"), and the record's file holds that, so the cell does too.
    out.append(Cell("first-edit-e1", "edit", [
        "open {base}/S-argon.kdbx",
        "view-group Root",
        "view-group G",
        "edit E1 Title append -phone",
        "save {out}/kpdx-edit-e1.kdbx",
    ], [_dump("edit-e1")]))
    out.append(Cell("first-edit-carriers", "edit", [
        "open {base}/S-argon.kdbx",
        "view-group Root",
        "view-group GKPG",
        "edit R1 Title append -phone",
        "save {out}/kpdx-edit-carriers.kdbx",
        "view-group Root",
        "view-group G",
        "edit Gm Title append -phone",
        "save {out}/kpdx-edit-carriers.kdbx",
    ], [_dump("edit-carriers")]))
    out.append(Cell("first-rename-g", "edit", [
        "open {base}/S-argon.kdbx",
        "view-group Root",
        "rename-group G -phone",
        "save {out}/kpdx-rename-g.kdbx",
    ], [_dump("rename-g")]))
    out.append(Cell("first-delete", "delete", [
        "open {base}/S-argon.kdbx",
        "view-group Root",
        "view-group G",
        "recycle E2",
        "save {out}/mid-recycled.kdbx",
        "view-group Root",
        "view-group Bin",
        "empty-bin",
        "save {out}/kpdx-delete.kdbx",
    ], [_dump("delete", "mid-recycled.kdbx"), _dump("delete")]))
    out.append(Cell("first-move", "move", [
        "open {base}/S-argon.kdbx",
        "view-group Root",
        "view-group G",
        "move-entry E1 N",
        "view-group N",
        "save {out}/mid-moved-e1.kdbx",
        "view-group G",
        "move-group N Root",
        "view-group Root",
        "save {out}/kpdx-move.kdbx",
    ], [_dump("move", "mid-moved-e1.kdbx"), _dump("move")]))
    for d, target, source in (("CP", "C", "P"), ("PC", "P", "C")):
        for r in (1, 2, 3):
            run = "merge-from%s-r%d" % (d, r)
            out.append(Cell("first-" + run, "merge", [
                "open {base}/%s.kdbx" % target,
                "view-group Root",
                "merge {base}/%s.kdbx {out}/kpdx-%s.kdbx" % (source, run),
            ], [_dump(run)]))
    return out
