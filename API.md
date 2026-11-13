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

Flat objects of strings and arrays of strings, because `JsonBody` reads nothing else and a request
body is bytes from a caller that has proved nothing yet. That is why fields are addressed rather
than patched: a map of name to value is nesting, and one field per request is the better REST
anyway, since a partial failure cannot leave half an entry written.

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
govern it either, since the value never crosses the gate. So an entry's auto-type settings stay the
operator's, set in KeePass's own entry dialog.

**That does not keep auto-type the operator's, which is what this used to conclude.** KeePass's
global auto-type needs no association to choose an entry: by default an entry matches when its
title is contained in the title of the window in front, and a single match is typed at once, with
the default sequence of the user name, a tab, the password and Enter. The title is a field
`update` may change. So a client allowed to update an entry and not to read its password can give
the entry the title of a window it controls, a web page it serves among them, and the operator's
next press of the hotkey there types the password into it. Refusing a title change to a client
without `protected` would change what `update` means across the API, so the operator's ruling of
2026-11-01 leaves that to a change of its own, and `docs/access-control.md` warns the operator
beside the warning about URLs.

**The database's name, description and default username**, and which group is its recycle bin, are
neither served nor accepted. The name and the description are what the operator wrote in KeePass for
themselves, and either can name a person or describe what the database is for, while
`GET /gate/status` names the database by the first characters of its authority's digest, which is
what tells a client which database answered without telling it what the operator calls it. Serving
the name would undo that and help no client: a client paired with several databases already tells
them apart by the client profile each pairing is stored under, a name it chose itself. That covers
the database's own name and nothing else: **the root group's name is served like any group's**,
under the rules, and KeePass names a new database's root group after its file, so a client allowed
to list or read at the root learns that name unless the operator has renamed the group. The default
username is what KeePass's own Add Entry dialog fills in, so if it is ever wanted it is a question
about what `createEntry` fills in rather than a value to read. And the recycle bin is out of reach,
so naming it is all a route would do.

**Destroying what a removal keeps.** The recycle bin is out of reach, so an entry already in it is
answered `404`, and no operation clears an entry's history: each keeps an overwritten or removed
value recoverable, and making it unrecoverable is the operator's, in KeePass, as emptying the bin
is. A client that may delete an entry sends it to the bin, and a client that may update it leaves a
revision behind, so neither takes away what the operator would restore, within two limits the
operator sets in KeePass: a database that keeps no bin deletes for good, from the gate as from
KeePass's own window, and an entry keeps only as many revisions as Database Settings allows, so a
client that changes it more often than that has pushed out every revision from before it started, as
`docs/access-control.md` warns the operator.

**Tags are not here yet, and nothing refuses them.** An entry's tags and a group's are neither
served nor accepted, and nothing in the rules stands against serving them, or filtering a listing on
them, since each is a field of a node the rules already answer for, §401. Of what a program reading
entries finds missing, they matter most, and they wait for a change of their own.

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

**A tag covers what its caller may read, and every caller holding `read` is handed a tag.** A
group's tag covers what `read` serves, and a group has no switches to scope it by. An entry's
covers the entry as its caller's rules open it: every string's name and whether it is protected,
the value of every string nothing protects, the values of protected strings where `read` and
`protected` stand in one rule, a protected title or URL also where `list` and `protected` stand
in one rule beside `read`, which is what a listing of entries shows such a caller, the
attachments' names, sizes and bytes where `read` and
`attachments` stand in one rule, and their names and sizes alone where `attachments` stands with
`list` and not with `read`, which is what the listing of attachments serves such a caller. A
switch counts in the rule it is written in, and profiles are combined with or, as a read combines
them. A value the caller may not read enters as a fixed mark beside its name, the same mark
whatever the value is and however long, and attachments the caller is shown nothing of enter
nothing at all, with no mark of how far its rules reach. So an entry holding no attachment has
the same tag for a caller that may read attachments and for a caller that may not, since neither
is shown any, and a rule widened over an entry where nothing new comes into sight moves no tag.
A caller without `read` is handed no tag, on a read or in a write's answer, and any `If-Match` it
sends is answered `412` without anything being written, because a condition it can never be told
the answer to is one it cannot meet and ignoring it would make the write unconditional.

