# Changelog

What each version of KeePassGate changes. The plugin, PyKeePassGate and the routing document carry
one version and move together. Which number a change moves is the API's contract, [`API.md`](API.md)
§411, and what each kind of release publishes is [`CONVENTIONS.md`](CONVENTIONS.md#versions).

Newest first. A version is written here before it is released, under its own heading, and says
**unreleased** until it is. A patch releases only what it fixes, so its heading names the halves
it releases, as `0.1.1, the plugin and the client, unreleased` would, and a minor version names
none, since it releases both.

## 0.2.0, unreleased

- **A router refuses to be built with a route bound to nothing**: a handler, a guard or a node
  selector given as null counted as bound, so it loaded and the route's first request was answered
  `500` as a defect. The plugin binds none to nothing, so only a router assembled outside it
  changes.
- **A router built without renewal refuses the renewal route before looking at the body**, as a
  router built without pairing refuses the pairing routes. A malformed body was answered `400` and
  only a well formed body was told the route is not served. The plugin always serves renewal, so
  only a router assembled outside it, as the tests assemble theirs, answered this way.
- **A body sent to a path none of whose operations takes a body is refused as an undeclared
  target whatever the method**, as a query name nothing on the path declares already was. Sent
  with a method the path does not serve, it was answered `405` naming the methods the path does
  serve.
- **The plugin refuses to load a routing document whose `get` and `head` hold a path parameter or
  a body to different schemas.** Only whether each carried a body was compared, so a `head`
  allowing a segment its `get` refused loaded, and a `GET` for that segment was answered `405`
  naming `HEAD`. The document the plugin ships holds every pair to one schema.
- **The plugin refuses to load a routing document allowing a larger body than its reader takes**:
  an array in a request body declaring no `maxItems`, or more than 32, and a body declaring more
  than 32 members. The reader refuses either before the document's schema is asked, so such a
  document loaded and offered bodies that were answered `400 malformed`. The document the plugin
  ships allows none.
- **The plugin refuses to load a routing document offering a body under a second media type**
  beside `application/json`. The router answers `415` to every other type whatever the document
  declares, so such a document offered a body the router turned away, under a schema nothing
  checked. The document the plugin ships offers none.
- **The plugin refuses to load a routing document declaring a cookie parameter.** Nothing reads a
  cookie, so a cookie declared required, or with a pattern, loaded and was kept by nobody. The
  document the plugin ships declares none.
- **The plugin refuses to load a routing document carrying an `x-kpg-` extension it does not
  read**, on an operation or on a path. The three it reads were looked up by name and nothing else
  was looked at, so a misspelled `x-kpg-attachments` loaded as absent, and the route was served to
  a rule granting `read` without `A`. The document the plugin ships carries none, so nothing it
  serves changes.
- **A port the gate could not take, and a gate that stops, leave no socket open behind them.** On
  .NET Framework a refused listener kept the socket it was made with, and a stopped listener
  opened a new socket, each held by the password manager until a collection.
- **The gate closes every connection it is serving when the plugin stops, even after its accept
  loop failed.** A loop that ended for a reason nobody chose had already given the port up, and
  the stop that followed closed nothing, so connections accepted before the failure were served
  on past the plugin's teardown.
- **A run of connections naming nothing an open database answers to writes one log line a
  minute**, the first at once and the rest counted into the next, as refusals above the connection
  ceiling already were. Each wrote a line, and such a connection needs no key, certificate or
  invitation, so one loop of them pushed the plugin's log and the file kept before it out of
  existence in about two minutes. Each reason, no name, no match or two databases at once, is
  paced on its own.
- **A client that sends anything after its request still reads the answer.** A second request
  pipelined behind the first, a line break written on its own or a TLS half close was left
  unread, so the connection closed with a reset, which threw away an answer the client had not
  read yet, a landed write's `201` among them. The gate now shuts its sending half first and reads
  and drops what still arrives, for up to two seconds of quiet, before it closes.
- **An answer its client reads too slowly is cut off at the connection budget**, counted from the
  moment it starts being written. Only the socket's send timeout bounded it, which covers one TLS
  record, so an admitted client reading a large answer a record at a time held a thread and a
  connection slot for minutes. A pairing and a renewal keep the longer deadline they had.
- **The routing document names the transport's answers before routing as the transport gives
  them.** It promised a `501` for a method the gate does not implement, which the router answers
  instead, and named no `501` for a `Transfer-Encoding`, the only reason a `501` is sent; it
  called every such `error` a sentence, where the `417`'s is the one word `expectation`.
- **A request without a `Host` header is refused with `400`**, as RFC 9112 requires of HTTP/1.1.
  It was served, so a request every other server refuses worked against this gate alone. The
  gate still routes by the name the TLS was made under and reads nothing in the header, and
  PyKeePassGate has always sent the header.
- **Only HTTP/1.1 is served**, and every other version is answered `505`, as the routing
  document already stated. HTTP/1.0 was read and served, and a 1.0 request carrying
  `Expect: 100-continue` was sent an interim `100 Continue`, where RFC 9110 has a server ignore
  that expectation.
- **The plugin refuses to load a routing document whose parameters declare what the router does
  not keep**: a query parameter or `If-Match` declared required, or a parameter given a `style`,
  `explode`, `allowReserved` or `allowEmptyValue` other than OpenAPI's own for where it sits. Only
  the schema was read, so a query parameter declared required loaded and the router served a
  request without it. The document the plugin ships declares none of them, so nothing it serves
  changes.
- **The audit is set aside at its bound only where its access control lists let it, whatever
  privileges KeePass's account holds.** In a KeePass started from a shell that enables the backup and
  restore privileges, as Git Bash run elevated does, a directory sealed for an append-only audit let
  the file be set aside and the previous file deleted, and then refused the next, so no
  decision was recorded from then on. A directory that lets files be removed and not added keeps
  the previous file too, which was deleted before the rename it then refused.
- **The plugin's log is set aside the same way**, so it goes on being written in a directory
  sealed for an append-only audit, which it shares by default. With the backup and restore
  privileges enabled it was given up for the session at its first roll there, and wherever its
  own ACL let it be removed, its previous file was deleted for nothing.
- **A debugger attached stops wherever the plugin records a failure it did not foresee**, with the
  exception in hand, and continuing carries on as it would with no debugger: every such failure is
  caught, so a debugger was told nothing.
- **Nothing the plugin's own windows do ends in WinForms' error dialog.** Every handler on the
  window under Tools, the approval dialogs, the authority chooser and the KPG pages records what it
  did not foresee, as the plugin's other catch-alls do. Let through, such a failure could reach
  that dialog in the middle of a save or a lock of KeePass's own, which KeePass handles messages
  inside, and while it stood KeePass refused every lock and the gate went on serving reads.
- **KeePass's start goes on past a defect in making the plugin's menu item**, which is left out of
  the Tools menu instead, and a dialog the plugin put a tab on is no longer held open by a defect in
  taking the tab off as it closes without KeePass announcing it.
- **A lock lets go of a database's keys and connections whatever closing the approval question
  did.** A failure there kept the host key and the authority in memory after the lock, kept the
  connections open and went on putting pairing requests in front of the operator.
- **A dialog the plugin could not note holds every write back until it closes.** It was left
  unwatched, so a revocation could land under Database Maintenance and be put back by that
  dialog's close.
- **Revocations pressed on the KPG tab land at OK whatever else fails there.** A failure while the
  clients were named for the audit dropped everything the tab held, revocations included.
- **A rule the grant editor could not store is never stored later.** It redrew before it stored,
  and a redraw that failed left the rule on the sheet, so the next edit wrote it, after the tab had
  reported it as not applied. A grant picker that could not be closed is reported as not closed,
  rather than as a picker that could not be opened.
- **A refusal until a date can be chosen across midnight on the eve of a certificate's last day.**
  Tomorrow was read more than once, and a midnight between two readings threw, so the choice went
  without a word and the renewal dialog stayed unanswered.
- **A retry of keeping the authority the operator chose stops when the plugin is let go.** It
  ticked once more as KeePass closed, and told the operator the authority was not kept.
- **The message at exit names the standing answers it lets go, and gives no cause.** It counted
  them and blamed KeePass being busy whenever they were given and nothing being written after
  them, which was untrue after a save that failed or a save made since, and promised a dialog for
  clients it did not name. It now names each certificate by the key the log names it by, and the
  log line names the authority each was given under.
- **Every save of a database writes the standing answers KeePass could not take**, the operator's
  own included. They rode on the plugin's own writes alone, so keeping an authority or pressing
  the KPG tab's OK and then saving went to the file without them, and the message at exit told the
  operator nothing had been written since. An answer is no longer written while Database
  Maintenance is open on its database, whose close took it away after memory had let it go, nor
  let go after a write whose file did not read back as written.
- **A later standing answer about a certificate is the answer that counts.** An answer KeePass could
  not take was written with the plugin's next write whatever the database held by then, so it went
  over the operator's own later answer, in the very write that had just put that answer there, and
  over an answer a synchronize brought in; and an answer until a date whose day had begun was
  written to refuse nothing. **Allow renewal** on such an answer was lost when a write landed before
  the settings dialog's OK. The waiting answer now gives way to anything written since it was given,
  and Allow renewal holds it back from every write until OK or Cancel.
- **A renewal refused in this session stays refused when an older answer until a date has passed.**
  An answer until a date that KeePass could not take, and whose day had begun, stood in front of
  a later "Refuse in this session" for the same certificate: the renewal dialog came up again on
  every request, and the clients page showed the client as refused by nothing.
- **A standing answer KeePass could not take is let go once its authority is recreated or
  deleted.** Held in memory until a write could take it, an answer about a certificate whose
  authority was then recreated or deleted was never written and never let go: it stayed held for
  the session, and the message at exit told the operator its client would be asked about again,
  which a client of a retired authority never is.
- **A removal whose save fails is put back where it was, and every change taken back out puts the
  usage count back too.** The undo put an entry or a group back at the end of its parent's list,
  below the recycle bin under the root, and left each node a failed change touched counting one
  more use, in a database then marked as holding nothing unsaved, for the next save to write.
- **The first recycle bin a removal makes is no decision about the bin.** The plugin stamped the
  bin setting's time when a removal through the API made the first bin, which KeePass's own first
  delete does not, so the next synchronize turned the bin back on over an operator who had turned
  it off on another copy to make removals final.
- **A removal through the API stamps what KeePass's own delete stamps.** It marked the removed
  entry or group as modified, and a group's parent too, where KeePass's delete moves no
  modification time. A merge reads that time as the newer edit, so the next synchronize took the
  parent whole from this copy, undoing a rule the operator had narrowed on it in another copy, and
  filed another copy's edit to a binned entry in the entry's history. A removal no longer changes
  its parent's tag either.
- **A pairing, a renewal or a program's revocation of itself that reached the database is always
  written to the audit.** A defect in KeePass or in another plugin, raised while the database was
  saved or while KeePass's window was redrawn, ended the request with the change in force and no
  line for it. The line is written before the window is redrawn now, and a save that ends in such
  a defect is settled as the database leaves it before the request fails: a change the file
  received is written down, and a change it never received is taken back out, which also keeps an
  ordinary change answered `500` from being made twice by its retry.
- **A list of profiles in another shape is refused by pairing and renewal**, as a list that does
  not parse is. A `Profiles` spelled with a capital, a `profiles` that is a string, a member beside
  it or a name the list cannot hold read as fewer names than the list holds, or none, so a renewal
  wrote its client a record with no profiles and a pairing wrote its own names over the list. Only
  another program or a hand edit writes such a list.
- **A write that loses sight of its entry or group before writing writes nothing.** Five write
  routes asked whether the caller may reach protected values only when their own lookup found the
  node, and handed the write on when it did not, though the write looks the node up again. An
  operator moving the entry at that moment let a rule without `protected` write a protected value,
  or strip a protected field it could put back unprotected. Such a write is answered `404` now, as
  a read is, or `503 locked` when the database locked.
- **A change whose answer cannot carry its tag is taken back out before the failure goes on.**
  A write answering with the tag it left took that tag after changing the node and before
  anything could undo the change, so a decryption that failed while the tag was taken left the
  change and its history revision in memory behind a `500`, unsaved and unmarked, for the next
  save to write.
- **An attachment that failed to decrypt once is tagged the next time a tag is asked for.** The
  failure was kept in place of its digest, so every tag of every entry holding that attachment
  failed again, answered `500` and counted under Tools, until KeePass let the attachment go.
- **A write digests its node only where something reads the digest**: an `If-Match` naming a
  tag, and an answer carrying one. Every write asked either took the node's tag on KeePass's
  thread, under the writer's lock, including a write under `*`, a condition naming no tag, and a
  change answering with the tag it leaves, which took a second.
- **A write refused `412` is logged with its cause**: the node no longer holding a tag named, an
  `If-Match` that does not read, one naming no strong tag, or a program the gate hands no tags.
  Every one was logged as a node that changed, which sent an operator looking for an edit nobody
  made.
- **An entry's tag is the same whatever culture KeePass runs under.** Its strings and attachments
  entered the tag in the order KeePass keeps them, which is by the current culture as the file
  loaded, so a change of regional format or of Windows' collation between two sessions changed
  the tag of entries nobody had changed, and a write under the earlier tag was refused `412`. The
  names now enter in ordinal order, so a tag an entry with custom names was handed before this
  version can be refused once.
- **The key a node's tag is made with is overwritten when its database stops being served**, as
  the rest of the host's material is. It is derived from the host's private key, and it was let go
  without being overwritten, along with the array it was derived into.
- **A group holding the recycle bin and nothing else is refused for the bin.** Every listing leaves
  the bin out, so such a group read as empty to the program refused `409` and was logged as a group
  still holding something. The log names the bin now, and the routing document states that a group
  holding the bin cannot be removed.
- **A program's revocation of itself and a standing answer to a renewal rebuild neither the group
  tree nor the entry list**, which show neither, and the rebuild replaced any search results the
  operator had on screen. A standing answer that lands tells the Clients page, which draws the
  client's name red from it and went on showing it black until something else redrew the page.
- **A write whose database KeePass closed as the save ended is answered `500 not verified`.** A
  lock handled among the messages KeePass's save pumps once it has lifted its own block closes the
  database before the save returns, and the close cleared what the plugin reads to know that the
  file holds the change, so the write was answered as saved and verified whatever the save had
  done.
- **A pairing or a renewal whose record the file did not keep leaves the database marked unsaved**
  whatever the file reads as afterwards. Taking the record back out read the file a second time,
  and a file part way through a copy at the first read and whole at the second cleared the mark,
  over a record memory no longer held and the file might.
- **A database opened from a URL holding a character Windows forbids in a path is named by its
  file**, as KeePass names it. Its name threw, so the authority chooser asking which authority to
  keep failed as an incident under Tools without asking, and Choose authority on the KPG tab failed
  with a path error.
- **A program asking who it is as KeePass locks is told the database is locked**, as every other
  route tells it. A lock between the gate reading its record and reading the database's profiles
  left the list naming nothing, and `whoami` answered that the operator had taken every profile
  away.
- **A lock closes the window asking which authority to keep**, as it closes a pairing or a renewal
  dialog, and unlocking asks again. KeePass locked under it, and it went on listing every
  authority's clients over the locked KeePass, modal over the window the operator unlocks in; and a
  choice whose database locked while it was being kept sent the operator to the KPG tab for a
  question the unlock was about to put again.
- **The Profiles page counts no rule in the recycle bin.** Nothing in the bin is reached through the
  gate, so its rules decide nothing, and the page counted them among the rules removing a name
  leaves inert and offered to put them back in force when the name was added again.
- **A database is served as soon as KeePass has unlocked it**, while KeePass is still asking its own
  questions about it: a master key due for a change, weak key derivation, duplicate identifiers.
  Until those were answered, the database and the certificate answering for it while locked both
  answered to its name, so every connection was dropped and the log claimed two databases were
  open.
- **A KeePass that keeps a dialog's copy of a node's settings, or its database, as another type
  leaves the KPG tab on the group and entry dialogs read only**, with its reason, as a KeePass that
  renamed either does. The tab looked editable and refused every rule chosen on it as a copy the
  dialog had not made yet.
- **An authority the operator chose while another write held the plugin's writer is kept once
  that write is done**, however long its save takes. It was tried for three seconds, while a save
  of a large database takes a minute and more, and the operator was then told it was not kept and
  sent to the KPG tab to choose again. A refusal that does not end by itself, Database Maintenance
  left open or KeePass busy with an operation of its own, is still tried for a few seconds and
  then told.
- **A protected field is kept from a strip whenever any profile could put an unprotected value
  back.** The guard asked whether some profile could create there and whether some profile could
  create a protected value there, so a program holding one profile that could put an unprotected
  value back beside another reaching protected values stripped the field. Each profile is asked on
  its own now.
- **A removal needs one profile to allow both sides of it.** Removing an entry or a group asks the
  group it leaves and the node itself, and a program holding two profiles that each allowed one
  side removed what neither of them may. One profile has to allow both now.
- **The authority chooser asks about every database even when asking about one database fails.**
  A round marked every database it would ask about before the first question, so a defect asking
  about one database left the other databases unasked, and unserved, until each was locked and
  unlocked. They are asked a moment later now, and the failure is still counted on the plugin's
  window under Tools.
- **The chooser warns before it is answered**: answer on one copy of the database, and
  synchronize before answering on another. Its button is **Keep this authority** rather than
  **Keep this one**.
- **The KPG tab shows a database whose every authority is retired as retired**, rather than as
  having no authority yet. Copies that each kept a different authority in the chooser, before
  their answers were synchronized, retire every authority, and the tab then sent the operator
  looking for a step they had missed. A delete, which retires an authority too, is shown the same
  way.
- **The client's messages and the documentation name the authority** wherever "one" stood for
  it, and no longer assume two copies or two authorities.
- **A database holding a crafted authority key stops only itself being served.** A key named
  `KPG.Authority.` with no digest, beside the database's own authority, made the gate drop every
  connection to every open database for as long as that database was open. It now waits for the
  operator to choose between its authorities, as a database synchronized from copies that each made
  an authority does, and every other open database is served.
- **The plugin stores a URL as it is written**, as it stores every other value. It refused one
  holding `{` or beginning `cmd://` with `400`, and accepts both now. KeePass's Open URL does more
  with a URL than those two cases: it replaces an environment variable in it, hands a path or a
  network share to whatever Windows starts for it, and with an override switched on puts the
  entry's own values into a command line. Telling which URL is safe takes what KeePass is set to
  do as well as what the URL holds, so one check given both is to take the place of a refusal
  that covered two cases. Until then, [`docs/access-control.md`](docs/access-control.md) warns
  against opening a URL a program may have written.
- **A translated KeePass is read the way it reads itself.** Where KeePass's language and Windows'
  regional format sort letters differently, Danish and English among them, the gate looked names up
  in the wrong order and missed what was there: an admitted client was refused `401`, a field was
  served empty and an attachment answered `404` though both were listed, a node's own seal was
  passed over for the grant above it, and two authorities with no retirement were served as one.
  Every connection is now served in KeePass's own culture.
- **A program reading the database while KeePass changes it reads one tree.** A read ran beside
  KeePass's own work and could miss a group or an entry that was there, or meet a list KeePass was
  changing and be answered `500`. Every read now holds KeePass still for as long as it takes,
  a fraction of a millisecond for one entry and about seventeen for ten thousand, and waits first
  for whatever the operator is typing or clicking and for KeePass's own timers. One KeePass cannot
  make room for within ten seconds is answered `503 busy`.
- **A change waits for KeePass to finish what it is doing**, a save, an import, an export or XML
  Replace, where it was answered `503 busy` straight away, and is answered so only if KeePass is
  still at it half a minute later. Under a message box in KeePass it is answered `503 busy` at
  once, since nothing happens in KeePass until the box is closed.
- **The plugin issues its certificates through Bouncy Castle, with names in UTF8String**, where
  Windows issued them with names in PrintableString, and no key of the plugin's passes through
  Windows any more. A database whose authority Windows issued keeps working as it was: the
  authority goes on issuing, the clients it admitted go on being served, and a certificate it
  issues now names it exactly as it names itself.
- **A stored authority somebody crafted costs the time of refusing it**, not the time it asks for.
  One over 64 KiB, asking for more than 10,000 iterations of its key derivation or holding more
  parts than a stored pair has is refused before any of that work is begun.
- **Another plugin's older Bouncy Castle stops the plugin starting**, naming both copies. Every 2.x
  release of Bouncy Castle is one assembly to .NET, so a copy another plugin loaded first was the
  one this plugin ran on, whatever it lacked.
- **The plugin is compiled at C# 13**, and the build needs the .NET SDK 9 or later. Nothing it does
  changes: the compiled plugin differs only in caching every delegate it makes from a static method,
  in a holder class the compiler nests inside each class making them, rather than making the
  delegate at every use, and in an attribute the compiler adds to the module.
- **The plugin's code answers every finding of CodeQL's `security-and-quality` queries**, each fixed
  or suppressed with its reason where it is made, and nothing the plugin does changes. Its six calls
  into Windows go through one class, and the path of a file it writes is joined by a helper that
  refuses a part taking the place of those before it. A check the code before it had already
  settled, and a loop that only asked whether there was anything, are gone. A loop that filters,
  maps or tests a list is the query it is, and the ASCII letters, digits and hexadecimal digits the
  wire's grammars allow are asked of one class. The writers it builds JSON with, and the stream
  a request's body is read into, are disposed once they are read, and what it disposed by hand is
  disposed by `using`, or on every way out where it is handed on.
- **PyKeePassGate refuses to connect over a TLS context offering anything but TLS 1.3**, before it
  dials, which every context it makes already did, so nothing a program built on it does changes.
- **A renewal takes nothing beside a profile for its own but an identity of this client's.** It
  keeps the identity it renews from at `<directory>.retired` and the renewed identity at
  `<directory>.renewing`, and took whatever identity it found at either for an identity a renewal
  had left: renewing `work` revoked a separately paired profile `work.retired` on the gate and
  removed its files. Either is now this client's only when it carries this client's key, and
  anything else stops the renewal before anything is asked, untouched.
- **A profile name is ASCII letters, digits, hyphens and underscores**, where it was anything
  but a separator, a colon or a null that did not end in a dot or a space. A dot is kept for what
  the client keeps beside a profile, as a renewal of `work` keeps `work.renewing` and
  `work.retired`, which a profile could otherwise be called. What else is refused failed only
  once the directory was made, or was a Windows device name such as `CON`. An identity paired
  under such a name by 0.1.0 is refused under it, exit 2 or `ValueError`, and is reached with
  `--directory` naming its directory.
- **A `503` names its word in a field as well as in its body**, `KPG-Unavailable: busy`, `locked`
  or `undecided`, which the routing document declares, and so does PyKeePassGate's stand-in gate.
  The answer to a `head` carries no body, so `GateClient.head` met every `503` as a
  `GateUnavailable` with no word and never spent the three retry budgets it takes. It now reads
  the field and waits a `head` out as it does a `get`.
- **The command line reads and writes UTF-8 on every standard stream**, whatever the code page.
  On Windows, plain output written to a file or a pipe was encoded as cp1252 and ended in a
  traceback at the first name beyond it, part way through a listing, and a value piped to
  `--password`, `add-field` or `update-field` was decoded as cp1252 and stored as other text. A
  piped value that is not UTF-8 is now refused, exit 2, and a byte order mark at its start is
  dropped.
- **Input that ends before a line is refused rather than sent as an empty value**, exit 2 with
  nothing sent: a producer that printed nothing, a closed standard input or `< /dev/null` gave
  `update --password`, `add --password`, `add-field` and `update-field` an empty value, which
  they wrote and reported done, exit 0. An empty line is still the empty value it was sent as.
  `pair` given no invitation that way is refused the same way, where it was refused as a
  malformed token, exit 1.
- **`fetch` writes an attachment only its owner may read**, `0600`, narrowing a file already
  there before writing into it and refusing a link at the name, exit 2, as the identity's own
  files are written. It wrote through `Path.write_bytes`, which created the file readable by every
  account under the usual umask, left a file already there as open as it was, and followed a
  link to wherever it pointed.
- **An entry's standard strings refuse a look-alike name rather than read as empty**, as the
  reference resolver already did. `entry.password`, and the title, the user name, the URL and the
  notes likewise, answered `""` for an entry holding the string under no name spelled exactly so
  beside a name beyond ASCII, such as `Password` with a soft hyphen in it, which KeePass's
  collation reads as the password: empty where KeePass shows a value, and where the gate withheld
  a value. They now raise `AmbiguousName`, and an entry whose names are all ASCII reads a
  missing standard string as empty, as KeePass does. **`AmbiguousName` is new**, for a name only
  KeePass's own comparison can match, and the reference resolver raises it for the two cases of
  that kind it refused as `Unresolved`: a standard string held under a look-alike name, and a
  placeholder naming a field only in another case beyond ASCII. A program catching `Unresolved`
  for either catches `AmbiguousName` now.
- **An entry's strings are held to 524288 characters, names and values together**, read and
  written alike. Reading an entry built an answer of every string it held with no ceiling, at
  about four times their size in memory inside KeePass, measured at 94.5 MB for two hundred
  fields of 64K characters, and nothing bounded how far writes could grow an entry. A read of an
  entry over it, or of one field over it with its name, is now refused with `413` before
  anything is decrypted, and a write that would take an entry over it is refused with `413`
  and changes nothing. Stripping a field is never refused for it.
- **A write to an entry is refused `503 busy` while KeePass's own attachment editor is open on an
  attachment of that entry's.** The editor holds the attachment and not the entry, and KeePass
  writes the edited bytes back onto the entry as it closes, so a removal of the attachment
  answered as done came back, and an update was overwritten, with nothing to show for it. Since the
  editor does not tell which entry it came from, every entry holding an attachment of that name
  is refused while it is open.
- **Nothing is written to a database while KeePass's TAN wizard is open on it**, and a write
  answers `503 busy`, pairing and renewal included. The wizard adds its entries from inside its
  own OK, and KeePass's save handles messages as it goes, so an OK pressed during the save a
  write makes added to the list the save was walking: the save threw, KeePass showed its save
  warning, and the write was answered `500 not saved`.
- **A Windows lock, a user switch, a suspend or a change of remote control that arrives while a
  write's save is running locks KeePass once that write has been answered**, where KeePass is set
  to lock for it. KeePass drops such a lock while it is saving, so it was lost for good and the
  gate went on serving the database. The plugin now makes it through KeePass's own lock, which
  still keeps the workspace unlocked while a dialog of KeePass's is open.
- **A request refused for its request line, its target, its version, a byte that is not ASCII or
  a header ahead of its length is answered with a status the client can read**, `400`, `414` or
  `505`, and so is a head over its ceiling, `431`, and a locked database's `503` to any of them.
  The body behind such a request was left unread, so closing the connection brought a reset that
  discarded the answer, and a client whose body did not arrive with its head saw the connection
  fail as though no gate were there.
- **Each connection is served on a thread of the gate's own, never a thread of KeePass's thread
  pool**, which KeePass waits on from its own thread: its managed Argon2, used without its native
  library, blocks a save or an unlock on lanes it queues there. Thirty-one writes at once froze
  the first save for seventeen to twenty-two seconds and answered some of them `503 busy`, and
  thirty-one connections that sent nothing, from a peer that proved nothing, held KeePass's own
  background work back for about ten seconds.
- **Closing or unlocking a database ends only that database's connections, and closing it lets go
  of only its own approvals.** Both ended every connection, so closing or unlocking a second
  database reset every request to the first, and closed the connection of a pairing or a renewal
  waiting on the first database's dialog, which the operator then approved into a closed socket:
  the record was written and the certificate never reached the client. A close also let go of
  every approval kept for a busy KeePass, so a pairing approved for a database still open was told
  that its database was locked.
- **PyKeePassGate refuses a gate it does not work with, before it sends anything else**, with
  `IncompatibleGate` naming both versions, and the command line exits `1` with it. A client works
  with a gate of its own major version whose major and minor it does not lead, and the patch is
  never compared, as [`API.md`](API.md) §411 now states as part of the API's contract. The
  documents promised that the same major and minor make a pair, and neither half checked it. A
  paired client asks the status route once, before its first request, under the budgets of that
  request.
- **The pairing hello carries the gate's version beside its proof**, as `version`, so that a
  client refuses a gate it does not work with before the request that puts it in front of the
  operator. A connection that arrived on an invitation reaches no other route, so the status route
  was out of reach.
- **The stand-in gate answers the status route itself**, with `FakeGate.version`, the client's own
  version unless a test sets another, so a script need not answer the request the client now makes
  first. `None` hands the route to the handler as before.
- **A write that loses sight of its entry while asking which strings are protected writes nothing
  either.** Changing an entry, writing a field and stripping a field ask a second lookup which of
  the entry's strings carry their own protection, and that lookup answered "none" when it missed,
  so the write went ahead: a rule without `protected` could overwrite, or strip, a string protected
  by its own flag rather than by the database. Such a write is answered `404` now, or `503 locked`
  when the database locked, as a miss of the first lookup is.
- **A renewal or a pairing writes its record only from the list of profiles the dialog showed,
  read again under the writer's lock.** A renewal read the list a second time after the dialog, on
  the connection's thread, and dropped every approved profile that read did not find, so a lookup
  that missed the list wrote a record carrying none of them; a lookup that missed it as the dialog
  was built offered nothing and did the same. A pairing compared the write against a read taken
  after its dialog, so a name the dialog showed as new, put on the list meanwhile, was written as
  though it had been listed. Each is now answered `503 busy` with the approval used up and the
  operator asked again, and so is a renewal whose profile was taken off the list while the dialog
  stood, which used to renew without that profile.
- **An approval the operator gives just as KeePass locks or its database closes is let go and
  answered `503 locked`.** It reached the plugin's memory after the lock or the close had let go of
  what was there, so the client was told `503 busy` with `heldUntil`, and the same pairing or
  renewal asked again once the database was open again was written without the dialog.
- **A pairing whose held approval another exchange on the same invitation wrote first is told
  `409 no invitation`**, and the log states that another request took the approval. It was logged
  as held until its time ran out and answered `503 busy`, inviting a retry on an invitation that
  was already spent.
- **A renewal held for a busy KeePass keeps its approval when the retry cannot read the
  operator's standing answers**, and is told `503 busy` with `heldUntil` again. A read that failed
  once let go of the approval, so the next retry put the same dialog in front of the operator
  again.
- **A pairing or a renewal answered `500 internal error` is put to the operator again when it is
  asked for again**, as [`PAIRING.md`](PAIRING.md) §311 states for a certificate that could not be
  issued. The approval was kept for the rest of the pairing budget with nothing telling the client
  so, and the same request asked again was issued without the dialog.
- **A pairing or a renewal that a lock kept off the screen gets no `unanswered` line in the
  audit**, since nobody was asked, and the log states that it was never shown. It was answered as
  a dialog the lock closed, so the audit recorded the operator leaving a question unanswered that
  never reached them, and the log a dialog closed that never stood.
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
- **Every certificate the gate refuses for a database shares one allowance in the audit**, rather
  than each counting against its own. A client keeps the certificates it renewed out of, and each
  brought a whole burst and rate of its own once it was revoked or had expired, so a client that
  kept them could write as many times the pace as it had certificates.
- **No client is paced where the audit has no file to write**, as on an account with no local
  application data and no `KEEPASSGATE_AUDIT_FILE`. The pace keeps a client from rolling the file
  away, and with no file a client past its first thousand decisions was still held to ten a second.
- **An audit line's `at` is when it was decided, for a paced client too.** It was read once the
  client's wait was over, so a client past its allowance had its decisions and refused renewals
  stamped up to seconds after the rules or the operator's earlier answer had decided them.
- **A protected value withheld is written down by the walk that withheld it**, and a strip of a
  protected field refused because some profile could put an unprotected value back is written down
  at all. A handler decided on its own and asked the rules again only to write the line, so a rule
  changed in between left a withheld value unrecorded, and that strip, decided profile by profile,
  was recorded by a walk pooling every profile, which permitted it and wrote nothing.
- **A change to an entry that reached the disk is never answered `503 busy`.** Its answer reads the
  entry again to confirm the tag it hands back, and a read that could not hold KeePass's thread was
  answered `busy`, so a client retried a change already made. It is answered without the tag.
- **A read too large to serve is `413` whatever its `If-Match`**, for an attachment, an entry and a
  field alike, as RFC 9110 section 13.2.1 orders it and the routing document already stated. The
  condition was asked first, so a stale tag was told `412`, and a client handed no tag was told
  `412` every time and never that the read was too large.
- **A new field or attachment name holding a Unicode format character is `400 malformed`**, the
  zero-width spaces and joiners, the soft hyphen and the byte order mark among them, beside the
  bidirectional controls already refused. A name holding such a character is drawn as the name
  without it, so a program holding `C` could add a second, unprotected `Password` beside the real
  `Password`.
- **A new field or attachment name too long for its routes is `400 malformed`.** Percent-encoded,
  a name of a few hundred characters beyond ASCII, inside the 512 the body allows, took the route
  naming it past the 2048 characters of a target the transport reads, so it was made with `201`
  and could never be read, changed or stripped by name, every request for it answered `414`.
- **A refusal that profiles a damaged list lost would have allowed is written down as
  `profile list unreadable`**, with `removedProfiles` naming them, where it was `profile removed`.
  A list of profiles that will not read names fewer than it held, and nobody had removed them, so
  the line sent the operator to name again profiles nobody took off.
- **`GET /gate/whoami` answers `profilesUnreadable`**, always present and true only while the
  database's list of profiles will not read. A damaged list names fewer profiles than it held, so
  `removedProfiles` then held names nobody removed, and a program had no way to tell that from the
  operator taking them away.
- **A read refused `412` is logged as a change refused `412` is**, a warning with its reason, a
  client handed no tag and an `If-Match` that does not read among them. A read's `412` was written
  nowhere, so a program refused on its reads over and over showed nowhere at all.

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
