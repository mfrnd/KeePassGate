# Can a pykeepass program be pointed at KeePassGate?

**Not as a `PyKeePass` object. Yes as a facade, for 23 of its 56 members.** The distinction is the
whole answer, and it is worth stating in that order because the first half is a design decision and
the second half is a feature somebody could build tomorrow.

Measured against **pykeepass 4.2.0**, with `spikes/pykeepass/` holding both measurements.

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

Of 56 public members on `PyKeePass`:

| | count | what it means |
| --- | --- | --- |
| **served** | 23 | PyKeePassGate can implement it now, over routes the gate already serves |
| **needs-routes** | 6 | nothing in the design forbids it; the gate does not serve it yet |
| **refused** | 27 | it needs the file, the key, the XML document, or an operation the design refuses |

`spikes/pykeepass/surface.json` has the per member table and the reason for each.

**The 23 are the ones a program actually uses.** `entries`, `groups`, `root_group`, `add_entry`,
`add_group`, `delete_entry`, `delete_group`, and the whole `find_*` family. A program that reads
entries, searches them, and adds or removes a few would move across with its call sites unchanged.

Two costs come with them, and both are properties of an API rather than defects. A listing carries
uuid, group, title and url, so `find_entries_by_title` and `find_entries_by_url` are one request,
while `find_entries_by_username`, `by_password`, `by_notes` and `by_string` are a listing plus one
read per candidate. And `attachments` across a database is one request per entry, because
attachments are addressed through the entry that holds them.

## The 6 that only need routes

`trash_entry`, `trash_group`, `recyclebin_group`, `database_name`, `database_description` and
`default_username`.

Nothing about them is forbidden. Delete already honors the recycle bin, so exposing trash and delete
as two operations is a routing decision; and the three database properties are metadata the gate
could serve with a node to authorize against, which the root group already is. If a facade is ever
built, these are the routes to add with it.

## The 27 that cannot exist here

Three kinds, and only the first is about `pykeepass` at all:

- **The file and the key**: `save`, `read`, `reload`, `password`, `keyfile`, `transformed_key`,
  `database_salt`, `encryption_algorithm`, `kdf_algorithm`, `version`. A client never holding these
  is the point of the gate.
- **The XML document**: `xml`, `dump_xml`, `tree`, `payload`, `xpath`, and `deref`. There is no
  document on this side, and dereferencing a `{REF:...}` placeholder would read other entries, which
  the ACL may refuse, so a resolved value would be a permission decision made invisibly.
- **Operations the API already decided against**: `move_entry` and `move_group`, because a move is
  one operation about two nodes and the router authorizes one; `empty_group`, because a recursive
  delete lets a single grant destroy entries nobody decided about; and `binaries`, `add_binary`,
  `delete_binary`, because a database wide binary pool has no node for the ACL to attach to. Plus
  the five `credchange_*` properties, which are key change policy and live with the file.

## What would have to change

**For a genuine `PyKeePass`, upstream would have to change.** The constructor would need to accept a
backend rather than a filename, and `save()` would have to delegate to that backend instead of
writing bytes. That is a change to somebody else's library, and even with it the XML-backed members
would still fail, because there is no document to walk. Subclassing does not avoid this: overriding
`__init__` and `save` leaves `xpath`, `tree` and `payload` reaching for a tree that was never built.

**For a facade, this side would have to change, and only a little.** Six routes for the
needs-routes group, and a decision to accept the read-per-candidate cost on four of the `find_*`
methods. Neither touches a principle.

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
