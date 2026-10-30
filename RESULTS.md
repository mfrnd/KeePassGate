# What the probe settled

Which comments CodeQL takes for a suppression, and what GitHub's code scanning does with a finding a
comment suppresses: on a pull request, on the default branch, under a ruleset, and through
`advanced-security/dismiss-alerts`. Code scanning was measured on 2026-10-29 and 2026-10-30 in
`mfrnd/codeql-suppression-probe`, a throwaway public repository, by `github/codeql-action` v4.39.1
running CodeQL 2.27.3, with the ruleset in
[`spikes/suppressions/ruleset.json`](spikes/suppressions/ruleset.json) on its default branch. The
comments were measured there, and then with the CodeQL 2.27.3 bundle on the development machine. The
pull request, alert and analysis numbers below are that repository's, which was kept only until they
were recorded here, and [`README.md`](README.md) describes how to make another.

| Question | Answer |
| --- | --- |
| Which comment suppresses a finding? | `codeql[rule-id]` on a line of its own directly above it, or `lgtm[rule-id]` on the flagged line. **One rule to a bracket**: two markers in one comment suppress two rules, and a list in one bracket suppresses nothing |
| Does code scanning honor the suppression? | **No.** CodeQL marks the result suppressed in the SARIF, and code scanning makes an alert of it all the same, so a suppressed finding new in a pull request blocks the merge under the ruleset until its alert is dismissed |
| Does an alert survive an edit near its line? | It survives a comment line added or removed above it. An edit to the flagged line or to a line after it makes a new alert, so a dismissal has to be made again on every run |
| Is `advanced-security/dismiss-alerts` the answer? | **No.** It dismisses the default branch's alerts from a pull request, leaves the alerts new in a pull request alone, and did not open an alert again when its suppression went |
| What keeps a pull request free of them? | Uploading its SARIF less what the code suppresses, so that a suppressed finding makes no alert |
| What keeps the default branch's list true? | Uploading everything, dismissing each alert the code suppresses with the comment's reason, and opening again every such dismissal the code no longer backs |
| Which CodeQL runs? | The highest version GitHub enables by default on the day, unless the workflow pins one; `tools: linked` runs the bundle the pinned action ships |
| Verdict | **Filter on a pull request, dismiss on the default branch, and fail on any finding nothing suppresses in the whole SARIF**, since a pull request's own upload holds only the lines it changed |

## 1. What ran, in order

The probe's history is not on this branch, only the tree it ended with, so each step is described
here by what it changed. A case is named for its method, `C1` to `C16` in `src/Probe/Cases.cs` and
`p1` to `p7` in `probe_py/cases.py`. The comment above a case describes it as it was first written,
and a later step may have changed the case since: `C1` lost its suppression in #7, and `C2` gained
one.

| Step | What it changed | What it measured |
| --- | --- | --- |
| the first two pushes to `main` | added `C1` to `C11` and `p1` to `p5`, under a first `codeql.yml` that let the action upload the analysis, printed what the SARIF marks suppressed, and ended with `advanced-security/dismiss-alerts` | which comments suppress, section 2 |
| #1, never merged | added `C12`, a new finding under a suppression, and `C13`, a new finding under none | a suppressed finding new in a pull request, section 3 |
| #2 | added the review job; added `C14` and `p6`, changed the line under `C1`'s suppression and took away the line under `C7`'s; added `C15` while the job's token could only read, then gave the job its write back; added `old.yml` and `dependabot.yml` | the review job, section 8, and a pull request's alerts, section 3 |
| #3 and #4, Dependabot's, never merged | moved `actions/upload-artifact` and `actions/checkout` to newer releases | what a Dependabot pull request's token holds, section 8 |
| #5 | took `C8`'s suppression away and put one above `C9`, whose finding `main` had open | a removed suppression, section 3, and `dismiss-alerts` run from a pull request, section 5 |
| #6 | replaced `dismiss-alerts` with `tools/filter_sarif.py` and `upload-sarif` | filtering before the upload, section 6 |
| #7, as first opened | took `C1`'s suppression away, put one above `C2`, whose finding `main` had open, and added `p7`, a suppressed security finding | the filter against each, section 6 |
| #8 | had `main` upload the whole SARIF and run `tools/sync_dismissals.py`, and had the filter drop a security finding like any other | dismissing on `main`, section 7 |
| #7, finished after #8 | changed `C14`'s flagged line; changed `C15`'s to one CodeQL does not report, its right operand being a local rather than a call, which leaves its comment over no finding; added `C16`, a new finding whose reason runs past 280 characters; then merged | dismissing on `main`, section 7, and fingerprints, section 4 |

