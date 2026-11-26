# The certificate authority

<!-- The recorded probe output below smuggles a newline as `%0A`, which runs into the
     `URL=` after it. Quoted verbatim, so the word is ignored here rather than corrected. -->
<!-- cspell:ignore AURL -->
<!-- RFC 8018's name for the password-based encryption scheme a PKCS#12 bag may name. -->
<!-- cspell:ignore PBES -->

Reference for how KeePassGate issues, presents, validates and withdraws identity.
[`DESIGN.md`](DESIGN.md) says how it fits the rest, [`PAIRING.md`](PAIRING.md) covers the flow that
gets a stranger admitted, and `spikes/tls-and-certificates` holds the code behind every measurement
quoted here but one: the `caIssuers` download of §214, which a test of the pin measured while it
built a chain.

**The one sentence.** Every database carries its own certificate authority inside itself, so pairing
a client means issuing it a certificate that only that database will ever honor.

> **Rewritten twice, and the second time changed the shape.** An earlier version put a single
> authority beside the plugin with a passphrase-wrapped key and a registry of admitted clients. A
> red team found that its chain validation accepted foreign authorities, and that the registry could
> be supplied by an attacker. Moving the authority into the kdbx closes the second by construction
> rather than by guarding it. Section 11 records every change and why, so a reader does not re-tread
> ground.

---

## 1. Why an authority at all §201

The obvious alternative is a password-authenticated key exchange such as SRP: a one-time code, a
key exchange, a shared secret at both ends, and a few thousand lines of arithmetic and negotiation,
all of it because the protocol would have to carry its own authentication.

mTLS moves authentication into the transport, where it is solved. What remains is deciding which
certificates to trust, which is what an authority is for.

**What it does not do.** mTLS offloads **authentication**. It does not decide what a client may
touch and it does not withdraw access. Those stay in the ACL and the admission records.

## 2. Why the authority lives in the database §202

The obvious design puts one authority on the host with a registry beside it. That was the first
draft and it has a hole: the registry has to say which databases a client may reach, and **a
database is a file an attacker can supply**. Get a prepared `.kdbx` opened and it supplies a
registry admitting a certificate of their own.

Moving the authority inside the database removes the question rather than guarding it. A malicious
kdbx carries its own authority and its own paired clients, and a certificate issued by it opens
exactly one door: its own, which contains nothing. **The trust domain becomes the database.**

Four things follow for free:

- **No external state.** No registry file, no DPAPI-wrapped backup that restores nowhere. Backing up
  the authority is backing up the database, which people already do.
- **No passphrase, and no hand-rolled key wrapping.** The key is reachable only when the database is
  decrypted, and no API path reaches it. An earlier draft specified Argon2 over AES-CBC with an
  encrypt-then-MAC construction; delegating to KeePass's own encryption is strictly better than
  cryptography written here.
- **Replication works, soundly.** Copy the file and the authority, the host certificate and every
  pairing travel with it.
- **A claim no longer needs to name a database**, because the issuer already did.

What it costs: **the authority is only as protected as the database's master key**, and anyone who
can open the file can read the signing key. That is the correct boundary, since anyone who can open
the file already holds every secret the authority protects.

---

## 3. Algorithm and sizes §203

**ECDSA on NIST P-256, SHA-256, throughout.** Verified end to end on .NET Framework 4.8.

| | |
| --- | --- |
| Key | ECDSA P-256 |
| Signature | SHA-256 with ECDSA |
| Authority certificate | about 393 bytes, and about 384 for one written while Windows issued these |
| Leaf certificate | about 436 bytes for the host, and a few dozen more for the responder's marker or a client's claims |
| Serial | 16 random bytes, high bit cleared so the INTEGER encodes positive; an authority written while Windows issued these carries the 8 that `CertificateRequest.CreateSelfSigned` chose |

Each size moves by a few bytes from one certificate to the next, because an ECDSA signature is two
integers and each is a byte longer when its top bit is set: over three hundred authorities the
figure was 392, 393 or 394, and over three hundred hosts 434 to 437.

**Bouncy Castle issues every certificate here**, and signs with the authority's key as it reads that
key out of the stored PKCS#12 itself, so no key this plugin holds is ever handed to Windows. Of
what the plugin holds, Windows is given certificates to read and nothing else. The one thing
Windows parses that could hold a key is an admission record's certificate, bytes a database chose,
and those it is handed under `EphemeralKeySet`, §222. The rest of the cryptography is Bouncy
Castle's too: it terminates the TLS, `DESIGN.md` §133, and it is the fixed-time comparison every
certificate's bytes go through, §214, which .NET Framework does not have.

**Names are written as UTF8String**, which holds an underscore where PrintableString cannot:
Windows wrote PrintableString, and fell back to BMPString for a name holding one. RFC 5280 section
4.1.2.4 allows either of the two, where RFC 3280 required UTF8String of every certificate issued
after 2003. A leaf's issuer is its authority's subject copied as it is stored, never written again,
so an authority written while Windows issued these, whose names are PrintableString, goes on
chaining everything it issues: section 7.1 compares names after normalizing them, and the anchor
pin compares bytes, §214.

---

## 4. What the database holds §204

**The authority, every admission and every revocation are records in the plugin's store**: each
an entry in a group of the plugin's own, written once, under a check keyed by the store's key.
What takes an authority out of service is a retirement, written beside it as a further record
once it is recreated, deleted or not kept; what takes a client away is a revocation that names
its client key; and what takes a single certificate away is a record of a kind of its own.
[`STORE.md`](STORE.md) §614, §615 and §648 hold the records and §627 the writes, and the
paragraphs on them below hold of them there.

Everything else under `KPG` keys in the **database's own** `CustomData`, which is the one dictionary
KeePass stamps key by key and merges the same way. A node's access rule was the exception and lived
on that node; it is a record of the store that names its node now, `STORE.md` §617, and nothing of
the plugin's is on a group or an entry. Everything requires KDBX 4, and this requires KDBX 4.1,
because a per-key modification time is a 4.1 field: a database with a profile name in that
dictionary is a 4.1 file. That is a format and not a gate: other programs open the file, and the
paragraph on them below has what saving and merging in them was measured to do. Nothing that takes
access away is kept in that dictionary any more, which is what the paragraphs on a revocation below
are about.

**Neither the authority nor an admission takes the file to 4.1**, and that reverses a rule this
section held twice over. While the authority was a key of the dictionary, the authority alone
made a 4.1 file. Once the authority was entries of the plugin's store, which need nothing above
4.0, a database with an authority alone was a 4.0 file, and the first admission record took it
to 4.1, being a key of the dictionary still. An admission is an entry of the store as well now,
so a saved database that holds an authority and an admission, and nothing of the plugin's in
its custom data, is a 4.0 file. **Nor does a profile name**, which took the file to 4.1 while
it was a key of this dictionary and is an entry of the store as well now. **Nor does a standing
answer to a renewal**, which took the file to 4.1 while it was a key of this dictionary and is a
version of the store now, `STORE.md` §618, measured beside an authority and a client paired for
no profile. `AdmissionInRealDatabaseTests`
holds the measurements, in `An_admission_and_a_profile_name_in_the_store_leave_the_file_KDBX_4_0`
and, through the pairing's own write and KeePass's save, with the version read from the file's
header,
`A_pairing_leaves_the_file_KDBX_4_0_whatever_it_names_and_whatever_is_refused`. A
pairing that approves a profile the list does not hold adds that name in the same write, and
leaves a 4.0 file as a client paired for no profile does.

**The rest of this store is replaced in 0.3.0 as those parts were.** What other programs do to
custom data, in the paragraphs on them below, is what reopened it: the plugin's data is in
entries in a group of the plugin's own, with no custom data anywhere. [`STORE.md`](STORE.md)
§601 holds that store and §604 lists what of this section it reverses. The rules on groups and
entries have moved as well: a rule is a record of the store for each node and profile, written
as the node's dialog closes with OK, `STORE.md` §617 and §629, and what this section holds of a
rule carried by its node is the account of what that reversed. **So have the standing answers
to a renewal**, the last of what the plugin kept here: a version of the store about the client,
`STORE.md` §618, and this section holds the account of what that reversed. **Nothing of the
plugin's is in this dictionary.**

**The admission, the revocation, the profile names and the standing answers to a renewal were
keys of this dictionary**, `KPG.Client.<sha256>` for each certificate admitted,
`KPG.Revoked.<sha256>` for each certificate revoked, `KPG.Profiles` for the set of names and
`KPG.RenewalRefused.<sha256>` for each certificate whose renewals were refused forever or until
a date, and the paragraphs below record what each reversal was and why. A database that holds
such keys is not read for them: they admit, refuse and name nobody, nothing removes them, and
they keep the file a KDBX 4.1 file for as long as they are in it, `STORE.md` §645.

**Not the root group, and the group dialog is why.** `GroupForm` copies a group's `CustomData` when
it opens and writes the copy back on OK, so anything written to the root group while an Edit Group
dialog is open is destroyed by that OK, silently. Everything here used to live there. The
database's own dictionary is copied by one dialog only, Database Maintenance, and the plugin
refuses to write while that one is open.

