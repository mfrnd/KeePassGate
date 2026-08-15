# CLAUDE.md

Guidance for anyone, human or AI assistant, working in this repository.

## What this is

**A KeePass 2.x plugin, written from scratch**, serving a small specification-defined HTTP API over
mutual TLS, so that automation and AI agents can each be given a narrow, revocable, audited slice of
a database.

**Status: the design is settled, steps 1 to 6 of the build order are done, and step 7, validation,
is under way.** The plugin loads into KeePass, puts a KPG tab on the database settings dialog, that
tab creates, recreates and deletes the database's own certificate authority, and the gate listens on
loopback and answers one route over mutual TLS 1.3. The anchor pin and the extended key usage check
were already in; what step 7 adds is the admission record and the validity window.

## Read these before proposing anything

| | |
| --- | --- |
| [`DESIGN.md`](DESIGN.md) | what to build, and a section attacking it. **The contract.** |
| [`CA.md`](CA.md) | the certificate authority: issuance, claims, validation, revocation |
| [`PAIRING.md`](PAIRING.md) | how a stranger becomes a client, with the flow diagrams |
| [`FINDINGS.md`](FINDINGS.md) | what was measured rather than assumed, and what turned out wrong |
| [`TESTING.md`](TESTING.md) | the three test tiers, what each one buys, and how to run them |

**One document is historical and says so at the top.** [`ANALYSIS.md`](ANALYSIS.md) is the argument
that was made, and several of its conclusions were later reversed; it opens with a banner naming
them. **Do not build from it.** It is kept because the reasoning is worth having and because a
reversal without its argument is just an assertion.

**The code those two measured is not on any mainline branch.** `spikes/http-lib-measurements`, taken
from `master` and never merged, keeps the three vendored HTTP proofs and gathers what they settled
in its own `RESULTS.md`. A spike lives on its own branch so that no mainline history has to carry it
being added and deleted again.

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
  straight. The authority's three parts are **one key**, `AuthorityDocument`, because there the
  argument runs the other way: merged apart, a certificate from one replica and a private key from
  another read as damage. And **a revocation is written empty rather than removed**, since a merge
  carries a newer value and carries nothing for a key that went. See CA.md §204.
- **The authority takes the file to KDBX 4.1**, because a per-key modification time is a 4.1
  field. A database saved with an authority in it is a 4.1 file before anything has paired with
  it, and a record keeps it there. That is a format and not a gate: KeePass 2.47
  opens it and saves it back as 4.0 without the per-key times a revocation needs to travel, and so
  does KeePassXC. See CA.md §204.
- **The admission record is load bearing.** A certificate that chains, passes its EKU check and has
  no record in the database is refused, and the permitted profiles are read from the **record**, not
  from the certificate's SAN. This is not belt and braces: it is what makes a lost record fail
  closed. See the `GroupForm` trap below.
- **Claims carry profile names only**: `kpg://profile/<name>`. No database identifier, because the
  issuing authority already identifies it. **There is no `kpg://any` wildcard**, and re-proposing one
  reopens a measured escalation.
- **The authority key has no passphrase.** It is protected by the kdbx encryption. An earlier draft
  wrapped it in Argon2 over AES-CBC with an encrypt-then-MAC construction and DPAPI; all deleted, and
  its backup story was broken anyway because a DPAPI blob restores nowhere.
- **Pairing tokens live in plugin memory only**, expiring on TTL or KeePass exit, whichever first.
  They are never written to the database.
- **Two host certificates per database**: a normal one in the kdbx, and a short-lived responder
  minted at unlock and held in memory so a locked KeePass can answer `503 locked` rather than failing at
  the transport. The normal host key is actively dropped at lock. TODO(step 9): the responder,
  the locked answer and dropping the key at lock arrive with step 9.
- **We own the transport.** Accept loop, ClientHello peek, TLS termination, request parser, body
  read, response write. Selecting a host certificate by SNI makes this unavoidable, because
  .NET Framework exposes no server-side SNI hook. Which TLS stack terminates it is the bullet
  below.
- **No third-party HTTP dependency.** `uHttpSharp` fits the seam natively with zero changes and is
  ruled out on **license** (LGPL-2.1) and liveness (no commits since 2021), not on fit. Wanting
  WebSocket, HTTP/2 or chunked bodies flips this; nothing else does.
- **The TLS is Bouncy Castle, not SChannel**, and that reverses an earlier decision on a
  measurement: SChannel will not serve with a key that has not been written to the user's profile,
  so the platform's own TLS meant a host key on the disk. `Org.BouncyCastle.Tls` is MIT, alive, and
  parses the stored PKCS#12 itself. Do not re-propose `SslStream` for the server without measuring
  `EphemeralKeyProbe.cs` on `spikes/tls-and-certificates` again first.
- **Spec-first routing.** The OpenAPI document is the routing table and handlers bind to
  `operationId`, so an undeclared path has nowhere to go.
- **The JSON grammar is `Newtonsoft.Json`'s and the policy is ours**, which is the split worth
  keeping. `JsonBody` decides what this wire may carry, which is one flat object of strings and
  arrays of strings, and parses none of it. Three alternatives were measured and refused, on
  assembly count and on license. The counts are on `spikes/json-reading`, and re-proposing one means
  arguing with them.
