# The API

Why the routes are shaped the way they are, and what was deliberately left out of them.

**What each route answers is the routing document's**,
[`src/KeePassGate/Spec/openapi.yaml`](src/KeePassGate/Spec/openapi.yaml), which ships inside the
plugin and is read at startup: the statuses, the shapes and the limits, said beside the operation,
schema, parameter or response they belong to, and the reason any one of them is so in a comment
beside it, where a section number in the 500 block marks whatever something cites. This document
holds what spans the API, which no one key could carry. Where the two disagree, the routing
document is right and this is out of date.

Calling it from a program is [`docs/pykeepassgate.md`](docs/pykeepassgate.md), and what decides
whether a call is allowed is [`docs/access-control.md`](docs/access-control.md).

## Two rules generate the whole table §401

**The verb is the operation's own name.** Create is `create`, read is `read`, update is `update`,
strip is `strip`, delete is `delete`, list is `list`. No route decides for itself, so a reviewer
checks the routing document by reading each `operationId` beside its `x-kpg-verb`, and a route
added later has one obvious answer rather than an argument. The two routes asking what a client
may do at a node do none of the six, so they are named `get` and declare `any`, which asks about
all of them at once.

**The node is the nearest addressable thing.** A field and an attachment are not nodes, so a rule
about them is a rule on the entry that holds them. A create's node is the container it goes into,
because that is what somebody has to be allowed to add to.

## Bodies are flat §409

Flat objects of strings, because `JsonBody` reads nothing else and a request body is bytes from a
caller that has proved nothing yet. That is why fields are addressed rather than patched: a map of
name to value is nesting, and one field per request is the better REST anyway, since a partial
failure cannot leave half an entry written.

Responses are not bound by the reader: a list of entries is an array of objects, and a read carries a
`fields` object. `JsonWriter` writes them, as widely as the reader is narrow.

## Deliberately not here §407

**Query parameters, but one.** Scoping is a path, so `GET /groups/{uuid}/entries` rather than
`?group=`. Filtering and paging were proposed and cut; when a database is large enough to need them,
they arrive as query parameters on the list routes, which is where selecting among a collection
belongs, and where the one there is arrived: `includeExpired`, on the two entry listings and the
heads beside them. A query name nothing declares is refused, `openapi.yaml` §507, rather than
every query being refused for all time, so the paging arrives by being declared.

**Moving an entry or a group between parents.** It is the one operation about two nodes, and the
router authorizes exactly one. A client holding `create` in the destination and `delete` in the
source can already do it in two calls, each properly decided and each recorded.

**Recursive delete.** A group that still holds something is refused, for the reason `deleteGroup`
gives.

**Optimistic concurrency was here, and both halves of what this said were wrong.** It read "an
`If-Match` over the modification time is the obvious shape and nothing needs it yet". Something
did: the operator asked for it, for the lost update below. And the modification time turned out
not to be a shape at all once it was measured. §410 has what replaced it.

**A group's expiry.** KeePass keeps one on a group as well as on an entry, and a group's is neither
served nor accepted. KeePass does nothing with it but draw the group with its expired icon: the
entries inside an expired group are listed, searched and auto-typed as any others, so there is
nothing in it for a client to heed. An entry's own is served and set, which `Entry` and
`EntryFields` say.

**An entry's auto-type settings**, whether it auto-types, its sequence and the windows it is
associated with, are neither served nor accepted, and adding them would be a way around the rules
rather than a field. Auto-type is KeePass typing an entry's values into another window, the
password among them. A client allowed to update an entry and not to read its password could
associate the entry with a window of its own, or write a sequence that types the password into a
field it reads, and the next time the operator auto-types there, KeePass would hand the client the
value the rules withhold, while the audit records an update and nothing more. The `protected` switch cannot
govern it either, since the value never crosses the gate. So auto-type stays the operator's, set in
KeePass's own entry dialog.

**Destroying what a removal keeps.** The recycle bin is out of reach, so an entry already in it is
answered `404`, and no operation clears an entry's history: each keeps an overwritten or removed
value recoverable, and making it unrecoverable is the operator's, in KeePass, as emptying the bin
is. A client that may delete an entry sends it to the bin, and one that may update it leaves a
revision behind, so neither can take away what the operator would restore.

## Optimistic concurrency §410

**The lost update.** A program reads an entry, works on it, and writes back what it read beside its
own change. Whatever the operator changed in KeePass in the meantime, or another client, or a
synchronize, is carried over by the old values, and nothing tells either side. An agent is the
caller that does exactly this, because it reads, thinks for a while and then writes.