**So a caller has one tag for an entry, whichever route hands it out**, and a write is asked
against that tag and answers with it: what a caller may read depends on its rules and the entry
and never on the route. Nothing about the rule is in the tag, no scope, no mask and no client, so
two callers that see the same of an entry hold the same tag, and a reader can hand its tag to a
writer that sees what it sees. Mixing the scope in was considered and dropped: the content
already differs wherever two views differ, and a scope would move a tag when an operator widens a
rule over an entry where nothing new comes into sight, a `412` for nothing anybody can see.
Binding the tag to the client's certificate would move every tag at a renewal, for nothing
anybody wrote, and would close no attack, since the guessing attack compares a client's own tags.

**This is the third answer.** The first handed every caller the tag of the whole node, and the
tag is a digest of the content: a client that may update a password and not read it wrote a
guess, compared the tag that came back with the tag it held, and had the password in as many
writes as it had guesses, measured at 614 writes and under eight seconds; a client holding `list`
alone polled the tag to learn when a protected value changed. A key keeps a tag from being
computed and does nothing about it being compared. Mixing a time or a counter into the tag stops
a write reproducing an old tag, and leaves the comparison between two reads, which a client that
may not read the value can still make; a signed token over a counter is the same thing with a
second key. The second answer kept the tag of the whole node and handed it only to a caller
whose one rule read everything it covered, `read` reaching attachments and protected values in
one rule. That closed the attack and left most agents with no tag at all: an entry KeePass or
`createEntry` makes holds a protected value from the start, its `Password`, so a rule without
`protected`, which is the rule an operator writes for a program that should not see passwords,
was never handed a tag, and its writes could not be made against what it had read. **The
operator ruled on 2026-11-10 to reverse it**: the tag follows the view. A tag holds nothing its
holder may not read, so comparing two of its tags tells a caller nothing a read does not. A guess
written blind does move the tag, through the entry's `modified`, which is in every view, and it
moves it the same whether the guess was right or wrong, with nothing derived from the value in
either tag. That rests on an update that would change nothing being written for a caller that
may not read what it names, below: a skipped guess would leave `modified`, and with it the tag,
where it was, and show there exactly as it shows in a listing.

**What the third answer costs.** A client writing a protected value it may not read is told that
the value changed in between only by the entry's `modified`, which is in every view: a change
that moves the served second moves its tag, and a change that does not is missed. That is what
`modified` misses, two changes inside one second, a synchronize taking the other copy's strings
at an equal time, and a plugin that never calls `Touch`, and for a value in sight the content
catches all three. The client never read the old value, so nothing it read is carried over a
change it was not told of. A caller's tag changes when the operator widens or narrows its rules
in a way that changes what it sees of the node, so its next `If-Match` fails once and it reads
again, which is right, since it now sees something else. And two callers that see different
things of one node hold different tags for it, so a tag passed from one program to another is
good only between programs that see the same.

**A caller's view of an entry holds everything any route shows that caller of the entry**, and
the read routes serve what the view holds. The listing of entries shows a protected title or URL
to a caller holding `list` and `protected` in one rule, §401, so for a caller holding that beside
`read` the title and the URL are in its view: `readEntry` and `readField` serve them and its tag
covers them, while every other protected string still needs `read` with `protected`. **This
reverses a view that read protected values under `read` with `protected` alone**, under which
such a caller was shown a title by a listing and refused the same title by a read, and held a tag
that did not move when the title it had been shown changed, so its conditional write went ahead
over a change a listing would have shown it. That was first written down as a cost and then
closed, on the operator's word that a listing and a read have to agree: `list` with `protected`
brings
the listed strings into the view the way `list` with `attachments` brings in names and sizes.
Without `read` it brings nothing in, since a view that does not read holds no value. **And the
same rule decides the listing, so the two agree in both directions**: a protected title or URL
is shown to a caller, by a listing and by a read alike, when the caller may list or read the
entry on that route's own verb and holds `protected` beside `list` or beside `read` in one rule
on that entry. So a listing also shows the title to a caller holding `read` with `protected`
beside `list` without it, where it answered `null` for a title the same caller's read served.
The condition is in one place that the listing and the view both ask, and what a listing shows
of an entry's title and URL is what that caller's view holds of them whenever it reads the
entry. A caller that lists and does not read the entry is shown the title by the listing under
`list` with `protected`, as it was, and its view of the entry holds no value. One edge
of another kind is kept: a caller holding `read` and `attachments` with no `list` is
listed no attachments and reads any of them by its name, so every attachment is in its view and
in its tag, and its tag moves for an attachment it has not named.

