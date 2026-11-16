r"""Turns what `run.py` wrote into `out\` into the tables the report quotes, `tables.md` beside it.

    summarize.py <out dir>

Every cell is computed from the comparison files; nothing here is typed in. A place is one or
more dump paths (`PLACES`); a save cell names what the program did to all of that place's paths,
and a merge cell names, out of the four runs of a program and method (A older or the two equal,
each copy open in turn), which copy's value the result holds.
"""
from __future__ import annotations

import functools
import re
import sys
from collections import OrderedDict
from pathlib import Path

G = r"X/Root/Group\[root\]/Group\[places\]"
E = G + r"/Entry\[entry\]"
M = r"X/Meta"

# id, what it is, the dump paths it covers (a regular expression matched whole)
PLACES = [
    ("H.version", "format version", r"H\.version"),
    ("H.comment", "outer header field 1, comment", r"H\.Comment"),
    ("H.kdf", "a key of the plugin's own in the KDF parameters", r"H\.kdf\[KPG_kdf\]"),
    ("H.kdf.only", "a KDF key only one copy holds", r"H\.kdf\[KPG_kdf_only[AB]\]"),
    ("H.pcd", "public custom data, a string key both copies hold", r"H\.pcd\[KPG_shared\]"),
    ("H.pcd.uint64", "public custom data, a UInt64 key", r"H\.pcd\[KPG_counter\]"),
    ("H.pcd.bytes", "public custom data, a byte array key", r"H\.pcd\[KPG_bytes\]"),
    ("H.pcd.only", "public custom data, a key only one copy holds", r"H\.pcd\[KPG_only[AB]\]"),
    ("H.eoh", "end of header field's content", r"H\.EndOfHeader"),
    ("H.field13", "an outer header field of an undefined identifier", r"H\.field13"),
    ("I.unreferenced", "an inner header binary no entry refers to", r"I\.Binary\[I\.unreferenced\](#\d+)?"),
    ("I.binary", "an attachment's binary and its flags", r"I\.Binary\[E\.binary\]"),
    ("I.field4", "an inner header field of an undefined identifier", r"I\.field4"),
    ("T.trailing", "bytes after the last block", r"T\.trailing"),
    ("M.generator", "Generator", M + r"/Generator"),
    ("M.settings-changed", "SettingsChanged", M + r"/SettingsChanged"),
    ("M.name", "DatabaseName", M + r"/DatabaseName"),
    ("M.name-changed", "DatabaseNameChanged", M + r"/DatabaseNameChanged"),
    ("M.desc", "DatabaseDescription", M + r"/DatabaseDescription"),
    ("M.desc-changed", "DatabaseDescriptionChanged", M + r"/DatabaseDescriptionChanged"),
    ("M.user", "DefaultUserName", M + r"/DefaultUserName"),
    ("M.user-changed", "DefaultUserNameChanged", M + r"/DefaultUserNameChanged"),
    ("M.history-days", "MaintenanceHistoryDays", M + r"/MaintenanceHistoryDays"),
    ("M.color", "Color", M + r"/Color"),
    ("M.key-changed", "MasterKeyChanged", M + r"/MasterKeyChanged"),
    ("M.key-rec", "MasterKeyChangeRec", M + r"/MasterKeyChangeRec"),
    ("M.key-force", "MasterKeyChangeForce", M + r"/MasterKeyChangeForce"),
    ("M.key-force-once", "MasterKeyChangeForceOnce", M + r"/MasterKeyChangeForceOnce"),
    ("M.memprot", "MemoryProtection, the five flags", M + r"/MemoryProtection/Protect\w+"),
    ("M.icon.data", "a custom icon's data", M + r"/CustomIcons/Icon\[icon-used\]/Data"),
    ("M.icon.name", "a custom icon's name (4.1)", M + r"/CustomIcons/Icon\[icon-used\]/Name"),
    ("M.icon.time", "a custom icon's time (4.1)", M + r"/CustomIcons/Icon\[icon-used\]/LastModificationTime"),
    ("M.icon.unused", "a custom icon no node uses", M + r"/CustomIcons/Icon\[icon-unused\]/UUID"),
    ("M.icon.only", "a custom icon only one copy holds", M + r"/CustomIcons/Icon\[icon-only[AB]\]/UUID"),
    ("M.bin-enabled", "RecycleBinEnabled", M + r"/RecycleBinEnabled"),
    ("M.bin-uuid", "RecycleBinUUID", M + r"/RecycleBinUUID"),
    ("M.bin-changed", "RecycleBinChanged", M + r"/RecycleBinChanged"),
    ("M.template", "EntryTemplatesGroup", M + r"/EntryTemplatesGroup"),
    ("M.template-changed", "EntryTemplatesGroupChanged", M + r"/EntryTemplatesGroupChanged"),
    ("M.history-max-items", "HistoryMaxItems", M + r"/HistoryMaxItems"),
    ("M.history-max-size", "HistoryMaxSize", M + r"/HistoryMaxSize"),
    ("M.last-selected", "LastSelectedGroup", M + r"/LastSelectedGroup"),
    ("M.last-top-visible", "LastTopVisibleGroup", M + r"/LastTopVisibleGroup"),
    ("M.cd", "the database's custom data, a key both copies hold", M + r"/CustomData/Item\[kpg-shared\]/Value"),
    ("M.cd.time", "its per-key time (4.1)", M + r"/CustomData/Item\[kpg-shared\]/LastModificationTime"),
    ("M.cd.only", "a key only one copy holds", M + r"/CustomData/Item\[kpg-only[AB]\]/Value"),
    ("G.name", "group Name", G + r"/Name"),
    ("G.notes", "group Notes", G + r"/Notes"),
    ("G.icon", "group IconID", G + r"/IconID"),
    ("G.custom-icon", "group CustomIconUUID", G + r"/CustomIconUUID"),
    ("G.times.creation", "group CreationTime", G + r"/Times/CreationTime"),
    ("G.times.lastmod", "group LastModificationTime", G + r"/Times/LastModificationTime"),
    ("G.times.last-access", "group LastAccessTime", G + r"/Times/LastAccessTime"),
    ("G.times.expiry", "group ExpiryTime", G + r"/Times/ExpiryTime"),
    ("G.times.expires", "group Expires", G + r"/Times/Expires"),
    ("G.times.usage", "group UsageCount", G + r"/Times/UsageCount"),
    ("G.times.location", "group LocationChanged", G + r"/Times/LocationChanged"),
    ("G.expanded", "group IsExpanded", G + r"/IsExpanded"),
    ("G.at-sequence", "group DefaultAutoTypeSequence", G + r"/DefaultAutoTypeSequence"),
    ("G.enable-auto-type", "group EnableAutoType", G + r"/EnableAutoType"),
    ("G.enable-searching", "group EnableSearching", G + r"/EnableSearching"),
    ("G.last-top-entry", "group LastTopVisibleEntry", G + r"/LastTopVisibleEntry"),
    ("G.previous-parent", "group PreviousParentGroup (4.1)", G + r"/PreviousParentGroup"),
    ("G.tags", "group Tags (4.1)", G + r"/Tags"),
    ("G.cd", "group custom data, a key both copies hold", G + r"/CustomData/Item\[kpg-shared\]/Value"),
    ("G.cd.time", "a time on a group custom data item", G + r"/CustomData/Item\[kpg-shared\]/LastModificationTime"),
    ("G.cd.only", "group custom data, a key only one copy holds", G + r"/CustomData/Item\[kpg-only[AB]\]/Value"),
    ("E.icon", "entry IconID", E + r"/IconID"),
    ("E.custom-icon", "entry CustomIconUUID", E + r"/CustomIconUUID"),
    ("E.fg", "entry ForegroundColor", E + r"/ForegroundColor"),
    ("E.bg", "entry BackgroundColor", E + r"/BackgroundColor"),
    ("E.override-url", "entry OverrideURL", E + r"/OverrideURL"),
    ("E.quality-check", "entry QualityCheck (4.1)", E + r"/QualityCheck"),
    ("E.tags", "entry Tags", E + r"/Tags"),
    ("E.previous-parent", "entry PreviousParentGroup (4.1)", E + r"/PreviousParentGroup"),
    ("E.times.creation", "entry CreationTime", E + r"/Times/CreationTime"),
    ("E.times.lastmod", "entry LastModificationTime", E + r"/Times/LastModificationTime"),
    ("E.times.last-access", "entry LastAccessTime", E + r"/Times/LastAccessTime"),
    ("E.times.expiry", "entry ExpiryTime", E + r"/Times/ExpiryTime"),
    ("E.times.expires", "entry Expires", E + r"/Times/Expires"),
    ("E.times.usage", "entry UsageCount", E + r"/Times/UsageCount"),
    ("E.times.location", "entry LocationChanged", E + r"/Times/LocationChanged"),
    ("E.title", "entry Title", E + r"/String\[Title\]/Value"),
    ("E.string", "entry custom string", E + r"/String\[kpg-plain\]/Value"),
    ("E.protected", "entry protected custom string", E + r"/String\[kpg-protected\]/Value"),
    ("E.protected.flag", "its Protected attribute", E + r"/String\[kpg-protected\]/Value@Protected"),
    ("E.binary", "entry attachment reference", E + r"/Binary\[kpg-att\.txt\]/Value"),
    ("E.at.enabled", "entry AutoType Enabled", E + r"/AutoType/Enabled"),
    ("E.at.obfuscation", "entry AutoType DataTransferObfuscation", E + r"/AutoType/DataTransferObfuscation"),
    ("E.at.sequence", "entry AutoType DefaultSequence", E + r"/AutoType/DefaultSequence"),
    ("E.at.association", "entry AutoType Association", E + r"/AutoType/Association\[[^\]]*\]/(Window|KeystrokeSequence)"),
    ("E.history", "entry History item", E + r"/History/Entry\[\d+\]/String\[Title\]/Value"),
    ("E.cd", "entry custom data, a key both copies hold", E + r"/CustomData/Item\[kpg-shared\]/Value"),
    ("E.cd.time", "a time on an entry custom data item", E + r"/CustomData/Item\[kpg-shared\]/LastModificationTime"),
    ("E.cd.only", "entry custom data, a key only one copy holds", E + r"/CustomData/Item\[kpg-only[AB]\]/Value"),
    ("D.shared", "a deletion record both copies hold, its time", r"X/Root/DeletedObjects/DeletedObject\[del-shared\]/DeletionTime"),
    ("D.only", "a deletion record only one copy holds", r"X/Root/DeletedObjects/DeletedObject\[del-only[AB]\]/UUID"),
    ("X.file", "an unknown element in KeePassFile", r"X/KpgX"),
    ("X.meta", "an unknown element in Meta", M + r"/KpgX"),
    ("X.memprot", "in MemoryProtection", M + r"/MemoryProtection/KpgX"),
    ("X.icon", "in a custom icon", M + r"/CustomIcons/Icon\[[^\]]*\]/KpgX"),
    ("X.meta.cd-item", "in a Meta custom data item", M + r"/CustomData/Item\[[^\]]*\]/KpgX"),
    ("X.root", "in Root", r"X/Root/KpgX"),
    ("X.group", "in a group", G + r"/KpgX"),
    ("X.group.times", "in a group's Times", G + r"/Times/KpgX"),
    ("X.group.cd-item", "in a group custom data item", G + r"/CustomData/Item\[[^\]]*\]/KpgX"),
    ("X.entry", "in an entry", E + r"/KpgX"),
    ("X.entry.times", "in an entry's Times", E + r"/Times/KpgX"),
    ("X.entry.string", "in an entry String", E + r"/String\[kpg-plain\]/KpgX"),
    ("X.entry.binary", "in an entry Binary", E + r"/Binary\[[^\]]*\]/KpgX"),
    ("X.entry.autotype", "in AutoType", E + r"/AutoType/KpgX"),
    ("X.entry.association", "in an Association", E + r"/AutoType/Association\[[^\]]*\]/KpgX"),
    ("X.entry.cd-item", "in an entry custom data item", E + r"/CustomData/Item\[[^\]]*\]/KpgX"),
    ("X.entry.history", "in a history item", E + r"/History/Entry\[\d+\]/KpgX"),
    ("X.deleted", "in a DeletedObject", r"X/Root/DeletedObjects/DeletedObject\[[^\]]*\]/KpgX"),
    ("XA.meta", "an unknown attribute on Meta", M + r"@kpgAttribute"),
    ("XA.group", "on a Group", G + r"@kpgAttribute"),
    ("XA.entry", "on an Entry", E + r"@kpgAttribute"),
    ("XA.value", "on a String's Value", E + r"/String\[kpg-plain\]/Value@kpgAttribute"),
    ("XC.meta", "an XML comment in Meta", M + r"/comment\(\)"),
]
COMPILED = [(pid, desc, re.compile(rx + r"(#\d+)?")) for pid, desc, rx in PLACES]

