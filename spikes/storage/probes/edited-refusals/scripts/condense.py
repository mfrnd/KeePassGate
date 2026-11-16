"""Folds the raw dumps of a run into the files the verdicts are read from.

    condense.py <raw-dir> <out-dir>

Every `*.jsonl` in <raw-dir> holds one line per cell, `{"cell": ..., "file": <dump>}`, as
`probe/Program.cs` writes them. Most cells hold the same records as others, so each distinct thing
is written once and referred to after:

    versions.jsonl  every distinct entry version: `mod`, `title`, `s`, `canon`, by `id`
    rows.jsonl      every distinct [entry label, group label, slot, location time, version id];
                    a row's number is its line, counted from 0
    frames.jsonl    every distinct pair of recycle bin and group list, by `id`, counted from 0
    states.jsonl    every distinct file: its frame, its deletion records and its rows in file order
    cells.tsv       each cell and the file it ended as

Nothing is dropped: `scripts/verdicts.py` rebuilds every dump exactly from these.
"""

import hashlib
import json
import pathlib
import sys


def text(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def ident(obj):
    return hashlib.sha256(text(obj).encode()).hexdigest()[:16]


def main(raw, out):
    versions, dumps, cells = {}, {}, {}
    for path in sorted(pathlib.Path(raw).glob("*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            row = json.loads(line)
            dump = row["file"]
            entries = []
            for e in dump["entries"]:
                v = {k: x for k, x in e.items() if k not in ("u", "g", "slot", "loc")}
                vid = ident(v)
                versions[vid] = v
                entries.append((e["u"], e["g"], e["slot"], e.get("loc"), vid))
            frame = text({"bin": dump["bin"], "groups": dump["groups"]})
            deleted = text(dump["deleted"])
            sid = ident([frame, deleted, entries])
            dumps[sid] = (frame, deleted, entries)
            if row["cell"] in cells:
                raise SystemExit("cell twice: " + row["cell"])
            cells[row["cell"]] = sid

    rows = sorted({e for _, _, entries in dumps.values() for e in entries}, key=lambda r: text(list(r)))
    row_no = {r: i for i, r in enumerate(rows)}
    frames = sorted({f for f, _, _ in dumps.values()})
    frame_no = {f: i for i, f in enumerate(frames)}

    out = pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)

    def write(name, lines):
        with open(out / name, "w", encoding="utf-8", newline="\n") as f:
            for line in lines:
                f.write(line + "\n")

    write("versions.jsonl", (text({"id": vid, **versions[vid]}) for vid in sorted(versions)))
    write("rows.jsonl", (text(list(r)) for r in rows))
    write("frames.jsonl", (text({"id": frame_no[f], **json.loads(f)}) for f in frames))
    write("states.jsonl", (text({"id": sid, "frame": frame_no[dumps[sid][0]], "deleted": json.loads(dumps[sid][1]),
                                 "rows": [row_no[e] for e in dumps[sid][2]]}) for sid in sorted(dumps)))
    write("cells.tsv", (cell + "\t" + cells[cell] for cell in sorted(cells)))
    print(f"{len(cells)} cells, {len(dumps)} distinct files, {len(rows)} distinct rows, "
          f"{len(frames)} distinct frames, {len(versions)} distinct entry versions")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