**Rejected on the way, so they are not proposed again.** A counter or version number: KeePass
offers nothing that serves as either, the modification time having been measured and refused, the
history stopping at its ceiling and the usage count counting use; a counter of the plugin's own
in an entry's custom data, moved only on API writes, has to report the operator's edits as the
counter plus one without storing it, so two edits in a row share a number and an update is lost
between them, and closing that means writing on every edit the operator makes. A random token
per state, kept in memory, is gone at every restart, which is the behavior the key's own
reversal below took away.

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
misses none of those, for whatever its caller may read. What it covers is what the routes serve
that caller, as they serve them: the times to the
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
under the same lock, and then writes nothing, for a caller that may read every value it names; for
any other caller it is written like any change, so that a guess at a value it may not read costs
the same right or wrong. What a caller may read is read off its view of the node, the view its
tag is taken of, so a protected title or URL a listing shows the caller counts as read, on
`protected` beside `list` as well as beside `read`. A read's is decided against the tag it is
answered
with, and the tag and what it is answered with are of one moment: a group's are taken inside the
hold of KeePass's thread the read is decided in, and an entry's are both made of the view of the
entry taken inside that hold, `DESIGN.md` §119. **That reverses a tag taken
before its content**, which was the safe order while a change could land between the two: a
client was left an older tag over newer content, which its next write failed over, and never a
newer tag over older content, which its next write would have carried straight over the change.

**A write answers with the tag it left, in its body as `etag`** and not as an `ETag` header: RFC
9110 section 9.3.4 makes an `ETag` on the answer to a `PUT` say the body sent was stored as sent,
and on a `POST` or a `DELETE` it names nothing the request had, while this is the tag of the
caller's view of the whole node. It is taken of the node as the change left it, and of what the
caller may read of that: a change that makes a string protected leaves a view its own caller
reads less of, and the tag in the answer is of the view after. Taking a field or an attachment off
answers the entry's tag, since the entry is still there; removing a group or an entry, or creating
one, answers none.

**A condition says nothing about a node the caller may not reach.** It is asked after the router
has decided, so a refused node is the same `404` whatever tag was named.

**It does tell a client which names an entry carries, where the client may change or strip a part
it is not shown.** A condition is asked after a field or an attachment the entry does not carry
has been answered `404`, so a condition that cannot hold, any condition from a client handed no
tag and a made-up tag from any other, is answered `412` for a name that is there and `404` for a
name that is not. A client that may change or strip an entry without holding `read` on it learns
the entry's names that way, a name at a time and without writing anything, and so does a client
that reads the entry without `attachments` and may strip: it is handed a tag, is shown nothing of
the attachments, and a strip asks no switch, so it learns their names the same way. A
write without the condition tells it as much, by making the change wherever the name is there, and
answering the condition first would go against RFC 9110 section 13.2.1, which has a server ignore a
condition on a request that would be refused without it. The same is conceded to `create`, whose
`409` tells a client that may add and not list that a name is taken.

## An entry's ceiling is a read's §412

**A read hands out at most 524288 characters of an entry's names and values, and what is counted
is what its caller is handed.** Building an entry's answer costs about four times its strings in
memory inside KeePass, so a read of an entry is held down as a read of an attachment is. Counted
are the name of every string the answer serves and the value of every string the caller's rules
let it read. A value the caller is withheld counts nothing and is never measured: the view the
count is taken of, `VaultEntryView.Characters`, holds nothing to measure it with. So two clients
can be answered differently about one entry, each by what it would have been handed. Every read
of an entry's strings is held to the ceiling by that one rule, over what the route hands out:
`readEntry` over the caller's whole view, `readField` over a string in the caller's sight with
its name, and `listFields` over the names, which are all it serves, so a listing of fields can
answer `413` as well. Each is `413` with that count, before any value is read and before the tag
is taken, and in the order a read's refusals had already: `404`, then `413`, then `412`.
`readField` decides whether a value is withheld before it measures anything, so a withheld value
is answered `null` whatever its length and never `413`. `openapi.yaml` §513 is the response the
routes declare.

**Refused before any value is read is not refused before anything is decrypted.** No value is
handed anywhere until an answer asks for it, and a view asks a string's length only when its
size is asked and only of a string in its caller's sight. That length is KeePass's own count,
and KeePass counts a protected value it holds as bytes, which is how a file's protected values
are held once loaded, by decrypting it once inside its own type and keeping the count. So the
first measurement of such a value costs a decryption, and what comes out of it is a number.
`FINDINGS.md` has the measurement. An attachment's size is kept beside its encrypted bytes, so
an attachment is measured and refused with nothing decrypted.