**A node's rule was carried by the node, because nothing smaller carried it, and is a record of
the store now.** A group's and an entry's `CustomData` keeps no time per key, so a merge takes the
whole dictionary from one side or the other and decides by the node's own modification time: that
is a fact about KeePass, and it stands. While a rule was a key of that dictionary, it was written
only into the copy a group or entry dialog puts back on OK, so it traveled exactly as far as any
other edit made in that dialog, the plugin wrote no rule of its own, and a merge kept the rules
of whichever copy touched the node last, a narrowing made in the other copy lost with them.
**That is what the store reverses for a rule**, `STORE.md` §617: a rule is a record for each node
and profile, merged record by record whatever became of the node's time, a rule changed apart in
two copies stands in both versions and grants what both allow, and the plugin writes its rules
itself, as the node's dialog closes with OK. What still holds is about the node and not about its
rules: a removal through the API stamps what KeePass's own delete stamps, so a child's removal is
no edit of its parent, and the first bin it makes is no decision about the bin. A merge that
hands back a rule for a profile the operator has since removed hands back something inert,
because the database's list of profiles decides which names are in force (`DESIGN.md` §139);
that replaced a purge which rewrote every rule naming a removed profile, and moved the times of
entries nobody had edited to make the rewrite carry. A dialog looked at and accepted with no rule
changed writes nothing, so reading a rule is never an unsaved change.

**An admission is a record for each certificate, never one blob.** Each is an entry of its own, and
a merge brings an entry the other copy lacks, so two hosts pairing different clients keep both;
`MergeInRealDatabaseTests` measures it against the KeePass this builds with. It was a key of
`PwDatabase.CustomData` for each certificate, on the same argument: that dictionary keeps a
modification time for each key and a merge decides key by key, while a node's dictionary is taken
whole as the paragraph above describes, which the same tests still measure. A single blob would be
last-writer-wins in either, and would lose pairings silently. What reversed the key is what
reversed the authority's, below.

**A profile name is added and taken off by records of its own**, an addition and a removal,
`STORE.md` §616, and a merge brings each record the other copy lacks, so two hosts that each
approve a pairing for a name the database has never named keep both records and both names. A
removal wins over every addition of its name that did not see it, whichever copy wrote either, and
a name added again after it, naming it, stands. **This reverses the list as one key**,
`KPG.Profiles`, holding every name in one value, which a merge kept from one side only, for a
synchronize whichever copy wrote it last: a name only the other copy added was lost, granting
nothing and shown as removed on every client holding it, and a name only the other copy took out
came back into force, its rules and its clients with it, without the question the Profiles page
asks first.

**A revocation is a record that names the client's key, and it is never removed.** Revoking a
client on the clients page writes a record of kind `revoked` holding the client key, §217, and
every certificate whose admission carries that key is refused from then on, in whichever copy and
at whichever time it was issued: a certificate renewed at a machine that had not read the
revocation is refused once the copies meet. Every write of the plugin's restates the revocation
in the store's summary entry, and a deletion record of its entry still tells of it, so deleting
that entry for good, in any program that leaves a deletion record, changes nothing. A revocation
has no end and is not pruned, since a client key has no date at which it expires. `STORE.md` §615
and §623 hold the record and its three carriers. Measured over copies brought together by
`PwDatabase.MergeIn`, in `MergeInRealDatabaseTests`:
`A_revoked_client_stays_revoked_when_the_other_copy_still_holds_the_admission`, and
`A_revoked_client_stays_refused_whatever_happened_to_the_times`, where the other copy's
admission was touched after the revocation and the revocation's entry dated back.

**A single certificate is taken away by a record of its own**, of kind `withdrawn`, which names
that certificate by its admission key: what a client writes for the certificate on its connection
with `DELETE /gate/certificate`, §217. It refuses that certificate and leaves the client's other
certificates admitted, the certificate a renewal has just issued among them, which a revocation
by the client's key would refuse. It is restated and told of as a revocation is, and is as final.
The record names every certificate its copy holds as renewed from that certificate when it is
written, by an admission in force or by a record that has since taken the renewal away, so what
a client keeps does not turn on which of its certificates was taken away first. A renewal made
from that certificate that the record does not name, as at a gate whose copy was from before,
does not count: its certificate is refused once the copies meet, and so is whatever is renewed
onward from it. `STORE.md` §648, held by `StoreAdmissionChainTests`.

**This reverses the revocation as a custom data key of its own**, `KPG.Revoked.<sha256>` beside an
emptied record, added once and never changed, and the argument for it is kept because the fact it
rested on still holds. It was measured on KeePass 2.61.1 on `spikes/revocation-merge`, and
`CustomDataMergeRuleTests` still pins it method by method against the KeePass this builds with:
`PwDatabase.MergeInDbProperties` visits the source's keys and no others, so **a KeePass merge never
removes a key the target holds**, and it decides a key both sides hold by modification time, the
merge method and `SettingsChanged`, so **it never reads a value**. A key that is added once and
never changed survives every KeePass merge and import, whatever the clocks read and whether or not
a program in between dropped the times. Two things reversed it. A KeePassXC merge from an older
copy replaces the whole dictionary, in the paragraphs on other programs below, which took away
every revocation written since with nothing in the file to tell that it happened, where an entry
written once arrived in every merge that was run, `STORE.md` §603. And that key named a single
certificate, so a renewal made at a gate not yet told of the revocation escaped it once the
copies met, which a revocation by the client's key refuses, `STORE.md` §615.

**That key had itself reversed the empty record as the revocation**, and the argument for that is
kept because it was right about the merge and wrong about what the merge would be given. A merge
carries a newer value and carries nothing at all for a key that went, so the empty value was the
removal in a form the merge could carry. It carried only while the file kept the time the mark was
written at, which a save in another program usually does not, as the paragraph on other programs
below has it, after which KeePass 2.61.1 kept whichever side was stamped or already held the key.
The time was also the revoking host's clock, compared with the pairing host's record, so a
revocation made sooner after the pairing than the two clocks disagree by lost the same way. And
**File > Import** with *Overwrite existing* put back every record the imported copy held. In each of
those the client was admitted again with everything it had, and the plugin, which cannot see the
times or an import, could only revoke it again.

**An admission that a decision refuses is deleted by the plugin's next write**, with a deletion
record: an admission whose client is revoked, whose certificate was taken away or whose issuer is
retired. It admits nobody before that write and after it, since admission reads the decision and
never waits for the deletion, and an admission that a stale copy brings back arrives beside the
decision that refuses it. The clients page counts such an admission under the list and does not
list it as a client. The write it goes with is a write this plugin makes anyway: a revocation, a
certificate given up, a pairing or a renewal, keeping an authority, recreating or deleting it.
Opening or reading a database changes nothing in it. `STORE.md` §627. Held over a real database by
`PairingInRealDatabaseTests.A_pairing_deletes_an_admission_that_came_back_beside_its_revocation`.

**Nothing is removed for its age, and that reverses the prune of expired records.** The admission
of a certificate that has expired stays, and the clients page lists it as expired, until the
certificate is taken away, its client revoked or its authority retired. Past its `notAfter` it
admits nobody, since the gate reads the certificate's dates. Such a record used to go with the
plugin's next write of its own, and a revocation once its certificate had expired or the authority
it named was retired or gone, on the argument that kept they were a key for every certificate the
database ever issued, which a client given a week at a time, renewing as each runs out, makes a
number with no bound. That growth is real and is accepted until a compaction exists, `STORE.md`
§643. What reversed the prune is what it was decided on: this machine's clock, where no time
decides anything in the store, and a removal, where nothing in a file tells which copies have seen
a record. A clock that was wrong took a client's record out of the file, and setting the clock
right did not bring it back. Held by
`PairingInRealDatabaseTests.A_pairing_leaves_the_admission_of_an_expired_certificate_where_it_is`
and `MergeInRealDatabaseTests.An_expired_admission_a_merge_brings_stays_and_admits_nobody`.

**A standing answer to a renewal was changed rather than only added while it was a key**, unlike a
revocation, and that is what its move into the store reverses, `STORE.md` §618: two answers made
apart give the stricter there, and no merge decides by a time. What follows is the account of the
key. A later answer about the same certificate replaced an earlier one. That makes it a key a merge
can decide either way: Synchronize and the methods like it take the newer by modification time,
which a slow clock or a copy saved without the times turns around, and Overwrite existing takes the
other copy's whatever its time. The direction is safe, since a renewal still waits on a person
approving it, and no standing answer admits anybody, but the price is not always a dialog: a forever
that wins over a newer until a date answers every renewal `409 forever`, and the client's name is
red on the clients page until the operator lifts it there. A standing answer that will not read
counts as forever, and the log names it. It goes once no admission in force names the certificate,
its client revoked, the certificate taken away or its authority retired, with the next pairing,
renewal or certificate given up, or with a settings dialog's OK that writes into the store; it does
not go for the certificate having expired, since that admission stays, and this reverses its going
by the clock with the expired record. And it goes when the operator presses **Allow renewal** on
that certificate's row and accepts the settings dialog, which removes the key. A removal is the one
thing a KeePass merge never carries, so a copy of the file from before the lift brings the answer
back, red on the page again, and the operator lifts it again. **A refusal in this session has no key
at all**: the plugin holds it in memory, per client, until KeePass exits, because an answer about
one session written into the file would outlast that session in every copy of it, `PAIRING.md` §312.

**Each authority is one record, and that is the same argument the other way.** A merge that took
its parts separately could take the certificate from one replica and the private key from another,
and the host certificate would no longer chain to the authority beside it: the store reads that as
damaged, the gate serves nothing, and the only remedy revokes every client. Inside one record
there is nothing to take apart. `AuthorityRecord` is the shape, one flat object of strings, which
is what `JsonBody` already reads, and its content is protected in memory as a password is.

