# Changelog

What each version of KeePassGate changes. The plugin, PyKeePassGate and the routing document carry
one version and move together. Which number a change moves is the API's contract, [`API.md`](API.md)
§411, and what each kind of release publishes is [`CONVENTIONS.md`](CONVENTIONS.md#versions).

Newest first. A version is written here before it is released, under its own heading, and says
**unreleased** until it is. A patch releases only what it fixes, so its heading names the halves
it releases, as `0.1.1, the plugin and the client, unreleased` would, and a minor version names
none, since it releases both.

## 0.3.0, unreleased

- **Breaking: no authority that 0.2 made is served, so a database is given an authority again and
  every program paired again.** The authority, its host certificate and the gate key are now a
  record in the plugin's store: an entry in a group of the plugin's own named `KeePassGate`, its
  content protected in memory as a password is, under a check keyed by the store's own key. Create,
  Recreate and Delete on the KPG tab write it there at the settings dialog's OK, and so does keeping
  an authority where copies of the database each made their own. 0.2 kept the authority in the
  database's custom data, and that is reversed rather than migrated: 0.3 reads nothing there about
  the authority and changes nothing there, so a database 0.2 paired shows no authority on the tab
  and is served nothing until **Create authority** and OK, from which it is served; a save puts the
  authority in the file. Each program then pairs again. The admission records and the revocations
  0.2 wrote stay where they were, are read by nothing, and admit and refuse nobody. A
  database whose store holds no key is served nothing, but for `503 lost` below. An edit to what the
  plugin wrote in an entry of the store is undone at the plugin's next write into the store, with
  the edit kept in the entry's history; in a database that keeps no history such an edit stops the
  store, and a title or notes changed on such an entry are not put back. **Keep this authority** is
  refused while KeePass's entry dialog is open on an entry the choice would delete or put back,
  since that dialog's OK writes its own copy over it. `STORE.md` §614, §626, §627, §628 and §645.
- **The X on the clients page revokes the client, with every certificate it holds.** The page
  lists a client's certificates as a group of rows: the certificate admitted last carries the
  client's name and the X, and each earlier certificate is indented under it as `earlier
  certificate`, with no X. Pressing the X, or **Revoke** on that row's menu, takes every row of
  the client away at OK. It used to take away the certificate of the row pressed and leave the
  client's other certificates working. A revoked client is refused whatever a copy of the database
  from before the revocation still holds, and wherever and whenever a certificate of that client
  was issued, a certificate renewed on another machine before it learned of the revocation among
  them; it pairs again, as a new client. The audit holds a `revoked` line for each certificate of
  the client that the database held, and a single line with `admissionKey` as `null` where it held
  none. The row's menu names the certificate of its row on its first line. `STORE.md` §615.
- **An admission, a revocation and a certificate a program gives up are records in the plugin's
  store**, entries of the group `KeePassGate`, as the authority is. A pairing and a renewal write
  the admission there and save the database before the certificate is handed over, as before, and
  KeePass's group tree and entry list are rebuilt once the write has landed, which puts the selected
  group's entries in place of search results the operator was looking at. `DELETE
  /gate/certificate`, `pykeepassgate revoke`, gives up the certificate it is called with and no
  other, so a program's other certificates keep working, the certificate a renewal issued among
  them, as before. A pairing, a renewal or a certificate given up is answered `503 busy` while
  KeePass's entry dialog is open on an entry of the plugin's group that the write would delete or
  put back, and while Database Maintenance is open, as before. Nothing a program is sent changes.
  **A paired database is a KDBX 4.0 file until a profile is named.** A saved database that holds
  an authority and a program paired for no profile is a 4.0 file, where the first program paired
  used to make it a 4.1 file. A profile name still takes the file to 4.1, on the Profiles page or
  by approving a program for a name the database does not hold yet, and so does a renewal
  refused forever or until a day, since the profile names and those answers are kept in the
  database's custom data. `STORE.md` §612, §615, §626, §627 and §648.
- **The row of an expired certificate stays on the clients page**, reading `expired`, until its
  client is revoked or the authority is recreated or deleted. It used to go with the plugin's next
  change to the database, on the clock of the computer that made the change. A standing answer to
  a renewal of such a certificate stays with it. Nothing the plugin keeps about clients is removed
  for its age, so the plugin's group grows with every pairing and renewal. A record of a client
  that does not read is listed as `not counted`, and a certificate that more than one record
  admits as `listed twice` on each of its rows; neither admits anybody. `STORE.md` §615 and §643.
- **A renewal made from a certificate that was already given up is refused.** Where a certificate
  is given up on a copy of the database and renewed on another copy that had not learned of it,
  the renewed certificate stops working once the copies are synchronized, and so does whatever
  was renewed onward from it. The clients page
  lists such a certificate as `not counted`, with the reason in its tooltip, and the program
  pairs again. A renewal made before the older certificate was given up, on the copy where it was
  given up, keeps working, as `renew` followed by giving the old certificate up does. `STORE.md`
  §648.
- **Breaking: a field whose name begins `KPG.` is out of every program's reach, on whichever
  entry it is.** A program that made fields of its own whose names begin with `KPG.` renames them
  before the upgrade, in KeePass or through the API under 0.2, since 0.3 neither serves nor
  changes them. Such a field was served, listed and written like any other custom field. It is
  now in no read, no listing of fields and no `ETag`; reading, changing or stripping such a field
  is answered `404`, as a field the entry does not hold is, and `createField` answers a name with
  that prefix `404` whether or not the entry holds it. The prefix is the plugin's own: the store
  this release keeps a database's records in writes them as entries whose fields carry it, an
  authority's private key and the store's key among them, so a route that served such a field
  could hand a client what makes a record pass for the plugin's. The plugin's own group with
  everything in it, and every entry of that store wherever it sits, are left out of every route
  as the recycle bin is, and a group holding such an entry cannot be removed. An entry of the
  operator's is taken for such an entry only where its identifier begins with a tag the store's
  key gives, which a random identifier meets about once in one and a half million entries.
  Nothing else changes for a program, and a 0.2 client works with a 0.3 gate, `API.md` §411.
  `STORE.md` §633.
- **A gate answers `503 lost`, `503 split`, `503 outdated` or `503 stopped` to every request for a
  database whose store holds it back, and PyKeePassGate raises `StoreLost`, `StoreSplit`,
  `StoreOutdated` and `StoreStopped` for them.** `lost` is a database whose file holds no store key
  any more, as the entry that carried it deleted for good leaves it, answered only by a gate that
  was serving the database when the key went, with the host certificate it served with, and only
  until the database is locked or closed, after which nothing answers; `split` is a store under more
  than one store key, as a copy that made a store of its own leaves after a synchronize, until the
  operator keeps one; `outdated` is a store a later release raised, or that holds a record of a kind
  this release does not know, until the plugin is updated; `stopped` is a store that is not whole,
  as a record of the plugin's deleted or edited by hand leaves it, until the operator seals it
  again, and a store that has no room left for its next write, which the plugin's log warns of once
  the store has grown past half its room, `STORE.md` §643. Each is answered before a route is
  chosen, in the body and in `KPG-Unavailable`, as `locked` and `undecided` are, and ahead of
  `undecided`. A pairing, a renewal and a certificate given up are answered the word as well where
  the store came to hold the gate back by the time of their write, as while the operator read the
  dialog: nothing is issued or written, an invitation is not spent, and the approval is used up.
  Where a second authority arrived in the database by then, they are answered `503 undecided` the
  same way, and a request already under way when it arrived is answered `503 undecided` as well,
  each only while the request's own authority is among the authorities standing: a request whose
  authority is gone is answered `409 no invitation` or `401 unpaired`.
  None is waited out, since each ends by an act of the operator's. The routing document
  declares `frozen` beside them, which a later gate sends and this gate never does, and the client
  raises `StoreFrozen` for it. A 0.2 client takes each for the gate being unavailable, `API.md`
  §411. `STORE.md` §656.
- **Fixed: a press on a heading of the clients list no longer puts its rows in another order.**
  The list sorted itself at such a press, as a grid's text columns do unless told otherwise, while
  each row was still acted on by its place before the press. The X beside a client then took
  another client's admission away at OK and left the client it stood beside admitted, a row's
  menu lifted the answer standing on another certificate, and the Profiles column showed another
  row's profiles. The headings now do nothing when pressed. 0.2.0 and 0.1.0 have the fault: there
  the page acts on the rows it shows only while no heading was pressed since the dialog opened.

## 0.2.0, 2026-11-17

0.2 breaks programs written for 0.1, the single time the rule of `API.md` §411 is set aside, so what
makes a 0.1 client, a program built on PyKeePassGate, a script around its command line or an
operator's setup change comes first, each item marked **Breaking**. Upgrading means moving the
plugin and the client together: a 0.1 client cannot read the answers a 0.2 gate writes as it sends
them, and a 0.2 client refuses a 0.1 gate. The plugin needs KeePass 2.62 or later. A database a 0.1
plugin wrote is served as it stands, with nothing to migrate, as the items on the authority, on
revocations and on tags below state: every entry's `ETag` handed out by 0.1 fails its next
`If-Match` once. The audit and the plugin's log stay where they were. What other programs can do to
a paired database is under **Known limits** at the end.

- **Breaking: the plugin is built against KeePass 2.62 and needs that release or a later release.**
  It was built against 2.61.1. A plugin loads in the KeePass it was built against and in every later
  release, since a KeePass redirects every earlier version to itself, and in no earlier release:
  this build does not load in KeePass 2.61.1. Before the pin moved, the plugin as built against
  2.61.1 was run in KeePass 2.62, and the end to end tier passed there, so a 0.1 plugin goes on
  working in 2.62. Nothing the plugin reads of KeePass by name changed between the two releases.
  KeePass 2.62 writes the copy of a database's custom data that its Database Maintenance dialog
  holds back whichever way the dialog is closed, the close box included, which the plugin's refusal
  of a write while that dialog is open already covers. `FINDINGS.md` section 8.
- **Breaking: an answer that grows with what the database holds is written as it is sent and never
  refused for its size, and no write is refused for an entry's size.** Each listing goes out with
  `Transfer-Encoding: chunked` and no `Content-Length`, and so does a read of an entry or of a
  single field, `readEntry` and `readField`. Reading an entry built an answer of every string it
  held, at several times their size in memory inside KeePass, measured at 99 MB alive at once for
  two hundred protected fields of 65536 characters. Each value is now read as it is written, so what
  a read keeps alive is a piece of the value it is writing, whatever the entry holds. The listings
  of groups and of entries, which grow with the database, are written the same way: measured over
  invented entries, a listing of ten thousand kept 11 MB alive beside them while it went out, and
  keeps 0.03 MB. So are `listFields` and `listAttachments`, and the listing of an entry's fields is
  a single answer holding every name. An answer of a bounded size carries a `Content-Length` as
  before: a single group, a change, a refusal, and the routes about the gate and the caller. A
  request with `Transfer-Encoding` is still refused. No read of an entry or of a field is refused
  for its size. No write is refused for an entry's size, because a refusal that moves with what an
  entry's strings come to tells a client that may write and not read how long the values it is
  withheld are. So nothing bounds what a client holding `update` or `create` adds to an entry beyond
  what a request carries, and how much of an answer a client reads is the client's own setting:
  PyKeePassGate's `max_response_kilobytes`, 4096 kilobytes for a read unless raised. A client has to
  read a chunked answer. PyKeePassGate reads either framing, and raises `TransportError` for an
  answer cut short of its last chunk or of its length. The stand-in gate,
  `pykeepassgate.testing.FakeGate`, frames those answers in chunks as the plugin does. A 0.1 client
  does not read a chunked answer, and raises `ProtocolError` for every such answer of a 0.2 gate,
  `API.md` §411. `API.md` §412.
- **Breaking: a write meeting a lock is `503 locked` on every route.** A write whose database locked
  or closed once the rules had answered was `500 internal error` on the routes that looked nothing
  up before writing, and `503 locked` on the others. `500 internal error` is now a write KeePass
  answered with an error in a database still open, and `locked` is worth sending again once the
  database is open.
- **Breaking: only HTTP/1.1 is served**, and every other version is answered `505`, as the routing
  document already stated. HTTP/1.0 was read and served, and a 1.0 request carrying `Expect:
  100-continue` was sent an interim `100 Continue`, where RFC 9110 has a server ignore that
  expectation.
- **Breaking: a request without a `Host` header is refused with `400`**, as RFC 9112 requires of
  HTTP/1.1. It was served, so a request every other server refuses worked against this gate alone.
  The gate still routes by the name the TLS was made under and reads nothing in the header, and
  PyKeePassGate has always sent the header.
- **Breaking: a body sent to a path none of whose operations takes a body is refused as an
  undeclared target whatever the method**, as a query name nothing on the path declares already was.
  Sent with a method the path does not serve, it was answered `405` naming the methods the path does
  serve.
- **Breaking: the groups are listed depth first, as KeePass's tree shows them**: each group, then
  everything inside it, then the next. They came each group's own groups before the groups inside
  them, which is neither KeePass's tree nor one level at a time, where the document promised tree
  order. The entries already came group by group in that order.
- **Breaking: a new field or attachment name holding a Unicode format character or any default
  ignorable code point is `400 malformed`**, the zero-width spaces and joiners, the soft hyphen, the
  byte order mark, the Hangul fillers, the variation selectors, the combining grapheme joiner and
  the Khmer inherent vowels among them, beside the bidirectional controls already refused. A name
  holding such a character is drawn as the name without it, so a program holding `C` could add a
  second, unprotected `Password` beside the real `Password`: `Pass` and `word` around a Hangul
  filler were made with `201`.
- **Breaking: a new field or attachment name too long for its routes is `400 malformed`.**
  Percent-encoded, a name of a few hundred characters beyond ASCII, inside the 512 the body allows,
  took the route naming it past the 2048 characters of a target the transport reads, so it was made
  with `201` and could never be read, changed or stripped by name, every request for it answered
  `414`.
- **Breaking: a removal needs one profile to allow both sides of it.** Removing an entry or a group
  asks the group it leaves and the node itself, and a program holding two profiles that each allowed
  one side removed what neither of them may. One profile has to allow both now.
- **Breaking: a protected field is kept from a strip whenever any profile could put an unprotected
  value back.** The guard asked whether some profile could create there and whether some profile
  could create a protected value there, so a program holding one profile that could put an
  unprotected value back beside another reaching protected values stripped the field. Each profile
  is asked on its own now.
- **Breaking: an update naming what is already there is written, for a client that may not read
  it.** Such an update took no revision, moved no time and saved nothing, whoever sent it, so a
  client whose rule held `update` without `read` could send a guess at a field, a protected value, a
  group's notes or an attachment and learn from the `modified` a listing serves, or from how quickly
  the answer came, whether the guess was right. It is now skipped only for a client that may read
  every value it names, through any profile it holds, and written like any change for every other, a
  history revision included. A request naming nothing still writes nothing.
- **Breaking: every client holding `read` is handed an entry's `ETag`, taken over what that client
  may read.** A tag covered the whole entry and went only to a client whose one rule read all of it,
  attachments and protected values included, so a rule without `protected` was handed no tag for any
  entry holding a password, which is every entry KeePass makes, and every `If-Match` it sent was
  answered `412`. A tag now covers the entry as its client's rules open it: a protected value the
  client may not read enters as a mark beside its name, attachments enter by name and size for a
  client that may list them and by their bytes as well for a client that may read them, and not at
  all for any other. Nothing about the rule itself is in a tag, so a rule widened over an entry
  where nothing new comes into sight, `attachments` granted over an entry holding none, moves no
  tag. A listing and a read now show a client the same of an entry's protected title and URL, under
  one rule: `protected` beside `list` or beside `read` in one rule on the entry. A read serves them
  to a client holding `list` with `protected` beside `read`, where it withheld what the listing
  showed, a listing shows them to a client holding `read` with `protected` beside `list`, where it
  answered `null` for what the read served, and that client's tag covers both. An update naming a
  protected title or URL as it stands is skipped for a client either route shows it to, as for any
  value its client may read. Every route about an entry hands a client the same tag, and a write is
  asked against that tag and answers with it. Two clients that see different things of an entry hold
  different tags for it, and two that see the same hold the same. A client without `read` is still
  handed no tag, and its `If-Match` still fails. This reverses the earlier rule, and it moves the
  minor version by `API.md` §411, since a client that was handed no tag may now rely on being handed
  a tag. An entry's tag is also made differently, of a digest of each value kept for as long as
  KeePass keeps the value, so that it does not read every value for every request, and every entry's
  `ETag` handed out by an earlier version fails its next `If-Match` once. What it costs: a client
  writing a protected value it may not read is told the value changed in between only when the
  change moved the entry's `modified`, and a client whose rules are widened or narrowed over what it
  sees of an entry has its next `If-Match` fail once. Guessing a value by comparing tags stays
  closed, because a tag holds nothing derived from a value its client may not read: a guess written
  blind moves the tag the same whether the guess was right.
- **Breaking: PyKeePassGate's limit on an answer is given per read and in kilobytes, and an answer
  of a bounded shape is no longer under it.** `max_response_bytes` was a setting of the client and
  of a pairing, and every answer was read under it. It is `max_response_kilobytes` now, a whole
  number of kilobytes of 1024 bytes, 1 or more and 4096 unless given, with no alias for the name it
  replaces: `MAX_RESPONSE_BYTES` is `MAX_RESPONSE_KILOBYTES`, `api.response_limit` is gone, and
  `transport.response_limit` takes kilobytes. Each read whose answer grows with what the database
  holds takes it for that call alone: `groups`, `subgroups`, `group`, `entries`, `entry`, `fields`,
  `field`, `attachments`, `attachment`, `get` and `send`, and `refs.resolve` and
  `refs.resolve_entry` for the entries a reference leads to. Those two hand it to `client.entry` as
  a keyword, so a reader of a program's own given to them, `refs.Entries`, has to take a
  keyword-only `max_response_kilobytes`. `GateClient` takes it as what a read given none is under,
  so a program raises the limit for the attachment it knows is large and not for every answer. The
  command line's option is `--max-response-kilobytes`, on the commands that make such a read, and
  `transport.connect` takes the limit and `dial` by keyword only. A pairing, a renewal, the status,
  the identity, what a client may do at a node, a write and a revocation are read under a limit of
  their own, `BOUNDED_RESPONSE_KILOBYTES`, whatever the client was given, and `api.pair`, `Pairing`
  and the commands that make no such read take no limit: the answer to a pairing or a renewal was
  measured at 2399 bytes and grows with the profiles approved, so a limit lowered for the reads
  refused an identity the gate had already written and saved. An answer over that limit is a
  `ProtocolError` that names no setting, since none raises it. A refusal needs no room of its own:
  the largest refusal the gate sends was measured at 270 bytes on the wire, so a read under the
  smallest limit still reads a `404`, a `412` or a `503`, and the plugin's tests hold the largest
  refusal of each kind to that kilobyte. `FINDINGS.md` section 16 has the measurements.
  `docs/pykeepassgate.md`.
- **Breaking: a group read without its notes is an answer PyKeePassGate cannot read**,
  `ProtocolError`, as the document requires the member. `Group.of` told a read from a listing by
  whether the notes were there, so such a read came back as a listed group with its tag dropped, and
  `group` ended in an `AttributeError` traceback advising the call it had just made. `Group.of` is
  gone, and `Group.read` and `Group.listed` take its place.
- **Breaking: a listed entry has no `protected` and a listed attachment no `content`**: each raises
  `AttributeError`, as a listed entry's password does, where the entry answered `()`, naming nothing
  protected beside a withheld title, and the attachment `None`. A read carries both as before.
- **Breaking: an entry's standard strings refuse a look-alike name rather than read as empty**, as
  the reference resolver already did. `entry.password`, and the title, the user name, the URL and
  the notes likewise, answered `""` for an entry holding the string under no name spelled exactly so
  beside a name beyond ASCII, such as `Password` with a soft hyphen in it, which KeePass's collation
  reads as the password: empty where KeePass shows a value, and where the gate withheld a value.
  They now raise `AmbiguousName`, and an entry whose names are all ASCII reads a missing standard
  string as empty, as KeePass does. **`AmbiguousName` is new**, for a name only KeePass's own
  comparison can match, and the reference resolver raises it for the two cases of that kind it
  refused as `Unresolved`: a standard string held under a look-alike name, and a placeholder naming
  a field only in another case beyond ASCII. A program catching `Unresolved` for either catches
  `AmbiguousName` now. A standard string held under a name with a null in it is refused the same
  way, by the read model and by `refs.resolve`: .NET Framework's collation gives the null no weight,
  so KeePass reads such a name as `Password`, and the client answered empty, where KeePass has a
  value or the gate withheld a value.
- **Breaking: PyKeePassGate refuses an `If-Match` the gate could not match before sending it**: a
  tag without its quotes, a list the gate cannot read, or a list naming weak tags alone, which the
  gate compares strongly and never matches. Each went to the gate and came back as the `412` a node
  that changed gets, so a script that pasted a tag without its quotes read the node again, found the
  same tag and sent it the same way. The library raises `ProtocolError`, and `--if-match` exits 2.
- **Breaking: PyKeePassGate refuses a gate it does not work with, before it sends anything else**,
  with `IncompatibleGate` naming both versions, and the command line exits `1` with it. A client
  works with a gate of its own major version whose major and minor it does not lead, and the patch
  is never compared, as [`API.md`](API.md) §411 now states as part of the API's contract. The
  documents promised that the same major and minor make a pair, and neither half checked it. A
  paired client asks the status route once, before its first request, under the budgets of that
  request. A pairing is checked through the hello, which carries the gate's version beside its
  proof, as `version`, so that a client refuses a gate it does not work with before the request that
  puts it in front of the operator: a connection that arrived on an invitation reaches no other
  route, so the status route is out of its reach.
- **Breaking: a profile name is ASCII letters, digits, hyphens and underscores**, where it was
  anything but a separator, a colon or a null that did not end in a dot or a space. A dot is kept
  for what the client keeps beside a profile, as a renewal of `work` keeps `work.renewing` and
  `work.retired`, which a profile could otherwise be called. What else is refused failed only once
  the directory was made, or was a Windows device name such as `CON`. An identity paired under such
  a name by 0.1.0 is refused under it, exit 2 or `ValueError`, and is reached with `--directory`
  naming its directory.
- **Breaking: the command line reads and writes UTF-8 on every standard stream, whatever the code
  page, unless `PYTHONIOENCODING` names another encoding, and warns when its output is on its way to
  being misread.** On Windows, plain output written to a file or a pipe was encoded as cp1252 and
  ended in a traceback at the first name beyond it, part way through a listing, and a value piped to
  `--password`, `add-field` or `update-field` was decoded as cp1252 and stored as other text. A
  piped value that is not UTF-8 is now refused, exit 2, and a byte order mark at its start is
  dropped. `PYTHONIOENCODING` decides the encoding of all three streams, and of input and output the
  error handler, as Python reads it. On Windows, plain output holding a character beyond ASCII that
  goes to a file or a pipe while the console decodes another code page, which is how Windows
  PowerShell and PowerShell capture a program's output, is warned about once with
  `OutputEncodingWarning`: `$value = pykeepassgate field <uuid> Password` held other text for such a
  value, and nothing failed. The client's page stated that whatever read the output read UTF-8, and
  now gives the console setting that makes PowerShell do so.
- **Breaking: input that ends before a line is refused rather than sent as an empty value**, exit 2
  with nothing sent: a producer that printed nothing, a closed standard input or `< /dev/null` gave
  `update --password`, `add --password`, `add-field` and `update-field` an empty value, which they
  wrote and reported done, exit 0. An empty line is still the empty value it was sent as. `pair`
  given no invitation that way is refused the same way, where it was refused as a malformed token,
  exit 1.
- **Breaking: a value piped in over more than a line is refused**, exit 2 with nothing sent. It was
  cut to its first line and stored, so a key in PEM was stored as its `BEGIN` line and reported
  done. A value piped in is read to the end of input now, so whatever sends it must close the pipe,
  which the help of each command that reads a value names.
- **Breaking: `status` answered `ok: false` prints `not serving`** where it printed `serving`, and a
  sentence on standard error, where it printed nothing there and exited 1.
- **Breaking: Ctrl+C ends a command with exit code 130 and the line `interrupted`**, and a reader
  that stops reading, as `head -1` does, ends it with exit code 1 and nothing on standard error.
  Both ended in a traceback, and the second with exit code 120 besides. The wait `pair` and `renew`
  spend on the operator ends within half a second of Ctrl+C on Windows as well. PyKeePassGate waited
  for the gate's answer in a single wait on the socket, which Windows wakes for no Ctrl+C, so the
  interrupt took effect only once the operator had answered, and an approval went with it: the
  command kept nothing, while the gate had spent the invitation and saved a record for a key nobody
  held, and a renewal left a second record for the client the same way. Every read now waits on the
  socket half a second at a time, and still waits out its whole timeout. Ctrl+C still ends only the
  client's side: the request stands at KeePass until the operator answers it, which the client's
  page states, with what to ask the operator to do.
- **Breaking: plain output escapes a line break wherever it is**, in an identifier, a subject or a
  directory as well as a name or a value: each command escaped the values it named, and the rest
  went out as they were.
- **An attachment is read at any size, and a write still takes 512 KiB.** A read of an attachment
  over 512 KiB was refused with `413`, since its answer was built whole and kept about eight times
  the attachment alive at once, so a file attached in KeePass could not be fetched through the gate.
  `readAttachment` is now written as it is sent, and the attachment is read a piece at a time from
  where KeePass keeps it in memory, with no copy of the whole: the answer for 64 MiB was written in
  about a tenth of a second with a tenth of a megabyte alive, against a quarter of a second and 491
  MB, each measured into a stream that keeps none of it, with no TLS under it and no tag taken. An
  entry's tag covers every attachment its client may read, at any size. A write takes at most 512
  KiB as before, since it arrives in a request's body. A protected attachment that KeePass keeps
  through a cipher another KeePass plugin put in place of Windows' memory protection is read through
  KeePass's own read, a copy of the whole attachment for as long as the read takes, and so is every
  attachment on a KeePass that keeps an attachment otherwise and every protected attachment on a
  Windows the read in place does not fit, which the plugin checks as it loads and logs as a warning.
  PyKeePassGate holds a whole answer under `max_response_kilobytes`, so an attachment over about 3
  MiB needs it raised, and its stand-in gate frames an attachment as the plugin does. `API.md` §412.
- **A field's value is read and written a piece at a time, as an attachment is.** A read of a
  protected value decrypted a copy of the whole value and decoded it into a string, so a long
  protected value was alive as text for as long as its answer was sent, and its bytes beside that
  while it was decoded. It is now decrypted, decoded and written 16 KiB at a time from where KeePass
  keeps its bytes: a protected value of 64 MiB was read with 0.2 MB alive, against 128 MB, in the
  time it took. A value KeePass keeps as text is handed over as that text and escaped a step at a
  time, where a long value with nothing to escape ahead of a line break was copied whole to be
  written. Reading this way changes neither the answer nor the `ETag`. On a KeePass or a Windows
  where that cannot be done, the plugin reads through KeePass's own read, a copy of the whole value,
  and logs a warning as it loads. `DESIGN.md` §145.
- **A protected value read or written through the gate stays encrypted in KeePass's memory.**
  KeePass keeps a protected value encrypted in memory until something reads it as text, and from
  then on keeps the text. The gate read every value that way, and an entry's `ETag` reads every
  value its client may read, so a listing of an entry's fields left every protected value of the
  entry in the open in KeePass's memory until the database was locked, and a protected value written
  through the gate was in the open from the start. A protected value is now read from the encrypted
  bytes KeePass keeps for it, with every copy of those bytes overwritten once it has been digested
  or decoded, and written so that KeePass encrypts it at once, and a protected title or address is
  read only by a listing that shows it. A value a client is served is still in the plugin's memory,
  as text that cannot be overwritten, until that memory is reused.
- **A program reading the database while KeePass changes it reads one tree, a read is decided and
  served at one moment, and a change is decided as it is made.** A read ran beside KeePass's own
  work and could miss a group or an entry that was there, or meet a list KeePass was changing and be
  answered `500`, and the rules were asked before a request's handler ran, so KeePass could move in
  between. A rule taken away, a profile removed or a client revoked after the rules had answered was
  served regardless, from the database as it stood after the edit, so a value typed into an entry
  once its group was sealed went out to the client the seal was for. A change that waited behind a
  save wrote over a string marked protected in the meantime for a rule without `protected`, or
  removed an entry by the rule of the group the entry had just left. A request that changes nothing
  is now decided with KeePass's thread held still once, from the caller's admission record to what
  it is answered with, and a change is asked about again inside its own turn, where that answer is
  acted on and written to the audit in place of the earlier line. A read's `ETag` and its content
  are of one moment as well, where a tag could be older than what it arrived beside. A read waits
  first for whatever the operator is typing or clicking and for KeePass's own timers, and a read
  whose turn does not come within ten seconds, or whose hold would outlast five, is answered `503
  busy`. The longest such hold measured is about thirty milliseconds, listing ten thousand entries.
  An entry's values are read, its tag taken and a listing written once the hold has ended, from what
  the database held inside it, so a read does not hold KeePass for longer the more an entry holds or
  the more a listing writes.
- **A client revoked while its request waited for KeePass is answered `401 unpaired`**, as its next
  request is, where the request was served as though still admitted. A database that came to hold
  two authorities in that wait answers `503 undecided` the same way.
- **A read asked for while KeePass merges waits for the merge to end.** A synchronize, an import and
  XML Replace let messages through between one step and the next, and a read was served there from a
  database part way between the two being merged: entries not yet arrived, not yet moved to where
  the merge puts them, or not yet deleted. Such a read is now answered from what the merge leaves,
  once it is over, or `503 busy` when the merge takes more than ten seconds, which the client waits
  out as it does any other `busy`. A read during a save is served as before.
- **A change to an entry that reached the disk is never answered `503 busy`.** Where the rules
  cannot be asked about the entry as the change left it, the change is answered without its tag,
  which a read hands back, rather than `busy`, which would have its client make the change again.
- **The routing document's `ETag` no longer claims that no listing carries a tag**: a listing of an
  entry's fields or attachments carries the entry's, as the routes already declared.
- **An entry's tag is the same whatever culture KeePass runs under.** Its strings and attachments
  entered the tag in the order KeePass keeps them, which is by the current culture as the file
  loaded, so a change of regional format or of Windows' collation between two sessions changed the
  tag of entries nobody had changed, and a write under the earlier tag was refused `412`. The names
  now enter in ordinal order.
- **A change whose answer cannot carry its tag is taken back out before the failure goes on.** A
  write answering with the tag it left took that tag after changing the node and before anything
  could undo the change, so a decryption that failed while the tag was taken left the change and its
  history revision in memory behind a `500`, unsaved and unmarked, for the next save to write.
- **An attachment that failed to decrypt once is tagged the next time a tag is asked for.** The
  failure was kept in place of its digest, so every tag of every entry holding that attachment
  failed again, answered `500` and counted under Tools, until KeePass let the attachment go.
- **A write digests its node only where something reads the digest**: an `If-Match` naming a tag,
  and an answer carrying a tag. Every write asked either took the node's tag on KeePass's thread,
  under the writer's lock, including a write under `*`, a condition naming no tag, and a change
  answering with the tag it leaves, which took a second.
- **The key a node's tag is made with is overwritten when its database stops being served**, as the
  rest of the host's material is. It is derived from the host's private key, and it was let go
  without being overwritten, along with the array it was derived into.
- **Every copy of a stored certificate and its key is overwritten once it has been read or
  written**, as the copy the authority keeps already was when a database locks. Each read of the
  authority or of the host certificate left copies of a private key in the clear until the memory
  was reused.
- **A protected value withheld is written down by the walk that withheld it**, and a strip of a
  protected field refused because some profile could put an unprotected value back is written down
  at all. A handler decided on its own and asked the rules again only to write the line, so a rule
  changed in between left a withheld value unrecorded, and that strip, decided profile by profile,
  was recorded by a walk pooling every profile, which permitted it and wrote nothing.
- **A write that loses sight of its entry or group before writing writes nothing.** Write routes
  asked whether the caller may reach protected values only when their own lookup found the node, and
  handed the write on when it did not, though the write looks the node up again. An operator moving
  the entry at that moment let a rule without `protected` write a protected value, or strip a
  protected field it could put back unprotected. Such a write is answered `404` now, as a read is,
  or `503 locked` when the database locked.
- **A write that loses sight of its entry while asking which strings are protected writes nothing
  either.** Changing an entry, writing a field and stripping a field ask a second lookup which of
  the entry's strings carry their own protection, and that lookup answered "none" when it missed, so
  the write went ahead: a rule without `protected` could overwrite, or strip, a string protected by
  its own flag rather than by the database. Such a write is answered `404` now, or `503 locked` when
  the database locked, as a miss of the first lookup is.
- **A write to an entry is refused `503 busy` while KeePass's own attachment editor is open on an
  attachment of that entry's.** The editor holds the attachment and not the entry, and KeePass
  writes the edited bytes back onto the entry as it closes, so a removal of the attachment answered
  as done came back, and an update was overwritten, with nothing to show for it. Since the editor
  does not tell which entry it came from, every entry holding an attachment of that name is refused
  while it is open.
- **Nothing is written to a database while KeePass's TAN wizard is open on it**, and a write answers
  `503 busy`, pairing and renewal included. The wizard adds its entries from inside its own OK, and
  KeePass's save handles messages as it goes, so an OK pressed during the save a write makes added
  to the list the save was walking: the save threw, KeePass showed its save warning, and the write
  was answered `500 not saved`.
- **A change waits for KeePass to finish what it is doing**, a save, an import, an export or XML
  Replace, where it was answered `503 busy` straight away, and is answered so only if KeePass is
  still at it half a minute later. Under a message box in KeePass it is answered `503 busy` at once,
  since nothing happens in KeePass until the box is closed.
- **A Windows lock, a user switch, a suspend or a change of remote control that arrives while a
  write's save is running locks KeePass once that write has been answered**, where KeePass is set to
  lock for it. KeePass drops such a lock while it is saving, so it was lost for good and the gate
  went on serving the database. The plugin now makes it through KeePass's own lock, which still
  keeps the workspace unlocked while a dialog of KeePass's is open.
- **A write whose database KeePass closed inside its save is no longer taken for saved and
  verified.** An operator's trigger on saving that closes the active database, or another plugin's
  save handler closing it, closes the database before the save returns, and the close cleared what
  the plugin reads to know that the file holds the change. The plugin now logs that whether the file
  holds the change is not known, leaves the change in place and keeps the standing answers owed. The
  write's connection is closed with its database, so the client is answered nothing and reads a
  connection that ended.
- **A database left marked unsaved after a failed write shows the unsaved mark**, in KeePass's title
  bar and on its save button. A save KeePass abandoned before writing, over a file another program
  rewrote, returned without redrawing the window, and the change taken back out left the flag up
  with nothing on screen to show it.
- **A removal whose save fails is put back where it was, and every change taken back out puts the
  usage count back too.** The undo put an entry or a group back at the end of its parent's list,
  below the recycle bin under the root, and left each node a failed change touched counting one more
  use, in a database then marked as holding nothing unsaved, for the next save to write.
- **The first recycle bin a removal makes is no decision about the bin.** The plugin stamped the bin
  setting's time when a removal through the API made the first bin, which KeePass's own first delete
  does not, so the next synchronize turned the bin back on over an operator who had turned it off on
  another copy to make removals final.
- **A removal through the API stamps what KeePass's own delete stamps.** It marked the removed entry
  or group as modified, and a group's parent too, where KeePass's delete moves no modification time.
  A merge reads that time as the newer edit, so the next synchronize took the parent whole from this
  copy, undoing a rule the operator had narrowed on it in another copy, and filed another copy's
  edit to a binned entry in the entry's history. A removal no longer changes its parent's tag
  either.
- **A group holding the recycle bin and nothing else is refused for the bin.** Every listing leaves
  the bin out, so such a group read as empty to the program refused `409` and was logged as a group
  still holding something. The log names the bin now, and the routing document states that a group
  holding the bin cannot be removed.
- **The plugin stores a URL as it is written**, as it stores every other value. It refused a URL
  holding `{` or beginning `cmd://` with `400`, and accepts both now. KeePass's Open URL does more
  with a URL than those two cases: it replaces an environment variable in it, hands a path or a
  network share to whatever Windows starts for it, and with an override switched on puts the entry's
  own values into a command line. Telling which URL is safe takes what KeePass is set to do as well
  as what the URL holds, so one check given both is to take the place of a refusal that covered two
  cases. Until then, [`docs/access-control.md`](docs/access-control.md) warns against opening a URL
  a program may have written.
- **A pairing or renewal dialog that comes up late is given only what is left of the six minutes its
  connection has, and is not shown with under thirty seconds left.** A dialog waits behind another
  on the screen and for KeePass to show it, and used to give the operator the whole of its two
  minutes however late it came up, which could take the exchange to about seven minutes. The gate
  closes the connection at six, so an approval given that late was written and saved, the invitation
  spent, with no certificate handed back. The countdown on the dialog now starts from the time left
  once the waits after it up to the save are allowed for, when that is under two minutes, so a
  client waiting six minutes is still waiting when the save begins, and a request with under thirty
  seconds left is answered `503 busy` with nothing spent, as a request that never got the screen is.
  `DESIGN.md` §119.
- **A pairing or a renewal whose save outlasts what is left of its six minutes is still answered.**
  The connection's deadline ran on through the write, and the save of a large database, or of a
  database on slow storage, could take the rest: the socket closed over a record that landed, with
  the invitation spent and the certificate handed to nobody. The deadline is dropped where the
  record is handed to the writer, as an ordinary request's is when it has been read, and the answer
  is given the ordinary budget to be written in. The reference client still waits six minutes for
  the answer unless `connection_timeout` asks for longer, which a client expecting a slow save
  gives.
- **A renewal or a pairing writes its record only from the list of profiles the dialog showed, read
  again under the writer's lock.** A renewal read the list a second time after the dialog, on the
  connection's thread, and dropped every approved profile that read did not find, so a lookup that
  missed the list wrote a record carrying none of them; a lookup that missed it as the dialog was
  built offered nothing and did the same. A pairing compared the write against a read taken after
  its dialog, so a name the dialog showed as new, put on the list meanwhile, was written as though
  it had been listed. Each is now answered `503 busy` with the approval used up and the operator
  asked again, and so is a renewal whose profile was taken off the list while the dialog stood,
  which used to renew without that profile.
- **An approval the operator gives just as KeePass locks or its database closes is let go and
  answered `503 locked`.** It reached the plugin's memory after the lock or the close had let go of
  what was there, so the client was told `503 busy` with `heldUntil`, and the same pairing or
  renewal asked again once the database was open again was written without the dialog.
- **A pairing whose held approval another exchange on the same invitation wrote first is told `409
  no invitation`**, and the log states that another request took the approval. It was logged as held
  until its time ran out and answered `503 busy`, inviting a retry on an invitation that was already
  spent.
- **A renewal held for a busy KeePass keeps its approval when the retry cannot read the operator's
  standing answers**, and is told `503 busy` with `heldUntil` again. A read that failed once let go
  of the approval, so the next retry put the same dialog in front of the operator again.
- **A pairing or a renewal answered `500 internal error` is put to the operator again when it is
  asked for again**, as [`PAIRING.md`](PAIRING.md) §311 states for a certificate that could not be
  issued. The approval was kept for the rest of the pairing budget with nothing telling the client
  so, and the same request asked again was issued without the dialog.
- **A pairing or a renewal that a lock kept off the screen gets no `unanswered` line in the audit**,
  since nobody was asked, and the log states that it was never shown. It was answered as a dialog
  the lock closed, so the audit recorded the operator leaving a question unanswered that never
  reached them, and the log a dialog closed that never stood.
- **An invitation, a hello's challenge and an approval held for a busy KeePass end by elapsed
  time**, so setting the system clock back or forward neither lengthens nor shortens them. Set back
  an hour, it kept an unused code alive for the hour, and kept an approval writable without the
  dialog past its pairing budget. What the tab shows and a client is told is still the wall-clock
  time read when each was made.
- **A lock that closes an approval dialog logs it once**, however many databases are open. KeePass
  locks each of them in turn while the dialog stands, and each logged again that the request in
  front of the operator was closed unanswered.
- **Closing a database closes the pairing or renewal dialog standing for it**, as unanswered, as a
  lock does, and the log states that its database closed. The dialog stayed up over the closed
  database, offering an Approve that could do nothing. Closing another database still leaves it
  where it is.
- **Closing a database spares the pairings and renewals waiting on its operator**, as a lock does,
  so a client whose dialog the close took away, or whose request was still waiting for the screen,
  is told `503 locked`. Its connection was ended with the database's others, and the client saw it
  close without an answer. Every other connection to that database is still ended.
- **A list of profiles in another shape is refused by pairing and renewal**, as a list that does not
  parse is. A `Profiles` spelled with a capital, a `profiles` that is a string, a member beside it
  or a name the list cannot hold read as fewer names than the list holds, or none, so a renewal
  wrote its client a record with no profiles and a pairing wrote its own names over the list. Only
  another program or a hand edit writes such a list.
- **A pairing, a renewal or a program's revocation of itself that reached the database is always
  written to the audit.** A defect in KeePass or in another plugin, raised while the database was
  saved or while KeePass's window was redrawn, ended the request with the change in force and no
  line for it. The line is written before the window is redrawn now, and a save that ends in such a
  defect is settled as the database leaves it before the request fails: a change the file received
  is written down, and a change it never received is taken back out, which also keeps an ordinary
  change answered `500` from being made twice by its retry.
- **A program's revocation of itself whose database closed inside the save before KeePass wrote the
  file is not written to the audit.** A trigger on saving that closes the database, answered by
  discarding the changes, left no database holding the revocation and a file still admitting the
  program, under an audit line recording that it had revoked itself; the next open admitted it
  again. The line is now written for such a database only when KeePass reported a save of it that
  landed.
- **A pairing or a renewal whose record the file did not keep leaves the database marked unsaved**
  whatever the file reads as afterwards. Taking the record back out read the file a second time, and
  a file part way through a copy at the first read and whole at the second cleared the mark, over a
  record memory no longer held and the file might.
- **A certificate the database admits, turned away on a connection that arrived on an invitation, is
  written to the audit**, with the reason `this connection arrived on an invitation`. It is refused
  as before, and was the line missing from the audit's account of every certificate this database
  issued that an operation turns away.
- **A request refused for its request line, its target, its version, a byte that is not ASCII or a
  header ahead of its length is answered with a status the client can read**, `400`, `414` or `505`,
  and so is a head over its ceiling, `431`, and a locked database's `503` to any of them. The body
  behind such a request was left unread, so closing the connection brought a reset that discarded
  the answer, and a client whose body did not arrive with its head saw the connection fail as though
  no gate were there.
- **Each connection is served on a thread of the gate's own, never a thread of KeePass's thread
  pool**, which KeePass waits on from its own thread: its managed Argon2, used without its native
  library, blocks a save or an unlock on lanes it queues there. Thirty-one writes at once froze the
  first save for seventeen to twenty-two seconds and answered some of them `503 busy`, and
  thirty-one connections that sent nothing, from a peer that proved nothing, held KeePass's own
  background work back for about ten seconds.
- **Closing or unlocking a database ends only that database's connections, and closing it lets go of
  only its own approvals.** Both ended every connection, so closing or unlocking a second database
  reset every request to the first, and closed the connection of a pairing or a renewal waiting on
  the first database's dialog, which the operator then approved into a closed socket: the record was
  written and the certificate never reached the client. A close also let go of every approval kept
  for a busy KeePass, so a pairing approved for a database still open was told that its database was
  locked.
- **A port the gate could not take, and a gate that stops, leave no socket open behind them.** On
  .NET Framework a refused listener kept the socket it was made with, and a stopped listener opened
  a new socket, each held by the password manager until a collection.
- **The gate closes every connection it is serving when the plugin stops, even after its accept loop
  failed.** A loop that ended for a reason nobody chose had already given the port up, and the stop
  that followed closed nothing, so connections accepted before the failure were served on past the
  plugin's teardown.
- **A run of connections naming nothing an open database answers to writes one log line a minute**,
  the first at once and the rest counted into the next, as refusals above the connection ceiling
  already were. Each wrote a line, and such a connection needs no key, certificate or invitation, so
  one loop of them pushed the plugin's log and the file kept before it out of existence in about two
  minutes. Each reason, no name, no match or two databases at once, is paced on its own.
- **A client that sends anything after its request still reads the answer.** A second request
  pipelined behind the first, a line break written on its own or a TLS half close was left unread,
  so the connection closed with a reset, which threw away an answer the client had not read yet, a
  landed write's `201` among them. The gate now shuts its sending half first and reads and drops
  what still arrives, for up to two seconds of quiet, before it closes. The two seconds are counted
  from the gate's last write, so a client still reading a large answer that sends anything after
  them meets a reset and loses what it had not read. `DESIGN.md` §110.
- **An answer its client reads too slowly is cut off at the connection budget**, counted from the
  moment it starts being written. Only the socket's send timeout bounded it, which covers one TLS
  record, so an admitted client reading a large answer a record at a time held a thread and a
  connection slot for minutes. A pairing or a renewal refused or answered `503` before its write
  keeps the longer deadline it had, and a pairing or a renewal answered after its write is given the
  connection budget for its answer as every other connection is.
- **A database is served as soon as KeePass has unlocked it**, while KeePass is still asking its own
  questions about it: a master key due for a change, weak key derivation, duplicate identifiers.
  Until those were answered, the database and the certificate answering for it while locked both
  answered to its name, so every connection was dropped and the log claimed two databases were open.
- **A translated KeePass is read the way it reads itself.** Where KeePass's language and Windows'
  regional format sort letters differently, Danish and English among them, the gate looked names up
  in the wrong order and missed what was there: an admitted client was refused `401`, a field was
  served empty and an attachment answered `404` though both were listed, a node's own seal was
  passed over for the grant above it, and two authorities with no retirement were served as a single
  authority. Every connection is now served in KeePass's own culture.
- **A program asking who it is as KeePass locks is told the database is locked**, as every other
  route tells it. A lock between the gate reading its record and reading the database's profiles
  left the list naming nothing, and `whoami` answered that the operator had taken every profile
  away.
- **A `503` names its word in a field as well as in its body**, `KPG-Unavailable: busy`, `locked` or
  `undecided`, which the routing document declares, and so does PyKeePassGate's stand-in gate. The
  answer to a `head` carries no body, so `GateClient.head` met every `503` as a `GateUnavailable`
  with no word and never spent the retry budgets it takes. It now reads the field and waits a `head`
  out as it does a `get`.
- **`GET /gate/whoami` answers `profilesUnreadable`**, always present and true only while the
  database's list of profiles will not read. A damaged list names fewer profiles than it held, so
  `removedProfiles` then held names nobody removed, and a program had no way to tell that from the
  operator taking them away.
- **A refusal that profiles a damaged list lost would have allowed is written down as `profile list
  unreadable`**, with `removedProfiles` naming them, where it was `profile removed`. A list of
  profiles that will not read names fewer than it held, and nobody had removed them, so the line
  sent the operator to name again profiles nobody took off.
- **The routing document and PAIRING.md name a list of profiles that will not read among the reasons
  for a pairing's or a renewal's `503 busy`**, which both answer before the operator is asked, since
  what either writes is taken from the list. Neither document named it.
- **The routing document states what an attachment write takes**: the bytes base64 encoded, at most
  524288 of them, and `400 malformed` for a string that does not decode or decodes to more. Only the
  read side stated the encoding.
- **The routing document's access routes describe a mask as what the rules grant at the node**,
  asked of the node alone, as the design documents and the `Access` schema already did. They
  described it as each operation's own answer, which a removal, asking the group above as well, is
  not.
- **The routing document no longer claims every audit line names both of a client's keys.** A
  refused certificate's line names its `admissionKey` alone and a pairing's line its `clientKey`
  alone, as `docs/audit.md` already stated, so a program revoked and still trying finds those lines
  by its admission key.
- **The routing document no longer promises a save behind every `200 ok`**: an update asking for
  nothing, or, from a caller that may read every value it names, for nothing the node does not
  already hold, changes and saves nothing and is answered the same. It states too that an `auto`
  expiry set since KeePass last opened the file is written once more, to the second, the first time
  a client writes back the moment it read.
- **The routing document states what the group and entry listings leave out and in what order the
  groups come.** The sweeps of every group and every entry, and a group's own groups, leave out the
  recycle bin and what it holds, which nothing on those routes stated.
- **The routing document states which characters a profile asked for at pairing may hold**: ASCII
  letters and digits, `-` and `_`, and never `default`. It gave only the length, so a client built
  from it learned the rule by being answered `400 profile`.
- **The routing document answers a field or attachment named in a path as the gate does**: a name
  the database cannot keep apart from a name the entry carries is `404` there, where the document
  promised `409`, which only a name in a body gets. A name that decodes into a climb, such as
  `a/../b`, is named among those a path cannot address, and is answered as an undeclared path.
- **The routing document's `Malformed` no longer promises a body naming which refusal it was**:
  every case is answered `malformed`. It no longer lists a surrogate without its pair, which never
  reaches it, and a field's value states that whitespace and nothing else is refused, where it
  stated only that line breaks and tabs are allowed.
- **The routing document names the error words worth matching on**, `not saved`, `not verified`,
  `not recorded` and `internal error` on a `500` among them, where it named a few and called the
  rest told apart by the status. It tells `409 no exchange`, after which the same invitation starts
  again from a hello, from `409 no invitation`, and a revocation's `500 internal error` covers a
  revocation that went wrong before anything was saved as well as a revocation never attempted.
- **The routing document names the transport's answers before routing as the transport gives them.**
  It promised a `501` for a method the gate does not implement, which the router answers instead,
  and named no `501` for a `Transfer-Encoding`, the only reason a `501` is sent; it called every
  such `error` a sentence, where the `417`'s is the one word `expectation`.
- **The plugin issues its certificates through Bouncy Castle, with names in UTF8String**, where
  Windows issued them with names in PrintableString, and no key of the plugin's passes through
  Windows any more. A database whose authority Windows issued keeps working as it was: the authority
  goes on issuing, the clients it admitted go on being served, and a certificate it issues now names
  it exactly as it names itself.
- **A stored authority somebody crafted costs the time of refusing it**, not the time it asks for.
  An authority over 64 KiB, asking for more than 10,000 iterations of its key derivation or holding
  more parts than a stored pair has is refused before any of that work is begun.
- **Another plugin's older Bouncy Castle stops the plugin starting**, naming both copies. Every 2.x
  release of Bouncy Castle is one assembly to .NET, so a copy another plugin loaded first was the
  copy this plugin ran on, whatever it lacked.
- **A database holding a crafted authority key stops only itself being served.** A key named
  `KPG.Authority.` with no digest, beside the database's own authority, made the gate drop every
  connection to every open database for as long as that database was open. It now waits for the
  operator to choose between its authorities, as a database synchronized from copies that each made
  an authority does, and every other open database is served. So is every other open database beside
  a database whose stored authorities or certificates will not read at all, which is left unserved
  alone.
- **A revocation names the authority that issued its certificate, and goes once that authority is
  retired or gone.** A merge with a copy from before a recreation brought revocations back that
  nothing took away until their certificates expired, with no row on the Clients page; the plugin's
  next write of its own takes them away now. A revocation written before this names no authority and
  still goes when its certificate expires.
- **The Clients page counts the revoked clients a merge brought back by client, not by record.** A
  renewed client whose certificates were both revoked and both brought back was counted as two
  revoked clients.
- **An answer to which authority to keep retires nothing its window did not list.** KeePass runs a
  periodic trigger under that window, so a synchronize could bring a third authority while the
  window listed two, and **Keep this authority** then retired the third as well, with every client
  it admitted, for good and with nobody having been shown either. An answer now carries the
  authorities its window listed and is not written to a database holding any other set, an authority
  more or an authority fewer: the window opens again, listing the authorities the database holds,
  and does so after **Cancel** as well. A database that an answer given on another copy decided
  meanwhile is served as that answer left it, and a message names the authority it serves, where it
  used to state that nothing is served. The question on the KPG tab is held to the same rule.
  `CA.md` §204.
- **The window asking which authority to keep waits for a synchronize to end.** A synchronize that a
  trigger starts as KeePass opens its first database is shown in KeePass's progress window, which
  pumps before KeePass blocks its own interaction, and the window was put up there: over a
  synchronize that had not merged, with no main window on the screen, for an answer the plugin then
  refused because of the progress window, and gave up on with a message when the synchronize went on
  for a few seconds more. The window now waits on the same answer about KeePass that the write of
  its answer is refused on, so it opens once the synchronize is over. That answer now covers a merge
  another KeePass plugin runs as well, which nothing of KeePass's announces: the window does not
  open inside such a merge, and no answer is written inside it. `CA.md` §204.
- **The message that an authority was not kept waits for KeePass to finish what it is doing.** An
  answer the plugin cannot write is tried again for a few seconds and then given up, with a message
  sending the operator back to the KPG tab. That message was raised the moment the seconds had
  passed, which could be in the middle of a save or a synchronize of KeePass's own, and KeePass then
  waited for it: a save was measured standing still until the message was closed. The answer is
  still given up after the same few seconds, and the message is shown once KeePass is no longer part
  way through an operation. `CA.md` §204.
- **The authority chooser asks about every database even when asking about one database fails.** A
  round marked every database it would ask about before the first question, so a defect asking about
  one database left the other databases unasked, and unserved, until each was locked and unlocked.
  They are asked a moment later now, and the failure is still counted on the plugin's window under
  Tools.
- **The chooser warns before it is answered**: answer on one copy of the database, and synchronize
  before answering on another. Its button is **Keep this authority** rather than **Keep this one**.
- **The KPG tab shows a database whose every authority is retired as retired**, rather than as
  having no authority yet. Copies that each kept a different authority in the chooser, before their
  answers were synchronized, retire every authority, and the tab then sent the operator looking for
  a step they had missed. A delete, which retires an authority too, is shown the same way.
- **The window asking which authority to keep reads no more than the last eight megabytes of each
  audit file** for when each client was last seen. An audit that could not be set aside grows past
  that, and was read whole while KeePass's window waited.
- **An authority the operator chose while another write held the plugin's writer is kept once that
  write is done**, however long its save takes. It was tried for three seconds, while a save of a
  large database takes a minute and more, and the operator was then told it was not kept and sent to
  the KPG tab to choose again. A refusal that does not end by itself, Database Maintenance left open
  or KeePass busy with an operation of its own, is still tried for a few seconds and then told.
- **A database opened from a URL holding a character Windows forbids in a path is named by its
  file**, as KeePass names it. Its name threw, so the authority chooser asking which authority to
  keep failed as an incident under Tools without asking, and Choose authority on the KPG tab failed
  with a path error.
- **A lock closes the window asking which authority to keep**, as it closes a pairing or a renewal
  dialog, and unlocking asks again. KeePass locked under it, and it went on listing every
  authority's clients over the locked KeePass, modal over the window the operator unlocks in; and a
  choice whose database locked while it was being kept sent the operator to the KPG tab for a
  question the unlock was about to put again.
- **A retry of keeping the authority the operator chose stops when the plugin is let go.** It ticked
  once more as KeePass closed, and told the operator the authority was not kept.
- **Recreating or deleting the authority that fails part way tells the operator what may be gone.**
  The authority is retired before the records are taken away, and a failure in between was refused
  as though nothing had been removed, from a database that already served nobody.
- **A plugin that fails to start names the reason in KeePass's own box**, as a DLL missing beside it
  already did. It answered KeePass that it had not loaded, so KeePass left it out without a word and
  the reason was in the plugin's log alone, where it is still written. The log is made before
  anything else as the plugin starts, so a start that fails early has its line there too, and each
  step of the plugin's teardown runs whatever the step before it threw.
- **KeePass's start goes on past a defect in making the plugin's menu item**, which is left out of
  the Tools menu instead, and a dialog the plugin put a tab on is no longer held open by a defect in
  taking the tab off as it closes without KeePass announcing it.
- **A KPG tab that fails to build in a way the plugin does not expect is counted as an incident** on
  the plugin's window under Tools, and what the tab had made by then is let go of. It was logged as
  another dialog the tab could not be put on and never counted, and the controls, fonts and timer of
  the half-built tab were left to the garbage collector each time the dialog opened.
- **Nothing the plugin's own windows do ends in WinForms' error dialog.** Every handler on the
  window under Tools, the approval dialogs, the authority chooser and the KPG pages records what it
  did not foresee, as the plugin's other catch-alls do. Let through, such a failure could reach that
  dialog in the middle of a save or a lock of KeePass's own, which KeePass handles messages inside,
  and while it stood KeePass refused every lock and the gate went on serving reads.
- **A debugger attached stops wherever the plugin records a failure it did not foresee**, with the
  exception in hand, and continuing carries on as it would with no debugger: every such failure is
  caught, so a debugger was told nothing.
- **An audit line the plugin fails to put together is counted on its window under Tools**, each
  time, as the plugin's other failures it did not foresee are. It was logged once, and every later
  decision such a defect lost went unsaid.
- **A lock lets go of a database's keys and connections whatever closing the approval question
  did.** A failure there kept the host key and the authority in memory after the lock, kept the
  connections open and went on putting pairing requests in front of the operator.
- **A dialog the plugin could not note holds every write back until it closes.** It was left
  unwatched, so a revocation could land under Database Maintenance and be put back by that dialog's
  close.
- **Revocations pressed on the KPG tab land at OK whatever else fails there.** A failure while the
  clients were named for the audit dropped everything the tab held, revocations included.
- **A rule the grant editor could not store is never stored later.** It redrew before it stored, and
  a redraw that failed left the rule on the sheet, so the next edit wrote it, after the tab had
  reported it as not applied. A grant picker that could not be closed is reported as not closed,
  rather than as a picker that could not be opened.
- **A refusal until a date can be chosen across midnight on the eve of a certificate's last day.**
  Tomorrow was read more than once, and a midnight between two readings threw, so the choice went
  without a word and the renewal dialog stayed unanswered.
- **The audit is set aside at its bound only where its access control lists let it, whatever
  privileges KeePass's account holds.** In a KeePass started from a shell that enables the backup
  and restore privileges, as Git Bash run elevated does, a directory sealed for an append-only audit
  let the file be set aside and the previous file deleted, and then refused the next, so no decision
  was recorded from then on. A directory that lets files be removed and not added keeps the previous
  file too, which was deleted before the rename it then refused.
- **The plugin's log is set aside the same way**, so it goes on being written in a directory sealed
  for an append-only audit, which it shares by default. With the backup and restore privileges
  enabled it was given up for the session at its first roll there, and wherever its own ACL let it
  be removed, its previous file was deleted for nothing.
- **A `KEEPASSGATE_LOG_FILE` that is no path at all leaves the plugin's log where it would have
  been**, and the log's first line names the value. The log went on with no file, and nothing told
  the operator why.
- **The plugin's log is written again once it can be**, as the audit is, beginning with a line
  naming how many lines went unwritten. A single write it could not make, such as a write made while
  a program held the file without letting anybody write to it, ended the file for the session.
- **KeePass's window never waits on the audit file.** The lines for a pairing, a renewal or a
  program's revocation of itself that lands, and for the operator's revocation at **OK**, are handed
  to a writer of the plugin's own and reach the file in order with every other line. An audit on
  slow or unreachable storage held KeePass's window for its own write and for every connection's
  line ahead of it.
- **Nor on the plugin's log.** A line KeePass's own thread writes there, a line logged while a read
  holds that thread still among them, is handed to a writer of the plugin's own and reaches the file
  in order with every other line, so a log on slow or unreachable storage no longer holds KeePass's
  window for each line.
- **Closing KeePass waits two seconds at most for the audit's and the log's last lines**, so what is
  recorded and logged as KeePass closes reaches the files, and storage that does not answer holds
  the exit no longer than that.
- **Every certificate the gate refuses for a database shares one allowance in the audit**, rather
  than each counting against its own. A client keeps the certificates it renewed out of, and each
  brought a whole burst and rate of its own once it was revoked or had expired, so a client that
  kept them could write as many times the pace as it had certificates. No record names the client a
  refused certificate belonged to, so that allowance is apart from every client's own: a program
  keeping the certificates it renewed out of has one more allowance between them, however many it
  kept, and is recorded at up to twice its own pace, as `docs/audit.md` now states where it stated
  that they gave it nothing more. A client flooding on both pushes a line out of both files in about
  a quarter of an hour rather than half an hour.
- **No client is paced where the audit has no file to write**, as on an account with no local
  application data and no `KEEPASSGATE_AUDIT_FILE`. The pace keeps a client from rolling the file
  away, and with no file a client past its first thousand decisions was still held to ten a second.
- **An audit line's `at` is when it was decided, for a paced client too.** It was read once the
  client's wait was over, so a client past its allowance had its decisions and refused renewals
  stamped up to seconds after the rules or the operator's earlier answer had decided them.
- **A write or a read refused `412` is logged with its cause**, as a warning: the node no longer
  holding the tag named, an `If-Match` that does not read, an `If-Match` naming no strong tag, or a
  program the gate hands no tags. A write's `412` was logged as a node that changed whatever the
  cause, which sent an operator looking for an edit nobody made, and a read's `412` was written
  nowhere, so a program refused on its reads over and over showed nowhere at all.
- **A program's revocation of itself and a standing answer to a renewal rebuild neither the group
  tree nor the entry list**, which show neither, and the rebuild replaced any search results the
  operator had on screen. A standing answer that lands tells the Clients page, which draws the
  client's name red from it and went on showing it black until something else redrew the page.
- **The message at exit names the standing answers it lets go, and gives no cause.** It counted them
  and blamed KeePass being busy whenever they were given and nothing being written after them, which
  was untrue after a save that failed or a save made since, and promised a dialog for clients it did
  not name. It now names each certificate by the key the log names it by, and the log line names the
  authority each was given under.
- **Every save of a database writes the standing answers KeePass could not take**, the operator's
  own included. They rode on the plugin's own writes alone, so keeping an authority or pressing the
  KPG tab's OK and then saving went to the file without them, and the message at exit told the
  operator nothing had been written since. An answer is no longer written while Database Maintenance
  is open on its database, whose close took it away after memory had let it go, nor let go after a
  write whose file did not read back as written.
- **A later standing answer about a certificate is the answer that counts.** An answer KeePass could
  not take was written with the plugin's next write whatever the database held by then, so it went
  over the operator's own later answer, in the very write that had just put that answer there, and
  over an answer a synchronize brought in; and an answer until a date whose day had begun was
  written to refuse nothing. **Allow renewal** on such an answer was lost when a write landed before
  the settings dialog's OK. The waiting answer now gives way to anything written since it was given,
  and Allow renewal holds it back from every write until OK or Cancel.
- **A renewal refused in this session stays refused when an older answer until a date has passed.**
  An answer until a date that KeePass could not take, and whose day had begun, stood in front of a
  later "Refuse in this session" for the same certificate: the renewal dialog came up again on every
  request, and the clients page showed the client as refused by nothing.
- **An answer to a renewal given while Allow renewal waits for OK survives the OK**, and the clients
  page shows it. Allow renewal on an answer until a day that has passed lets the renewal dialog come
  up again before OK, and a refusal for this session given there was taken back by the OK as well,
  which had never seen it.
- **A standing answer KeePass was too busy to take is written by the next save even when the
  database's answer changed while the renewal dialog stood**, Allow renewal accepted or a
  synchronize among the changes. It was compared with what the database held when the dialog came
  up, so that save took the change for a later answer and let the operator's go unwritten.
- **A standing answer KeePass could not take is let go once its authority is recreated or deleted.**
  Held in memory until a write could take it, an answer about a certificate whose authority was then
  recreated or deleted was never written and never let go: it stayed held for the session, and the
  message at exit told the operator its client would be asked about again, which a client of a
  retired authority never is.
- **A write, a pairing or a renewal still in progress as KeePass exits, or still waiting for
  KeePass's thread when it closes, is logged as KeePass closing**, and the write is answered as
  failed. Each was answered as a timeout: the write logged as KeePass not answering in time and told
  `503 busy`, and a pairing or a renewal logged and audited as a dialog nobody answered, though no
  dialog was shown.
- **A pairing or renewal dialog that KeePass's exit takes away is in the audit as `unanswered`.**
  The line was written once the dialog's answer reached the waiting connection, which at an exit
  comes after the plugin has unloaded and stopped waiting for the audit's writer, so most exits
  under a dialog left no line. It is now written as a lock, a close of its database or the exit
  takes the dialog away. The waiting client is still told nothing at an exit: stopping the gate
  closes its connection before the answer exists, which [`PAIRING.md`](PAIRING.md) §308 now states,
  where it read as though every dialog taken away were answered `503 locked`.
- **A Windows logoff or shutdown is no longer held up by the message about standing answers never
  written.** KeePass unloads the plugin inside the session's end, and the message box waited there
  for an answer nobody was at the desk to give, until Windows gave up on KeePass. At a logoff or a
  shutdown the answers go to the plugin's log alone; an exit the operator starts still shows the
  message, and shows it once however often the plugin is told to stop.
- **KeePass's main window is hidden before the plugin tells of a standing answer lost at exit.**
  File, Exit closes the databases without redrawing the window, and the box naming a refusal of a
  renewal that no save took went up over it, so the window went on listing the closed database's
  groups and entries, titles, user names and URLs among them, until somebody pressed OK. With no
  database open, no inactivity lock cleared it. KeePass hides the window itself right after its
  plugins unload, and the plugin now takes that step before the box goes up.
- **A clients list emptied by revoking reads "No clients once OK is pressed."**, rather than that no
  client has been admitted to the database, which every client revoked there still reaches until OK.
- **The renewal dialog reads in fewer words**: "Approving issues a new certificate and saves this
  database. The certificate it holds now stays valid until the client gives it up.", "Profiles for
  the new certificate; uncheck any it should lose. The certificate it holds now keeps every one
  until it is given up:", and the countdown as the pairing dialog's.
- **The pairing dialog reads in fewer words**: what approving does, "Approving issues a certificate
  and saves this database. Revoke it later on the KPG tab.", what the list is, "Profiles it asks
  for; uncheck any it should not get:", and the countdown, "Times out in 115 s; nothing is issued."
  That the admission is saved before the client is answered stays in `PAIRING.md`.
- **The note under the KPG tab's pages reads "Changes apply on OK, then save the database."**, the
  same two things as before in fewer words.
- **A revocation or an Allow renewal that went through puts no sentence under the clients list.**
  The rows show it: a revoked row goes at once and a lifted name stops being red, and the note under
  the pages states that changes apply on OK. Only a revocation or a lift that did not go through
  puts words there, which leaves the list up to two lines it used to lose.
- **The line over the invitation code shows the minutes left and the identifier and nothing else.**
  That the code lives in KeePass's memory, that the first client to use it spends it and that Renew
  invite replaces it read the same on every visit, so they are the code box's tooltip now, and the
  client list has the line back.
- **With no code out, the clients page shows it in the empty code box rather than on a line over
  it.** The line stated what the box beside it already showed, and the client list has that line
  back. The gray text is never the box's text, so the copy button stays dead over it and nothing can
  copy it.
- **A renewal refused for this session shows on an open clients page at once.** The page draws a
  client's name red from every answer that refuses its renewals, and an answer for this session, or
  an answer the database could not take when it was given, is held in the plugin's memory, which
  nothing told the page about: the name stayed black until the page was drawn again for some other
  reason.
- **The access editor offers a profile named while its dialog is open.** A pairing approved over a
  group or an entry dialog can name a profile the database did not, and the editor read the
  database's profiles once, as the dialog opened: the new name was missing from its pick list, and a
  rule for a name put back stayed struck through as granting nothing. It reads them again when its
  tab comes forward and when a record lands.
- **A screen reader names every X on the KPG tab and the access editor by what pressing it does.**
  The columns of Xs on the clients, the profiles and the rules are headed by a painted bin, over a
  heading left empty, and a table names a cell by its heading and its row, so each X was read out as
  a row number and an X. The headings now hold Revoke, Remove profile and Remove rule, which are
  never drawn and take no room. The access editor's rule and profile pickers, and the renewal
  dialog's menu of ways to refuse, carry a name of their own as well.
- **On a Windows without Consolas the invitation code goes under the Invite button rather than past
  the page.** The code falls back to Courier New there, and at 100 percent a whole code no longer
  fits beside the button once it reads Renew invite, so the row ran 12 pixels past the tab. The box
  now takes a line of its own wherever a whole code does not fit beside the button.
- **The KPG tab's facts and the access editor's masks take the font of the dialog they are on.**
  Both were drawn in fonts made once, at the size the tab was built at, so on a dialog whose font is
  not the system's the bold values were smaller than the names beside them, and the masks smaller
  than the profiles.
- **The authority chooser and the clients page name UTC in the headings of their moment columns**,
  Created, Admitted and Last request on the chooser and Admitted and Until on the clients page, as
  the renewal dialog now names it beside its two moments. Every moment on screen is printed in UTC,
  and a bare moment read as local time. The chooser's Created is when the authority was made, where
  it read five minutes earlier.
- **The access editor's tooltips tell the operator what the row can do.** A profile cut in its
  column is shown whole on hover; an inherited row is no longer told to remove and recreate a rule
  it does not hold; a tab that cannot be changed, a history revision among them, no longer offers a
  choice or a Discard that does nothing; a removed profile's rule from above on a sealed node no
  longer promises that adding the name back puts it in force; and a revision's rules from above are
  named as what the groups grant now. Whether a rule narrows or widens the rule it replaces is
  judged by what each grants rather than by its letters, and a node deeper than the rules are read
  is named as such rather than as a broken rule above.
- **The access editor writes a node's rules back as they were written when edits cancel out.**
  Turning the seal on and off, or adding a rule and removing it, left the rules respelled, which the
  entry dialog takes for a changed entry, with a revision in its history, and a stored empty value
  read as a change as the dialog opened.
- **Creating and deleting an authority on one settings dialog leaves the database as it was.** The
  delete retired the authority the create had made, and OK wrote that retirement for an authority
  the file never held, a key kept for good that made the database read as a database that had
  retired every authority it had. Two recreates on one dialog did the same to the authority between
  them.
- **The clients page names a revocation and a lifted answer as waiting for OK**, which they do. The
  line under the list stated that the client could no longer reach the database, or that its next
  renewal raised the dialog again, while the dialog had changed nothing yet, and the plugin's log
  wrote the revocation and the lift down as they were pressed, where a Cancel left the lines
  standing. The log now names each as the dialog's OK lands it.
- **The Profiles page shows a stored list that will not read, and writes over it only when told
  to.** It showed such a list as naming nothing, with no warning, and its first Add or Remove at OK
  replaced the damaged value and every name it held. Add and Remove now stay off under a line
  stating that the list will not read, until **Replace it with a new list** is pressed, and the
  tab's OK leaves a list that turned unreadable while the dialog was open as it is.
- **A profile edit on the KPG tab takes back the code made on the dialog**, as Cancel does. The edit
  killed the invite button, but a code made before it stayed on screen, copyable and good, so a
  program could still pair against a list OK was about to change. A code kept by an earlier OK is
  not taken back and still pairs, and the band over the invite button now states so, where it told
  the operator to press OK and reopen the dialog to pair.
- **What the KPG tab and the authority chooser call clients are the clients the gate lets in.** The
  Recreate and Delete confirmations, the Profiles page's counts and the chooser counted every
  readable record, among them records of another authority, not for client use, not yet valid or
  expired, which the clients page marks as refused. They now judge each record as that page does.
- **The clients page takes away a record stored with no certificate.** A value under `KPG.Client.`
  with nothing after it was listed as a record that will not read, with an X that could never remove
  it. Only a hand-written or foreign file holds such a record.
- **Deleting an authority whose records outlived it asks in the words of what is there**: records
  that admit nobody, and nothing else changing. A Delete waiting for OK no longer shows the notice
  written for a create, to press OK and reopen the dialog to pair.
- **The authority chooser opened from the KPG tab reads the audit the plugin writes.** A relative
  `KEEPASSGATE_AUDIT_FILE` was resolved again against KeePass's working directory, so once that
  directory moved, every client's last request read as unknown or as another file's.
- **No stray key keeps an authority on the chooser.** Keep carried an access key, so a bare K typed
  while either button had the focus kept the selected authority and retired every other for good,
  and Enter on a focused Keep kept as well. Keep now takes a click or Space, as Approve does, and a
  database name holding `&` no longer makes a hidden access key on the pairing and renewal dialogs
  and the chooser.
- **The renewal dialog marks its two moments as UTC, and its refuse-until range runs where the
  operator is.** The moments read as local time with nothing marking them. The calendar ran from a
  local tomorrow to the date of the certificate's end in UTC, so far east of Greenwich the choice
  was grayed out a day early, and far west it offered a day whose refusal began after the
  certificate had stopped.
- **The pairing, renewal and refuse-until dialogs and the authority chooser scale their insets and
  gaps with the display**, where at 150 and 200 percent their text and widths grew and the space
  between them did not.
- **The Profiles page counts no rule in the recycle bin.** Nothing in the bin is reached through the
  gate, so its rules decide nothing, and the page counted them among the rules removing a name
  leaves inert and offered to put them back in force when the name was added again.
- **A KeePass that keeps a dialog's copy of a node's settings, or its database, as another type
  leaves the KPG tab on the group and entry dialogs read only**, with its reason, as a KeePass that
  renamed either does. The tab looked editable and refused every rule chosen on it as a copy the
  dialog had not made yet.
- **A group or entry dialog closed by another program while a grant picker is open no longer ends
  KeePass.** WinForms closed the picker after the dialog had gone, and the plugin disposed it inside
  its own close, which KeePass answered with its fatal error box before ending, without unloading
  the plugin. The picker is now let go of a message later, and what it held is dropped, since the
  dialog it was for has closed. An operator's own ways of leaving the dialog, a press outside the
  picker or Alt and F4, close the picker first and never reached this.
- **The clients page, the pairing dialog and the renewal dialog show every profile, whatever the
  length of its name.** A profile name may be sixty-four characters with no space in it, and on the
  clients page a name wider than its column pushed every profile after it below the row, where
  nothing drew it: a client holding `ops` after a long name showed no `ops` to the operator deciding
  whether to revoke it. Each profile now keeps a line of its own, a name too long for the column
  ends in an ellipsis, the mark "(removed)" is never the part that is cut, and the row's tooltip
  names every profile whole, marked as the row marks it. On the pairing dialog a row of wide letters
  ran past the edge of the list, and the mark after the name, "(new)" or "(removed)", was the part
  cut off, so an operator could approve a name without being told that approving adds it or gives it
  back to other clients. The list is now as wide as its widest row, and the dialog grows with it for
  such a name. The renewal dialog's list of the profiles a client holds is widened the same way,
  where such a name ran past its edge and two names alike but for their ends looked the same. The
  authority chooser's list of clients names each client and its profiles whole in the row's tooltip,
  where it showed no tooltip and a name its columns cut was shown nowhere.
- **KeePass's main window stays disabled under the plugin's dialogs**, the authority chooser, the
  pairing and renewal dialogs and the window under Tools. None of them holds KeePass's periodic
  triggers off, so a trigger that synchronizes or saves ran inside such a dialog and ended by
  enabling the main window behind it, where the operator could open the database settings, edit
  entries or close the database while the plugin's question still stood.
- **The message after a port change tells the operator that clients pair again**, rather than that
  they have to be told the new port. PyKeePassGate keeps the port it paired against and offers no
  way to change it, as the getting started page already stated.
- **A port typed outside 1024 to 65535 on the Tools window is refused rather than moved into the
  range.** The box held itself to the range, so 80 was saved as 1024 and 70000 as 65535 without a
  word, and the refusal written for the number could never be shown.
- **OK on the Tools window writes the port in its box while `KEEPASSGATE_PORT` names no port.** The
  next start then takes nothing, and the box offers 51776, but OK on it untouched answered that
  nothing had changed and wrote nothing, so the default was the one port the window could not choose
  over the variable.
- **Tab on the Tools window reaches OK before Cancel**, as on every KeePass dialog.
- **Windows is the only platform the plugin is made for, and the documentation now states it.**
  KeePass 2.x also runs under Mono, on Linux and macOS, and under Mono 6.8 the plugin does not load,
  because it asks Windows itself for KeePass's thread as it starts. `docs/getting-started.md`.
- **The operator's pages name what a client's rules do not keep from it.** Global auto-type chooses
  an entry by its title, so a program holding `U` can rename an entry after a window it controls and
  have the password typed there, and a click in KeePass's entry view opens a web address in an
  entry's notes, title, user name or custom field as Open URL does, `docs/access-control.md`.
  KeePass's own **Policy** options govern the person at the keyboard and not the gate,
  `docs/architecture.md`. And a client learns some of what its rules hide: a group it may not reach
  is named by the identifier of what it is shown inside it, a group it may remove and not list is
  refused `409` while it holds something, and an `If-Match` that cannot hold tells it which names an
  entry carries, as the routing document's `Unreachable`, `Conflict` and `IfMatch` state.
- **`docs/getting-started.md` names what removing the plugin leaves behind**, and starts the install
  from a published release.
- **The client's messages and the documentation name the authority** wherever "one" stood for it,
  and no longer assume two copies or two authorities.
- **The documents no longer promise that a retired authority is retired in a backup, or that an
  empty mask closes a subtree whatever lies below it.** A retirement keeps a copy synchronized or
  merged in from serving the old authority, but a copy opened on its own still serves it and every
  client it admitted, so the recovery from an authority no longer trusted now names recreating or
  deleting the authority in such a copy as well. An empty mask grants a profile nothing from there
  down only until a nearer rule below names the profile again.
- **The documents no longer promise that the recycle bin stays out of a client's reach whatever
  happens to the database.** The bin the gate leaves out is the group the database names as its
  recycle bin when a request is served, and no other group. A synchronize or an import keeps one
  copy's choice of bin, so where both copies had a bin, the group that loses stays in the database
  as an ordinary group, and a client reaches it, with everything deleted into it, under the rules
  where it sits: for a bin KeePass made under the root, the root's. After a synchronize or an
  import, check which group Database Settings names as the recycle bin, and empty or delete every
  other group that was a bin. `DESIGN.md` §138.
- **Every method that reads or writes one group or entry with its tag takes the retry budgets and a
  `connection_timeout`**, keyword-only and for that call alone: the reads of a group, an entry, a
  field or an attachment and the listings of fields and attachments, `head`, and every update,
  removal, strip and field or attachment write. `send` and `get` take a `connection_timeout` for one
  call too. A program wanting its own budget for such a call needed a second client, or `send`,
  which hands back no read's tag.
- **The command line has a command for every route, heads included.** `pykeepassgate head` asks any
  route a read command asks, `head entry <uuid>` and the rest, with HEAD, reading nothing, and
  prints the tag it answered with, as `etag` under `-o json`. The reads of one group or one entry
  take `--if-match` as the writes do, and so do the library's typed reads, as `if_match=`, where
  only `get`, `head` and `send` took a condition.
- **`pykeepassgate --version` names the client's version**, the half of the comparison a client and
  a plugin are paired by that the command line could not show, beside the gate's that `status`
  prints.
- **`refs.resolve_entry` resolves every string of an entry together**, each entry they name read
  once and the entry itself not again, and names each string `protected` that a protected value went
  into. `show --resolve` uses it, so a password a URL borrows by reference is marked `(protected)`
  and listed under `protected`, where it printed unmarked, and an entry several strings borrow from
  is read once rather than for each. `refs.resolve` answers a reference to the entry a value came
  from out of that entry, where it read the entry again.
- **`refs.resolve` refuses a value that takes more work than it spends**, with `Unresolved`: more
  than a thousand compilations, `refs.COMPILES`, or more than 4 MiB, `refs.CHARACTERS`. Strings that
  name each other in wide layers are compiled once for every path through them, so 34 short strings
  took 37 seconds and a few more never finished, and nothing bounded it. Folding and replacing take
  a pass over the text where they took a pass per character and per occurrence, and a long string in
  an entry of many fields is compiled in time that grows with the string alone: each field's
  placeholder was looked for by folding the case out of the whole string again, so a string of a few
  hundred KiB beyond ASCII in an entry of a few hundred fields took 0.4 seconds a compilation and
  minutes inside the limit on compilations. What resolves to what is as before.
- **An entry read without `expires` is an answer PyKeePassGate cannot read**, `ProtocolError`, as
  the document requires the member on every read and the client refuses any other required member
  missing. It was read as a gate answering nothing about the expiry, an `AttributeError` from
  `entry.expires`, and `show` left the line out.
- **An expiry whose instant in UTC falls outside the years 1 to 9999 is refused as such**, a
  `ValueError` from the library and exit 2 from `--expires`. `9999-12-31T23:00:00-05:00` parsed, and
  moving it to UTC raised `OverflowError`, which the library's documents do not name and which left
  the command line as a traceback.
- **An identifier or a name that names nothing is refused before anything is dialed**: an empty
  identifier or name, and text UTF-8 cannot encode, such as a lone surrogate from argv. The library
  raises `ProtocolError`, and the command line refuses the argument, exit 2. `groups --under ""`
  listed the whole database, which is another question and another grant, `entries --group ""` asked
  for `/groups//entries`, and a surrogate left as a `UnicodeEncodeError` traceback.
- **A pairing waits at least six minutes for the operator's answer, whatever `connection_timeout`
  is**, as a renewal already did. `pair --connection-timeout 30` hung up on an operator who took
  longer, after the invitation was spent and the record written, leaving a record for a key nobody
  holds. The hello still keeps the timeout given.
- **A renewal opens a kept identity with the password it was given, and calls a kept identity it
  could not present what it is.** An identity at `<dir>.retired` under a password, beside an
  identity whose key needs none, was opened with no password at all, and every renewal was refused
  with the claim that the gate still admitted it, though the gate was asked nothing; so was a kept
  identity that would not load.
- **An identity opened by a relative path renews beside its own directory**, `.` and `..` among
  them. `.` raised a bare `ValueError` out of every renewal, after `pair --directory .` had been
  accepted, and `..` kept the two directories a renewal makes in the working directory, inside the
  identity's own. An identity at a root is refused a renewal with a `StorageError` naming what to do
  instead, which is keeping it in a directory of its own.
- **Every file PyKeePassGate stores is flushed to the disk before it is counted written.** A renewal
  takes the staging copy away and, once the gate takes the old certificate back, the retired copy
  too, and nothing was flushed: a power loss within seconds of a renewal that reported success could
  leave the identity's files empty, with no copy left and the old certificate revoked. On POSIX the
  directories that hold an identity are flushed as well, since a file's name is its directory's data
  there, and a power loss could take away the names a first pairing made, or the copies a renewal
  keeps beside the identity while its files are written over: each directory PyKeePassGate makes is
  flushed into the directory above, and each directory it writes an identity into once the files are
  in it, and a directory that cannot be flushed is a `StorageError`.
- **PyKeePassGate writes a key only into a regular file of its own, narrowed first.** A file already
  there was narrowed after the key went in, so a re-pair over a widened identity held the new key at
  the old mode until then, and a narrowing refused came with the key already written. A hard link at
  a file's name passed the check that holds the file to its name, and the key went into the file it
  named; a file another account owns, in a directory shared on purpose, received the key before the
  narrowing failed; and a FIFO swapped in after the look made `prepare` wait for a reader forever.
  Each is now refused before anything is written, and `fetch` writes the same way.
- **A renewal refusing a shared directory beside the identity names what mends it**: removing
  `<dir>.renewing` or `<dir>.retired`, or making it this account's alone. The refusal named
  `--allow-shared-directory` on `pair`, which never waives either, and under `renew` named only
  `pair` for the identity's own directory, where `renew` takes the flag as well.
- **A `gate.key` of the wrong length is a `StorageError` naming the file**, as `identity.pem` and
  `authority.pem` already are. HMAC takes a key of any length, so a gate key a re-pair cut short
  named every connection under the wrong key, and every call failed as an EOF in the handshake,
  which the client explains as a database not open or an authority recreated.
- **A pairing or a renewal refused `503 busy` names the database's list of profiles among the
  causes**, the cause that does not end until the operator repairs the list. The client gave only
  causes that end by themselves, and a renewal was told what any route is, that asking again shortly
  is safe, so a program waited out its whole budget for a list that would not read. An ordinary
  route's busy message names the busy that waits for the operator's save as well.
- **PyKeePassGate reads `profilesUnreadable` on the identity route**, as
  `Identity.profiles_unreadable`, and `pykeepassgate whoami` prints a `profile list` line when the
  database's list of profiles will not read, in which case every profile on the record is among the
  removed profiles until the operator repairs it. The stand-in gate's `identified(...)` answers the
  route as the plugin does, `profiles_unreadable=True` among it. An identity without the member is
  an answer the client cannot read, `ProtocolError`.
- **PyKeePassGate refuses a host nothing could dial where it is given**, with a `ValueError` naming
  `host`, and `--host` as an argument, exit 2: a host that is not text, or empty, and for the `dial`
  a client has unless given another, a name `socket` could not encode to resolve, such as a label
  over 63 characters or a lone surrogate. Each left the first call as `UnicodeError` or `TypeError`,
  naming nothing, and the command line printed a traceback.
- **Every exception PyKeePassGate raises survives being pickled as what it was**, which a process
  pool does with an exception raised in a worker. A copy of `RenewalTooEarly` or `IncompatibleGate`
  could not be made at all, and `Withheld` came back as a sentence built around its own message,
  with whether it was a listing's and what stood for it gone. `Withheld` keeps both now, as `listed`
  and `through`.
- **PyKeePassGate holds a path it sends to RFC 3986's grammar**, an absolute path and a query of the
  characters the gate takes as they stand and escapes of two hex digits, and refuses anything else
  with `ProtocolError` before dialing. Only a space, a line break, a tab and a null were refused, so
  a control, a quote, a hash, a brace or a stray `%` given to `send` or `get` spent a connection and
  came back as the gate's `400`.
- **PyKeePassGate refuses a body that is not UTF-8, or that is nested past the decoder's stack, as a
  body it cannot read**, with `ProtocolError`. A body that was not UTF-8 was decoded leniently, so a
  value came back holding U+FFFD where its bytes were, as though it were the value stored, and a
  hundred thousand bytes of `[` raised `RecursionError` out of every call, past `except GateError`.
  A `client.json` nested that deep is the `StorageError` any unreadable file is.
- **PyKeePassGate reads the numbers in an answer as digits and nothing else**: the status as three
  of them, and `Content-Length` and `Retry-After` as RFC 9110 writes them. Python's `int` took a
  sign, an underscore and digits of any script, so a negative `Content-Length` counted as met and a
  body cut short of it as whole, and a `Retry-After` of more digits than a `float` holds raised
  `OverflowError`, which is no `GateError`, from any peer of a pairing. A version part of more
  digits than `int` reads raised `ValueError` the same way, and is now a version the client cannot
  compare.
- **PyKeePassGate refuses a socket its `dial` hands back that TLS cannot use, and closes it**: a
  datagram socket is a `TypeError`, as anything but a connected stream socket is, and a socket never
  connected is a `TransportError`, as a closed socket is. The first raised `NotImplementedError` and
  the second a bare `OSError` once TLS had wrapped it, neither of them an error the client promises,
  and each left its socket open.
- **PyKeePassGate refuses to connect over a TLS context offering anything but TLS 1.3**, before it
  dials. Every context it makes already offered TLS 1.3 alone, so only a program handing
  `transport.connect` a context of its own that offers an earlier version is refused, with
  `ValueError`.
- **A renewal takes nothing beside a profile for its own but an identity of this client's.** It
  keeps the identity it renews from at `<directory>.retired` and the renewed identity at
  `<directory>.renewing`, and took whatever identity it found at either for an identity a renewal
  had left: renewing `work` revoked a separately paired profile `work.retired` on the gate and
  removed its files. Either is now this client's only when it carries this client's key, and
  anything else stops the renewal before anything is asked, untouched.
- **`fetch` writes an attachment only its owner may read**, `0600`, narrowing a file already there
  before writing into it and refusing a link at the name, exit 2, as the identity's own files are
  written. It wrote through `Path.write_bytes`, which created the file readable by every account
  under the usual umask, left a file already there as open as it was, and followed a link to
  wherever it pointed.
- **A value piped in loses its carriage return on every platform**, as it did on Windows alone:
  elsewhere a line a Windows program wrote reached the gate with the carriage return still on it,
  and a password was stored with it.
- **`revoke` whose files will not all go still reports the revocation**: what it gave up and the
  admission key, with the directory to remove named on standard error and exit code 1. It printed
  only the file's `StorageError`, so a revocation that landed read as refused.
- **A key password is asked for at the terminal whatever standard input is**, so a value or an
  invitation piped into a command sits beside a key password typed at the terminal, where every pipe
  was refused as no terminal. Input from the null device is still no terminal, and a process started
  with no standard input at all is refused as input that ended, where it raised `AttributeError`.
- **The stand-in's `tag` and `written` take an entity tag and nothing else**: visible ASCII between
  two quotes, with no quote inside. `tag` took a quote inside, and `written` took anything, though
  its tag is the tag a later `If-Match` sends back.
- **The stand-in gate refuses what the plugin refuses before any route**, with the plugin's status
  and reason: a head over 8 KiB, a target over 2048 characters or a target that is no path the
  plugin takes, `..` among them, a version other than HTTP/1.1, a request without a `Host`, and a
  body over 1 MiB, beside the repeated header and the control character it already refused, and a
  refused `HEAD` comes without content. It used to hand all of them to the handler, so a program
  that sent a name of `..` or an attachment too large passed its tests and met the refusal only
  against KeePass.
- **The stand-in gate answers the status route itself**, with `FakeGate.version`, the client's own
  version unless a test sets another, so a script need not answer the request the client now makes
  first. `None` hands the route to the handler as before.
- **The plugin is compiled at C# 13**, and the build needs the .NET SDK 9 or later. Nothing it does
  changes: the compiled plugin differs only in caching every delegate it makes from a static method,
  in a holder class the compiler nests inside each class making them, rather than making the
  delegate at every use, and in an attribute the compiler adds to the module.
- **Dependencies**:
  - Microsoft.OpenApi and Microsoft.OpenApi.Readers from 1.6.30 to 1.6.31
  - Newtonsoft.Json from 13.0.3 to 13.0.4
  - SharpYaml from 2.1.1 to 2.1.5

**Known limits.** What the plugin keeps in a database holds in KeePass 2.62 or later with the plugin
in it, and only there. Other programs open a paired database, KeePassXC, KeePass before 2.48 and
programs on Android among them, and nothing detects what they do to it. So a paired database is
saved and merged only in KeePass with the plugin in it, and after any change made anywhere else the
Clients page, the Profiles page and the Certificate Authority page are where to look, and after a
save in a program that deletes rules, the rules on every group and entry that had any.
[`CA.md`](CA.md) §204 has what was measured, and [`docs/clients.md`](docs/clients.md) the operator's
account.

- **A merge in another program can undo a revocation.** A revocation is a key that no KeePass merge
  takes away, but merging an older copy into the paired database in KeePassXC, from 2.4.2, takes
  away every revocation written since and puts back the record it emptied, so a revoked client is
  admitted again with everything it had. A Keepass2Android merge keeps every revocation and can give
  an admission record and the list of profiles the older copy's values. Saving in KeePassXC from
  2.4.1 to 2.8.0 or in KeePass 2.47 keeps every key the plugin wrote and its value, and usually
  drops the times they were written at, so a later synchronize in KeePass can bring back a removed
  profile or a standing answer to a renewal.
- **KeePassXC's merge can replace the plugin's data.** A KeePassXC merge, from 2.4.2, replaces the
  custom data of the database it merges into with the other copy's, whole, whenever the other copy
  was read in a later second, which a copy chosen to merge from nearly always is. Merging an older
  copy into the paired database takes away the retirements and admission records written since as
  well, and puts back the older value of every key changed since, the list of profiles among them,
  so a replaced authority and a removed profile come back. A merge either way keeps a group's access
  rules as they were in the database merged into, however the other copy narrowed them.
- **Android programs can delete the rules kept on groups and entries.** A save in KeePassDroid 2.7
  was measured to delete the access rules on every group and entry, and a save in KeePassDX 3.2.0 or
  3.3.0 the rules on every group, without moving the modification time of the group or entry, while
  a save in KeePassDX 4.5.8 or Keepass2Android 1.15-r6 keeps them. Releases not named here were not
  measured. A group or entry left without its rules answers each profile with the nearest rule above
  it, or refuses a profile no rule above names, and its **Refuse any profile not named here** check
  mark goes with the rules, so wherever they narrowed what a group above grants, a client reaches
  more. A KeePassDX merge, in 4.5.8 and 3.2.0, puts the arriving copy's rules on a group or an entry
  whichever copy is newer, so an older, wider rule can arrive.

## 0.1.0, 2026-10-25

The first version, so there is nothing earlier to describe a change from: everything in it is new.

- **The plugin**: a KeePass 2.x plugin serving an HTTP API over mutual TLS 1.3, with every database
  its own certificate authority, whose authority and host certificate are issued together for a
  hundred years.
- **Pairing**, by which a program holding an invitation from the operator becomes a client of one
  database, approved on KeePass's own screen, with a certificate lasting as long as the operator
  picks, a day to a year, from what the program asks for. Its dialog, and the renewal dialog below,
  come up in front without taking the keyboard, no key approves either, and a lock closes either as
  unanswered.
- **A client's certificate from start to end**: the **KPG** tab warns before one expires, a program
  renews its own on an approval dialog like the pairing one, which the operator can refuse once,
  in this session, until a date or forever, and the **Clients** page shows such an answer in red
  and takes it back from the row's menu, a program can give its own up, and the records of
  expired certificates go with the plugin's next write of its own: a pairing, a renewal, a
  revocation or a change to the authority.
- **Access rules** on groups and entries, written on the **KPG** tab, and an audit file of every
  decision they make, every pairing and renewal the operator answers, and every client revoked,
  which the plugin opens with the right to append and no other.
- **The API**, whose routes [`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) declares: a
  database's groups, entries, fields and attachments, who the caller is and what it may do at a
  group or an entry, and which version of the plugin is answering and the commit it was built from.
  An update naming only what a node holds already writes nothing, and a node that goes away under a
  request is answered as gone, or as locked, never as empty. A read of one group or one entry
  carries an `ETag` to a client whose rule may read all of it, the same across a restart, a change
  answers with the tag it left in its body, and a request carrying `If-Match` is answered `412` and
  changes nothing when the node no longer holds what the tag says.
- **PyKeePassGate**, the reference client, as a library and as a command line. Each refusal is an
  exception saying what to do next, and each word a `503` carries has a budget of its own to wait
  it out with; a value the rules withhold is raised as withheld rather than read as empty; nothing
  it hands back prints a value; its key can be kept under a password; a program can make the
  connection it goes over; field references are followed as KeePass follows them, under the
  client's own rules; `pykeepassgate.testing` is a stand-in gate for the tests of a program
  built on it; the command line has a command for every route, each printing lines for a person
  or, with `-o json`, one document for a program; and a write can be made against what was read,
  with the tag a read hands back. It is published on PyPI as `pykeepassgate`.
- **A package for the release**: `build.ps1 -Task Package` zips the plugin and everything it loads,
  with its license and notices, beside a `SHA256SUMS`.

[`docs/`](docs/README.md) says how to use all of it.
