# The API

Why the routes are shaped the way they are, and what was deliberately left out of them.

**What each route answers is the routing document's**,
[`src/KeePassGate/Spec/openapi.yaml`](src/KeePassGate/Spec/openapi.yaml), which ships inside the
plugin and is read at startup: the statuses, the shapes and the limits, said beside the operation,
schema, parameter or response they belong to, and the reason any one of them is so in a comment
beside it, where a section number in the 500 block marks whatever something cites. This document
holds what spans the API, which no one key could carry. Where the two disagree, the routing
document is right and this is out of date.

## Two rules generate the whole table §401

**The verb is the operation's own name.** Create is `create`, read is `read`, update is `update`,
strip is `strip`, delete is `delete`, list is `list`. No route decides for itself, so a reviewer
checks the routing document by reading each `operationId` beside its `x-kpg-verb`, and a route
added later has one obvious answer rather than an argument.

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

**Optimistic concurrency.** An `If-Match` over the modification time is the obvious shape and nothing
needs it yet.

**A group's expiry.** KeePass keeps one on a group as well as on an entry, and a group's is neither
served nor accepted. KeePass does nothing with it but draw the group with its expired icon: the
entries inside an expired group are listed, searched and auto-typed as any others, so there is
nothing in it for a client to heed. An entry's own is served and set, which `Entry` and
`EntryFields` say.

## Durability §408

Every write goes through the plugin's single writer: it takes one lock, marshals to the thread that
owns the database, makes the change, calls KeePass's own save, and does not answer until KeePass says
there is nothing unsaved. A `200` that meant "in memory, maybe" would be a lie an agent cannot
detect. What a client sees of that, a read meeting a change before its save lands and the revision
a change leaves, is said to the client in the routing document's own description, `openapi.yaml`
§501.
