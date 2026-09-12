# The certificate authority

<!-- The recorded probe output below smuggles a newline as `%0A`, which runs into the
     `URL=` after it. Quoted verbatim, so the word is ignored here rather than corrected. -->
<!-- cspell:ignore AURL -->

Reference for how KeePassGate issues, presents, validates and withdraws identity.
[`DESIGN.md`](DESIGN.md) says how it fits the rest, [`PAIRING.md`](PAIRING.md) covers the flow that
gets a stranger admitted, and `spikes/tls-and-certificates` holds the code behind every measurement
quoted here.

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
| Authority certificate | about 384 bytes |
| Leaf certificate | 393 bytes |
| Serial | 16 random bytes, high bit cleared so the INTEGER encodes positive |

Issuing takes no third-party cryptography: `CertificateRequest` and `SubjectAlternativeNameBuilder`
mint every certificate here. The rest of the cryptography is Bouncy Castle's: it terminates the
TLS, `DESIGN.md` §133, and it is the fixed-time comparison every certificate's bytes go through,
§214, which .NET Framework does not have.

---

## 4. What the database holds §204

All of it under `KPG` keys in the **database's own** `CustomData`, which is the one dictionary
KeePass stamps key by key and merges the same way. A node's access rule is the exception and lives
on that node, where it belongs and travels. Everything requires KDBX 4, and this requires KDBX 4.1,
because a per-key modification time is a 4.1 field: a database that has been paired with is a 4.1
file. That is a format and not a gate, since an older KeePass opens the file and saves it back
without the times. Nothing that has to survive a merge turns on them any more, which is what the
revocation paragraph below is about.

| Key | Contents | Written |
| --- | --- | --- |
| `KPG.Authority.<sha256>` | one authority, filed under the digest of its certificate: the authority, the host certificate and the gate key, all three in one document | once, at creation and on recreation, and never rewritten |
| `KPG.RetiredAuthority.<sha256>` | the constant `retired`, for an authority recreated, deleted or not kept | once, and never changed or removed |
| `KPG.Client.<sha256>` | one admission record per paired client | at every pairing, and written empty when the client is revoked |
| `KPG.Revoked.<sha256>` | the revoked certificate's `notAfter` | once, when the client is revoked, and never changed; removed once that moment has passed |
| `KPG.Profiles` | the set of profile names this database knows | when the Profiles page is accepted, and at a pairing that approves a name it does not hold, in the same write as the record |

**Not the root group, and the group dialog is why.** `GroupForm` copies a group's `CustomData` when
it opens and writes the copy back on OK, so anything written to the root group while an Edit Group
dialog is open is destroyed by that OK, silently. Everything here used to live there. The
database's own dictionary is copied by one dialog only, Database Maintenance, and the plugin
refuses to write while that one is open.

**A node's rule is carried by the node, because nothing smaller carries it.** A group's and an
entry's `CustomData` keeps no time per key, so a merge takes the whole dictionary from one side or
the other and decides by the node's own modification time. A rule is written only into the copy a
group or entry dialog puts back on OK, so it travels exactly as far as any other edit made in that
dialog, and the plugin writes no rule of its own. A merge that hands back a rule for a profile the
operator has since removed hands back something inert, because the database's list of profiles
decides which names are in force (`DESIGN.md` §139); that replaced a purge which rewrote every rule
naming a removed profile, and moved the times of entries nobody had edited to make the rewrite
carry. A write that says nothing new stays a no-op, so reading a rule is never an unsaved change.

**One key per client, never one blob, and in the dictionary that merges per key.** Measured on
KeePass 2.61.1: `PwDatabase.CustomData` keeps a modification time for each key and a merge
arbitrates key by key, while a group's dictionary keeps none, so `PwGroup.AssignProperties`
replaces the whole of it from whichever replica's group was touched last. Two hosts pairing
different clients keep both only in the first. A single blob would be last-writer-wins in either,
and would lose pairings silently.

