"""Judges the history extension, and part 0 again with every rule.

    history.py <rules.py> <history-dir> <part0-dir> <part0-out-dir>

<history-dir> holds what `scripts/condense.py` wrote for the extension (`recorded/history/` or a
fresh `out/history/`); the tables go into the same directory: `verdicts.tsv`, `counts.md`,
`limits.md`, `holdings.md`, `shortest.md` and `checks.txt`. Part 0's folded dumps in <part0-dir>
(`recorded/`) are judged again under every rule of <rules.py> by `scripts/verdicts.py`, into
<part0-out-dir>, so that part 0's record stays as it was.

The parts, by the scenario's edit: `limits-<limit>-<N>` (1), `maintenance-*` (2), `library-*` and
`pykeepass-*` (3), `both-character` (4), `tamper-*` (5). The decided state, the verdicts and the
columns are those of `scripts/verdicts.py`.
"""

import collections
import importlib.util
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("verdicts", HERE / "verdicts.py")
V = importlib.util.module_from_spec(spec)
spec.loader.exec_module(V)

PARTS = {"1": "history limits", "2": "Database Maintenance", "3": "an edit that leaves no history",
         "4": "an edit in both copies", "5": "a history item tampered with"}
REFUSALS = ["retirement", "revocation", "removal", "marker"]
LETTER = {"wider": 0, "unchanged": 1, "refusing": 2}


def part(edit):
    return {"limits": "1", "maintenance": "2", "library": "3", "pykeepass": "3", "both": "4", "tamper": "5"}[edit.split("-")[0]]


def steps(edit, merge):
    """B's edits, A's, a script's or a maintenance run, and the merge."""
    if edit.startswith("limits-"):
        n = int(edit.rsplit("-", 1)[1])
    elif edit.startswith("maintenance-before") or edit.startswith("tamper-") or edit == "both-character":
        n = 2
    elif edit.startswith("maintenance-after"):
        n = 2 if merge not in ("A", "B") else 1
    else:
        n = 1
    if merge == "A" and edit == "both-character":
        return 1
    return n + (0 if merge in ("A", "B") else 1)


def counts(cells_of, verdict, rule):
    n = [0, 0, 0]
    for c in cells_of:
        n[LETTER[verdict[c, rule][0]]] += 1
    return n


def fmt(n):
    return "/".join(map(str, n))