**No write is refused for what an entry's strings come to, which reverses a ceiling held in both
directions**, the operator's ruling of 2026-11-10. A create, an update or a new field that would
take an entry's strings past the ceiling was refused `413` under the writer's lock, so that what
the gate let a client write it also handed back whole, and the refusal named what the strings
would come to, protected values included. That count was a measurement of what its caller may not
read. A client holding `update` and not `read`, or `read` without `protected`, padded an entry to
just under the ceiling and read the length of the values it was withheld off the refusal, and
with the count left out of the message it would have searched for the padding the refusals began
at. `FINDINGS.md` has the measurement, under "What a version of a node can be made of".
Considered in its place and refused, because each keeps a refusal that depends on a value its
caller may not read: a floor or a rounding for each string, a ceiling for each protection class,
the message without its count, random slack in the ceiling, and pacing the refusals.

**What the reversal gives up.** An entry can be written through the gate past what a read hands
back whole, as KeePass or a merge could always leave an entry. Such an entry is read a field at a
time, `listFields` and then `readField`, and stripping a field is how it is brought back under.
Nothing bounds what a client holding `update` or `create` can add to an entry beyond what a
request may carry, 65536 characters in a value and 1 MiB in a body, so how large an entry and the
file around it grow is that client's to decide, a request at a time. PyKeePassGate raises on a
whole read refused this way and does not go on to read field by field: which strings to fetch of
an entry too large to hand over is its caller's decision.

**A tag is still taken over the whole of a caller's view, however large, and a read takes it
with KeePass's thread free.** A read refused for its size takes no tag, so it digests nothing.
Every other answer that hands a tag out, a listing of the fields and a read of a short field
among them, and every write under `If-Match` or answered with an `etag`, reads every value in its
caller's sight. A read does that once the hold of KeePass's thread has ended, from the view of
the entry it took inside the hold, and encodes a long value for the digest a piece at a time,
`DESIGN.md` §119. Measured on an entry of fifty protected strings of a million characters each,
a read that hands a tag out holds KeePass's thread for under a tenth of a millisecond and then
takes about sixty milliseconds on its own connection's thread, and 48 MB a megabyte at a time:
a copy of each protected value, digested and overwritten, so that KeePass goes on protecting the
value in memory, `DESIGN.md` §136. A write takes its tags inside
its own turn on KeePass's thread, where the comparison and the change are one step, beside a
save that takes far longer; `FINDINGS.md` has the numbers. No ceiling was added for it.

## Durability §408

Every write goes through the plugin's single writer: it takes one lock, marshals to the thread that
owns the database, makes the change, calls KeePass's own save, and does not answer until KeePass says
there is nothing unsaved. A `200` that meant "in memory, maybe" would be a lie an agent cannot
detect. What a client sees of that, a read meeting a change before its save lands and the revision
a change leaves, is said to the client in the routing document's own description, `openapi.yaml`
§501.

## The version is the contract's §411

The plugin, PyKeePassGate and the routing document carry one version, and it is the version of
this API's contract, which is what makes it something a client can compare. **A change a client
may rely on moves the minor version, a change that breaks an existing client moves the major, and
a patch changes no contract.** Breaking is a program that worked before having to change: a route,
a status or a body on the wire, what a certificate or an admission record carries, or what the
client raises or returns. That is a design rule, written here as part of the contract, and not a
convention a release checks: nothing enforces it but whoever makes the change.

**A client works with a gate when their major versions are equal and the client's major and minor
do not lead the gate's.** Patches are released on each side on their own, so they are not
compared, and a gate ahead of its client is a working pair, since everything the client relies on
is still served. A `0.1` client works with a `0.2` gate, and a `0.2` client does not work with a
`0.1` gate.

**And the client refuses any other gate, before it sends anything else.** A paired client asks
the status route before its first request, and the pairing hello carries the version beside the
gate's proof, since a connection that arrived on an invitation reaches no other route: the client
reads it once the proof verifies and stops before the request that puts it in front of the
operator. Either raises an error naming both versions, and the command line exits with it. A
version that does not read as three whole numbers is refused as well, since nothing about it can
be compared.

**Under 0.x the rule has a consequence.** With the major versions required to be equal, a change
that breaks an existing client can no longer be a 0.x minor version, since the client it breaks
would still take the gate for a gate it works with. So the next such change is 1.0.
