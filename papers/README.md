# Papers

The working papers of the evaluation of where KeePassGate should keep its data: the ground reports,
each option's design with its critiques and its digest, and the first draft of the evaluation with
the check of it. They are kept as they were written, and so are left out of the spelling and
Markdown checks, which they were not written against. The two changes are mechanical. First,
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