**The profile list is one key, so a merge takes it whole.** `KPG.Profiles` holds every name in one
value, and a merge keeps one side's list, which for a synchronize is whichever copy wrote it last: a
name only the other copy added is lost, and a name only the other copy took out comes back. Two hosts
that each approve a pairing for a name the database has never named therefore keep both records and
one list. The list decides which names are in force (`DESIGN.md` §139), so a lost name grants
nothing until it is added again, and the clients page shows it as removed on every client holding
it, while a name that came back is in force again, its rules and its clients with it, as though it
had been added on the Profiles page without the question that page asks first. After merging two
copies that both changed their profiles, the Profiles page is where to look.

**A revocation is a key of its own, only ever added and never changed.** Revoking adds
`KPG.Revoked.<sha256>` beside the record, holding the certificate's `notAfter`, and writes the
record empty, and a client is admitted only by a record that is not empty and has no revocation
beside it. What makes that hold was measured on KeePass 2.61.1, and the host tests pin it against
the KeePass this builds with: `PwDatabase.MergeInDbProperties` visits the source's keys and no
others, so **a KeePass merge never removes a key the target holds**, and it decides a key both
sides hold by modification time, the merge method and `SettingsChanged`, so **it never reads a
value**. A key that is added once and never changed survives every KeePass merge and import,
whatever the clocks say and whether or not a program in between dropped the times.

**This reverses the empty record as the revocation**, and the argument for it is kept because it was
right about the merge and wrong about what the merge would be given. A merge carries a newer value
and carries nothing at all for a key that went, so the empty value was the removal in a form the
merge could carry. It carried only while the file kept the time the mark was written at, and other
programs decide that: KeePassXC 2.7.12 saves a paired file as 4.0 with every time gone, and so does
KeePass 2.47, which opens a 4.1 file rather than refusing it, after which KeePass 2.61.1 keeps
whichever side is stamped or already holds the key. The time was also the revoking host's clock,
compared with the pairing host's record, so a revocation made sooner after the pairing than the two
clocks disagree by lost the same way. And **File > Import** with *Overwrite existing* put back every
record the imported copy held. In each of those the client was admitted again with everything it
had, and the plugin, which cannot see the times or an import, could only revoke it again. The
emptied record is still written, and is still that losing kind of mark: a stale copy can bring it
back, and the revocation beside it is what refuses it when it does.

**A record that comes back is taken away by the plugin's next write.** It admits nobody meanwhile,
since admission reads the revocation rather than waiting for this, and the clients page counts it
under the list rather than listing it as a client. The write it rides on is one this plugin makes
anyway: a revocation, a pairing, keeping one authority, recreating or deleting it. Opening or
reading a database changes nothing in it.

**A revocation goes once its certificate has expired**, during the next revocation, because past
`notAfter` the gate refuses that certificate on its dates and the key has nothing left to refuse. A
stale copy that brings a pruned one back does no harm, and it is pruned again. A record that cannot
be read gives no date, and its revocation is written empty and kept.

**Each authority is one key, and that is the same argument the other way.** A merge that took its
parts separately could take the certificate from one replica and the private key from another, and
the host certificate would no longer chain to the authority beside it: the store reads that as
damaged, the gate serves nothing, and the only remedy revokes every client. Inside one key there is
nothing to take apart. `AuthorityDocument` is the shape, one flat object of strings, which is what
`JsonBody` already reads.

**One key per authority, filed under its digest, rather than one key for the only authority**, and
that reverses the single `KPG.Authority` key for the reason the revocations moved: a recreation
written over the old key lost to any copy whose time for that key was later, so a stale copy merged
in could put back the authority the operator had replaced. A document is now written once and never
rewritten in place. What replaces it is a retirement, `KPG.RetiredAuthority.<sha256>`, added beside
it and never changed or removed: recreating retires the old authority, deletes its document and
writes the new one, and deleting retires it and deletes its document. Anything that later wants to
renew a part, the host certificate say, needs a key of its own that is also written once. The single
key is not read or migrated: a database that holds one reads as having no authority, and is given a
new one and paired again.

**Which authority a database has is decided by the retirements, never by counting documents.** The
authorities are the documents with no retirement beside them, so a retired document that a merge
brings back serves nothing, and the plugin's next write takes it away again. **More than one
authority with no retirement serves nothing**: that is two copies that each created or recreated
one, meeting in a synchronize, and choosing between them is the operator's decision rather than
this plugin's.

