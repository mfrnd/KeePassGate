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

No third-party cryptography: `CertificateRequest` and `SubjectAlternativeNameBuilder` cover
everything an authority does here.

---

## 4. What the database holds §204

All of it under `KPG` keys in the **database's own** `CustomData`, which is the one dictionary
KeePass stamps key by key and merges the same way. A node's access rule is the exception and lives
on that node, where it belongs and travels. Everything requires KDBX 4, and this requires KDBX 4.1,
because a per-key modification time is a 4.1 field: a database that has been paired with is a 4.1
file. That is a format and not a gate, since an older KeePass opens the file and saves it back
without the times, which is what the revocation paragraph below turns on.

| Key | Contents | Written |
| --- | --- | --- |
| `KPG.Authority` | the authority, the host certificate and the gate key, all three in one document | at creation, and on recreation |
| `KPG.Client.<sha256>` | one admission record per paired client | at every pairing |

**Not the root group, and the group dialog is why.** `GroupForm` copies a group's `CustomData` when
it opens and writes the copy back on OK, so anything written to the root group while an Edit Group
dialog is open is destroyed by that OK, silently. Everything here used to live there. The
database's own dictionary is copied by one dialog only, Database Maintenance, and the plugin
refuses to write while that one is open.

**A node's rule is carried by the node, because nothing smaller carries it.** A group's and an
entry's `CustomData` keeps no time per key, so a merge takes the whole dictionary from one side or
the other and decides by the node's own modification time. A rule rewritten without that time moving
therefore loses every merge it is in: one ordinary synchronize against a copy made before a profile
was deleted hands the profile back. So the write that changes a rule touches the group or entry as
well, which is the one place in this plugin that rule still applies, and the cost is stated rather
than hidden: a purge moves the modification and access time of every entry it rewrites, none of
which the operator edited. A write that says nothing new stays a no-op, so reading a rule is never
an unsaved change.

**One key per client, never one blob, and in the dictionary that merges per key.** Measured on
KeePass 2.61.1: `PwDatabase.CustomData` keeps a modification time for each key and a merge
arbitrates key by key, while a group's dictionary keeps none, so `PwGroup.AssignProperties`
replaces the whole of it from whichever replica's group was touched last. Two hosts pairing
different clients keep both only in the first. A single blob would be last-writer-wins in either,
and would lose pairings silently.

**A revocation is written as an empty value rather than as a removal.** A merge carries a newer
value; it carries nothing at all for a key that went, so a record removed on one copy and still
present in another comes back on the next synchronize, admitting a client the operator believes is
gone. The empty value is the removal in a form the merge can carry. It is read as no record
everywhere, and the mark stays in the file: one string per client ever paired.

**It carries only while the file keeps the time the mark was written at, and other programs decide
that.** The time is the 4.1 field, and a program that saves the file without it leaves a mark with
nothing to be newer with. Measured: KeePassXC 2.7.12 saves a paired file as 4.0 with every time
gone, and so does KeePass 2.47, which opens a 4.1 file rather than refusing it; pykeepass 4.2.0
keeps them. KeePass 2.61.1 then keeps whichever side is stamped, or whichever side already holds
the key, so a synchronize against a copy that still holds the record admits the client again with
everything it had, whichever copy the synchronize runs from. A later save in KeePass puts the 4.1
header back and not the times, so the version says nothing about whether the marks can still
travel. The time is also the revoking host's clock, compared with the pairing host's record, so a
revocation made sooner after the pairing than the two clocks disagree by loses the same way. And
**File > Import** with *Overwrite existing* is not a merge at all: it puts back every record the
imported copy held, revoked ones included, and the authority it held. The plugin sees none of this,
since KeePass keeps the times behind an internal accessor and has no hook for an import, and cannot
stamp a mark again, since writing the value already there is not a change. What it can do is revoke
a record that came back, which writes a new mark: a record that returns is listed on the clients
page as a client again.

**The authority's three parts are one key, and that is the same argument the other way.** A merge
that took them separately could take the certificate from one replica and the private key from
another, and the host certificate would no longer chain to the authority beside it: the store reads
that as damaged, the gate serves nothing, and the only remedy revokes every client. Inside one key
there is nothing to take apart, so whichever replica wins contributes all three. `AuthorityDocument`
is the shape, one flat object of strings, which is what `JsonBody` already reads.

### How each value is written §222

A certificate and its key are stored as **base64 of a PKCS#12 with no password**. No password
because there is nothing for one to protect: the database's own encryption is what guards this, and
a passphrase here would be cryptography written by us over cryptography that already works. The gate
key is base64 of the 32 raw bytes. Sizes are unremarkable: roughly 1.3 KB of base64 for the
authority.

**They are loaded with `EphemeralKeySet`, and that is load bearing.** Measured in
`EphemeralKeyProbe.cs`: importing a PKCS#12 without it writes a key file into
`%APPDATA%\Microsoft\Crypto\Keys` for as long as the certificate is loaded, which would put the
authority's private key on a disk outside the database that is meant to be the only place it lives.
With the flag nothing touches the disk.

