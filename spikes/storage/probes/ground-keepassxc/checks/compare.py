"""Compares a fresh run in `out\\` with the record in `recorded\\`, file by file.

Written on this branch. Before comparing, it takes away from both sides only what differs between
any two runs of the same code, and prints for each file what it took away:

- `paths`: the branch root in front of a path, which the record had replaced already;
- `times in cli.log`: the start and end times in a `cli.log` header;
- `identifiers`: what the probe draws at random. An 8 digit identifier in a dump becomes the name
  of the group or entry it belongs to, a full one in a command's report becomes `{uuid}` or
  `[uuid]`, and an XML export's base64 identifiers become `<uuid>`; the `deleted` lines, which the
  dump sorts by identifier, are sorted again by name;
- `clock`: the date text of KeePassXC's `_LAST_MODIFIED` marker and the value of
  `KPXC_RANDOM_SLUG`, and every absolute time, as text or as an XML export's base64 seconds, that
  is not within two days of the invented T0 of 2026-01-01;
- `order of custom data items`: the order of the items in an XML export's custom data, which
  KeePassXC writes in no fixed order; they are sorted;
- `group flags`: the `search=` and `autotype=` fields of a dump's group line, taken from the fresh
  file only, and only where the recorded file has none: the record's `merge-fast`, `merge-slow` and
  `save-fast` were written by an earlier `Program.cs` whose dump did not print them yet.

Usage: `python -I -X utf8 compare.py <family directory>`.
"""
import base64
import difflib
import re
import sys
from datetime import datetime, timedelta
from pathlib import Path

T0 = datetime(2026, 1, 1)
PREFIX = re.compile(r"[A-Za-z]:\\[^\"'\s]*?\\(?=spikes\\storage\\probes\\ground-keepassxc\\|\.keepassxc\\)")
EXIT = re.compile(r"^(### exit -?\d+) \S+/\S+$")
GUID = re.compile(r"\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}")
GUID32 = re.compile(r"\[[0-9a-f]{32}\]")
XML_UUID = re.compile(r">[A-Za-z0-9+/]{22}==<")
XML_TIME = re.compile(r">([A-Za-z0-9+/]{11}=)<")
CD_BLOCK = re.compile(r"(<CustomData>)(.*?)(\s*</CustomData>)", re.S)
CD_ITEM = re.compile(r"\s*<Item>.*?</Item>", re.S)
MARKER = re.compile(r"_LAST_MODIFIED=[^@]*@")
XML_MARKER = re.compile(r"(<Key>(?:_LAST_MODIFIED|KPXC_RANDOM_SLUG)</Key>\s*<Value>)[^<]*(</Value>)")
STAMP = re.compile(r"\b(\d{4}-\d\d-\d\d)T\d\d:\d\d:\d\d(?:\.\d+)?Z")
G_LINE = re.compile(r"^G '(?P<name>[^']*)' u=(?P<u>[0-9a-f]{8})")
E_LINE = re.compile(r"^  E '(?P<name>[^']*)' u=(?P<u>[0-9a-f]{8})")
SHORT = re.compile(r"(?<=u=)[0-9a-f]{8}|(?<=prev=)[0-9a-f]{8}|(?<=^deleted )[0-9a-f]{8}"
                   r"|(?<=' )[0-9a-f]{8}(?= historyMax)|(?<=missing )[0-9a-f]{8}")
FLAGS = re.compile(r" search=\S+ autotype=\S+$")


def far_from_t0(m: re.Match) -> str:
    when = datetime.strptime(m.group(1), "%Y-%m-%d")
    return m.group(0) if abs((when - T0).days) <= 2 else "<now>"


def xml_time(m: re.Match) -> str:
    """A KDBX 4 time in an XML export: base64 of the seconds since 0001-01-01, little endian."""
    seconds = int.from_bytes(base64.b64decode(m.group(1)), "little")
    when = datetime(1, 1, 1) + timedelta(seconds=seconds)
    return m.group(0) if abs((when - T0).days) <= 2 else "><now><"