**So the plugin asks, and answers `503 undecided` until somebody has.** A connection to such a
database is answered before a request is read, with each authority's own host certificate and no
responder. KeePass asks the operator which authority to keep in a dialog over its main window, the
first time the plugin sees the state: when the database is opened or unlocked, or at the save that
ends a synchronize, and never from a connection. For each authority it shows the fingerprint, when
it was made, and every client chaining to it with its profiles, when it was admitted and when it
last made a request where the audit says so.
Keeping one retires every other, through the single writer's guard, and marks the database
modified without saving it; the records of the authorities not kept stay, admitting nobody, and
the clients page names them for what they are. Cancel serves nothing, and the KPG tab's authority
page asks again, waiting for the settings dialog's OK like everything else there. **A choice the
writer refuses is tried again for a few seconds** before the operator is told to choose again,
which is the operator's ruling of 2026-10-14: another client's write holding the writer, or KeePass
part way through a save, is over in a moment, and only a refusal that outlasts that, Database
Maintenance left open among them, is put back in front of the operator.

**Recreating and deleting both remove every admission record, and every revocation with the records
it emptied.** Each certificate they name chains to the authority just retired, and the retirement
refuses it whatever a stale copy brings back, so a revocation beside it would refuse nothing more.
This reverses recreation emptying the records rather than removing them, which it did while the
replacement went under the same key and an emptied record was the only mark a merge with an older
copy would carry.

**All of this is a claim about KeePass 2.48 or later with the plugin in it.** Other programs can open
the database, and saving or merging in one can quietly bring back a revoked client, a retired
authority or a removed profile, because their merges replace or drop keys: KeePassXC 2.7.12, the
one read from source, replaces the whole dictionary. Nothing here detects that. After any change
made outside KeePass, the clients page and the authority are where to look.

**KeePass's own Database Maintenance dialog lists every one of these keys on its Plugin Data tab,
and deletes any of them on request.** None of that is a way to manage what this plugin keeps.
Deleting an emptied record there changes nothing, since the revocation beside it still refuses the
certificate. Deleting a revocation leaves the client refused only by its emptied record, which a
stale copy merged in afterwards can beat, the way a dropped time, a slow clock or an import beat it
before revocations had keys of their own. Deleting a retirement lets a stale copy's old authority
back in beside the one in use, and the save that ends that synchronize asks the operator to choose
between them again. Revoking, recreating and deleting are done on the KPG tab, which writes each of
them the way this section says.

### How each value is written §222

A certificate and its key are stored as **base64 of a PKCS#12 with no password**. No password
because there is nothing for one to protect: the database's own encryption is what guards this, and
a passphrase here would be cryptography written by us over cryptography that already works. The gate
key is base64 of the 32 raw bytes. Sizes are unremarkable: roughly 1.3 KB of base64 for the
authority.

**Nothing here is ever written to the disk, and that is what decided the transport.** Importing a
PKCS#12 on .NET Framework writes a key file into `%APPDATA%\Microsoft\Crypto\Keys` for as long as
the certificate is loaded, unless `EphemeralKeySet` is asked for. Everything the plugin loads asks
for it.

That is only possible because the TLS server is managed code. An ephemeral key **signs perfectly
well and cannot be used with SChannel at all**, in either direction, so serving with the platform's
own TLS would have meant putting the host key on the disk for as long as a database was open.
`DESIGN.md` §133 records the reversal; `EphemeralKeyProbe.cs` and `ManagedTlsProbe.cs` are the two
halves of the measurement.

So all three secrets live in the database and in memory, and nowhere else the plugin puts them: the
authority that can mint identities, the host key that authenticates a server, and the gate key.

**KeePass can put them somewhere else.** Its own File, Export, KeePass XML (2.x) writes the
database's custom data in the clear with everything else, so an exported file holds the authority's
private key, the host key, the gate key and every admission record, and KeePass announces an export
to no plugin. Such a file is the unlocked database: it is kept as one, and if it leaves the
operator's hands the answer is recreating the authority, which [`PAIRING.md`](PAIRING.md) §301
names as the recovery from a leaked authority key.