**It does not follow that SChannel will serve with one, and an earlier draft of this document
said it would.** Measured afterwards: an `EphemeralKeySet` certificate signs, and SChannel
refuses TLS with it in either direction, a server getting `SEC_E_NO_CREDENTIALS` and a client
never getting its certificate onto the wire. That is what put `Org.BouncyCastle.Tls` under the
transport, which does serve with them, so no key this plugin loads reaches the disk.
`FINDINGS.md` records the measurement.

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
| Authority | 10 years | CA true, **pathLen 0** | keyCertSign, cRLSign | **none** |
| Host, normal | 5 years | CA false | digitalSignature, keyAgreement | serverAuth |
| Host, responder | short, re-minted each unlock | CA false | digitalSignature | serverAuth, plus a KPG marker |
| Client | 1 year | CA false | digitalSignature | clientAuth |

Every certificate carries a **subject key identifier** and every leaf an **authority key
identifier**, so the authority can be renewed later without invalidating what it signed. **`pathLen
0`** so it cannot mint a subordinate. **No EKU on the authority**, because one there constrains
everything beneath it and buys nothing. **A leaf's `notAfter` is clamped to the authority's**, so
nothing outlives what vouches for it.

### Why there are two host certificates §208

A locked KeePass has closed its database, so the authority, the admission records and the normal
host key are gone. Without help there is nothing to complete a handshake with, and a client gets a
bare connection failure indistinguishable from a wrong port, a stopped KeePass, or an attack.

The **responder certificate** is minted at unlock and held only in plugin memory. While locked it is
the only identity available and it can do nothing but answer "locked".

- **The normal host key must be actively dropped at lock**, or two certificates buy nothing: memory
  disclosure while locked would otherwise yield the full host identity.
- **The responder must be distinguishable by what it carries**, not by convention, so a client can
  refuse to send real requests over it. It is a `kpg://host/responder` URI in the subject
  alternative name rather than an extension of its own: the UUID arc `2.25` does not survive
  being read back on .NET Framework, and a private arc would be squatting on somebody else's
  number. An impostor holding the responder key reads nothing, but
  could answer with a fabricated secret, and an agent acting on a forged credential is worse than an
  agent that fails.
- **The gate key must stay resident too**, or the gate never resolves and the responder is never
  reached. Of the three secrets it is the right one to keep: it authenticates nothing, and every
  paired client already holds it.

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

- **Never read the extension with `X509Extension.Format`, and never use `GetNameInfo`.** Both were
  measured against a hostile SAN and both fail. Parse the ASN.1 and take only entries tagged
  `[6] uniformResourceIdentifier`.
- **Reject a claim containing a percent escape.** A newline inside a URI path survives issuance as
  `%0A`.
- **Reject rather than repair.** Anything that is not exactly `kpg://profile/<name>` is dropped.
  Empty names, and the ACL's own `default`, are never accepted from a certificate.

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
approved for, the rules say what those profiles reach, and the router allows the intersection.
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
3. **A certificate is always asked for**, decided in the gate's own TLS server rather than by an
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
if (!FixedTimeEquals(root, resolved.AuthorityDer)) return false;   // the load-bearing line
```

With the pin: our client `True`, foreign client `False`.

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
record is refused. Revoking is deleting the record, which takes effect on the next connection,
offline and immediately.

**The key is SHA-256 over the certificate's `RawData`**, compared in fixed time, stored lowercase
hex. `X509Certificate2.Thumbprint` is **SHA-1**, measured at 40 hex characters, and a design that
says SHA-256 throughout should not key its allowlist on it.

Each record holds that key, the certificate itself so expiry is knowable, the subject as issued, the
**approved profiles**, and the issuance date.

---

## 10. Expiry, renewal and loss §219

Storing the certificate rather than only its digest is what makes any of this possible.

- **The plugin knows every expiry** and warns on the Certificate Authority page: 30 days for a client, 90
  for a host or the authority.
- **A host certificate expiring is a total outage**, and it happens while nobody is looking.
  Reissuing would be cheap and clients would never notice, because they trust the authority rather
  than the leaf. **Not built:** there is no reissue dialog, and recreating the authority is what
  replaces a host certificate today, at the cost of revoking every client.
- **Recreating the authority revokes every client at once.** It asks first, and it states the count.
- **Renewal would be re-issuance**: same profiles, new key, new certificate, new record, old record
  removed. **Not built:** a client that is about to expire pairs again.
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
| The stored form was unspecified | base64 PKCS#12, no password, loaded with `EphemeralKeySet` | without the flag the key is written to the user's profile, which contradicts "inside the database and nowhere else" |
| A stored pair was to be checked by issuer name | checked with the anchor pin | every authority shares one subject, so names match where bytes do not |
| SAN read loosely | ASN.1 tags, reject rather than repair | measured three ways to smuggle a claim past a renderer |