**One record per authority, filed under its digest, written once and never rewritten in place.**
What takes an authority out of service is a retirement, a record of its own added beside it and
never changed or removed: recreating retires the old authority, deletes its entry and writes the
new authority, and deleting retires it and deletes its entry. Anything that later wants to renew a
part, the host certificate for instance, needs a record of its own that is also written once.

**This reverses keeping the authority in the database's custom data**, one key per authority with
a retirement key beside each, and the argument for that is kept because the merge it was written
for still holds: a key added once and never changed survives every KeePass merge. What reversed it
is what other programs do to custom data, in the paragraphs on them below: a KeePassXC merge from
a copy that lacked the authority took it away in every merge measured, and a merge from an older
copy puts back the authority that copy held with every retirement written since gone, while
nothing in the file tells that it happened. An entry written once arrived in every merge that was
run, and the store's summary entry names what is present, so a removal another program makes is
read as a removal, `STORE.md` §603 and §604. Those keys are not read or migrated: a database that
holds them reads as having no authority, and is given a new authority and paired again,
`STORE.md` §645.

**Which authority a database has is decided by the retirements, never by counting authorities.**
The authorities are the records with no retirement beside them and no deletion record naming them,
so a retired authority that a merge brings back serves nothing, and its entry is taken away again
at the plugin's next write of its own. **More than one authority with no retirement serves
nothing**: that is copies that each created or recreated an authority, meeting in a synchronize,
and choosing between them is the operator's decision rather than this plugin's. A record that
passes its check and will not read whole, filed under a digest not its own for instance, is an
authority too: alone it is damaged, beside another authority it is one more candidate, which can
be retired and never kept, and it holds up that database alone, since **what one database holds
stops no other being served**.

**So the plugin asks, and answers `503 undecided` until somebody has.** A connection to such a
database is answered before a request is read, with each authority's own host certificate and no
responder. KeePass asks the operator which authority to keep in a dialog over its main window, the
first time the plugin sees the state: when the database is opened or unlocked, or at the save that
ends a synchronize, and never from a connection. For each authority it shows the fingerprint, when
it was made, and every client chaining to it with its profiles, when it was admitted and, where the
audit holds it, when it last made a request. Keeping an authority retires every other authority,
through the single writer's guard, and marks the database modified without saving it; the
admissions that the authorities not kept issued are deleted in that write, `STORE.md` §627, where
they used to stay in the custom data, admitting nobody, for the clients page to name.
Cancel serves nothing, and the KPG tab's authority page asks again, waiting for the settings
dialog's OK like everything else there. **A choice the writer refuses is tried again** before the
operator is told to choose again, which is the operator's rulings of 2026-10-14 and 2026-11-08:
another client's write holding the writer is waited out however long its save takes, which can be a
minute and more for a large database, and a refusal that does not end by itself, an entry dialog
left open on an entry the choice deletes, sets back or protects, the summary entry among them, or
KeePass busy with an operation of its own, is tried again for a few seconds and then put back in
front of the operator. The answer is given up when those seconds have passed, and the operator is
told once KeePass is no longer part way through an operation of its own: told from the retry's own
tick, the message stood inside that operation, and a save was measured waiting under it until it was
closed. **A lock closes the dialog unanswered**, as it closes a pairing or a renewal dialog and for
their reason: KeePass locks under a dialog the plugin does not register, and this dialog lists every
authority's clients. An answer given as the lock lands is not written, and the unlock asks again.

**The dialog waits for whatever would refuse its answer.** The question is posted at the open and
at the save, and it used to be held back by KeePass's two signals alone, `DESIGN.md` §119, on the
reading that a synchronize blocks interaction from its start. A synchronize shown in KeePass's
status dialog does not: that dialog opens the progress window, which pumps, before it raises the
block, and KeePass uses it for a synchronize a trigger starts while the first database opens. The
question was delivered there, over a synchronize that had not merged and with no main window on
the screen, and its answer was refused for the progress window. So the question waits on the
writer's own answer about KeePass, the progress window included, which is the answer the write is
refused on, and the two cannot differ. **That answer looks for a merge as well**, on the stack of
KeePass's own thread, as the queue of turns does, `DESIGN.md` §119. A merge another plugin runs
raises none of KeePass's signals and pumps as any merge does, and the answer to this question is
the one write that takes no turn, so nothing else kept it, or the question, out of such a merge.

**An answer retires nothing the dialog did not list.** KeePass raises a periodic trigger under a
dialog the plugin does not register, so a synchronize can run while the dialog stands and bring an
authority it does not list, or a retirement, which is another copy's answer. Keeping wrote to
whatever the database held by then, so an answer about two authorities retired a third, with every
client it admitted, that the operator was never shown, and a retirement is never removed. The
answer now carries the digests the dialog listed to the write, which is refused, inside the
writer's own turn, for a database whose authorities are any other set, an authority more or an
authority fewer. The operator is then asked again about the authorities the database holds, as a
state the plugin has not asked about always is: the round that noticed the new state at the
synchronize's save can ask nothing on top of the dialog standing, so it comes round again until
that dialog has closed, whichever way it closed. A database another copy's answer decided
meanwhile is served as that answer left it, and nobody is asked: the operator is told which
authority it serves, unless that is the authority chosen. The answer used to be tried again there
like a refusal that might clear, each try logged as an error, and then reported with the sentence
that nothing is served. The KPG tab's own question is held to the same rule.

**Copies that keep different authorities retire every authority.** Each copy asks on its own, and
the last requests it shows come from that machine's own audit, so copies asked before any answer is
synchronized can each keep a different authority. Each answer retires every authority it did not
keep, and a retirement is never removed, so once the answers meet in a synchronize nothing is served
and every client is refused, with no way back but a new authority and every client paired again. The
KPG tab then shows every authority the database has held as retired, rather than showing no
authority yet, as it does after a delete, which retires an authority too. **So the chooser warns,
before anything is chosen**, to answer on one copy and synchronize before answering on another.

**Recreating and deleting both delete every admission the retired authority issued, and no
revocation.** Each such admission names that authority as its issuer, and the retirement refuses
its certificate whatever a stale copy brings back. The admissions go with the retirement and
never without it: both commands wait for the settings dialog's OK, and the write that takes the
act deletes them, with a deletion record each, as the store stands then, `STORE.md` §614 and
§627. A revocation stays, since it names a client key and no authority, and it is what refuses
that client's admissions wherever a copy still holds an authority that issued them. This
reverses removing every revocation with the records, which was done while a revocation named the
authority that issued its certificate and so refused nothing the retirement did not; and that
had reversed recreation emptying the records rather than removing them, which it did while the
replacement went under the same key and an emptied record was the only mark a merge with an older
copy would carry. Held in a saved file by
`AuthorityInRealDatabaseTests.Deleting_deletes_the_admissions_of_the_authority_it_retires_from_the_file_and_keeps_every_revocation`.

**A merge with a copy from before brings none of those admissions back into force.** Each was
deleted with a deletion record, and an admission present beside a deletion record that names it
is out of force, `STORE.md` §623. An admission of that authority that this copy never held, as a
pairing made in the other copy leaves, names a retired issuer: it admits nobody, the clients page
counts it under the list, and the plugin's next write of its own deletes it.

**All of this is a claim about KeePass 2.48 or later with the plugin in it.** Other programs open
the database, and what they do to it was measured on `spikes/revocation-merge`, for KeePassXC 2.4.1
to 2.8.0 and KeePass 2.47. **Saving in any of them keeps every key and its value, and usually not
the times**: the file goes back to an older format, 4.0 or even 3.1, unless something else in it is
4.1 as well, such as the previous parent a KeePass delete or move records, and only KeePassXC 2.7.0
or later keeps the times even then. A revocation survived that while it was a key, being only
ever added, while a standing answer to a renewal goes to whichever copy still carries a time at
the next synchronize in KeePass, as the profile list did while it was a key. **A KeePassXC merge,
from 2.4.2, replaces the custom data of the database it merges into with the other copy's,
whole**, whenever the other copy was read in a later second, which a copy chosen to merge from
nearly always is. Merging an older copy into the paired database therefore takes away every key
written since, which was every revocation, admission record and profile list while those were
keys, and puts back the older value of every key changed since, a standing answer among them,
stamped, where the file keeps times, with the time of the merge, which a later synchronize in
KeePass takes as the newest. A group both copies hold
keeps the custom data of the database merged into, however the other copy changed it, and takes
the newer of the two times, so a rule narrowed in the other copy was lost without looking older
while a rule was a key of that dictionary.
Merging the other way, the paired database into an older copy, gives that copy the paired
database's keys.

**Android programs that open KDBX 4 files were measured on the same branch**, in an emulator of
Android 11: Keepass2Android 1.15-r6 and 1.08d-r5, KeePassDX 4.5.8, 3.2.0 and 3.3.0, and KeePassDroid
2.7. No save and no merge in any of them removed a key from the database's custom data, so a
revocation held there while it was a key. **A save in KeePassDroid 2.7 deletes the custom data of
every group and entry, and a save in KeePassDX 3.2.0 or 3.3.0 that of every group**, without moving
the modification time of the group or entry, so nothing on the node shows that it went: measured
with the rules that were kept there, which such a save deleted, and which are records of the store
now. A save in KeePassDX 4.5.8 keeps a node's custom data and the times, and a save in
Keepass2Android keeps it and drops the times, as a save in KeePass 2.47 does. A Keepass2Android
merge gives a key both copies hold the value of the copy that is open unless the arriving copy's
settings were changed later, so the admission record and the profile list went back to the older
copy's values whenever the older copy was the open copy or arrived with the later settings. A
KeePassDX merge, in 4.5.8 and 3.2.0, puts the arriving copy's custom data on a group or an entry
whichever copy is newer, so an older, wider rule arrived under the newer time while a rule was
kept there. KeePassDroid 2.7 showed no merge to run. A group or an entry whose rules went so
answered each profile with the nearest rule above it, or refused a profile no rule above named,
and its **Refuse any profile not named here** check mark went with the rules, so wherever they
had narrowed what a group above grants, a client reached more. **That is what a rule kept as a
record of the store reverses**, `STORE.md` §617: none of those saves and merges reaches a rule
through a node's custom data any more.

