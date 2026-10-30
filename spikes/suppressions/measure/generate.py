"""Writes the files the volume measurement comments on, and the comments themselves.

`real` is the review KeePassGate's pull request #51 was refused: the same 122 bodies, in the same
order, over 31 files holding them as that pull request's files did. `bulk` is fresh lines for the
reviews after it, with the same bodies over again, so that every comment has a line of its own.

    python generate.py <verdict job log> <probe checkout>
"""
import hashlib
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

ROW = re.compile(r"\| `([^`]+)` \| ([^|]+) \| `([^`]+):(\d+)` \| (.*) \|$")
BULK_FILES = 8
BULK_LINES = 100


def body(rule: str, severity: str, reason: str, key: str) -> str:
    # The body `tools/suppression_review.py` writes, with its mark.
    return (f"**Suppresses `{rule}`** ({severity}): {reason}\n\n"
            "Resolve this thread once the suppression is accepted.\n\n"
            f"<!-- code-scanning: {key} -->")


def main() -> None:
    log, checkout = Path(sys.argv[1]), Path(sys.argv[2])
    rows = []
    for line in log.read_text(encoding="utf-8").splitlines():
        found = ROW.search(line)
        if found:
            rows.append((found[1], found[2].strip(), found[3], int(found[4]), found[5]))
    if len(rows) != 122:
        raise SystemExit(f"expected the 122 rows of #51's summary, found {len(rows)}")

    files: "OrderedDict[str, str]" = OrderedDict()
    for _, _, path, _, _ in rows:
        files.setdefault(path, "measure/real/" + path.replace("/", "__") + ".txt")
    longest = {path: max(row[3] for row in rows if row[2] == path) + 1 for path in files}

    real = []
    for rule, severity, path, covered, reason in rows:
        line = max(covered - 1, 1)
        digest = hashlib.sha256(f"{path}:{line}:{rule}".encode()).hexdigest()[:12]
        real.append({"path": files[path], "line": line,
                     "body": body(rule, severity, reason, f"added:{path}:{rule}:{digest}")})

    bulk = []
    for index in range(BULK_FILES * BULK_LINES):
        rule, severity, _, _, reason = rows[index % len(rows)]
        name = f"measure/bulk/b{index // BULK_LINES + 1}.txt"
        bulk.append({"path": name, "line": index % BULK_LINES + 1,
                     "body": body(rule, severity, reason, f"bulk:{index}")})

    for path, name in files.items():
        target = checkout / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("".join(f"{path}, line {n}\n" for n in range(1, longest[path] + 1)),
                          encoding="utf-8", newline="\n")
    for number in range(1, BULK_FILES + 1):
        target = checkout / f"measure/bulk/b{number}.txt"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("".join(f"bulk {number}, line {n}\n" for n in range(1, BULK_LINES + 1)),
                          encoding="utf-8", newline="\n")
    (checkout / "measure/comments.json").write_text(
        json.dumps({"real": real, "bulk": bulk}, indent=1) + "\n", encoding="utf-8", newline="\n")
    size = len(json.dumps({"commit_id": "0" * 40, "event": "COMMENT", "comments": real}))
    print(f"{len(files)} files, {len(real)} real comments ({size} bytes as one request), "
          f"{len(bulk)} bulk slots")


if __name__ == "__main__":
    main()