## 2. Which comments suppress

### In code scanning

Read from the SARIF the first two runs on `main` kept, in which CodeQL marks each result it takes for
suppressed with a suppression of kind `inSource`. The C# findings are `cs/useless-assignment-to-local`,
a warning, and `cs/non-short-circuit`, an error; the Python ones are `py/unused-local-variable`.

| Case | The comment | Suppressed |
| --- | --- | --- |
| `C1`, `p1` | `codeql[rule-id]` on a line of its own, directly above the flagged line | **yes** |
| `C3` | the same, with a blank line between | no |
| `C4` | the same, after the code on the line above | no, and nothing on its own line either |
| `C5`, `p3` | `lgtm[rule-id]` after the code on the flagged line | **yes** |
| `C6`, `C11`, `p5` | two rule ids in one bracket, with a space after the comma and without | no |
| `C7`, `p4` | a rule id other than the one found | no |
| `C8` | `codeql[rule-id]` above an error, as `C1` is above a warning | **yes** |
| `C10` | above a statement over two lines, whose finding starts on the first | **yes** |

The Python cases first assigned to a variable named `unused`, which `py/unused-local-variable` does
not report, and the first Python analysis found nothing at all; they assign to `value` since.

### On the command line

Measured on 2026-10-29 with the bundle `codeql-bundle-v2.27.3` for Windows, which carries CLI 2.27.3
and `codeql/csharp-queries` 1.10.2, the C# extracted with build mode `none`. Each case has the shape
of four lines on KeePassGate's `master`, all in tests, that carry two rules on one line: an empty
`catch (Exception)`, which `cs/catch-of-all-exceptions` and `cs/empty-catch-block` both report on
the catch line, and in Python an `except BaseException:` holding only `pass`, which
`py/catch-base-exception` and `py/empty-except` both report. The cases differ only in the comment.

| Case | The comment, above the catch line unless placed otherwise | `cs/catch-of-all-exceptions` | `cs/empty-catch-block` |
| --- | --- | --- | --- |
| `K0` | none | found | found |
| `K1` | `// codeql[cs/catch-of-all-exceptions] measured: one marker` | **suppressed** | found |
| `K2` | `// codeql[cs/catch-of-all-exceptions] codeql[cs/empty-catch-block] measured: two markers, one reason` | **suppressed** | **suppressed** |
| `K3` | `// codeql[cs/catch-of-all-exceptions] measured: first reason; codeql[cs/empty-catch-block] measured: second reason` | **suppressed** | **suppressed** |
| `K7` | as `K3`, with no `;` between the two | **suppressed** | **suppressed** |
| `K4` | `// codeql[cs/catch-of-all-exceptions] measured: stacked, upper` on the line above `// codeql[cs/empty-catch-block] measured: stacked, lower` | found | **suppressed** |
| `K5` | `// codeql[cs/catch-of-all-exceptions] measured: above`, and `// lgtm[cs/empty-catch-block] measured: on the line` after the code on the catch line | **suppressed** | **suppressed** |
| `K6` | `// codeql[cs/catch-of-all-exceptions, cs/empty-catch-block] measured: a list` | found | found |
| `K8` | `// lgtm[cs/empty-catch-block] measured: lgtm alone above` | found | **suppressed** |
| `K9` | `/* codeql[cs/catch-of-all-exceptions] codeql[cs/empty-catch-block] measured: block comment */` | **suppressed** | **suppressed** |

| Case | The comment, above the `except` line | `py/catch-base-exception` | `py/empty-except` |
| --- | --- | --- | --- |
| `P0` | none | found | found |
| `P1` | `# codeql[py/catch-base-exception] codeql[py/empty-except] measured: two markers` | **suppressed** | **suppressed** |
| `P2` | `# codeql[py/catch-base-exception, py/empty-except] measured: a list` | found | found |
| `P3` | `# codeql[py/catch-base-exception] measured: stacked, upper` on the line above `# codeql[py/empty-except] measured: stacked, lower` | found | **suppressed** |

