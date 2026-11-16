"""Gives every cell a verdict under every reading rule, and writes the tables the report quotes.

    verdicts.py <rules.py> <dir> [<out-dir>] [--only R1,R2,...]

<dir> holds what `scripts/condense.py` wrote (`recorded/` or a fresh `out/`); the results go into
<out-dir>, by default the same directory, and cover the rules `--only` names, by default all: `verdicts.tsv`, `counts.md`, `contents.md`, `meanings.md`, `shortest.md`,
`boundary.md` and `checks.txt`. The rules are whatever `RULES` in <rules.py> names.

A cell is `<scenario>|<merge>`; a scenario is `<target>.<edit>.<time>`. `A` and `B` are the two
copies read as they are; every other merge is a result read after the merge. The decided state is
what the unedited base gives: authority a1 serves and a0 is retired, clients c1 and c2 are admitted
and c3 is revoked, `reader`, `writer` and `auditor` are on the list and `legacy` is removed, and the
recycle bin's marker hides `g:bin`. A cell is **wider** when anything the decided state refuses is
served, **refusing** when nothing is wider and anything the decided state serves is refused, and
**unchanged** otherwise.
"""

import collections
import importlib.util
import json
import pathlib
import sys

TARGETS = ["retirement", "revocation", "removal", "marker", "admission"]
TARGET_LABEL = {"retirement": "e:retired-a0", "revocation": "e:revoked-c3", "removal": "e:removed-legacy",
                "marker": "e:bin-marker", "admission": "e:client-c1"}
TARGET_NAME = {"retirement": "retirement of a0", "revocation": "revocation of c3", "removal": "removal of legacy",
               "marker": "recycle bin marker", "admission": "admission record of c1 (control)"}
EDITS = ["character", "rename", "delete-string", "empty", "title", "move", "recycle", "delete-record",
         "delete-no-record", "duplicate"]
EDIT_NAME = {"character": "one character of the payload", "rename": "payload string renamed",
             "delete-string": "payload string deleted", "empty": "every string emptied", "title": "title only",
             "move": "moved to another group", "recycle": "moved to the recycle bin",
             "delete-record": "deleted with a deletion record", "delete-no-record": "deleted without a record",
             "duplicate": "duplicated, original edited", "none": "no edit"}
TIMES = ["untouched", "older", "newer", "same"]
TIME_NAME = {"untouched": "A untouched", "older": "A's title edit later", "newer": "A's title edit earlier",
             "same": "A's title edit in the same second", "title-only": "A's title edit alone"}
METHODS = ["Synchronize", "OverwriteIfNewer", "OverwriteExisting", "KeepExisting", "CreateNewUuids"]
METHOD_SHORT = {"Synchronize": "Sync", "OverwriteIfNewer": "OIN", "OverwriteExisting": "OE",
                "KeepExisting": "KE", "CreateNewUuids": "CNI"}
RELEASES = ["2.4.2", "2.6.6", "2.7.12", "2.8.0"]
MERGES = [f"kp-{m}-into{d}" for d in "AB" for m in METHODS] + [f"xc-{r}-into{d}" for d in "AB" for r in RELEASES]
# The columns of `contents.md`: each KeePassLib method alone, the four releases together.
COLUMNS = [f"kp-{m}-intoA" for m in METHODS] + [[f"xc-{r}-intoA" for r in RELEASES]] \
    + [f"kp-{m}-intoB" for m in METHODS] + [[f"xc-{r}-intoB" for r in RELEASES]]


def column_name(m):
    return "xc into " + m[0][-1] if isinstance(m, list) else merge_short(m)


def merge_short(m):
    if m in ("A", "B"):
        return m + " alone"
    tool, name, into = m.split("-")
    return (METHOD_SHORT[name] if tool == "kp" else "xc " + name) + " " + into.replace("into", "into ")