def main(rules_path, d, part0, part0_out):
    V.main(rules_path, part0, part0_out)
    d = pathlib.Path(d)
    rules = V.load_rules(rules_path).RULES
    dumps, cells = V.load(d)
    base_cell = "revocation.maintenance-before-30.untouched|A"
    fp0, fp1 = V.fingerprints(dumps[cells[base_cell]])
    decided = {r: rules[r](dumps[cells[base_cell]]) for r in rules}
    checks = []
    for r in rules:
        st = decided[r]
        ok = (st.serving == [fp1] and not st.undecided and not st.store_refuses and st.admitted == ["c1", "c2"]
              and st.names == ["auditor", "reader", "writer"] and st.hidden == ["g:bin"])
        checks.append(f"{r} on the unedited copy: the decided state: {'yes' if ok else 'NO ' + repr(st)}")
    states = {(r, sid): rules[r](dump) for r in rules for sid, dump in dumps.items()}
    verdict = {(c, r): V.classify(states[r, sid], decided[r], fp0) for c, sid in cells.items() for r in rules}
    lines = ["\t".join(["cell"] + list(rules))]
    lines += ["\t".join([c] + [": ".join(filter(None, verdict[c, r])) for r in rules]) for c in sorted(cells)]
    (d / "verdicts.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    split = V.split
    merge_cells = [c for c in cells if split(c)[4] not in ("A", "B")]
    scenarios = sorted({split(c)[0] for c in cells})

    # holdings of the record under test
    orig = {}
    base = dumps[cells[base_cell]]
    for t, label in V.TARGET_LABEL.items():
        orig[t] = next(v["canon"] for v in base["entries"] if v["u"] == label and v["slot"] == "cur")

    def current(sc, side, t):
        cur = [v for v in dumps[cells[f"{sc}|{side}"]]["entries"] if v["u"] == V.TARGET_LABEL[t] and v["slot"] == "cur"]
        return cur[0]["canon"] if cur and cur[0]["canon"] != orig[t] else None

    def holding(c):
        sc, t, e, time, m = split(c)
        dump = dumps[cells[c]]
        eb, ea = current(sc, "B", t), current(sc, "A", t)
        named = {x[1] for x in dump["deleted"] if x[1]}
        by_u = collections.OrderedDict()
        for v in dump["entries"]:
            by_u.setdefault(v["u"], []).append(v)

        def code(v):
            return "O" if v["canon"] == orig[t] else "E" if v["canon"] == eb else "F" if v["canon"] == ea else "T"

        def entry(u):
            vs = by_u.get(u, [])
            cur = [v for v in vs if v["slot"] == "cur"]
            hist = [v for v in vs if v["slot"] != "cur"]
            s = (code(cur[0]) + ("" if cur[0]["g"] == "g:store" else "@2" if cur[0]["g"].startswith("new:") else "@" + cur[0]["g"][2:])) if cur else "-"
            if hist:
                s += "[" + ",".join(code(h) for h in hist) + "]"
            return s + ("!" if u in named else "")

        parts = [entry(V.TARGET_LABEL[t])]
        parts += ["n:" + entry(u) for u, vs in by_u.items() if u.startswith("new:") and any(code(v) != "T" for v in vs)]
        return " ".join(parts)

    hold = {c: holding(c) for c in cells if split(c)[1] in V.TARGETS}
    same_kinds = all(hold[c] == hold[c.replace(split(c)[1] + ".", "revocation.", 1)] for c in hold)
    checks.append(f"holdings of the five records the same cell for cell: {'yes' if same_kinds else 'no'}")
    xc_differ = sum(1 for sc in scenarios for d2 in "AB"
                    if len({hold[f"{sc}|xc-{r}-into{d2}"] for r in V.RELEASES}) > 1)
    checks.append(f"scenarios and directions in which the four keepassxc-cli releases leave different holdings: {xc_differ}")

    # counts.md
    out = ["# Verdicts of the history extension, by part and rule", "",
           "Wider / unchanged / refusing. Refusal cells are the merge cells of the four records that refuse,",
           "control cells those of c1's admission record, and the edited copy B is read as it is.", ""]
    for p, name in PARTS.items():
        out += [f"## Part {p}: {name}", ""]
        sc_p = [sc for sc in scenarios if part(sc.split(".")[1]) == p]
        ref = [c for c in merge_cells if split(c)[0] in sc_p and split(c)[1] in REFUSALS]
        ctl = [c for c in merge_cells if split(c)[0] in sc_p and split(c)[1] == "admission"]
        alone = [c for c in cells if split(c)[0] in sc_p and split(c)[1] in REFUSALS and split(c)[4] == "B"]
        out += V.table(["rule", f"refusal cells ({len(ref)})", f"control cells ({len(ctl)})", f"B alone, refusals ({len(alone)})"],
                       [[r, fmt(counts(ref, verdict, r)), fmt(counts(ctl, verdict, r)), fmt(counts(alone, verdict, r))] for r in rules])
        out += ["", "By edit, refusal cells (72 for each edit and time):", ""]
        rows = []
        for e in sorted({(split(c)[2], split(c)[3]) for c in ref}, key=lambda k: (k[0].rsplit("-", 1)[0], len(k[0]), k)):
            cs = [c for c in ref if (split(c)[2], split(c)[3]) == e]
            rows.append([e[0] + ("" if e[1] == "untouched" else " " + e[1])] + [fmt(counts(cs, verdict, r)) for r in rules])
        out += V.table(["edit"] + list(rules), rows)
        out += ["", "By edit, control cells:", ""]
        rows = []
        for e in sorted({(split(c)[2], split(c)[3]) for c in ctl}, key=lambda k: (k[0].rsplit("-", 1)[0], len(k[0]), k)):
            cs = [c for c in ctl if (split(c)[2], split(c)[3]) == e]
            rows.append([e[0] + ("" if e[1] == "untouched" else " " + e[1])] + [fmt(counts(cs, verdict, r)) for r in rules])
        out += V.table(["edit"] + list(rules), rows)
        details = collections.Counter(verdict[c, "R6"][1] for c in ref + ctl + alone if verdict[c, "R6"][0] != "unchanged")
        out += ["", "R6, what the cells that are not unchanged hold:", ""]
        out += V.table(["detail", "cells"], [[k, str(v)] for k, v in sorted(details.items(), key=lambda kv: (-kv[1], kv[0]))]) if details else ["None."]
        out.append("")
    (d / "counts.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # limits.md: part 1, the edited copy alone
    out = ["# Part 1: where the written version leaves the history", "",
           "Copy B after N edits of the revocation, each in the dialog: the first edit changes a character",
           "of the payload, the others the title. Per limit and N: the history items of the entry, whether",
           "the written version is among them, and each rule's verdicts over the four records that refuse,",
           "wider / unchanged / refusing.", ""]
    rows = []
    for lim in ["default", "one", "none", "unlimited", "byte"]:
        for n in [1, 2, 3, 10, 11, 12]:
            sc = f"revocation.limits-{lim}-{n}.untouched"
            dump = dumps[cells[sc + "|B"]]
            hist = [v for v in dump["entries"] if v["u"] == "e:revoked-c3" and v["slot"] != "cur"]
            kept = any(v["canon"] == orig["revocation"] for v in hist)
            cs = [f"{t}.limits-{lim}-{n}.untouched|B" for t in REFUSALS]
            rows.append([lim, str(n), str(len(hist)), "yes" if kept else "no"] + [fmt(counts(cs, verdict, r)) for r in rules])
    out += V.table(["limit", "edits", "history items", "written version kept"] + list(rules), rows)
    out.append("")
    (d / "limits.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # holdings.md
    out = ["# What each result holds of the record under test", "",
           "The revocation's entry; the other four records hold the same in every cell (`checks.txt`).",
           "`O` the content as written, `E` the content of B's last edit, `F` the content of A's edit,",
           "`T` anything else (a history item tampered with); history items in brackets, oldest first; `-`",
           "no current version; `!` a deletion record naming the entry; `n:` a copy that Create new IDs made,",
           "`@2` its group. The four keepassxc-cli releases share a column where they agree.", ""]
    for p, name in PARTS.items():
        out += [f"## Part {p}: {name}", ""]
        rows = []
        for sc in [s for s in scenarios if s.startswith("revocation.") and part(s.split(".")[1]) == p]:
            _, e, time = sc.split(".")
            row = [e + ("" if time == "untouched" else " " + time)]
            for m in ["B", "A"] + V.COLUMNS:
                ms = m if isinstance(m, list) else [m]
                got = [hold[f"{sc}|{x}"] for x in ms]
                row.append(got[0] if len(set(got)) == 1 else " / ".join(got))
            rows.append(row)
        out += V.table(["edit", "B", "A"] + [V.column_name(m) for m in V.COLUMNS], rows)
        out.append("")
    (d / "holdings.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    # where B lost the written version: what each merge brings back
    lost = [sc for sc in scenarios if sc.startswith("revocation.") and "O" not in hold[sc + "|B"]]
    back = collections.Counter()
    for sc in lost:
        for m in V.MERGES:
            h = hold[f"{sc}|{m}"]
            back["current" if h.startswith("O") else "history" if "O" in h.split(" ")[0] else "copy" if "O" in h else "lost"] += 1
    checks.append(f"revocation scenarios whose copy B holds no written version: {len(lost)}; their merges: "
                  + ", ".join(f"{k} {v}" for k, v in sorted(back.items())))

    # shortest.md
    out = ["# The shortest sequences that end wider, by part and rule", "",
           "Steps: each edit in B or A, a script's change, a maintenance run, and the merge.", ""]
    for p, name in PARTS.items():
        out += [f"## Part {p}: {name}", ""]
        rows = []
        for r in rules:
            wide = [c for c in cells if part(split(c)[2]) == p and verdict[c, r][0] == "wider"]
            if not wide:
                rows.append([r, "none", "-", "-", "-"])
                continue
            n = min(steps(split(c)[2], split(c)[4]) for c in wide)
            best = sorted(c for c in wide if steps(split(c)[2], split(c)[4]) == n)
            groups = collections.OrderedDict()
            for c in best:
                sc, t, e, time, m = split(c)
                groups.setdefault((e + ("" if time == "untouched" else " " + time), verdict[c, r][1]), []).append((t, V.merge_short(m)))
            for (e, det), items in groups.items():
                recs = sorted({t for t, _ in items})
                ms = [x for x in dict.fromkeys(m for _, m in items)]
                rows.append([r, str(n), e, ", ".join(recs) + ": " + ", ".join(ms), det])
        out += V.table(["rule", "steps", "edit", "records: merges", "what is wider"], rows)
        out.append("")
    (d / "shortest.md").write_text("\n".join(out), encoding="utf-8", newline="\n")

    checks.append(f"cells {len(cells)}, merge cells {len(merge_cells)}, distinct files {len(dumps)}")
    (d / "checks.txt").write_text("\n".join(checks) + "\n", encoding="utf-8", newline="\n")
    print("\n".join(checks))


if __name__ == "__main__":
    main(*sys.argv[1:5])