Nothing here detects any of it. After any change made outside KeePass, the clients page, the
Profiles page and the authority are where to look. What the same programs do to the entries
that the authority, the admissions, the revocations, the profile names and the rules are kept
in is `STORE.md` §603 and §637.

**KeePass's own Database Maintenance dialog lists the keys of this dictionary on its Plugin Data
tab, and deletes any of them on request.** None of that is a way to manage what this plugin
keeps, and nothing of the plugin's is among those keys. The authority, its retirements, the
admissions, the revocations, the profile names, the rules and the standing answers to a renewal
are entries of the plugin's store, and what deleting or editing such an entry in KeePass does is
`STORE.md` §628 and §642: a standing answer deleted for good is lost, and refuses forever until
**Allow renewal** lifts it, §618.
While a revocation was a key, deleting it there left the client refused only by its emptied
record, which a stale copy merged in afterwards could beat. Revoking, recreating and deleting are
done on the KPG tab, which writes each of them the way this section describes.

### How each value is written §222

A certificate and its key are stored as **base64 of a PKCS#12 with no password**. No password
because there is nothing for one to protect: the database's own encryption is what guards this, and
a passphrase here would be cryptography written by us over cryptography that already works. The gate
key is base64 of the 32 raw bytes. Sizes are unremarkable: roughly 1.4 KB of base64 for the
authority.

**The algorithms are the ones Windows wrote**, measured rather than remembered: a SHA-1 MAC, and the
key and the certificate each under `pbeWithSHAAnd3-KeyTripleDES-CBC`. The rest of the shape differs,
and nothing reads the difference. Bouncy Castle iterates 1024 times where Windows did 2000, writes
the MAC's digest with NULL parameters where Windows wrote none, and names the key `KeePassGate`
where Windows named its key storage provider. So an authority Windows wrote and one Bouncy Castle
wrote differ in those three, and Windows, Bouncy Castle and the client's `cryptography` each read
both. So does a replica still running 0.1.0, which loads what this release writes through Windows
with its key, as it loaded its own, and signs with it, `CertificateBlobTests`. It is written as DER,
because Bouncy Castle's own is BER with lengths left open, which `cryptography` reads only through a
fallback it warns is deprecated. The certificate's algorithm is the one that had to be asked for,
because Bouncy Castle's own is RC2 with a forty-bit key, which OpenSSL keeps in its legacy provider
only. `cryptography`'s wheels load that provider unless `CRYPTOGRAPHY_OPENSSL_NO_LEGACY` is set, so
with that variable set, or on an OpenSSL built without the provider, the client could not have read
the identity a pairing hands it, where triple DES reads everywhere.

**Nothing here is ever written to the disk, and that is what decided the transport.** Importing a
PKCS#12 on .NET Framework writes a key file into `%APPDATA%\Microsoft\Crypto\Keys` for as long as
the certificate is loaded, unless `EphemeralKeySet` is asked for. The plugin imports none of its
own: Bouncy Castle writes the stored form and reads it back, and Windows is handed the certificate
inside without its key. While Windows read these, everything asked for the flag, and one read
still does. An admission record's certificate, §223, is bytes a database chose, and Windows takes
a PKCS#12 there as readily as a certificate, so it is read under `EphemeralKeySet`, and a key a
crafted record carries is written nowhere before the record is refused.

Keeping every key off the disk is only possible because the TLS server is managed code. An
ephemeral key **signs perfectly well and cannot be used with SChannel at all**, in either
direction, so serving with the platform's own TLS would have meant putting the host key on the disk
for as long as a database was open. `DESIGN.md` §133 records the reversal; `EphemeralKeyProbe.cs`
and `ManagedTlsProbe.cs` are the two halves of the measurement.

So all three secrets live in the database and in memory, and nowhere else the plugin puts them: the
authority that can mint identities, the host key that authenticates a server, and the gate key.

**KeePass can put them somewhere else.** Its own File, Export, KeePass XML (2.x) writes every
entry's protected strings and the database's custom data in the clear with everything else, so an
exported file holds the authority's private key, the host key, the gate key, the store's key and
every admission record, and KeePass announces an export to no plugin. Such a file is the unlocked
database: it is kept as the database is, and if it leaves the operator's hands the answer is
recreating the authority, which [`PAIRING.md`](PAIRING.md) §301 names as the recovery from a
leaked authority key.

**Reading is total and fails closed.** Anything that will not decode leaves the authority *damaged*
rather than absent, and the two are never conflated: an absent authority is created, a damaged one
has to be recreated, and only the operator can decide to do that. A host certificate that does not
chain to the stored authority is damage too, checked with the anchor pin rather than by comparing
names, because every authority this plugin issues carries the same subject and a name comparison
therefore accepts somebody else's host. And a key is believed only when it is the private half of
the certificate beside it, because a PKCS#12 pairs the two by a local identifier anybody can write.

**A crafted value costs what refusing it costs, not what it asks for.** A PKCS#12 names its own
work: how many times its MAC and each bag's key are derived, and how many bags there are. So before
Bouncy Castle's PKCS#12 store is handed anything, a value over 64 KiB is refused, and so is one
asking for more than 10,000 iterations of the MAC or of any bag, where this plugin writes 1024 and
Windows wrote 2000, and one holding more than four parts, four bags or one key bag. The bags inside
an encrypted part are counted by opening it, once its own count has passed, since a key bag sealed
in one is otherwise unseen until Bouncy Castle has done its work; and a key derivation whose count
cannot be read without running it, PBES2, is refused. The MAC is checked first, both ways Bouncy
Castle reads an empty password. The worst value still accepted costs about 44 ms here, where one
crafted bag at 5,000,000 iterations cost three seconds. The certificate and its key both have to
be on P-256, named by the curve's identifier rather than spelled out, and the certificate's point
has to be uncompressed, which is what this plugin and Windows both wrote.

**Pairing tokens are not here.** They live in plugin memory and expire on TTL or on KeePass exiting,
whichever comes first. [`PAIRING.md`](PAIRING.md) says why that is more than a simplification.

### The trap this store carries §206

`GroupForm` **takes a copy of a group's `CustomData` when it opens and writes that copy back on
OK**. A value written to the live group therefore survives Cancel and is destroyed by OK, silently.

Measured rather than remembered, in `SnapshotHazardTests`: the snapshot field is still there in
KeePass 2.61.1, a group's custom data can be replaced wholesale from inside KeePass but not from a
plugin, a write made while the dialog is open is destroyed by OK, and an authority created before
the dialog opened comes back unharmed, for the reason given below. That Cancel leaves the write
alone is read off the dialog's code rather than pressed, and the same tests hold it: only its OK
puts the copy back.

**The dialog this plugin puts its tab on carries no such snapshot**, which the same tests assert
against the real type. So the buttons on the KPG tab are safe from their own dialog, and the hazard
belongs to whatever writes while somebody has a *group* open. Nothing in the custom data this
store holds is in a group, since §204 keeps it in the database's own dictionary, and a node's
rule, which a group dialog could destroy while it was a key of the node's custom data, is a
record of the plugin's store now, `STORE.md` §617, so a group dialog destroys nothing of it.
The dialog that copies that dictionary is Database
Maintenance, which writes it back the same way, and [`PAIRING.md`](PAIRING.md) §307 is where
pairing answers that dialog. The authority, the admissions and the revocations are in a group, as
entries of the plugin's store, and no group dialog copies an entry: an entry edited in KeePass's
entry dialog is set back at the plugin's next write of its own, `STORE.md` §628, and a write is
refused while that dialog holds an entry it would delete or set back, `STORE.md` §626.

Here the consequence is worse than a lost setting, so the design does not merely try to prevent it:

**An admission record is required on every connection, and is where the authorized profiles are read
from.** A record lost to a stale snapshot, to a failed save, or to the operator answering "Discard
changes" makes that certificate **inert**, and the client must pair again. Without the rule, a lost
record leaves a fully working certificate that nobody can see and nobody can revoke, which is
fail-open in a design that fails closed everywhere else.

---

## 5. The certificates §207

```text
   authority (self-signed, P-256, CA:TRUE, pathLen:0)   the `ca` member
     |
     +-- host certificate, normal      serverAuth               the `host` member
     +-- host certificate, responder   serverAuth + KPG marker   memory only
     +-- client certificate            clientAuth               issued at pairing and at renewal
```

| | Life | `basicConstraints` | `keyUsage` (critical) | EKU |
| --- | --- | --- | --- | --- |
| Authority | 100 years | CA true, **pathLen 0** | keyCertSign, cRLSign | **none** |
| Host, normal | as long as the authority, issued with it | CA false | digitalSignature, keyAgreement | serverAuth |
| Host, responder | 90 days, re-minted at every unlock and at every lock | CA false | digitalSignature | serverAuth, plus a KPG marker |
| Client | 1 to 365 days, as the operator picks | CA false | digitalSignature | clientAuth |

