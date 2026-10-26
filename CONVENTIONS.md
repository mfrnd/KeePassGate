# Conventions

The rules that hold on every branch of this repository, whatever a branch is being used for. They
are here rather than in any one branch's own guidance because a branch that holds a proof and a
branch that holds the product are still the same project, read by the same people and published
together.

**Nothing here depends on a language, a build system or a tool.** Those rules live with the thing
they configure.

## Visibility

**Written as public from the start.** Write every file as if it were already read by anybody,
because it will be: the repository is written to be published, and a history is published whole,
so a file pushed while the repository was private is still in it on the day it is not. That
mainly means: nothing about the author's actual database. No credentials, no host names, no
entry titles, no group layout, no subject identities, no examples lifted from a real `.kdbx`.
Examples and tests use invented names, obviously invented.

**A `.kdbx` is never committed**, on any branch, for any reason. Anything that needs one builds its
own, throwaway, somewhere temporary.

## Branches

**One step per branch, stacked, never merged**, each starting from the previous one's tip.

**Every commit on a branch has to stand on its own**, not merely the state the branch ends in. A fix
that lands later than the thing it fixes leaves the commits in between failing, and a reader who
checks one out gets that failure rather than the work.

**A spike gets its own branch from `master`, named `spikes/<topic>`**, and is never merged either. A
proof that lands on a branch carrying the product has to be deleted again later, and the deletion
then sits in the history of everything downstream. The branch that carries the product keeps what a
proof established, not the code that established it.

## Prose

- **No dashes as punctuation, anywhere.** Not em-dashes, not en-dashes, and not the ASCII
  substitutes `--` and ` - `. This covers prose, code comments, commit messages, documents and any
  string a user reads. When a dash feels wanted, rewrite the sentence. Hyphenated compounds
  (`fail-closed`, `top-down`) and command-line flags (`--noresult`) are unaffected. ASCII
  punctuation only otherwise.
- **American English**: authorize, behavior, recognize, analyze, license as both noun and verb.
  Names belonging to somebody else keep their own spelling: a property, a quoted button, a
  third-party option.
- **Backtick code references** in prose and commit messages alike: file paths, identifiers and
  literals, but not product names.
- **No second person, anywhere.** Not `you`, not `your`, not `yourself`. This covers prose, code
  comments, commit messages, documents and any string an operator reads. Name whoever is meant, or
  nobody: an imperative reaches the reader without pointing at them, and most sentences read better
  once the pronoun goes. A quotation keeps its own words, because a reported sentence, an example
  error message and an instinct being argued with are not the document addressing anybody.

## References

**A reference to a decision names the section that settles it.** Four documents settle things:
`DESIGN.md`, `CA.md`, `PAIRING.md` and `API.md`. Every section of each carries a number, and
everything else points at the number rather than at a title: code comments, the operator's pages,
`CLAUDE.md` and commit messages alike. The form is a section sign and three digits. One grep then
finds the paragraph that decides something and every place leaning on it, in one answer, which is
what a reference by title never gives. **The routing document is a fifth**,
`src/KeePassGate/Spec/openapi.yaml`, which settles what each route answers, and it carries a number
only where something cites it.

**The numbers are flat and permanent.** Each document allocates from its own hundred, `DESIGN.md`
from 100, `CA.md` from 200, `PAIRING.md` from 300, `API.md` from 400 and `openapi.yaml` from 500,
so a number says which document it is in before anything is looked up. A new section takes the
next number above the highest that document has used, wherever in the document it is inserted.
**A number is never renumbered and never reused.** Renumbering silently rewrites every reference
that was correct the day before, and reusing a number that a deleted section held sends an old
reference somewhere plausible and wrong, which is worse than sending it nowhere.

**A number is defined by sitting at the end of a heading and nowhere else**, so that what it names
is always a section and never a sentence about one. `openapi.yaml` has no headings, so there it sits
at the end of a comment line between keys, beside the operation, schema, parameter or response it
names. A `#` inside a description is part of the text a client reads, so a number is never written
there, and neither is a reference.

**Nothing else is anchored.** `CONVENTIONS.md`, `CLAUDE.md`, `TESTING.md`, `FINDINGS.md` and the
pages a reader goes through start to finish are read rather than cited, and numbering them would
put a handle on every paragraph in the repository for the sake of the few that are ever pointed at.
A document that is historical and frozen is not anchored either.

## Versions

**One version, for the plugin, PyKeePassGate and the routing document, moved together.** A client
and a gate meet in somebody's install, and one number is what lets anybody tell which halves belong
together, where three would each need an answer to which of the other two they work with. It is
written in one place, and everything else reads it or is held to it by a test, so a version moved in
one place and not another fails the gate rather than shipping.

