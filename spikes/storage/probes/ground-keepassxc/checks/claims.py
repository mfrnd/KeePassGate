"""Checks every measured claim of `papers\\ground\\ground-keepassxc.md` against one result directory.

Written on this branch. The directory is `out\\` after `Reproduce.ps1`, or `recorded\\`: both hold
the probe's text output at the same relative paths. Each check carries the identifier `RUN.md`
cites, reads only text files (dumps through KeePassLib, the command's own reports, counts through
pykeepass, the exports) and prints one line per check: `holds`, `FAILS`, or `no data` where a file
it needs is absent, which is the case in `recorded\\` for everything the record never kept. A
failing cell is printed with what was found.

Usage: `python -I -X utf8 claims.py <result directory>`.
"""
# The German stem is how 2.7.12 and 2.8.0 report a merge they saved under the de-DE locale, and
# TOTP is a column of KeePassXC's CSV export.
# cspell:ignore zusammengef TOTP
import ast
import re
import sys
from pathlib import Path

ROOT = Path(sys.argv[1])
RELEASES = ["2.4.2", "2.6.6", "2.7.12", "2.8.0"]
OLD = {"2.4.2", "2.6.6"}
SETS = ["fast", "slow"]
NOW = re.compile(r"^\d{4}-\d\d-\d\dT")


class Entry:
    def __init__(self, m: re.Match, group: str) -> None:
        self.title, self.group = m["title"], group
        self.u, self.mod, self.loc, self.prev = m["u"], m["mod"], m["loc"], m["prev"]
        self.qc_off = bool(m["qc"])
        self.cur = m["sum"]
        self.hist: list[tuple[str, str]] = []

    def everywhere(self) -> list[str]:
        return [self.cur] + [s for _, s in self.hist]


class Group:
    def __init__(self, m: re.Match) -> None:
        self.path, self.u, self.mod, self.loc, self.prev = m["path"], m["u"], m["mod"], m["loc"], m["prev"]
        self.notes, self.cd = m["notes"], m["cd"]
        self.tags = [t for t in m["tags"].split("|") if t]
        self.search, self.autotype = m["search"], m["autotype"]


class Dump:
    G = re.compile(r"^G '(?P<path>[^']*)' u=(?P<u>\S+) mod=(?P<mod>\S+) loc=(?P<loc>\S+) prev=(?P<prev>\S+) "
                   r"notes=\"(?P<notes>[^\"]*)\" tags=\[(?P<tags>[^\]]*)\] cd=\{(?P<cd>[^}]*)\}"
                   r"(?: search=(?P<search>\S+) autotype=(?P<autotype>\S+))?$")
    E = re.compile(r"^  E '(?P<title>[^']*)' u=(?P<u>\S+) mod=(?P<mod>\S+) loc=(?P<loc>\S+) prev=(?P<prev>\S+)"
                   r"(?P<qc> qc=off)? :: (?P<sum>.*)$")
    H = re.compile(r"^    H (?P<n>\d+): (?P<items>.*)$")
    BIN = re.compile(r"bin='(?P<path>[^']*)' (?P<u>\S+)|bin=none|bin=missing (?P<m>\S+)")

    def __init__(self, path: Path) -> None:
        lines = path.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")
        self.version = lines[0].split(" kdbx ")[1].strip()
        self.meta = lines[1]
        b = self.BIN.search(self.meta)
        self.bin = b["path"] if b and b["path"] is not None else None
        self.deleted = [(l.split()[1], l.split("@")[1]) for l in lines if l.startswith("deleted ")]
        self.groups: dict[str, Group] = {}
        self.entries: dict[str, Entry] = {}
        group, last = "", None
        for line in lines[2:]:
            if m := self.G.match(line):
                g = Group(m)
                self.groups[g.path] = g
                group = g.path
            elif m := self.E.match(line):
                last = Entry(m, group)
                self.entries[last.title] = last
            elif (m := self.H.match(line)) and last:
                for item in m["items"].split(" ; "):
                    t, _, s = item.partition(" ")
                    last.hist.append((t, s[1:-1]))
        self.names = {e.u: e.title for e in self.entries.values()} | {g.u: g.path for g in self.groups.values()}


def has(summary: str, key_value: str) -> bool:
    return re.search(r"(?:^| )" + re.escape(key_value), summary) is not None


def bins(summary: str) -> dict[str, tuple[bool, str]]:
    return {n: (bool(p), h) for n, p, h in re.findall(r"bin:([^\[=]+)(\[P\])?=([0-9a-f]{8})", summary)}


def tags(summary: str) -> list[str]:
    m = re.search(r"(?:^| )tags=\[([^\]]*)\]", summary)
    return [t for t in m.group(1).split("|") if t] if m else []


def cd(summary: str) -> str:
    m = re.search(r"(?:^| )cd=\{([^}]*)\}", summary)
    return m.group(1) if m else ""


def counts(path: Path) -> dict[tuple[str, str], tuple[list[str], list[tuple[list[str], int]]]]:
    """`count_hist.py` output: (file, title) -> (current fields, [(fields, binaries) per item])."""
    result = {}
    key = None
    head = re.compile(r"^(?P<f>\S+) (?P<t>[^:]+): current=(?P<c>\[.*\]) history=(?P<n>\d+)$")
    item = re.compile(r"^    \S+ (?P<f>\[.*\]) binaries=(?P<b>\d+)$")
    for line in path.read_text(encoding="utf-8").splitlines():
        if m := head.match(line):
            key = (m["f"], m["t"])
            result[key] = (ast.literal_eval(m["c"]), [])
        elif (m := item.match(line)) and key:
            result[key][1].append((ast.literal_eval(m["f"]), int(m["b"])))
    return result


