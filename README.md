# KeePassGate: the HTTP library measurements

**Proofs, kept for reference and never merged.** KeePassGate needs an HTTP implementation it can
drive from a stream it authenticated itself, and there are several candidates. Arguing about which
one is cheaper to carry produces an opinion. Vendoring each one, building it, and running a request
through it produces a number.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. Those are on the mainline branches, which carry what these proofs establish and not the
code that establishes it.

## What a proof here looks like

One directory per candidate, named `spikes/<library>`, self contained and buildable on its own:

```text
spikes/<library>/
  KeePassGate.<Name>.csproj    one program, net48, nothing referenced from outside the directory
  README.md                    what this one was asked, and what it answered
  src/                         the harness: a certificate authority, a TLS host, one request
  vendor/                      the library as it arrives, plus a note on every line changed
```

Each mints its own certificate authority in memory, serves one request over loopback with TLS 1.3
and mutual TLS, and prints what it saw. Nothing here touches a certificate store, a database or the
network, so a proof can be run without arranging anything first.

```powershell
cd spikes/<library>
dotnet build
.\bin\Debug\net48\KeePassGate.<Name>.exe
```

**The number that matters is divergence**: how many lines of the vendored library had to change for
it to serve. It is recorded per candidate rather than estimated, because carrying a patched library
is the cost that keeps being paid.
