# The audit file

Every decision the rules make is written down, allowed or refused, and so is every certificate this
database issued that the gate turns away: which client asked, what it asked for, which group or
entry it was about, and what decided it. So is every pairing and renewal the operator answers, and
every client revoked on the Clients page or revoking itself. Nothing else in KeePassGate keeps that
record, and it is the one place where a node the rules refuse and a node that is not there are told
apart, because a client is told the same `404` for both.

**KeePass's own record of use counts only what a program changes.** KeePass keeps, for every entry,
when it was last accessed and how many times it has been used. A change the gate makes to an entry
moves both, and a read moves neither, so an entry a program reads and never changes looks unused in
KeePass however often it is read: this file is where its reads are.

## Where it is

By default, under this Windows account:

```text
%LOCALAPPDATA%\KeePassGate\audit.jsonl
```

which is usually `C:\Users\<account>\AppData\Local\KeePassGate\audit.jsonl`. The directory is made
the first time a decision is recorded.

**Not beside the database**, and not inside it. A `.kdbx` is often on a share or in a synced folder,
and a record of which program read which entry does not belong in either. Keeping it inside the
database would be worse again: a record of what a program reached, held in the thing it reached, is
a record that whoever compromised the database can edit, and it is a record nobody can read while
KeePass is locked, which is exactly when somebody wants to read it.

