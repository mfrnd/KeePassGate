"""Checks the claims of the ground report `measured` against the output of the re-read.

Added on the branch, with no recorded copy: the agent read these off its output by eye and by
console filters. Reads `droid-reread.txt`, `xc-reread-fast.txt` and `xc-reread-slow.txt` in the
directory given, `out` or `recorded`, and prints one line per claim with what the files hold.

    uv run --no-project python -I -X utf8 spikes/storage/probes/ground-measured/checks.py <dir>
"""

import re
import sys
from datetime import datetime
from pathlib import Path

HEAD = re.compile(r"^(\S+\.kdbx) \| kdbx (\S+) \| .* SettingsChanged (\S+) \| RecycleBin \S+ \| deleted (\d+)$")
ENTRY = re.compile(r"^  entry (\S+) \S+ in (\S+) mtime (\S+) rule (\S) tags \[(.*?)\] binaries (\d+) strings \{(.*)\}(.*)$")
HISTORY = re.compile(r"^    history (\d+): mtime (\S+) rule (\S) strings \{(.*)\}(.*)$")
GROUP = re.compile(r"^  group (\S+) \S+ mtime (\S+) rule (\S) entries (\d+)(.*)$")


def read(path: Path) -> dict[str, dict]:
    files: dict[str, dict] = {}
    cur: dict | None = None
    for line in path.read_text(encoding="utf-8").splitlines():
        if m := HEAD.match(line):
            cur = {"version": m[2], "settings": m[3], "deleted": int(m[4]), "entries": [], "groups": {}}
            files[m[1]] = cur
        elif cur is None:
            continue
        elif m := GROUP.match(line):
            cur["groups"][m[1]] = {"mtime": m[2], "rule": m[3]}
        elif m := ENTRY.match(line):
            cur["entries"].append({"title": m[1], "group": m[2], "mtime": m[3], "rule": m[4],
                                   "strings": m[7], "history": []})
        elif m := HISTORY.match(line):
            cur["entries"][-1]["history"].append({"mtime": m[2], "rule": m[3], "strings": m[4], "other": m[5]})
    return files


def t(s: str) -> datetime:
    return datetime.strptime(s, "%Y-%m-%dT%H:%M:%SZ")