def sort_items(m: re.Match) -> str:
    items = CD_ITEM.findall(m.group(2))
    return m.group(1) + "".join(sorted(items)) + CD_ITEM.sub("", m.group(2)) + m.group(3)


def normalize(text: str, strip_flags: bool) -> tuple[list[str], set[str]]:
    taken: set[str] = set()
    text = text.replace("\r\n", "\n")
    # KeePassXC writes a dictionary of custom data in no fixed order (2.6.6 in an XML export).
    ordered = CD_BLOCK.sub(sort_items, text)
    if ordered != text:
        taken.add("order of custom data items")
        text = ordered
    # In an XML export the marker's key and value stand on lines of their own.
    marked, n = XML_MARKER.subn(r"\1<date>\2", text)
    if n and marked != text:
        taken.add("clock")
        text = marked
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    dump = bool(lines) and lines[0].startswith("# ") and " kdbx " in lines[0]
    names = {}
    if dump:
        for line in lines:
            m = G_LINE.match(line) or E_LINE.match(line)
            if m:
                names[m.group("u")] = ("G:" if line.startswith("G") else "E:") + m.group("name")
    result = []

    def sub(pattern: re.Pattern, repl, value: str, label: str) -> str:
        new, n = pattern.subn(repl, value)
        if n and new != value:
            taken.add(label)
        return new

    for line in lines:
        new = sub(PREFIX, "", line, "paths")
        new = sub(EXIT, r"\1 <time>", new, "times in cli.log")
        new = sub(GUID, "{uuid}", new, "identifiers")
        new = sub(GUID32, "[uuid]", new, "identifiers")
        new = sub(XML_UUID, "><uuid><", new, "identifiers")
        new = sub(XML_TIME, xml_time, new, "clock")
        new = sub(MARKER, "_LAST_MODIFIED=<date>@", new, "clock")
        new = sub(STAMP, far_from_t0, new, "clock")
        if dump:
            new = sub(SHORT, lambda m: names.get(m.group(0), "<uuid>"), new, "identifiers")
            if strip_flags and new.startswith("G '"):
                new = sub(FLAGS, "", new, "group flags")
        result.append(new)
    deleted = [i for i, line in enumerate(result) if line.startswith("deleted ")]
    for i, line in zip(deleted, sorted(result[i] for i in deleted)):
        result[i] = line
    return result, taken


def main() -> None:
    family = Path(sys.argv[1])
    rec, out = family / "recorded", family / "out"
    recorded = {p.relative_to(rec) for p in rec.rglob("*") if p.is_file()}
    fresh = {p.relative_to(out) for p in out.rglob("*") if p.is_file()}
    fresh -= {Path("compare.txt"), Path("claims.txt"), Path("claims-recorded.txt")}
    same = differs = 0
    for rel in sorted(recorded & fresh, key=str):
        r_text = (rec / rel).read_text(encoding="utf-8")
        f_text = (out / rel).read_text(encoding="utf-8")
        r_lines, r_taken = normalize(r_text, False)
        f_lines, f_taken = normalize(f_text, "search=" not in r_text)
        taken = ", ".join(sorted(r_taken | f_taken)) or "nothing"
        if r_lines == f_lines:
            same += 1
            print(f"same     {rel}  (taken away: {taken})")
        else:
            differs += 1
            print(f"DIFFERS  {rel}  (taken away: {taken})")
            for d in list(difflib.unified_diff(r_lines, f_lines, "recorded", "out", n=0, lineterm=""))[2:]:
                print("    " + d)
    for rel in sorted(recorded - fresh, key=str):
        print(f"only recorded  {rel}")
    for rel in sorted(fresh - recorded, key=str):
        print(f"only out       {rel}")
    print(f"{same} the same, {differs} different, {len(recorded - fresh)} only recorded, "
          f"{len(fresh - recorded)} only out")


if __name__ == "__main__":
    main()
