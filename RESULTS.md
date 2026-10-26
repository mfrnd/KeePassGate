# Can a pykeepass program be pointed at KeePassGate?

**Not as a `PyKeePass` object. Yes as a facade, for 65 of the 122 public members of the four classes
a program holds, 24 of them on `PyKeePass` itself.** The distinction is the whole answer, and it is
worth stating in that order because the first half is a design decision and the second half is a
feature somebody could build tomorrow.

Measured against **pykeepass 4.2.0**, with `spikes/pykeepass/` holding both measurements.

**Remeasured on 2026-10-25, against the gate as of KeePassGate 0.1.0, and the first measurement was
wrong in three ways.** It classified `PyKeePass` alone, while a program spends most of its time on
the `Entry`, `Group` and `Attachment` objects it is handed, which is where most of what is missing
turned out to be. It counted `delete_entry` and `delete_group` as served, when the gate's deletes
send a node to the recycle bin and refuse a group that is not empty, while pykeepass's remove for
good, and with them everything inside; and it counted `trash_entry` and `trash_group` as missing,
when they are exactly what the gate's deletes do. And it refused `deref`, which step 24 has since
built on the client's side under the caller's own rules. The numbers below are the second
measurement.

## The two things being compared

| | |
| --- | --- |
| **pykeepass** | opens a `.kdbx` **file** with its master password and hands back a `PyKeePass`: groups, entries, `find_entries`, `save`. |
| **PyKeePassGate** | asks a **running KeePass** over the gate's mutual-TLS API, holds no file and no master password, and is refused anything the operator did not approve. |

They are not alternatives. One decrypts a file sitting on the disk; the other asks a KeePass that
already has one open.

## Why not as a `PyKeePass` object

A `PyKeePass` **is** a decrypted `.kdbx`, and three measured facts make producing one a trap rather
than an integration:

- **It is constructed from a file and a password, and from nothing else.** There is no constructor
  that takes entries, a connection, or anything a gate could hand over.
- **`save()` writes to the file the object was opened from.** A user who edited an entry on a
  synthesized database and saved it would have changed a copy, silently, while the real database
  carried on unchanged and the ACL never saw the write. That is not an incompatibility, it is a way
  to lose work and believe it was kept.
- **A synthesized database is protected by whatever password the synthesizer chose**, because the
  real one is exactly what the gate exists not to hand over. What looks like protection would be a
  secret invented on this side.

There is a fourth reason that is not about `pykeepass` at all. **A client is admitted to some
profiles, not to a file.** An object shaped like a whole database invites every operation the ACL
exists to refuse, and the refusal would arrive as an exception from a save rather than as a decision
at the gate.

## What a facade can do today

| | `PyKeePass` | `Entry` | `Group` | `Attachment` | what it means |
| --- | --- | --- | --- | --- | --- |
| **served** | 24 | 25 | 11 | 5 | PyKeePassGate can implement it now, over routes the gate already serves, with the meaning pykeepass gives it |
| **served-differently** | 2 | 1 | 1 | 0 | the gate has the operation, but it means something else, so a facade must not offer it under that name |
| **needs-routes** | 0 | 7 | 2 | 0 | nothing in the design forbids it; the gate does not serve it yet |
| **refused** | 30 | 7 | 6 | 1 | it needs the file, the key, the XML document, or an operation the design refuses |
| **public members** | 56 | 40 | 20 | 6 | |

`spikes/pykeepass/surface.json` has the per member table and the reason for each.

**The served ones are the ones a program actually uses.** `entries`, `groups`, `root_group`,
`add_entry`, `add_group`, `trash_entry`, `trash_group`, the whole `find_*` family, and on an entry
its standard fields, its custom fields with their protected flag, its attachments, its times and
its expiry. A program that reads entries, searches them, and adds or trashes a few would move
across with its call sites unchanged.

Two costs come with them, and both are properties of an API rather than defects. A listing carries
uuid, group, title and url, so `find_entries_by_title` and `find_entries_by_url` are one request,
while `find_entries_by_username`, `by_password`, `by_notes` and `by_string` are a listing plus one
read per candidate. And `attachments` across a database is one request per entry, because
attachments are addressed through the entry that holds them.

## The 4 served under a name that means something else

`PyKeePass.delete_entry` and `Entry.delete` remove an entry for good, while the gate's delete sends
it to the recycle bin when the database keeps one; `PyKeePass.delete_group` and `Group.delete`
remove a group and everything in it for good, while the gate refuses a group that is not empty. The
gate's behavior is the decided one, `API.md` §407, so a facade offers `trash_entry` and
`trash_group`, which mean what the gate does, and leaves the four deletes out, since a method that
looks like pykeepass's and does something else is worse than no method.