Every certificate carries a **subject key identifier** and every leaf an **authority key
identifier**, so the authority can be renewed later without invalidating what it signed. A leaf's
authority key identifier is its authority's subject key identifier copied, so a stored authority
whose subject key identifier is absent, empty or not DER cannot issue, and is refused as an
authority that cannot: a pairing under it is answered `500 internal error` and its database is
served without a responder, while every other open database is served as before. **`pathLen 0`**
so it cannot mint a subordinate. **No EKU on the authority**, because an EKU there constrains
everything beneath it and buys nothing. **A leaf's `notAfter` is clamped to the authority's**, so
nothing outlives what vouches for it, **and its `notBefore` is never earlier than the
authority's**, so nothing starts before it either: a clock that reads earlier than the authority's
start issues nothing.

**A client proposes how long its certificate lasts, and the operator picks.** A pairing request may
ask for whole days from one to 365, and the approval dialog offers 1, 7, 30 and 90 days and a year,
with the one asked for preselected and offered as a choice of its own when it is none of those,
`PAIRING.md` §303. A request asking for nothing is offered a year, which is what every client
certificate was given before any could ask, so asking can shorten a certificate and never lengthen
one. The issuer holds the same bounds as the wire, `Lifetime`, so nothing reaches a certificate
that the document would have refused on the way in. A short life is worth having on a client
certificate where it was worth nothing on the host's: a client's key lives on another machine, in
a program that may be told what to do by what it reads, and a certificate that runs out is refused
without anybody having to remember to revoke it.

**A hundred years for the authority and its host certificate, and nothing counts down to it.** What
was asked for was no expiry at all, RFC 5280's `99991231235959Z`, with a hundred years if that
turned out not to be fully supported, and it is not: Bouncy Castle writes the end of year 9999 as
the RFC asks when asked to, and `X509Certificate2.NotAfter` reads it back as an earlier moment in
any time zone ahead of UTC, with nothing to show it. A hundred years is read exactly everywhere it
is read, `LongLivedCertificateTests`. The two are issued together and end together, because a host
certificate that ran out first, five years into an authority's ten, was an outage every client met
at once with nothing to reissue it. A shorter life protects nothing here: the host key is kept in
the same document as the authority's and cannot leak without it, and a client pins the authority
rather than the leaf. So nothing warns as either end approaches, nothing refuses a pairing ahead of
it, and neither is reissued or renewed. A leaked key is answered by recreating the authority,
whatever its dates say. The responder keeps its ninety days, §208.

### Why there are two host certificates §208

A locked KeePass has closed its database, so the authority, the admission records and the normal
host key are gone. Without help there is nothing to complete a handshake with, and a client gets a
bare connection failure indistinguishable from a wrong port, a stopped KeePass, or an attack.

The **responder certificate** is minted at unlock and again at the lock it answers, and held only in
plugin memory. While locked it is the only identity available and it can do nothing but answer
"locked".

- **The normal host key is let go at lock**, or two certificates buy nothing: memory disclosure
  while locked would otherwise yield the full host identity. What letting go means is worth being
  exact about, because the phrase promises more than the platform gives. What is overwritten is the
  plugin's own byte arrays, the stored PKCS#12 the host certificate and its key travel in and the
  authority material's copy of the gate key among them, and the certificate handles are released. So
  is every other array the plugin decodes a stored PKCS#12 into or writes one from, the authority's
  as well as the host's, as soon as it has been read or written, so a read leaves no copy of either
  behind. The keys are neither of those, the host's, which the TLS server signs with, and the
  authority's, which issues. Each is a BouncyCastle `AsymmetricKeyParameter` over an immutable
  `BigInteger`, which offers nothing to overwrite, so what happens to it is that the last reference
  goes and the collector reuses the memory when it gets to it. Read as erasure that would be a claim
  this design cannot keep. It is the bound §220 concedes anyway: a dump taken at the wrong moment
  finds it, and what the drop takes away is the key being reachable for as long as the database
  stays locked. The same bound covers what no plugin can overwrite: the text of the authority's
  record, which KeePass keeps protected in memory and hands out as an immutable string each time it
  is read; the members parsed out of that text as the authority is read, its certificates and their
  keys and the gate key as text, which are left to the collector as soon as the authority is read,
  since a reading of the store keeps of a record no member but its `id` and the store's key, the key
  as bytes; and whatever Bouncy Castle parsed the bytes into. The key a node's tag is made with,
  derived from the host's PKCS#12, is an array of the plugin's own, and it is overwritten with the
  rest, as the array it was derived into is. So are the bytes of the store's key every reading of
  the store finds: the gate's when it lets go of a database's readings, at the lock and at a close;
  those the tab, the chooser, the writer and the pairing dialog read, as soon as each is done with
  its reading, the dialog's within the turn it reads in; the copy
  an act of theirs holds, once its write is done, written, refused or thrown; and the key a composed
  record is read back with. The memory a gate keeps of what it served a database with, `STORE.md`
  §656, goes at the lock and at the close as well: a copy of the gate key, overwritten, and the host
  certificate with its key, under the same bound as the host's key above. A database whose store key
  has gone from the file is not served: the memory answers it with the store's word, whichever word
  that is, `503 lost` for a file with no key, and goes at the lock, at the close, when KeePass ends,
  where a store that holds nothing back gives no authority to answer with, and where a key that
  stands retires the authority it was made from. The gate key itself is not let go at all: a copy of
  it is kept for the responder, as the bullet below has it.
- **The responder must be distinguishable by what it carries**, not by convention, so a client can
  refuse to send real requests over it. It is a `kpg://host/responder` URI in the subject
  alternative name rather than an extension of its own: the UUID arc `2.25` does not survive
  being read back on .NET Framework, and a private arc would be squatting on somebody else's
  number. An impostor holding the responder key reads nothing, but
  could answer with a fabricated secret, and an agent acting on a forged credential is worse than an
  agent that fails.

  **The client is where that check has to happen, and PyKeePassGate now makes it.** A locked
  database answers `503 locked` on every path before it has read a request, so the plugin cannot ask
  the question about itself: the marker exists for the peer to read. The responder chains to the
  same authority as the normal host certificate, so a paired client's pin admits it and nothing else
  the client holds tells the two apart. The refusal is raised **before the request is written**,
  which is what the paragraph above is asking for: waiting for the answer would hand the request
  body, and whatever value it was going to store, to whoever holds that key. Pairing is covered by
  the same check, so a hello to a locked database costs no invitation.
- **The gate key must stay resident too**, or the gate never resolves and the responder is never
  reached. Of the three secrets it is the right one to keep: it authenticates nothing, and every
  paired client already holds it.
- **Its life has to cover the lock, not the session**, and that is a correction rather than a
  detail. It was seven days, minted at unlock, on the reasoning that a responder only has to
  outlive one session. A lock is unbounded: a machine locked over a holiday presented an expired
  certificate, the client refused it, and what the operator got was the bare handshake failure this
  certificate exists to replace, which is the one failure it was built for. So it is minted again
  at the lock it answers, leaving its whole life ahead of that lock, and the life is ninety days.
  What a shorter one buys is a bound on how long a leaked responder key is still accepted, and that
  key can do one thing, which is to claim a database is locked; the marker above is what stops a
  client acting on anything else it says, and the marker carries that argument rather than the
  dates. Ninety days is still a bound, so a lock that outlasts it presents a responder that has
  expired and meets the bare failure again, which is the price of there being a bound at all.

A lock with no responder to answer it really is a bare connection failure. A KeePass that has never
been unlocked since starting has minted none yet. A database holding several authorities, none of
them kept, mints none, since minting a responder would be choosing between them, §204. And a lock at
which a fresh responder could not be minted, while the responder held had expired, has none to
answer with. A lock that outlasts the ninety days above answers with a responder the client refuses,
which the client meets the same way.

---

## 6. What a client certificate says §209

Claims travel as subject alternative name URIs carrying **profile names only**:

```text
URI:kpg://profile/ops
URI:kpg://profile/readonly
```

The database is not named, because the issuing authority already identifies it. That removes the
`kpg://any/<profile>` escalation an earlier draft carried, where a client meant to be trivial in one
database gained whatever a differently configured database granted the same name.

**The SAN is a convenience, not the authority.** Profiles are read from the admission record. That
follows from section 4, and it has a pleasant side effect: the parsing rules below stop being
security critical and become hygiene.

### Parsing rules §210

**One thing is ever read out of a subject alternative name and it is the responder marker of
§208**, which is what §212 leaves, and the client is what reads it: the plugin writes subject
alternative names and reads none. So one of these rules belongs to that reader and the rest belong
to the two places that write.

- **Never read the extension with `X509Extension.Format`, and never use `GetNameInfo`.** Both were
  measured against a hostile SAN and both fail. Parse the ASN.1 and take only entries tagged
  `[6] uniformResourceIdentifier`. PyKeePassGate asks its decoder for exactly those, and a
  certificate whose extension the decoder refuses is not a responder, which is safe precisely
  because the question is whether one particular URI is there.
- **The reader compares and never takes a claim apart.** The question asked of it is whether one
  exact URI is present, compared byte for byte, so a percent escape, a trailing slash and a near
  miss all fail to match without any rule of their own. There is nothing that parses `kpg://profile/<name>`,
  because §212 leaves nothing to parse one for.
- **Reject rather than repair, at the two places that write.** Issuance refuses a profile name that
  is not one to sixty-four characters of ASCII letters, digits, hyphens and underscores, and
  refuses the ACL's own `default` whatever case it is written in, so a claim carrying a percent
  escape, an empty name or a smuggled newline is one this authority will not mint. Reading an
  admission record drops a profile that could not have been issued as a claim, §223, so a record
  edited by hand grants nothing strange either.

