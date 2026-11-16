"""One harness over one jobs file, as run.ps1's store step runs them.

    uv run --no-project python -I -X utf8 spikes\\storage\\probes\\source-harness\\batch.py k2a|kpdx <jobs> [<parts>]

KeePassDX's batch is split into <parts> jobs files (default 4) run in that many JVMs at once, each
started again after a job that did not end (common.run_kpdx); Keepass2Android's runs whole. Prints
every FAILED line and each run's last line.
"""
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common as c  # noqa: E402


def split(jobs, parts):
    chunks = [[] for _ in range(parts)]
    cur, n = None, 0
    for line in Path(jobs).read_text(encoding="utf-8").splitlines():
        if line.startswith("job "):
            cur = chunks[n % parts]
            n += 1
        if cur is not None:
            cur.append(line)
    files = []
    for i, ch in enumerate(chunks):
        f = Path(jobs).with_name("%s-%d.txt" % (Path(jobs).stem, i))
        f.write_text("\n".join(ch) + "\n", encoding="utf-8")
        files.append(f)
    return files


def main():
    program, jobs = sys.argv[1], sys.argv[2]
    if program == "k2a":
        results = [c.run([c.K2A, "batch", jobs])]
    else:
        parts = int(sys.argv[3]) if len(sys.argv) > 3 else 4
        with ThreadPoolExecutor(parts) as ex:
            results = list(ex.map(lambda f: c.run_kpdx(["batch", f]), split(jobs, parts)))
    for r in results:
        for line in (r.stdout + r.stderr).splitlines():
            if line.startswith("FAILED") or line.startswith(program + ":"):
                print(line)


if __name__ == "__main__":
    main()
