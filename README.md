# KeePassGate

A KeePass 2.x plugin that serves a small, specification-defined HTTP API over mutual TLS, so that
automation and AI agents can each be given a narrow, revocable, audited slice of a database.

**Steps 1 to 22 of the build order are done, and step 23 is being built.** The plugin loads into
KeePass, the KPG tab creates, recreates and deletes the database's own certificate authority, and
the gate answers over mutual TLS 1.3 with the host certificate chosen from the rotating name in
the ClientHello. A client is served only if the database holds an admission record for its
certificate, so revoking one empties that record and writes a revocation beside it, and takes
effect on the next connection. **A stranger can become a client**: the tab hands out an
invitation, the two sides prove they hold it, the operator approves what it may reach, and the
record is saved before the certificate is handed over. A client's certificate lasts as long as the
operator picks, and the client can renew it on a dialog like that one or give it up itself. A
locked KeePass answers that it is locked rather than failing at the transport. **Routing is the
OpenAPI document**, embedded in the plugin: handlers bind to `operationId`, bodies are checked
against the declared schema before a handler runs, and a path the document does not declare has
nowhere to go. **Twenty-four routes read and change groups, entries, fields and attachments**, each
declaring the verb it needs and the node it is about, two of them saying what the caller may do
there, and the router refuses anything the admission record and the rules on that node do not both
allow, writing every decision to an audit file outside the database.

**License: MIT.** GPLv2 is not required. KeePass is GPL-2.0-or-later and grants no plugin exception,
but the FSF's own requirement is a GPL-compatible license, and MIT is one, so a permissive
KeePassGate is sound under the strict reading of the linking question as well as the loose one.

## Using it

**[`docs/`](docs/README.md) is the documentation for whoever runs this**: installing the plugin,
giving a database an authority, letting a program in, deciding what it may reach, taking it back
out again, and finding the record of what it did. It shows the real screens, screenshotted from a
driven KeePass.

Everything below is written for whoever builds it.

## The documents, in reading order