SAVE_PROGRAMS = ["keepass-2.62-resave", "keepass-2.62-edit", "keepass-2.47-resave", "keepass-2.47-edit",
                 "keepassxc-2.4.2-add", "keepassxc-2.6.6-add", "keepassxc-2.7.12-add", "keepassxc-2.8.0-add",
                 "kdbxweb-resave", "kdbxweb-edit", "pykeepass-resave", "pykeepass-edit"]
MERGE_PROGRAMS = ["keepass-2.62-Synchronize", "keepass-2.62-OverwriteIfNewer", "keepass-2.62-OverwriteExisting",
                  "keepass-2.62-KeepExisting", "keepass-2.62-CreateNewUuids",
                  "keepassxc-2.4.2", "keepassxc-2.6.6", "keepassxc-2.7.12", "keepassxc-2.8.0", "kdbxweb"]


@functools.lru_cache(maxsize=None)
def place_of(path: str) -> str | None:
    for pid, _, rx in COMPILED:
        if rx.fullmatch(path):
            return pid
    return None


def read_files(path: Path) -> dict[str, "OrderedDict[str, str]"]:
    files: dict[str, OrderedDict] = {}
    cur = None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("== "):
            cur = files.setdefault(line[3:], OrderedDict())
        elif cur is not None and "\t" in line:
            p, v = line.split("\t", 1)
            cur[p] = v
    return files


