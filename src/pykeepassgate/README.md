# PyKeePassGate

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
the invitation over a second channel. It is spent by the first client to use it and it does not
survive KeePass closing.

```shell
pykeepassgate pair --name agent-invented --profile-name reader
```

The gate listens on 51776 unless its operator chose another port, and `pair` dials that without
being told; `--port` names a different one.

Leave `--invitation` off and the token is read from standard input, so it stays out of the shell
history and out of the process list.

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

## What to catch

Every exception says what to do next rather than what went wrong, because that is the part a caller
has to decide.

| Exception | What it means, and what to do |
| --- | --- |
| `DatabaseLocked` | The database is locked. Wait, and try again. |
| `GateBusy` | The gate cannot serve this right now: a write under a dialog open on what it would change, or a read while the database kept changing under it. Retry. |
| `NotAdmitted` | This database holds no record for this certificate. It was never admitted, or it was revoked. Pair again. |
| `ProofRefused` | During pairing: the token is wrong, or whatever answered is not the KeePass whose operator holds it. Stop. |
| `PairingRefused` | The operator said no. The invitation is spent, so ask for another. |
| `PairingUnanswered` | Nobody answered the dialog. The invitation is **not** spent, so this can be tried again. |
| `RouteRefused` | Everything else the gate refuses, carrying the status and the reason it gave, usually one word: a `413` says in a sentence what was too large. A refused `head` carries the status alone, because its answer has no body to say anything in. A `404` is deliberately both "no such node" and "not yours", so a client cannot map a database by asking: if the node should be there, the rule is too narrow and the operator has to widen it. |
| `MalformedToken` | The text handed over is not an invitation, so nothing was sent. Check what was pasted. |
| `ProtocolError` | Something on the wire is not what the protocol allows: an answer this client cannot read, or a request it will not send. Do not retry an answer it cannot read: the two halves disagree about the wire, which `tests/pykeepassgate/test_protocol.py` exists to stop. |
| `StorageError` | From `pair`, before anything is sent: the directory cannot hold credentials, and the message says why. Nothing is spent, so fix that and pair again. Afterwards: what is stored for this client will not read, or could not be written, and the message names the file or the directory to look at. |
| `TransportError` | No whole answer arrived: nothing is listening, the handshake failed, or the connection broke or timed out once the request was on its way. After a read, send it again. After a write, the change may have landed, so look before sending it again: read or list what it changed, where the rules allow that, or ask the operator. |

## What it does not do

**It does not open a `.kdbx`.** There is no file and no master password on this side of the API, by
design. What that means for anybody expecting a `pykeepass` object is measured on the
`spikes/pykeepass-compatibility` branch: 23 of its 56 public members are servable over the routes the
gate already has, and the object itself is not one of them.

**It defends nothing against local code execution as the user.** The client's key is on the disk,
because a client has to still be the same client tomorrow. The whole design concedes this
throughout, and so does this.
