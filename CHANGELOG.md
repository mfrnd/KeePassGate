# Changelog

What each version of KeePassGate changes. The plugin, PyKeePassGate and the routing document carry
one version and move together, and [`CONVENTIONS.md`](CONVENTIONS.md#versions) says how the number
moves: at 0.x, a minor version for anything added to or broken in either half, and a patch for a
fix that changes no interface.

Newest first. A version is written here before it is released, under its own heading, and says
**unreleased** until it is. A patch releases only the half it fixes, so its heading names that
half, as `0.1.1, the plugin, unreleased`, and a minor version names none, since it releases both.

## 0.1.1, the plugin, unreleased

- **The plugin is compiled at C# 13**, and the build needs the .NET SDK 9 or later. Nothing it does
  changes: the compiled plugin differs only in caching a delegate made from a static method rather
  than making one at every use, and in an attribute the compiler adds.
- **The plugin's code answers every finding of CodeQL's `security-and-quality` queries**, each fixed
  or suppressed with its reason where it is made, and nothing the plugin does changes. Its six calls
  into Windows go through one class, and the path of a file it writes is joined by a helper that
  refuses a part taking the place of those before it.

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
  with the tag a read hands back. Until the first release is published it is installed from a
  checkout.
- **A package for the release**: `build.ps1 -Task Package` zips the plugin and everything it loads,
  with its license and notices, beside a `SHA256SUMS`.

[`docs/`](docs/README.md) says how to use all of it.