### The measurement behind those rules §211

A SAN was built with one honest claim and three hostile entries. `SanProbe.cs` reproduces it.
The probe predates the move to profile-only claims, so it uses the old `kpg://any/...` shape; what it
measures is how a parser reads a SAN, which the claim format does not affect.

```text
--- X509Extension.Format(true) ---
URL=kpg://any/readonly                    <- the only real claim
DNS-Name=URL=kpg://any/admin              <- a dNSName whose TEXT is a URI claim
RFC822-Name=URL=kpg://any/super           <- an rfc822Name doing the same
URL=kpg://any/ops%0AURL=kpg://any/admin   <- a newline smuggled in as a percent escape

GetNameInfo(UrlName, false): [kpg://any/ops%0AURL=kpg://any/admin]
```

Three defects. `Format` output is not a data format, and a parser splitting on `URL=` reads the
dNSName entry as a claim to `admin`. The labels are localized, so a parser matching them breaks on
the operator's Windows language. And `GetNameInfo` returns one value, here the attacker's rather
than the honest one. The ASN.1 tags make the distinction the rendering destroys: `0x86` URI, `0x82`
dNSName, `0x81` rfc822Name.

### The record is the ceiling, and a claim is read by nobody §212

```text
permitted = record(kdbx) INTERSECT acl(kdbx)
```

**Neither term is the certificate.** The admission record says which profiles this client was
approved for, less any the database no longer names, the rules say what those profiles reach, and
the router allows the intersection.
Nothing anywhere reads a profile claim, and the plugin reads no subject alternative name at all: the
one thing ever read out of one is the responder marker of §208, and the client is what reads it.

This heading used to say a claim was a ceiling, which invites the reading that a certificate can
narrow what its record grants. It cannot, and writing code that assumed it could would be a defect
rather than a refinement. A certificate whose record is missing grants nothing at all, and one whose
record names a profile its claims do not grants what the record says. The record being the only
ceiling is what makes a lost record fail closed instead of leaving a working certificate nobody can
see and nobody can revoke, which §206 argues and §221 records as a reversal.

The claims are for a person reading a certificate, and issuance is where they are written. What the
formula does say holds either way: the certificate cannot be widened by its holder, because the
issuer generates the key and every extension; the database can withdraw access without touching PKI;
and an unrecognized profile intersects to nothing.

---

## 7. Validating a connection §213

Order matters, and every failure is a refusal.

1. **The gate resolves the SNI**, before any TLS work. Section 8.
2. **TLS 1.3 offered and nothing older**, so the handshake fails rather than falling back.
3. **A certificate is always asked for**, decided in `GateTlsServer` rather than by an
   SChannel flag, with a callback that tolerates absence. Measured:
   `false` makes the server send no `CertificateRequest` under TLS 1.3, so a client holding a good
   certificate never offers one; and `true` requires nothing, since a certificate-less client
   completes the handshake anyway. Enforcement is the route table's job.
4. **The anchor is pinned** against the resolved database's authority, and a certificate that
   fails it is taken as no certificate. See below.
5. **The EKU is checked explicitly**: `clientAuth` for a client, here, and `serverAuth` for a host,
   by the client, `PAIRING.md` §304.
6. **Validity dates**, compared exactly. The allowance for a clock behind the one that issued is in
   the certificate rather than in this check: everything the authority issues starts five minutes
   before the moment it was issued, so a client checking the host's dates is given the same five
   minutes, and only on `notBefore`.
7. **The admission record is looked up** by SHA-256 over the certificate DER. Absent means refused,
   and so does an admission that a revocation, a certificate taken away or a retirement refuses,
   §217.
8. **The profiles come from that record.**

### The anchor pin, which the obvious code does not do §214

**Measured, and this would have shipped.** Two authorities, one client certificate from each,
validated with the pattern almost anyone writes first:

```text
chain.ChainPolicy.RevocationMode = NoCheck
chain.ChainPolicy.VerificationFlags = AllowUnknownCertificateAuthority
chain.ChainPolicy.ExtraStore.Add(ourAuthority)
chain.Build(presented)

  our client      -> True
  FOREIGN client  -> True     <-- signed by a completely different authority
```

`ExtraStore` supplies candidates without constraining the anchor, and
`AllowUnknownCertificateAuthority` tolerates an untrusted root without saying which.
`X509ChainTrustMode.CustomRootTrust`, which would fix it, is .NET 5 and later.

**And the engine went out on the way to `False`, which nothing here asked it to do.** When it could
not find the issuer it read the leaf's authority information access extension, RFC 5280 section
4.2.2.1, and downloaded whatever that named before `Build` returned. The leaf belongs to whoever
opened the connection, so the address does too: measured, one address that accepts and answers
nothing held the connection thread for 15.0 s, and the request really was issued by the KeePass
process. `X509ChainPolicy.DisableCertificateDownloads` would have forbidden it and is .NET 5 and
later; `UrlRetrievalTimeout` at a millisecond bounded the wait, to 79 ms, and the request was
still opened. §217 declines to depend on the network at all, and this was that network arriving
through a door nobody opened.

**So the pin asks the question itself, and builds no chain.** The hierarchy is one authority and
the leaves it signs, §207, which makes the whole question small:

```text
both are of the one shape    canonical DER, so that encoded again each is the bytes that arrived,
                             and signed ecdsa-with-SHA256 with its parameters absent
the anchor is an authority   CA:TRUE, keyCertSign if it has a key usage,
                             its key EC on P-256, named by the curve's identifier,
                             named as its own issuer in its own bytes and signed by its own key
the leaf is no authority     CA:TRUE on a leaf is refused, so the anchor cannot be its own leaf
the leaf names the anchor    its issuer is the anchor's subject, byte for byte
the anchor signed the leaf   its signature checks against the anchor's key
nothing critical is unread   every critical extension is one this plugin writes
```

Basic constraints are read for `CA:TRUE` and nothing more: a path length is never consulted,
since nothing stands between the leaf and the anchor for one to count. Bouncy Castle checks the two
signatures, and that is all it is asked. With the pin: our client `True`, foreign client `False`,
and a foreign authority named exactly as ours, which every authority this plugin makes is, refused
by its signature. Nothing the leaf names is fetched, and nothing a machine store holds can change
the answer, so there is no case left in which the pin cannot answer at all. **Anything of another
shape is an answer, and the answer is no**: a certificate that will not parse, an anchor whose key
is RSA or whose curve is spelled out, a signature of another algorithm or an encoding that is not
the canonical one is refused as the foreign client is, and nothing a peer sends makes the pin
throw. Dates are not asked: §213 checks them on the connection, and everywhere else an expired
certificate still belongs to its authority.

**A refusal here is not a refused connection.** The certificate is taken as no certificate, so the
handshake completes and every request on the connection is answered `401 unpaired`, as a caller
presenting nothing is, except that a connection that arrived on an invitation still reaches the
pairing routes.

**The comparisons go through one door.** `FixedTime.Equals` is what does them, because every
comparison of certificate bytes in this plugin goes through that one door and one that did not
would be a question a reader has to answer again. On its own it defends nothing here: an anchor
is a public certificate, and every client that can reach this code was handed a copy of it at
pairing. What is load bearing is that the anchor's own key and bytes are what is checked, which
is the line the platform would not write.

`SslPolicyErrors.None` is the **wrong** test, for a client built on `SslStream`. The authority is
deliberately in no machine store, so a correct deployment always reports
`RemoteCertificateChainErrors`, and since the SNI rotates it always reports
`RemoteCertificateNameMismatch` too. Such a client's callback ignores `SslPolicyErrors` entirely and
reaches its own verdict; the reference client is not one, and `PAIRING.md` §304 has how it checks.

### Why hostname verification is abandoned §215

The SNI is rebuilt per connection, so it can never appear in a SAN. Dropping the name is safe only
because **the authority is single purpose**: hostname verification exists to stop a certificate
valid for one host being accepted for another under a *shared* authority, and this one issues for
exactly one database. Any `serverAuth` certificate under it is that database's host by construction.

The EKU check closes the one remaining gap, since the same authority also signs clients and would
otherwise let a client certificate be presented as a host.

The trade is real: the callback becomes the entire authentication, so a bug there is total. That was
already true for the pin, which the platform will not do.

---

## 8. The gate §216

```text
SNI = hex(nonce_16) "." hex(HMAC-SHA256(key, nonce)[0..16]) ".kpg"
```

Two labels, because 64 hex characters exceed the 63-octet DNS label limit. The server reads the
ClientHello off the socket itself, since .NET Framework exposes no server-side SNI callback, then
replays the buffered bytes into the Bouncy Castle TLS server through a prepending stream.

Resolution tries, for **every open database**: its gate key, then each unexpired pairing token
held in memory. Comparison is fixed time. **No match drops the connection before any TLS work**,
with no response and no oracle, so an expired token is indistinguishable from one that never
existed.

Two properties follow. A passive observer sees a different name every connection, and under TLS 1.3
the certificate messages are encrypted, so SNI is the only cleartext identifier and it now carries
nothing. And an attacker who cannot compute a tag never reaches TLS, certificate selection, or
the pairing dialog.

**The ClientHello parser is the first code to touch unauthenticated bytes.** It stays total and
bounds checked, returning no name rather than throwing.

Two honest limits. The tag is **replayable**, since the nonce is client chosen and the server keeps
no record, so it stops blind probing rather than an observer who captured one connection. And SNI
travels **in the clear**, so an observer learns that some database is being addressed, though not
which one.

