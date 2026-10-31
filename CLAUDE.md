# CLAUDE.md

Guidance for anyone, human or AI assistant, working in this repository.

## What this is

**A KeePass 2.x plugin, written from scratch**, serving a small specification-defined HTTP API over
mutual TLS, so that automation and AI agents can each be given a narrow, revocable, audited slice of
a database.

**Status: steps 1 to 31 of the build order are done and the API is served.** The plugin loads
into KeePass, the KPG tab creates, recreates and deletes the database's own certificate authority,
the gate listens on loopback and answers over mutual TLS 1.3, a client is served only if
the database holds an admission record for its certificate, and **a stranger can now become one**:
the tab hands out an invitation, both sides prove they hold it, the operator approves what it may
reach, and the record is written and saved before the certificate is handed over, and **a locked
KeePass now answers `503 locked`** with a second host certificate held only in memory. **The routing
table is now the OpenAPI document itself**: handlers bind to `operationId`, an undeclared path has
nowhere to go, and a document that disagrees with the handlers stops the plugin loading. **What a
grant means is now settled**: rules live in group and entry `CustomData`, nearest wins per profile,
and every operation declares the verb it needs or the plugin does not load. **An operator can now
write one**: the KPG tab on the group and entry dialogs edits the rules as a `CRUSDLAP` mask,
writing into the copy the dialog puts back on OK. **And the two halves are combined**: the router
intersects the admission record with the rules before any handler runs, and writes every decision
the rules make, every certificate the database issued that an operation turns away, and
every pairing and renewal the operator answers and every client revoked, to a JSON Lines
file outside the database. **And there is an API**: twenty-four routes over groups,
entries, fields and attachments, every one declaring the verb it needs and the node it is
about, and the routes about the gate and the caller beside them, with `PyKeePassGate`
covering all of them. **And a profile has a home**: the set of names a database
knows lives beside the authority, with a page of its own to manage them, and a name taken off it
grants nothing whatever rule still names it. **And the plugin has a window of its own**, under KeePass's Tools
menu, saying where the gate is listening and taking the port it should listen on. **And there is
documentation for whoever runs it**: `docs/`, written in the order an operator meets the plugin,
around screenshots a fixture takes by driving a real KeePass. **And a certificate has a
lifecycle**: the authority and its host certificate are issued together for a hundred years, a
client's certificate lasts as long as the operator picks from what the client proposes, the records
of expired certificates go with the plugin's next write of its own, a pairing, a renewal, a
revocation or a change to the authority, and a client can give its own certificate
up, or renew it on an approval dialog like the pairing one, which can refuse it once, in this
session, until a date or forever, an answer the clients page shows in red and takes back on
the operator's word. **And the client has options of its own**: a program built on it
can keep its key under a password, make the connection it goes over, and test against the stand-in
gate the client's own tests use. **And a client can ask about itself**: who the gate takes it for,
with the key the operator knows it by, and what it may do at a group or an entry before it tries.
**And field references are followed**: the reference client compiles them as KeePass does, on its
own side and under the caller's own rules, held to KeePass's own compiler. **And there is a package
a release can publish**: `build.ps1 -Task Package` zips the plugin with everything it loads, its
license and the notices, beside a `SHA256SUMS`. **And the project has a front page**: the README
says what this is for and shows it, around a moving picture of the whole journey from a database
to a revoked client, which the Screenshots fixture takes with the stills, and a diagram of the
pieces. **And the command line covers every route**: a command for each route the library
serves, held to the routing table as the library is, and `-o json` on every one for a program.
**And a write can be made against what was read**: a read of one group or one entry carries an
`ETag`, a keyed digest of what the node holds, to a client whose rule reads all of it, and a
request carrying `If-Match` is answered `412` when the node has changed since, decided under the
writer's lock. **And `master` takes a merge only once the gate has passed**: a ruleset requires the one
check `gate passed`, which stands for every job the workflow runs. **And a `v` tag publishes a
release**: the plugin's zip as a GitHub release and the client on PyPI, last, through Trusted
Publishing, after a rehearsal against a draft and TestPyPI, as `RELEASING.md` describes. **And
CodeQL reads what the gate does not**: the C#, the Python and the workflows, on every pull request,
every push to `master` and once a week. **Step 32, a clean analysis, is being built**: what the
build order plans for it is in `DESIGN.md` §132.

## Read these before proposing anything