class NoData(Exception):
    pass


def need(rel: str) -> Path:
    p = ROOT / rel
    if not p.exists():
        raise NoData(rel)
    return p


_dumps: dict[str, Dump] = {}


def dump(rel: str) -> Dump:
    if rel not in _dumps:
        _dumps[rel] = Dump(need(rel))
    return _dumps[rel]


CHECKS = []


def check(cid: str, text: str):
    def wrap(fn):
        CHECKS.append((cid, text, fn))
        return fn
    return wrap


def merge_cells(sets=SETS, releases=RELEASES):
    for s in sets:
        for v in releases:
            for into in "AB":
                yield s, v, into, f"merge-{s}/{v}-into{into}"


def per_cell(fn, sets=SETS, releases=RELEASES):
    """Runs fn(dump, release, into, set) over every merge cell; fn returns (ok, evidence)."""
    rows = []
    for s, v, into, rel in merge_cells(sets, releases):
        ok, ev = fn(dump(rel + "/result.txt"), v, into, s)
        rows.append((f"{s} {v} into {into}", ok, ev))
    return rows


def raw(s: str, v: str, into: str, title: str):
    c = counts(need(f"counts/merge-{s}.txt"))
    return c[(f"{v}-into{into}/{into}.kdbx", title)]


# ---- Set 1, section 4 ------------------------------------------------------------------------

@check("R01", "added-A and added-B are both in KPG in every result")
def _(): return per_cell(lambda d, v, i, s: (all(t in d.entries and d.entries[t].group == "KPG" for t in ("added-A", "added-B")), ""))


@check("R02", "in-newA: group KPG/NewA and its entry are in both results")
def _(): return per_cell(lambda d, v, i, s: ("in-newA" in d.entries and d.entries["in-newA"].group == "KPG/NewA", ""))


@check("R03", "one-A: A's Data=A in both; into B, B's version (Data=0 at T0) in history")
def _():
    def f(d, v, i, s):
        e = d.entries["one-A"]
        return has(e.cur, "Data=A") and (i == "A" or any(t == "+00:00:00" and has(h, "Data=0") for t, h in e.hist)), e.cur
    return per_cell(f)


@check("R04", "prot-A: A's protected Secret=sA in both, still protected")
def _(): return per_cell(lambda d, v, i, s: (has(d.entries["prot-A"].cur, "Secret[P]=sA"), d.entries["prot-A"].cur))


@check("R05", "att-A: A's attachment in both; into B the old one in history")
def _():
    def f(d, v, i, s):
        e = d.entries["att-A"]
        new = bins(e.cur).get("blob.bin", (0, ""))[1] == "a3c0f97c"
        old = any(bins(h).get("blob.bin", (0, ""))[1] == "d064f5c4" for _, h in e.hist)
        return new and (i == "A" or old), e.cur
    return per_cell(f)


@check("R06", "notes-A: Notes=nA in both")
def _(): return per_cell(lambda d, v, i, s: (has(d.entries["notes-A"].cur, "Notes=nA"), ""))


@check("R07", "tag-A: tag tagA in both")
def _(): return per_cell(lambda d, v, i, s: (tags(d.entries["tag-A"].cur) == ["tagA"], d.entries["tag-A"].cur))


@check("R08", "both-diff: B's whole, FieldB only; FieldA only in the history item at +1h")
def _():
    def f(d, v, i, s):
        e = d.entries["both-diff"]
        where = [t for t, h in e.hist if has(h, "FieldA=a")]
        return has(e.cur, "FieldB=b") and not has(e.cur, "FieldA=") and where == ["+01:00:00"], f"FieldA in history at {where}"
    return per_cell(f)


@check("R09", "both-samefield: Shared=B; A's Shared=A in history")
def _():
    def f(d, v, i, s):
        e = d.entries["both-samefield"]
        return has(e.cur, "Shared=B") and any(t == "+01:00:00" and has(h, "Shared=A") for t, h in e.hist), e.cur
    return per_cell(f)


@check("R10", "att-both: B's (base.bin, b.bin); a.bin only in history")
def _():
    def f(d, v, i, s):
        e = d.entries["att-both"]
        return set(bins(e.cur)) == {"base.bin", "b.bin"} and any("a.bin" in bins(h) for _, h in e.hist), str(sorted(bins(e.cur)))
    return per_cell(f)


@check("R11", "tag-both: B's (t0, tagB); tagA only in history")
def _():
    def f(d, v, i, s):
        e = d.entries["tag-both"]
        return tags(e.cur) == ["t0", "tagB"] and any("tagA" in tags(h) for _, h in e.hist), str(tags(e.cur))
    return per_cell(f)


@check("R12", "cd-both: B's (cd0, cdB); cdA only in history")
def _():
    def f(d, v, i, s):
        e = d.entries["cd-both"]
        c = cd(e.cur)
        return "cd0=" in c and "cdB=" in c and "cdA=" not in c and any("cdA=" in cd(h) for _, h in e.hist), c
    return per_cell(f)


@check("R13", "both-tie: the target's version stays, the source's is nowhere, entry and history")
def _():
    def f(d, v, i, s):
        e = d.entries["both-tie"]
        mine, theirs = ("FieldA=a", "FieldB=") if i == "A" else ("FieldB=b", "FieldA=")
        return has(e.cur, mine) and not any(has(x, theirs) for x in e.everywhere()), e.cur
    return per_cell(f)