def read_cmp(path: Path) -> tuple[str | None, dict[str, str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if lines and lines[0].startswith(("REFUSED", "UNREADABLE")):
        return lines[0].split("\t", 1)[1], {}
    out = {}
    for line in lines:
        if line.startswith("#") or "\t" not in line:
            continue
        p, v = line.split("\t", 1)
        out[p] = v
    return None, out


def shorten(v: str, n: int = 60) -> str:
    v = v.replace("|", "/")
    return v if len(v) <= n else v[:n] + "..."


# --- saves ------------------------------------------------------------------------------------

def save_outcome(base: dict[str, str], cmp_: dict[str, str], pid: str) -> str:
    paths = [p for p in base if place_of(p) == pid]
    added = [p for p in cmp_ if place_of(p) == pid and cmp_[p].startswith("added")]
    if not paths and not added:
        return "-"
    res = []
    for p in paths:
        r = cmp_.get(p, "kept")
        if r == "kept":
            res.append("kept")
        elif r.startswith("gone"):
            res.append("dropped")
        elif r.startswith("rewritten"):
            res.append("rewritten (fresh random)")
        elif r.startswith("changed"):
            m = re.match(r"changed \((.*) -> (.*)\)$", r)
            res.append(f"rewritten (to {shorten(m.group(2)) if m else r})")
        else:
            res.append(r)
    for p in added:
        res.append(f"added ({shorten(cmp_[p][7:-1])})")
    uniq = list(OrderedDict.fromkeys(res))
    return "; ".join(uniq)


def saves(out: Path) -> list[str]:
    lines = []
    bases = {s: read_files(out / "bases" / f"{s}.txt") for s in ("a41", "e41", "a40", "e40")}
    lines.append("## Saves: what an open and save, or an unrelated edit, does to each place")
    lines.append("")
    lines.append("Cell for the 4.1 Argon2d base; a set that differs is named after it "
                 "(e41 AES-KDF 4.1, a40 Argon2d 4.0, e40 AES-KDF 4.0).")
    lines.append("")
    header = "| place | " + " | ".join(SAVE_PROGRAMS) + " |"
    lines.append(header)
    lines.append("|" + " --- |" * (len(SAVE_PROGRAMS) + 1))
    refusals = []
    for pid, desc, _ in COMPILED:
        if pid in ("H.field13", "I.field4", "H.eoh"):
            continue
        row = [f"`{pid}` {desc}"]
        for prog in SAVE_PROGRAMS:
            per = OrderedDict()
            for s in ("a41", "e41", "a40", "e40"):
                f = out / "saves" / prog / f"{s}-base.txt"
                if not f.exists():
                    per[s] = "not run"
                    continue
                refused, c = read_cmp(f)
                if refused:
                    per[s] = "refused"
                    if pid == "H.version":
                        refusals.append(f"- {prog} {s}: {refused}")
                    continue
                per[s] = save_outcome(bases[s]["base.kdbx"], c, pid)
            first = per["a41"]
            extra = [f"{s}: {v}" for s, v in per.items() if s != "a41" and v != first and v != "-"]
            row.append(shorten(first, 80) + ("" if not extra else " [" + "; ".join(shorten(x, 60) for x in extra) + "]"))
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")
    if refusals:
        lines.append("Refusals of the main base:")
        lines += refusals
        lines.append("")
    # The risky files, a place each.
    lines.append("## Saves of the files holding a place no reader may expect")
    lines.append("")
    lines.append("| file | place | " + " | ".join(SAVE_PROGRAMS) + " |")
    lines.append("|" + " --- |" * (len(SAVE_PROGRAMS) + 2))
    for s in ("a41", "e41"):
        for r, pid in (("hfield", "H.field13"), ("ifield", "I.field4"), ("eoh", "H.eoh")):
            row = [f"{s} risky-{r}", f"`{pid}`"]
            for prog in SAVE_PROGRAMS:
                f = out / "saves" / prog / f"{s}-risky-{r}.txt"
                if not f.exists():
                    row.append("not run")
                    continue
                refused, c = read_cmp(f)
                if refused:
                    m = re.search(r"ERROR (\w+)", refused)
                    row.append("refused: " + (m.group(1) if m else shorten(refused, 70)))
                    continue
                row.append(shorten(save_outcome(bases[s][f"risky-{r}.kdbx"], c, pid), 60))
            lines.append("| " + " | ".join(row) + " |")
    lines.append("")
    return lines


# --- merges -----------------------------------------------------------------------------------

def cell_outcome(o: dict[str, str], a: dict[str, str], cmp_: dict[str, str], pid: str) -> str:
    """One merge run, one place: `same`, `open`, `arriving`, `union`, `dropped`, `rewritten (to
    ...)`, `new`, or several of them joined by `+`. A path only one copy holds counts for the copy
    whose state the result shows: an open copy's path kept is the open state, dropped the
    arriving state; an arriving path taken is the arriving state, not taken the open state; both
    kept is a union, both gone dropped."""
    paths = list(OrderedDict.fromkeys([p for p in list(o) + list(a) if place_of(p) == pid]))
    news = [p for p in cmp_ if place_of(p) == pid and cmp_[p].startswith("new")]
    if not paths and not news:
        return "-"
    kinds: set[str] = set()
    one = {"ok": False, "od": False, "at": False, "an": False}
    for p in paths:
        r = cmp_.get(p, "same")
        if r in ("same", "open", "arriving", "dropped"):
            kinds.add(r)
        elif r.startswith(("rewritten", "other")):
            kinds.add(f"rewritten (to {shorten(r[r.index('(') + 1:-1], 40)})")
        elif r == "only-open kept":
            one["ok"] = True
        elif r == "only-open dropped":
            one["od"] = True
        elif r == "only-arriving taken":
            one["at"] = True
        elif r == "only-arriving not taken":
            one["an"] = True
        else:
            kinds.add("rewritten")
    if one["ok"] and one["at"]:
        kinds.add("union")
    elif one["od"] and one["an"]:
        kinds.add("dropped")
    elif one["ok"] or one["an"]:
        kinds.add("open" if not (one["od"] or one["at"]) else "open+arriving")
    elif one["od"] or one["at"]:
        kinds.add("arriving")
    if news:
        kinds.add("new")
    kinds.discard("same") if len(kinds) > 1 else None
    return "+".join(sorted(kinds)) if kinds else "same"


def label(diff_ab: str, diff_ba: str, tie_ab: str, tie_ba: str, only: bool = False) -> str:
    cells = (diff_ab, diff_ba, tie_ab, tie_ba)
    if "refused" in cells:
        return "refused"
    if all(c == "-" for c in cells):
        return "-"
    if all(c == "same" for c in cells):
        return "kept"
    for whole in ("dropped", "union"):
        if all(c == whole for c in cells):
            return "united" if whole == "union" else "dropped"
    if len(set(cells)) == 1 and cells[0].startswith("rewritten"):
        return cells[0]
    names = {("arriving", "open"): "the later time wins", ("arriving", "arriving"): "the arriving copy's replaces",
             ("open", "open"): "the open copy's stays", ("open", "arriving"): "the earlier time wins",
             ("dropped", "dropped"): "dropped", ("same", "same"): "kept", ("union", "union"): "united"}
    pair = (diff_ab, diff_ba)
    base = names.get(pair, f"A older and open: {diff_ab}; B newer and open: {diff_ba}")
    tie = {("open", "open"): "the open copy's", ("arriving", "arriving"): "the arriving copy's",
           ("dropped", "dropped"): "dropped", ("same", "same"): "kept", ("union", "union"): "united"}.get(
        (tie_ab, tie_ba), f"{tie_ab} / {tie_ba}")
    return f"{base}; at equal times {tie}"


def merges(out: Path) -> list[str]:
    lines = ["## Merges: which copy's value each place holds afterwards", "",
             "A is the copy whose times are older (or equal, in the second pair); each pair is merged",
             "both ways. Sets: a41 (Argon2d 4.1) unless named; KeePassXC also with the slow key",
             "derivation (s41); a set that differs is named in brackets.", ""]
    bases = {s: read_files(out / "bases" / f"{s}.txt") for s in ("a41", "e41", "a40", "e40", "s41")}
    cols = MERGE_PROGRAMS
    lines.append("| place | " + " | ".join(cols) + " |")
    lines.append("|" + " --- |" * (len(cols) + 1))

    def lab(prog: str, s: str, pid: str) -> str:
        cells = []
        for pair, d in (("diff", "AB"), ("diff", "BA"), ("tie", "AB"), ("tie", "BA")):
            f = out / "merges" / prog / s / f"{pair}-{d}.txt"
            if not f.exists():
                return "not run"
            refused, c = read_cmp(f)
            if refused:
                cells.append("refused")
                continue
            first, second = ("A", "B") if d == "AB" else ("B", "A")
            o = bases[s][f"{first}-{pair}.kdbx"]
            a = bases[s][f"{second}-{pair}.kdbx"]
            cells.append(cell_outcome(o, a, c, pid))
        return label(*cells)

    def saved(prog: str, s: str, pid: str) -> str:
        """What the same program's plain save did to the place, from the save table."""
        sp = ("keepass-2.62-resave" if prog.startswith("keepass-2.62") else
              f"{prog}-add" if prog.startswith("keepassxc") else "kdbxweb-resave")
        key = "a41" if s == "s41" else s
        f = out / "saves" / sp / f"{key}-base.txt"
        if not f.exists():
            return ""
        refused, c = read_cmp(f)
        return "" if refused else save_outcome(bases[key]["base.kdbx"], c, pid)

    def lab_or_dropped(prog: str, s: str, pid: str) -> str:
        # A place the program's own save drops is gone from every merge result it writes, whatever
        # the four runs' pattern of paths one copy alone holds would make of it.
        v = lab(prog, s, pid)
        return "dropped" if v not in ("not run", "refused") and saved(prog, s, pid) == "dropped" else v

    for pid, desc, _ in COMPILED:
        if pid in ("H.field13", "I.field4", "H.eoh"):
            continue
        row = [f"`{pid}` {desc}"]
        for prog in cols:
            main = lab_or_dropped(prog, "a41", pid)
            extras = []
            others = ("e41", "a40", "e40") if prog.startswith("keepass-2.62") else ("s41",) if prog.startswith("keepassxc") else ("e41",)
            for s in others:
                v = lab_or_dropped(prog, s, pid)
                if v != main and v not in ("-", "not run"):
                    extras.append(f"{s}: {v}")
            row.append(shorten(main, 90) + ("" if not extras else " [" + "; ".join(shorten(x, 70) for x in extras) + "]"))
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")
    return lines


def key_format(out: Path) -> list[str]:
    lines = ["## KeePass 2.62: a master key change, a KDF change, Save As, and 4.1 brought down to 4.0", ""]
    ops = ("rekey", "kdf", "save-as", "down40")
    bases = {s: read_files(out / "bases" / f"{s}.txt") for s in ("a41", "e41")}
    lines.append("| place | " + " | ".join(ops) + " |")
    lines.append("|" + " --- |" * (len(ops) + 1))
    for pid, desc, _ in COMPILED:
        if pid in ("H.field13", "I.field4", "H.eoh"):
            continue
        row = [f"`{pid}`"]
        for op in ops:
            per = {}
            for s in ("a41", "e41"):
                f = out / "key_format" / f"{s}-{op}.txt"
                if not f.exists():
                    per[s] = "not run"
                    continue
                refused, c = read_cmp(f)
                per[s] = "refused" if refused else save_outcome(bases[s]["base.kdbx"], c, pid)
            row.append(shorten(per["a41"], 70) + ("" if per["e41"] == per["a41"] else f" [e41: {shorten(per['e41'], 50)}]"))
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")
    return lines


def cap(out: Path) -> list[str]:
    d = out / "cap"
    if not d.exists():
        return []
    lines = ["## Capacity: a value of 1 KiB, 64 KiB and 1 MiB characters in one place", ""]
    files = sorted(d.glob("*.txt"), key=lambda p: (p.stem.rsplit("-", 1)[0], int(p.stem.rsplit("-", 1)[1])))
    progs = [p for p in SAVE_PROGRAMS if not p.endswith("-edit")]
    lines.append("| place | chars | file bytes | " + " | ".join(progs) + " |")
    lines.append("|" + " --- |" * (len(progs) + 3))
    for f in files:
        place, size = f.stem.rsplit("-", 1)
        body = f.read_text(encoding="utf-8").splitlines()
        if body[0].startswith("NOT MADE"):
            lines.append(f"| `{place}` | {size} | not made | " + " | ".join("-" for _ in progs) + " |")
            continue
        m = re.search(r"file (\d+) bytes", body[0])
        res = {}
        for line in body[1:]:
            # What moved may itself hold tabs, between a path and its value.
            name, rest = line.split("\t", 1)
            what, secs = rest.rsplit("\t", 1)
            res[name] = f"{shorten(what, 50)} ({secs})"
        lines.append(f"| `{place}` | {size} | {m.group(1) if m else '?'} | " + " | ".join(res.get(p, "-") for p in progs) + " |")
    lines.append("")
    return lines


def main() -> None:
    out = Path(sys.argv[1])
    lines = ["# kdbx-places: the tables", "", "Written by `scripts/summarize.py` from `out/`.", ""]
    for part in (saves, merges, key_format, cap):
        try:
            lines += part(out)
        except FileNotFoundError as e:
            lines += [f"(not run: {e.filename})", ""]
    (out / "tables.md").write_text("\n".join(lines).rstrip("\n") + "\n", encoding="utf-8", newline="\n")
    print(f"tables.md: {len(lines)} lines")


if __name__ == "__main__":
    main()
