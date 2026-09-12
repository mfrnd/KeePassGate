# CLAUDE.md

Guidance for anyone, human or AI assistant, working in this repository.

## What this is

**A KeePass 2.x plugin, written from scratch**, serving a small specification-defined HTTP API over
mutual TLS, so that automation and AI agents can each be given a narrow, revocable, audited slice of
a database.

**Status: steps 1 to 18 of the build order are done and the API is served.** The plugin loads
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
the rules make, and every certificate the database issued that an operation turns away, to a JSON Lines file
outside the database. **And there is an API**: twenty-two routes over groups,
entries, fields and attachments, every one declaring the verb it needs and the node it is about, with
`PyKeePassGate` covering all of them. **And a profile has a home**: the set of names a database
knows lives beside the authority, with a page of its own to manage them, and a name taken off it
grants nothing whatever rule still names it. **And the plugin has a window of its own**, under KeePass's Tools
menu, saying where the gate is listening and taking the port it should listen on.

## Read these before proposing anything

| | |
| --- | --- |
| [`DESIGN.md`](DESIGN.md) | what to build, and a section attacking it. **The contract.** |
| [`API.md`](API.md) | the reasoning behind the routes and what is deliberately absent, with the per-route detail in [`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) |
| [`CA.md`](CA.md) | the certificate authority: issuance, claims, validation, revocation |
| [`PAIRING.md`](PAIRING.md) | how a stranger becomes a client, with the flow diagrams |
| [`FINDINGS.md`](FINDINGS.md) | what was measured rather than assumed, and what turned out wrong |
| [`TESTING.md`](TESTING.md) | the four test tiers, what each one buys, and how to run them |

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
  burn cannot be reverted. The settings dialog a token was made on withdraws it unless that dialog
  is accepted, so Cancel means Cancel for the one thing on the tab the database does not hold.
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
  filters. Four places catch everything, because in those four an escaping exception ends KeePass:
  the plugin entry point, the handlers of KeePass's window events, the settings dialog's close
  among them, the Tools menu item, and the connection thread.
  Those report through `GateIncidents`, which counts on the plugin's window under Tools and
  logs at `UNEXPECTED`. Two more roots are kept safe another way: the accept thread and a
  connection's deadline timer have nothing above them either, and rather than catching
  everything, every call they make is total or filtered. `GateListener.Drop` says what keeps
  that true. And two places catch everything without being a fifth, since each keeps what was
  thrown and throws it again where one of the four sees it: `GateTlsServer`'s callbacks, because
  Bouncy Castle turns whatever a callback throws into a fatal alert, and the call `ControlUiThread`
  marshals, because WinForms would answer one escaping it with a dialog of its own.
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

  **And `KPG.Profiles` is replayed as a difference**, because it is the one key with a second
  writer: a pairing names an approved profile in the same write as the admission record, so the
  tab's own list is not the only version of it. Key by key is no help there, since the tab did
  touch that key, and the older list went over the newer one. `PendingStore.Reconcile` registers a
  merge through `ProfileNames.Merge` for it, so a name an approval added survives and a name the
  operator removed still goes, and at the ceiling a name the operator added that no longer fits is
  left out, said on the page as it goes and named in the log at OK, however the list was edited in
  between. What no merge can decide is left where it is: a client approved for
  a profile the operator is part way through removing is admitted to a name OK takes off the list,
  where it grants nothing, which is why a buffered profile edit kills the invite button.

  One case survives all of that, deliberately: recreating or deleting the authority while a pairing
  lands leaves that record behind, inert, because its certificate chains to an authority that is
  gone. The clients page says so on the row rather than listing it as a client.

  **Revocation waits too, and that is a cost that was argued and accepted.** Immediacy is something
  a revocation would rather have, and an operator who revokes and leaves the dialog open has revoked
  nothing. What it buys is one model for the whole tab, because whichever model somebody assumes on
  the page they are not looking at is the one that costs them.

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
  body, the query parameters or the guard. The answer omits `Content-Length` rather than sending
  a wrong one, which RFC 9110 section 9.3.2 permits.
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
  library, and `pykeepassgate.cli`, a command line that can do nothing the library cannot. A route
  the gate serves and this does not cover is a gap, and a method here with no route behind it is a
  lie. Its `pyproject.toml` is at the repository root because there is one Python project.
- **The client's protocol code is pinned against the plugin's own output.**
  `tests/pykeepassgate/test_protocol.py` holds vectors produced by `PairingToken`, `SniName` and
  `PairingProof` over inputs written down beside them. Without those, both halves could agree on
  something neither document describes, because both halves are code written here. Regenerate them
  from the plugin rather than from the client if they ever have to change. `ProtocolVectorsTests`
  holds the plugin to the same values, so a change to either half fails in that half's own tier.
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

  Measured on `spikes/pykeepass-compatibility`, taken from `master` and never merged: of the 56 public
  members on `PyKeePass`, 23 are servable over the routes the gate already has, 6 would need routes
  nothing forbids, and 27 cannot exist here. Its `RESULTS.md` has the per member table and what
  would have to change on each side.

## Measured facts that cost time if rediscovered

Every one of these was established by running code, and most contradict what the documentation
suggests. The code is on branches of its own: `spikes/tls-and-certificates` holds the nine that
measured TLS and X.509, `spikes/build-environment` the one that measured whether the gate can
fail while nothing is wrong, and `spikes/revocation-merge` the one that measured how KeePass merges
a database's custom data. Each gathers what it settled in its own `RESULTS.md`. Run them from
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
- **`dotnet test` with a filter that matches nothing exits 0**, having run nothing, and its trx
  counts nothing. A gate that believes the exit code is green over an empty run, so `build.ps1`
  reads each tier's trx and refuses one in which nothing executed, `Assert-Executed`.
- **Windows PowerShell 5.1 passes an argument ending in a backslash so that the backslash escapes
  the closing quote.** `-p:KeePassDevDir=C:\Program Files\KeePass\` reaches `dotnet` as one
  argument that swallows the `--nologo` after it. `Resolve-KeePass` trims a trailing separator for
  that reason, keeping it only on a drive root.
- **C# 7.3 is available, and the packaging is why.** A `.plgx` would be recompiled by KeePass's
  in-box CodeDOM provider, which caps the language at C# 5. This ships a plain DLL and escapes that
  entirely; the probes are conservative only because `Add-Type` compiles them.

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
.\build.ps1 -Task Lint     # markdownlint and cspell on their own
.\build.ps1 -Task Client   # PyKeePassGate on its own: ruff, mypy, pytest
.\build.ps1 -Task All      # the default, then the end to end tests
.\build.ps1 -Task Deploy   # copy the plugin into plugins\KeePassGate in the provided KeePass
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
what makes `dotnet test` available; they open in Visual Studio 2019 as well. `msbuild` is not on
`PATH`, so if it is ever needed directly, locate it:

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

**There is CI, and there is no branch protection.** The workflow runs every gate on every pull
request and on every push to `master`, and no check is required before a merge. A red check
therefore reports and does not block: read it before merging, because nothing else will. Requiring
the gate is one setting on a public repository, and GitHub offers it on a private one only under a
paid plan.

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
  versions.

  **`ruff check` is the gate and `ruff format` is not**, which is a decision rather than an
  omission: thirteen files have never been through the formatter and running it would rewrite them
  all. What is enforced is what a formatter cannot decide for you, and `pycodestyle` is in the
  selected rules, so a line too long or a space in the wrong place still fails. What is left to the
  author is where a line breaks, which is a judgment this project spends deliberately. Adopting it
  means reformatting everything in one commit that changes nothing else, not letting it arrive a
  file at a time under other work.
- **Docs stay with code.** A behavior or architecture change updates the design documents in the
  same pass, and reversals are recorded rather than edited away.
- **American English is checked, not only asked for.** cspell's own `en_US` dictionary accepts the
  British doubled L, the past tense of cancel or dial spelled with two, and its language
  dictionaries accept a British spelling or two in code, so `cspell.yaml` refuses about 250 British
  spellings by name, in `flagWords`. A name that is somebody else's keeps its own spelling behind a
  `cspell:ignore` where it is quoted.

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

[`DESIGN.md`](DESIGN.md) §132 ends with a multi-step build order. **Steps 1 to 18 are done.** The skeleton
loads and shows a KPG tab, that tab creates, recreates and deletes the authority, the gate serves
the document's routes over mutual TLS to a client the authority issued and the database holds a
record for, a stranger can pair, a locked database answers `503 locked`, a grant means something
definite, an operator can write one from the group and entry dialogs, the router refuses
anything the record and the rules do not both allow, recording every decision either way,
twenty-two routes read and change groups, entries, fields and attachments, and two launchers start
the pinned KeePass with the plugin in it.

**What is left**: the documentation for whoever runs it.

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
names, so the rules can only ever answer about what the operator approved at pairing and has not
taken away since, and there is no place where the two halves could be compared
wrongly. The **router** asks, before the body is checked and before any handler runs, because a
handler could forget to. A node that may not be reached and one that is not there answer the same
404, and the audit is where the operator is told which it was. **Each client's lines are paced**, by
`DecisionPace`: a thousand at once and ten a second after that, per admission key, and a decision
past that waits on the caller's own connection rather than being dropped or refused, because the
file rolls by size and a client that could fill it quickly could push its own earlier lines out of
it. `DESIGN.md` §120 has the argument.

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
status route reads nothing, and pairing is authorized by a person reading a dialog rather than by a
grant, because a client that is pairing holds no profiles yet.

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

**The ordinary budget ends with the request, and only pairing's is a sum.** A connection's deadline
is dropped once the whole request is off the wire, because that is where the peer stops being able
to make the connection take longer: what is left is this plugin's own write and save. Pairing keeps
its deadline, and that asymmetry is the argument rather than an exception to it, since a pairing's
remaining work is a list of waits somebody wrote down: the screen, because one pairing at a time is
put to the operator and a second waits as long again just to get in front of them, then the dialog
and the grace on it, then the turn at the writer. `PairingBudget` is what those are added up
against. `GateOptionsTests` still pins that one.

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
or told: the KPG tab's clients page lists every admitted client and takes one away, and from
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
log, which has a file by default.