---

## 9. Admission and revocation §217

**CRL and OCSP are rejected**: both assume infrastructure disproportionate to a local plugin, and a
CRL adds a refresh window in which a revoked client still works.

The admission record does both jobs. A certificate that chains, passes its EKU check and has no
record is refused, and so is a certificate whose client is revoked or that was itself taken away.
Either is a record of the plugin's store written beside the admission, §204, and takes effect on
the next connection, offline and immediately: the store is read from the live database at every
request and no answer is kept beside the certificates, so nothing has to be reloaded for a
revocation to bite. A certificate that several admissions name is admitted by none of them,
`STORE.md` §615.

[`docs/clients.md`](docs/clients.md) is the same thing said to whoever presses the button.

**The operator revokes from the KPG tab's clients page**, which lists every client the database has
admitted with the certificates it holds and what each may reach, and takes a client away. It is a
list rather than a bare set of names because revoking is a decision about access, and a name on
its own does not name what is being taken. It sits under the widgets that hand an invitation
out, on the same page, because a list of clients is what an invitation turns into and the
operator letting somebody in is the person who should see who is already in. It writes the way
the rest of that tab writes: the client's rows go at once, the revocation lands when the dialog
is accepted, and saving is what keeps it. Cancel discards it.

**The operator's revocation names the client and not a certificate.** A client's certificates are
a group of rows under its name, and the X on the first of them revokes the client with every
certificate it holds, by its client key. This reverses a row's X taking that row's certificate
away and no other: a client that had renewed kept its other certificate, and a certificate it
renewed to at another machine, or before the dialog was accepted, stayed admitted, where a
revocation by the client key refuses each of them wherever it was issued, `STORE.md` §615.
**The page takes a single certificate away as well**, with **Take certificate away** in a row's
menu: it writes the record a client's own giving up writes, of kind `withdrawn`, for that row's
certificate alone, so the client keeps every other certificate it holds and is not revoked,
`STORE.md` §648. That is how a record that a lost answer to a renewal left behind is cleared,
`PAIRING.md` §312, and it waits for the dialog's OK as the revocation does.

