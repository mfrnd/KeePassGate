# KeePassGate and pykeepass: the compatibility question

**One question, kept for reference and never merged.** Can a program that was written against
[`pykeepass`](https://github.com/libkeepass/pykeepass) be pointed at a KeePassGate database instead,
and if not, what would have to change?

This branch is taken from `master` and holds nothing else: no plugin, no client, no design
documents. Those are on the mainline branches, which carry the answer and not the working out.

## Why the question keeps coming up

`pykeepass` is the usual way to touch a KeePass database from Python. It opens a `.kdbx` **file**
with its master password and hands back a `PyKeePass` object. KeePassGate's own client,
PyKeePassGate, asks a **running KeePass** over a mutual-TLS API and is refused anything the operator
did not approve. The names are close enough that somebody will assume one is a drop-in for the
other, and that assumption is worth answering with a measurement rather than an opinion.

## How it is answered here

Two measurements rather than an argument, both runnable with nothing arranged first:

- **What `pykeepass` itself requires**, read off the library rather than off its documentation, so
  that a release which changes one of those requirements fails a test instead of leaving a document
  quietly wrong.
- **Its public surface, member by member**, classified against the routes the gate actually serves.
  The classification is checked in and the probe reads the library's own `dir()`, so a member that
  appears or disappears upstream is reported rather than missed.

Both live under `spikes/pykeepass/`, which is a directory rather than a package: nothing here is
installed, imported by anything, or shipped.