- **A plain DLL on .NET Framework 4.8**, not a `.plgx`.
- **The access control model is one document per node**, and its types arrive with step 12.
  Grants live under a `KPG` key, which is this plugin's own namespace and touches nothing another
  plugin owns.

  **There is no per-subject opt-in, and adding one would be a defect.** The ACL guards every route
  there is, because there is one API generation and no legacy to exempt. A setting that let a
  subject out of the ACL could only ever be wrong here, however sensible it looks in a design that
  has something older to carry.

## Measured facts that cost time if rediscovered

Every one of these was established by running code, and most contradict what the documentation
suggests. The code is on branches of its own: `spikes/tls-and-certificates` holds the nine that
measured TLS and X.509, and `spikes/build-environment` the one that measured whether the gate can
fail while nothing is wrong. Each gathers what it settled in its own `RESULTS.md`. Run them from
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
- **Never parse a SAN with `X509Extension.Format` or `GetNameInfo`.** `AddDnsName` accepts text that
  renders as a URI claim, the labels are localized, and `GetNameInfo` returns one entry. Read the
  ASN.1 tags: `0x86` URI, `0x82` dNSName, `0x81` rfc822Name. `SanProbe`.
- **There is no RFC 5705 exporter** on .NET Framework, but
  `TransportContext.GetChannelBinding(ChannelBindingKind.Endpoint)` works on both sides under TLS 1.3
  and both agree. Take the application-data portion by its documented offsets, not the whole blob.
  `ChannelBindingKind.Unique` returns something under TLS 1.3 where tls-unique is undefined; ignore
  it. `BindingProbe`.
- **The SNI needs two labels.** 64 hex characters exceed the 63-octet DNS label limit.
- **C# 7.3 is available, and the packaging is why.** A `.plgx` would be recompiled by KeePass's
  in-box CodeDOM provider, which caps the language at C# 5. This ships a plain DLL and escapes that
  entirely; the probes are conservative only because `Add-Type` compiles them.

### The trap that shaped the design

**`GroupForm` takes a copy of a group's `CustomData` when it opens and writes that copy back on OK.**
A value written to the live group therefore **survives Cancel and is destroyed by OK**, silently and
completely.

Two consequences here. The admission record is checked on every connection precisely so that losing
one to a stale snapshot makes the certificate inert rather than invisible and irrevocable. And
**testing any `CustomData` work means pressing OK, not just looking**: make the change, press OK,
reopen the dialog, and only then believe it. Do the same after saving and reloading the file, because
those are two different failures.

Equally load bearing: **KeePass's save reports nothing to its caller.** `MainForm.SaveDatabase` is
`void`, shows its own warnings, and leaves the caller no way to ask whether anything reached the
disk. What it does say is `PwDatabase.Modified`, which a successful save clears, so that is what a
write is believed on. Every write waits for its save on KeePass's thread and reads back what it
wrote, pairing's among them rather than pairing's alone as this once said, and it waits through
`BeginInvoke` and a bounded wait, never `Invoke`, which has none. `DESIGN.md` §119 says why that
wait is safe.

## Build and toolchain

**`build.ps1` is the entry point.** It is also the gate, and CI runs the same script on every pull
request and on every push to `master`, because there is one definition of clean and it is in that
file.

```powershell
.\build.ps1                # build, then every test that does not need a running KeePass
.\build.ps1 -Task All      # the above, then the end to end tests
.\build.ps1 -Task Deploy   # copy the plugin into plugins\KeePassGate in the provided KeePass
```

**`build.ps1 -ListGates` is what CI asks for.** The workflow builds one job per name it returns, so
a task added to the script is checked without anybody editing `.github/workflows/gate.yml`. The
jobs are never written there, so what changes in that file is the commit each action is pinned
to, which its header says as well.

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

- **Docs stay with code.** A behavior or architecture change updates the design documents in the
  same pass, and reversals are recorded rather than edited away.

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

[`DESIGN.md`](DESIGN.md) §132 ends with a multi-step build order. **Steps 1 to 6 are done.** The skeleton
loads and shows a KPG tab, that tab creates, recreates and deletes the authority, and the gate
serves one route over mutual TLS to a client the authority issued.

**The storage decision survived the thing most likely to invalidate it.** The `GroupForm` snapshot
hazard is real and now measured rather than remembered: `SnapshotHazardTests` reads the mechanism off
the real KeePass and reproduces the behavior with KeePass's own types. What it established, and what
step 8 has to build on: the settings dialog the KPG tab lives on carries **no** snapshot, so those
buttons are safe from their own dialog, and anything written while a **group** dialog is open is
destroyed by its OK.

**Step 7 is validation**, and it is smaller than the build order implies. The anchor pin and the
extended key usage check are already in `ClientCredentials`, because a transport with no identity is
not testable. What is left is the admission record lookup, the profiles that come from it, and the
validity window with its five minutes of skew on `notBefore` only.

**One thing to know before touching the transport.** An `EphemeralKeySet` certificate signs happily
and SChannel will not do TLS with it in either direction. That is why `Org.BouncyCastle.Tls`
terminates the transport, and why every key the plugin loads can stay off the disk.
`HostCertificateHandshakeTests` pins both halves, including the SChannel refusal, so a tidy-up that
goes back to `SslStream` fails loudly rather than quietly writing keys to somebody's profile.

**A dependency ships beside the plugin.** `build.ps1 -Task Deploy` copies every DLL from the build
output into the plugins directory, because KeePass loads plugins with `Assembly.LoadFrom`, which
probes the plugin's own directory rather than KeePass's.
