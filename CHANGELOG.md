# Changelog

What each version of KeePassGate changes. The plugin, PyKeePassGate and the routing document carry
one version and move together, and [`CONVENTIONS.md`](CONVENTIONS.md#versions) says how the number
moves: at 0.x, a minor version for anything added to or broken in either half, and a patch for a
fix that changes no interface.

Newest first. A version is written here before it is released, under its own heading, and says
**unreleased** until it is.

## 0.1.0, unreleased

The first version, so there is nothing earlier to describe a change from: everything in it is new.

- **The plugin**: a KeePass 2.x plugin serving an HTTP API over mutual TLS 1.3, with every database
  its own certificate authority, whose authority and host certificate are issued together for a
  hundred years.
- **Pairing**, by which a program holding an invitation from the operator becomes a client of one
  database, approved on KeePass's own screen.
- **Access rules** on groups and entries, written on the **KPG** tab, and an audit file of every
  decision they make, which the plugin opens with the right to append and no other.
- **The API**, whose routes [`openapi.yaml`](src/KeePassGate/Spec/openapi.yaml) declares: a
  database's groups, entries, fields and attachments, and which version of the plugin is answering
  and the commit it was built from. An update naming only what a node holds already writes
  nothing, and a node that goes away under a request is answered as gone, or as locked, never as
  empty.
- **PyKeePassGate**, the reference client, as a library and as a command line. Each refusal is an
  exception saying what to do next, and each word a `503` carries has a budget of its own to wait
  it out with; a value the rules withhold is raised as withheld rather than read as empty; and
  nothing it hands back prints a value. Until the first release is published it is installed from
  a checkout.

[`docs/`](docs/README.md) says how to use all of it.