@check("R14", "rewound-B: the base version stays; B's change only in history at -1h")
def _():
    def f(d, v, i, s):
        e = d.entries["rewound-B"]
        return has(e.cur, "Data=0") and e.mod == "+00:00:00" and any(t == "-01:00:00" and has(h, "Data=B") for t, h in e.hist), e.cur
    return per_cell(f)


@check("R15", "hist: B's; history of 4: -2h, -1h, T0, A's +1h (dump times, raw count 4)")
def _():
    def f(d, v, i, s):
        e = d.entries["hist"]
        n = len(raw(s, v, i, "hist")[1])
        times = [t for t, _ in e.hist]
        return has(e.cur, "Data=B") and times == ["-02:00:00", "-01:00:00", "+00:00:00", "+01:00:00"] and n == 4, f"raw {n}"
    return per_cell(f)


@check("R16", "hist-many: B's; raw history into A 10 (2.4.2, 2.6.6) or 11 (2.7.12, 2.8.0); into B 10, oldest dropped")
def _():
    def f(d, v, i, s):
        e = d.entries["hist-many"]
        n = len(raw(s, v, i, "hist-many")[1])
        want = 10 if (i == "B" or v in OLD) else 11
        oldest = e.hist[0][0] if e.hist else "-"
        return has(e.cur, "Data=B") and n == want and (i == "A" or oldest == "-08:00:00"), f"raw {n}, oldest in dump {oldest}"
    return per_cell(f)


@check("R17", "hist-big: A's; raw history into A 2; into B 2 (2.4.2, 2.6.6) or 3 of 400 KiB each (2.7.12, 2.8.0)")
def _():
    def f(d, v, i, s):
        e = d.entries["hist-big"]
        items = raw(s, v, i, "hist-big")[1]
        want = 2 if (i == "A" or v in OLD) else 3
        return bins(e.cur).get("big.bin", (0, ""))[1] == "521bdd7c" and len(items) == want and all(b == 1 for _, b in items), f"raw {len(items)}"
    return per_cell(f)


@check("R18", "move-A: in KPG/Sub in both; into B its location time stays T0; previous parent set by 2.7.12 and 2.8.0 only")
def _():
    def f(d, v, i, s):
        e = d.entries["move-A"]
        if i == "A":
            return e.group == "KPG/Sub", e.group
        return e.group == "KPG/Sub" and e.loc == "+00:00:00" and ((e.prev != "-") == (v not in OLD)), f"loc {e.loc} prev {e.prev}"
    return per_cell(f)


@check("R19", "move-A-edit-B: into A B's content in Sub with location time T0 and no previous parent; into B moved to Sub")
def _():
    def f(d, v, i, s):
        e = d.entries["move-A-edit-B"]
        ok = e.group == "KPG/Sub" and has(e.cur, "Data=B")
        if i == "A":
            ok = ok and e.loc == "+00:00:00" and e.prev == "-"
        return ok, f"{e.group} loc {e.loc} prev {e.prev}"
    return per_cell(f)


@check("R20", "del-A-unch: back in A beside A's tombstone; stays in B, which gets no tombstone")
def _():
    def f(d, v, i, s):
        e = d.entries.get("del-A-unch")
        if not e:
            return False, "absent"
        stones = [u for u, _ in d.deleted]
        return (e.u in stones) if i == "A" else not stones, f"{len(stones)} tombstones"
    return per_cell(f)


@check("R21", "del-A-chB-after: back in A, stays in B")
def _(): return per_cell(lambda d, v, i, s: ("del-A-chB-after" in d.entries, ""))


@check("R22", "del-A-chB-before: back in A, stays in B")
def _(): return per_cell(lambda d, v, i, s: ("del-A-chB-before" in d.entries, ""))


@check("R23", "GDelA, the empty group A deleted: back in A, stays in B")
def _(): return per_cell(lambda d, v, i, s: ("KPG/GDelA" in d.groups, ""))


@check("R24", "bin-A: stays in A's bin; into B a group Recycle Bin arrives as an ordinary group, searching and auto-type off")
def _():
    def f(d, v, i, s):
        e = d.entries["bin-A"]
        if i == "A":
            return e.group == "Recycle Bin" and d.bin == "Recycle Bin", f"bin {d.bin}"
        g = d.groups.get("Recycle Bin")
        flags = "not printed by this dump" if g and g.search is None else (g and f"search={g.search} autotype={g.autotype}")
        ok = bool(g) and e.group == "Recycle Bin" and d.bin is None and (g.search is None or (g.search, g.autotype) == ("False", "False"))
        return ok, f"bin setting {d.bin}, {flags}"
    return per_cell(f)


@check("R25", "GNotesBoth: B's notes nB in both")
def _(): return per_cell(lambda d, v, i, s: (d.groups["KPG/GNotesBoth"].notes == "nB", ""))


@check("R26", "GNotesA: A's notes nA in both")
def _(): return per_cell(lambda d, v, i, s: (d.groups["KPG/GNotesA"].notes == "nA", ""))


@check("R27", "GTagsA: into A gtA in 2.7.12 and 2.8.0, none in 2.4.2 and 2.6.6; into B none, under A's modification time")
def _():
    def f(d, v, i, s):
        g = d.groups["KPG/GTagsA"]
        if i == "A":
            return g.tags == ([] if v in OLD else ["gtA"]), str(g.tags)
        return g.tags == [] and g.mod == "+01:00:00", f"{g.tags} mod {g.mod}"
    return per_cell(f)