**Reading is total and fails closed.** Anything that will not decode leaves the authority *damaged*
rather than absent, and the two are never conflated: an absent authority is created, a damaged one
has to be recreated, and only the operator can decide to do that. A host certificate that does not
chain to the stored authority is damage too, checked with the anchor pin rather than by comparing
names, because every authority this plugin issues carries the same subject and a name comparison
therefore accepts somebody else's host.

**Pairing tokens are not here.** They live in plugin memory and expire on TTL or on KeePass exiting,
whichever comes first. [`PAIRING.md`](PAIRING.md) says why that is more than a simplification.

### The trap this store carries §206

`GroupForm` **takes a copy of a group's `CustomData` when it opens and writes that copy back on
OK**. A value written to the live group therefore survives Cancel and is destroyed by OK, silently.

Measured rather than remembered, in `SnapshotHazardTests`: the snapshot field is still there in
KeePass 2.61.1, a group's custom data can be replaced wholesale from inside KeePass but not from a
plugin, a write made while the dialog is open is destroyed by OK and survives Cancel, and an
authority created before the dialog opened comes back unharmed because the dialog copied it.

**The dialog this plugin puts its tab on carries no such snapshot**, which the same tests assert
against the real type. So the buttons on the KPG tab are safe from their own dialog, and the hazard
belongs to whatever writes while somebody has a *group* open. Nothing this store holds is in a
group, since §204 keeps all of it in the database's own dictionary, so what a group dialog can
destroy is a node's rule. The dialog that copies this store is Database Maintenance, which writes
the database's dictionary back the same way, and [`PAIRING.md`](PAIRING.md) §307 is where pairing
answers that one.

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
     +-- client certificate            clientAuth               issued at pairing