**So a read of one group or one entry carries an `ETag`, and every request about one may carry
`If-Match`**, answered `412` when the node no longer holds what the tag says, as RFC 9110 section
13.1.1 has it. The targets that have a tag are a group, an entry, and an entry's fields and
attachments, one at a time or listed, which all carry the entry's tag, because a field and an
attachment are not nodes and the entry answers for them, §401. A listing of groups or entries
answers about a collection and carries none, and so does an access answer, a create sent to a
collection and the gate's own routes: those declare no `If-Match` and never read the header,
which is what a server may do for a resource it has no tag for, and the only conditional header
read anywhere is this one.

**Only for a client whose rule may read everything the tag covers.** For a group that is `read`;
for an entry, `read` reaching its attachments, and its protected values too when it holds any, in
one rule, since a switch belongs to the rule it is written in. Any other caller is handed no tag,
on a read or in a write's answer, and any `If-Match` it sends is answered `412` without anything
being written, because a condition it can never be told the answer to is one it cannot meet and
ignoring it would make the write unconditional. **This is the second answer, and the first was
wrong.** Every caller was handed the tag, and the tag is a digest of the content: a client that may
update a password and not read it wrote a guess, compared the tag that came back with the one it
held, and had the password in as many writes as it had guesses, measured at 614 writes and under
eight seconds; a client holding `list` alone polled the tag to learn when a protected value changed.
A key keeps a tag from being computed and does nothing about it being compared. Mixing a time or a
counter into the tag stops a write reproducing an old tag, and leaves the comparison between two
reads, which a client that may not read the value can still make; a signed token over a counter is
the same thing with a second key. Handing the tag only to a caller that may read everything it
covers takes the question away, since such a caller learns nothing from a tag that a read does not
already tell it.

**`If-Match` is optional**, at the operator's word, and that was argued rather than assumed. A
required one, answering `428` without it, would make the safe write the easy one for an agent; it
would also turn every write a script makes into a read first, which a script that wanted to write
blindly would do anyway and learn nothing from. Optional leaves the choice with the caller, which
is where the knowledge of whether it read anything is.

**The tag is a keyed digest of the content, not a clock.** The modification time was the obvious
tag and was measured before it was used, `FINDINGS.md` under "What a version of a node can be made
of": it steps every half to one and a half milliseconds, so two changes inside one step share it; a
synchronize takes the other copy's strings at an equal time and leaves it alone; a move does not
touch it; and a plugin that never calls `Touch` moves nothing. A digest of what the routes serve
misses none of those. What it covers is what the routes serve, as they serve them: the times to the
second, which is all a file keeps of them, the strings as a read decodes them, and an attachment's
bytes up to the largest the gate hands out, above which its size stands for it, since no client can
read or write those bytes through the gate.

**It is an HMAC under a key derived from the database's host certificate**, as the database stores
it, which no client holds: the gate key would have been the obvious choice and every paired client
has it. So nothing outside the gate can compute a tag for a guess, and one content gives one tag
across a lock, a restart and a reload, until the authority is recreated. **This reverses a key drawn
when the plugin loaded and kept in memory**, which made every tag change whenever KeePass started:
a client that read on one day and wrote on the next was refused for nothing anybody wrote, and the
operator asked why a tag should change when the node had not.

**A write's condition is decided where the write is made**, under the writer's lock on KeePass's
thread, against the node as the change finds it, so nothing can land between the check and the
change, and after every refusal that does not depend on it, as RFC 9110 section 13.2.1 orders
them: a node gone is `404`, a name taken or a group not empty `409`, and a dialog open on the node
`503 busy`, whatever the tag says. An update that would change nothing is asked the condition too,
under the same lock, and then writes nothing. A read's is decided against the tag it is answered
with, and that tag is taken before what it is answered with, so a change landing in the middle of a
read leaves a client an older tag over newer content, which its next write fails over, and never a
newer tag over older content, which its next write would carry straight over the change.

**A write answers with the tag it left, in its body as `etag`** and not as an `ETag` header: RFC
9110 section 9.3.4 makes an `ETag` on the answer to a `PUT` say the body sent was stored as sent,
and on a `POST` or a `DELETE` it names nothing the request had, while this is the tag of the whole
node. Taking a field or an attachment off answers the entry's tag, since the entry is still there;
removing a group or an entry, or creating one, answers none.

**A condition says nothing about a node the caller may not reach.** It is asked after the router
has decided, so a refused node is the same `404` whatever tag was named.

## Durability §408

Every write goes through the plugin's single writer: it takes one lock, marshals to the thread that
owns the database, makes the change, calls KeePass's own save, and does not answer until KeePass says
there is nothing unsaved. A `200` that meant "in memory, maybe" would be a lie an agent cannot
detect. What a client sees of that, a read meeting a change before its save lands and the revision
a change leaves, is said to the client in the routing document's own description, `openapi.yaml`
§501.