**At 0.x, a minor version for anything that breaks either half, and a patch for a fix that breaks
nothing.** Breaking is a program or an operator that worked before having to change: a route, a
status or a body on the wire, what a certificate or an admission record carries, what the client
raises or returns, or what the operator has to do. A change that adds without breaking anything is a
minor version as well, because the halves move together and a client using the addition needs the
gate that serves it.

**A minor version releases both halves, and a patch releases only the half it fixes.** An `x.y.0`
publishes the plugin, with the routing document inside it, and PyKeePassGate together, and the
same major and minor version is the promise that a client and a gate work together. A patch moves
the one number, since it is written once, and publishes only the half whose files it changed: the
plugin for anything under `src/KeePassGate`, the client for anything under `src/pykeepassgate` or
`pyproject.toml`. A patch that changed neither has nothing to release. So a patch number may exist
on one side only, and the client at `0.1.2` beside the plugin at `0.1.1` is a pair, because both
are `0.1`. A full split, with a protocol version the gate serves, waits for 1.0 or a second client.
[`RELEASING.md`](RELEASING.md) is how a version is published.

**From the first release on, every change goes into [`CHANGELOG.md`](CHANGELOG.md)**, under the
version it will ship in and in the same commit as the change, as the design documents do. Until
then everything is the first version, and the changelog records as much. **A dependency's update
is the one exception**: Dependabot makes it and cannot write the changelog, so the pull request
that makes the version names every shipped dependency that moved, as
[`RELEASING.md`](RELEASING.md#a-release) has it.

## Terminology

**One thing has one name, everywhere.** Prose, identifiers, commit messages and the pages written
for whoever runs the result all use the same word for the same thing, because a reader who meets
two names for one thing spends the paragraph deciding whether they are the same thing. Where a
name is genuinely contested, the design documents settle it and hold the list; nothing else
introduces a synonym for something already named there.

A name belonging to somebody else keeps their spelling: a property on a library type, a quoted
button, a third-party option.

## Controls

- **Every control this plugin builds carries a `Name`**, on every window and every tab, whether or
  not anything automates it today. WinForms reports that name to UI automation as the
  `AutomationId`, so a control without one cannot be pointed at from outside the process at all,
  and the next person who needs to point at one has to change the product before they can write the
  test. A name costs a line and removes the question. They begin `Kpg`, so a control of ours is
  recognizable in a tree that also holds KeePass's own and any other plugin's.
- **`AccessibleName` is not a test hook, and using it as one does harm.** It replaces what a
  control reports as its name, so a button carrying one announces that instead of its caption, to a
  screen reader as much as to a test. Where the desktop tests point at a control they point by its
  `AutomationId`, which comes from `Name`, so a hook was never what it was for. Set it only where a
  control has no text that already names it, a box or a grid or a spin control, and then set it to a
  phrase somebody would want read aloud rather than to the control's name.
- **A tab page looked like the exception and is not.** Its accessible name was set to its own
  caption, and taking that off broke a unit test that asserted the property, which reads like the
  desktop tier needing it and is not the same claim at all. Measured by removing it and running the
  desktop tier, which was forty-seven tests on the day: all of them passed, because a tab already
  reports its caption as its name. The count is what that run had, not a standing number.

## Commits

- **One logical unit each**, imperative mood, subject starting with a capital letter, no
  `component:` prefix. Split file hunks and introduce intermediate deltas where a commit would
  otherwise claim something that is not true yet.
- **Each commit stands alone and says why**, not merely what. The history is read as if it were a
  pull request, by somebody who was not here.
- **A forward reference is marked wherever it is not a design document.** Describing what is to be
  built is what a design document is for, so `DESIGN.md`, `CA.md` and `PAIRING.md` may name what
  does not exist yet and say nothing about it. Anywhere else, a reference to something a later step
  brings carries `TODO(step N)` at the reference itself, naming the step that makes it true. A bare
  `TODO` names nobody and expires never, which is why the form is fixed and why `build.ps1` fails
  when a marker names a step the build order already marks done.
- **A step named in a sentence is a reference like any other.** "Step 14 is where the two are put
  together", in a comment describing a class, claims behavior nothing has yet; the shape of the
  sentence is what hides that, not the reference. One thing outside the design documents is exempt,
  and it is `CLAUDE.md`'s status section, because saying a step is next is already saying it has not
  happened. What the exemption does not cover is going on saying it afterwards: status prose
  describes the present, so the step that lands is the one that brings it up to date. A build order
  entry is the opposite and is left alone, being a log written at its own step's vantage.
- **A professional tone.** Plain technical language: no anthropomorphism, no rhetorical openers, no
  cuteness. A gate does not answer "civilly", a test does not "assume", and a subject does not open
  with "Say what". The subject states what the commit does and the body says why, both in the
  register a reviewer would use.
- **No `Claude-Session:` trailer.** It is a link only one person can open, into a session nobody
  else has, and this history is written for anybody to read. `Co-Authored-By` stays, because
  that one says something a reader can act on.