@check("R28", "B never receives a tombstone; A keeps its own")
def _(): return per_cell(lambda d, v, i, s: ((len(d.deleted) == 4) if i == "A" else not d.deleted, f"{len(d.deleted)} tombstones"))


@check("R29", "no merge removes an entry or a group the target holds")
def _():
    o = dump("save-fast/A-orig.txt")
    a = set(o.entries) | {"G:" + g for g in o.groups}
    # B as `Program.cs` makes it: the base's groups and entries and `added-B`.
    b = {"Ordinary1", "Ordinary2", "Ordinary3", "added-B", "both-diff", "both-tie", "both-samefield", "one-A",
         "rewound-B", "prot-A", "att-A", "att-both", "tag-A", "tag-both", "cd-both", "notes-A", "move-A",
         "move-A-edit-B", "del-A-unch", "del-A-chB-after", "del-A-chB-before", "bin-A", "hist", "hist-many", "hist-big"}
    b |= {"G:" + g for g in ("", "General", "KPG", "KPG/Sub", "KPG/GNotesBoth", "KPG/GNotesA", "KPG/GTagsA", "KPG/GDelA")}

    def f(d, v, i, s):
        held = set(d.entries) | {"G:" + g for g in d.groups}
        lost = (a if i == "A" else b) - held
        return not lost, f"lost {sorted(lost)}"
    return per_cell(f)


@check("I01", "the inputs: base and B are KDBX 4.0, A is 4.1; KPG holds 21 entries and five subgroups in the base")
def _():
    v = {n: dump(f"inputs/fast-{n}.txt").version for n in ("base", "A", "B")}
    base = dump("inputs/fast-base.txt")
    kpg = [e for e in base.entries.values() if e.group == "KPG"]
    subs = [g for g in base.groups if g.startswith("KPG/") and g.count("/") == 1]
    ok = v == {"base": "4.0", "A": "4.1", "B": "4.0"} and len(kpg) == 21 and len(subs) == 5
    return [("set 1", ok, f"formats {v}, {len(kpg)} entries, {len(subs)} subgroups")]


@check("R30", "both-tie: the command's report says nothing about it")
def _():
    rows = []
    for s, v, into, rel in merge_cells():
        text = need(rel + "/merge.txt").read_text(encoding="utf-8")
        rows.append((f"{s} {v} into {into}", "both-tie" not in text, ""))
    return rows


@check("C01", "every merge exited 0: fast, slow and extra sets")
def _():
    rows = []
    for s in ("fast", "slow", "extra"):
        for v in RELEASES:
            for into in "AB":
                log = need(f"merge-{s}/{v}-into{into}/cli.log").read_text(encoding="utf-8").splitlines()
                exits = [log[k + 1] for k, l in enumerate(log) if l.startswith("### ") and '"merge" "-s"' in l and "--dry-run" not in l]
                rows.append((f"{s} {v} into {into}", bool(exits) and all(x.startswith("### exit 0 ") for x in exits), str(len(exits))))
    return rows


def normalized(path: Path) -> list[str]:
    d = Dump(path)
    out = []
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        line = re.sub(r"_LAST_MODIFIED=[^@]*@", "_LAST_MODIFIED=<date>@", line)
        line = re.sub(r"(?<=u=)[0-9a-f]{8}|(?<=prev=)[0-9a-f]{8}|(?<=^deleted )[0-9a-f]{8}|(?<=' )[0-9a-f]{8}(?= historyMax)",
                      lambda m: d.names.get(m.group(0), "<uuid>"), line)
        out.append(line)
    return sorted(out)


@check("C02", "the slow KDF gives the fast KDF's result in every cell, identifiers aside, raw counts included")
def _():
    rows = []
    for v in RELEASES:
        for into in "AB":
            same = normalized(need(f"merge-fast/{v}-into{into}/result.txt")) == normalized(need(f"merge-slow/{v}-into{into}/result.txt"))
            try:
                rc = {k[1]: (len(x[1]), x[0]) for k, x in counts(need("counts/merge-fast.txt")).items() if k[0] == f"{v}-into{into}/{into}.kdbx"} == \
                     {k[1]: (len(x[1]), x[0]) for k, x in counts(need("counts/merge-slow.txt")).items() if k[0] == f"{v}-into{into}/{into}.kdbx"}
                ev = "raw counts equal" if rc else "raw counts differ"
            except NoData:
                rc, ev = True, "raw counts not recorded"
            rows.append((f"{v} into {into}", same and rc, ev))
    return rows


@check("C03", "one merge under the slow KDF took 7.3 to 10.7 seconds (informational: a time on a shared machine)")
def _():
    from datetime import datetime, timedelta

    def at(text: str) -> datetime:
        whole, _, fraction = text.rstrip("Z").partition(".")
        return datetime.fromisoformat(whole) + timedelta(seconds=float("0." + (fraction or "0")))

    spans = []
    for v in RELEASES:
        for into in "AB":
            log = need(f"merge-slow/{v}-into{into}/cli.log").read_text(encoding="utf-8").splitlines()
            for k, l in enumerate(log):
                if l.startswith("### ") and '"merge" "-s"' in l and "--dry-run" not in l:
                    m = re.match(r"### exit \S+ (\S+)/(\S+)$", log[k + 1])
                    spans.append((at(m.group(2)) - at(m.group(1))).total_seconds())
    return [("merges", None, f"{len(spans)} merges, {min(spans):.1f} to {max(spans):.1f} s")]


# ---- Set 2 ---------------------------------------------------------------------------------

def extra_cells(fn):
    rows = []
    for v in RELEASES:
        for into in "AB":
            ok, ev = fn(dump(f"merge-extra/{v}-into{into}/result.txt"), v, into)
            rows.append((f"{v} into {into}", ok, ev))
    return rows