| | |
| --- | --- |
| [`docs/`](docs/README.md) | how to run it, with the screens |
| [`DESIGN.md`](DESIGN.md) | what to build, and a section attacking it |
| [`API.md`](API.md) | the reasoning behind the routes and what is deliberately absent, with the per-route detail in [`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) |
| [`CA.md`](CA.md) | the certificate authority in full: issuance, claims, validation, revocation |
| [`PAIRING.md`](PAIRING.md) | how a stranger becomes a client, with the flow diagrams |
| [`FINDINGS.md`](FINDINGS.md) | what was measured rather than assumed, and what turned out wrong |
| [`TESTING.md`](TESTING.md) | the four test tiers, what each one buys, and how to run them |
| [`CONVENTIONS.md`](CONVENTIONS.md) | the rules that hold on every branch |
| [`CHANGELOG.md`](CHANGELOG.md) | what each version changes, and that there is one version |
| [`ANALYSIS.md`](ANALYSIS.md) | the argument, and the alternatives that were rejected |

## What was decided, in one place

- **mTLS instead of a pairing protocol.** Verified working in process on .NET Framework 4.8,
  including TLS 1.3.
- **TLS 1.3 required, nothing older offered**, which is affordable now that Windows 10 is out of
  support and is what makes it safe for a certificate to carry claims.
- **Every database is its own certificate authority**, stored inside itself. A malicious kdbx
  vouches only for itself, which is what a registry on the host could never guarantee.
- **A certificate proves identity, an admission record grants.** The record lives in the database
  and is checked on every connection, so losing it makes a certificate inert rather than invisible.
- **A rotating SNI names the database**, resolved before any TLS work. An unresolvable name is
  dropped with no response, so an attacker never reaches TLS, certificate selection, or the
  pairing dialog.
- **No third-party HTTP dependency.** Selecting a certificate by SNI means owning the accept loop,
  so all that was left to buy was a header parser. The one that fits the seam natively is LGPL-2.1
  and unmaintained; `DESIGN.md` §107 records the trade and what would reopen it.
- **The TLS is Bouncy Castle**, taken so that no private key is ever written to the user's profile.
  The platform's own TLS refuses to serve with a key that has not been, which would have put the
  host key on the disk for as long as a database was open.
- **`IGateContext`** is the seam. The router, authorization and audit never see the transport, which
  is how it stays replaceable.
- **Spec-first routing.** The OpenAPI document is the routing table and handlers bind to
  `operationId`, so an undeclared path has nowhere to go.
- **A plain DLL on .NET Framework 4.8**, not a `.plgx`, because the packaging format is what forces
  C# 5.

## Building it

**The KeePass to build against is provided rather than found.** `keepass.version` says which one,
and `tools/KeePass.ps1` fetches that exact release as a portable install into `.keepass\<version>\`,
which is gitignored. Changing the pin provides the new version rather than reusing whatever was
there, because the directory is named after it. Pass `-KeePassDir` to point at a KeePass kept
somewhere else; otherwise a fresh clone provides what it needs before anything is built.

Nothing is trusted on the way. The archive is checked against a SHA-256 pinned in
`keepass.sha256`, and the `KeePass.exe` inside it has to say it is KeePass, be the version asked
for, and carry a valid Authenticode signature from KeePass's author. That last check is the one
that does not depend on where the bytes came from.

```powershell
.\build.ps1                 # prose, the client, the build, and every test that does not need KeePass
.\build.ps1 -Task KeePass   # provide the pinned KeePass, and say whether a newer one is out
.\build.ps1 -Task All       # the default, then the end to end tests
.\build.ps1 -Task Deploy    # copy the plugin into plugins\KeePassGate in the provided KeePass
.\run-debug.ps1             # deploy, then start that KeePass with the Debug build in it
.\run-release.ps1           # the same, with the build that would ship
uv run pre-commit install   # once per clone: the fast half of the gate, run at every commit
```

A plain DLL on .NET Framework 4.8, built with the `dotnet` CLI, opening in Visual Studio 2019 as
well. [`TESTING.md`](TESTING.md) says what the test tiers are for.

## The client

[`src/pykeepassgate/`](src/pykeepassgate/README.md) is **PyKeePassGate**, the reference client:
pairing, and every route the gate serves. `pykeepassgate.api` is the library and
`pykeepassgate.cli` is a command line over it that can do nothing the library cannot, and
`pykeepassgate.testing` is a scripted stand-in gate for testing a program built on the library.

Reference implementation means the code is the second copy of the specification. Where it and the
plugin disagree, one of them is wrong, and the protocol tests pin every construction against
vectors the plugin itself produced, so a disagreement fails a test rather than a pairing.

```shell
uv sync
uv run pykeepassgate pair --port 45678 --name agent-invented --profile-name reader
uv run pykeepassgate status
```

`45678` stands for the port the gate is listening on, which its window under **Tools**,
**KeePassGate (KPG)**, shows: 51776 unless another has been chosen.

The question a `pykeepass` user asks first is answered on `spikes/pykeepass-compatibility`, taken from
`master` and never merged. Short version: not as a `PyKeePass` object, because that is a decrypted
`.kdbx` and this side has neither the file nor the password, but 23 of its 56 public members are
servable over the routes the gate already has. That branch's `RESULTS.md` has the table.

## The measurements

The design rests on what the platform actually does rather than on what its documentation suggests,
and the difference was measured. Those measurements are on two more branches, taken from `master`
and never merged.

`spikes/tls-and-certificates` holds the probes covering TLS and X.509. Three returned something other
than what the documentation would suggest, and two of those would have shipped as defects.
`spikes/build-environment` holds the one asking whether the gate can fail while nothing is wrong, and
it answers twice. Each branch's `RESULTS.md` has the table, with the output that settled each
question beside it.

Run them from **Windows PowerShell**, not PowerShell 7.