## The 9 that only need routes

- **An entry's tags**: `Entry.tags`, KeePass's own way of sorting entries, neither served nor
  written. Of everything missing, the one that matters most to a program.
- **An entry's history**: `Entry.history` and `Entry.is_a_history_entry`. Every update already keeps
  a revision, as KeePass does, and no route reads one. A revision holds earlier protected values,
  so reading it is a question for the protected switch rather than a field to add.
- **Icons, the last access time and order**: `Entry.icon`, `Group.icon`, `Entry.atime`,
  `Group.atime`, `Entry.index` and `Entry.reindex`. Nothing forbids them and nothing needs them.

## The 44 that cannot exist here

Five kinds, and only the first is about `pykeepass` at all:

- **The file and the key**: `save`, `read`, `reload`, `password`, `keyfile`, `transformed_key`,
  `database_salt`, `encryption_algorithm`, `kdf_algorithm`, `version`. A client never holding these
  is the point of the gate.
- **The XML document**: `xml`, `dump_xml` on all three classes that have it, `tree`, `payload` and
  `xpath`. There is no document on this side. `deref` was here, on the argument that following a
  `{REF:...}` placeholder reads other entries the ACL may refuse, so a resolved value would be a
  permission decision made invisibly. Step 24 answered that by following references on the
  client's side, every entry read through the gate under the caller's own rules, so it is served.
- **Operations the API already decided against**: `move_entry` and `move_group`, because a move is
  one operation about two nodes and the router authorizes one; `empty_group`, because a recursive
  delete lets a single grant destroy entries nobody decided about; and `binaries`, `add_binary`,
  `delete_binary`, and `Attachment.id`, an index into that pool, because a database wide binary
  pool has no node for the ACL to attach to; `Group.append` and setting an entry's or a group's
  `group`, each a move; and a group's `expires`, `expiry_time` and `expired`, which `API.md` §407
  declines. Plus the five `credchange_*` properties, which are key change policy and live with the
  file.
- **What an entry's own state leaves to KeePass or to the operator**: `Entry.autotype_enabled`,
  `autotype_sequence` and `autotype_window`, because a client that may update an entry and not read
  its password could point auto-type at a window it controls and have KeePass type the password
  there; `Entry.delete_history`, because a revision is what keeps an overwritten value recoverable
  and destroying one is the operator's, as emptying the recycle bin is; `Entry.save_history`,
  because every update already keeps one; and `touch` on both, because the times are KeePass's,
  moved by the writes that change something.
- **The database's metadata**: `database_name`, `database_description`, `default_username` and
  `recyclebin_group`. These were counted as needing a route, the root group to authorize against,
  until the operator ruled on 2026-10-26 that none is served, `API.md` §407: the status route names
  a database by its authority's digest so that a client is not told what the operator calls it, no
  client needs the name to tell its databases apart, the default username is a question about what
  a create fills in, and the bin is out of reach.

## What would have to change

**For a genuine `PyKeePass`, upstream would have to change.** The constructor would need to accept a
backend rather than a filename, and `save()` would have to delegate to that backend instead of
writing bytes. That is a change to somebody else's library, and even with it the XML-backed members
would still fail, because there is no document to walk. Subclassing does not avoid this: overriding
`__init__` and `save` leaves `xpath`, `tree` and `payload` reaching for a tree that was never built.

**For a facade, this side would have to change, and only a little.** Tags on an entry and a
decision to accept the read-per-candidate cost on four of the
`find_*` methods, none of which touches a principle; history, which needs a design of its own; and
the four deletes left out on purpose.

**What must not change** is the shape of what is handed back. Two rules, decided before anything was
built and still right:

- **No `save`.** A method that looks like `pykeepass`'s and does something else is worse than no
  method. Writes go through named calls that say they are talking to a gate.
- **Absent rather than empty.** Where the gate does not serve something, the attribute should not
  exist, so `hasattr` is the compatibility test and a missing feature is not an empty list.

PyKeePassGate's `model.py` already follows both.

## Running pykeepass's own test suite against it

Considered, and it does not work as a conformance check. Their tests construct a `PyKeePass` from a
`.kdbx` in their fixtures, so every one of them fails at construction for the reason above, and a
suite that fails at setup measures nothing about what is supported.

`surface.py` is the conformance check that does work: it fails when the library and the
classification disagree, which is the only way a table about somebody else's release stays true.