| | |
| --- | --- |
| [`DESIGN.md`](DESIGN.md) | what to build, and a section attacking it. **The contract.** |
| [`docs/`](docs/README.md) | the operator's documentation, and the screenshots it is written around |
| [`API.md`](API.md) | the reasoning behind the routes and what is deliberately absent, with the per-route detail in [`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) |
| [`CA.md`](CA.md) | the certificate authority: issuance, claims, validation, revocation |
| [`PAIRING.md`](PAIRING.md) | how a stranger becomes a client, with the flow diagrams |
| [`FINDINGS.md`](FINDINGS.md) | what was measured rather than assumed, and what turned out wrong |
| [`TESTING.md`](TESTING.md) | the four test tiers, what each one buys, and how to run them |
| [`RELEASING.md`](RELEASING.md) | how a version is published, and the settings a release needs |

**One document is historical and says so at the top.** [`ANALYSIS.md`](ANALYSIS.md) is the argument
that was made, and several of its conclusions were later reversed; it opens with a banner naming
them. **Do not build from it.** It is kept because the reasoning is worth having and because a
reversal without its argument is just an assertion.

**The code those two measured is not on any mainline branch.** `spikes/http-lib-measurements`, taken
from `master` and never merged, keeps the three vendored HTTP proofs and gathers what they settled
in its own `RESULTS.md`. A spike lives on its own branch so that no mainline history has to carry it
being added and deleted again.

**How the screenshots are taken was worked out on a third.** `spikes/window-capture` holds the
survey of what Windows offers for getting one window into a file, and what was measured on these
dialogs: two methods were tried and found wrong before the one in use. What is on the mainline is
the method and the reason; the alternatives are there.

**The measurements the transport rests on are on two more.** `spikes/tls-and-certificates` holds
what was measured about TLS and X.509 on .NET Framework, and `spikes/build-environment` whether the
gate can fail while nothing is wrong. Both are kept runnable rather than retired, because each is a
claim about somebody else's code that a later release could make false. What they settled is under
"Measured facts that cost time if rediscovered".

**Whether CI needs a machine of its own was settled on a seventh.** `spikes/hosted-runner-desktop`
drives a desktop GUI on `windows-latest` and answers yes: the end to end tier runs on a hosted
runner, with no self-hosted machine and no container. It is a claim about somebody else's runner
image, so it is kept runnable too.

## Visibility

**The secrecy rule is in [`CONVENTIONS.md`](CONVENTIONS.md)** and holds on every branch: write every
file as if it were already public, and nothing about the author's actual database reaches any of
them. That holds whatever the repository's setting says on a given day: it is written to be
published, and a file pushed while it was private is in the history on the day it is not, where
only a rewrite takes it back out.

**License: MIT.** KeePass is GPL-2.0-or-later and grants no plugin exception, but the FSF's own
requirement is a GPL-compatible license and MIT is one. This is deliberate and it constrains
dependencies: see the LGPL note under "already decided".

## Things that are already decided

These came out of a long design discussion and several rounds of adversarial review. Treat them as
settled unless there is new evidence, and where there is, say so explicitly rather than quietly
designing around them.

- **Every database is its own certificate authority**, stored inside itself under `KPG` keys in the
  database's own `CustomData`. A malicious `.kdbx` therefore vouches only for itself. An earlier design
  put one authority on the host with a registry beside it, and that registry had to name databases,
  which an attacker-supplied file could forge. **Do not re-propose a host-level authority or
  registry.**
- **The database's own `CustomData`, never the root group's**, and it was the root group's until a
  merge was measured. KeePass stamps each key of `PwDatabase.CustomData` with a modification time
  and merges those key by key; a group's dictionary carries no stamps at all, so
  `PwGroup.AssignProperties` replaces the whole of it from whichever replica's group was touched
  last. Records shaped one-per-client to survive a merge did not. Two consequences worth keeping
  straight. Each authority's three parts are **one key**, `AuthorityDocument`, because there the
  argument runs the other way: merged apart, a certificate from one replica and a private key from
  another read as damage. And **what has to survive a merge is a key that is only ever added**,
  because a KeePass merge never removes a key and never reads a value. A revocation is
  `KPG.Revoked.<key>` beside the emptied record, never changed and pruned once the certificate has
  expired; an authority is one key per authority, `KPG.Authority.<fp>`, written once, and one
  recreated or deleted gets `KPG.RetiredAuthority.<fp>` beside it, so which one is served is decided
  by the retirements and never by counting documents. More than one authority with no retirement
  serves nothing: every connection is answered `503 undecided`, and KeePass asks the operator which
  to keep in a dialog over its main window, never raised from a connection. **This reverses two
  rules**: a revocation used to be written empty rather than removed and the only authority lived
  under one key, both resting on a merge carrying the newer value, which a dropped time, a slow
  clock or an import defeats. A record that comes back beside its revocation is deleted by the
  plugin's next write of its own, and recreating or deleting the authority removes every record
  and every revocation with it. See CA.md §204.
- **The authority takes the file to KDBX 4.1**, because a per-key modification time is a 4.1
  field. A database saved with an authority in it is a 4.1 file before anything has paired with
  it, and a record keeps it there. That is a format and not a gate: KeePass 2.47
  opens it and saves it back as 4.0 without the per-key times, and so does KeePassXC. A revocation
  no longer needs them, being a key of its own, but merging in another program can still undo one,
  which is why a paired database belongs in KeePass 2.48 or later with the plugin. See CA.md §204.
- **The admission record is load bearing.** A certificate that chains, passes its EKU check and has
  no record in the database is refused, and so is one whose record has a revocation beside it. The
  permitted profiles are read from the **record**, less any name the database's list of profiles no
  longer holds, and never from the certificate's SAN. This is not belt and braces: it is what makes
  a lost record fail closed. See the `GroupForm` trap below.
- **Claims carry profile names only**: `kpg://profile/<name>`. No database identifier, because the
  issuing authority already identifies it. **There is no `kpg://any` wildcard**, and re-proposing one
  reopens a measured escalation.
- **The authority key has no passphrase.** It is protected by the kdbx encryption. An earlier draft
  wrapped it in Argon2 over AES-CBC with an encrypt-then-MAC construction and DPAPI; all deleted, and
  its backup story was broken anyway because a DPAPI blob restores nowhere.
- **Pairing tokens live in plugin memory only**, expiring on TTL or KeePass exit, whichever first.
  They are never written to the database. `InvitationBook` is the whole of it, under one lock, and a
  burn cannot be reverted. **So are approvals KeePass was too busy to write**, in `ApprovalBook`
  beside it, the operator's ruling of 2026-10-14 over an HMAC token on the wire: keyed by a digest of
  exactly the approved request, held for the rest of the pairing budget, used once, let go by a
  lock, a close and an exit, and compared under the writer's lock at the write with the authority,
  the profile list and the renewed record it was approved against. The client is told
  `heldUntil` in its `503 busy`. And the settings dialog a token was made on withdraws it unless
  that dialog is accepted, so Cancel means Cancel for the one thing on the tab the database does
  not hold.
- **The approval dialogs are not registered with `GlobalWindowManager`**, and that is the reverse of
  every dialog KeePass shows. A registered dialog holds KeePass's inactivity lock off and drops a
  Windows lock, a session switch or a suspend asked for while it stands, and a dialog a client can
  raise, one after another, would then keep KeePass unlocked with nobody at the desk. So a lock goes
  through under either dialog and closes it as unanswered, `PAIRING.md` §308. Do not register one to
  keep it from being left over a locked database: closing it at the lock is what does that.
- **The proofs need nothing from the transport.** `tls-server-end-point` is a hash of the
  certificate in use, so each side computes it from bytes it already holds. Do not re-propose
  `TransportContext.GetChannelBinding`: it was necessary while `SslStream` terminated the TLS and
  the reason went away with it.
- **Two host certificates per database**: a normal one in the kdbx, and a responder held only in
  memory, minted at unlock and again at the lock it answers and good for ninety days, so a locked
  KeePass can answer `503 locked` rather than failing at the transport. It was seven days and minted
  at unlock until a long lock outlived it. The normal host key is let go at lock, and `CA.md` §208
  is exact about how much of that is erasure: the gate key and the stored PKCS#12 are overwritten,
  while the key the TLS server signs with is a BouncyCastle value over an immutable integer that
  offers nothing to overwrite, so the last reference goes and nothing more. The responder is marked by a
  `kpg://host/responder` URI in its subject alternative name, **not** a private OID: the UUID arc
  `2.25` encodes on .NET Framework and does not survive being read back, and `1.3.6.1.4.1` would be
  squatting on somebody else's enterprise number.
- **We own the transport.** Accept loop, ClientHello peek, TLS termination, request parser, body
  read, response write. Selecting a host certificate by SNI makes this unavoidable, because
  .NET Framework exposes no server-side SNI hook. Which TLS stack terminates it is the bullet
  below.
- **Handlers catch what they named.** `Diagnostics/Expected` holds the lists, used as exception
  filters. Four places catch everything, each for what an escape would do there. **The handlers
  of KeePass's own events**, the settings dialog's close among them, because KeePass raises its
  events with nothing around them, so an escape stops KeePass's own work where it was: a lock of
  every database stops at the one being closed and leaves the ones after it unlocked, and a lock
  the inactivity timer began leaves that timer's critical section held, since KeePass releases it
  outside a `finally`, so the timer locks nothing again that session, which is read off KeePass
  2.61.1 and not driven. Then WinForms, which KeePass gives no handler for an unhandled exception,
  puts its own error dialog over KeePass, whose Quit ends the process outside KeePass's own
  shutdown. **The Tools menu item**, which would bring up that dialog. **The connection thread**,
  where an escape ends the process. **And each step of `Terminate`**: KeePass throws away whatever
  `Terminate` throws, so an escape would cost the steps after it, among them what lets go of
  KeePass's static window events, the socket and the keys, and `Initialize` runs it too, with
  KeePass carrying on.
  Those report through `GateIncidents`, which counts on the plugin's window under Tools and
  logs at `UNEXPECTED`. Two more roots are kept safe another way: the accept thread and a
  connection's deadline timer have nothing above them, like the connection thread, and rather
  than catching everything, every call they make is total or filtered. `GateListener.Drop`
  explains what keeps that true. And three places catch everything without being a fifth, since
  each throws what it caught again: `GateTlsServer`'s callbacks, because Bouncy Castle turns
  whatever a callback throws into a fatal alert, and the call `ControlUiThread` marshals, because
  WinForms would answer one escaping it with that dialog, both keeping it for where one of the
  four sees it, and `Initialize`, which calls `Terminate` and throws it to KeePass, since KeePass
  warns the operator that the plugin cannot be loaded, with the exception's message, and undoes
  nothing. **And three reporters catch everything, since none may add an exception of its own**:
  the log, `TraceGateLog`, which the catch-alls report through and whose formatting and trace
  listeners run code nobody here wrote, `GateIncidents.Record`, which they report to, and the
  audit line `JsonLinesAudit.Record` shapes, which may not fail the request it records.
  `Unattended.Post` catches what its posted work throws, which is the catch-all of the handler
  that posted it, followed across the post to KeePass's message queue, and what its reporter
  throws, for the reason the log does. CodeQL reports each of these places but the ones that
  throw again from inside the catch, which its query accepts, and each place it reports carries a
  suppression naming where it stands in this account.
  **A line that has to outlive a defect is written before it rather than caught around it**: the
  audit line for a pairing, a renewal or a client's own revocation that landed is written by
  `DatabaseWriter` on its own landed path, before any listener runs, so no listener's defect can
  lose it and the operation writes none of its own.
- **Everything on the KPG settings tab waits for that dialog's OK**, and this reverses an
  earlier decision rather than refining it. The tab used to write the moment a button was pressed,
  which was safe, because KeePass's database settings dialog holds no copy of the custom data this
  plugin writes and so never undid any of it. It was still wrong: a dialog offering OK and Cancel
  says nothing has happened yet, and a tab inside one that has already happened is a tab lying about
  the buttons around it. `PendingDatabase` wraps `IGateDatabase` for the tab and nothing above it
  knows; `AuthorityPanelFactory` applies on `FormClosed` with `DialogResult.OK` and discards
  otherwise. KeePass's own `FormClosed` handler, subscribed when the dialog was made, has taken the
  page away by then, and that costs nothing: what is applied is the buffer the panel wrote into,
  which outlives it, and not the panel.

  **Replayed key by key, never wholesale**, because a snapshot written back would be this plugin
  doing to itself exactly what `GroupForm` does to it: a pairing that lands while the dialog is open
  writes an admission record to the live database, and only touching the keys the operator changed
  leaves it alone.

  **And `KPG.Profiles` is replayed as a difference**, because it is a key whose second writer
  has to be merged with the tab: a pairing names an approved profile in the same write as the
  admission record, so the tab's own list is not the only version of it. Record and revocation keys
  have second writers too, a client revoking itself and the prune that rides on the plugin's own
  writes, and key by key is enough for those, since a revocation reads the same whoever wrote it and
  a pruned record had nothing left to say. Key by key is no help for the list, since the tab did
  touch that key, and the older list went over the newer one. `PendingStore.Reconcile` registers a
  merge through `ProfileNames.Merge` for it, so a name an approval added survives and a name the
  operator removed still goes, and at the ceiling a name the operator added that no longer fits is
  left out, said on the page as it goes and named in the log at OK, however the list was edited in
  between. What no merge can decide is left where it is: a client approved for
  a profile the operator is part way through removing is admitted to a name OK takes off the list,
  where it grants nothing, which is why a buffered profile edit kills the invite button.

  **A standing answer to a renewal is the other key with a second writer that matters.** The
  clients page's **Allow renewal** takes one away in the buffer, and the renewal dialog writes one
  live once an until-date passes while the settings dialog is open. That answer is the later word
  on the certificate, so `PendingDatabase` registers a merge for every `KPG.RenewalRefused.` key,
  through `PendingStore.Reconcile` by kind, that keeps whatever changed under the dialog. An answer
  for this session has no key: `PendingSessionRefusals` holds the lift beside the buffer, and the
  factory applies it to the plugin's one `SessionRefusals` on OK and drops it otherwise.

  One case survives all of that, deliberately: recreating or deleting the authority while a pairing
  lands leaves that record behind, inert, because its certificate chains to an authority that is
  gone. The clients page says so on the row rather than listing it as a client.

  **Revocation waits too, and that is a cost that was argued and accepted.** Immediacy is something
  a revocation would rather have, and an operator who revokes and leaves the dialog open has revoked
  nothing. What it buys is one model for the whole tab, because whichever model somebody assumes on
  the page they are not looking at is the one that costs them. **Allow renewal waits the same
  way**: the client's name stops being red at once, as a revoked row goes at once, and the answer
  is lifted, from the database and from the session alike, when the dialog is accepted.

- **The tabs say `KPG` and the menu item says `KeePassGate (KPG)`**, and which form goes where is
  about space rather than style. A tab strip is not ours: KeePass puts four or five of its own tabs
  on those dialogs and any other plugin may add one, so every character spent there is width taken
  from everybody, and the icon does the work a long caption was doing. A menu item is a line of its
  own, costs nobody anything, and is the only place that can teach the abbreviation. `GateName` has
  all three forms and every caption reads one of them.

- **The plugin's own name carries KeePass's icon 29, `TerminalEncrypted`**, on the Tools menu item
  and on all three tabs. Read off `MainWindow.ClientIcons` rather than shipped as a resource, so it
  matches the window it is in and follows a KeePass that restyles its icons, and copied every time
  because each copy is handed to a menu or an image list that disposes it. **A tab strip shows
  pictures only if the whole control has an `ImageList`**, and none of the three dialogs brings one,
  so `TabPageInjector` gives it one and takes it away again on detach: that makes every tab on
  those dialogs a little taller, which WinForms offers no way around. `GateIconTests` pins both the
  icon's identity and the fact that KeePass brings no list of its own, because a KeePass that
  started putting icons on its own tabs would have us appending to somebody else's list.

- **What is about the plugin is not on the KPG tab.** The listening socket and the port live
  on the window `Plugin.GetMenuItem` puts under Tools, because that answer is the same whichever
  database is open and is wanted when none is. The tab carries what is about the database it is on,
  and nothing else. **The version is not shown on either**, which is a deletion rather than an
  oversight: KeePass's own plugins dialog lists every plugin with its version, which is where
  somebody checking one looks and where the answer is right for all of them at once. The menu item
  is given a mnemonic of its own: `K`, since KeePass spends g, l, t, d, a, r, p and o on that
  menu's own items.

- **The screenshots in `docs/` are a build output.** `build.ps1 -Task Screenshots` drives a
  throwaway KeePass through the whole operator flow and screenshots each step where it happens,
  writing into `docs/images`. It is in the `Live` category so ordinary runs exclude it, and in a
  `Screenshots` category so the end to end run excludes it too, because it writes into the working
  tree. The output directory arrives in an environment variable, so a fixture nobody asked to run
  rewrites nothing. **What is captured is the whole window**, frame, banner and buttons, because a
  tab strip cut out of a dialog is a picture of a page with no edges: a reader cannot see what it
  sits in, where the room runs out or which button ends it. What makes that publishable is the
  session, not the crop: the database settings dialog carries the open database's path in its
  banner, so `LiveSession` builds the session where that path names nobody.
  `build.ps1 -Task Lint` refuses a picture no page refers to and a reference to a picture that is
  not there; that a picture is named in `DocumentationImages` is checked by the fixture that takes
  it, which is in the `Screenshots` category and so runs only when the pictures are retaken.
  **One picture moves, and it is a build output too**: `lifecycle.gif`, taken last in the same run.
  It is a click journey somebody can follow, from the database the fixture filled, entries in two
  folders and nothing else: every dialog opened from the menu bar, the authority created, an
  invitation copied and pasted at the client's prompt, the pairing approved, a rule on the top
  group and a seal on one folder written on the group dialogs, then PyKeePassGate's real command
  line reading what the rules allow and refused what they do not, revoked from the clients page
  and refused again. Create authority is the one press drawn rather than made: the pictures after
  it are of the written down authority, for the reason the stills have one. The frames are then
  composed from a fixed timeline, not from the clock: the pictures it took, the rectangles of the
  controls it pressed, and the text the client typed and printed, with the terminal drawn beside
  the windows, typed a character at a time, a pointer and its clicks drawn where the real presses
  landed, and the keys pressed drawn on caps. `AnimatedGif` writes them, each frame only the
  rectangle that changed, so the same run makes the same file. What the client makes new every
  run, its key, its certificate's expiry and the directory it stored them in, is shown in the
  terminal as written down values, and the moments on its row of the clients page are covered.
- **Nothing on a tab scrolls.** The content is held to 449 by 314 and has to fit it; horizontal
  scrolling is never acceptable. That is KeePass's own first page on the settings dialog, inside a
  padding of 3. The page this plugin adds has no padding and is laid out to the tab control's whole
  display area, a few pixels more even after the icon's share of the strip, and `TabRoom` holds a
  fit test to the smaller of the two, so the tests are strict by that padding on purpose.
  `GateTabFitsTests` measures it against the real dialog, including the longest text every label
  can hold at once. **The KPG tab has three pages of its own**, "Certificate Authority",
  "Profiles" and "Clients", because subjects competing for one page is what makes a tab that has
  to be shortened rather than read. Each page is measured in the room that page is given, not all
  of them in the sum, which is the whole point of there being three. A
  `TabControl` with no window handle never sizes its pages, so the measurement creates one and
  brings each page to the front before believing what it is told.
- **The grant editor writes into the dialog's own copy**, `m_sdCustomData`, on the group dialog and
  the entry dialog alike, which is what makes those dialogs' OK keep a rule and their Cancel discard
  it. The copy does not exist yet when the tab is built, so the field is resolved on every access
  and reading falls back to the node itself. A KeePass that renames it leaves the editor showing the
  rules and refusing to change them.
- **No third-party HTTP dependency.** `uHttpSharp` fits the seam natively with zero changes and is
  ruled out on **license** (LGPL-2.1) and liveness (no commits since 2021), not on fit. Wanting
  WebSocket, HTTP/2 or chunked bodies flips this; nothing else does.
- **The TLS is Bouncy Castle, not SChannel**, and that reverses an earlier decision on a
  measurement: SChannel will not serve with a key that has not been written to the user's profile,
  so the platform's own TLS meant a host key on the disk. `Org.BouncyCastle.Tls` is MIT, alive, and
  parses the stored PKCS#12 itself. Do not re-propose `SslStream` for the server without measuring
  `EphemeralKeyProbe.cs` on `spikes/tls-and-certificates` again first.
- **Spec-first routing.** The OpenAPI document at `src/KeePassGate/Spec/openapi.yaml` is the
  routing table, embedded in the assembly, and handlers bind to `operationId`, so an undeclared path
  has nowhere to go. Every declared operation must have a handler and every handler a declared
  operation, asserted when the router is built, which is as the plugin loads.
- **No schema library checks request bodies**, and that is a count rather than a preference:
  fourteen assemblies against three for reading the document. `BodyCheck` covers the subset this
  wire carries and **refuses at startup any schema it does not implement**, a path or query
  parameter's as well as a body's, holding every keyword to a list of what its type may say, which
  is what stops a subset becoming a gap. Re-proposing one means arguing with the counts on
  `spikes/json-reading`.
- **The JSON grammar is `Newtonsoft.Json`'s and the policy is ours**, which is the split worth
  keeping. `JsonBody` decides what this wire may carry, which is one flat object of strings and
  arrays of strings, with no NUL anywhere in it and no string a `.kdbx` would not give back:
  nothing XML 1.0 cannot represent, and nothing that is whitespace alone. `JsonWriter` shapes what a
  handler writes, and neither owns a parser. Three
  alternatives were measured and refused, on assembly count and on license. The counts are on
  `spikes/json-reading`, and re-proposing one means arguing with them.

  **And nothing here escapes a JSON string.** A response, an audit line, an ACL document, a
  profile list, a pairing bundle: every value this plugin writes as one goes out through that
  library, because two implementations of escaping can only ever be found to disagree by
  something a caller sent. There was a second one, forty lines on `HttpResponse`, written
  before anything referenced could do it and deleted the moment something could.
- **Every GET declares the HEAD that answers for it**, bound to the same handler and carrying
  the same `x-kpg-verb`. Declared rather than derived, so the document stays the whole account
  of what the API answers; one handler, so the two cannot come to disagree about whether
  something is there. The plugin refuses to load a `get` with no `head`, or a pair that differs in
  anything that decides who may ask or what is asked: the verb, the reach, the certificate, the
  body, the query parameters, `If-Match` or the guard. The answer omits `Content-Length` rather
  than sending a wrong one, which RFC 9110 section 9.3.2 permits.
- **A node's `ETag` is a keyed digest of what it holds, never a clock and never a plain hash.**
  `NodeTags` takes an HMAC-SHA256 over exactly what the routes serve, as they serve it: an entry's
  identifier, its group, its two times to the second, every string with its name, whether it is
  protected and its value as a read decodes it, every attachment's name, size and, up to
  `VaultAttachment.MostServed`, the SHA-256 of its bytes, and its expiry; a group's identifier, its
  parent, its two times, its name and its notes. Rules stay out. A clock on its own was measured
  and refused, in `FINDINGS.md` under "What a version of a node can be made of": it misses two
  changes inside one step, a merge at an equal time, a move and a plugin that never calls `Touch`.
  An entry has one tag for itself, its fields and its attachments, as the rules answer for all three
  by the entry. **The tag is taken before what it is served with**, so a change landing in the
  middle of a read leaves a client an older tag beside newer content, which a write then fails over,
  rather than the other way round, which a write would have carried over the change. Attachment
  digests are kept per `ProtectedBinary` in a `ConditionalWeakTable` of `Lazy` values, so two
  readers decrypt one once between them, on the strength of KeePass never changing one in place,
  which `KeePassContractTests` pins. A lookup takes no tag: `IVault.EntryTag` and `GroupTag` are
  asked only by a route that will hand one out.

  **The key is derived from the host certificate's stored PKCS#12**, `NodeTags.KeyFrom`, never the
  gate key, which every paired client holds. So a tag is the same across a lock, a restart and a
  reload, and changes only when the authority is recreated. **This reverses a key drawn when the
  plugin loaded**, which changed every tag whenever KeePass started, for nothing anybody wrote.

  **A tag is handed only to a client whose one rule may read everything it covers**: `read` on a
  group; on an entry `read` reaching attachments, and protected values too when it holds any,
  `AclResolver.ReadsWhole`. Anybody else gets no tag, and any `If-Match` it sends fails. **This
  reverses handing every caller the tag**, which a review measured as a way to recover a password a
  client may update and not read, by writing guesses and comparing the tags that came back, `API.md`
  §410. A key stops a tag being computed; only not handing it out stops it being compared.

  **`If-Match` is optional, at the operator's word**, so a request without one is served as it
  always was, and one whose node no longer holds what the tag says is `412`, `API.md` §410. It is
  declared on exactly the operations whose target has a tag, reads among them, and the router hands
  it on only for those; `ApiTests` sweeps every one of them by `operationId` against the document,
  so a document declaring it somewhere a handler does not ask fails. A write's is asked inside the
  change, under the writer's lock, after every refusal that does not depend on it, as RFC 9110
  section 13.2.1 orders them, and one that cannot be read matches nothing, because every way of
  ignoring it ends with the write going ahead. A write answers with its tag in the body, as
  `etag`, since RFC 9110 section 9.3.4 gives an `ETag` header on a `PUT` another meaning.
- **A plain DLL on .NET Framework 4.8**, not a `.plgx`.
- **The access control model is one document per node**: `AclDocument`, `AclResolver`, `AclGrant`,
  `AclVerb`, `AclDefault`, with `KdbxNodes` adapting a `PwGroup` or a `PwEntry`. Grants live under
  a `KPG` key, which is this plugin's own namespace and touches nothing another plugin owns. The
  document is one flat object of strings and arrays of strings, which is what `JsonBody` already
  reads, so no parser arrives with the ACL.

  **`protected` is a scope and not a verb**, like `attachments`, and it is the one scope a handler
  asks about rather than the router. An attachment is a route of its own, so the router asks before
  dispatch; a protected value arrives inside the answer to a route that is allowed, so the question
  is asked once between the handler and the response. It governs writing as well as reading, and
  what counts as protected is `IVault.Protects`, which is KeePass's own `MemoryProtection` rather
  than a list kept here. **Both sides ask it now**: a read used to trust the stored string's own
  flag, so a standard field the database protects and the string did not was handed out as though
  nothing protected it.

  **Nearest wins, per profile**, and profiles are combined with or. An empty array is a rule
  granting nothing from there down; a profile the document does not name is silence, answered by
  `default`. A document that will not read refuses where it is found rather than being skipped,
  because walking past it would answer with a permission it may have been taking away.

  **There is no per-subject opt-in, and adding one would be a defect.** The ACL guards every route
  there is, because there is one API generation and no legacy to exempt. A setting that let a
  subject out of the ACL could only ever be wrong here, however sensible it looks in a design that
  has something older to carry.

- **The API is generated by two rules**: the verb is the operation's own name, and the node is the
  nearest addressable thing, so a field or an attachment is answered for by the entry holding it.
  `root` is a group identifier, which is what stops `create` from implying `list`. A flat sweep is an
  operation on the root group. A node that is refused and one that is absent answer the same 404,
  because telling them apart would let a client map a database by asking. See
  [`API.md`](API.md) §401.
- **PyKeePassGate is the reference client, and reference means load bearing.** It lives at
  `src/pykeepassgate/`, imports as `pykeepassgate`, and is split into `pykeepassgate.api`, the
  library, and `pykeepassgate.cli`, a command line that can do nothing the library cannot, with
  `pykeepassgate.testing` beside them, a scripted stand-in gate for testing a program built on the
  library, which is not a model of the rules, and `pykeepassgate.refs`, which follows field
  references as KeePass does under the caller's own rules, in a module of its own so that the
  client stays one method per route. A route the gate serves and this does not cover is a
  gap, and a method here with no route behind it is a lie. Its `pyproject.toml` is at the
  repository root because there is one Python project.
- **The client's protocol code is pinned against the plugin's own output.**
  `tests/pykeepassgate/test_protocol.py` holds vectors produced by `PairingToken`, `SniName` and
  `PairingProof` over inputs written down beside them. Without those, both halves could agree on
  something neither document describes, because both halves are code written here. Regenerate them
  from the plugin rather than from the client if they ever have to change. `ProtocolVectorsTests`
  holds the plugin to the same values, so a change to either half fails in that half's own tier.
  **The resolver is pinned against KeePass's own compiler the same way**:
  `tests/Shared/ReferenceVectors.json` is what `SprEngine` makes of every case, written by
  `ReferenceVectorsTests` in the host tier and read by `test_refs.py`, and it is regenerated from
  KeePass, never from the client. **What it follows is KeePass's order, and what it cannot be sure
  of it refuses**: a search other than by identifier, every placeholder beyond the entry's own
  strings, and a placeholder that matches a name only once letters beyond ASCII lose their case.
- **The client cannot be a `pykeepass` backend, and the reason is the design.** A `PyKeePass` is a
  decrypted `.kdbx`, and this side of the API has neither the file nor the master password on
  purpose. Its `save` writes the file it was opened from, so an edit to a synthesized one would
  change a copy while the real database carried on and the ACL never saw the write.

  **The read model that replaces it has two rules**: no `save`, because a method that looks like
  theirs and does something else is worse than no method, and absent rather than empty, so a
  missing feature is an attribute that is not there rather than a blank value. `model.py` follows
  both, and a third that comes of the second: **withheld is not empty either**, so a protected value
  the caller's rule does not open raises `Withheld` from every way of reading it, never `None` or
  an empty string, while its name stays where the gate put it.

  Measured on `spikes/pykeepass-compatibility`, taken from `master` and never merged, and remeasured
  on 2026-10-25 over the four classes a program holds, `PyKeePass`, `Entry`, `Group` and
  `Attachment`: of their 122 public members, 65 are servable over the routes the gate already has,
  4 are served under a name that means something else there, the deletes, which go to the recycle
  bin where `pykeepass`'s are for good, 9 would need routes nothing forbids, among them an
  entry's tags and history, and 44 cannot exist here, the database's name and description among
  them. The
  first measurement counted `PyKeePass` alone, the deletes as served and `trash_*` and `deref` as
  missing, and its `RESULTS.md` records both, with the per member table and what would have to
  change on each side.

## Measured facts that cost time if rediscovered

Every one of these was established by running code, and most contradict what the documentation
suggests. The code is on branches of its own: `spikes/tls-and-certificates` holds the nine that
measured TLS and X.509, `spikes/build-environment` the one that measured whether the gate can
fail while nothing is wrong, `spikes/revocation-merge` the one that measured how KeePass merges
a database's custom data, `spikes/entity-tags` the one that measured what a node's version
could be made of, and `spikes/automation-unload` the one that measured why a domain that drove
KeePass through UI automation cannot unload. Each gathers what it settled in its own `RESULTS.md`.
Run them from
**Windows PowerShell**, not PowerShell 7, which is .NET Core and answers a different question.

- **`X509Chain` does not pin an anchor.** `ExtraStore` plus `AllowUnknownCertificateAuthority`
  accepts a certificate from a completely foreign authority. `X509ChainTrustMode.CustomRootTrust`
  would fix it and is .NET 5 and later. **Byte-compare the chain's last element** and require exactly
  two elements. `ChainProbe`.
- **`X509Chain` goes out on the network for a certificate somebody else chose.** When the issuer is
  not in `ExtraStore`, CryptoAPI follows the `caIssuers` URLs written into the leaf before `Build`
  returns, so a peer names the address and the process fetches it. Measured at 15.0 seconds for one
  unanswered address, on a connection thread, with the request really arriving.
  `X509ChainPolicy.DisableCertificateDownloads` would forbid it and is .NET 5 and later, so on this
  framework `UrlRetrievalTimeout` is the whole of the vocabulary: zero means the platform's fifteen
  seconds, and a millisecond takes away the wait and the answer rather than the first packet.
  `AnchorPinTests.A_foreign_leaf_that_names_a_download_is_refused_without_waiting_for_it`.
- **`ClientCertificateRequired = false` is what breaks optional client certificates.** Under TLS 1.3
  the server then sends no `CertificateRequest` at all, so a client holding a good certificate never
  offers one. Under TLS 1.2 it does, so testing on 1.2 and shipping on 1.3 is a silent
  authentication bypass. And `= true` requires nothing, since a certificate-less client completes the
  handshake anyway. `OptionalClientCertProbe`.
- **`SslPolicyErrors.None` is the wrong test.** A correct deployment always reports
  `RemoteCertificateChainErrors` (the authority is in no machine store) and
  `RemoteCertificateNameMismatch` (the SNI rotates). The callback ignores it entirely.
- **`X509Certificate2.Thumbprint` is SHA-1**, 40 hex characters. Admission keys on SHA-256 over
  `RawData`. `ChainProbe` prints it.
- **`new X509Certificate2(byte[])` accepts a PKCS#12, not just a certificate.** The constructor
  sniffs the blob, so 2350 bytes of password-less PFX load happily, report `HasPrivateKey` true, and
  answer `RawData` with the 676 bytes of the certificate inside. Anything keyed on the digest of
  what it stored therefore has to take the digest of `RawData` after parsing and refuse a
  disagreement, or a record admits a certificate whose stored bytes nobody ever checked and quietly
  carries a private key besides. `AdmissionRecord` does exactly that; measured by feeding a PFX to
  the byte constructor on .NET Framework 4.8 and printing all three numbers.
- **`X509Certificate2.NotAfter` cannot hold a certificate that never expires.** RFC 5280 writes
  "no well-defined expiration" as `99991231235959Z`, and `CertificateRequest` encodes it exactly
  so. Read back, `NotAfter` converts to local time, and ahead of UTC that moment falls in the year
  10000, which no `DateTime` holds: what comes back is `DateTime.MaxValue`, which converted back to
  UTC is up to fourteen hours early, and nothing throws. Measured in this machine's own zone as
  well as in simulated ones. The client's half is the same fact: `cryptography` reads the value
  exactly, and moving that `datetime` into a zone ahead of UTC raises `OverflowError`. A hundred
  years, the fallback the operator named, is read exactly by every reader on the path in every
  zone tried. `LongLivedCertificateTests` on this side, and
  `test_an_authority_and_a_host_good_for_a_hundred_years_are_accepted` on the client's.
- **A KeePass merge of `PwDatabase.CustomData` visits the source's keys and no others, and never
  reads a value.** `PwDatabase.MergeInDbProperties` is the whole of it, run at the end of every
  `MergeIn`, which is File then Synchronize and File then Import alike. So no method removes a key
  the target holds, and a removal travels nowhere. For a key both sides hold, the two modification
  times, the method and `SettingsChanged` decide: `KeepExisting` copies nothing; `OverwriteExisting`
  takes every source key with the source's time, or with none where it has none; `Synchronize`,
  `OverwriteIfNewer` and `CreateNewUuids` take the source only when its time is strictly newer or
  only it has one, and between two keys without times only when the target lacks the key or the
  source's settings are newer. A key that is only ever added and never changed therefore survives
  every KeePass merge, whatever the clocks say and whether or not a program that drops the times
  has saved either copy. `CustomDataMergeRuleTests` pins that against the pinned KeePass, and
  `spikes/revocation-merge` holds the probe that measured every method in both directions and with
  the times lost on either side.
- **Never parse a SAN with `X509Extension.Format` or `GetNameInfo`.** `AddDnsName` accepts text that
  renders as a URI claim, the labels are localized, and `GetNameInfo` returns one entry. Read the
  ASN.1 tags: `0x86` URI, `0x82` dNSName, `0x81` rfc822Name. `SanProbe`. **The plugin reads
  none**: it writes the profile claims and the responder marker, and the marker is for the peer to
  read, `CA.md` §208. Its tests read what the authority wrote through Bouncy Castle's
  `GeneralNames`, which is here for the keys and the TLS.
- **There is no RFC 5705 exporter** on .NET Framework, but
  `TransportContext.GetChannelBinding(ChannelBindingKind.Endpoint)` works on both sides under TLS 1.3
  and both agree. Take the application-data portion by its documented offsets, not the whole blob.
  `ChannelBindingKind.Unique` returns something under TLS 1.3 where tls-unique is undefined; ignore
  it. `BindingProbe`.
- **The SNI needs two labels.** 64 hex characters exceed the 63-octet DNS label limit.
- **Bouncy Castle turns whatever a server callback throws into a fatal alert.** Its record layer
  catches every exception on the way through and raises a `TlsFatalAlert` made from it, so a defect
  in the plugin's own handshake code reaches the connection thread looking exactly like a peer
  whose handshake failed, and was logged at `Info` as one. `GateTlsServer` keeps the first
  exception its callbacks throw, in `Failure`, and the listener counts a handshake that failed with
  one as an incident.
  `GateListenerTests.A_handshake_this_side_fails_is_counted_rather_than_put_down_to_the_peer`.
- **A `TcpClient` closed before its stream is taken answers `Client` with null.** `Dispose` clears
  it on .NET Framework 4.8, so the timeout setters then throw `NullReferenceException` and
  `GetStream` reads `Client` again: a lock closing an accepted connection before its thread's first
  line was counted as a defect. `GateListener.Opened` reads the socket once, and null is a
  connection that ended.
  `GateListenerTests.A_connection_closed_before_its_thread_starts_is_one_that_ended`.
- **Under TLS 1.3 a refused handshake's alert can lose the race to the reset behind it.** The gate
  refuses and closes with the client's request unread, so the client's first read after the
  handshake meets either the alert or the reset: a client half a second late to read lost the
  alert five times in five. A test that asks why a handshake was refused reads the gate's log,
  never what the client read, which is how the tests over `BouncyCastleGateClient` ask it.
- **A locked document's close carries no path, and the document is what still knows it.** The lock
  ran `PwDatabase.Close` already, which resets the connection, so both `FileClosingPre` and
  `FileClosed` announce that document with an empty path. `PwDocument.LockedIoc` is where KeePass
  moved the file it was locked from, reachable through `MainForm.DocumentManager.FindDocument`.
  Anything that fills the gap from a single remembered string instead names whichever database last
  closed with a name. `MainWindowDatabasesTests` drives the four handlers in KeePass's own order.
- **KeePass tells two database paths apart by lowering them in the current culture**,
  `Trim().ToLower()`, so under a Turkish culture `I` and `i` are different letters and two paths
  differing only there are two databases. A path matched against one of KeePass's has to be
  compared the same way, which is `KeePassPaths`, taking the culture of KeePass's own thread once;
  `OrdinalIgnoreCase` would join what KeePass keeps apart.
  `LockInRealDatabaseTests.Two_spellings_KeePass_keeps_apart_are_kept_apart_here_too`, under tr-TR.
- **`MainForm.SaveDatabase(db, null)` saves a named database**; `UIFileSave` saves whichever document
  is active. Pairing names its database, so it uses the first. Neither reports anything, so a write
  is believed only after reading the record back and finding `PwDatabase.Modified` false. **And
  `Modified` is not enough on its own**, see "KeePass's save reports nothing to its caller" below:
  the file is hashed back as well.
- **KeePass's own two busy signals say nothing about an ordinary dialog.**
  `MainForm.UIIsInteractionBlocked` is raised only through `UIBlockInteraction`, whose callers in
  2.61.1 are save, save as, opening a database, search, duplicate entry removal, the status dialog
  and logger, the shell command action and file search. `MessageService.CurrentMessageCount` moves
  only for message boxes, task dialogs, triggers, `PerformExport` and the secure desktop. An open
  `DatabaseSettingsForm`, or any plain dialog, raises neither: read off the IL, and a host probe
  holding the settings dialog open read a count of zero. So the writer's refusal of a write inside
  an operation KeePass is running covers no ordinary dialog, and the dialogs that write back are
  covered by `GroupDialogs`, one at a time. `KeePassContractTests` pins both calls, in
  `The_main_window_still_says_when_it_has_blocked_itself` and
  `An_export_still_shows_in_the_count_of_windows_KeePass_is_waiting_on`. **Nor do they say anything
  about XML Replace**, under Tools then Database Tools, which merges into the live database with
  KeePass's progress window as its status logger, pumping on every step, and raises neither: a
  group created under the root there makes the merge throw out of `ApplyDeletions`, after the
  write's own save. So there is a third signal, the plugin's own: `GroupDialogs.Working` answers
  whether KeePass's `StatusProgressForm` is open, for any database, and the writer asks it after
  the two. `VaultWritesUnderDialogsTests` drives the real window and the real merge.
- **A `ToolStripDropDown` must not be disposed inside its own `Closed` handler.** That event is
  raised while WinForms is still delivering the click or keystroke that closed it, and disposing
  there disposes the control whose event is running: the next message reaches
  `ToolStripDropDown.SetVisibleCore` through `ToolStripManager.ModalMenuFilter` and throws
  `ObjectDisposedException` **into the message loop**, where no handler on the page can see it and
  KeePass answers by closing the dialog. Rebuilding the grid underneath and restoring the dialog's
  `AcceptButton` have the same problem for the same reason. `GrantPanel.Finish` defers all three
  with `BeginInvoke`, and `GrantPickerTests` reproduces the crash on a real shown form: revert the
  deferral and the test host dies rather than the assertion failing.

- **A menu holding nothing but its items owns Enter and Escape while it is open**, which a dropdown
  hosting a list does not. Enter clicks the highlighted item, or does nothing when none is, Escape
  closes it, and the dialog's accept button and its own `ProcessDialogKey` see neither. Its
  `ItemClicked` is raised before its `Closing` and `Closed`, and the item's own `Click` after them,
  and a press outside it closes it and is then delivered to whatever it landed on. **And it must
  not be closed by code before its window goes**: the next window's menu then closed the moment it
  opened, as an `AppFocusChange`. `SplitButton` leans on all of it, and `SplitButtonTests` drives
  the first three through a real message loop. The fourth is a rule rather than a test: nothing in
  `SplitButton` closes its menu by code, and the fixture closes every menu it opened with Escape.

- **A control with an `AccessibleName` reports that to UI automation as its name**, in place of
  its text. That is why the desktop tests point at a control by its `AutomationId`, which comes
  from `Name` and is ours: with the lookup keyed on the identifier, `AccessibleName` is free to
  be the phrase a screen reader should say, and is set only where a control has no text of its
  own to say it. Keying the lookup on the name instead is what made the two collide.
- **A caret cannot be hidden from another process, and both calls say nothing about it.**
  `HideCaret` hides a caret only if a window in the **calling process** owns it, and `DestroyCaret`
  destroys one only under the same condition; `AttachThreadInput` shares an input queue rather than
  ownership, so it does not bridge that. Both return as though they had worked. What does work is
  moving the focus to a control that draws no caret, which UI automation can do across the
  boundary. Do not ask the window which control that is: a `NumericUpDown` publishes no editable
  child, so its row answers as a focused `Pane`, while a grid cell does publish as a focused `Edit`
  and would be moved off a picture that was already steady. The caller names the control.
- **UI automation's managed client leaves a thread no AppDomain can unload.** The first walk into
  a window's controls loads the client-side providers into the domain that asks, and their
  `WinEventTracker` starts a queue on a background thread waiting in native code with no timeout
  and no way to be told to stop, so `AppDomain.Unload` gives up on it after fifteen seconds. NUnit
  unloads the test assembly's domain at the end of a run, which is why every end to end run ended
  with "Exception encountered unloading application domain". `ClientSideProviderQueue` ends the
  thread once the last fixture in the `Live` namespace has finished, through the queue's own way
  in, and `ClientSideProviderQueueTests` pins the framework's private names it reaches for, in the
  host tier. An empty set of client-side providers does not prevent it, and was measured not to;
  `RunConfiguration.DisableAppDomain` does, and costs `GrantTabFitsTests` its themed child domain
  for the whole assembly or the end to end runs the test host's binding redirects for them alone.
  `spikes/automation-unload` holds the probe and the numbers.
- **`WM_SETTEXT` reaches a `TextBox` and not a `Label`.** A label paints from its managed `Text` and
  never reads the window's, so writing to one from outside changes nothing and reports success.
  Three of `Steadied`'s four substitutions were labels and had never once taken effect; what kept
  those pictures reproducible was the authority being written down rather than the writing. Settle
  a value at its source, and where that is impossible cover it with `Redacted` rather than trying
  to type over it.
- **A single line `TextBox` never raises `KeyDown` for Enter on a form with an `AcceptButton`.**
  `IsInputKey` is false for it, so `PreProcessMessage` hands the key to `Form.ProcessDialogKey`,
  which clicks the accept button and swallows the message: the box's own handler never runs, and
  `Handled` and `SuppressKeyPress` inside it are never consulted. A control says the key is its own
  through `PreviewKeyDown` and `IsInputKey`, which is the only thing that works here;
  `SuppressKeyPress` takes the beep away and nothing else. Measured on KeePass's own
  `DatabaseSettingsForm`, which does set an accept button and overrides none of the preprocessing.
  `ProfileListTests.Enter_on_a_dialog_with_an_accept_button_adds_the_name_rather_than_pressing_it`
  posts the key through a real message loop, which is the only way to see this: a test that calls
  the handler directly passes either way.
- **KeePass's dialogs scale, and by more than the text does.** They are `AutoScaleMode.Font` against
  `AutoScaleDimensions` of 6 by 13, so a larger system font makes a larger dialog: the settings
  dialog's own first page goes from 449 by 314 at font height 13 to 599 by 410 at 16, 674 by 482 at
  19 and 974 by 628 at 26. A page that fits at one scaling therefore fits at all of them, and a test
  that grows the text while holding the room is measuring something that cannot happen. What does
  happen is a window of ours that does not scale while its contents do, which is what a fixed
  `ClientSize` with no `AutoScaleMode` buys, and a page put on a dialog after the dialog has
  scaled, which nothing scales unless told to: `TabPageInjector` scales each one by its dialog's
  own factor as it attaches it. `GateTabFitsTests.Everything_still_fits_when_the_display_is_scaled`.
- **`Button.PerformClick()` does nothing on a form that was never shown**, silently. What a button
  does is public on the form, as it is on the tab, so tests reach the decision rather than the
  control.
- **A control made visible before its parent has a window gets a window of its own at once**, and
  when the parent's arrives WinForms moves the control in `Controls` to where its window lands,
  straight after the last sibling that already had one. Docking and flow order read `Controls`, so
  they change with nothing in the code having asked: the two lines under the clients list swapped
  places that way. `ClientList.OnLayout` puts its children back before docking reads them.
- **GDI+ answers a font family it does not have with Microsoft Sans Serif**, and says nothing. A
  font asked for by name is checked by name afterwards, which is how `AuthorityPanel.CodeFont`
  settles on Consolas where it is installed and a monospaced family where it is not.
- **`ToolTip` reads a tip right to left whenever the top-level form does**, whatever the control
  under the pointer says, so a tip on a panel pinned left to right is still drawn right to left
  under a right-to-left translation. `Tooltips.Wrapped` marks both ends of every line left to
  right, and `TooltipsTests` pins the flag and the drawing.
- **KeePass compares placeholders without case in .NET Framework's terms, which are not Python's.**
  `OrdinalIgnoreCase` and `char.ToUpperInvariant` leave the Turkish i with no dot, U+0131, and the
  long s, U+017F, as letters of their own, so `{TITLE}` spelled with the first is no placeholder to
  KeePass and a reference searching by it is no reference, while Python's `str.upper` turns both
  into ASCII and would have followed them. A `ü` and a `Ü` are one letter to both. So
  `pykeepassgate.refs` compares ASCII without case and refuses a placeholder that matches a name only
  beyond it, rather than guess at .NET Framework's tables. `ReferenceVectors.json` holds the cases,
  made by KeePass itself.
- **KeePass's modification time is not a version.** `Touch` stamps `DateTime.UtcNow`, which steps
  every 0.5 to 1.5 milliseconds here, so a thousand changes back to back shared their times in
  every pair; a KDBX 4 file keeps whole seconds, so every time moves across a reload; a synchronize
  takes the other copy's strings at an equal time and leaves the time alone; and a move stamps only
  `LocationChanged`. `FINDINGS.md` has the table, `NodeTagsTests` drives the merge and the
  reload, and `spikes/entity-tags` holds the probes.
- **CodeQL takes some comments for suppressions that do not look like one, and ignores some that
  do.** A Python comment that is `noqa` with no codes after a colon suppresses every rule on its
  line, and an `lgtm` at the start of a comment or after a `;` every rule on its line and the next.
  A rule named in another case, a list of two in one bracket, a marker after code on its line, and
  one in a `/* */` over several lines suppress nothing, while spaces inside the brackets are trimmed
  and the keyword is read in any case. Measured against the pinned bundle by
  `tools/suppression_vectors.py`, which `tests/tools/SuppressionVectors.json` records.
- **GitHub refuses a pull request review of more than 80 comments, and answers 502 to a large one
  it goes on making.** A review of 81 is refused at once as a secondary rate limit, with no
  `retry-after` and nothing made, which is the documented 80 content-generating requests a minute
  with each comment counted as one. GitHub takes a quarter to a third of a second a comment, and a
  review it is still making after about ten seconds is answered 502 and finished all the same, so
  a 5xx leaves open whether a review landed. `tools/suppression_review.py` posts twenty to a review,
  twenty seconds apart, and reads back what a 5xx left; `spikes/codeql-suppressions` measured it.
- **GitHub takes a review thread only on a line its own diff shows, and that diff is GitHub's.** A
  renamed file is listed with its old name and only its changed hunks, a rename that changes
  nothing takes no thread on any line, and a name git quotes comes back plain. A file of 25,000 new
  lines refuses every line with 422, "diff is too large", while one of 5,000 takes its lines though
  GitHub's list of the files gives it no diff either. A comment on the whole file is taken where
  no line is, and is a thread the merge waits for like any other. So `tools/suppression_review.py`
  reads what a pull request changes from GitHub's list and puts a note on the whole file where no
  line takes it; `spikes/codeql-suppressions` measured all of it.
- **The workflow's own token may delete a review thread it opened, and may not resolve one.** Under
  the review job's permissions, GraphQL's `resolveReviewThread` answers 200 with a `FORBIDDEN`
  error, "Resource not accessible by integration", and resolves nothing, whether the thread is
  outdated, on a line nothing changed or on a whole file; with `contents: write` added it resolves
  all three. Deleting a thread's only comment, `DELETE /pulls/comments/{id}`, is answered 204 under
  the same permissions, and the thread goes with it. A push that takes a thread's lines away
  leaves it outdated and unresolved, and the ruleset wants every comment resolved. So
  `tools/suppression_review.py` deletes its own threads whose suppression is gone rather than ask
  for a token that may write to the repository; `spikes/codeql-suppressions` measured it.
- **`dotnet test` with a filter that matches nothing exits 0**, having run nothing, and its trx
  counts nothing. A gate that believes the exit code is green over an empty run, so `build.ps1`
  reads each tier's trx and refuses one in which nothing executed, `Assert-Executed`.
- **Windows PowerShell 5.1 passes an argument ending in a backslash so that the backslash escapes
  the closing quote.** `-p:KeePassDevDir=C:\Program Files\KeePass\` reaches `dotnet` as one
  argument that swallows the `--nologo` after it. `Resolve-KeePass` trims a trailing separator for
  that reason, keeping it only on a drive root.
- **`ContinuousIntegrationBuild` maps nothing outside a git checkout.** The SDK builds the `/_/`
  path map from the source roots its git support reads out of `.git`, so in a source download or a
  `git archive` the switch is set and every full path is compiled into the plugin and its symbols.
  `build.ps1 -Task Package` asks git for the top of the repository first and refuses anything else,
  and refuses a tree with uncommitted changes, since the plugin reports HEAD's commit as the one it
  was built from.
- **C# 13 is available, and the packaging is why.** A `.plgx` would be recompiled by KeePass's
  in-box CodeDOM provider, which caps the language at C# 5. This ships a plain DLL and escapes that
  entirely; the probes are conservative only because `Add-Type` compiles them. A feature that needs
  the runtime's help, such as a default interface method, still does not compile for `net48`.
- **NUnit 4.6 needs C# 13, and neither its package nor the compiler names it.** It adds an
  `Action` overload beside every `TestDelegate` one, on `Assert.Throws`, `DoesNotThrow`, `Catch`
  and `That`, and marks the new ones `OverloadResolutionPriority(1)`. A language before 13 ignores
  the attribute, so a lambda matches both and every call is `CS0121`.

### The trap that shaped the design

**Two dialogs take a copy of custom data and write it back, and the second is easier to miss.**

**`GroupForm` takes a copy of a group's `CustomData` when it opens and writes that copy back on OK.**
A value written to the live group therefore **survives Cancel and is destroyed by OK**, silently and
completely.

Two consequences here. The admission record is checked on every connection precisely so that losing
one to a stale snapshot makes the certificate inert rather than invisible and irrevocable. And
**testing any `CustomData` work means pressing OK, not just looking**: make the change, press OK,
reopen the dialog, and only then believe it. Do the same after saving and reloading the file, because
those are two different failures.

**`DatabaseOperationsForm`, under Tools then Database Maintenance, does the same to
`PwDatabase.CustomData`**, which is where this plugin's own keys now live. Worse in two ways than
the group dialog. Its write-back is conditional on the key count differing rather than on anything
the operator did, so a pairing landing while it is open is destroyed with the Plugin Data tab never
opened. And its one button is both the accept and the cancel button, with `DialogResult.Cancel`, so
there is no way to decline it. `GroupDialogs` watches for it and `DatabaseWriter` refuses while it
is open.

**A dialog that is adding something is about a group it does not hold**, which a watch keyed on the
node a dialog holds cannot see. Add Entry and the TAN wizard put what they made into their group
only on OK, so that group is empty while they are open and an API removal would pass; Add Group puts
its group into the tree before it opens and takes it back out on Cancel, with whatever was created
in it. So `GroupDialogs` also notes the group each of them will fill or may take away, and the vault
answers `503 busy` to a removal of a group Add Entry or the wizard will fill, and to anything created
in a group whose Add Group dialog is still open. Edit Group holds nothing about what is inside, so
creating under it stays allowed.

Equally load bearing: **KeePass's save reports nothing to its caller.** `MainForm.SaveDatabase` is
`void`, shows its own warnings, and leaves the caller no way to ask whether anything reached the
disk. What it does say is `PwDatabase.Modified`, which a successful save clears, so that is what a
write is believed on. Every write waits for its save on KeePass's thread and reads back what it
wrote, pairing's among them rather than pairing's alone as this once said, and it waits through
`BeginInvoke` and a bounded wait, never `Invoke`, which has none. `DESIGN.md` §119 says why that
wait is safe.

**`Modified` alone turned out to be too generous, and a write is now believed on two things.**
KeePass clears the flag from what it did in memory, so a save that wrote the file and then failed
its own verification, or never reached the disk at all, still reads as done. So the file is hashed
back against `PwDatabase.HashOfLastIO` after a landed write, and a disagreement answers
`500 not verified`, which is deliberately not `500 not saved`: the second has been taken back out,
so a retry duplicates nothing, while the first has not, because nothing knows whether the file holds
the change. The same asymmetry decides the null case: `Asking` lets an unreadable file through
because the next save is about to put it back, and both of these refuse it, since a commit that got
as far as deleting the old file is exactly the unreadable case.

## Build and toolchain

**`build.ps1` is the entry point.** It is also the gate, and CI runs the same script on every pull
request and on every push to `master`, because there is one definition of clean and it is in that
file.

```powershell
.\build.ps1                # prose, the client, the build, and every test that does not need KeePass
.\build.ps1 -Task Lint     # markdownlint, cspell and the suppression comments, on their own
.\build.ps1 -Task Client   # the Python on its own, PyKeePassGate and the tools: ruff, mypy, pytest
.\build.ps1 -Task All      # the default, then the end to end tests
.\build.ps1 -Task Deploy   # copy the plugin into plugins\KeePassGate in the provided KeePass
.\build.ps1 -Task Package  # the plugin as Release, zipped with a SHA256SUMS into dist\, from a clean clone
.\run-debug.ps1            # deploy, then start that KeePass, Debug
.\run-release.ps1          # deploy, then start that KeePass, Release
```

**`build.ps1 -ListGates` is what CI asks for.** It answers one line per check and the runner CI
gives it, and the workflow builds one job per line, so a task added to the script, or a runner
added for one, is checked without anybody editing `.github/workflows/gate.yml`. Every check runs on
Windows, and the client's on Linux as well, under PowerShell 7. The jobs are never written there
and neither is where they run, so what changes in that file is the commit each action is pinned
to, which its header says as well.

**The fast half can also run at a commit.** `.pre-commit-config.yaml` holds the client's style and
types, the spelling and the markdown rules, run on what is being committed, and nothing installs
it: `uv run pre-commit install` does, once per clone. It is not the gate, and `TESTING.md` says
what it leaves to `build.ps1`.

The projects are SDK-style, target `net48` and build with the **.NET SDK's `dotnet` CLI**, which is
what makes `dotnet test` available. **`global.json` holds the SDK to 9 or later**, the first to
compile C# 13, and rolls forward to the newest installed, so a machine with only an older SDK
refuses to build rather than failing on the language. `msbuild` is not on `PATH`, so if it is
ever needed directly, locate it:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"
```

**KeePass is provided, not found.** `keepass.version` at the root pins the release, `keepass.sha256`
pins the archive it must arrive as, and `tools/KeePass.ps1` fetches it into `.keepass\<version>\`,
which is gitignored. `Directory.Build.props` reads the same file, so an editor build compiles
against the same one. Override with `-KeePassDir` or `-p:KeePassDevDir=` for a machine that keeps
its own somewhere else.

Three things about it that are decisions rather than details. The directory is **named after the
version**, so changing the pin cannot silently reuse the old one. The archive is verified twice,
once against the pinned hash and once by checking that `KeePass.exe` carries a valid Authenticode
signature from KeePass's author, which is the check that does not depend on where the bytes came
from. And a version with no line in `keepass.sha256` is **refused**, with the hash that arrived
printed so it can be pinned deliberately.

A KeePass launched from there writes its own `KeePass.config.xml` next to the exe, which is why
testing there cannot damage the real configuration.

**The plugin's `ProductName` must be exactly `KeePass Plugin`**, which `src/KeePassGate/KeePassGate.csproj`
sets with `<Product>`. KeePass reads it from the file before it opens the assembly and **silently
skips** anything else: no error, no message, no entry in its plugin list. `PluginNamingTests` pins it
against the constant read out of KeePass itself. The entry point must likewise be named for the file,
`KeePassGate.KeePassGateExt` in `KeePassGate.dll`, namespace included.

**There is CI, and `master` requires it.** The workflow runs every gate on every pull request and on
every push to `master`, and a ruleset on `master` requires one check, `gate passed`, which passes
only when every other job did, on a branch up to date with `master`. GitHub does not run a pull
request's checks again when `master` moves, so a check that passed before another merge landed is no
check of the two together; a branch that is behind is updated, which runs the gate again, before it
merges, and rebased rather than merged, so its commits stay its own. So `master` changes through a
pull request, merged with a merge commit, and is never force pushed or deleted. The ruleset asks for
no approval, because a sole maintainer cannot approve their own pull request, and the repository's
administrators may bypass it. It names `gate passed` rather than each gate, because the gates are
what `build.ps1 -ListGates` answers, and a list written into a setting would go stale the first time
a gate was added. This reverses "there is no branch protection", which held until the build order's
pull requests had all been merged, since a required check on a stack being merged would have held
every one of them.

**Pins move in pull requests of their own, proposed rather than noticed.** `.github/dependabot.yml`
opens one grouped pull request a week for each kind: the actions' commits, `uv.lock`, the prose
tools and the plugin's libraries, never a library's next major version and never a floor in
`pyproject.toml`, and each a week after its release. Only the prose tools and a minor or patch
version of the actions merge themselves, through `dependabot-auto-merge.yml`, since a green gate
shows that an update breaks nothing and not that it is what it claims to be; the libraries and the
lock wait for a person. KeePass is pinned where no such service reads, so `keepass-watch.yml` opens an
issue when it releases a newer version, and moving that pin stays a person's job, since
`tools/KeePass.ps1` refuses a version until its hash is written down.

**CodeQL reads what the gate does not.** `codeql.yml` analyzes the C#, the Python and the workflows
on every pull request, every push to `master` and once a week, with what
`.github/codeql/codeql-config.yml` names: the `security-and-quality` queries and each language's
alert-suppression query, with no rule left out. The C# is compiled
by `build.ps1 -Task Build` against the pinned KeePass, because CodeQL analyzes a compiled language
from what the compiler sees, and without the build every call into KeePass resolves to nothing. The
CodeQL is the one the pinned action ships, `tools: linked`, so a new version arrives in Dependabot's
pull request rather than on a day nothing changed. **A pull request's own upload reports only what
its lines introduce**, and code scanning ignores the mark CodeQL puts on a suppressed finding, so
each analysis is kept as CodeQL wrote it and judged by the job `code scanning passed`, which
`tools/code_scanning.py` does the work of: it uploads a pull request's findings without the ones
the code suppresses and everything else as it is, and fails on any finding nothing suppresses,
anywhere in the code. **And no suppression merges unseen**: `tools/suppression_review.py` opens a
review thread on every suppression a pull request adds, changes or removes, which the merge waits
for, and deletes its own thread again once that suppression is no longer in the pull request,
unless somebody answered in it or resolved it. It keeps one comment on the pull request listing
what it suppresses and counting the rest by rule, and warns about a suppression under which CodeQL
found nothing and a finding CodeQL suppressed that no comment accounts for. It fails only when it
cannot write to the pull request, which is what a fork's pull request is given. **And the Security
tab lists every suppression**: on `master` and the weekly run, `tools/dismissals.py` dismisses the
alert of each finding the code suppresses, with the comment's reason, and opens again every one it
dismissed that the code no longer suppresses, since GitHub leaves an alert dismissed after its
finding is gone. The PowerShell is left out, because CodeQL reads none.

## Testing

[`TESTING.md`](TESTING.md) is the full account. In short: unit tests that never load KeePass, host
tests that drive the real KeePass types in process, and an end to end tier under the `Live` category
that starts a throwaway KeePass and types into it.

**Never test against the author's real database.** The end to end tier builds its own KeePass and its
own KDBX4 under `%ProgramData%\KeePassGateLive`, creating the database with KeePassLib, which is already
referenced. For ad hoc work `pykeepass` (ephemerally, via `uv run --no-project --with pykeepass`) is
equally fine. Keep any driver scripts in the session scratchpad; a `.kdbx` must never be committed,
and `-pw:` puts the password in the process list, so it is only ever acceptable for a dummy.

## Conventions

**The rules that hold on every branch are in [`CONVENTIONS.md`](CONVENTIONS.md)**: secrecy, branch
layout, prose and commits. What follows is what this branch adds, because each of these depends on
the tools this branch carries.

- **Python is checked the way the C# is.** `ruff`, `mypy --strict` and `pytest` with a coverage
  floor, all configured in the root `pyproject.toml`, all run by `build.ps1 -Task Client`, which
  `Test` runs. `uv` owns the environment and `uv.lock` is committed, so every machine gets the same
  versions. The tools written in Python are held to the same checks and the same floor, with their
  tests in `tests/tools`, and imported by those as the scripts they are.

  **`ruff check` is the gate and `ruff format` is not**, which is a decision rather than an
  omission: thirteen files have never been through the formatter and running it would rewrite them
  all. What is enforced is what a formatter cannot decide for you, and `pycodestyle` is in the
  selected rules, so a line too long or a space in the wrong place still fails. What is left to the
  author is where a line breaks, which is a judgment this project spends deliberately, including
  inside the fenced examples in `docs/`, where the formatter would take the alignment out of a
  block written to be read. Adopting it means reformatting everything in one commit that changes
  nothing else, not letting it arrive a file at a time under other work.
- **Docs stay with code.** A behavior or architecture change updates the design documents in the
  same pass, and reversals are recorded rather than edited away.
- **A CodeQL finding is fixed, or suppressed where it is, with the reason.** The one spelling is a
  comment of its own directly above the line the finding is on: `// codeql[cs/rule-id] why` in C#,
  `# codeql[py/rule-id] why` in Python, with a marker for each rule when two are found on one line,
  `// codeql[cs/one] codeql[cs/two] why`. `build.ps1 -Task Lint` refuses every other: a list in
  one bracket, a rule in another case, a marker after code, above a blank line or above another
  comment, all of which suppress nothing, and `lgtm` in every form and a Python `noqa` without
  codes, which suppress more than they look like. `tools/suppressions.py` decides that as CodeQL
  does: it finds the comments with tree-sitter's grammar of each language and applies CodeQL's own
  patterns to them, and `tests/tools/SuppressionVectors.json` holds it to what the pinned CodeQL
  made of 47 cases. **tree-sitter was chosen over each language's own parser**, Roslyn and
  `tokenize`, once both had been built: they found the same comments in every file of the
  repository and agreed with CodeQL on every vector, and this one is a third of the size, needs no
  build, and reads every branch of an `#if`, which covers whatever CodeQL reads across the two
  configurations it builds. A file holding a marker that does not parse cleanly is refused, so a
  grammar older than the code fails loudly rather than being read with a guess.
- **A call into Windows goes through one class on each side**: `Ui/NativeMethods` in the plugin and
  `Shared/User32` in the tests, where each declaration is private and called once, by the method of
  its name beside it. CodeQL reports every declaration of unmanaged code and every call to one, and
  nothing managed does what these do, so both are suppressed there with the reason, and nowhere
  else. A new call into Windows is a new pair of them in that file. The tests' class is not
  `NativeMethods` too, because a test importing the plugin's `KeePassGate.Ui` would see both.
- **A path is joined with `Paths.Join`, never `Path.Combine`**, which starts again at any rooted
  part after the first and drops what came before it, silently. The helper refuses that part, and
  one climbing out with `..`, and is the plugin's and the tests' alike; CodeQL reports every call
  to `Path.Combine`, which is what keeps it so.
- **American English is checked, not only asked for.** cspell's own `en_US` dictionary accepts the
  British doubled L, the past tense of cancel or dial spelled with two, and its language
  dictionaries accept a British spelling or two in code, so `cspell.yaml` refuses about 250 British
  spellings by name, in `flagWords`. A name that is somebody else's keeps its own spelling behind a
  `cspell:ignore` where it is quoted.
- **The version is written once, as `Version` in `Directory.Build.props`.** The build stamps the
  plugin with it, hatchling reads it from there for the client's package, the client's
  `__version__` comes from the installed metadata, and the routing document's `info.version`, which
  YAML cannot take from anywhere, is held to it by a test on each side. Moving it is one line there
  and the same in `openapi.yaml`; `CONVENTIONS.md` says when, and `CHANGELOG.md` says what moved.
  A minor version releases both halves and a patch only the half it fixes, so a patch number may
  exist on one side only, and the same major and minor version is what makes two halves a pair.

## Security posture

This plugin guards every secret the author has, and it runs inside the KeePass UI process, so a bug
here can hang or crash the password manager.

- **Fail closed, always.** A malformed marker, an unknown subject, an unparseable claim, a missing
  record: all deny. There is no permissive default anywhere.
- **The threat model includes the caller.** AI agents are intended consumers and can be
  prompt-injected, so the ACL is not defending against a hostile network, it is defending against a
  legitimate client asking for more than it should.
- **Local code execution as the user is conceded**, throughout. Do not add a control that only
  defends against it, and do not claim one does.

## Where to start

[`DESIGN.md`](DESIGN.md) §132 ends with a multi-step build order. **Steps 1 to 31 are done.** The
skeleton loads and shows a KPG tab, that tab creates, recreates and deletes the authority, the gate
serves the document's routes over mutual TLS to a client the authority issued and the database holds
a record for, a stranger can pair, a locked database answers `503 locked`, a grant means something
definite, an operator can write one from the group and entry dialogs, the router refuses anything
the record and the rules do not both allow while recording every answer the rules give, twenty-four
routes read and change the database, two launchers start the pinned KeePass with the plugin in it,
a profile has a home of its own, the plugin has a window under the Tools menu, `docs/` says how
to use the result, a client's certificate can be renewed and given up, a program built on the client
can keep its key under a password, make its connection and test against a stand-in gate, a client
can ask who the gate takes it for and what it may do at a node, the audit records every pairing and
renewal the operator answers and every client revoked, the client follows field references as
KeePass does, under the caller's own rules, `build.ps1 -Task Package` makes the zip a release
would publish, the README tells somebody arriving what this is, around a picture of the
whole journey that moves and a diagram of the pieces, the command line reaches every route
and prints JSON for a program, a write can be made against what was read, refused with `412`
when the node has changed since, a merge into `master` waits for the whole gate to pass, a
tag publishes both halves, and CodeQL analyzes every language it reads.

**Start with [`docs/`](docs/README.md) if the question is what this does**, and with the documents
below if the question is why it does it that way. What is left is step 32, a clean analysis.

**The storage decision survived the thing most likely to invalidate it.** The `GroupForm` snapshot
hazard is real and now measured rather than remembered: `SnapshotHazardTests` reads the mechanism off
the real KeePass and reproduces the behavior with KeePass's own types. What it established, and what
step 8 built on: the settings dialog the KPG tab lives on carries **no** snapshot of custom data, so
those buttons are safe from their own dialog, and anything written while a **group** dialog is open
is destroyed by its OK.

**That dialog does hold one other copy**, and it is the recycle bin choice rather than custom data.
A bin made underneath it is undone by its OK, so a removal that would make the first one is refused
while it is open. `GroupDialogs` therefore watches the settings dialog beside the group, entry and
maintenance dialogs, the TAN wizard as well, which fills a group on OK, and KeePass's progress
window, which says an operation of KeePass's own is running inside the tree: six in all. A KeePass
that lacks any of the six starts no watch, so every write and every pairing is refused and the log
names the missing one. The settings dialog is left out of the count it keeps, since it carries no
node's custom data, and so are the wizard, which puts back no custom data either, and the progress
window, which holds nothing. The count gates nothing: it gated the profile purge, a button on that
very dialog, and went on being kept for the tests and the log when the purge went.

**The profile list is the database's own names.** `GrantSheet.Available` is what the profile
picker offers, being `ProfileNames` less every profile already on the table, **explicit and
inherited alike**, because a profile is one row however it got there and narrowing an inherited
one is done on the row that is already there.

**And the list decides what a client holds.** A client is given the profiles its record holds less
every name not on the list, read beside the record on every request, so removing a profile touches
the list and nothing else: a rule still naming it grants nothing and the grant editor strikes it
through, and adding the name back restores every rule and every client holding it, which the
Profiles page and the approval dialog both say before it happens. This reverses a purge that took a
removed name out of every rule, revisions included, and still lost to a stale copy merged in; see
DESIGN.md §139. An unreadable list is an empty one, so every client stays admitted and reaches
nothing.

**What step 13 settled about the editor.** The tab is captioned `KPG` with the plugin's icon, and
not "Access control", because another plugin may well put an access control page on the same dialogs
and two pages with one name governing different clients is a trap. Neither column on the table is
typed into: a mask spelled by hand is a rule nobody meant, and a profile spelled by hand is a rule
for a name that may not exist, which grants nobody anything and reads exactly like one that works.
Both are chosen from a list, and choosing a profile opens the rule picker straight after it, because
a profile with no rule grants nothing and nobody opened the list to say that. `F2` and `Alt+Down` are
the keyboard's way into either picker, and while one is open `Enter` and `Escape` belong to it, which
is what `DialogKeys.Suspend` takes away from the dialog's own OK and Cancel: without that, choosing a
verb presses OK on the group dialog and closes it with the rule half made.

A rule is a `CRUSDLAP` mask in a monospaced column rather than a row of check boxes, so two rules
differ in the place the eye is already looking. Inherited rules are shown among the local ones in
gray, because a table listing only what this node grants reports nothing for
an entry that a group above already opens wide. **A third state was missing and is now drawn**: a
seal does not take away what a group above grants, it stops it arriving, so an inherited row the
seal refuses keeps its place and carries a line through the mask. Gray was already spent saying the
rule comes from elsewhere, which stays true either way, and the mask is where the eye already is.
Its tooltip is replaced rather than extended, because the inherited one offers to replace a rule
that arrives, and on that row nothing arrives: naming the profile is what lets the grant through
again. And the editor never writes the same rules in other words. A node's custom data keeps no
modification time, but the entry dialog compares the entry it hands back with the one it was
given, so a rule respelled by a byte is a changed entry, with a revision in its history and the
database marked modified, for having been looked at.

**What step 14 settled about authorization.** The intersection is structural: the profiles handed to
the resolver are the ones the admission record carries, less any the database's list no longer
names, so the rules can only ever answer about what the operator approved when the client was
admitted, at pairing or at a renewal, and has not taken away since, and there is no place where the
two halves could be compared wrongly. The **router** asks, before the body is checked and before any
handler runs, because a handler could forget to. A node that may not be reached and one that is not
there answer the same 404, and the audit is where the operator is told which it was. **Each client's
lines are paced**, by `DecisionPace`: a thousand at once and ten a second after that, per client
key, so a renewal keeps the allowance the client has spent, and a decision past that waits on the
caller's own connection rather than being dropped or refused, because the file rolls by size and a
client that could fill it quickly could push its own earlier lines out of it. `DESIGN.md` §120 has the argument.

**What step 12 settled about verbs.** A rule holds **six verbs and two switches**, which is
`CRUSDLAP` and is the order they are declared, written and shown in. The verbs are create, read,
update, strip, delete and list, and `list` sits below `read` because knowing an entry exists is not
knowing what is in it. Removal is two verbs rather than one: `strip` is may remove the parts inside
a node, `delete` is removals are allowed here, and removing a node asks for `delete` from the group
it leaves and from itself.

**A switch is not a verb**, and getting that wrong is the mistake worth naming: `attachments` does
not mean "may read attachments" and `protected` does not mean "may read passwords". Each means "and
those too", for whichever verbs the same rule grants. `.R...LA.` is read and list over entries and
over the attachments inside them, and `.R...L.P` is read and list with the protected values in them.
A rule holding switches and no verb grants nothing. **Neither switch is asked about for a removal**,
because removing shows nobody anything and a rule that may remove an entry already takes everything
in it, so `...S....` strips an attachment and a protected field alike. The routing table says
separately, as `x-kpg-attachments`, whether an operation reaches that far, and declaring the switch
as a verb stops the plugin loading, because no operation does attachments.

A grant is a **set** rather than a rung on a ladder, so `create` without `read` is expressible, which
is what an append-only agent wants.

`x-kpg-verb` is read out of the document as an `AclVerb`, and an operation that declares none stops
the plugin loading. It stays in the document rather than becoming an attribute in code so that the
two cannot disagree, and `none` is a decision written down rather than a field left absent: the
status route and the caller's identity read nothing a rule is about, a client's revocation of itself
touches no node a rule is about, and pairing and renewal are authorized by a person reading a dialog
rather than by a grant, because a client that is pairing holds no profiles yet. `any` is the one
value no rule can hold: the two routes asking what a client may do at a node declare it, and the
router answers it profile by profile, every verb and switch through the resolver's own walk,
refusing it as an absent node unless some profile is granted a verb there. One mask per profile,
because a switch belongs to its rule and a union across profiles would claim combinations the gate
refuses.

**Four things about the router that are decisions rather than details.** A caller with no
certificate is refused the same way everywhere, so an undeclared path and one it may not use answer
alike and only an admitted client is told which it was. That is about the *refusal*, not about
whether a route exists: the routes are declared in a document that ships inside the plugin, so a
`405` for a declared path asked for with the wrong method says no more than reading that document
says, and `GET /pair/hello` answers exactly that. A `405` is about the path and never the query:
a query name nothing on the path declares makes an undeclared target, answered as one whichever
method asked, and so does a body sent where none is declared. An operation may carry a guard for a
precondition the document cannot state, which is how pairing keeps its routes to connections that
arrived on an invitation, and a guard runs **before** the body is checked so that nothing is
reported about a body on a route the caller may not reach. **And the reverse of that guard is the
router's**: a connection that arrived on an invitation reaches the pairing routes and nothing else,
which `PAIRING.md` §302 has always said and nothing enforced. A client the database already admits
can hold an invitation too, and its certificate was judged on the pairing path the way it is judged
anywhere, so it reached every ordinary route there. That grants it nothing extra, since the same
certificate reaches the same routes on its own name, and it did buy the pairing budget on a
connection with no person in it. And `PairingHandler` is two operations now rather than one
dispatcher: which path is which is the document's answer.

What steps 8 and 9 leave it. `DatabaseWriter` is the single writer, and the only place a request
writes to a database. `GroupDialogs` watches for the dialog that would take a write back, tracking
specific `GroupForm` instances rather than counting windows, because the question is per node and a
count cannot answer it, and because a count would also depend on what this plugin's own dialogs
register with `GlobalWindowManager`. **It refuses while it is not watching**, which is a different answer from
seeing nothing open and is the fail-closed direction on the one guard this design rests on.
`LockWatch` drives both lock transitions against seams rather than KeePass, and
`OpenDatabaseTargets` is where a database becomes a `LockedTarget` and back. `JsonBody` reads
request bodies strictly and says in its own comments where it stops being enough, which is exactly
where step 11 began.

**A pairing connection is measured against `GateOptions.PairingBudget` rather than `Budget`.** The
ordinary budget is what stops a peer holding a thread by feeding it slowly, and phase 4 exceeds it
on purpose, because it is blocked on a person. Closing the socket there cancels nothing: the
invitation is still spent and the record still saved, and only the answer is lost.

**The ordinary budget ends with the request, and only pairing's and renewal's are a sum.** A
connection's deadline is dropped once the whole request is off the wire, because that is where the
peer stops being able to make the connection take longer: what is left is this plugin's own write
and save. Pairing keeps its deadline, and that asymmetry is the argument rather than an exception to
it, since a pairing's remaining work is a list of waits somebody wrote down: the screen, because one
pairing at a time is put to the operator and a second waits as long again just to get in front of
them, then the dialog and the grace on it, then the turn at the writer. `PairingBudget` is what
those are added up against. `GateOptionsTests` still pins that one. A renewal waits for the same
things on the same screen, so the listener gives it the same deadline once it has read which
request arrived.

**This used to claim a floor for both**, `DatabaseWriter.LockTimeout + DatabaseWriter.WriteTimeout`,
and it was wrong: `WriteTimeout` bounds the wait for the marshaled call to *start*, and a call that
has started is waited out with no clock on it. No sum of this plugin's timeouts bounds a save, which
is a whole-database write of whatever size the database is on whatever storage it sits on: 201 MiB
of attachments measured 80.6 seconds here, and a heavy KDF adds eight seconds a save. What the
deadline destroyed was never the peer's grip on the thread but the answer to a write that landed
anyway, leaving an agent to retry and make a second entry under an identifier it never saw. The test
that pinned the floor is gone rather than rewritten, because what it asserted was a relation between
two constants that bounded nothing; `GateListenerTests.A_write_that_outlasts_the_budget_is_still_answered`
replaced it, over a real socket. See DESIGN.md §119.

**Two things about records that cost time to learn.** They are read from the live database on every
connection and never cached, so a record that lands takes effect with nothing having to be reloaded
or told: the KPG tab's clients page lists every admitted client, names in red the ones whose
renewals stand refused and lifts that answer from the row's menu, and takes one away, and from
the next request onwards that client is refused. **Landing is what waits now**, see the tab's own
entry below: the row goes at once, the revocation lands when the dialog is accepted, and the file changes
when the database is saved. Until then the certificate still works. The press itself goes into the
dialog's buffer and cannot fail there, so whether a revocation reached the database is said in the
log when the dialog is accepted, not on the page. And a record written into the
`.kdbx` file underneath a running KeePass is invisible to it and will be overwritten by its next
save: KeePass serves the database it holds in memory, which is why pairing writes through the plugin
rather than through the file.

**One thing to know before touching the transport.** An `EphemeralKeySet` certificate signs happily
and SChannel will not do TLS with it in either direction. That is why `Org.BouncyCastle.Tls`
terminates the transport, and why every key the plugin loads can stay off the disk.
`HostCertificateHandshakeTests` pins both halves, including the SChannel refusal, so a tidy-up that
goes back to `SslStream` fails loudly rather than quietly writing keys to somebody's profile.

**A dependency ships beside the plugin, and so does everything it drags in.** `build.ps1 -Task
Deploy` copies every DLL from the build output into `plugins\KeePassGate`, because KeePass loads
plugins with `Assembly.LoadFrom`, which probes the plugin's own directory rather than KeePass's.
**A directory of its own, not `plugins` itself**: KeePass finds a plugin anywhere under `plugins`,
and a flat install shares every dependency by file name with every other plugin installed flat.
Only KeePass has a binding redirect, so one `Newtonsoft.Json.dll` cannot serve two plugins built
against different major versions, and whichever was there first stops binding.
**The whole closure, not the direct references**: copying what the plugin names was enough while its
one dependency had none of its own, and stopped being enough the moment one did. A plugin missing a
transitive dependency failed to load with nothing in the log to say why, which is what 34 of the 35
end to end tests said the first time. It still fails to load, and now names what is missing in its
log, which has a file by default, and in KeePass's warning that the plugin cannot be loaded.