So:

- **Two `codeql[...]` markers in one comment suppress both rules on the next line**, in C# and
  Python alike, with one reason after both or a reason after each, with or without a `;` between.
- **A list inside one marker suppresses nothing**, in both languages.
- **Of two marker comments stacked, only the lower one counts**, since the upper one covers the
  comment line under it.
- **`codeql[a]` above and `lgtm[b]` after the code on the flagged line suppress both.**
- **`lgtm[id]` alone on a line covers the next line as well as its own.**
- **C# counts a `/* ... */` on one line as well as `//`**, and two markers in one worked.
- **A database made fresh has no notion of a new finding**, and every case was marked alike, so a
  finding new in a pull request is suppressed the same way: what is new is code scanning's
  comparison, made after the filter of section 6 has run.

The bundle came from the action's own release, 667 MiB, with a `.checksum.txt` asset beside it:

```powershell
gh release download codeql-bundle-v2.27.3 -R github/codeql-action -p codeql-bundle-win64.tar.gz
```

The longest path inside it is 193 characters and Windows keeps `LongPathsEnabled` at 0 unless it is
changed, so it was extracted under a short drive made with `subst`. The Python extractor demands the
`py` launcher unless it is given `-O python.python_executable_name=python` with a Python from `uv`
on `PATH`.

### The vectors

Measured on 2026-10-30 by `tools/SuppressionVectors.ps1`, a generator written for KeePassGate's step
32 and uncommitted at the time: 44 cases holding 51 findings, analyzed by CodeQL 2.27.3 with
`security-and-quality` and the alert-suppression query, each finding written to
`tools/SuppressionVectors.json` as suppressed or not. Beyond the rows above:

- **`///` and a `/* */` on one line suppress; a `/* */` over several lines does not.**
- **A Python `noqa` with no codes suppresses every rule on its own line**, and only there, bare or
  followed by words. `noqa: F841` does not, and a `noqa` alone above a line covers nothing below it.
- **`lgtm` in the middle of a sentence does not suppress; after a `;` it does.**
- **A marker counts only inside a comment.** A fake one inside a C# verbatim string or a Python
  triple-quoted string suppresses nothing, and a real one after a raw string, an interpolated string
  with nested strings, a character literal holding a quote, or strings holding `#` does.
- **The keyword is read in any case, and the rule id is not.** An upper-case rule id, as in
  `codeql[CS/USELESS-ASSIGNMENT-TO-LOCAL]`, suppresses nothing, in C# and Python alike.
- **Spaces inside the brackets are trimmed**: `codeql[ cs/x ]` suppresses.

The last two correct what was first assumed. A lexer written by hand in PowerShell for step 32
agreed with CodeQL on 48 of the 51 findings, and its three misses were the rule id's case and the
trimmed spaces, not its lexing.

### From the query source

Read in `shared/util/codeql/util/suppression/AlertSuppression.qll` at the `codeql-cli/v2.27.3` tag
of `github/codeql`, and not run:

- every `codeql[...]` match in a comment is an annotation, rewritten to `lgtm[...]`;
- a `codeql` comment counts only when nothing precedes it on its line;
- the `codeql` and `lgtm` keywords are matched under `(?i)`, so in any case;
- a bare `lgtm` at the start of a comment covers every rule on its line.

What the CLI then makes of an annotation is closed source, which is why the rows above were measured
rather than read.

## 3. A suppressed finding on a pull request

Measured on #1, #2 and #5, under the first workflow, which uploaded every result as CodeQL wrote it.

- **A pull request's analysis is filtered to the lines it changed.** #5 changed only comments, and
  its uploaded analyses held no result at all, `results_count` 0, while the SARIF its run kept
  through `output:` held every one.
- **A suppressed finding new in a pull request is still a new alert**: `C12` on #1 and the new
  findings of #2, each marked suppressed in the SARIF, each an alert with a comment from code
  scanning on its line. The CodeQL check failed, and the ruleset blocked the merge.
- **Dismissing the alert through the API unblocks the merge**: the check was evaluated again within
  about 45 seconds. Opening the alert again blocked it again.
- **Code scanning's own comments on a pull request are review threads**, and a dismissal resolves
  them.