**To put it somewhere else**, set `KEEPASSGATE_AUDIT_FILE` to the full path of the file, for the
account KeePass runs as, before KeePass starts. The plugin writes the path it settled on into
[its own log](getting-started.md#the-plugins-log) as it loads, so a machine where the file is not
where it was expected can be checked rather than guessed at.

**It works under an append-only ACL.** An administrator who wants the file to grow and never be
rewritten can give the Windows account KeePass runs as the right to append to it and nothing else
but the two rights Windows asks for on every open, which `icacls` spells `(AD,S,RA)`; `(AD)` alone
refuses even an append. A file that can be renamed away is not append only, so removing and renaming
in its directory has to be taken away as well. The plugin opens the file asking for the right to
append and no other, so every decision is still written down. Two things change: the file can no
longer be set aside at eight megabytes, which the plugin's log says once while the file grows past
it, and the window asking which authority to keep cannot read the file, so it says `unknown` for
when each client last made a request.

**That ACL is something the plugin works under, not a defense it provides.** Anything running as
that account can do whatever the account can, and KeePassGate concedes code running as the user
throughout. What keeps the file append only is that the account cannot change the ACL, which means
somebody else owns the file, and arranging that is the administrator's.

## What a line says

One JSON object per line, so that a line is a record and whatever the operator already greps with
can read it.

| | |
| --- | --- |
| `at` | when it was decided, UTC, to the second |
| `database` | which database answered: the SHA-256 of its authority's certificate, in hex, whose first sixteen characters are the **Fingerprint** on the Certificate Authority page. Recreating the authority gives the same file a new one. `null` on the operator's revocation in a database serving no authority, which is one whose two copies each made one and nobody has yet chosen |
| `admissionKey` | which certificate the line is about, by the same admission key the Clients page revokes: the one that asked, or the one a revocation took away. `null` on a pairing, whose program holds no certificate yet |
| `clientKey` | which client that certificate belongs to, by its **client key**: the admission key of the certificate it paired with, which the KPG tab counts and tells clients apart by and which a renewal keeps. `null` where no admission record stands behind the line, which is a pairing the operator did not approve and a certificate the gate refuses |
| `operation` | what it asked for, by the name the routing table gives it, or `null` for the operator's own revocation, which no route asked for |
| `node` | which group or entry it was about, by UUID, or `null` if it never reached one |
| `decidedAt` | which node's rule answered, by UUID, or `null` if no rule did |
| `decision` | `permitted` or `refused` for the rules; `approved`, `refused`, `unanswered` or `revoked` for an admission |
| `reason` | a few words saying why |

**The two keys answer two questions.** Every line a program ever caused carries the same
`clientKey`, however often it renewed, so searching for that one value finds its whole history, and
two programs that chose the same name are told apart by it. `admissionKey` says which of its
certificates asked, which is what to look for after revoking one: a renewal changes it, and the
renewal's own line names the old certificate under `admissionKey` and the new one under `issued`.
The names are the ones `GET /gate/whoami` answers with, so a word means one thing in both places.

**The reference client says which keys are its own.** PyKeePassGate prints its admission key when
it pairs, renews or gives its certificate up, and `pykeepassgate identity` prints both keys at any
time without connecting. `pykeepassgate whoami` asks the gate for both. The admission key a program
prints when it pairs is its client key as well, for good.

**`permitted` means the rules allowed it, and nothing more.** The line is written as the rules
answer, before the body is checked and before anything is read or written, so a request can be
`permitted` here and still be answered with a refusal of its body, a conflict, `503 busy`,
`503 locked` or a failure. None of those gets a line of its own: what the request came to is what
the program was answered. Why a change was refused is in the plugin's log, a warning per refusal,
which is where to look when a program's writes keep coming back `busy`: some of the reasons, a
dialog left open or a database file changed on disk, clear only when somebody at the desk acts.

**A program asking what it may do at a group or an entry is a decision as well**, and written down
like any other: `permitted` when some profile it holds may do something there, `refused` when none
may.

**A locked database is not a decision, and gets no line.** A request that meets the database
locked, whether it was locked when the request arrived or KeePass locked it while the request was
still being read, is answered `503 locked` and written down nowhere, because no rule was asked. A
group or an entry that is simply not there while the database is open is still refused, and still
written down, with one more member, `requested`, naming the identifier the program asked for, so
the line says which entry a reference was following when the program was told only that there was
nothing there. It holds an identifier as the routes write one, 32 hexadecimal characters or `root`,
and is left out for anything else.

**A refusal that a removed profile would have allowed says so.** When the rules refuse a program
whose record still holds a profile this database no longer names, and that profile would have
allowed the request, `reason` is `profile removed`, `decidedAt` is the node whose rule would have
allowed it, and one more member, `removedProfiles`, lists the removed profiles that would have.
Every other line leaves that member out. A removed profile grants nothing, so naming it says why a
request failed without saying what anybody can reach.

**A revoked client is still written down.** A certificate this database issued and the gate
refuses, because its record has been taken away, it has expired, or it is not a client certificate,
gets a line for every operation it asks for: its key under `admissionKey`, `node` as `null`,
`refused`, and why. So a revoked program that keeps trying can be seen doing it. Its `clientKey` is
`null`: a revoked certificate's record has been emptied, one with no record never had one, and one
that has expired or is not a client certificate is refused before its record is read, so naming
the client would cost every such request a read of the database of its own. The revocation's own
line names both keys, so the client a revoked certificate belonged to is found there. A program
with no certificate, or with one from another database, names nobody this database issued and is
not written down.

`node` and `decidedAt` are usually different, and that is the point of both being there. Nearest
wins, so a refusal is normally decided somewhere above the node asked about: without the second
one, a line refusing an entry sends whoever reads it to an entry with no rule on it.

**No entry title, no field name, no value, no group path.** A log of titles would be an inventory
of the database sitting beside the database, readable by anybody who can read a file. What
identifies a node is its UUID, which means something to whoever holds the database and nothing to
anybody else.

**Nothing a caller sent reaches a line.** The reason is a phrase this plugin chose out of a fixed
set, so a client cannot write into the file that records what it did.

## Pairings, renewals and revocations

No rule decides these, and they are written down in the same file and the same form all the same:
what a program reached starts with how it got in. Their `decision` is the operator's word or the
act's, so `permitted` goes on meaning only that the rules allowed something, and `node` and
`decidedAt` are always `null`.

| | |
| --- | --- |
| A pairing | `pairRequest`, with no `admissionKey`, since the program has no certificate yet. `approved`, `refused` or `unanswered`, as the operator answered the dialog or left it to close itself. One more member, `invitation`, names the invitation it arrived on, as the KPG tab and the dialog show it, and an approved one adds `issued`: the admission key of the certificate it admitted, which every later line from that program names under `admissionKey` until it renews. That is its client key for good, so an approved pairing's `clientKey` is the same key; a refused or unanswered one admitted nobody and its `clientKey` is `null` |
| A renewal | `renewCertificate`, with the certificate that asked under `admissionKey` and its client under `clientKey`, which the renewal does not change. `approved`, with the new certificate's key under `issued`; `refused`, with a reason saying which answer the operator gave, once, in this session, until a later date or forever, or that an answer given before refused it without asking; or `unanswered` |
| A revocation | `revoked`, with the certificate it took away under `admissionKey` and the client it belonged to under `clientKey`. The operator's, with `operation` as `null`, is written when the settings dialog's **OK** puts it into the database, which is when it takes effect, and never on **Cancel**; its client key is read from the record just before **OK** empties it, and is `null` only if that record was already gone or could not be read. Its `database` is the authority served once **OK** is through, so a revocation pressed after recreating the authority in the same dialog names the new one. A program's revocation of itself, `revokeCertificate`, is written once the revocation is in the database |

**`approved` always names a client that was admitted.** An approval whose record did not reach the
database, or whose invitation had been spent or had run out by the time the operator answered,
admitted nobody: the program was told so, the plugin's log says why, and the audit has no line for
it. Nor is there a line when there was nobody to ask, with no screen to show the dialog on.

**Not everything that changes who is admitted is here yet.** Recreating or deleting the authority
takes every record away and is not written down, and neither is a change to the rules or to the
profiles. Those wait for a design of audit levels.

**Never a profile.** A pairing line does not say what the program asked for or what the operator
approved, and a renewal line does not say what it kept: which profiles a program holds is in its
record, and a second copy of that is what is worth stealing.

## How large it gets

At eight megabytes the file is set aside as `audit.jsonl.1` and a new one is started. One previous
file is kept, and the one before that is deleted. Anybody who needs more than the recent past is
shipping the lines somewhere else, which is what a line per decision is for.

**A client cannot roll the file away quickly.** Every request the rules decide about writes a line,
so a program sending requests as fast as it can would otherwise push everything before them out of
both files in a couple of minutes, including the lines recording what it did first. Each client is
recorded up to a thousand times at once and ten times a second after that, counted by its client
key so that renewing does not start it again, and a request past that is answered more slowly
rather than refused or left out. A certificate the gate refuses has no client key and is counted by
its own admission key. A single client flooding the gate therefore
needs well over an hour to push a line out of both files, and every line it writes on the way names
it. Its renewals and its revocation of itself count against the same allowance, since a renewal
refused by an earlier answer is answered at once and as often as it is asked. A pairing waits on
the operator, and the operator's revocation on nobody, so neither is held back.

## When it is not being written

A failed write costs the line and never the request. A full disk, a file somebody opened
exclusively, a directory that has gone away: each of those is reported in the plugin's log when it
begins and the gate carries on answering, because a plugin that refused every request while it
could not write a log would be a denial of service anybody could arrange. When a line can be written
again the log says so, with how many decisions went unrecorded in between, and a later failure is
reported in its turn.

Not every failure lasts. A program that opens the file for reading without letting anybody else
write to it stops every line for exactly as long as it holds the file, and most programs that
follow a log leave writing open to others.

The same is true of having nowhere to write at all. If the account has no local application data
directory the plugin can find, there is no default path, nothing is recorded, and everything else
works exactly as before. Naming a file with `KEEPASSGATE_AUDIT_FILE` is what makes the audit
independent of that, and the plugin's log says which of the two happened as it loads.
