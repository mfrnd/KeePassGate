# KeePassGate

A KeePass 2.x plugin that serves a small, specification-defined HTTP API over mutual TLS, so that
automation and AI agents can each be given a narrow, revocable, audited slice of a database.

**Design settled, and step 4, the skeleton, is under way**: the plugin loads into KeePass, shows a
KPG tab on the database settings dialog and stops cleanly. It has no certificate authority, serves
no API and opens no socket yet.

**License: MIT.** GPLv2 is not required. KeePass is GPL-2.0-or-later and grants no plugin exception,
but the FSF's own requirement is a GPL-compatible license, and MIT is one, so a permissive
KeePassGate is sound under the strict reading of the linking question as well as the loose one.

## The documents, in reading order

| | |
| --- | --- |
| [`DESIGN.md`](DESIGN.md) | what to build, and a section attacking it |
| [`API.md`](API.md) | the routes, what each verb is worth, and what is deliberately absent (TODO(step 15): it arrives with them) |
| [`CA.md`](CA.md) | the certificate authority in full: issuance, claims, validation, revocation |
| [`PAIRING.md`](PAIRING.md) | how a stranger becomes a client, with the flow diagrams |
| [`FINDINGS.md`](FINDINGS.md) | what was measured rather than assumed, and what turned out wrong |
| [`CONVENTIONS.md`](CONVENTIONS.md) | the rules that hold on every branch |
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
- **No third-party HTTP dependency.** Selecting a certificate by SNI means owning the accept and the
  TLS, so all that was left to buy was a header parser. The one that fits the seam natively is
  LGPL-2.1 and unmaintained; `DESIGN.md` §107 records the trade and what would reopen it.
- **`IGateContext`** is the seam. The router, authorization and audit never see the transport, which
  is how it stays replaceable. TODO(step 6): `IGateContext` arrives with the transport.
- **Spec-first routing.** The OpenAPI document is the routing table and handlers bind to
  `operationId`, so an undeclared path has nowhere to go.
- **A plain DLL on .NET Framework 4.8**, not a `.plgx`, because the packaging format is what forces
  C# 5.

## Building it

**The KeePass to build against is provided rather than found.** `keepass.version` says which one,
and `tools/KeePass.ps1` fetches that exact release as a portable install into `.keepass\<version>\`,
which is gitignored. Changing the pin provides the new version rather than reusing whatever was
there, because the directory is named after it.

Nothing is trusted on the way. The archive is checked against a SHA-256 pinned in
`keepass.sha256`, and the `KeePass.exe` inside it has to say it is KeePass, be the version asked
for, and carry a valid Authenticode signature from KeePass's author. That last check is the one
that does not depend on where the bytes came from.

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