```

| | Life | `basicConstraints` | `keyUsage` (critical) | EKU |
| --- | --- | --- | --- | --- |
| Authority | 100 years | CA true, **pathLen 0** | keyCertSign, cRLSign | **none** |
| Host, normal | as long as the authority, issued with it | CA false | digitalSignature, keyAgreement | serverAuth |
| Host, responder | 90 days, re-minted at every unlock and at every lock | CA false | digitalSignature | serverAuth, plus a KPG marker |
| Client | 1 year | CA false | digitalSignature | clientAuth |

Every certificate carries a **subject key identifier** and every leaf an **authority key
identifier**, so the authority can be renewed later without invalidating what it signed. **`pathLen
0`** so it cannot mint a subordinate. **No EKU on the authority**, because one there constrains
everything beneath it and buys nothing. **A leaf's `notAfter` is clamped to the authority's**, so
nothing outlives what vouches for it.

**A hundred years for the authority and its host certificate, and nothing counts down to it.** What
was asked for was no expiry at all, RFC 5280's `99991231235959Z`, with a hundred years if that
turned out not to be fully supported, and it is not: `CertificateRequest` writes the end of year
9999 as the RFC asks, and `X509Certificate2.NotAfter` reads it back as an earlier moment in any time
zone ahead of UTC, without saying so. A hundred years is read exactly everywhere it is read,
`LongLivedCertificateTests`. The two are issued together and end together, because a host
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
  exact about, because the phrase promises more than the platform gives. Two of the three parts
  really are erased: the gate key and the stored PKCS#12 the host certificate and its key travel in
  are overwritten, and the certificate handles are released. The key the TLS server signs with is
  neither of those. It is a BouncyCastle `AsymmetricKeyParameter` over an immutable `BigInteger`,
  which offers nothing to overwrite, so what happens to it is that the last reference goes and the
  collector reuses the memory when it gets to it. Read as erasure that would be a claim this design
  cannot keep. It is the bound §220 concedes anyway: a dump taken at the wrong moment finds it, and
  what the drop takes away is the key being reachable for as long as the database stays locked.
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

A KeePass that has never been unlocked since starting has no responder, so that case really is a
bare connection failure.

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
  is not one to sixty-four characters of letters, digits, dash and underscore, and refuses the
  ACL's own `default` whatever case it is written in, so a claim carrying a percent escape, an
  empty name or a smuggled newline is one this authority will not mint. Reading an admission record
  drops a profile that could not have been issued as a claim, §223, so a record edited by hand
  grants nothing strange either.

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
4. **The anchor is pinned** against the resolved database's authority. See below.
5. **The EKU is checked explicitly**: `clientAuth` for a client, `serverAuth` for a host.
6. **Validity dates**, with five minutes of skew allowed on `notBefore` only.
7. **The admission record is looked up** by SHA-256 over the certificate DER. Absent means refused.
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

```csharp
if (!chain.Build(presented)) return false;
if (chain.ChainElements.Count != 2) return false;               // no intermediates in this hierarchy
byte[] root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData;
if (!FixedTime.Equals(root, anchor.RawData)) return false;      // the load-bearing line
```

With the pin: our client `True`, foreign client `False`.

**The load-bearing part is the comparison, not its timing.** `FixedTime.Equals` is what does it,
because every comparison of certificate bytes in this plugin goes through that one door and one
that did not would be a question a reader has to answer again. On its own it defends nothing here:
an anchor is a public certificate, and every client that can reach this code was handed a copy of
it at pairing. What is load bearing is that the anchor's own bytes are compared at all, which is
the line the platform will not write.

**And the engine goes out on the way to `False`, which nothing here asked it to do.** When it cannot
find the issuer it reads the leaf's authority information access extension, RFC 5280 section
4.2.2.1, and downloads whatever that names before `Build` returns. The leaf belongs to whoever
opened the connection, so the address does too: measured, one address that accepts and answers
nothing holds the connection thread for 15.0 s, and the request really is issued by the KeePass
process. Nothing worth fetching exists here. The anchor is handed in through `ExtraStore` and the
hierarchy is a leaf and the certificate above it, so a download can only ever be the engine looking
for an issuer the pin is about to refuse.

`X509ChainPolicy.DisableCertificateDownloads` says exactly that and is .NET 5 and later, like
`CustomRootTrust` above. What this framework has is `UrlRetrievalTimeout`, where zero is the default
the policy already carries and means fifteen seconds, so the pin sets the smallest positive value
and the same leaf is refused in 79 ms. A millisecond is still a millisecond: the request may be
opened before it is abandoned, so what the bound takes away is the wait and the answer rather than
the first packet. §217 declines to depend on the network at all, and this is that network arriving
through a door nobody opened, closed here as far as the platform allows.

`SslPolicyErrors.None` is the **wrong** test. The authority is deliberately in no machine store, so
a correct deployment always reports `RemoteCertificateChainErrors`, and since the SNI rotates it
always reports `RemoteCertificateNameMismatch` too. The callback ignores `SslPolicyErrors` entirely
and reaches its own verdict.

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
record is refused, and so is one with a revocation beside it. Revoking adds that revocation and
empties the record, §204, which takes effect on the next connection,
offline and immediately: the record is read from the live database per connection and never cached
beside the certificates, so nothing has to be reloaded for a revocation to bite.

[`docs/clients.md`](docs/clients.md) is the same thing said to whoever presses the button.

**The operator revokes from the KPG tab's clients page**, which lists every client the database has
admitted with what each one may reach, and takes one away. It is a list rather than a bare set of
names because revoking is a decision about access, and a name on its own does not say what is being
taken. It sits under the widgets that hand an invitation out, on the same page, because a list of
clients is what an invitation turns into and the operator letting somebody in is the one who should
see who is already in. It writes the way the rest of that tab writes: the row goes at once, the
revocation lands when the dialog is accepted, and saving is what keeps it. Cancel discards it.

Recreating the authority is the other way, and it is the blunt one: it revokes every client at once
and each has to pair again. That is a recovery from a compromised authority, not a way to remove one
client.

**The key is SHA-256 over the certificate's `RawData`**, compared in fixed time, stored lowercase
hex. `X509Certificate2.Thumbprint` is **SHA-1**, measured at 40 hex characters, and a design that
says SHA-256 throughout should not key its allowlist on it.

Each record holds that key, the certificate itself so expiry is knowable, the subject as issued, the
**approved profiles**, and the issuance date.

### The stored form §223

One field per line, name and value either side of the first equals sign, with a version marker
first:

```text
v=1
subject=CN=agent-fictitious
admitted=2026-08-15T09:14:22Z
profiles=ops,readonly
certificate=<base64 DER>
```

Small and strict rather than general, and that is a decision rather than laziness. Everything in a
record has already been through issuance, which refuses a subject or a profile that is not a plain
name, so a reader can be total in eighty lines. **Anything that is not exactly a record is refused**:
a missing field, a repeated one, an unknown version, a date that will not parse, a certificate that
will not decode, or a certificate whose digest is not the key the record is filed under. A record
that cannot be read admits nobody, which is the same answer as no record at all.

Profile names that could not have been issued as claims are dropped when a record is read, so a
record edited by hand into something strange grants nothing strange.

A JSON dependency has since arrived for the access control document, so this could move to it, and
deliberately has not: a record is read before anything is trusted, and that is worth a reader with no
third-party grammar underneath it.

---

## 10. Expiry, renewal and loss §219

Storing the certificate rather than only its digest is what makes any of this possible.

- **The plugin knows every client's expiry** and warns on the Certificate Authority page in the
  last third of each certificate's own life, and never more than thirty days ahead: thirty days for
  a year, ten for a month, a little over two for a week, eight hours for a day. A fixed thirty days
  would warn about a week-long certificate from the moment it was issued, which is a warning that
  is always there and so says nothing. A third is when ACME clients usually renew a ninety-day
  certificate, thirty days before its end, and the cap keeps the year every client is issued for
  warned about thirty days ahead, as it was while the window was fixed. The page says how many
  clients that is and the days within which the last of them expires. **The authority and its host
  certificate are not warned about**: issued together for a hundred years, §207, neither runs out
  while anybody is minding the database. This reverses ninety days of warning for both, on the page
  and in the plugin's log.
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
- **Renewal would be re-issuance**: same profiles, new key, new certificate, new record, old record
  removed. **Not built:** a client that is about to expire pairs again, which is what
  [`docs/clients.md`](docs/clients.md) tells an operator to do.
- **Key loss** cannot happen separately from losing the database itself.
- **Key theft** means whoever holds the file. Every minted certificate is still refused until it has
  a record, which is what the record is for.

---

## 11. What is not defended against §220

- **Local code execution as the user.** It reads the host key, the client key and any token as
  typed. Conceded throughout.
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
| Hostname verification | the anchor pin plus an explicit EKU check | the SNI rotates and can never be in a SAN |
| `kpg://<uuid>/<profile>` | `kpg://profile/<name>` | the issuing authority already identifies the database |
| Profiles read from the SAN | read from the admission record | a lost record must make a certificate inert, not invisible |
| One host certificate | two, the second memory only | so a locked KeePass answers `503 locked` without holding a privileged key |
| Signing requests accepted | the issuer generates the key and every extension | a request carries extensions, and copying them lets a client choose its own claims |
| No `keyUsage`, SKI, AKI or `pathLen` | all specified, and `notAfter` clamped | |
| The stored form was unspecified | base64 PKCS#12, no password | the database's own encryption is what guards it |
| Everything loaded with `EphemeralKeySet`, on SChannel | still everything, on a managed TLS stack | measured: SChannel cannot use an ephemeral key, so keeping the platform's TLS would have meant writing the host key to the disk |
| A stored pair was to be checked by issuer name | checked with the anchor pin | every authority shares one subject, so names match where bytes do not |
| SAN read loosely | ASN.1 tags, reject rather than repair | measured three ways to smuggle a claim past a renderer |
| A revocation written as an empty record | a revocation key of its own beside the emptied record, only ever added | measured: a KeePass merge never removes a key and never reads a value, while the empty record lost to a dropped time, a slow clock and an import |
| One `KPG.Authority` key for the only authority | one key per authority, filed under its digest, with a retirement beside every one replaced | a recreation written over the old key lost to a later time, so a stale copy could put a replaced authority back |
| Recreating emptied the records | recreating and deleting remove them, and the revocations | the retirement refuses every old certificate, so the emptied records refused nothing more |
| Ten years for the authority and five for its host, each warned about ninety days ahead and refused at pairing once past | a hundred years for both, issued together, warned about and refused at no point | no expiry was asked for, and `X509Certificate2.NotAfter` misreads `99991231235959Z` ahead of UTC, measured; a shorter host life protects nothing, since its key cannot leak without the authority's |
| A client warned about thirty days before its end, whatever its life | in the last third of its life, and thirty days at most | a fixed thirty days would warn about a week-long certificate from the day it was issued |
