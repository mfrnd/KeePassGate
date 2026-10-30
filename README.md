# KeePassGate: what code scanning does with a CodeQL suppression comment

**Measurements, kept for reference and never merged.** KeePassGate answers each CodeQL finding where
it is found: it is fixed, or suppressed by a `codeql[rule-id]` comment above it that gives the
reason. Which comments CodeQL takes for a suppression, and what GitHub's code scanning then does with
a suppressed finding on a pull request, on the default branch and under a ruleset, are questions
about CodeQL and GitHub rather than about the plugin, and they were answered by running both: in a
throwaway public repository, and with the CodeQL command line.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. What the probe settled is in [`RESULTS.md`](RESULTS.md), with every pull request it ran
listed in order.

## Running it

**The probe is a repository of its own**, because what it measures is what GitHub does with an
upload, a pull request and a merge, and none of that happens to a directory inside another
repository. `spikes/suppressions` is that repository's tree as its last pull request left it, and
`spikes/suppressions/ruleset.json` is the ruleset its default branch carried. The workflows are kept
under `spikes/suppressions/.github`, where GitHub runs none of them.

- `.github/workflows/codeql.yml` analyzes the C# and the Python with the `security-and-quality`
  queries and each language's alert-suppression query, keeps the SARIF as an artifact, and uploads
  it.
- `.github/workflows/suppressions.yml` puts every suppression a pull request adds or changes to
  review.
- `.github/workflows/old.yml` does nothing that matters. It pins an old release of an action, so that
  Dependabot, which `.github/dependabot.yml` turns on, opens a pull request of its own.
- `src/Probe/Cases.cs` and `probe_py/cases.py` hold one case per method or function, each named in
  the comment above it.
- `tools/filter_sarif.py`, `tools/sync_dismissals.py` and `tools/suppressions.py` are the prototypes
  those workflows run.

Copy the tree to the root of a throwaway public repository, where code scanning and rulesets need no
paid plan, push it as `main`, and give `main` the ruleset:

```powershell
gh api -X POST repos/<owner>/<repository>/rulesets --input spikes\suppressions\ruleset.json
```

It requires a pull request before every merge, with every review thread resolved and merge commits
only, and code scanning results from CodeQL with both thresholds at `all`. Every measurement is then
a pull request, and the run on `main` after it merges. `RESULTS.md` lists them in the order they
ran, with what each changed, since the tree here is only the state the last one left. Nothing is
built or analyzed on the machine that pushes: the runner builds the C#, and every result is read
from GitHub's API or from the SARIF a run keeps.

## Reading a re-run

Pull request, alert and analysis numbers belong to the repository that made them, so a re-run is
compared on what happened to each case rather than on its numbers: whether its finding was marked
suppressed, made an alert, kept that alert across an edit, or held up a merge. The workflows carry no
`tools:` input, so a re-run is analyzed by whichever CodeQL GitHub enables by default that day, and
each SARIF names the version it was made with under `runs[].tool.driver.semanticVersion`. A
difference is a finding: record it in `RESULTS.md` beside what it contradicts.