- **A removed suppression never shows on the pull request.** #5 took `C8`'s away, and since the
  flagged line itself did not change, its result was filtered out with everything else the pull
  request did not touch. After the merge it surfaced on `main` as a new open alert, section 4.
- **The check failed on an error.** `C12` is `cs/non-short-circuit`, an error. For a new warning the
  check's conclusion is success, and only the ruleset's `alerts_threshold` blocks one, section 6.

## 4. Fingerprints

Code scanning follows an alert from one analysis to the next by a fingerprint of the code at its
location, so how far an edit reaches decides whether a finding keeps its alert, and with it any
dismissal.

- **An edit to the flagged line, or to a line after it, makes a new alert**: the old one goes to
  fixed and a new open one appears. `C14`'s flagged line changed in #7 and its alert went from #32
  to #44, and `p6`'s went from #29 to #43 when #7 added `p7` after it. The edit need not be inside
  the method: the comment #5 put above `C9` sits below `C8`'s flagged line, which is why `C8`'s
  finding came back on `main` as a new alert.
- **A comment line added or removed above the flagged line keeps the alert**: `C1` kept #31 when #7
  took its suppression away, and `C2` kept #6 when #7 put one above it.
- **So a dismissal is never made once and for all.** It has to be applied again on every run, to
  whatever alert the finding has become. Alerts dismissed on a pull request stay dismissed after the
  merge.

This corrects what the first measurements recorded, that a change on any line near a finding makes a
new alert, an insertion after the method included. The lines after the flagged one do; a comment line
above it does not, so a suppression can be put above a finding or taken away without moving its
alert.

## 5. `advanced-security/dismiss-alerts` v2.0.3

The first workflow ended with `advanced-security/dismiss-alerts@a18f986bdb40edba0dd7a74382c15d4a3d50a1c8`,
v2.0.3, given the analysis's `sarif-id` and the SARIF CodeQL wrote, from the first push to `main`
until #6 took it out.

- **It dismisses alerts from a pull request for the whole repository.** Run on a pull request that
  had not merged, it dismissed alert 2, `main`'s alert for `C9`'s finding.
- **It leaves the alerts new in a pull request alone**, though the code suppresses them.
- **It did not open an alert again when its suppression was removed**: the alert stayed dismissed.
- It runs on Node 20, which is deprecated.

A dismissing step of KeePassGate's own would therefore have to dismiss, on a pull request, only the
alerts with no instance on the default branch; open again every alert carrying its own mark whose
result is no longer suppressed; and match an alert to a result only after processing, on the run's
own commit. Filtering before the upload, section 6, needs none of it.

## 6. Filtering before the upload

Measured on #6 and on #7 as first opened. `analyze` runs with `upload: failure-only` and an `output:`
directory, `tools/filter_sarif.py` drops every result CodeQL marked `inSource`, `upload-sarif`
uploads what is left under the same category with `wait-for-processing`, and the last step reads
`/code-scanning/sarifs/{id}` for its `processing_status` and every analysis it lists for its
`error`. The `codeql.yml` on this branch still runs that way on a pull request.

- **Nothing is dismissed through the API.**
- **A suppressed finding new in the pull request makes no alert**, so the pull request passes.
- **A suppression added above a finding `main` has open leaves `main`'s alert open until the
  merge**, by construction: `C2` on #7.
- **A suppressed security finding was kept, and the job failed**: `p7` on #7, whose CodeQL check
  read "1 new alert including 1 high severity security vulnerability". The first filter kept one on
  purpose, since a security finding is to be fixed rather than suppressed; #8 had it drop one like
  any other, section 7.
- **`upload-sarif` filters a pull request's upload to the lines it changed**, as the action's own
  upload does.
- **After the switch, the alerts dismissed earlier on `main` showed as fixed**, their results being
  no longer uploaded.
- **The alert-suppression query comes from the bundle the run used**, `codeql/csharp-queries` 1.10.2
  beside CLI 2.27.3, and not from the registry, so the two cannot drift apart.
- **A removed suppression is still not on the pull request**, `C1` on #7, **but the SARIF the run
  kept holds the uncovered result**, so a check of the whole SARIF for a result nothing suppresses
  shows it where code scanning does not.
- **The CodeQL check's conclusion is success for a new warning.** Only the ruleset's
  `alerts_threshold` blocks one.

