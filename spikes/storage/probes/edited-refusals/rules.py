"""The reading rules weighed by this family, and nothing else.

A rule takes one file's dump and answers what the gate would serve from that file: which
authority serves, which clients are admitted, which profile names are on the list, which groups the
recycle bin markers hide, and whether the whole store refuses. `RULES` names every rule.
`scripts/verdicts.py` gives every cell a verdict under every rule in `RULES`, so a rule is added by
writing it here and running that script again, over `recorded/` or a fresh `out/`; no database and
no other file has to change.

A dump is a file as `probe/Program.cs` printed it, rebuilt by `scripts/verdicts.py`:
`groups` holds [label, name, parent label, identifier in hex or None], `deleted` holds
[identifier in hex, label or None, time] for every deletion record, and `entries` holds one item
per entry version: `u` the entry's label, `g` its group's label, `slot` (`cur` for the current
version, `h0`, `h1` and on for history items), `title`, `s` every string other than the five
standard fields, by name, and `canon`, the SHA-256 that the check is computed over.
"""

import hashlib
import hmac
import collections
import json
from dataclasses import dataclass, field

# The field of `KPG.Data` that names what a record is about, for each kind of record that refuses.
REFUSING = {"retired": "authority", "revoked": "certificate", "profile-removed": "name", "recycle-bin": "group"}
# The fields a record of each kind needs before it reads as that record at all.
NEEDS = {
    "authority": ("fp", "storeKey"),
    "client": ("certificate", "subject", "authority", "profiles"),
    "profile-added": ("name", "act", "seen"),
    "retired": ("authority",),
    "revoked": ("certificate",),
    "profile-removed": ("name", "act"),
    "recycle-bin": ("group",),
}


@dataclass
class State:
    serving: list = field(default_factory=list)   # fingerprints of the authorities no retirement found retires
    undecided: bool = False                       # more than one serves: 503 undecided, nothing served
    admitted: list = field(default_factory=list)  # subjects of the clients admitted
    names: list = field(default_factory=list)     # profile names on the list
    hidden: list = field(default_factory=list)    # labels of the groups a recycle bin marker hides
    store_refuses: bool = False                   # the whole store refuses: nothing served


def record(v):
    """The version's kind and data when it reads as a record of a known kind, otherwise None."""
    kind = v["s"].get("KPG.Kind")
    if kind not in NEEDS:
        return None
    try:
        data = json.loads(v["s"]["KPG.Data"])
    except (KeyError, ValueError):
        return None
    if not isinstance(data, dict) or any(f not in data for f in NEEDS[kind]):
        return None
    return kind, data


def verifies(v, key):
    """Whether `KPG.Mac` is HMAC-SHA256 under `key` over `canon`."""
    if key is None:
        return False
    mac = hmac.new(key, bytes.fromhex(v["canon"]), hashlib.sha256).hexdigest()
    return hmac.compare_digest(mac, v["s"].get("KPG.Mac", ""))


def witness(key, kind, target):
    """The identifier of the deletion record that witnesses a revocation or a retirement."""
    digest = hmac.new(key, f"KPG-witness/1/{kind}/{target}".encode(), hashlib.sha256).digest()
    return digest[:16].hex().upper()


def read(dump, history=False, check=False, strict=False, witnesses=False):
    """EVALUATION.md 4.4's read, over current versions wherever they sit, with four switches.

    Always: an authority counts when its own check verifies under the store key in its document,
    and copies with one fingerprint count once; exactly one authority not retired serves, more than
    one is `503 undecided`; what grants (an admission record, a profile addition) counts only when
    its check verifies under the serving authority's store key, in its current version.

    history:   a refusal is also found in history items.
    check:     a refusal counts only when its check verifies: a retirement under the store key of the
               authority it names, every other refusal under the serving authority's.
    strict:    a refusal whose check fails, or a store entry (one in a group named `KeePassGate`,
               or one carrying a string whose name begins `KPG.`) that reads as no record, makes the
               whole store refuse.
    witnesses: a revocation or a retirement also counts when only its witness is found.
    """
    current = [v for v in dump["entries"] if v["slot"] == "cur"]
    looked = dump["entries"] if history else current
    deleted = {d[0].upper() for d in dump["deleted"]}
    store_groups = {g[0] for g in dump["groups"] if g[1] == "KeePassGate"}
    state = State()

    keys = {}
    for v in current:
        r = record(v)
        if r and r[0] == "authority":
            key = bytes.fromhex(r[1]["storeKey"])
            if verifies(v, key):
                keys[r[1]["fp"]] = key

    def counts(v, key):
        if not check or verifies(v, key):
            return True
        if strict:
            state.store_refuses = True
        return False

    if strict:
        for v in current:
            if (v["g"] in store_groups or any(n.startswith("KPG.") for n in v["s"])) and record(v) is None:
                state.store_refuses = True

    def found(kind, key):
        for v in looked:
            r = record(v)
            if r and r[0] == kind and counts(v, key(r[1])):
                yield r[1]

    retired = {d["authority"] for d in found("retired", lambda d: keys.get(d["authority"]))}
    if witnesses:
        retired |= {fp for fp, key in keys.items() if witness(key, "retired", fp) in deleted}
    state.serving = sorted(fp for fp in keys if fp not in retired)
    if len(state.serving) != 1:
        state.undecided = len(state.serving) > 1
        return state
    fp = state.serving[0]
    key = keys[fp]

    revoked = {d["certificate"] for d in found("revoked", lambda d: key)}
    removals = [(d["name"], d["act"]) for d in found("profile-removed", lambda d: key)]
    markers = {d["group"].upper() for d in found("recycle-bin", lambda d: key)}

    admitted = set()
    additions = []
    for v in current:
        r = record(v)
        if not r or not verifies(v, key):
            continue
        kind, d = r
        if kind == "client" and d["authority"] == fp:
            if witnesses and witness(key, "revoked", d["certificate"]) in deleted:
                revoked.add(d["certificate"])
            if d["certificate"] not in revoked:
                admitted.add(d["subject"])
        elif kind == "profile-added":
            additions.append((d["name"], set(d["seen"])))
    state.admitted = sorted(admitted)
    state.names = sorted({n for n, seen in additions if all(act in seen for m, act in removals if m == n)})
    state.hidden = sorted(g[0] for g in dump["groups"] if g[3] and g[3].upper() in markers)
    return state