@check("X01", "meta: B's entry and 10 history items; A's field kpg-20260101T010000 nowhere (dump and raw)")
def _():
    def f(d, v, i):
        e = d.entries["meta"]
        c = counts(need("counts/merge-extra.txt"))[(f"{v}-into{i}/{i}.kdbx", "meta")]
        raw_a = any("kpg-20260101T010000" in fs for fs in [c[0]] + [x[0] for x in c[1]])
        ok = has(e.cur, "kpg-20260101T120000=") and len(e.hist) == 10 and not any("kpg-20260101T010000" in x for x in e.everywhere())
        return ok and len(c[1]) == 10 and not raw_a, f"raw {len(c[1])} items"
    return extra_cells(f)


@check("X02", "meta2: A's entry with both A fields; B's version as the history item at +2h")
def _():
    def f(d, v, i):
        e = d.entries["meta2"]
        return has(e.cur, "kpg-20260101T010000=") and has(e.cur, "kpg-20260101T030000=") and \
            any(t == "+02:00:00" and has(h, "kpg-20260101T020000=") for t, h in e.hist), e.cur
    return extra_cells(f)


@check("X03", "GFlags: into A A's (searching and auto-type off); into B B's (inherit), under A's modification time")
def _():
    def f(d, v, i):
        g = d.groups["KPG/GFlags"]
        want = ("False", "False") if i == "A" else ("inherit", "inherit")
        return (g.search, g.autotype) == want and (i == "A" or g.mod == "+01:00:00"), f"{g.search}/{g.autotype} mod {g.mod}"
    return extra_cells(f)


@check("X04", "GCustom: into A A's KPG=rule-A with _LAST_MODIFIED added; into B none")
def _():
    def f(d, v, i):
        c = d.groups["KPG/GCustom"].cd
        return ("KPG=rule-A" in c and "_LAST_MODIFIED=" in c) if i == "A" else ("KPG=" not in c), c
    return extra_cells(f)


@check("X05", "excl: the exclusion from reports kept by 2.7.12 and 2.8.0, lost by 2.4.2 and 2.6.6, both directions")
def _(): return extra_cells(lambda d, v, i: (d.entries["excl"].qc_off == (v not in OLD), f"qc_off={d.entries['excl'].qc_off}"))


# ---- Back and forth ------------------------------------------------------------------------

def trip(fn, releases=("2.4.2", "2.8.0")):
    return [(v, *fn(dump(f"roundtrip/{v}/A.txt"), dump(f"roundtrip/{v}/B.txt"), v)) for v in releases]


@check("T01", "after A<-B, B<-A, A<-B: both-tie, A holds FieldA and B FieldB, neither has the other's")
def _():
    def f(a, b, v):
        ea, eb = a.entries["both-tie"], b.entries["both-tie"]
        return has(ea.cur, "FieldA=a") and has(eb.cur, "FieldB=b") and not any(has(x, "FieldB=") for x in ea.everywhere()) \
            and not any(has(x, "FieldA=") for x in eb.everywhere()), ""
    return trip(f)


@check("T02", "move-A-edit-B: A has it in KPG/Sub, B in KPG")
def _(): return trip(lambda a, b, v: (a.entries["move-A-edit-B"].group == "KPG/Sub" and b.entries["move-A-edit-B"].group == "KPG",
                                     f"{a.entries['move-A-edit-B'].group} / {b.entries['move-A-edit-B'].group}"))


@check("T03", "only A holds the tombstones")
def _(): return trip(lambda a, b, v: (bool(a.deleted) and not b.deleted, f"{len(a.deleted)} / {len(b.deleted)}"))


@check("T04", "only A has a designated recycle bin")
def _(): return trip(lambda a, b, v: (a.bin == "Recycle Bin" and b.bin is None, f"{a.bin} / {b.bin}"))


@check("T05", "2.8.0: only A has the group tag")
def _(): return trip(lambda a, b, v: (a.groups["KPG/GTagsA"].tags == ["gtA"] and b.groups["KPG/GTagsA"].tags == [], ""), ("2.8.0",))


@check("T06", "2.8.0: the third merge reported a history change to hist-big and wrote the file, hist-big being the same in both")
def _():
    log = need("roundtrip/2.8.0/cli.log").read_text(encoding="utf-8")
    third = log.split("### 2.8.0 ")[3]
    a, b = dump("roundtrip/2.8.0/A.txt").entries["hist-big"], dump("roundtrip/2.8.0/B.txt").entries["hist-big"]
    same = (a.cur, a.mod, a.hist) == (b.cur, b.mod, b.hist)
    reported = "'hist-big'" in third and ("merged" in third or "zusammengef" in third)
    return [("2.8.0", reported and same, f"reported={reported} same={same}")]


# ---- A plain save and the edits, section 6 -------------------------------------------------

def norm_lines(path: Path) -> list[str]:
    """`scripts\\Show-Diff.ps1`'s normalization, the same three replacements."""
    out = []
    for l in path.read_text(encoding="utf-8").splitlines():
        if l.startswith("# "):
            l = re.sub(r"^# \S+", "# db", l)
        l = re.sub(r" (Notes|Password\[P\]|Password|URL|UserName)=(?= |\}|$)", "", l)
        l = re.sub(r"(bin:[^\[=]+)\[P\]", r"\1", l)
        l = re.sub(r"_LAST_MODIFIED=[^@]*@none", "_LAST_MODIFIED=<date>@none", l)
        out.append(l)
    return out


