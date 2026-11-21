# Papers

The working papers of the evaluation of where KeePassGate should keep its data: the ground reports,
each option's design with its critiques and its digest, the first draft of the evaluation with
the check of it, and the reports of the measurements made after that draft. They are kept as they
were written, and so are left out of the spelling and Markdown checks, which they were not written
against. The two changes are mechanical. First,
`tools/Copy-Papers.ps1` replaced the paths of the machine they were written on and counts what it
replaced, and in the two later Android reports, `ground/measured-overwrite.md` and
`ground/measured-k2a-synchronize.md`, the paths into their evidence by the places that evidence has
on this branch. Second, it replaced, by a table of exact phrases per paper, the wording that made
the programs covered look like a person's preference: whose phone, programs, setup or database,
whose requirement, idea, question or ruling, and instructions quoted as given. A program is
compatible when measurement against the stated criteria shows it, so the papers name the programs
in scope, a phone, an arrangement that is nobody's, an existing database, the survival requirement,
the project's decision or an idea by its name, and quote no instruction; "the operator" as the
person who runs the plugin, approves a pairing, answers a dialog or edits a rule stays, and so does
a ruling the product's own documents record. They cite each other by their old names:
`option-<key>.md` is `options/<key>/design.md`, `critique-<key>-<lens>.md` is
`options/<key>/critique-<lens>.md`, `digest-<key>.md` is `options/<key>/digest.md`, and
`EVALUATION.md` here is `draft/EVALUATION-draft.md`. The two later Android reports also cite
`EVALUATION-final.md`, the evaluation as it stood when they were measured, which is not among these
papers. The governed documents are `RESULTS.md`, `README.md` and `EVALUATION.md` at the root of the
branch.

**A paper is not corrected where a probe run again disagrees with it.** `RESULTS.md` lists every
sentence that came out wider than the output it quotes, and the family's `RUN.md` under
`spikes/storage/` has the number beside the record. Where a paper and `RESULTS.md` differ,
`RESULTS.md` holds.

**The other projects' sources a paper cites are not on the branch**:
`spikes\storage\probes\ground-keepass\src\`, `ground-keepassxc\src\` and `ground-android\src\`.
The `RUN.md` of `ground-keepass`, of `ground-keepassxc` and of `spikes\storage\android` names the
release, tag or commit each was read at and how to fetch it into a directory that is not
committed. The three Android measurements, `measured-android.md`, `measured-overwrite.md` and
`measured-k2a-synchronize.md`, and the probe `ground-android.md` quotes are in
`spikes\storage\android\`, not under `probes\`.

**The measurements made after the first draft** are in `ground/` beside the others, each with its
reproduction on this branch. These reports were written with no path of the machine in them and
none of the wording above, so `tools/Copy-Papers.ps1` copied them unchanged. Where a report names
`desktop-evidence\` or `android-evidence\` beside it, the `RUN.md` of its reproduction maps that
evidence to its place here.

| Paper | Reproduction | Its record |
| --- | --- | --- |
| `ground/measured-rollback-rule-models.md` | `spikes\storage\probes\rollback-rule-models\` | `recorded\` there |
| `ground/measured-desktop-windows.md` | `spikes\storage\probes\desktop-windows\` | `recorded\` there, the report's `desktop-evidence\` |
| `ground/measured-store-as-a-whole.md` | `spikes\storage\probes\store-as-a-whole\` | `recorded\` there |
| `ground/measured-kdbx-places.md` | `spikes\storage\probes\kdbx-places\` | `recorded\` there |
| `ground/measured-source-harness.md` | `spikes\storage\probes\source-harness\` | `recorded\` there |
| `ground/measured-keepassdx.md` | `spikes\storage\android\`, `RUN.md` section 15 | `recorded\keepassdx\` there |
| `ground/measured-edited-refusals.md` | `spikes\storage\probes\edited-refusals\` | `recorded\` there |
| `ground/measured-hide-and-lock.md` | `spikes\storage\probes\desktop-hide-and-lock\` | `recorded\` there, the report's `desktop-evidence\hide-and-lock\` |
| `ground/measured-k2a-background.md` | `spikes\storage\android\`, `RUN.md` section 16 | `recorded\k2a-background\` there |
| `ground/measured-decided-store.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-Decided` | `recorded-decided\` there |
| `ground/measured-two-readings.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-Rewrite` and with `-Copies` | `recorded-rewrite\` and `recorded-copies\` there |
| `ground/measured-copies-extended.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-Reading` and each of `nothing`, `profile`, `verb`, `cap` and `cap-items` | `recorded-reading-<name>\` there, a directory for each |
| `ground/measured-rules-per-profile.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-Rows` | `recorded-rows\` there |
| `ground/measured-rules-owed-forms.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-Owed` | `recorded-owed\` there |
| `ground/measured-rules-cut-parts.md` | `spikes\storage\probes\store-as-a-whole\`, `run-cut.ps1` | `recorded-cut\` there |
| `ground/measured-rules-across-levels.md` | `spikes\storage\probes\store-as-a-whole\`, run with `-S34` | `recorded-s34\` there |
| `ground/measured-rule-waits-for-save.md` | not on this branch: measured in a real KeePass on the branch `measure/rules-wait-for-save` | not on this branch |
| `ground/measured-lost-settled.md` | `spikes\storage\probes\store-as-a-whole\`, `model\lost_runs.py all` and `compare_lost.py` | `recorded-lost\` there, with its vectors in `recorded-lost\vectors\` |

The two desktop families show windows and the Android records come from an emulator, so neither
runs again from the branch without those; each `RUN.md` states what did.