**An analysis can end in error under a green job.** One of the probe's analyses was recorded as
"Unknown Error", with no results, while the job that uploaded it succeeded, so a job that uploads
has to read every analysis's `error` and fail on one, which is what the last step does.

## 7. Dismissing on the default branch

Measured on #8 and on #7 as finished. A pull request keeps the filter. A push to `main` uploads the
whole SARIF instead, and `tools/sync_dismissals.py` then dismisses every open alert whose result the
code suppresses, as `won't fix` by `github-actions[bot]`, with a comment of `Suppressed in code:`
followed by the reason the code's comment gives, and opens again every dismissal of its own that the
analysis no longer reports as suppressed.

- **It works as intended.** After #7 merged, `main`'s dismissed alerts were exactly the ten
  suppressions in the code that suppress something: `C2`, `C5`, `C9`, `C10`, `C14`, `C16`, `p1`,
  `p3`, `p6` and `p7`.
- **A dismissed alert whose finding disappears stays `dismissed`**, with only `fixed_at` set, so
  without a cleanup the list fills with stale entries. Opening such an alert again settles it as
  `fixed` at once.
- **Opening again is needed, not only a safety net.** #7 took `C1`'s suppression away while its
  finding kept its fingerprint, and alert #31 stayed dismissed until the step opened it again. It
  was open when the probe ended.
- **GitHub refuses a dismissal comment over 280 characters**, with "Invalid request" and 422,
  rather than cutting it, so the step cuts the comment to 280 itself: `C16`.
- **A new alert on `main` stayed open about 6 seconds** before the step dismissed it.
- **A suppressed security finding passed the pull request and was dismissed on `main`** with its
  severity shown: `p7`, `py/insecure-protocol`, high.
- **The pull request's own check read "No new alerts in code changed by this pull request"** on #7,
  with three suppressions added, `C2`'s, `C16`'s and `p7`'s, and one suppressed line changed,
  `C14`'s.

## 8. The review job

Measured on #2. `suppressions.yml` runs `tools/suppressions.py`, which lists every suppression a
pull request adds or changes against its base, and posts each one it has not posted before as a
comment in one `COMMENT` review.

- **`GITHUB_TOKEN` with `pull-requests: write` posts the review**, on context lines inside a hunk as
  well as on changed ones.
- **A mark in each comment's body keeps a later run from posting it again.**
- **With `pull-requests: read` the post is refused** with 403, "Resource not accessible by
  integration", and the job fails, as it was written to.
- **"Require conversation resolution" blocks the merge until every thread is resolved.**
- **A Dependabot pull request's token holds `PullRequests: write` and `SecurityEvents: write`**, on
  #3 and #4.
- **A fork's pull request could not be measured**, for want of a second account. GitHub documents
  its token as read only, and the run with `pull-requests: read` stands in for it.
- **`lgtm` in a comment's prose is not a suppression, and the prototype took it for one.** It put
  `# p6: an lgtm suppression added by a pull request.`, the comment naming `p6`, to review as a
  suppression of its own line. CodeQL takes no `lgtm` in the middle of a sentence, section 2.

## 9. Which CodeQL runs

Read in `src/feature-flags.ts` of `github/codeql-action` at v4.39.1, and not run.

- **With no `tools:` input, `init` asks GitHub which version to use.** It reads the
  `default_codeql_version_*_enabled` feature flags from the API and takes the highest version they
  enable, so a new CodeQL can arrive with no commit in the repository. The probe's workflows carry
  none, and ran 2.27.3.
- **`tools: linked` runs the bundle the pinned action commit ships with**, so the version moves only
  when the action does.

KeePassGate's own workflow had no `tools:` input when this was read.

## 10. Read rather than run

Searched on 2026-10-29 for a widely adopted way to make code scanning honor a suppression comment.
There is none.

- **Code scanning ignores a SARIF's `suppressions`.** The maintainers of `github/codeql-action`
  confirmed it in its issue #1230, in 2023 and again in 2024, and pointed to
  `advanced-security/dismiss-alerts`, which section 5 found unfit. Section 3 measured the same thing.
- **`advanced-security/filter-sarif`**, with 35 stars, filters by path and rule only.
- **Other projects write their own**, and draugr's #1451 proposes the design KeePassGate took.
