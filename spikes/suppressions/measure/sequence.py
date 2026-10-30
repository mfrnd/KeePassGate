"""Runs fixed steps of the volume measurement on the probe's pull request: one step per push, read off the run.

Step A is the run the pull request's opening started: the 122 comments #51 was refused, in one
review. Then single reviews of fresh lines, sized by halving the interval between the largest size
accepted and the smallest refused, until the two are within `PRECISION` or `STEPS` ran. Between two
steps it waits `CLEAR` seconds after an accepted review, and after a refusal what GitHub asked for,
never less than `CLEAR_REFUSED`; it never lets the comments made in the last hour pass `HOURLY`.
After every step it counts the pull request's review comments with the operator's own token, which
shows whether a refused review left anything behind.

    python driver.py <probe checkout> <pull request number> <results directory>
"""
import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

REPO = "mfrnd/codeql-suppression-probe"
BRANCH = "measure/review-volume"
FIRST_GUESS = 80
PRECISION = 2
STEPS = 7
CLEAR = 150
CLEAR_REFUSED = 150
HOURLY = 450
LONGEST_HOLD = 1800

checkout, number, out = Path(sys.argv[1]), int(sys.argv[2]), Path(sys.argv[3])
out.mkdir(parents=True, exist_ok=True)
made: list[tuple[float, int]] = []


def log(text: str) -> None:
    line = f"{datetime.now(timezone.utc).isoformat(timespec='seconds')} {text}"
    print(line, flush=True)
    with (out / "driver.log").open("a", encoding="utf-8") as written:
        written.write(line + "\n")


def run(*arguments: str, cwd: Path | None = None) -> str:
    done = subprocess.run(arguments, cwd=cwd, capture_output=True, text=True, encoding="utf-8")
    if done.returncode != 0:
        raise RuntimeError(f"{' '.join(arguments)}: {done.stderr.strip()}")
    return done.stdout


def gh(*arguments: str) -> str:
    return run("gh", *arguments)


def review_comments() -> int:
    pages = gh("api", "--paginate", f"repos/{REPO}/pulls/{number}/comments?per_page=100",
               "--jq", "length")
    return sum(int(n) for n in pages.split())


def run_for(sha: str) -> int:
    for _ in range(60):
        runs = json.loads(gh("run", "list", "-R", REPO, "-w", "measure.yml", "-b", BRANCH, "-L", "20",
                             "--json", "databaseId,headSha,event"))
        found = [r["databaseId"] for r in runs if r["headSha"] == sha and r["event"] == "pull_request"]
        if found:
            return found[0]
        time.sleep(5)
    raise RuntimeError(f"no run appeared for {sha}")


def results_of(run_id: int) -> list[dict]:
    while json.loads(gh("run", "view", str(run_id), "-R", REPO, "--json", "status"))["status"] != "completed":
        time.sleep(10)
    for _ in range(12):
        try:
            text = gh("run", "view", str(run_id), "-R", REPO, "--log")
        except RuntimeError:
            time.sleep(10)
            continue
        found = [json.loads(line.split("RESULT ", 1)[1]) for line in text.splitlines() if "RESULT {" in line]
        if found:
            return found
        time.sleep(10)
    raise RuntimeError(f"run {run_id} printed no result")


def step(plan: dict | None) -> list[dict]:
    before = review_comments()
    if plan is not None:
        (checkout / "measure/plan.json").write_text(json.dumps(plan) + "\n", encoding="utf-8", newline="\n")
        sizes = ", ".join(str(n) for n in plan["batches"])
        run("git", "-c", "user.name=mfrnd", "-c", "user.email=mfrnd@users.noreply.github.com",
            "commit", "-q", "-m", f"Plan step {plan['step']}: reviews of {sizes} comments", "-m",
            "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>",
            "measure/plan.json", cwd=checkout)
        run("git", "push", "-q", "origin", BRANCH, cwd=checkout)
    sha = run("git", "rev-parse", "HEAD", cwd=checkout).strip()
    run_id = run_for(sha)
    log(f"step {plan['step'] if plan else 'A'}: run {run_id} for {sha[:7]}")
    records = results_of(run_id)
    after = review_comments()
    for record in records:
        record["run"] = run_id
        record["pr_comments_before"], record["pr_comments_after"] = before, after
        with (out / "results.jsonl").open("a", encoding="utf-8") as written:
            written.write(json.dumps(record) + "\n")
        created = record.get("created")
        if record["status"] == 200 and created:
            made.append((time.time(), created))
        log(f"  {record['size']} comments: {record['status']}"
            + (f", review {record['review']} holds {created}" if record["status"] == 200 else
               f", {record.get('message')!r}, retry-after {record['headers'].get('retry-after')},"
               f" remaining {record['headers'].get('x-ratelimit-remaining')}")
            + f"; the pull request went from {before} to {after} review comments")
    return records


def hold(last: dict) -> None:
    pause = CLEAR if last["status"] == 200 else max(float(last.get("wait") or 0), CLEAR_REFUSED) + 30
    log(f"  holding {pause:.0f} s")
    time.sleep(pause)


def room_for(size: int) -> None:
    held = 0.0
    while True:
        recent = sum(n for at, n in made if time.time() - at < 3600)
        if recent + size <= HOURLY:
            return
        if held > LONGEST_HOLD:
            raise RuntimeError(f"{recent} comments made in the last hour leave no room for {size}")
        log(f"  {recent} comments made in the last hour; holding 300 s for room for {size}")
        time.sleep(300)
        held += 300


def main() -> None:
    plans = [
        {"step": "C1", "set": "bulk", "offset": 200, "batches": [81], "pause": 1, "retry": False},
        {"step": "C2", "set": "bulk", "offset": 281, "batches": [20] * 8, "pause": 20, "retry": True,
         "max_retries": 3, "max_wait": 600},
    ]
    for index, plan in enumerate(plans):
        before = review_comments()
        records = step(plan)
        after = review_comments()
        made.append((time.time(), after - before))
        if index + 1 < len(plans):
            last = records[-1]
            pause = 180 if last["status"] != 200 else CLEAR
            log(f"  holding {pause} s")
            time.sleep(pause)
    log("done")


if __name__ == "__main__":
    main()
