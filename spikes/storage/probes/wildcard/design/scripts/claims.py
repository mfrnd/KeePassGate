"""Counts, in one run of the design's probe, the numbers `design.md` quotes from it.

Written on this branch, not by the design's agent. It reads `summary.txt`, `merges.txt`,
`kdbxweb.txt`, `keepass247.txt`, `bench.txt` and `lattice.txt` from the directory it is given,
`recorded` or `out`, and prints one line a claim, so that the record and a fresh run can be held
to the paper and to each other with the same code:

    uv run --no-project python -I -X utf8 claims.py <recorded or out directory>

It measures nothing: every number is a count over what the probe printed.
"""

import re
import sys
from collections import Counter
from pathlib import Path

SEVEN = ["U", "C", "H", "R", "D", "X", "K"]
FIVE = ["U", "C", "R", "D", "K"]
KDBXWEB_SETS = ["U", "C", "H", "D", "K"]
METHODS = ["Synchronize", "OverwriteIfNewer", "OverwriteExisting", "KeepExisting"]
RELEASES = ["2.4.2", "2.6.6", "2.7.12", "2.8.0"]
EMPTY_FOUR = "[Notes(0),Password(0),URL(0),UserName(0)]"


def read(directory: Path, name: str) -> list[str]:
    return (directory / name).read_text(encoding="utf-8-sig").splitlines()


def canon(shown: str) -> str:
    """A joined state as `Show` prints it, with its rules in one order.

    The probe orders a state's rules by their nodes' identifiers, which each `make` draws at
    random, so two runs, or the two bases the design's run made, can print the same rules in two
    orders."""
    m = re.search(r"rules=\[(.*)\]", shown)
    if not m:
        return shown
    return shown[: m.start(1)] + "; ".join(sorted(m.group(1).split("; "))) + shown[m.end(1):]


def summary(directory: Path) -> dict[str, dict[str, str]]:
    cells: dict[str, dict[str, str]] = {}
    for line in read(directory, "summary.txt"):
        if " | " not in line:
            continue
        parts = line.split(" | ")
        label = parts[0].strip()
        cell = {"verdict": parts[1].strip(), "line": line}
        for part in parts[2:]:
            key, _, value = part.partition(" ")
            cell[key] = value
        state = cell.get("state", "")
        cell["shown"] = canon(state.split(" ", 1)[1]) if " " in state else ""
        cells[label] = cell
    return cells


def kp(set_: str, into: str, method: str) -> str:
    return f"KeePass {set_} into {into} {method}"


def xc(release: str, set_: str, into: str) -> str:
    return f"KeePassXC {release} {set_} into {into} (exit 0)"


def blocks(directory: Path) -> list[tuple[str, str, list[str]]]:
    out: list[tuple[str, str, list[str]]] = []
    label, header, body = "", "", []
    for line in read(directory, "merges.txt"):
        if line.startswith("## "):
            if label:
                out.append((label, header, body))
            label, header, body = line[3:], "", []
        elif line.startswith("# "):
            header = line
        elif line:
            body.append(line)
    if label:
        out.append((label, header, body))
    return out


