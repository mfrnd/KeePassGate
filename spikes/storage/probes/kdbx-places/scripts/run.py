r"""Runs every measurement of `kdbx-places`, as `run.ps1` hands it the programs.

    run.py --work <dir> --out <dir> --kpl <Kpl.exe> --kp247 <dir of KeePass 2.47> --kw <kdbxweb dir>
           --xc <release>=<keepassxc-cli.exe> ... [--steps bases,saves,merges,key_format,cap]

Writes, under --out: `bases\<set>.txt`, the dump of every input; `saves\<program>\<set>-<file>.txt`,
what a save did to each place; `merges\<program>\<set>\<pair>-<direction>.txt`, what a merge
result holds of its two copies; `key_format\...` and `cap\...` the same way; and `log.txt`, one line
per operation with its exit, what it printed and its seconds. Comparisons list only what moved: a
path not named kept its value, and the header line counts the paths compared.
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import places  # noqa: E402

SETS = {"a41": ("argon", "41"), "e41": ("aes", "41"), "a40": ("argon", "40"), "e40": ("aes", "40"),
        "s41": ("slow", "41")}
RISKY = ("hfield", "ifield", "eoh")
METHODS = ("Synchronize", "OverwriteIfNewer", "OverwriteExisting", "KeepExisting", "CreateNewUuids")
PAIRS = (("diff", "AB"), ("diff", "BA"), ("tie", "AB"), ("tie", "BA"))
# The dump path that holds each capacity place's value, as `Kpl.exe cap` writes it.
_G = r"X/Root/Group\[root\]/Group\[places\]"
CAP_PATHS = {
    "H.pcd": r"H\.pcd\[KPG_shared\]", "H.kdf": r"H\.kdf\[KPG_kdf\]", "M.name": r"X/Meta/DatabaseName",
    "M.desc": r"X/Meta/DatabaseDescription", "M.user": r"X/Meta/DefaultUserName",
    "M.cd": r"X/Meta/CustomData/Item\[kpg-shared\]/Value",
    "M.icon.name": r"X/Meta/CustomIcons/Icon\[icon-used\]/Name",
    "M.icon.data": r"X/Meta/CustomIcons/Icon\[icon-unused\]/Data",
    "G.notes": _G + r"/Notes", "G.at-sequence": _G + r"/DefaultAutoTypeSequence",
    "G.cd": _G + r"/CustomData/Item\[kpg-shared\]/Value", "G.tags": _G + r"/Tags",
    "E.override-url": _G + r"/Entry\[entry\]/OverrideURL", "E.tags": _G + r"/Entry\[entry\]/Tags",
    "E.at-window": _G + r"/Entry\[entry\]/AutoType/Association\[0\]/Window",
    "E.cd": _G + r"/Entry\[entry\]/CustomData/Item\[kpg-shared\]/Value",
}
PK_WITH = ["uv", "run", "--no-project", "--with", "pykeepass==4.2.0", "python", "-I", "-X", "utf8"]


class Runner:
    def __init__(self, a):
        self.a = a
        self.work = Path(a.work)
        self.out = Path(a.out)
        self.xc = dict(x.split("=", 1) for x in a.xc)
        self.log = (self.out / "log.txt").open("a", encoding="utf-8", newline="\n")
        self.dumps: dict[str, dict[str, str]] = {}

    def run(self, label: str, cmd: list[str], stdin: str | None = None, cwd: Path | None = None) -> tuple[int, str]:
        t = time.monotonic()
        p = subprocess.run(cmd, input=stdin, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", cwd=cwd,
                           creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        secs = time.monotonic() - t
        printed = " | ".join(line.strip() for line in (p.stdout + p.stderr).splitlines() if line.strip())[:400]
        # What a program printed is logged only when it failed: on success it is a prompt and a
        # message in the language of the machine, which adds nothing the comparisons lack.
        self.log.write(f"{label}\texit {p.returncode}\t{secs:.1f} s\t{printed if p.returncode else ''}\n")
        self.log.flush()
        return p.returncode, printed

    def dump(self, f: Path) -> dict[str, str]:
        key = str(f)
        if key not in self.dumps:
            try:
                self.dumps[key] = dict(places.Dumper(places.open_file(key)).run())
            except Exception as e:  # noqa: BLE001 - an unreadable result is a result
                self.dumps[key] = {"UNREADABLE": f"{type(e).__name__}: {e}"}
        return self.dumps[key]

    def write(self, rel: str, lines: list[str]) -> None:
        p = self.out / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    # --- bases ------------------------------------------------------------------------------
    def bases(self) -> None:
        for s, (kdf, ver) in SETS.items():
            raw = self.work / "raw" / s
            if raw.exists():
                shutil.rmtree(raw)
            self.run(f"make {s}", [self.a.kpl, "make", str(raw), kdf, ver])
            dst = self.work / "bases" / s
            dst.mkdir(parents=True, exist_ok=True)
            for name in ("base", "A-diff", "B-diff", "A-tie", "B-tie"):
                variant = name.split("-")[0] if name != "base" else "base"
                places.inject(str(raw / f"{name}.kdbx"), str(dst / f"{name}.kdbx"), variant,
                              None, tie=name == "B-tie")
            if s in ("a41", "e41"):
                for r in RISKY:
                    places.inject(str(raw / "base.kdbx"), str(dst / f"risky-{r}.kdbx"), "base", r)
            lines = []
            for f in sorted(dst.glob("*.kdbx")):
                lines.append(f"== {f.name}")
                lines += [f"{p}\t{v}" for p, v in self.dump(f).items()]
            self.write(f"bases/{s}.txt", lines)

    def inputs(self, s: str) -> list[Path]:
        d = self.work / "bases" / s
        return [d / "base.kdbx"] + sorted(d.glob("risky-*.kdbx"))

    # --- saves ------------------------------------------------------------------------------
    def programs(self):
        """Each program's save operations: name, and a function from (in, out) to a command."""
        kw = [self.a.kw]
        ps = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(HERE / "kp247.ps1"),
              "-KeePass", self.a.kp247]
        ops = [
            ("keepass-2.62-resave", lambda i, o: ([self.a.kpl, "resave", i, o], None)),
            ("keepass-2.62-edit", lambda i, o: ([self.a.kpl, "edit", i, o], None)),
            ("keepass-2.47-resave", lambda i, o: (ps + ["-In", i, "-Out", o], None)),
            ("keepass-2.47-edit", lambda i, o: (ps + ["-In", i, "-Out", o, "-Edit"], None)),
            ("kdbxweb-resave", lambda i, o: (["node", str(HERE / "kw.js")] + kw + ["resave", i, o], None)),
            ("kdbxweb-edit", lambda i, o: (["node", str(HERE / "kw.js")] + kw + ["edit", i, o], None)),
            ("pykeepass-resave", lambda i, o: (PK_WITH + [str(HERE / "pk.py"), "resave", i, o], None)),
            ("pykeepass-edit", lambda i, o: (PK_WITH + [str(HERE / "pk.py"), "edit", i, o], None)),
        ]
        for rel, cli in sorted(self.xc.items(), key=lambda kv: [int(x) for x in kv[0].split(".")]):
            ops.append((f"keepassxc-{rel}-add", lambda i, o, cli=cli: ("copy", [cli, "add", "-u", "xc-user", o, "xc-added"])))
        return ops

    def saves(self) -> None:
        for s in ("a41", "e41", "a40", "e40"):
            for f in self.inputs(s):
                before = self.dump(f)
                for name, make in self.programs():
                    o = self.work / "saves" / name / s / f.name
                    o.parent.mkdir(parents=True, exist_ok=True)
                    if o.exists():
                        o.unlink()
                    cmd, extra = make(str(f), str(o))
                    if cmd == "copy":
                        shutil.copyfile(f, o)
                        code, printed = self.run(f"{name} {s}/{f.name}", extra, stdin="probe-only\n")
                    else:
                        code, printed = self.run(f"{name} {s}/{f.name}", cmd)
                    rel = f"saves/{name}/{s}-{f.stem}.txt"
                    if code != 0 or not o.exists():
                        self.write(rel, [f"REFUSED\texit {code}: {printed}"])
                        continue
                    self.write(rel, self.save_lines(before, self.dump(o)))

    def save_lines(self, before: dict[str, str], after: dict[str, str]) -> list[str]:
        lines = places.save_cmp(before, after)
        moved = [ln for ln in lines if not ln.endswith("\tkept")]
        return [f"# {len(set(before) | set(after))} paths, {len(lines) - len(moved)} kept"] + moved

    # --- merges -----------------------------------------------------------------------------
    def merges(self) -> None:
        jobs = []
        for s in ("a41", "e41", "a40", "e40"):
            for m in METHODS:
                jobs.append((f"keepass-2.62-{m}", s))
        for s in ("a41", "s41"):
            for rel in self.xc:
                jobs.append((f"keepassxc-{rel}", s))
        for s in ("a41", "e41"):
            jobs.append(("kdbxweb", s))
        for prog, s in jobs:
            d = self.work / "bases" / s
            for pair, direction in PAIRS:
                first, second = (("A", "B") if direction == "AB" else ("B", "A"))
                op, ar = d / f"{first}-{pair}.kdbx", d / f"{second}-{pair}.kdbx"
                o = self.work / "merges" / prog / s / f"{pair}-{direction}.kdbx"
                o.parent.mkdir(parents=True, exist_ok=True)
                label = f"merge {prog} {s} {pair}-{direction}"
                if prog.startswith("keepass-2.62-"):
                    code, printed = self.run(label, [self.a.kpl, "merge", str(op), str(ar), str(o), prog.rsplit("-", 1)[1]])
                elif prog.startswith("keepassxc-"):
                    shutil.copyfile(op, o)
                    arr = o.parent / f"{pair}-{direction}-arriving.kdbx"
                    shutil.copyfile(ar, arr)
                    code, printed = self.run(label, [self.xc[prog.split("-", 1)[1]], "merge", "-s", str(o), str(arr)],
                                          stdin="probe-only\n")
                else:
                    code, printed = self.run(label, ["node", str(HERE / "kw.js"), self.a.kw, "merge", str(op), str(ar), str(o)])
                rel = f"merges/{prog}/{s}/{pair}-{direction}.txt"
                if code != 0 or not o.exists():
                    self.write(rel, [f"REFUSED\texit {code}: {printed}"])
                    continue
                lines = places.cell_cmp(self.dump(op), self.dump(ar), self.dump(o))
                moved = [ln for ln in lines if not ln.endswith("\tsame")]
                self.write(rel, [f"# {len(lines)} paths, {len(lines) - len(moved)} the same in both and kept"] + moved)

    # --- key and format changes --------------------------------------------------------------
    def key_format(self) -> None:
        for s in ("a41", "e41"):
            f = self.work / "bases" / s / "base.kdbx"
            before = self.dump(f)
            for op in ("rekey", "kdf", "save-as", "down40"):
                o = self.work / "key_format" / s / f"{op}.kdbx"
                o.parent.mkdir(parents=True, exist_ok=True)
                code, printed = self.run(f"keepass-2.62-{op} {s}", [self.a.kpl, op, str(f), str(o)])
                rel = f"key_format/{s}-{op}.txt"
                if code != 0:
                    self.write(rel, [f"REFUSED\texit {code}: {printed}"])
                    continue
                self.write(rel, self.save_lines(before, self.dump(o)))

    # --- capacity ---------------------------------------------------------------------------
    def cap(self) -> None:
        place_names = self.a.cap_places.split(",")
        for place in place_names:
            for size in (1024, 65536, 1048576):
                d = self.work / "cap" / place
                d.mkdir(parents=True, exist_ok=True)
                f = d / f"{size}.kdbx"
                code, printed = self.run(f"cap {place} {size}", [self.a.kpl, "cap", str(f), place, str(size), "argon"])
                if code != 0:
                    self.write(f"cap/{place}-{size}.txt", [f"NOT MADE\t{printed}"])
                    continue
                before = self.dump(f)
                lines = [f"# {place} {size} chars, file {f.stat().st_size} bytes"]
                for name, make in self.programs():
                    if name.endswith("-edit"):
                        continue
                    o = self.work / "cap-out" / name / place / f"{size}.kdbx"
                    o.parent.mkdir(parents=True, exist_ok=True)
                    if o.exists():
                        o.unlink()
                    cmd, extra = make(str(f), str(o))
                    t = time.monotonic()
                    if cmd == "copy":
                        shutil.copyfile(f, o)
                        code, printed = self.run(f"cap {name} {place} {size}", extra, stdin="probe-only\n")
                    else:
                        code, printed = self.run(f"cap {name} {place} {size}", cmd)
                    secs = time.monotonic() - t
                    if code != 0 or not o.exists():
                        lines.append(f"{name}\tREFUSED ({printed[:160]})\t{secs:.1f} s")
                        continue
                    after = self.dump(o)
                    rx = re.compile(CAP_PATHS[place])
                    mine = [p for p in before if rx.fullmatch(p)]
                    if not mine:
                        what = "no such path in the base"
                    elif all(after.get(p) == before[p] for p in mine):
                        what = "kept"
                    elif all(p not in after for p in mine):
                        what = "dropped"
                    else:
                        what = "rewritten (to " + " / ".join(str(after.get(p)) for p in mine)[:160] + ")"
                    lines.append(f"{name}\t{what}\t{secs:.1f} s")
                self.write(f"cap/{place}-{size}.txt", lines)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--work", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--kpl", required=True)
    ap.add_argument("--kp247", required=True)
    ap.add_argument("--kw", required=True)
    ap.add_argument("--xc", action="append", default=[])
    ap.add_argument("--steps", default="bases,saves,merges,key_format")
    ap.add_argument("--cap-places", default="")
    a = ap.parse_args()
    r = Runner(a)
    for step in a.steps.split(","):
        t = time.monotonic()
        getattr(r, step)()
        print(f"{step}: {time.monotonic() - t:.1f} s", flush=True)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    main()