def saves(fn):
    return [(v, *fn(dump("save-fast/A-orig.txt"), dump(f"save-fast/{v}/after-add.txt"), v)) for v in RELEASES]


@check("V01", "a plain save writes KDBX 4.0 (2.4.2, 2.6.6) or 4.1 (2.7.12, 2.8.0)")
def _(): return saves(lambda o, s, v: (s.version == ("4.0" if v in OLD else "4.1"), s.version))


@check("V02", "a plain save keeps everything else: strings, flags, attachments, notes, history, custom data, groups, tombstones, bin, times")
def _():
    def f(o, s, v):
        # Takes away only what the report lists as changed by a save: the format line, the
        # database's custom data, General's modification time, the added entry, the marker on
        # entry custom data, and in 2.4.2 and 2.6.6 the previous parents and the group tags.
        # Whatever is left must be the same on both sides.
        skip = ("# db kdbx", "meta ", "G 'General'", "  E 'xc-added'")
        before = [l for l in norm_lines(need("save-fast/A-orig.txt")) if not l.startswith(skip)]
        after = [l for l in norm_lines(need(f"save-fast/{v}/after-add.txt")) if not l.startswith(skip)]
        after = [l.replace("_LAST_MODIFIED=<date>@none; ", "") for l in after]
        if v in OLD:
            before = [re.sub(r"prev=[0-9a-f]{8}", "prev=-", l).replace("tags=[gtA]", "tags=[]") if not l.startswith("    H")
                      else l for l in before]
        left = [l for l in before if l not in after] + [l for l in after if l not in before]
        return not left and len(before) == len(after), f"left after taking those away: {left[:3]}"
    return saves(f)


@check("V03", "a plain save adds the empty standard fields to every entry, Password flagged protected")
def _():
    def f(o, s, v):
        missing = [e.title for e in s.entries.values()
                   if not all(has(e.cur, k) for k in ("Notes=", "Password[P]=", "URL=", "UserName="))]
        return not missing, f"missing in {missing[:3]}"
    return saves(f)


@check("V04", "a plain save flags every attachment protected")
def _():
    def f(o, s, v):
        flags = [p for e in s.entries.values() for x in e.everywhere() for p, _ in bins(x).values()]
        before = [p for e in o.entries.values() for x in e.everywhere() for p, _ in bins(x).values()]
        return bool(flags) and all(flags) and not any(before), f"{sum(flags)} of {len(flags)} flagged, {sum(before)} before"
    return saves(f)


@check("V05", "a plain save adds _LAST_MODIFIED to entry custom data, the entry's and each history item's")
def _():
    def f(o, s, v):
        e = s.entries["cd-both"]
        return all("_LAST_MODIFIED=" in cd(x) for x in e.everywhere() if cd(x)) and "_LAST_MODIFIED=" in cd(e.cur), cd(e.cur)
    return saves(f)


@check("V06", "the previous parent: dropped by 2.4.2 and 2.6.6, kept by 2.7.12 and 2.8.0")
def _(): return saves(lambda o, s, v: ((s.entries["move-A"].prev == "-") == (v in OLD) and o.entries["move-A"].prev != "-",
                                      f"{o.entries['move-A'].prev} -> {s.entries['move-A'].prev}"))


@check("V07", "group tags, as KeePassLib reads them: dropped by 2.4.2 and 2.6.6, kept by 2.7.12 and 2.8.0")
def _(): return saves(lambda o, s, v: (s.groups["KPG/GTagsA"].tags == ([] if v in OLD else ["gtA"]), str(s.groups["KPG/GTagsA"].tags)))


@check("V08", "a plain save keeps the recycle bin setting and group, and every tombstone")
def _(): return saves(lambda o, s, v: (s.bin == o.bin == "Recycle Bin" and sorted(s.deleted) == sorted(o.deleted), f"{len(s.deleted)} tombstones"))


@check("V09", "a plain save keeps the group flags (the recycle bin's, searching and auto-type off)")
def _():
    def f(o, s, v):
        g = s.groups["Recycle Bin"]
        if g.search is None:
            return None, "not printed by this dump"
        return (g.search, g.autotype) == ("False", "False"), f"{g.search}/{g.autotype}"
    return saves(f)


def edits(fn):
    return [(v, *fn(dump(f"save-fast/{v}/after-add.txt"), dump(f"save-fast/{v}/after-all.txt"), v)) for v in RELEASES]


@check("E01", "edit -u: the pre-edit state goes into history, the time is now")
def _():
    def f(a, b, v):
        bad = []
        for t in ("prot-A", "att-A", "cd-both", "hist-many", "hist-big"):
            x, y = a.entries[t], b.entries[t]
            if not (NOW.match(y.mod) and has(y.cur, "UserName=xc-user") and y.hist and y.hist[-1][0] == x.mod):
                bad.append(t)
        return not bad, f"not so: {bad}"
    return edits(f)


@check("E02", "edit truncates: hist-many drops its oldest item and hist-big one by size (dump and raw)")
def _():
    def f(a, b, v):
        hm = (a.entries["hist-many"].hist[0][0], b.entries["hist-many"].hist[0][0])
        hb = (a.entries["hist-big"].hist[0][0], b.entries["hist-big"].hist[0][0])
        c = counts(need("counts/save-fast.txt"))
        n = (len(c[("base-fast/A.kdbx", "hist-many")][1]), len(c[(f"{v}/S.kdbx", "hist-many")][1]),
             len(c[("base-fast/A.kdbx", "hist-big")][1]), len(c[(f"{v}/S.kdbx", "hist-big")][1]))
        return hm == ("-09:00:00", "-08:00:00") and hb == ("-01:00:00", "+00:00:00") and n == (10, 10, 2, 2), \
            f"oldest {hm} {hb}, raw hist-many {n[0]}->{n[1]}, hist-big {n[2]}->{n[3]}"
    return edits(f)


