"""The recommended store's read rule, once, over a dump of a real file (kp.Host.dump).

Design 5.2 with EVALUATION.md 4.1: every entry counts wherever it sits, the recycle bin included
(change 1); there is no limit of 64 (change 2); an entry that fails its check and is not under a
plugin identifier is passed over and counted, never damage (change 3); a revocation refuses the
client's key (change 5); a retirement may carry the store key (change 6); no witness, no mark
(decisions 15 and 11), so a node with no rule inherits. It knows nothing of the model.

read(dump) answers: status (ok, incomplete, nostore), the authority served, the clients admitted
with their profiles, the profile names, the words each user node gives, the renewal answers, and
counts of what it found.

ADDED (decided store): read(dump, history=True) is the store as decided on 2026-11-16, the history
rule of EVALUATION.md 4.3 (decision 7), over a dump that carries each entry's history items: a
record counts in its current version when that version passes the check, or else in its newest
history item that passes, and only an entry with no passing version is damage. A store key is
found in any version of an authority or a retirement. Without it, the reading is that of the run
with the repair had, and its answers are unchanged.

ADDED (two readings): the answer also names, under `byhist`, each entry that counts by a history
item and that item's index, oldest first, as the dump numbers it, and under `held` the store keys
the file holds; neither is printed by a part that existed before. read(dump, history, copies) with
`copies` set is the reading of a copy under another identifier: a record that can only take access
away counts from any entry whose content passes the check under a key held, whatever the entry's
identifier, as the record under the identifier its content gives. Such a record is a revocation, a
retirement (its refusal; a store key is never taken from a copy), a profile removal, and a version
whose value refuses everything (a rule of nothing, renewals refused forever). An admission, a
profile addition, an authority, a version of any other value and a summary entry never count from
a copy. With `copies` 'held' a record counted so is held to the completeness check like any other
(a summary entry has to restate it); with 'exempt' it is not. `fromcopy` names each, under the
identifier its content gives, with the copy it was read from.
"""
import json

import store as S

ALL = frozenset('rw')
IDENT = ('cap', ALL)
REFUSE = ('const', frozenset())


def meet(f, g):
    return ('const' if f[0] == 'const' and g[0] == 'const' else 'cap', f[1] & g[1])


def app(f, x):
    return f[1] if f[0] == 'const' else f[1] & x


def strict(text):
    """One flat JSON object of strings, nothing else."""
    d = json.loads(text)
    if not isinstance(d, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in d.items()):
        raise ValueError('not a flat object of strings')
    return d


class Cand:
    # ADDED (decided store): `versions`, every version of the entry the reading may count, the
    # current first and then the history items newest first; `by_history`, counted by a history item
    __slots__ = ('u', 'kind', 'data', 'd', 'mac', 'extra', 'key', 'place', 'versions', 'by_history',
                 'hidx', 'copy')     # ADDED (two readings): the history item counted; the copy read


def version(s):
    """A version of an entry: its kind, data, check and summary strings, the data parsed."""
    c = Cand()
    c.kind, c.data, c.mac = s.get('KPG.Kind'), s.get('KPG.Data'), s.get('KPG.Mac')
    c.extra = {k: v for k, v in s.items() if k.startswith('KPG.S.')}
    try:
        c.d = strict(c.data) if c.data is not None else None
    except ValueError:
        c.d = None
    return c


def take(c, v):
    """The candidate reads as version v."""
    c.kind, c.data, c.mac, c.extra, c.d = v.kind, v.data, v.mac, v.extra, v.d


def candidates(dump, history=False):
    out = []
    for u, e in dump['entries'].items():
        s = e['strings']
        hist = [h['strings'] for h in reversed(e.get('history', []))] if history else []
        if not any(k.startswith('KPG.') for x in [s] + hist for k in x):
            continue
        c = version(s)
        c.u, c.place, c.key, c.by_history = u, e['place'], None, False
        c.hidx, c.copy = None, None     # ADDED (two readings)
        c.versions = [version(x) for x in [s] + hist]
        out.append(c)
    return out