def load_rules(path):
    spec = importlib.util.spec_from_file_location("rules", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load(d):
    d = pathlib.Path(d)
    versions = {}
    for line in (d / "versions.jsonl").read_text(encoding="utf-8").splitlines():
        v = json.loads(line)
        versions[v.pop("id")] = v
    rows = [json.loads(line) for line in (d / "rows.jsonl").read_text(encoding="utf-8").splitlines()]
    frames = {}
    for line in (d / "frames.jsonl").read_text(encoding="utf-8").splitlines():
        f = json.loads(line)
        frames[f.pop("id")] = f
    dumps = {}
    for line in (d / "states.jsonl").read_text(encoding="utf-8").splitlines():
        s = json.loads(line)
        frame = frames[s["frame"]]
        entries = []
        for i in s["rows"]:
            u, g, slot, loc, vid = rows[i]
            e = {"u": u, "g": g, "slot": slot, **versions[vid]}
            if loc is not None:
                e["loc"] = loc
            entries.append(e)
        dumps[s["id"]] = {"bin": frame["bin"], "groups": frame["groups"], "deleted": s["deleted"], "entries": entries}
    cells = {}
    for line in (d / "cells.tsv").read_text(encoding="utf-8").splitlines():
        cell, sid = line.split("\t")
        cells[cell] = sid
    return dumps, cells


def split(cell):
    scenario, merge = cell.split("|")
    target, edit, time = scenario.split(".")
    return scenario, target, edit, time, merge


def fingerprints(dump):
    fps = {}
    for v in dump["entries"]:
        if v["slot"] == "cur" and v["s"].get("KPG.Kind") == "authority":
            fps[v["u"]] = json.loads(v["s"]["KPG.Data"])["fp"]
    return fps["e:authority-a0"], fps["e:authority-a1"]


def classify(st, decided, fp0):
    if st.store_refuses:
        return "refusing", "the store refuses"
    if st.undecided:
        return "refusing", "undecided: a0 and a1 both serve"
    if not st.serving:
        return "refusing", "no authority serves"
    wider, more = [], []
    if st.serving != decided.serving:
        (wider if st.serving == [fp0] else more).append("a0 serves" if st.serving == [fp0] else "a1 does not serve")
    wider += [c + " admitted" for c in sorted(set(st.admitted) - set(decided.admitted))]
    more += [c + " refused" for c in sorted(set(decided.admitted) - set(st.admitted))]
    wider += [n + " on the list" for n in sorted(set(st.names) - set(decided.names))]
    more += [n + " off the list" for n in sorted(set(decided.names) - set(st.names))]
    wider += [g + " not hidden" for g in sorted(set(decided.hidden) - set(st.hidden))]
    more += [g + " hidden" for g in sorted(set(st.hidden) - set(decided.hidden))]
    if wider:
        return "wider", "; ".join(wider + more)
    if more:
        return "refusing", "; ".join(more)
    return "unchanged", ""


class Shorts:
    """The record under test in a cell, in short.

    For each entry that holds the record: its current version and then its history items in
    brackets. `O` is the content (the `KPG.` strings) as written, `E` the content B's edit gave it,
    `?` anything else; a following `a` is A's title, `b` B's title, `c` the copy's title. `@g` is
    the group `General`, `@r` the recycle bin; `@2`, `@2g` and `@2r` are the second `KeePassGate`,
    `General` and `Recycle Bin` that Create new IDs makes. `-` is no
    current version, `!` a deletion record naming the entry. The original entry comes first, then
    `c:` the duplicate B made, then `n:` each copy under an identifier Create new IDs drew.
    """

    def __init__(self, dumps, cells):
        base = dumps[cells["none.none.untouched|A"]]
        self.orig = {}
        self.title = {}
        for t, label in TARGET_LABEL.items():
            v = next(v for v in base["entries"] if v["u"] == label and v["slot"] == "cur")
            self.orig[t] = v["canon"]
            self.title[t] = v["title"]
        self.edited = {}
        for cell, sid in cells.items():
            scenario, target, edit, time, merge = split(cell)
            if merge == "B" and target in TARGET_LABEL:
                cur = [v for v in dumps[sid]["entries"] if v["u"] == TARGET_LABEL[target] and v["slot"] == "cur"]
                if cur and cur[0]["canon"] != self.orig[target]:
                    self.edited[scenario] = cur[0]["canon"]

    def __call__(self, dump, scenario, target):
        orig, edited, base_title = self.orig[target], self.edited.get(scenario), self.title[target]
        by_u = collections.OrderedDict()
        for v in dump["entries"]:
            by_u.setdefault(v["u"], []).append(v)
        named = {d[1] for d in dump["deleted"] if d[1]}

        def code(v):
            c = "O" if v["canon"] == orig else "E" if edited and v["canon"] == edited else "?"
            t = v["title"]
            if t == base_title or (c == "E" and t == ""):
                return c
            for suffix, mark in ((" (A)", "a"), (" (seen)", "b"), (" - Copy", "c")):
                if t == base_title + suffix:
                    return c + mark
            return c + "~"

        names = {g[0]: g[1] for g in dump["groups"]}

        def where(v):
            g = v["g"]
            if g in ("g:store", "g:general", "g:bin"):
                return {"g:store": "", "g:general": "@g", "g:bin": "@r"}[g]
            second = {"KeePassGate": "@2", "General": "@2g", "Recycle Bin": "@2r"}
            return second.get(names.get(g), "@" + g) if g.startswith("new:") else "@" + g

        def entry(u):
            vs = by_u.get(u, [])
            cur = [v for v in vs if v["slot"] == "cur"]
            hist = [v for v in vs if v["slot"] != "cur"]
            s = code(cur[0]) + where(cur[0]) if cur else "-"
            if hist:
                s += "[" + ",".join(code(h) for h in hist) + "]"
            return s + ("!" if u in named else "")

        parts = [entry(TARGET_LABEL[target])]
        if "e:copy" in by_u or "e:copy" in named:
            parts.append("c:" + entry("e:copy"))
        for u, vs in by_u.items():
            if u.startswith("new:") and any(v["canon"] == orig or (edited and v["canon"] == edited) for v in vs):
                parts.append("n:" + entry(u))
        return " ".join(parts)


def table(head, rows):
    out = ["| " + " | ".join(head) + " |", "| " + " | ".join("---" for _ in head) + " |"]
    out += ["| " + " | ".join(r) + " |" for r in rows]
    return out


def main(rules_path, d, out=None, only=None):
    d = pathlib.Path(d)
    o = pathlib.Path(out) if out else d
    o.mkdir(parents=True, exist_ok=True)
    rules = load_rules(rules_path).RULES
    if only:
        rules = {r: rules[r] for r in only}
    dumps, cells = load(d)
    base = dumps[cells["none.none.untouched|A"]]
    fp0, fp1 = fingerprints(base)
    checks = []

    states = {r: {} for r in rules}
    for r, rule in rules.items():
        for sid, dump in dumps.items():
            states[r][sid] = rule(dump)
    decided = {r: states[r][cells["none.none.untouched|A"]] for r in rules}
    for r in rules:
        st = decided[r]
        ok = (st.serving == [fp1] and not st.undecided and not st.store_refuses and st.admitted == ["c1", "c2"]
              and st.names == ["auditor", "reader", "writer"] and st.hidden == ["g:bin"])
        checks.append(f"{r} on the unedited copy: a1 serves, c1 and c2 admitted, auditor reader writer on the list, "
                      f"g:bin hidden: {'yes' if ok else 'NO ' + repr(st)}")

    verdict = {}
    for cell, sid in cells.items():
        for r in rules:
            verdict[cell, r] = classify(states[r][sid], decided[r], fp0)

    # verdicts.tsv
    lines = ["\t".join(["cell"] + list(rules))]
    for cell in sorted(cells):
        lines.append("\t".join([cell] + [": ".join(filter(None, verdict[cell, r])) for r in rules]))
    (o / "verdicts.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    merge_cells = [c for c in cells if split(c)[4] not in ("A", "B")]
    letter = {"wider": 0, "unchanged": 1, "refusing": 2}
    shorts = Shorts(dumps, cells)
    holding = {c: shorts(dumps[sid], split(c)[0], split(c)[1]) for c, sid in cells.items() if split(c)[1] in TARGETS}

    # counts.md
    out = ["# Verdicts by reading rule", "",
           "Merge cells only: 18 a scenario (five KeePassLib methods and four `keepassxc-cli` releases, both",
           "directions). Each cell of a table is wider / unchanged / refusing, over the four time conditions of",
           "that edit (72 cells), or over the 18 cells of the scenario with A's title edit alone (`none`).", ""]
    totals = {}
    for r in rules:
        out += [f"## {r}", ""]
        rows = []
        for edit in EDITS + ["none"]:
            row = [EDIT_NAME[edit]]
            for t in TARGETS:
                n = [0, 0, 0]
                for c in merge_cells:
                    _, target, e, time, _ = split(c)
                    if target == t and e == edit:
                        n[letter[verdict[c, r][0]]] += 1
                row.append("/".join(map(str, n)))
            rows.append(row)
        out += table(["edit"] + [TARGET_NAME[t] for t in TARGETS], rows)
        tot = {"refusals": [0, 0, 0], "control": [0, 0, 0], "no edit": [0, 0, 0]}
        for c in merge_cells:
            _, target, e, time, _ = split(c)
            key = "no edit" if e == "none" else "control" if target == "admission" else "refusals"
            tot[key][letter[verdict[c, r][0]]] += 1
        totals[r] = tot
        out += ["", "Totals, wider / unchanged / refusing: " + "; ".join(f"{k} {'/'.join(map(str, v))} of {sum(v)}" for k, v in tot.items()), ""]
        details = collections.Counter(verdict[c, r][1] for c in merge_cells if verdict[c, r][0] != "unchanged")
        out += ["What the cells that are not unchanged hold:", ""]
        out += table(["detail", "cells"], [[k, str(v)] for k, v in sorted(details.items(), key=lambda kv: (-kv[1], kv[0]))])
        out += ["", "The edited copy B, read as it is, with no merge:", ""]
        rows = []
        for edit in EDITS:
            rows.append([EDIT_NAME[edit]] + [": ".join(filter(None, verdict[f"{t}.{edit}.untouched|B", r])) for t in TARGETS])
        out += table(["edit"] + [TARGET_NAME[t] for t in TARGETS], rows)
        out.append("")
    out += ["## All rules", ""]
    out += table(["rule", "refusal cells (2,880)", "control cells (720)", "no-edit cells (108)"],
                 [[r] + ["/".join(map(str, totals[r][k])) for k in ("refusals", "control", "no edit")] for r in rules])
    out.append("")
    (o / "counts.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # contents.md
    out = ["# What each result holds of the record under test", "", Shorts.__doc__.strip().replace("\n    ", "\n"), "",
           "Each edit's rows carry the four time conditions: `untouched`, A as it was made; `older`, A edits",
           "the same entry's title 1800 seconds after B's edit; `newer`, 1800 seconds before it; `same`, in the",
           "same second. `none title-only` is A's title edit with no edit in B. Where the four `keepassxc-cli`",
           "releases agree, their column holds the result once; where they differ, all four in release order.",
           "The verdicts under each rule, cell by cell, are in `verdicts.tsv`.", ""]
    bodies = {}
    for t in TARGETS:
        out += [f"## {TARGET_NAME[t]}", ""]
        rows = []
        scenarios = [f"{t}.none.title-only"] + [f"{t}.{e}.{time}" for e in EDITS for time in TIMES]
        for sc in scenarios:
            _, e, time = sc.split(".")
            row = [f"{e} {time}"]
            for m in ["B", "A"] + COLUMNS:
                ms = m if isinstance(m, list) else [m]
                got = [holding.get(sc + "|" + x, "not run") for x in ms]
                row.append(got[0] if len(set(got)) == 1 else " / ".join(got))
            rows.append(row)
        bodies[t] = rows
        out += table(["edit time", "B", "A"] + [column_name(m) for m in COLUMNS], rows)
        out.append("")
        witness = "w:retired-a0" if t == "retirement" else "w:revoked-c3" if t == "revocation" else None
        if witness:
            missing = [c for c in cells if c.startswith(t + ".") and not any(dd[1] == witness for dd in dumps[cells[c]]["deleted"])]
            out += [f"The witness `{witness}` is in every file of this kind: {'yes' if not missing else 'no, missing in ' + ', '.join(sorted(missing))}.", ""]
    (o / "contents.md").write_text("\n".join(out), encoding="utf-8", newline="\n")
    same = [t for t in TARGETS if bodies[t] == bodies[TARGETS[0]]]
    xc_differ = sum(1 for t in TARGETS for row in bodies[t] for x in row if " / " in x)
    checks.append(f"kinds whose table in contents.md equals the {TARGETS[0]}'s cell for cell: {len(same)} of {len(TARGETS)}")
    checks.append(f"cells of contents.md where the four keepassxc-cli releases differ: {xc_differ}")

    # meanings.md: what each holding means, kind by kind and rule by rule
    out = ["# What each holding means, kind by kind", "",
           "For every kind of record and every edit: each distinct holding of `contents.md` among the merge",
           "cells of that edit, the number of merge cells that ended with it, and the verdict under each rule.",
           "A verdict that differs between cells with the same holding is given with the count of each.", ""]
    for t in TARGETS:
        out += [f"## {TARGET_NAME[t]}", ""]
        rows = []
        for edit in EDITS + ["none"]:
            groups = collections.OrderedDict()
            for c in sorted(merge_cells):
                if split(c)[1] == t and split(c)[2] == edit:
                    groups.setdefault(holding[c], []).append(c)
            for h, cs in groups.items():
                row = [EDIT_NAME[edit], h, str(len(cs))]
                for r in rules:
                    seen = collections.Counter(": ".join(filter(None, verdict[c, r])) for c in cs)
                    row.append(next(iter(seen)) if len(seen) == 1 else "; ".join(f"{k} ({v})" for k, v in seen.items()))
                rows.append(row)
        out += table(["edit", "holding", "cells"] + list(rules), rows)
        out.append("")
    (o / "meanings.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # checks.txt
    other_bin = [c for c, sid in cells.items() if dumps[sid]["bin"] != "g:bin"]
    checks.append(f"files whose recycle bin is not g:bin: {len(other_bin)} of {len(cells)}")
    for w in ("w:retired-a0", "w:revoked-c3"):
        n = sum(1 for sid in cells.values() if any(x[1] == w for x in dumps[sid]["deleted"]))
        checks.append(f"files holding the witness {w}: {n} of {len(cells)}")
    stray = [c for c in merge_cells if split(c)[2] == "none" and any(verdict[c, r][0] != "unchanged" for r in rules)]
    checks.append(f"no-edit merge cells not unchanged under some rule: {len(stray)}" + (": " + ", ".join(sorted(stray)) if stray else ""))
    ctrl = [c for c in cells if split(c)[1] == "admission" and any(verdict[c, r][0] == "wider" for r in rules)]
    checks.append(f"control cells wider under some rule: {len(ctrl)}")
    checks.append(f"cells {len(cells)}, merge cells {len(merge_cells)}, distinct files {len(dumps)}")
    (o / "checks.txt").write_text("\n".join(checks) + "\n", encoding="utf-8", newline="\n")

    # shortest.md
    out = ["# The shortest sequences that end wider", "",
           "Steps: B's edit (1); A's own title edit, where the time condition has one (1); a merge (1). The",
           "copy that was edited, read as it is, takes one step. Every sequence that ends wider is listed by",
           "its length and by the copy it ends in.", ""]
    for r in rules:
        out += [f"## {r}", ""]
        by_len = collections.defaultdict(list)
        for c in cells:
            scenario, target, edit, time, merge = split(c)
            if verdict[c, r][0] != "wider" or merge == "A":
                continue
            if merge == "B":
                if time not in ("untouched", "title-only"):
                    continue
                n, into = 1, "B, as edited"
            else:
                n = 2 + (0 if time == "untouched" else 1)
                into = "A" if merge.endswith("intoA") else "B"
            by_len[n].append((into, target, edit, time, merge, verdict[c, r][1]))
        if not by_len:
            out += ["No cell ends wider.", ""]
            continue
        for n in sorted(by_len):
            out += [f"### {n} step{'s' if n > 1 else ''}", ""]
            grouped = collections.defaultdict(list)
            for into, target, edit, time, merge, det in by_len[n]:
                grouped[(into, target, edit, time if n > 2 else "", det)].append(merge)
            rows = []
            for (into, target, edit, time, det), ms in sorted(grouped.items()):
                ms = [m for m in ["B"] + MERGES if m in ms]
                rows.append([into, TARGET_NAME[target], EDIT_NAME[edit] + (f", {TIME_NAME[time]}" if time else ""),
                             ", ".join(merge_short(m) for m in ms), det])
            out += table(["ends in", "record", "edit", "merges", "what is wider"], rows)
            out.append("")
    (o / "shortest.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # boundary.md: what the same second decides
    out = ["# Cells that depend on the second boundary", "",
           "For every record, edit, merge and rule: the verdict when A's title edit falls in the same second",
           "as B's edit (`same`), beside the verdicts when it falls 1800 seconds later (`older`, B's edit the",
           "older) or earlier (`newer`). Listed where the three are not all alike; the last column names the",
           "condition whose verdict `same` shares.", ""]
    rows = []
    for t in TARGETS:
        for e in EDITS:
            for m in MERGES:
                for r in rules:
                    if f"{t}.{e}.same|{m}" not in cells:
                        continue
                    v = {time: verdict[f"{t}.{e}.{time}|{m}", r][0] for time in ("older", "same", "newer")}
                    if len(set(v.values())) > 1:
                        side = "older" if v["same"] == v["older"] else "newer" if v["same"] == v["newer"] else "neither"
                        rows.append([r, TARGET_NAME[t], EDIT_NAME[e], merge_short(m), v["older"], v["same"], v["newer"], side])
    out += table(["rule", "record", "edit", "merge", "older", "same", "newer", "same as"], rows)
    out += ["", f"{len(rows)} rows."]
    (o / "boundary.md").write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")

    print("\n".join(checks))
    for r in rules:
        print(r, "; ".join(f"{k} {'/'.join(map(str, v))}" for k, v in totals[r].items()))


if __name__ == "__main__":
    args = sys.argv[1:]
    only = None
    if "--only" in args:
        i = args.index("--only")
        only = args[i + 1].split(",")
        del args[i:i + 2]
    main(args[0], args[1], args[2] if len(args) > 2 else None, only)
