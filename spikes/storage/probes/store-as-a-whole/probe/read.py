"""The recommended store's read rule, once, over a dump of a real file (kp.Host.dump).

Design 5.2 with EVALUATION.md 4.1: every entry counts wherever it sits, the recycle bin included
(change 1); there is no limit of 64 (change 2); an entry that fails its check and is not under a
plugin identifier is passed over and counted, never damage (change 3); a revocation refuses the
client's key (change 5); a retirement may carry the store key (change 6); no witness, no mark
(decisions 15 and 11), so a node with no rule inherits. It knows nothing of the model.

read(dump) answers: status (ok, incomplete, nostore), the authority served, the clients admitted
with their profiles, the profile names, the words each user node gives, the renewal answers, and
counts of what it found.
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
    __slots__ = ('u', 'kind', 'data', 'd', 'mac', 'extra', 'key', 'place')


def candidates(dump):
    out = []
    for u, e in dump['entries'].items():
        s = e['strings']
        if not any(k.startswith('KPG.') for k in s):
            continue
        c = Cand()
        c.u, c.kind, c.data, c.mac = u, s.get('KPG.Kind'), s.get('KPG.Data'), s.get('KPG.Mac')
        c.extra = {k: v for k, v in s.items() if k.startswith('KPG.S.')}
        c.place, c.key = e['place'], None
        try:
            c.d = strict(c.data) if c.data is not None else None
        except ValueError:
            c.d = None
        out.append(c)
    return out


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


def read(dump):
    cands = candidates(dump)
    dels = dump['dels']
    named = set(dels)
    # The store keys: an authority verifies under the key its own document holds; a retirement that
    # carries a key verifies under that key (change 6).
    held = []
    for c in cands:
        if c.kind in ('authority', 'retired') and c.d and 'storeKey' in c.d:
            try:
                k = bytes.fromhex(c.d['storeKey'])
            except ValueError:
                continue
            if verifies(c, k) and k not in held:
                held.append(k)
    els, passed, damage = [], 0, 0
    for c in cands:
        for k in held:
            if verifies(c, k):
                c.key = k
                break
        if c.key is not None:
            els.append(c)
        elif any(c.u[:6] == S.tag(k, kind) for k in held for kind in S.KINDS):
            damage += 1             # under a plugin identifier and failing: damage
        else:
            passed += 1             # passed over and shown (change 3)
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
        for c in by.get('revoked', []):
            if ('revoked', c.d['client']) not in c_ref:
                status = 'incomplete'
        for c in by.get('retired', []):
            if ('retired', c.d['authority']) not in c_ref:
                status = 'incomplete'
        for c in by.get('removed', []):
            if (c.d['name'], c.u) not in c_prem:
                status = 'incomplete'
        for c in by.get('version', []):
            if c.u not in named and c.u not in deadall and c.u not in c_regs:
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
    for n in [dump['root']] + sorted(dump['groups']) + sorted(u for u, e in dump['entries'].items()
                                                              if not any(k.startswith('KPG.') for k in e['strings'])):
        raw[n] = words(n)
    serve = status == 'ok' and served is not None
    renew = {}
    for cl in sorted(clients):
        f = freg('renew:' + cl)
        renew[cl] = 'forever' if f is not None and not app(f, ALL) else 'ask'
    return dict(status=status, served=served, auths=auths, adm=adm if serve else {}, valid=valid,
                names=sorted(on), words={n: (w if serve else frozenset()) for n, w in raw.items()},
                words_raw=raw, renew=renew, refusals=refusals,
                counts=dict(elements=len(els), passed=passed, damage=damage, heads=len(heads),
                            heads_live=len(heads_live), records=len(dels), keys=len(held)))
