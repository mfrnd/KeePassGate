# The audit file

Every decision the rules make is written down, allowed or refused, and so is every certificate this
database issued that the gate turns away: which client asked, what it asked for, which group or
entry it was about, and what decided it. Nothing else in KeePassGate
keeps that record, and it is the one place where a node the rules refuse and a node that is not
there are told apart, because a client is told the same `404` for both.

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
| `database` | which database answered: the SHA-256 of its authority's certificate, in hex, whose first sixteen characters are the **Fingerprint** on the Certificate Authority page. Recreating the authority gives the same file a new one |
| `client` | which client asked, by the same admission key the Clients page revokes |
| `operation` | what it asked for, by the name the routing table gives it |
| `node` | which group or entry it was about, by UUID, or `null` if it never reached one |
| `decidedAt` | which node's rule answered, by UUID, or `null` if no rule did |
| `decision` | `permitted` or `refused` |
| `reason` | a few words saying why |

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
written down.

**A refusal that a removed profile would have allowed says so.** When the rules refuse a program
whose record still holds a profile this database no longer names, and that profile would have
allowed the request, `reason` is `profile removed`, `decidedAt` is the node whose rule would have
allowed it, and one more member, `removedProfiles`, lists the removed profiles that would have.
Every other line leaves that member out. A removed profile grants nothing, so naming it says why a
request failed without saying what anybody can reach.

**A revoked client is still written down.** A certificate this database issued and the gate
refuses, because its record has been taken away, it has expired, or it is not a client certificate,
gets a line for every operation it asks for: its key under `client`, `node` as `null`, `refused`,
and why. So a revoked program that keeps trying can be seen doing it. A program with no
certificate, or with one from another database, names nobody this database issued and is not
written down.

`node` and `decidedAt` are usually different, and that is the point of both being there. Nearest
wins, so a refusal is normally decided somewhere above the node asked about: without the second
one, a line refusing an entry sends whoever reads it to an entry with no rule on it.

**No entry title, no field name, no value, no group path.** A log of titles would be an inventory
of the database sitting beside the database, readable by anybody who can read a file. What
identifies a node is its UUID, which means something to whoever holds the database and nothing to
anybody else.

**Nothing a caller sent reaches a line.** The reason is a phrase this plugin chose out of a fixed
set, so a client cannot write into the file that records what it did.

## How large it gets

At eight megabytes the file is set aside as `audit.jsonl.1` and a new one is started. One previous
file is kept, and the one before that is deleted. Anybody who needs more than the recent past is
shipping the lines somewhere else, which is what a line per decision is for.

**A client cannot roll the file away quickly.** Every request the rules decide about writes a line,
so a program sending requests as fast as it can would otherwise push everything before them out of
both files in a couple of minutes, including the lines recording what it did first. Each client is
recorded up to a thousand times at once and ten times a second after that, and a request past that
is answered more slowly rather than refused or left out. A single client flooding the gate therefore
needs well over an hour to push a line out of both files, and every line it writes on the way names
it.

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