@check("E03", "the history item an edit makes has no entry custom data up to 2.7.12, and has it in 2.8.0")
def _():
    def f(a, b, v):
        item = [h for t, h in b.entries["cd-both"].hist if t == a.entries["cd-both"].mod]
        with_cd = bool(item) and bool(cd(item[-1]))
        return with_cd == (v == "2.8.0"), f"custom data in the new item: {with_cd}"
    return edits(f)


@check("E04", "rm outside the bin: to the bin, location time now; modification time unchanged (2.4.2, 2.6.6) or now with previous parent (2.7.12, 2.8.0)")
def _():
    def f(a, b, v):
        e = b.entries["Ordinary2"]
        general = a.groups["General"].u
        ok = e.group == "Recycle Bin" and NOW.match(e.loc)
        ok = ok and ((e.mod == "+00:00:00") if v in OLD else (NOW.match(e.mod) and e.prev == general))
        return bool(ok), f"mod {e.mod} loc {e.loc} prev {e.prev}"
    return edits(f)


@check("E05", "rm in the bin: deleted, tombstone at now")
def _():
    def f(a, b, v):
        u = a.entries["bin-A"].u
        stone = [t for x, t in b.deleted if x == u]
        return "bin-A" not in b.entries and bool(stone) and bool(NOW.match(stone[0])), str(stone)
    return edits(f)


@check("E06", "mv: absent from 2.4.2; 2.6.6 location time only; 2.7.12, 2.8.0 history item, modification and location time, previous parent")
def _():
    def f(a, b, v):
        if v == "2.4.2":
            helptext = need("help-2.4.2.txt").read_text(encoding="utf-8")
            steps = need("save-fast/2.4.2/steps.txt").read_text(encoding="utf-8")
            return not re.search(r"(?m)^mv\s", helptext) and "mv " not in steps, "no mv command"
        x, y = a.entries["Ordinary3"], b.entries["Ordinary3"]
        if y.group != "KPG/Sub":
            return False, y.group
        if v == "2.6.6":
            return bool(NOW.match(y.loc)) and y.mod == x.mod and len(y.hist) == len(x.hist), f"mod {y.mod} loc {y.loc}"
        return bool(NOW.match(y.loc) and NOW.match(y.mod)) and y.prev == a.groups["General"].u and len(y.hist) == len(x.hist) + 1, \
            f"mod {y.mod} loc {y.loc} prev {y.prev}"
    return edits(f)


# ---- Raw reads through pykeepass -------------------------------------------------------------

@check("U01", "unknown XML at all 8 positions survives in the input and is dropped by every release's save")
def _():
    lines = need("unknown/check.txt").read_text(encoding="utf-8").splitlines()
    rows = []
    for l in lines:
        name, _, rest = l.partition(": ")
        kept = ast.literal_eval(rest.split(" dropped=")[0][len("kept="):])
        want = 8 if name == "U.kdbx" else 0
        rows.append((name, len(kept) == want, f"kept {len(kept)}"))
    return rows


@check("G01", "raw tag text: entry verbatim in 2.4.2 and 2.6.6, normalized in 2.7.12 and 2.8.0; group tags dropped by 2.4.2 and 2.6.6, verbatim after")
def _():
    raw_text = "zeta; Alpha;alpha, b:c,  spaced tag ;zeta"
    rows = []
    for l in need("tags/get.txt").read_text(encoding="utf-8").splitlines():
        m = re.match(r"^(?P<f>\S+): entry=(?P<e>.*) group=(?P<g>.*)$", l)
        name, e, g = m["f"], ast.literal_eval(m["e"]), ast.literal_eval(m["g"])
        v = name[2:-5] if name != "T.kdbx" else None
        if v is None:
            ok = e == raw_text and g == raw_text
        elif v in OLD:
            ok = e == raw_text and g is None
        else:
            ok = e == "Alpha,alpha,b:c,spaced tag,zeta" and g == raw_text
        rows.append((name, ok, f"entry={e!r} group={g!r}"))
    return rows


@check("H01", "with the file's limit set to 3, all ten items of hist-many are still in the file after an add, four releases")
def _():
    c = counts(need("histmax/count.txt"))
    return [(k[0], len(x[1]) == 10, f"{len(x[1])} items") for k, x in c.items()]


@check("H02", "KeePassLib 2.62's Open returns 3 of those 10 items")
def _():
    e = dump("histmax/H-dump.txt").entries["hist-many"]
    return [("H.kdbx", len(e.hist) == 3, f"{len(e.hist)} items")]


@check("B01", "KeePassLib 2.62's Save wrote 2 of the 4 history items the probe created for hist-big, under the 1 MiB limit")
def _():
    c = counts(need("counts/base.txt"))
    x = c[("base-fast/base.kdbx", "hist-big")]
    return [("base.kdbx", len(x[1]) == 2, f"{len(x[1])} items in the file")]


@check("L01", "KeePassLib trims a history KeePassXC left over the limit when it opens the file (hist-many, 2.7.12 and 2.8.0 into A)")
def _():
    rows = []
    for s in SETS:
        for v in ("2.7.12", "2.8.0"):
            n = len(raw(s, v, "A", "hist-many")[1])
            d = len(dump(f"merge-{s}/{v}-intoA/result.txt").entries["hist-many"].hist)
            rows.append((f"{s} {v}", n == 11 and d == 10, f"raw {n}, KeePassLib {d}"))
    return rows