**A client gives its own certificate up with `DELETE /gate/certificate`**,
[`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) §509. It takes no body and names nothing,
because the certificate on the connection is what is given up, so a client can give up its own
certificate and nobody else's. It writes a record of kind `withdrawn` that names that single
certificate, `STORE.md` §648, through the single writer every change goes through, refused
while Database Maintenance is open on the database, and answers once the database is saved and the
file read back. The client's other certificates stay admitted, the certificate a renewal issued
among them, which the record names. **This reverses a client's own act writing what the clients
page writes**: the reference client calls the route with the old certificate after every renewal,
`PAIRING.md` §312, and a renewal copies the client key, so written as a revocation by that key the
call would refuse the certificate the renewal had just issued. Nobody presses OK on it, so it does
not wait for a dialog the way the page does, and a settings dialog open at the time leaves it
alone, since what that dialog holds for the store is written at its OK against the store as it
stands then. A client wants this for a key it no longer trusts or a certificate it no longer
needs; it grants nothing a client did not already have, since a client that holds a certificate can
already stop using it. Held over a real database by `GiveUpInRealDatabaseTests`, among them
`The_certificate_a_renewal_issued_is_still_admitted_after_the_old_certificate_is_given_up` and
`A_certificate_given_up_that_no_save_kept_puts_back_what_its_write_deleted`.

**Both are written to the audit**, `DESIGN.md` §120, at the moment they take effect. A client's own
is a line by the key it gave up and the client key its admission carried, once the record is in the
database. The operator's revocation names no certificate, so it is a line for each certificate of
that client that the dialog's OK ended in this copy, each with its key and the client key, read
from the store just before OK is applied, and a single line with the client key alone where the
copy admitted no certificate of the client. An admission that admitted nobody before the OK,
under an authority that is not served, of a certificate several admissions name, or that does
not read, gets no line. A certificate that the operator took away is a single line by its key,
where the OK wrote its record: with the client key of the admission that admitted it before the
OK, or with the certificate's key alone where it admitted nobody then. It gets no second line
where the same OK revokes its client, and no line where it was given up or taken away before
the OK. Held by
`GateWiringTests.A_revocation_writes_a_line_for_each_certificate_of_the_client_and_the_client_key_alone_where_the_copy_holds_none`,
`GateWiringTests.A_revocation_writes_lines_only_for_the_certificates_this_copy_admitted`,
`GateWiringTests.A_revocation_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel`
and
`GateWiringTests.A_certificate_taken_away_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel`.

Recreating the authority is the other way, and it is the blunt one: it revokes every client at once
and each has to pair again. That is a recovery from a compromised authority, not a way to remove one
client.

**The key is SHA-256 over the certificate's `RawData`**, compared in fixed time, stored lowercase
hex. `X509Certificate2.Thumbprint` is **SHA-1**, measured at 40 hex characters, and a design that
says SHA-256 throughout should not key its allowlist on it.

Each record holds that key, the certificate itself so expiry is knowable, the subject as issued, the
**client key**, the **approved profiles**, the issuance date, the authority that issued the
certificate and, where a renewal wrote the record, the certificate it was renewed from, §223.

**The client key says which client a record belongs to**, and it is the operator's ruling of
2026-10-16. It is the admission key of the client's first certificate, the one it paired with:
pairing writes the new certificate's own key there, and a renewal copies it from the record that
asked, so every certificate a client is ever issued carries the same one. A record is one
certificate, and a client can hold two at once, since a renewal leaves the old record valid until the
client gives it up, `PAIRING.md` §312; and a client chooses its own name, so two programs may pair
under one, such as one program on two machines. Neither the name nor the certificate tells clients
apart, and the client key does: **every count and naming of clients goes by it**, so a renewal never
counts twice and two programs under one name are two clients. Names need not be unique, and nothing
is ever revoked by name: a revocation names the client key, and refuses every certificate whose
record carries it. Written once with the record and never changed, so no merge has anything to
decide about it. It grants nothing, so it is not checked against anything: whoever holds the
store's key can write a whole record.

This reverses counting clients by name, which made a renewal one client again, after counting by
record had made it two, and merged every program that chose the same name into one. It also
reverses an earlier ruling of the same day, one admitted certificate per name with issuance in two
steps, withdrawn after review: revoking by name let a client push another out by choosing its name,
and let a merge bring a revoked one back.

### The stored form §223

An admission is the content of a record of kind `client` in the plugin's store, `STORE.md` §615:
one flat object of strings, a member for each field, under the record's check.

| Member | Holds |
| --- | --- |
| `certificate` | the admission key: the SHA-256 over the certificate, as 64 lowercase hexadecimal characters |
| `der` | the certificate itself, base64 of its DER |
| `client` | the client key, in the form of an admission key |
| `issuer` | the digest the authority that issued the certificate is filed under, §204 |
| `profiles` | the approved names, separated by single spaces, each once and in ordinal order |
| `subject` | the subject as issued |
| `admitted` | the moment of the approval, RFC 3339 in UTC to the second |
| `from` | on a record that a renewal writes, the admission key of the certificate it was renewed from |

Strict rather than general, and that is a decision rather than laziness. Everything in a record has
already been through issuance, which refuses a subject or a profile that is not a plain name, so
the reader can be total. **Anything that is not an admission reads as none**: a member but `from`
missing, an admission key, a client key, an issuer or a `from` that is not a digest, a date that
will not parse, a certificate that will not decode or is anything but a bare certificate, or a
certificate whose digest is not the record's `certificate`. A record that does not read admits
nobody, which is the same answer as no record at all, and it is counted among no admissions of a
certificate. A member the table does not name is passed over.

Profile names that could not have been issued as claims are dropped when a record is read, so a
record edited by hand into something strange grants nothing strange.

**An admission is held to its issuer.** The gate judges a certificate against the authority it
serves by the anchor pin, §214, before any record is looked up, and admits it only where its
record's `issuer` is that authority's digest.

**This reverses the record as lines of text under a custom data key**, a field to a line with a
version marker first, and three things about it. Its reader refused a field and a version it did
not know, where a member that no reader names is now passed over: the store's format number is the
only statement of the store's form, `STORE.md` §607. The record was filed under
`KPG.Client.<sha256>` and held no issuer, so which authority had issued a certificate was worked
out by the anchor pin against each authority the database held. And it stayed off the JSON
dependency on purpose, on the ground that a record is read before anything is trusted, which was
worth a reader with no third-party grammar underneath it. That ground went with the store's check:
a record's content is held to a keyed digest under the store's key, every other record of the
store is the same flat object of strings, and a text of lines inside a string would have been a
second grammar for the same fields.

---

## 10. Expiry, renewal and loss §219

Storing the certificate rather than only its digest is what makes any of this possible.

- **The plugin knows every client's expiry** and warns on the Certificate Authority page in the
  last third of each certificate's own life, and never more than thirty days ahead: thirty days for
  a year, ten for a month, a little over two for a week, eight hours for a day. A client proposes
  how long its certificate lasts and the operator picks, §207, and a fixed thirty days warned about
  a week-long one from the moment it was issued, which is a warning that is always there and so says
  nothing. A third is when ACME clients usually renew a ninety-day certificate, thirty days before
  its end, and the cap keeps a year-long certificate warned about thirty days ahead, as every client
  was when every one lasted a year. The page says how many clients that is and the days within which
  the last of them expires. **The authority and its host certificate are not warned about**: issued
  together for a hundred years, §207, neither runs out while anybody is minding the database. This
  reverses ninety days of warning for both, on the page and in the plugin's log.
- **A host certificate no longer runs out under its authority.** It lived five years to the
  authority's ten, and its end was a total outage that happened while nobody was looking, with
  nothing to reissue it but recreating the authority. Issued together, it ends when the authority
  does.
- **Nothing refuses a pairing ahead of the end either.** A database a century old raises the
  dialog like any other, and the issuer refuses to sign from an authority past its end, which
  pairing answers with `500 internal error`, `PAIRING.md` §311. The two refusals made before the
  dialog, for an authority or a host certificate that had run out, went with the warnings, and so
  did the invite button's refusal that matched them.
- **Recreating the authority revokes every client at once.** It asks first, and it states the count.
- **Renewal is re-issuance, approved on screen**: the same name, the profiles the record holds that
  the database still names less any the operator unchecks, a new key, a new certificate and a new
  record. The old record is left for the client to give up, `PAIRING.md` §312, so a lost answer
  costs nothing. This reverses renewal as a thing a client did by pairing again, with an invitation
  the operator had to hand out for a client they had already admitted.
- **Key loss** cannot happen separately from losing the database itself.
- **Key theft** means whoever holds the file. Every minted certificate is still refused until it has
  a record, which is what the record is for.

---

## 11. What is not defended against §220

- **Local code execution as the user.** It reads the host key, a client's private key and any
  token as typed. Conceded throughout.
- **Anyone who can open the database.** They hold the authority. That is the boundary, not a leak.
- **A prompt-injected agent using its own legitimate access.** That is the ACL's job and the audit
  log's.
- **A compromised second channel.** Whoever holds a pairing token may ask to pair. The operator's
  approval is what stands there.
- **Two copies of one database open at once.** They share an authority and a `SniKey` and are
  genuinely indistinguishable, so the plugin refuses to serve and logs how many answered.
- **An export KeePass makes.** Its XML export writes everything the database holds in the clear,
  the authority's key included, and tells no plugin it did; §222 says what such a file is.

---

## 12. What changed, and why §221

Recorded because a reader seeing only this version will not know which parts were nearly wrong.

| Was | Now | Why |
| --- | --- | --- |
| One authority beside the plugin | one per database, inside it | a registry naming databases is attacker suppliable, because a database is a file |
| Passphrase, Argon2, AES-CBC, HMAC | the kdbx's own encryption | the wrapping was cryptography written here for no gain |
| A DPAPI-wrapped portable backup | backing up the file | the DPAPI layer bound it to one account, so it restored nowhere |
| A local authoritative registry | records in the kdbx, checked on every connection | replication returns, and a lost record now fails closed |
| Thumbprint as the admission key | SHA-256 over `RawData` | `Thumbprint` is SHA-1, measured |
| "The chain is built to the authority" | an explicit DER anchor pin | the natural code accepts a foreign authority, measured |
| Pinned by building an `X509Chain` and comparing its last element's DER with the authority's | no chain engine: the anchor is judged an authority signed by itself, the leaf has to name it in its bytes and carry its signature, and both have to be of the one shape this plugin writes | the engine fetched whatever `caIssuers` address a foreign leaf named, 15.0 s measured; Mono's `X509Chain` throws for every EC certificate, `spikes/mono-compatibility`; and a machine could leave the engine unable to judge a chain at all, which the store had to report apart from a foreign host so as not to send the operator to Recreate |
| Hostname verification | the anchor pin plus an explicit EKU check | the SNI rotates and can never be in a SAN |
| `kpg://<uuid>/<profile>` | `kpg://profile/<name>` | the issuing authority already identifies the database |
| Profiles read from the SAN | read from the admission record | a lost record must make a certificate inert, not invisible |
| One host certificate | two, the second memory only | so a locked KeePass answers `503 locked` without holding a privileged key |
| Signing requests accepted | the issuer generates the key and every extension | a request carries extensions, and copying them lets a client choose its own claims |
| No `keyUsage`, SKI, AKI or `pathLen` | all specified, and `notAfter` clamped | |
| The stored form was unspecified | base64 PKCS#12, no password | the database's own encryption is what guards it |
| Everything loaded with `EphemeralKeySet`, on SChannel | still everything, on a managed TLS stack, until the row below took the stored keys away from Windows; an admission record's certificate is still read with it | measured: SChannel cannot use an ephemeral key, so keeping the platform's TLS would have meant writing the host key to the disk |
| Issued through `CertificateRequest`, signing with a key Windows held | issued by Bouncy Castle, signing with the key it reads out of the stored PKCS#12, and Windows handed certificates without their keys | Mono has neither ECDSA nor a `CertificateRequest` a .NET Framework build can reach, `spikes/mono-compatibility`, and no key needs to pass through Windows at all |
| An authority serial of the 8 random bytes `CertificateRequest.CreateSelfSigned` chose | 16, as every leaf's always was | one generator writes all four certificates; an authority written before still reads and issues |
| Names written as PrintableString, and BMPString for a name holding an underscore | UTF8String, with a leaf's issuer copied from its authority's subject as stored | UTF8String holds an underscore, which PrintableString cannot, and RFC 5280 section 4.1.2.4 allows either; copied rather than written again, so an authority written before still chains what it issues |
| A stored key believed because it was stored beside its certificate | believed only when it is the certificate's own | a local identifier anybody can write is all that pairs them, and an authority holding a key not its own issues certificates that chain to nothing |
| A stored pair was to be checked by issuer name | checked with the anchor pin | every authority shares one subject, so names match where bytes do not |
| SAN read loosely | ASN.1 tags, reject rather than repair | measured three ways to smuggle a claim past a renderer |
| A revocation written as an empty record | a revocation key of its own beside the emptied record, only ever added | measured: a KeePass merge never removes a key and never reads a value, while the empty record lost to a dropped time, a slow clock and an import |
| One `KPG.Authority` key for the only authority | one key per authority, filed under its digest, with a retirement beside every one replaced | a recreation written over the old key lost to a later time, so a stale copy could put a replaced authority back |
| One key of the database's custom data per authority, with a retirement key beside it | a record of the plugin's store per authority, with a retirement record beside it, each an entry written once under a check keyed by the store's key | a KeePassXC merge replaces the whole of the custom data with the other copy's, which took the authority away or put a retired authority back, measured; an entry written once arrived in every merge that was run, `STORE.md` §603 |
| Recreating emptied the records | recreating and deleting remove them, and the revocations | the retirement refuses every old certificate, so the emptied records refused nothing more |
| Ten years for the authority and five for its host, each warned about ninety days ahead and refused at pairing once past | a hundred years for both, issued together, warned about and refused at no point | no expiry was asked for, and `X509Certificate2.NotAfter` misreads `99991231235959Z` ahead of UTC, measured; a shorter host life protects nothing, since its key cannot leak without the authority's |
| A client warned about thirty days before its end, whatever its life | in the last third of its life, and thirty days at most | a client may ask for a week, which a fixed thirty days warned about from the day it was issued |
| A client about to expire paired again, with an invitation the operator handed out | it renews, on a dialog like the pairing one with no invitation, and gives the old certificate up itself | an admitted client is already known, so an invitation proved nothing new and cost the operator a step; the old record is left valid so that a lost answer costs nothing |
| The record of an expired certificate kept until somebody removed it, and an emptied one kept for good | both taken away by the plugin's next write of its own, an emptied one with its revocation | a client may ask for a day, and renewing leaves the old record behind until the client gives it up, so kept they were a key for every certificate ever issued; past its end the gate refuses a certificate on its dates, so neither record had anything left to say |
| Clients counted by record, then by the name they paired under | by the client key every record carries, the digest of the client's first certificate | by record a renewal was two clients, and by name two programs that chose one name were one; names need not be unique, and a name is never what is revoked |
| An admission as a custom data key for each certificate, `KPG.Client.<sha256>`, holding lines of text | a record of the plugin's store for each certificate, a member for each field, naming the authority that issued it | a KeePassXC merge replaces the whole of the custom data with the other copy's, which took the records written since away, measured; an entry written once arrived in every merge that was run, `STORE.md` §603 |
| A revocation as a custom data key for each certificate revoked, `KPG.Revoked.<sha256>`, beside an emptied record | a record of the plugin's store that names the client's key, restated by every summary entry and never removed | the same merge took the revocations away; and a renewal made at a gate not yet told of a revocation by certificate escaped it once the copies met, `STORE.md` §615 |
| A client's own `DELETE /gate/certificate` wrote what the operator's revocation writes | a record of a kind of its own that names that single certificate | the reference client gives the old certificate up after every renewal, and a revocation by the client's key would refuse the certificate the renewal had just issued, `STORE.md` §648 |
| The X on a row of the clients page took that row's certificate away | the X on a client's first row revokes the client, with every certificate it holds | a certificate the client renewed to elsewhere, or before the dialog was accepted, stayed admitted |
| Recreating and deleting removed every revocation with the records | they delete the admissions the retired authority issued, in the write that retires it, and no revocation | a revocation names a client key and no authority, and it is what refuses that client wherever a copy still serves an authority that issued to it |
| The record of an expired certificate, and a revocation whose certificate had expired or whose authority was gone, taken away by the plugin's next write of its own | nothing removed for its age: an expired admission stays listed until its certificate is taken away, its client revoked or its authority retired, and a revocation stays for good | no time decides anything in the store, and nothing in a file tells which copies have seen a record; a wrong clock took out a record that setting it right did not bring back. The growth is accepted until a compaction exists, `STORE.md` §643 |
