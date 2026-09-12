# PyKeePassGate

<!-- One of Python's built-in functions, named the way Python names it. -->
<!-- cspell:ignore repr -->

The reference client for [KeePassGate](https://github.com/mfrnd/KeePassGate), a KeePass 2.x plugin
that serves a small specification-defined API over mutual TLS so that automation and AI agents can
each be given a narrow, revocable, audited slice of a database.

**Reference implementation** means the code here is the second copy of the specification. Where the
plugin and this disagree, one of them is wrong and a test should already have said so. It also means
what is missing is a statement: a route with no method here is a route the gate does not serve.

## Install

**Not on PyPI yet.** The name is held there by
[a placeholder](https://pypi.org/project/pykeepassgate/), 0.0.1, which is not this client, so
`uv add pykeepassgate` and `pip install pykeepassgate` install something else until the first
release is published. Until then it is installed from a checkout of
[the repository](https://github.com/mfrnd/KeePassGate), whose `pyproject.toml` is the client's:

```shell
git clone https://github.com/mfrnd/KeePassGate
uv add ./KeePassGate              # as a dependency of the project this is run in
uv tool install ./KeePassGate     # or the command line on its own
```

`pip install ./KeePassGate` takes the same path. One dependency, `cryptography`, because the gate
hands over a PKCS#12 and Python's `ssl` reads PEM. Everything else, the command line included, is
the standard library.

## Pair

The operator presses **Invite** on the **KPG** tab of the database's settings dialog and sends
the invitation over a second channel. What they see while doing it is
[`docs/pairing.md`](https://github.com/mfrnd/KeePassGate/blob/master/docs/pairing.md), and worked
examples of everything below are
[`docs/pykeepassgate.md`](https://github.com/mfrnd/KeePassGate/blob/master/docs/pykeepassgate.md).
It is spent by the first client to use it and it does not survive KeePass closing.

```shell
pykeepassgate pair --name agent-invented --profile-name reader
```

The gate listens on 51776 unless its operator chose another port, and `pair` dials that without
being told. A different one goes in `--port`, and the **Listening** row of the operator's
**Tools**, **KeePassGate (KPG)** window shows which it is.

Leave `--invitation` off and the token is read from standard input, so it stays out of the shell
history and out of the process list. At a terminal it is asked for, and each character typed or
pasted shows as a `*`.

The same thing from a program:

```python
from pykeepassgate import api

credentials = api.pair(
    invitation="...",
    name="agent-invented",
    profiles=["reader"],
)

print(credentials.profiles)  # what the operator approved, not what was asked for
```

## Use

```python
from pykeepassgate import GateClient

client = GateClient.open()
print(client.status())
```

```shell
pykeepassgate status
pykeepassgate whoami
```

An entry KeePass shows as expired is listed and read like any other, because an expiry is advice
rather than a control. `client.entries(include_expired=False)` leaves the expired ones out, a read
entry's `expires` says when it expires, and `client.update_entry(uuid, expires=False)` takes the
mark off a password that has just been rotated.

**Nothing it hands back prints a value.** The `repr` of an entry, its fields, a field, an
attachment or a response names what it is and shows none of what it holds, so a log line or a
traceback carries no secret. A value is printed only by reading it.

## What to catch

Every exception says what to do next rather than what went wrong, because that is the part a caller
has to decide. Everything in the table but its last three rows derives from `GateError`, which
catches whatever this client raises of its own and nothing the standard library raises.

| Exception | What it means, and what to do |
| --- | --- |
| `DatabaseLocked` | The database is locked. Wait, and try again, or hand `retry_locked_timeout=` to the client and let it wait. Raised before anything is sent when the handshake was answered with the certificate only a locked database has, and as the gate's `503 locked` when the database is locked, or locks while a request is on its way: nothing was done, apart from a write whose handler had already run, which is `500 internal error`. |
| `GateBusy` | The gate cannot serve this right now: a write under a dialog open on what it would change, or while KeePass is busy with something of its own, or a read while the database kept changing under it. The same, with `retry_busy_timeout=`. |
| `AuthorityUndecided` | The database holds two certificate authorities and the operator has not kept one, which KeePass asks about on its own screen. The same, with `retry_undecided_timeout=`, and pair again if the one kept is not this client's. |
| `GateUnavailable` | The base of the three above, so catching it catches every `503`, and raised on its own for a `503` whose word this client does not know or that names none, with the word on `.error`. Nothing waits that one out. |
| `NotAdmitted` | The gate does not admit this certificate: it was never admitted, it was revoked, or it has expired, which is named as such, with the moment, before anything is dialed. Pair again. |
| `ProofRefused` | During pairing: the token is wrong, or whatever answered is not the KeePass whose operator holds it. Stop. |
| `PairingRefused` | The operator said no. The invitation is spent, so ask for another. |
| `PairingUnanswered` | Nobody answered the dialog. The invitation is **not** spent, so this can be tried again. |
| `PairingError` | The base of the three above, and raised on its own for the rest of what stops a pairing, with a message saying which. A name or a profile the gate would refuse, or more profiles than the 32 it takes, is caught before the request is sent: correct it and pair again. An invitation that is gone ran out or was spent: ask for another. An exchange the gate no longer holds leaves the invitation good: pair again with it. A pairing route the gate does not have is the two halves disagreeing about the wire. |
| `RouteRefused` | Everything else the gate refuses, carrying the status and the reason it gave, usually one word: a `413` says in a sentence what was too large. A refused `head` carries the status alone, because its answer has no body to say anything in. A `404` is deliberately both "no such node" and "not yours", so a client cannot map a database by asking: if the node should be there, the rule is too narrow and the operator has to widen it. |
| `Withheld` | A protected value this client may not read, from every way of reading one: the rule that lets it read or list the entry does not grant `P`, which the operator can add. Never `None` or an empty string in its place; `entry.withheld` and `field.withheld` name what is kept back without a `try`, so `{name: entry.fields[name] for name in entry.fields if name not in entry.withheld}` reads every value this client may read. |
| `MalformedToken` | The text handed over is not an invitation, so nothing was sent. Check what was pasted. |
| `ProtocolError` | Something on the wire is not what the protocol allows: an answer this client cannot read, or a request it will not send. Do not retry an answer it cannot read: the two halves disagree about the wire, which `tests/pykeepassgate/test_protocol.py` exists to stop. An answer longer than `max_response_bytes` is refused this way too, and the message says so: raise the setting rather than retry. |
| `NotPaired` | Nothing is stored for a paired client where this looked: nothing has paired under that profile, or the directory named is not the one the pairing used. Pair, or name the profile or the directory it used. A `StorageError`, so catching that catches this as well. |
| `StorageError` | From `pair`, before anything is sent: the directory cannot hold credentials, and the message says why. Nothing is spent, so fix that and pair again. Afterwards: what is stored for this client will not read, or could not be written, and the message names the file or the directory to look at. |
| `TransportError` | No whole answer arrived: nothing is listening, the handshake failed, as below, or the connection broke or timed out once the request was on its way. After a read, send it again. After a write, the change may have landed, so look before sending it again: read or list what it changed, where the rules allow that, or ask the operator. |
| `ValueError` | A setting or an argument that could not work, refused where it is given and before anything is dialed, stored or spent: a port, a timeout, a retry budget, a response cap, a profile's name or an expiry. The message names it. |
| `AttributeError` | Something this client never read: a listed entry's `username`, `password`, `notes`, `expires` or `custom()`, a listed group's `notes`, a listed field's `value`. Read it first. Never raised for a withheld value. |
| `KeyError` | A name `entry.fields` or `entry.custom()` does not hold, as from any mapping. |

**The `503`s are the only refusals this client will wait out**, and only when a budget says so.
Each carries a `Retry-After` saying when to come back, and each of the three budgets, one per word,
says for how long to go on doing that. All three are off by default, so a refusal is raised exactly
where it happened, and a word none of them names is raised at once whatever they say. Nothing else
is retried, because waiting fixes none of it. `docs/` has the whole of it.

**A failed handshake says why.** `certificate has expired` is a database that has stayed locked
longer than ninety days, which unlocking it in KeePass mends.
[`docs/pykeepassgate.md`](https://github.com/mfrnd/KeePassGate/blob/master/docs/pykeepassgate.md)
gives the other reasons.

**An answer over 4 MiB fails whole with `ProtocolError`** rather than arriving cut short, and
asking again reads the same answer to the same cap. The plugin does not page, so a listing grows
with the database, and a very large one can need more: `max_response_bytes=` on the client, or
`--max-response-bytes` on the command line, raises it, and the message names both.

## What it does not do

**It does not open a `.kdbx`.** There is no file and no master password on this side of the API, by
design. What that means for anybody expecting a `pykeepass` object is measured on the
`spikes/pykeepass-compatibility` branch: 23 of its 56 public members are servable over the routes the
gate already has, and the object itself is not one of them.

**It defends nothing against local code execution as the user.** The client's key is on the disk,
because a client has to still be the same client tomorrow. The whole design concedes this
throughout, and so does this.
