# The client, from the other side

<!-- WSL's configuration file, named the way Microsoft names it. -->
<!-- cspell:ignore wslconfig -->
<!-- Three of Python's built-in functions, named the way Python names them. -->
<!-- cspell:ignore getattr hasattr repr -->
<!-- The encryption scheme PKCS#5 names for a key under a password. -->
<!-- cspell:ignore PBES -->

PyKeePassGate is the reference client: a Python library and a command line over it. This page is
worked examples. [`../src/pykeepassgate/README.md`](../src/pykeepassgate/README.md) is the package's
own account of itself, and [`openapi.yaml`](../src/KeePassGate/Spec/openapi.yaml) is what the gate
serves.

Reference client means the code is the second copy of the specification. A route the gate serves
and this does not cover is a gap; a method here with no route behind it is a lie.

## Install

**The client is not on PyPI yet.** The name is held there by
[a placeholder](https://pypi.org/project/pykeepassgate/), 0.0.1, which is not this client, so
`uv add pykeepassgate` and `pip install pykeepassgate` install something else until the first
release is published. Until then it is installed from a checkout of this repository, whose
`pyproject.toml` is the client's:

```shell
git clone https://github.com/mfrnd/KeePassGate
uv add ./KeePassGate              # as a dependency of the project this is run in
uv tool install ./KeePassGate     # or the command line on its own
```

`pip install ./KeePassGate` takes the same path. One dependency, `cryptography`, because the gate
hands over a PKCS#12 and Python's `ssl` reads PEM. Everything else, the command line included, is
the standard library.

## Pair

The operator hands over an invitation from the **KPG** tab ([`pairing.md`](pairing.md)). Pairing blocks
while they read the dialog, which is the slow half and is meant to be.

```shell
pykeepassgate pair --name agent-invented --profile-name reader
```

The gate listens on 51776 unless its operator chose another port, and `pair` dials that without
being told. A different one goes in `--port`, or `port=` from a program, and the **Listening** row
of the operator's **Tools**, **KeePassGate (KPG)** window shows which it is.

Leave `--invitation` off and the code is read from standard input, so it stays out of the shell
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

print(credentials.profiles)
```

**Print what came back rather than what was asked for.** The operator may have cleared a profile,
and `credentials.profiles` is what they approved. A client that assumes it got what it asked for
discovers otherwise as a refusal on some later request, a long way from the cause.

**`pair` prints the admission key as well**: SHA-256 over the certificate it was issued, in hex,
which is what the operator's [audit file](audit.md) names this certificate by, as `admissionKey`,
from the line recording the approval onwards. It is how the person running the client and the
person at KeePass name the same program. `identity` prints it again later, `renew` prints the new
certificate's and `revoke` the one it gave up, and from a program it is
`credentials.admission_key`, read off the stored certificate. The one `pair` prints is also the
**client key**, which the KPG tab knows this client by, which every line of the audit names as
`clientKey`, and which a renewal keeps while the admission key changes. It is stored with the
identity, `credentials.client_key`, and `identity` prints it above the admission key; `whoami`
asks the gate for both.

**Ask for a shorter certificate when a year is too long.** `--lifetime 30` on the command line, or
`lifetime=timedelta(days=30)` from a program, asks for one that lasts thirty days. Anything from a
day to a year will do, in whole days, and anything else is refused before anything is sent. The
operator picks, from a list with the one asked for chosen already, so the lifetime is another thing
to read off what came back: `pair` prints when the certificate expires, and so does
`credentials.expires`. Asking for nothing is offered a year.

## Where the identity is kept

Pairing stores what it was handed, a private key among it, in one directory per profile:
`%LOCALAPPDATA%\pykeepassgate\<profile>` on Windows, and `pykeepassgate/<profile>` under
`$XDG_CONFIG_HOME` elsewhere, or under `~/.config` when that is not set or is not an absolute path,
which the XDG specification says to ignore. `--profile` picks the name, `default`
unless given, and `--directory` names the directory outright; every command takes one or the other,
never both. From a program, `api.pair` and `GateClient.open` take `profile=` and `directory=`.

**A profile is a name and not a path**, so one holding a separator or a colon, `..`, or a name
ending in a dot or a space is refused, exit 2 or `ValueError`, rather than stored somewhere nobody
named. Windows drops a trailing dot or space from a name, which would make `work.` the same
identity as `work`. A path is what `--directory` is for.

**The directory is checked before the invitation is used**, because afterwards is too late: once the
gate has answered, the invitation is spent and the record is written, and an identity that could not
be stored cannot be asked for again. A directory that cannot be made or written to, or a file name
taken by a link or by anything but a regular file, is refused with `StorageError`, and nothing is
spent.

**So is a directory another account can reach**, where the platform has file modes: one that already
existed and belongs to another account, or that another account may write to. That account could
put a file of its own where the key is about to go, or swap `authority.pem` afterwards for an
authority it holds the key to. On Windows the directory's ACL decides, and this client cannot read
it, so nothing is asked there. Where credentials are kept somewhere shared on purpose, say so:

```shell
pykeepassgate pair --name agent-invented --directory /srv/agents/kpg --allow-shared-directory
```

`allow_shared_directory=True` does the same for `api.pair`. It waives that one question and no
other: every other check on the directory still applies, and a link is still not followed, whether
it stands where a file goes or where the directory itself is named. `renew`
takes the same flag, and `GateClient.renew` the same parameter, for the identity's own directory,
which a renewal writes the new identity into; the two directories it makes beside it are its own
and are never waived.

**The key can be kept under a password.** `pair --key-password` asks for one at a terminal, twice
and without echo, before anything is sent, and stores the client's key encrypted under it. Every
command that uses the key afterwards asks for it at a terminal, once, and only when the key is
encrypted, which `identity` says without asking. **The command line has no other way to take it,
by design**: never as an argument, which the process list shows, and not from standard input
either, so with no terminal `pair --key-password` is refused, exit 2 with nothing spent, and a
command that needs the password fails with `StorageError`, exit 1. Standard input from the null
device is no terminal, on Windows as well, where it claims to be one: a command run with its input
from `NUL` is refused at once rather than left waiting on a console nobody is at. A program passes
`key_password=` instead, to `api.pair`, `GateClient` and `GateClient.open`: the password as text
or bytes, or a callable returning it, which is asked once, and only when the key turns out to be
encrypted.

```python
def key_password() -> str:
    ...     # from wherever this program keeps the secrets it is given

client = GateClient.open(key_password=key_password)
```

`renew` stores the new key under the password that opened the old one, and a key stored without
one renews to a key without one. An encrypted key with no password, or one the password given
does not open, is a `StorageError` naming `identity.pem`. **What it protects is a copy of the
directory** taken somewhere else, such as a backup, a disk image or the image of a virtual machine.
It protects nothing against a process running as the same user, which can read the password as
the client is given it, or wait for the client to open the key: the design concedes such a process
throughout, and this does not change that.

**How much it protects a copy is the password's to decide.** The key is PKCS#8 under PBES2, with
AES-256-CBC keyed by PBKDF2-HMAC-SHA256 at 2048 iterations, which is OpenSSL's default and the only
work factor this path offers: `ssl` opens a key from PEM and nothing else, and `cryptography`
writes PEM PKCS#8 no other way. That is little work per guess, so a password a person types keeps
a backup from a casual reader rather than a determined one. A program should pass a long random
`key_password`.

## Check the gate

```shell
pykeepassgate status
pykeepassgate identity
pykeepassgate whoami
```

The two names follow the API's: `whoami` asks `GET /gate/whoami`, and `identity` reads the
identity this client stored.

`status` connects and asks. `identity` says what this client is without connecting, which is what
to run when nothing is answering, to find out which side the problem is on: the subject, the client
key, the admission key, the database, the profiles the operator approved at pairing or at the last
renewal, when the certificate expires and whether it has, or that the stored moment is not one it
can read, which leaves the gate to decide, whether the key is encrypted, the port, and where the
identity is stored. The expiry and the key are what stop every other command before it dials, and
`identity` reads both off the stored files without asking for a password.

`whoami` asks the gate who this client is, as the database admitted it: the subject, the client
key, the admission key, when the certificate stops being admitted, and two lists of profiles. The
**client key** is the key the operator knows this client by: the admission key of the certificate
it paired with, which every renewal keeps, and which the KPG tab counts and tells clients apart by,
since a name need not be unique. The **admission key** is the certificate this identity holds now,
which a renewal changes. A renewed identity no longer holds the certificate it paired with, so the
client key is stored beside it when it is paired and copied at every renewal, and `identity`
prints the stored one. `profiles` are the ones the rules are asked about now, and `removed
profiles` are the ones its record holds that the database no longer names, which grant nothing
until the operator names them again. What `identity` prints is what the operator approved when this
identity was issued, at pairing or at its last renewal, so the two differ once a profile has been
removed since then.

```python
from pykeepassgate import GateClient

client = GateClient.open()
state = client.status()

print(state.ok, state.database, state.version, state.revision)

me = client.identity()
print(me.client_key, me.admission_key, me.profiles, me.removed_profiles)
```

`version` is the plugin's release, which this client shares, and `revision` is the commit the
plugin was built from, or empty for a build that carries none. Two machines running the same
version can be running different builds of it, and the revision says which.

`GateClient.open()` reads the credentials pairing stored, or the last renewal. Pass `profile=` where more than one
identity is kept on the machine. **Every setting is passed by name**: after the identity, which is
the credentials for `GateClient`, the directory and the profile for `GateClient.open`, the port and
the invitation for `Pairing`, and those and the name for `api.pair`, everything is keyword only, so
a setting added later never changes what an argument already written means.

## Give up an identity

```shell
pykeepassgate revoke
```

`revoke` asks the gate to revoke the certificate this client presents, and once the gate says the
revocation is saved, removes what is stored for it, by pairing or the last renewal: the key, the
certificate, the gate key and `client.json`, and the directory as well if that empties it. Nothing
can use them afterwards, since the database refuses the certificate from the next connection on,
and a private key kept for a certificate nobody accepts protects nothing. Pairing again is the way
back, into the same place.

When the gate does not confirm it, nothing is removed. `not saved` means the revocation was taken
back out and this client is still admitted, so running `revoke` again is safe. `not verified` means
KeePass holds the revocation in memory and may not have it in the file, so the certificate is
already refused, `status` says so, and what the file keeps is the operator's to settle.

A certificate the gate already refuses counts as given up, since that is what `revoke` asks for:
one the operator revoked on the Clients page, one an earlier `not verified` left refused, and one
that has expired, which is not dialed at all. `revoke` then removes the files as well, says the
gate already refused it, and exits 0.

From a program, `revoke_certificate` asks the gate and leaves the files alone, and `forget` removes
them:

```python
from pykeepassgate import api

client = api.GateClient.open()
client.revoke_certificate()
api.forget(client.credentials.directory)
```

## Renew a certificate

```shell
pykeepassgate renew --lifetime 30
```

`renew` asks the operator for a new certificate in place of the one this client holds, which they
approve on screen as they did the pairing, so it waits while they read: six minutes, as pairing
does, or longer when `--connection-timeout` says so, because the gate answers only once the
operator has and the approval is saved, and an answer hung up on is an approval lost. Once they
approve, it stores the new identity where the old one was, by writing it beside the old one and then
swapping the two, and then gives the old certificate up by presenting it. `--lifetime` is a
proposal, as on `pair`, and `expires` is what the operator picked. If KeePass is still busy a moment
after they approve, nothing is issued yet and the gate holds the approval, saying until when:
`renew` asks again by itself, budget or none, and the renewal is written without the dialog. Only
once that moment has passed, or KeePass locked in between, is `GateBusy` raised, with
`held_until` saying when it ran out, and asking again then shows them the dialog again.

The new identity is written into a directory ending `.renewing` first, and a copy of the old one
into a directory ending `.retired`, both made before the operator is asked, so that one that cannot
be made costs nothing. Only then are the four files of the identity written over with the new
ones: **anything else kept in the directory stays where it is**. A renewal that stops after the
operator approved, before the new identity is in place, leaves it whole in `.renewing`, and the gate
already admits it: `renew` says so, and a later renewal refuses to start rather than write over the
only key for it, and says how to finish, by copying its four files over the identity's, or giving it
up with `revoke --directory`.

**Renew before the certificate runs out.** The gate renews only a certificate it still admits, and
one that has expired by the gate's clock is refused as it refuses a stranger. So this client asks
nothing with one that has expired by this machine's clock: once that clock is five minutes past the
moment, it raises `NotAdmitted` naming it, before anything is dialed, and the way back is pairing
again. The five minutes are for a clock running ahead of the gate's, which would otherwise give up
on a renewal the gate still grants; a clock running behind dials and meets the gate's `401`. Beyond
that the two clocks are taken to agree, which on loopback, one machine, they do.

When giving the old certificate up does not happen, the renewal has still worked, and this client
is the new identity. The old one is then kept beside it, in a directory ending `.retired`, because
giving a certificate up takes presenting it: `revoke --directory` on that directory gives it up and
removes it, and `renew` says so, with exit code 1. A later renewal tries first: it gives the kept
certificate up itself, and forgets it once the gate takes it back, already refuses it or it has
expired. Only while the gate still admits it and will not take it back does the renewal refuse to
start.

From a program:

```python
from datetime import timedelta

renewal = client.renew(lifetime=timedelta(days=30))

print(renewal.credentials.expires)
if renewal.retired is not None:
    ...     # renewal.unrevoked says why the old certificate is still admitted
```

The client carries on as the new identity, so every call after `renew` presents the new
certificate.

## Reach a KeePass on another machine

The gate binds to loopback and nothing changes that. What reaches one from somewhere else is a
tunnel set up separately, and a tunnel has a local end: give that end to `--host`, and to `host=`
in a program. Everything else is unchanged, because the tunnel moves bytes and the gate still sees
the same client certificate and the same name.

```shell
# On the calling machine, forwarding to the one KeePass runs on. The local end is loopback and
# keeps the port, which is the arrangement that needs nothing said.
ssh -N -L 51776:127.0.0.1:51776 the-machine-keepass-runs-on
pykeepassgate status

# And when the local end is not on loopback: a container reaching a tunnel on the machine it
# runs on, whose end listens on the container bridge's address.
pykeepassgate status --host 172.17.0.1
```

```python
client = GateClient.open(host="172.17.0.1")
```

**WSL is not a container here.** Under WSL 2's default networking, `127.0.0.1` inside WSL is WSL's
own, and the gate listens only on the loopback of Windows, which no address inside WSL reaches.
Three arrangements work. A tunnel whose local end is inside WSL, a reverse tunnel opened from
Windows into WSL for instance, used with the default host. Mirrored networking,
`networkingMode=mirrored` in `.wslconfig`, under which `127.0.0.1` inside WSL reaches the loopback
of Windows, so the gate needs nothing said at all. Or a forwarder on Windows that listens on the
address WSL reaches Windows by, the gateway of WSL's default route, and passes on to `127.0.0.1` and
the port, with that address given to `--host`: listening on that address alone rather than on every
one, and with an inbound rule wherever the Windows firewall asks for one.

**Keep the port.** The port is part of what pairing stored, so a tunnel that forwards 51776 to
51776 works without another word; one that lands somewhere else needs pairing again against that
port. Pass `host=` to `api.pair` too if the pairing itself is going down the tunnel.

**It is not stored with the identity.** The credentials say which KeePass this client is, and where
that KeePass is reachable from is a fact about the machine calling. Move the client and
the identity still works; only the host changes.

**A program can make the connection itself.** `dial=`, on `GateClient`, `GateClient.open`,
`Pairing` and `api.pair`, is a callable taking the host, the port and the timeout and returning a
connected socket, and it is `socket.create_connection` unless one is given. A renewal dials with
the client's own, for the renewal and for giving the old certificate up. TLS, the rotating name and
everything else are layered over whatever it returns, unchanged, and each wait on that socket is
bounded by `connection_timeout` as on any other. It is how a program reaches a gate through
something of its own: a relay that bridges a socket pair to its standard input and output, a socket
the process that started it handed over, or a proxy that answers with a connected one. The host is
the dial's to read, so it can name anything the dial understands. The command line has no such
option, because a callable is not something an argument can carry.

```python
import socket

def dial(host: str, port: int, timeout: float) -> socket.socket:
    ...     # connect however this program reaches the machine KeePass runs on

client = GateClient.open(dial=dial)
```

## Read

```python
from pykeepassgate import GateClient

client = GateClient.open()

for group in client.groups():
    print(group.uuid, group.name)

for entry in client.entries():
    print(entry.uuid, entry.title, entry.url)
```

A listing gives what a listing gives: identifier, title, URL. Reading one gives the rest. A listed
group likewise has no `notes` until `client.group()` reads it, which needs `R`.

```python
entry = client.entry("...")

print(entry.username)
print(entry.password)
print(entry.expires, entry.expired)
```

**Absent is not empty.** An entry that was listed rather than read has `fields` set to `None`, and
asking it for a username raises `AttributeError` rather than handing back an empty string. Check
`entry.is_read` when it is not clear which one is in hand.

**Withheld is not empty either.** A value the database protects reaches this client only when the
rule that lets it read the entry grants `P`, and otherwise the gate withholds it. Reading a
withheld value raises `Withheld`, by every path there is: `entry.password` or `entry.title`, a
listed entry's title or URL, `entry.fields["Deploy key"]` and `get()` with a default, `values()`,
`items()` and `dict()` over `entry.fields` or `entry.custom()`, and `field.value` from the field
route. Nothing hands back `None` or an empty string in its place, because either would read as a
value, and `Withheld` is not an `AttributeError`, which `getattr` with a default and `hasattr`
would swallow. The message names the field, and the operator opens it by adding `P` to that rule.

The names all stay, so `in`, `len` and the keys of `entry.fields` say truthfully what the entry
holds, and `entry.withheld` and `field.withheld` say which values are kept back without a `try`:

```python
entry = client.entry(uuid)

readable = {name: entry.fields[name] for name in entry.fields if name not in entry.withheld}
print("withheld:", ", ".join(entry.withheld) or "nothing")
```

**Printing an object shows what it is and none of what it holds.** The `repr` and the `str` of an
entry, of its `fields`, of a field, an attachment, a group or a `transport.Response` say what each
is, by its names, its size or its status, and print no string an entry holds, no group's notes, no
attachment's bytes and no answer's body, because a repr is what a log line, a traceback or an
agent's transcript carries. A value is printed only by reading it, which is a decision somebody
made in the code that reads it.

**A standard field the entry does not carry reads as `""`, and is `404` on its own route.**
`entry.username` and the rest of the five read a string the entry does not hold as empty, which is
how KeePass reads one. `client.field(uuid, "UserName")` asks the gate for that one string, and the
gate answers as it does for any name the entry does not carry: a `RouteRefused` with `404`. Only an
entry neither KeePass nor the gate made can lack one, since both give every entry all five, KeePass's
entry dialog writes all five back on OK, and the gate empties one rather than taking it off. An
entry written by other software, or brought in by an import from another program's format and not
edited in KeePass since, is where one can be missing.

**A value is the string the entry stores.** A field reference such as `{REF:P@I:...}` or a
placeholder such as `{USERNAME}` comes back as that text, not as what KeePass would show or type,
because resolving one reads another entry, or the machine, past the rules that decide what this
client may see.

**An expired entry is listed and read like any other**, as KeePass shows one, struck through.
`entry.expires` is when a read entry expires, in UTC, or `None` when it does not, and
`entry.expired` is whether that moment has passed on this machine's clock; a listed entry has
neither, since a listing does not say. `client.entries(include_expired=False)` leaves the expired
ones out, judged by the gate as KeePass judges one.

```shell
pykeepassgate groups
pykeepassgate entries --group <uuid>
pykeepassgate entries --no-include-expired
pykeepassgate show <uuid>
```

`show` prints every string the entry holds, one to a line, and when it expires: notes written over
several lines print on one, with `\n` where each break was. The protected ones arrive only when the
rule grants `protected`. It is the one command that puts a password on a terminal.

## Write

```python
uuid = client.create_entry(
    group=api.ROOT,
    title="Invented Service",
    username="agent-invented",
    password="...",
    url="https://invented.invalid",
    expires="auto",
)

client.update_entry(uuid, notes="Made by the build agent.")
client.update_entry(uuid, password="...", expires=False)
client.delete_entry(uuid)
```

```shell
pykeepassgate add --title "Invented Service" --username agent-invented --password --expires auto
pykeepassgate update <uuid> --password --expires never
pykeepassgate remove <uuid>
```

`update` changes only what it names, and refuses to run with nothing named; `--expires never` takes
an expiry off. `--password` reads from standard input rather than taking a value, so the secret
stays out of the process list. At a terminal it is asked for, and each character typed or pasted
shows as a `*`; piped in, it is the one line as sent, spaces and all.

**`expires` is when the entry expires**: a moment with its time zone, `"auto"` on a create for
whatever KeePass's options give every new entry, or `False` for none, which on an update takes an
expiry off, as rotating an expired password wants. Left out, a new entry does not expire and an
update leaves the expiry as it is. A moment is sent in UTC to the second, and one with no time zone
is refused with `ValueError` before anything is sent, because which instant it names would be a
guess.

Each of these needs the matching verb on the node it touches: `C` to create, `U` to update, `D` to
delete. See [`access-control.md`](access-control.md).

**Each of these saves the whole database before it answers**, as KeePass's own Save does, so an
answer is a change on the disk rather than one in memory. On a large database that save takes
seconds, during which KeePass does not respond, and writes take their turn: one kept waiting more
than 20 seconds behind others is `GateBusy`.
[`architecture.md`](architecture.md#how-one-request-is-decided) says what else a save carries with
it.

## Fields and attachments

```python
for field in client.fields(uuid):
    print(field.name, field.protected)

client.create_field(uuid, "Deploy key", "...", protected=True)

for attachment in client.attachments(uuid):
    print(attachment.name, attachment.size)

content = client.attachment(uuid, "key.pem").content
```

Attachments need the `A` switch on the rule as well as the verb, which is what makes "read this
entry" and "read the files inside it" two separate decisions.

**512 KiB is the largest attachment this API carries**, in either direction. Reading one costs
about ten times its size in memory inside KeePass itself, so a bigger one is refused rather than
built, and the same size is what a write accepts: nothing can be read out and then not put back.
Reading a bigger one is refused with `413`, and the message names its size and the ceiling. Writing
one is refused as `400 malformed`, like any body the routing table does not allow, and a body over
1 MiB as `413` before any route sees it.

```shell
pykeepassgate attachments <uuid>
pykeepassgate fetch <uuid> key.pem --to ./key.pem
```

## Ask what it may do before trying

```shell
pykeepassgate access --group root
pykeepassgate access --entry <uuid>
```

`access` asks the gate what this client may do at one group or entry, and prints a rule for each
profile it holds, as the **KPG** tab shows one: `CRUSDLAP`, with a dot for each verb or switch that
profile is not granted there, and eight dots for one granted nothing there. A node where no profile
may do anything is refused with the `404` an absent one gets, so this tells a program about what it
can reach and nothing about what it cannot, and the audit records the question like any other.

```python
access = client.entry_access(uuid)

if access.allows("RP"):
    ...     # one profile's rule here reads the protected values too
```

**Ask about one rule at a time.** `A` and `P` say how far the verbs of the same rule reach, so a
profile granting `R` and another granting `P` do not let this client read a protected value, and
`allows` asks each profile's mask on its own: `"RP"` is whether this client may read the protected
values here, `"CA"` whether it may add an attachment to this entry, and `"C"` whether it may add to
this group. A switch says nothing about removing, which shows nobody anything, so beside only `S`
or `D` it is not asked: `"SA"`, whether this client may strip an attachment here, is answered by
`S`. **Removing the node itself is the one question a single answer does not settle**: it
needs `D` here and in the group the node would leave, which is that group's answer.
`client.group_access()` asks about a group, the root unless told another, and `access.grants` holds
the masks by profile. [`access-control.md`](access-control.md) says what each letter is worth.

## What to catch

Every exception says what to do next rather than what went wrong, because that is the part a caller
has to decide. Everything in the table but its last four rows derives from `GateError`, so
`except GateError` catches whatever this client raises of its own and nothing the standard library
raises. The last four are the standard library's, each for a mistake in the calling program, and
none of them answers anything sent: each is raised before a request is made, or by reading what
the client already holds.

```python
from pykeepassgate import api

try:
    client.entry(uuid)
except api.DatabaseLocked:
    ...     # wait, and try again
except api.GateBusy:
    ...     # KeePass is busy, or the database moved under the read: try again shortly
except api.GateUnavailable:
    ...     # any other 503, which `.error` names: not now, and nothing was done
except api.NotAdmitted:
    ...     # never admitted, revoked, or expired. Pair again.
except api.RouteRefused:
    ...     # the rules do not allow this. Ask the operator, not the gate.
```

| Exception | What it means, and what to do |
| --- | --- |
| `DatabaseLocked` | The database is locked. Wait, and try again. Raised two ways. Before the request is sent, when the certificate the handshake was answered with is the one only a locked database has, so nothing is handed over first. And as the gate's `503 locked`, which it answers to every request while the database is locked, and to one whose database locked while the request was on its way, at whichever step the request met the lock: the nodes are still there, and nothing was done. A write whose handler had already run when the lock came is the one exception, and is `500 internal error` instead. |
| `GateBusy` | The gate cannot serve this right now. A write meets it while a dialog is open on what the write would change, while KeePass is part way through something of its own, such as saving, importing, exporting, running XML Replace, or waiting for an answer to a window it put up, when its save would stop to ask the operator something, such as a database file changed on disk, or when other writes kept it waiting more than 20 seconds. A read meets it when the database kept changing while it was being read, which is somebody working in KeePass at that moment. Retry. From `pair` or `renew`, `held_until` set means the operator approved and the gate held the approval until that moment, which has passed: asking again shows the dialog again. |
| `AuthorityUndecided` | The database holds more than one certificate authority, because two copies of it that each made one met in a synchronize, and the operator has not kept one. The gate answers this to every request until somebody chooses at KeePass, which asks on its own screen, so it waits on a person rather than ending on its own. A client of an authority that is not kept stops reaching the database once one is, and pairs again. |
| `GateUnavailable` | The base of the three above, so catching it catches every `503`, and raised on its own for a `503` whose word this client does not know, or that names none. `.error` is the word, and `.retry_after` the delay the gate asked for. No budget waits it out, because nothing on this side knows what ends it. |
| `NotAdmitted` | The gate does not admit this certificate: it was never admitted, it was revoked, or it has expired. One that has expired by this machine's clock, by more than the five minutes a clock running ahead of the gate's is allowed, is named as expired, with the moment, before anything is dialed, rather than left for the gate to refuse as it refuses a stranger. To go on reaching the gate, pair again: a renewal needs a certificate that has not expired. To give the identity up, `revoke` removes what is stored for it, and `forget` from a program. |
| `RouteRefused` | The gate refused it. `.status` says which: `404` is absent or not reachable and nothing can be done, while `400` and `409` are about the request itself, and `413` is something too large to carry, an attachment bigger than this gate hands out or a body bigger than it takes, which `.error` says in a sentence. `500` is none of those: the gate failed, and the operator's KeePass is where to look. After `500 not saved` or `500 internal error` nothing was left written that sending it again would duplicate; after `500 not verified` the change may be on the disk, so look first. `500 not recorded` is told only to `pair` and `renew`: the operator approved, the record did not reach the disk and was taken back out, and nothing was handed over, so asking again puts the dialog in front of the operator again; a renewing client's certificate still works, and a pairing's invitation is spent. |
| `Withheld` | A protected value this client may not read, from every way of reading one: the database protects it, and the rule that lets this client read or list the entry does not grant `P`. The message names the field; the operator can add `P` to that rule. The rest of the answer stands, and `entry.withheld` or `field.withheld` says the same without a `try`: `{name: entry.fields[name] for name in entry.fields if name not in entry.withheld}` reads every value this client may read. |
| `ProofRefused` | While pairing: the code is wrong, or whatever answered is not the KeePass whose operator holds it. Stop. |
| `PairingRefused` | The operator said no. The invitation is spent, so ask for another. |
| `PairingUnanswered` | Nobody answered the dialog. The invitation is **not** spent, so this can be tried again. |
| `PairingError` | The base of the three above, and raised on its own for the rest of what stops a pairing, with a message saying which. A name or a profile the gate would refuse, or more profiles than the 32 it takes, is caught before the request is sent: correct it and pair again. An invitation that is gone ran out or was spent: ask for another. An exchange the gate no longer holds leaves the invitation good: pair again with it. A pairing route the gate does not have is the two halves disagreeing about the wire. |
| `RenewalRefused` | The operator refused this renewal. On the dialog's own turn every refusal reads so, including one for this session, until a date or forever, which the next request is answered with at once, without the dialog. The certificate this client holds still works until it expires: ask again later, or pair again before then. |
| `RenewalRefusedThisSession` | The operator refused every renewal of this certificate until KeePass exits, and the gate keeps that answer in memory only. Ask again after KeePass has been restarted or once the operator has allowed renewal again on the KPG tab's clients page, or pair again before the one this client holds expires. |
| `RenewalTooEarly` | The operator refused renewing this certificate until `.not_before`, and asking before then is answered without anybody being asked. Ask again from then, or once the operator has allowed renewal again on the KPG tab's clients page. |
| `RenewalRefusedForever` | The operator refused every renewal of this certificate. Ask again once the operator has allowed renewal again on the KPG tab's clients page, or pair again, with an invitation from the operator, before the one this client holds expires. |
| `RenewalUnanswered` | Nobody answered the renewal dialog, and nothing changed. Ask again when somebody is at the screen. |
| `RenewalError` | The base of the five above, for a caller that only wants to know whether the operator let the renewal happen. Never raised on its own: whatever else stops a renewal is the exception any route would raise for it. |
| `MalformedToken` | The text handed over is not an invitation, so nothing was sent. Check what was pasted. |
| `ProtocolError` | Something on the wire is not what the protocol allows: an answer this client cannot read, or a request it will not send. Do not retry an answer it cannot read: the two halves disagree about the wire, which `tests/pykeepassgate/test_protocol.py` exists to stop. An answer longer than `max_response_bytes` is refused this way too, and the message says so: raise the setting rather than retry. |
| `NotPaired` | Nothing is stored for a paired client where this looked: nothing has paired under that profile, its identity was given up, or the directory named is not the one the pairing used. Pair, or name the profile or the directory it used. A `StorageError`, so catching that catches this as well. |
| `StorageError` | From `pair`, before anything is sent: the directory cannot hold credentials, and the message says why. Nothing is spent, so fix that and pair again. Afterwards: what is stored for this client will not read, or could not be written, and the message names the file or the directory to look at. A key stored under a password is one of these when no password was given, or the one given does not open it, and the message names `identity.pem`. |
| `TransportError` | No whole answer arrived: nothing is listening, the handshake failed, which a paragraph below explains, or the connection broke or timed out once the request was on its way. After a read, send it again. After a write, the change may have landed, so look before sending it again: read or list what it changed, where the rules allow that, or ask the operator. |
| `ValueError` | A setting or an argument that could not work, refused where it is given and before anything is dialed, stored or spent: a port, a timeout, a retry budget, a response cap, a lifetime, a profile's name, an expiry, a key password or a `dial`. The message names it, and never repeats a password. Correct it in the calling program. |
| `AttributeError` | Something this client never read, which is absence and not emptiness: a listed entry's `username`, `password`, `notes`, `expires` or `custom()`, a listed group's `notes`, a listed field's `value`, or the `expires` of a read that said nothing about it. Read the node first. A withheld value is never this, so `getattr` with a default and `hasattr` cannot turn one into a default. |
| `KeyError` | A name `entry.fields` or `entry.custom()` does not hold, as from any mapping. `get` answers its default for that, and for nothing else. |
| `TypeError` | A `dial` that handed back something other than a connected socket, which is a mistake in the program's own `dial`; what it handed back is closed first, if it can be. A socket it handed back already closed is a `TransportError` saying the socket could not be used, rather than that a handshake failed. |

**The ones that are worth waiting out say so on the wire.** `GateUnavailable` and the three beneath
it are the gate answering `503`, and every `503` it sends carries `Retry-After: 2`. The same number
whichever refusal it is, because how long a lock, an import or a choice between two authorities
lasts is the operator's business rather than the gate's. Nothing waits unless asked to: with no
budget the refusal is raised, and the number is there for a program that decides to come back on
its own. Each is raised before the gate has done anything, which is what makes asking again safe for
the database. On `pair` and `renew` a `GateBusy` can come after the operator approved, when KeePass
was still busy a moment later: then nothing was issued yet, the gate holds the approval for exactly
that request, and `held_until` says until when. Both ask again by themselves until then, every
`Retry-After`, whatever the busy budget says and within the six minutes the gate gives them, and
the approval is written without the dialog. A `GateBusy` with `held_until` that reaches a caller
is one whose moment passed: asking again puts the dialog in front of the operator a second time.

**A node that may not be reached and a node that is not there answer the same way**, so an absent entry
and a forbidden one are not distinguishable from here. That is deliberate: telling them apart would
let a client map a database by asking.

**A failed handshake says why, in the TLS library's words**, and the reasons want different things
done. `certificate has expired` is a database that has stayed locked longer than the ninety days
the certificate a locked database answers with lasts: unlocking it in KeePass mends it. The
authority and the certificate an unlocked database answers with are good for a hundred years, as
[`clients.md`](clients.md#expiry) says, so it is not them. Any other `certificate verify failed` is
something other than the gate answering the port, another program serving TLS there: the gate
answers a client only under the authority that client paired with.

`EOF occurred in violation of protocol` is the gate closing the connection before any certificate,
because nothing it serves answers to the name this client sent. That covers every such state: the
database is not open in KeePass, or has not been unlocked since KeePass started; two copies of it
are open and the gate cannot tell them apart; or **the authority this client paired with has been
recreated or deleted since**, which takes away the key every name this client sends is built from.
Open or unlock the database, or close the copy; after a recreate or a delete, pair again.

## Wait out a refusal instead of reporting it

Three budgets, in seconds, one for each word a `503` is known to carry, and all three are off
unless they are given. Each says how long this client will go on asking again, waiting what the
gate asked for between tries: `retry_busy_timeout` for `GateBusy`, `retry_locked_timeout` for
`DatabaseLocked` and `retry_undecided_timeout` for `AuthorityUndecided`, and on the command line
`--retry-busy-timeout`, `--retry-locked-timeout` and `--retry-undecided-timeout`.

```shell
pykeepassgate status --retry-busy-timeout 30
pykeepassgate pair --name agent-invented --retry-locked-timeout 300
```

```python
client = GateClient.open(retry_busy_timeout=30)

# Or for one call, which overrides what the client was built with. `send` is where a call says
# so, and every method above is one call to it.
client.send("POST", "/groups/root/entries", {"Title": "Nightly"}, retry_busy_timeout=120)
```

**They are separate because the three refusals end differently.** A busy KeePass is in the middle
of something of its own and will finish: saving, importing, a dialog open on what the write would
change. A locked one is waiting for a person to come back to their desk, and the lock can have
arrived on its own from an idle timeout, a minimize, a Windows lock or a suspend. An undecided one
is waiting for a person to keep one of two authorities, which is rare, and can end with this
client's authority not kept, after which nothing it waits for will come. An agent willing to wait
thirty seconds for the first may want to wait an hour for the second, and not at all for the
third.

**Nothing else is retried.** A `404`, a `409`, a `400`, a `500` or a `TransportError` is raised the
moment it arrives: waiting cannot fix any of them, and after two of them a write may have landed,
`500 not verified` and a `TransportError` once the request was on its way, so sending it again is a
decision for the caller rather than for this client. Nor is a `503` whose word none of the three
names, which is `GateUnavailable` itself: nothing on this side knows what ends it.

**Pairing repeats the whole exchange, never half of it.** `api.pair` starts again from the hello,
because a `503` on the pairing routes is answered before the invitation is spent, so the invitation
is still good. What it will not do is send the approval request a second time: that message is the
one whose own completion issues a certificate and writes a record, and a client that resent it
could not know whether the first one had already been approved.

**The budget bounds the waiting, not the calling.** `connection_timeout` is a separate number for a
separate thing, and it bounds each wait on the socket rather than a whole attempt: the dial, the
handshake and each read of the answer, each on its own. So a gate that stops answering is given up
on, and one that goes on answering, however slowly, is not: a program that needs a bound on a
whole call puts one around the call. **A refusal is not always quick either**: a pairing's or a
renewal's `503 busy` comes after the gate waited up to two minutes for the screen, and a write's
after up to twenty seconds for its turn, so a busy budget of ten seconds can hold a pairing for
five attempts of up to two minutes each; the budget counts only the pauses between them.

**A setting that cannot work is refused where it is given**, with a `ValueError` naming it, before
anything is dialed or stored: a budget that is neither `None` nor a finite number of seconds from
zero, a `connection_timeout` that is not a finite number of seconds above zero, and a port outside
1 to 65535. The command line refuses the same ones as wrong arguments, exit 2. A budget of NaN, for
one, would never run out, and the client would ask again for as long as the gate refused. A
`key_password` that is empty or is neither text, bytes nor a callable is refused the same way, by
a message that does not repeat it, and so is one longer than 1023 bytes, text counted as UTF-8,
which is the most a key can be stored and opened under, or text UTF-8 cannot encode.

## Read a larger answer

A client reads at most 4 MiB of one answer, `api.MAX_RESPONSE_BYTES`, so that nothing answering the
socket can make it hold as much memory as it likes. An answer over that fails whole with
`ProtocolError`, whose message names the setting below; nothing is cut short, because a listing
missing its tail would read as a complete one. It is not a `TransportError`: the connection worked,
and asking again reads the same answer to the same cap.

The plugin does not page ([`../API.md`](../API.md) §407), so a listing grows with the database, and a
very large database can outgrow the default. Raise it for that client:

```shell
pykeepassgate groups --max-response-bytes 16777216
```

```python
client = GateClient.open(max_response_bytes=16 * 1024 * 1024)
```

It is a setting of the client, taken by `GateClient`, `GateClient.open` and `api.pair`, and anything
but a positive whole number of bytes is refused with `ValueError` before anything is dialed.

## Test a program against a stand-in gate

`pykeepassgate.testing` is a scripted TLS stand-in for the gate, for testing a program built on this
client without a KeePass: a certificate authority, a host certificate under it, and a TLS 1.3
server on loopback that answers one request per connection and then closes, as the plugin does.
**It answers whatever the test tells it to answer, and it is not a model of the rules.** It knows
nothing of grants, of what a database holds or of how a pairing is approved, so a program tested
against it has been tested against its own script and against nothing else. What it is good for is
what a script can say: that the program sends what it means to, and does the right thing with each
answer and with each refusal this client raises.

```python
from cryptography.x509.oid import ExtendedKeyUsageOID

from pykeepassgate import api
from pykeepassgate.testing import Authority, FakeGate

authority = Authority(directory)            # writes authority.pem into it
host = authority.issue("host", ExtendedKeyUsageOID.SERVER_AUTH, "host.pem")
authority.issue("agent-invented", ExtendedKeyUsageOID.CLIENT_AUTH, "identity.pem")
(directory / "gate.key").write_bytes(bytes(32))

with FakeGate(host, authority.pem) as gate:
    gate.handler = lambda path, body, client: (503, '{"error":"busy"}')
    credentials = api.Credentials(directory, gate.port, "CN=agent-invented", "", (), "", "")
    client = api.GateClient(credentials)
    ...     # whatever the program does, which here meets `GateBusy`
```

A handler is called with the path, the body as text, and the client certificate as DER, or `None`
when none was offered, and answers with the status and the body. `gate.method()`, asked from inside
it, is the request's method, so one path can be answered differently for `GET` and `DELETE`:

```python
def entry(path: str, body: str, client: bytes | None) -> tuple[int, str]:
    if gate.method() == "DELETE":
        return 200, '{"ok":true}'
    return 200, '{"uuid":"..."}'
```

`gate.requests` and `gate.names` record what arrived, and `retry_after`, `declare`, `truncate` and
`linger` make the answers a gate in trouble gives. `held(until)` is the answer the plugin gives a
pairing or a renewal whose approval it holds until a moment, `503 busy` with `heldUntil`, for a
handler to return while a test watches `pair` or `renew` ask again. **Its answers carry the head the plugin's do**:
the reason phrase, `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store`,
`X-Content-Type-Options: nosniff`, and on a `503` `Retry-After: 2`, which `retry_after` changes,
or takes away when it is `None`. The one header it cannot give is the `Allow` the plugin puts on a
`405`. It imports no test framework, and the client's own tests build it through fixtures of their
own.

**One refusal it makes on its own, and differently from the plugin.** A client certificate that is
offered is verified in the handshake, its chain and its dates, because `ssl` cannot ask for one
without: an expired, not yet valid or foreign one ends the handshake, and the client raises
`TransportError`, where the plugin completes the handshake and answers `401`, which the client
raises as `NotAdmitted`. To test what a program does with `NotAdmitted`, script the `401` with a
current certificate.

## What it will not do

**It does not open a `.kdbx`.** There is no file and no master password on this side, by design. Where
a `pykeepass` object was expected, the `spikes/pykeepass-compatibility` branch measures exactly
how far the two can be reconciled: 23 of its 56 public members are servable over the routes the gate
already has, and the object itself is not one of them.

**It has no `save`.** The gate writes when told to, and there is no local copy to flush. A
method that looked like `pykeepass`'s and did something else would be worse than no method.