def read_with_history(dump):
    """R6: every record carries the check; a record counts in its current version when that
    version passes, or else in its newest history item that passes; a store entry with no passing
    version anywhere makes the whole store refuse. No witness.

    "Passes" is the check of R3: an authority under its own store key, a retirement under the key of
    the authority it names, every other record under the serving authority's key. It holds for what
    grants as well as for what refuses. A store entry is as in R4, judged by its current version.
    """
    versions = collections.OrderedDict()
    for v in dump["entries"]:
        versions.setdefault(v["u"], []).append(v)
    for vs in versions.values():
        # the current version first, then history items newest first (`h0` is the oldest)
        vs.sort(key=lambda v: -1 if v["slot"] == "cur" else -int(v["slot"][1:]))
        vs.sort(key=lambda v: 0 if v["slot"] == "cur" else 1)
    store_groups = {g[0] for g in dump["groups"] if g[1] == "KeePassGate"}
    state = State()

    def counted(u, key_of):
        for v in versions[u]:
            r = record(v)
            if r and verifies(v, key_of(*r)):
                return r
        return None

    def own(kind, d):
        return bytes.fromhex(d["storeKey"]) if kind == "authority" else None

    keys = {}
    for u in versions:
        r = counted(u, own)
        if r:
            keys[r[1]["fp"]] = bytes.fromhex(r[1]["storeKey"])

    def named(kind, d):
        return keys.get(d["authority"]) if kind == "retired" else own(kind, d)

    retired = set()
    for u in versions:
        r = counted(u, named)
        if r and r[0] == "retired":
            retired.add(r[1]["authority"])
    state.serving = sorted(fp for fp in keys if fp not in retired)
    if len(state.serving) != 1:
        state.undecided = len(state.serving) > 1
        return state
    fp = state.serving[0]
    key = keys[fp]

    def full(kind, d):
        return named(kind, d) if kind in ("authority", "retired") else key

    found = collections.defaultdict(list)
    for u, vs in versions.items():
        r = counted(u, full)
        if r is None:
            cur = [v for v in vs if v["slot"] == "cur"]
            if cur and (cur[0]["g"] in store_groups or any(n.startswith("KPG.") for n in cur[0]["s"])):
                state.store_refuses = True
            continue
        found[r[0]].append(r[1])
    revoked = {d["certificate"] for d in found["revoked"]}
    removals = [(d["name"], d["act"]) for d in found["profile-removed"]]
    markers = {d["group"].upper() for d in found["recycle-bin"]}
    state.admitted = sorted({d["subject"] for d in found["client"]
                             if d["authority"] == fp and d["certificate"] not in revoked})
    additions = [(d["name"], set(d["seen"])) for d in found["profile-added"]]
    state.names = sorted({n for n, seen in additions if all(act in seen for m, act in removals if m == n)})
    state.hidden = sorted(g[0] for g in dump["groups"] if g[3] and g[3].upper() in markers)
    return state


RULES = {
    # R1: a refusal counts if an entry with its kind and target is found; no check; current versions.
    "R1": lambda d: read(d),
    # R2: as R1, and history items count too.
    "R2": lambda d: read(d, history=True),
    # R3: every record carries a check; a record whose check fails is ignored.
    "R3": lambda d: read(d, check=True),
    # R4: as R3, but a refusal whose check fails, or a store entry that reads as no record, makes the
    # whole store refuse.
    "R4": lambda d: read(d, check=True, strict=True),
    # R5: as R3, and a revocation or a retirement also counts when only its witness is found.
    "R5": lambda d: read(d, check=True, witnesses=True),
    # R6: R2 and R4 joined: the current version when it passes, else the newest history item that
    # passes; a store entry with no passing version anywhere makes the whole store refuse.
    "R6": lambda d: read_with_history(d),
}
