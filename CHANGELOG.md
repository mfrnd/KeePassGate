# Changelog

What each version of KeePassGate changes. The plugin, PyKeePassGate and the routing document carry
one version and move together, and [`CONVENTIONS.md`](CONVENTIONS.md#versions) says how the number
moves: at 0.x, a minor version for anything added to or broken in either half, and a patch for a
fix that changes no interface.

Newest first. A version is written here before it is released, under its own heading, and says
**unreleased** until it is. A patch releases only what it fixes, so its heading names the halves
it releases, as `0.1.1, the plugin and the client, unreleased` would, and a minor version names
none, since it releases both.

## 0.2.0, unreleased

- **The KPG tab shows a database whose every authority is retired as retired**, rather than as
  having no authority yet. Copies that each kept a different authority in the chooser, before
  their answers were synchronized, retire every authority, and the tab then sent the operator
  looking for a step they had missed. A delete, which retires an authority too, is shown the same
  way.
- **The client's messages and the documentation name the authority** wherever "one" stood for
  it, and no longer assume two copies or two authorities.
- **A database holding a crafted authority key stops only itself being served.** A key named
  `KPG.Authority.` with no digest, beside the database's own authority, made the gate drop every
  connection to every open database for as long as that database was open. It now waits for the
  operator to choose between its authorities, as a database synchronized from copies that each made
  an authority does, and every other open database is served.
- **The plugin stores a URL as it is written**, as it stores every other value. It refused one
  holding `{` or beginning `cmd://` with `400`, and accepts both now. KeePass's Open URL does more
  with a URL than those two cases: it replaces an environment variable in it, hands a path or a
  network share to whatever Windows starts for it, and with an override switched on puts the
  entry's own values into a command line. Telling which URL is safe takes what KeePass is set to
  do as well as what the URL holds, so one check given both is to take the place of a refusal
  that covered two cases. Until then, [`docs/access-control.md`](docs/access-control.md) warns
  against opening a URL a program may have written.
- **A translated KeePass is read the way it reads itself.** Where KeePass's language and Windows'
  regional format sort letters differently, Danish and English among them, the gate looked names up
  in the wrong order and missed what was there: an admitted client was refused `401`, a field was
  served empty and an attachment answered `404` though both were listed, a node's own seal was
  passed over for the grant above it, and two authorities with no retirement were served as one.
  Every connection is now served in KeePass's own culture.
- **A program reading the database while KeePass changes it reads one tree.** A read ran beside
  KeePass's own work and could miss a group or an entry that was there, or meet a list KeePass was
  changing and be answered `500`. Every read now holds KeePass still for as long as it takes,
  a fraction of a millisecond for one entry and about seventeen for ten thousand, and waits first
  for whatever the operator is typing or clicking and for KeePass's own timers. One KeePass cannot
  make room for within ten seconds is answered `503 busy`.
- **A change waits for KeePass to finish what it is doing**, a save, an import, an export or XML
  Replace, where it was answered `503 busy` straight away, and is answered so only if KeePass is
  still at it half a minute later. Under a message box in KeePass it is answered `503 busy` at
  once, since nothing happens in KeePass until the box is closed.
- **The plugin issues its certificates through Bouncy Castle, with names in UTF8String**, where
  Windows issued them with names in PrintableString, and no key of the plugin's passes through
  Windows any more. A database whose authority Windows issued keeps working as it was: the
  authority goes on issuing, the clients it admitted go on being served, and a certificate it
  issues now names it exactly as it names itself.
- **A stored authority somebody crafted costs the time of refusing it**, not the time it asks for.
  One over 64 KiB, asking for more than 10,000 iterations of its key derivation or holding more
  parts than a stored pair has is refused before any of that work is begun.
- **Another plugin's older Bouncy Castle stops the plugin starting**, naming both copies. Every 2.x
  release of Bouncy Castle is one assembly to .NET, so a copy another plugin loaded first was the
  one this plugin ran on, whatever it lacked.
- **The plugin is compiled at C# 13**, and the build needs the .NET SDK 9 or later. Nothing it does
  changes: the compiled plugin differs only in caching a delegate made from a static method rather
  than making one at every use, and in an attribute the compiler adds.
- **The plugin's code answers every finding of CodeQL's `security-and-quality` queries**, each fixed
  or suppressed with its reason where it is made, and nothing the plugin does changes. Its six calls
  into Windows go through one class, and the path of a file it writes is joined by a helper that
  refuses a part taking the place of those before it. A check the code before it had already
  settled, and a loop that only asked whether there was anything, are gone. A loop that filters,
  maps or tests a list is the query it is, and the ASCII letters, digits and hexadecimal digits the
  wire's grammars allow are asked of one class. The writers it builds JSON with, and the stream
  a request's body is read into, are disposed once they are read, and what it disposed by hand is
  disposed by `using`, or on every way out where it is handed on.
- **PyKeePassGate refuses to connect over a TLS context offering anything but TLS 1.3**, before it
  dials, which every context it makes already did, so nothing a program built on it does changes.

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