def store_entries(dump, history=False):
    """ADDED (decided store): the entries the reading takes as the store's candidates."""
    return set(c.u for c in candidates(dump, history))


def verifies(c, key):
    if c.kind not in S.KINDS or c.d is None or c.mac is None:
        return False
    try:
        m = S.mac(key, c.kind, c.data, c.extra)
        height = int(c.d['height']) if c.kind == 'head' else 0
        return m == c.mac and c.u == S.ident(key, c.kind, S.subject_of(c.kind, c.d), height, m)
    except (KeyError, ValueError):
        return False


def heads_items(h):
    return [json.loads(v) for k, v in sorted(h.extra.items())]


def read(dump, history=False, copies=None):     # ADDED (two readings): `copies`
    """ADDED (decided store): `history` is the history rule of EVALUATION.md 4.3 (decision 7): a
    record counts in its current version when that version passes the check, or else in the
    newest history item that passes; a store entry with no passing version anywhere is damage and
    stops the whole database. Without it, the reading of the run with the repair: an entry under a
    plugin identifier whose current version fails is damage. The dump needs `history` too."""
    cands = candidates(dump, history)
    dels = dump['dels']
    named = set(dels)
    # The store keys: an authority verifies under the key its own document holds; a retirement that
    # carries a key verifies under that key (change 6). With the history rule, in any version.
    held = []
    for c in cands:
        for v in (c.versions if history else [c]):
            if v.kind in ('authority', 'retired') and v.d and 'storeKey' in v.d:
                try:
                    k = bytes.fromhex(v.d['storeKey'])
                except ValueError:
                    continue
                v.u = c.u
                if verifies(v, k) and k not in held:
                    held.append(k)
    els, passed, damage, by_history = [], 0, 0, 0
    for c in cands:
        for n, v in enumerate(c.versions if history else [c]):
            v.u = c.u
            for k in held:
                if verifies(v, k):
                    c.key = k
                    break
            if c.key is not None:
                if n:
                    take(c, v)
                    c.by_history = True
                    by_history += 1
                    c.hidx = len(c.versions) - 1 - n     # ADDED (two readings)
                break
        if c.key is not None:
            els.append(c)
        elif any(c.u[:6] == S.tag(k, kind) for k in held for kind in S.KINDS):
            damage += 1             # under a plugin identifier and failing: damage
        else:
            passed += 1             # passed over and shown (change 3)
    # ADDED (two readings): a record that can only take access away, counted from a copy under
    # another identifier, as the record under the identifier its content gives
    fromcopy = {}
    if copies:
        have = set(c.u for c in els)
        for c in cands:
            if c.key is not None:
                continue
            for v in (c.versions if history else [c]):
                if v.kind not in ('revoked', 'retired', 'removed', 'version') or v.d is None or v.mac is None:
                    continue
                if v.kind == 'version' and v.d.get('doc') != 'const:':
                    continue
                canon = None
                for k in held:
                    try:
                        if S.mac(k, v.kind, v.data, v.extra) == v.mac:
                            canon = (S.ident(k, v.kind, S.subject_of(v.kind, v.d), 0, v.mac), k)
                            break
                    except (KeyError, ValueError):
                        continue
                if canon is None:
                    continue
                if canon[0] not in have:
                    a = Cand()
                    a.u, a.place, a.key, a.by_history, a.hidx, a.copy = canon[0], c.place, canon[1], False, None, c.u
                    a.versions = [v]
                    take(a, v)
                    els.append(a)
                    have.add(canon[0])
                    fromcopy[canon[0]] = c.u
                break
    by = {}
    for c in els:
        by.setdefault(c.kind, []).append(c)

    # The keyed tag of each kind under each key held, once, so that a record is read by lookup.
    tags = {}
    for k in held:
        for kind in S.KINDS:
            tags.setdefault(S.tag(k, kind), []).append((kind, k))

    def tagged(u, kind):
        return [k for (kd, k) in tags.get(u[:6], ()) if kd == kind]

    heads = by.get('head', [])
    heads_live = [h for h in heads if h.u not in named]
    refusals, prem = set(), set()
    for c in by.get('revoked', []):
        refusals.add(('revoked', c.d['client']))
    for c in by.get('retired', []):
        refusals.add(('retired', c.d['authority']))
    for c in by.get('removed', []):
        prem.add((c.d['name'], c.u))
    hv = []
    for h in heads:
        for it in heads_items(h):
            if it['kind'] == 'revoked':
                refusals.add(('revoked', it['client']))
            elif it['kind'] == 'retired':
                refusals.add(('retired', it['authority']))
            elif it['kind'] == 'removed':
                prem.add((it['name'], it['id']))
    # versions: entries not named by a record; and what the heads in force restate
    vers, supers = {}, set()
    for c in by.get('version', []):
        if c.u in named:
            supers.add(c.d['object'])
        else:
            vers.setdefault(c.d['object'], {})[c.u] = (frozenset(c.d['after'].split()), S.undoc(c.d['doc']))
    for h in heads_live:
        for it in heads_items(h):
            if it['kind'] == 'version':
                vers.setdefault(it['object'], {})[it['id']] = (frozenset(it['after'].split()), S.undoc(it['doc']))
    # what a deletion record still tells, by its keyed tag and hash (design 5.2 points 3 and 4)
    clients = set(c.d['client'] for c in by.get('client', [])) | set(s for (k, s) in refusals if k == 'revoked')
    authorities = set(c.d['id'] for c in by.get('authority', [])) | set(s for (k, s) in refusals if k == 'retired')
    names = set(c.d['name'] for c in by.get('profile', []) + by.get('removed', [])) | set(n for (n, i) in prem)
    objects = set(vers) | supers | set('renew:' + c for c in clients)
    for g in dump['groups']:
        objects.add('rule:' + g)
    objects.add('rule:' + dump['root'])
    for u, e in dump['entries'].items():
        objects.add('rule:' + u)
    deadall = set()
    for V in vers.values():
        for after, f in V.values():
            deadall |= after
    lost = {}
    # The keyed hash of each subject the file names, once per key: a record's subject by lookup.
    subj = {}
    for k in held:
        for cl in clients:
            subj[(k, S.subject_hash(k, 'client:' + cl))] = ('client', cl)
        for a in authorities:
            subj[(k, S.subject_hash(k, 'authority:' + a))] = ('authority', a)
        for n in names:
            subj[(k, S.subject_hash(k, 'profile:' + n))] = ('profile', n)
        for o in objects:
            subj[(k, S.subject_hash(k, o))] = ('object', o)
    for u in dels:
        for kind, k in tags.get(u[:6], ()):
            what = subj.get((k, u[6:16]))
            if what is None:
                continue
            if kind == 'revoked' and what[0] == 'client':
                refusals.add(('revoked', what[1]))
            elif kind == 'retired' and what[0] == 'authority':
                refusals.add(('retired', what[1]))
            elif kind == 'removed' and what[0] == 'profile':
                prem.add((what[1], u))
            elif kind == 'version' and what[0] == 'object':
                o = what[1]
                if u not in deadall and u not in vers.get(o, {}):
                    lost.setdefault(o, set()).add(u)
    # whole or incomplete (point 5)
    store = bool(els) or any(tagged(u, 'head') for u in dels)
    status = 'ok'
    if not heads_live:
        status = 'incomplete' if store else 'nostore'
    else:
        known = set(h.u for h in heads_live)
        for h in heads_live:
            known |= set(h.d['after'].split())
        if any(tagged(u, 'head') and u not in known for u in dels):
            status = 'incomplete'
    if status == 'ok' and damage:
        status = 'incomplete'
    if status == 'ok':
        c_ref, c_prem, c_regs = set(), set(), set()
        for h in heads:
            for it in heads_items(h):
                if it['kind'] == 'revoked':
                    c_ref.add(('revoked', it['client']))
                elif it['kind'] == 'retired':
                    c_ref.add(('retired', it['authority']))
                elif it['kind'] == 'removed':
                    c_prem.add((it['name'], it['id']))
                elif it['kind'] == 'version':
                    c_regs.add(it['id'])
        def held_to(c):     # ADDED (two readings): with 'exempt', a record from a copy is not
            return c.copy is None or copies != 'exempt'

        for c in by.get('revoked', []):
            if ('revoked', c.d['client']) not in c_ref and held_to(c):
                status = 'incomplete'
        for c in by.get('retired', []):
            if ('retired', c.d['authority']) not in c_ref and held_to(c):
                status = 'incomplete'
        for c in by.get('removed', []):
            if (c.d['name'], c.u) not in c_prem and held_to(c):
                status = 'incomplete'
        for c in by.get('version', []):
            if c.u not in named and c.u not in deadall and c.u not in c_regs and held_to(c):
                status = 'incomplete'
    # the authority served (two standing: 503 undecided)
    auths = sorted(c.d['id'] for c in by.get('authority', []) if c.u not in named and ('retired', c.d['id']) not in refusals)
    served = auths[0] if len(auths) == 1 and status == 'ok' else None
    # names: an addition counts when it has seen every removal of its name the file knows
    on = set()
    for c in by.get('profile', []):
        if c.u not in named and set(i for (n, i) in prem if n == c.d['name']) <= set(c.d['after'].split()):
            on.add(c.d['name'])
    valid, adm = {}, {}
    for c in by.get('client', []):
        if c.u in named:
            continue
        if ('revoked', c.d['client']) in refusals or ('retired', c.d['issuer']) in refusals:
            continue
        profs = frozenset(c.d['profiles'].split()) & on
        valid[c.d['certificate']] = profs
        if served == c.d['issuer']:
            adm[c.d['certificate']] = profs

    def freg(o):
        if o in lost:
            return REFUSE
        V = vers.get(o)
        if not V:
            return REFUSE if o in supers else None
        dead = set()
        for after, f in V.values():
            dead |= after
        f = None
        for i, (after, g) in V.items():
            if i not in dead:
                f = g if f is None else meet(f, g)
        return f

    # the user tree: groups and entries outside the plugin's group, each walked from the root
    parent = {g: v['parent'] for g, v in dump['groups'].items()}
    for u, e in dump['entries'].items():
        parent[u] = e['parent']
    memo = {}

    def words(n):
        if n in memo:
            return memo[n]
        f = freg('rule:' + n) or IDENT
        up = frozenset() if n == dump['root'] else words(parent[n])
        memo[n] = app(f, up)
        return memo[n]

    raw = {}
    mine = set(c.u for c in cands)      # ADDED (decided store): with history, an entry with a KPG.
    for n in [dump['root']] + sorted(dump['groups']) + sorted(u for u, e in dump['entries'].items()
                                                              if not any(k.startswith('KPG.') for k in e['strings'])
                                                              and u not in mine):
        raw[n] = words(n)
    serve = status == 'ok' and served is not None
    renew = {}
    for cl in sorted(clients):
        f = freg('renew:' + cl)
        renew[cl] = 'forever' if f is not None and not app(f, ALL) else 'ask'
    counts = dict(elements=len(els), passed=passed, damage=damage, heads=len(heads),
                  heads_live=len(heads_live), records=len(dels), keys=len(held))
    if history:     # ADDED (decided store): records that count by a history item
        counts['byhistory'] = by_history
    if copies:      # ADDED (two readings): records that count from a copy
        counts['fromcopy'] = len(fromcopy)
    return dict(status=status, served=served, auths=auths, adm=adm if serve else {}, valid=valid,
                names=sorted(on), words={n: (w if serve else frozenset()) for n, w in raw.items()},
                words_raw=raw, renew=renew, refusals=refusals, counts=counts,
                # ADDED (two readings)
                byhist={c.u: c.hidx for c in els if c.by_history and c.copy is None},
                held=list(held), fromcopy=fromcopy)
