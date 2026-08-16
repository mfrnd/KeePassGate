# KeePassGate

A proposal, under analysis. **Nothing is built, and the decision to build has not been taken.**

The idea is a KeePass plugin written from scratch for automation and agents, rather than a third API
generation bolted onto [KeePassRPC](https://github.com/mfrnd/keepassrpc):

- clients authenticate with **mTLS**, so there is no bespoke pairing protocol and no hand-written
  session crypto,
- the contract is **OpenAPI-first**, so a consumer has a machine-readable specification,
- a client's identity and its coarse scope come from its **certificate**, so admission is mostly
  certificate handling,
- and the browser extension keeps talking to KeePassRPC, untouched.

## Status

| | |
| --- | --- |
| Analysis | not written yet |
| Decision to build | not taken |
| Code | none, beyond the probe below |

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