# ---- Exports -------------------------------------------------------------------------------

@check("P01", "CSV export: the ten columns, recycled entries included, no custom strings, notes of groups, tags or history")
def _():
    rows = []
    for v in ("2.6.6", "2.7.12", "2.8.0"):
        text = need(f"export/{v}.csv").read_text(encoding="utf-8")
        head = text.splitlines()[0]
        ok = head == '"Group","Title","Username","Password","URL","Notes","TOTP","Icon","Last Modified","Created"'
        ok = ok and '"Root/Recycle Bin","bin-A"' in text
        leaked = [w for w in ("sA", "FieldA", "kpg notes v0", "tagA", "cdA", "blob.bin") if w in text]
        rows.append((v, ok and not leaked, f"header {head[:40]}..., leaked {leaked}"))
    return rows


@check("P02", "HTML export: every custom string including protected ones in clear, entry and group notes; not the recycle bin, attachments or custom data; absent from 2.6.6")
def _():
    rows = []
    t = need("export/2.6.6.html").read_text(encoding="utf-8")
    rows.append(("2.6.6", "Unsupported format html" in t, ""))
    for v in ("2.7.12", "2.8.0"):
        t = need(f"export/{v}.html").read_text(encoding="utf-8")
        present = all(w in t for w in ("Secret", "sA", "FieldA", "nA", "kpg notes v0"))
        absent = [w for w in ("bin-A", "blob.bin", "cdA", "Recycle Bin") if w in t]
        rows.append((v, present and not absent, f"absent ones present: {absent}"))
    return rows


@check("P03", "XML export: everything, protected values unencrypted")
def _():
    rows = []
    for v in ("2.6.6", "2.7.12", "2.8.0"):
        t = need(f"export/{v}.xml").read_text(encoding="utf-8")
        ok = all(w in t for w in ("<History>", "cdA", "bin-A", "blob.bin", "kpg notes v0", "<Tags>")) and \
            re.search(r"<Key>Secret</Key>\s*<Value[^>]*>sA</Value>", t) is not None
        rows.append((v, ok, ""))
    return rows


# ---- KeePass synchronize, section 9 --------------------------------------------------------

@check("K01", "synchronizing 2.8.0's A (resurrected beside its tombstones) with B gives the entries of synchronizing the originals")
def _():
    x, o = dump("kpsync/xcA-sync-B.txt"), dump("kpsync/A-sync-B.txt")
    same = {(t, e.group) for t, e in x.entries.items()} == {(t, e.group) for t, e in o.entries.items()}
    gone = all(t not in x.entries for t in ("del-A-unch", "del-A-chB-before")) and "del-A-chB-after" in x.entries and "KPG/GDelA" not in x.groups
    return [("xcA-sync-B", same and gone, f"same entries {same}, deletions applied {gone}")]


@check("K02", "synchronizing the original A with 2.8.0's B loses A's group tag")
def _():
    return [("A-sync-xcB", dump("kpsync/A-sync-xcB.txt").groups["KPG/GTagsA"].tags == [] and dump("kpsync/A-sync-B.txt").groups["KPG/GTagsA"].tags == ["gtA"], "")]


@check("K03", "the location time KeePassXC regressed on move-A-edit-B comes through KeePass's synchronize")
def _():
    e = dump("kpsync/xcA-sync-B.txt").entries["move-A-edit-B"]
    return [("xcA-sync-B", e.loc == "+00:00:00" and e.prev == "-", f"{e.group} loc {e.loc} prev {e.prev}")]


@check("K04", "KeePassLib's synchronize, A target and B source, resolves both-tie to B's version with no trace of A's")
def _():
    e = dump("kpsync/A-sync-B.txt").entries["both-tie"]
    return [("A-sync-B", has(e.cur, "FieldB=b") and not any(has(x, "FieldA=") for x in e.everywhere()), e.cur)]


# ---- The releases --------------------------------------------------------------------------

@check("Z01", "each release: both programs validly signed by DroidMonkey Apps, LLC, KeePassXC.exe reporting the release")
def _():
    rows = []
    for l in need("releases.txt").read_text(encoding="utf-8").splitlines():
        if not re.match(r"^\d", l):
            continue
        v, exe = l.split()[:2]
        ok = "sig=Valid" in l and "signer=DroidMonkey Apps" in l and (exe != "KeePassXC.exe" or f"fileversion={v}" in l)
        rows.append((f"{v} {exe}", ok, ""))
    return rows


def main() -> None:
    print(f"# claims checked over {ROOT.name}")
    for cid, text, fn in CHECKS:
        try:
            rows = fn()
        except NoData as missing:
            print(f"{cid}  no data   {text}  (needs {missing})")
            continue
        verdicts = [ok for _, ok, _ in rows if ok is not None]
        failed = [(c, ev) for c, ok, ev in rows if ok is False]
        if not verdicts:
            status = "info"
        elif failed:
            status = "FAILS"
        else:
            status = "holds"
        print(f"{cid}  {status:<8}  {text}  ({len(verdicts) - len(failed)} of {len(verdicts)})")
        for c, ok, ev in rows:
            if ok is False or ok is None or cid in ("C03", "R16", "R17", "L01", "B01", "E02", "H01", "H02"):
                print(f"        {c}: {'ok' if ok else ('-' if ok is None else 'NOT SO')} {ev}")


if __name__ == "__main__":
    main()