def main() -> None:
    d = Path(sys.argv[1])
    s = summary(d)
    say = print

    # Sizes.
    base = next(b for b in blocks(d) if b[0] == "base")
    say("base snapshot text:", re.search(r"text=(\d+)", "\n".join(base[2])).group(1))
    say("bench snapshot text:", sorted(set(re.findall(r"snapshot text (\d+) chars", "\n".join(read(d, "bench.txt"))))))
    big = next(line for line in read(d, "summary.txt") if line.startswith("big: "))
    say("big:", big)

    # Signatures.
    say("signatures valid:", sum(1 for line in read(d, "summary.txt") if line.startswith("keepassxc-cli") and ": Valid " in line))

    # KeePass's four matching methods and KeePassXC's four releases, sets U, C, R, D, K.
    for who, label in (("KeePass", kp), ("KeePassXC", xc)):
        names = METHODS if who == "KeePass" else RELEASES
        cells = [(set_, into, n) for set_ in FIVE for into in "AB" for n in names]
        force = sum(1 for set_, into, n in cells if s[label(set_, into, n) if who == "KeePass" else label(n, set_, into)]["verdict"] == "IN FORCE")
        say(f"{who} sets U C R D K: {len(cells)} cells, {force} in force")
        for set_ in FIVE:
            shown = {s[label(set_, into, n) if who == "KeePass" else label(n, set_, into)]["shown"] for into in "AB" for n in names}
            say(f"  {who} set {set_}: {len(shown)} distinct joined state(s)")
    for set_ in FIVE:
        a = {s[kp(set_, into, m)]["shown"] for into in "AB" for m in METHODS}
        b = {s[xc(r, set_, into)]["shown"] for into in "AB" for r in RELEASES}
        say(f"set {set_}: KeePassXC's joined state equals KeePass's: {a == b}")
    sib = sum(1 for into in "AB" for n in METHODS if "SIBLINGS" in s[kp("C", into, n)]["shown"]) + sum(
        1 for into in "AB" for r in RELEASES if "SIBLINGS" in s[xc(r, "C", into)]["shown"])
    say("set C, both rule documents standing:", sib, "of 16")
    k = sum(1 for c in [s[kp("K", i, m)] for i in "AB" for m in METHODS] + [s[xc(r, "K", i)] for i in "AB" for r in RELEASES]
            if "retired=[fpA]" in c["shown"] and "clients=[c1,c2,c3]" in c["shown"])
    say("set K, fpA retired and c1 c2 c3 back:", k, "of 16")
    for set_ in "RD":
        n = sum(1 for c in [s[kp(set_, i, m)] for i in "AB" for m in METHODS] + [s[xc(r, set_, i)] for i in "AB" for r in RELEASES]
                if c["verdict"] == "IN FORCE")
        say(f"set {set_}: in force in {n} of 16")
    x = Counter(c["verdict"] for c in [s[kp("X", i, m)] for i in "AB" for m in METHODS] + [s[xc(r, "X", i)] for i in "AB" for r in RELEASES])
    say("set X:", dict(x))
    say("set U into B, records each copy received:",
        {m: s[kp("U", "B", m)]["records"] for m in METHODS + ["CreateNewUuids"]},
        {r: s[xc(r, "U", "B")]["records"] for r in RELEASES})
    say("set U into B Synchronize, present:", s[kp("U", "B", "Synchronize")]["present"])
    say("set U into A KeePassXC, present:", {r: s[xc(r, "U", "A")]["present"] for r in RELEASES})

    # Create new IDs.
    cnu = Counter()
    for set_ in SEVEN:
        for into in "AB":
            verdict = s[kp(set_, into, "CreateNewUuids")]["verdict"]
            cnu[verdict.split(":")[0] + (f" ({set_} into {into})" if not verdict.startswith("UNDECIDED: a copy") else "")] += 1
    say("Create new IDs, 14 cells:", dict(cnu))

    # Set H.
    say("set H into A, KeePass's four methods:", Counter(s[kp("H", "A", m)]["verdict"].split(":")[0] for m in METHODS))
    say("set H into B, Synchronize:", s[kp("H", "B", "Synchronize")]["verdict"].split(":")[0])
    say("set H into A, KeePassXC:", Counter(s[xc(r, "H", "A")]["verdict"].split(":")[0] + " " + s[xc(r, "H", "A")]["present"] for r in RELEASES))
    say("set H into B, three import methods:", Counter(s[kp("H", "B", m)]["verdict"].split(":")[0] for m in METHODS[1:]))
    say("set H into B, KeePassXC:", Counter(s[xc(r, "H", "B")]["verdict"].split(":")[0] for r in RELEASES))
    gone = [c for c in s.values() if c["verdict"].startswith("GONE")]
    say("every GONE line refuses c1 by its witness:", len(gone), sum(1 for c in gone if "c1(witness)" in c["refused"]))

    # Chains.
    chains = [c for label, c in s.items() if re.match(r"[UCH] KeePassXC .* then KeePass Synchronize", label)]
    say("chains:", len(chains), Counter((label[0], c["verdict"].split(":")[0], c["shown"][:60]) for label, c in s.items() if re.match(r"[UCH] KeePassXC .* then KeePass Synchronize", label)).most_common())
    next_writes = [(label, c) for label, c in s.items() if label.endswith("then the plugin's next write")]
    say("next writes:", len(next_writes))
    sources = {
        "C after KeePass Synchronize into A": kp("C", "A", "Synchronize"),
        "C after KeePassXC 2.8.0 into B": xc("2.8.0", "C", "B"),
        "U after KeePassXC 2.8.0 into A": xc("2.8.0", "U", "A"),
        "D after KeePass Synchronize into B": kp("D", "B", "Synchronize"),
        "K after KeePass Synchronize into A": kp("K", "A", "Synchronize"),
    }
    for label, c in next_writes:
        src = s[sources[label.replace(", then the plugin's next write", "")]]
        say(f"  {label}: present {c['present']}, same state as before: {c['shown'] == src['shown']}, records {c['records']}")

    # Saves.
    for r in RELEASES:
        for what in ("U-A", "big"):
            c = s[f"KeePassXC {r} add on {what} (exit 0)"]
            say(f"KeePassXC {r} add on {what}: {c['verdict']} {c['present']} records {c['records']}")
    adds = [b for b in blocks(d) if " add on " in b[0]]
    say("KeePassXC add, the database's custom data:", {b[0].split(" add on ")[0] + " " + b[0].split(" add on ")[1].split(" ")[0]: re.search(r"dbCustomData=(\d+)", b[1]).group(1) for b in adds})

    # What KeePassXC writes into a snapshot entry.
    kinds = Counter()
    for label, header, body in blocks(d):
        if not label.startswith("KeePassXC"):
            continue
        kind = "add" if " add on " in label else "by hand" if re.search(r" (rm|edit|mv) ", label) else "merge"
        for line in body:
            if not line.startswith("  snapshot "):
                continue
            four = EMPTY_FOUR in line
            moved = not re.search(r"mod=01-01T", line)
            hist = re.search(r"history=(\d+)", line).group(1)
            kinds[(kind, "four empty fields" if four else "other fields", "time moved" if moved else "time kept", "history " + hist)] += 1
    say("snapshot entries in files KeePassXC wrote:", sum(kinds.values()))
    for key, n in sorted(kinds.items()):
        say("  ", key, n)

    # By hand, Delete Duplicate Entries, pykeepass.
    for label, c in s.items():
        if re.match(r"KeePassXC \S+ (rm|edit|mv)", label) or "Delete Duplicate" in label or label.startswith("pykeepass"):
            say(f"{label}: {c['verdict']} | {c['present']} | {c['records']} | {c['refused']}")
    say("Delete Duplicate Entries:", [line for line in read(d, "summary.txt") if line.startswith("Delete Duplicate Entries removed")])

    # KDBX versions of the dumps.
    say("dumps by KDBX version:", Counter(re.search(r"kdbx (\S+)", h).group(1) for _, h, _ in blocks(d)))
    say("dumps not 4.0:", [(label, h.split()[1]) for label, h, _ in blocks(d) if "kdbx 4.0" not in h])

    # kdbxweb against KeePass's Synchronize of the same set, verdict and joined state as shown.
    kw = {line.split(" | ")[0].strip(): line.split(" | ") for line in read(d, "kdbxweb.txt") if " | " in line}
    for set_ in KDBXWEB_SETS:
        for into in "AB":
            w = kw[f"kdbxweb {set_} into {into}"]
            k_ = s[kp(set_, into, "Synchronize")]
            w_shown = canon(w[5].split(" ", 2)[2]) if len(w) > 5 else ""
            say(f"kdbxweb {set_} into {into}: {w[1].strip().split(':')[0]}, same verdict and state as Synchronize: "
                f"{w[1].strip() == k_['verdict'] and w_shown == k_['shown']}; present {w[2]} vs {k_['present']}")
    say("kdbxweb resave of U-A:", " | ".join(kw["kdbxweb resave of U-A"][1:5]))

    # KeePass 2.47.
    for line in read(d, "keepass247.txt")[1:]:
        say("2.47:", " | ".join(p.strip() for p in line.split(" | ")[:5]))

    say("lattice:", read(d, "lattice.txt"))


if __name__ == "__main__":
    main()