def main() -> None:
    d = Path(sys.argv[1])
    droid = read(d / "droid-reread.txt")
    xc = read(d / "xc-reread-fast.txt") | read(d / "xc-reread-slow.txt")
    every = {**{"droid " + k: v for k, v in droid.items()}, **{"xc " + k: v for k, v in xc.items()}}
    print(f"files read: android {len(droid)}, keepassxc and keepass 2.47 {len(xc)}")
    tops: dict[str, int] = {}
    for k in xc:
        tops[k.split("\\")[0]] = tops.get(k.split("\\")[0], 0) + 1
    print("2 keepassxc and keepass 2.47 files by directory: " + ", ".join(f"{k} {v}" for k, v in sorted(tops.items())))

    # 4.1: the title and the 17-character protected password of E1 and E2 in every file.
    bad = [k for k, f in every.items() for e in f["entries"] if e["title"] in ("E1", "E2")
           and not ("Password=<protected 17>" in e["strings"] and f"Title={e['title']}" in e["strings"])]
    missing = [k for k, f in every.items() if {"E1", "E2"} - {e["title"] for e in f["entries"]}]
    print(f"4.1 files lacking E1 or E2: {len(missing)}; E1 or E2 without its title or password: {len(bad)}")

    # 4.7: zero deleted objects in every file.
    print(f"4.7 files with a deleted object: {sum(1 for f in every.values() if f['deleted'])}")

    # 3.6, 4.4: never more than one history item.
    print(f"3.6 entries with more than one history item: "
          f"{sum(1 for f in every.values() for e in f['entries'] if len(e['history']) > 1)}")

    # 1, 4.2: KeePassXC merge targets, E1 newer and P's, one history item with C's rule and time.
    targets = {k: f for k, f in xc.items() if k.startswith("C\\")}
    faithful = [k for k, f in targets.items() for e in f["entries"] if e["title"] == "E1"
                and e["rule"] == "P" and e["mtime"] == "2026-02-01T07:00:00Z" and len(e["history"]) == 1
                and e["history"][0]["rule"] == "C" and e["history"][0]["mtime"] == "2026-01-01T10:00:00Z"
                and e["history"][0]["strings"] == e["strings"]]
    print(f"1 KeePassXC merge targets: {len(targets)}; E1 P's at P's time with one item, C's rule, "
          f"C's time and the current strings: {len(faithful)}")

    # 3.5: KeePassXC adds empty Notes, URL and UserName to E1 and E2, and to the history item,
    # their times unchanged.
    xc_files = {k: f for k, f in xc.items() if not k.startswith(("base\\", "A247\\"))}
    added = [k for k, f in xc_files.items() for e in f["entries"] if e["title"] in ("E1", "E2")
             and all(s in e["strings"] for s in ("Notes=;", "URL=;", "UserName="))
             and all(all(s in h["strings"] for s in ("Notes=;", "URL=;", "UserName=")) for h in e["history"])]
    moved = [k for k, f in xc_files.items() for e in f["entries"]
             if (e["title"] == "E2" and e["mtime"] != "2026-01-01T00:00:00Z")
             or (e["title"] == "E1" and e["mtime"] not in ("2026-02-01T07:00:00Z", "2026-01-01T10:00:00Z"))]
    print(f"3.5 KeePassXC files {len(xc_files)}: E1 and E2 entries with the three empty fields, history "
          f"included: {len(added)} of {sum(1 for f in xc_files.values() for e in f['entries'] if e['title'] in ('E1', 'E2'))}; "
          f"with another time: {len(moved)}")

    # 3.4: from 2.4.2, KeePassXC's `_LAST_MODIFIED` is inside the history item too; 2.4.1 adds none.
    marked = {}
    for k, f in targets.items():
        release = k.split("\\")[1]
        for e in f["entries"]:
            if e["title"] == "E1":
                marked.setdefault(release, []).append(all("_LAST_MODIFIED" in h["other"] for h in e["history"]))
    print("3.4 KeePassXC merge targets whose E1 history item carries _LAST_MODIFIED, by release: "
          + ", ".join(f"{r} {sum(v)} of {len(v)}" for r, v in sorted(marked.items())))

    # 3.5: every program keeps E2's time and E1's, which is P's or, in the stale base, C's.
    moved_any = [k for k, f in every.items() for e in f["entries"]
                 if (e["title"] == "E2" and e["mtime"] != "2026-01-01T00:00:00Z")
                 or (e["title"] == "E1" and e["mtime"] not in ("2026-02-01T07:00:00Z", "2026-01-01T10:00:00Z"))]
    print(f"3.5 E1 or E2 at a time other than the bases gave them, every file: {len(moved_any)}")

    # 3.5: Keepass2Android's merges keep the entry only the open copy held.
    k2a = [k for k in droid if k.startswith("B\\k2a")]
    kept = [k for k in k2a if any(e["title"] == "droid-merge" for e in droid[k]["entries"])]
    print(f"3.5 Keepass2Android merge results holding droid-merge: {len(kept)} of {len(k2a)}")

    # 3.8: KeePass 2.47, KeePassXC and KeePassDX write back the SettingsChanged they were given.
    base_xc = {f["settings"] for k, f in xc.items() if k.startswith("base\\")}
    given = sorted({f["settings"] for k, f in xc.items() if not k.startswith("base\\")} - base_xc)
    base_droid = {f["settings"] for k, f in droid.items() if k.startswith(("base\\", "base2\\"))}
    dx = sorted({f["settings"] for k, f in droid.items() if "\\kpdx-" in k} - base_droid)
    print(f"3.8 SettingsChanged values in KeePassXC and KeePass 2.47 results that no base holds: {given}; "
          f"in KeePassDX results that no Android base holds: {dx}")

    # 3.5: KeePass 2.47 adds nothing to existing entries; the Android programs leave them unchanged
    # and give their own entry all five standard fields.
    plain = "Password=<protected 17>; Title="
    kp247 = [e["strings"] for k, f in xc.items() if k.startswith("A247\\") for e in f["entries"] if e["title"] in ("E1", "E2")]
    print(f"3.5 KeePass 2.47 E1 and E2 holding only title and password: "
          f"{sum(1 for s in kp247 if s.startswith(plain) and s.count('=') == 2)} of {len(kp247)}")
    dro = [e for f in droid.values() for e in f["entries"]]
    old = [e for e in dro if e["title"] in ("E1", "E2")]
    own = [e for e in dro if e["title"].startswith("droid-")]
    print(f"3.5 Android E1 and E2 holding only title and password: "
          f"{sum(1 for e in old if e['strings'].startswith(plain) and e['strings'].count('=') == 2)} of {len(old)}; "
          f"own entries with Notes, Password, Title, URL and UserName: "
          f"{sum(1 for e in own if all(s + '=' in e['strings'] for s in ('Notes', 'Password', 'Title', 'URL', 'UserName')))} of {len(own)}")

    # 3.5: KeePassDroid's own entry carries one history item, an empty entry about 15 s older.
    kpd = {k: f for k, f in droid.items() if k.startswith("A\\kpd-2.7\\")}
    ages = []
    for f in kpd.values():
        for e in f["entries"]:
            if e["title"] == "droid-added" and len(e["history"]) == 1 and e["history"][0]["strings"] == "":
                ages.append(int((t(e["mtime"]) - t(e["history"][0]["mtime"])).total_seconds()))
    print(f"3.5 KeePassDroid 2.7 saves: {len(kpd)}; own entry with one empty history item: {len(ages)}, "
          f"older by {sorted(ages)} s")

    # 1, 3.4: Android merges, E1 and its history item by program and cell.
    cells: dict[str, set] = {}
    for k, f in droid.items():
        parts = k.split("\\")
        if parts[0] != "B" or "." in parts[2] or k.endswith("-src.kdbx"):
            continue
        for e in f["entries"]:
            if e["title"] == "E1":
                h = e["history"][-1] if e["history"] else None
                shape = (e["mtime"][:10], e["rule"], h and h["mtime"][:10], h and h["rule"],
                         h is not None and h["strings"] == e["strings"])
                cells.setdefault(parts[1] + " " + re.sub(r"-r\d$", "", parts[2]), set()).add(shape)
    for program in ("k2a", "kpdx"):
        runs = [k for k in droid if k.startswith("B\\" + program) and "." not in k.split("\\")[2]
                and not k.split("\\")[2].startswith("keep") and not k.endswith("-src.kdbx")]
        print(f"1 {program} merge results counted, without the two runs set aside, the copies beside "
              f"them and the run that kept the file: {len(runs)}")
    for cell, shapes in sorted(cells.items()):
        print(f"   {cell}: " + "; ".join(f"E1 {a} {b}, history {c} {d}, same strings {e}" for a, b, c, d, e in sorted(shapes, key=str)))

    # 3.8: the files that hold no SettingsChanged read back as the moment of reading.
    late = sorted(k for k, f in droid.items() if f["settings"] > "2026-11-11")
    print(f"3.8 Android files whose SettingsChanged is later than the runs: {len(late)}: " + ", ".join(late))
    print(f"    their KDBX versions: {sorted({droid[k]['version'] for k in late})}")


main()
