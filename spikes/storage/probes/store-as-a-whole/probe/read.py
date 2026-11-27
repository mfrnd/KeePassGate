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

ADDED (copies extended): read(dump, history, copies, ext) with `ext` set is a wider reading of a
copy, in which every record a copy gives is applied only as far as it can take away, whatever else
the file holds. `ext` is a dict:

  rules   how a copied rule version or renewal answer limits the object it names. A copy never
          joins the object's versions: it replaces nothing and grants nothing. It limits unless a
          version the file reads, or another passing copy, names it as replaced.
          'nothing'  only a copy that refuses everything limits (the reading measured before)
          'profile'  per profile the copy names: the copy limits that profile where what it grants
                     is within what the file's own rule of that node grants the profile (where
                     the file's own rule does not name the profile: only a copy granting nothing)
          'verb'     per profile the copy names: every word the copy does not grant is taken away
          'cap'      the same, and a profile the copy does not name gets at most what arrives from
                     above (the copy met with the file's own rule as two versions made apart are)
  settle  True: a retirement read from a copy can leave a single authority standing, which is
          then served (the reading measured before). False: it refuses the authority it names
          and that authority's clients, and never makes another authority served.
  heads   True: a copy of a summary entry gives its refusal items and, as copies of versions, its
          version items; never its presence, which would complete the store.
  key     True: a store key is taken from a copy of an authority or of a retirement.
  among   False: a copy that another passing copy names as replaced still limits (True unless set).
  names   read by bridge.py: a rule version the gate writes names, as replaced, the copies of
          that object's versions the file reads (True unless set).

A summary entry may carry items of kind `copy`, written by a Seal again that acknowledged copies:
a copy named there is not held to the completeness check. PP is set by the configurations that
write rules per profile (store.py's 'pp:' documents); every word is then a profile's letter.
"""
import json

import store as S

ALL = frozenset('rw')
IDENT = ('cap', ALL)
REFUSE = ('const', frozenset())
PP = False      # ADDED (copies extended): rules per profile
UNIV = frozenset(p + '.' + v for p in S.PROFS for v in 'rw')
# ADDED (rules per profile): a rule stored as a record for each node and profile, 'rule:<node>/<p>',
# and the node's switch as a record of its own, 'switch:<node>' (crdt_model.ROWS). A node's rule is
# put together from them as crdt_model.rows_node does. A deletion record of a row's version is told
# by trying, for each node of the tree, each profile name the file knows (store.ID_FORM 'pair'), or,
# in the form 'split', each node once and each name once.
ROWS = False
SHOW = False    # the answer also lists the standing versions of every rule object, in either form
# ADDED (owed forms): the letters a row may hold, and how a deletion record of a row's version is
# told in the form 'split' where the tags of several names the file knows meet. The form was run
# with a dictionary from a tag to a name, filled from a set of names, so that it kept whichever name
# the set yielded last, an order that changes with the process's hash seed. Here the names are
# sorted, and 'first' or 'last' keeps the first or the last in ordinal order, the two answers that
# reading can give for two names; 'each' tells it for every such name, and then only where no
# version present or restated carries its identifier
LETTERS = 'rw'
TAG_READ = 'last'
# ADDED (lost settled): how a deletion record of a version whose content is nowhere is answered, as
# crdt_model.JUDGE: None, the reading above; 'union', answered for every object it tells of once a
# version in force of any object names it in `after`; 'object', answered for an object only where a
# version in force of that object names it in `after`, a version present beside its deletion record
# told of by its own object alone; 'lost', answered for every object it tells of where a version in
# force of an object it tells of names it in `after`, and for an object alone where a version in
# force of that object names it in `lost`, a version present beside its deletion record told of by
# its own object and answered where a version in force of it names it in `after` or in `lost`. With
# JUDGE set, a deletion record under a tag tells of every name of the tag the file knows, whatever
# TAG_READ holds, and under a tag of 0 of the object `rule:` and the node with no slash as well,
# where the file holds a version of it.
JUDGE = None
# ADDED (renewal answers): the standing answers to a renewal, STORE.md 618 and its S7. A version
# of `renew:` and a client key holds `refuse:forever`, `refuse:until:` and a moment, RFC 3339 in UTC to
# the second, or `ask`, read strictly here and by nothing of the model: a text is in form only where
# composing what was read gives the same bytes. The answer is the strictest standing version, the
# later moment the stricter; a lost version and a text out of form refuse forever. The answer names
# every client key the file knows, from its admissions and revocations and from the object of every
# version present or restated. No clock is read: `renew` holds `ask`, `until:` and the moment, or
# `forever`, and `renew_detail` the cause and what stands.
ANSWERS = False
ANSWER_FORMAT = '%Y-%m-%dT%H:%M:%SZ'
# ADDED (seal again): the answer also holds `losses`, what a stopped store lost as PLAN 3.1 of the
# pull request that builds Seal again lists it for the seal's dialog and the seal's act: every
# condition of STORE.md 624 that holds, not only the first; each entry that is damage, by its
# identifier, the kind its tag names and its title; every deletion record that tells of a summary
# entry and that no summary entry in force names; each revocation, retirement and removal in force by
# a deletion record alone; each record no summary entry restates; each object a lost version makes
# refuse, with the deletion records; each object the deletion record of a damaged version would make
# refuse once the seal has deleted it; and `seal`, the versions granting nothing the seal writes,
# each with its object, its text, `after` and `lost`, composed from this reading alone.
SEAL = False
# ADDED (started again): the keys of STORE.md 649, the version record of 650 and the room of 643,
# read here as the product reads them and by nothing of the model. With STARTS, a keep record (kind
# `kept`, naming a key by its name, store.keyname) counts as a record, as an item of a summary entry
# present and as a deletion record under the tag of `kept` and the subject hash of `key:` and a name,
# under a key found and naming another; which keys stand is marked as the product marks them
# (`mark_keys`); a single standing key is the store key and only what passes under it counts, damage
# under its tags alone; several standing keys are a split store; a version record or a summary entry's
# item `format` above 1 makes the store outdated; a keep record or a version record no summary entry
# restates stops it. With ROOM, a store whose next summary entry does not fit the product's ceilings
# (StoreFormat.MaxWritten for its content, MaxItems for its items) is stopped, after every other check.
STARTS = False
ROOM = False
MAX_WRITTEN, MAX_ITEMS = 262144, 9999


def mark_keys(keys, rels):
    """ADDED (started again): the keys that stand and the keys set aside, as the product marks them:
    a relation another names in `after` counts for nothing; a key no counting relation names, or only
    relations under keys set aside, stands; a key a counting relation under a standing key names is
    set aside; unmarked keys on a circle of counting relations stand; again until a round marks
    nothing. `rels` is (source, target or None, identifier, after)."""
    named = set()
    for r in rels:
        named |= r[3]
    edges = set((r[0], r[1]) for r in rels if r[1] is not None and r[2] not in named)
    mark = {k: None for k in keys}
    while True:
        changed = True
        while changed:
            changed = False
            for k in keys:
                if mark[k] is not None:
                    continue
                into = [s for (s, t) in edges if t == k]
                if any(mark[s] == 'standing' for s in into):
                    mark[k], changed = 'aside', True
                elif all(mark[s] == 'aside' for s in into):
                    mark[k], changed = 'standing', True
        loose = [k for k in keys if mark[k] is None]

        def back(k):
            reached, todo = set(), [k]
            while todo:
                x = todo.pop()
                for (s, t) in edges:
                    if s == x and mark[t] is None:
                        if t == k:
                            return True
                        if t not in reached:
                            reached.add(t)
                            todo.append(t)
            return False
        circled = [k for k in loose if back(k)]
        if not circled:
            break
        for k in circled:
            mark[k] = 'standing'
    return [k for k in keys if mark[k] != 'aside'], [k for k in keys if mark[k] == 'aside']


def answer_text(text):
    """ADDED (renewal answers): ('forever',), ('until', moment), ('ask',), or None out of form."""
    import datetime
    if text == 'refuse:forever':
        return ('forever',)
    if text == 'ask':
        return ('ask',)
    if isinstance(text, str) and text.startswith('refuse:until:'):
        m = text[13:]
        try:
            ok = datetime.datetime.strptime(m, ANSWER_FORMAT).strftime(ANSWER_FORMAT) == m
        except ValueError:
            ok = False
        if ok:
            return ('until', m)
    return None


def answer_key_form(key):
    """ADDED (renewal answers): a client key as the product writes it, 64 lowercase hexadecimal
    characters. With store.HEX off the model's names pass, so that the family's runs read as before."""
    if not S.HEX:
        return True
    return len(key) == 64 and all(c in '0123456789abcdef' for c in key)


def answer_order(text):
    """ADDED (renewal answers): the stricter the greater; out of form the strictest."""
    a = answer_text(text)
    if a is None:
        return (3, '')
    if a[0] == 'until':
        return (1, a[1])
    return (0, '') if a[0] == 'ask' else (2, '')


def switch_refuses(s):
    """ADDED (rules per profile): as crdt_model.switch_refuses."""
    return s is not None and (s[0] == 'const' or 'deny' in s[1])


def gen(f):
    """ADDED (copies extended): a document of the two older forms as naming every profile alike."""
    if f[0] == 'pp':
        return f
    s = frozenset(p + '.' + v for p in S.PROFS for v in f[1])
    return ('pp', s, frozenset()) if f[0] == 'const' else ('pp', frozenset(), s)


def und(s):
    """ADDED (copies extended): a document's text read; with PP, in the form with profiles."""
    f = S.undoc(s)
    return gen(f) if PP and f[0] not in ('rows', 'sws') else f     # ADDED (rules per profile): not a row


def deps_of(x):
    """ADDED (S34 forms): the records a value holds, as crdt_model.deps_of."""
    if x is None:
        return frozenset()
    if x[0] == 'rows' and len(x) > 3:
        return x[3]
    if x[0] == 'sws' and len(x) > 2:
        return x[2]
    return frozenset()


def value_of(obj, doc):
    """ADDED (renewal answers): a version's value: an answer's text as it stands, or the text read."""
    if ANSWERS and obj.startswith('renew:'):
        return ('ans', doc)
    return und(doc)


def meet(f, g):
    if f[0] == 'ans' and g[0] == 'ans':     # ADDED (renewal answers): the stricter, by text alone
        return max(f, g, key=lambda x: (answer_order(x[1]), x[1]))
    if f[0] == 'rows' and g[0] == 'rows':   # ADDED (rules per profile): as crdt_model.meet
        G = f[1] if g[1] is None else g[1] if f[1] is None else f[1] & g[1]
        D = deps_of(f) | deps_of(g)         # ADDED (S34 forms): the records of every version
        return ('rows', G, f[2] or g[2], D) if D else ('rows', G, f[2] or g[2])
    if f[0] == 'sws' and g[0] == 'sws':     # ADDED (rules per profile)
        D = deps_of(f) | deps_of(g)         # ADDED (S34 forms)
        return ('sws', f[1] | g[1], D) if D else ('sws', f[1] | g[1])
    if f[0] == 'pp' or g[0] == 'pp':    # ADDED (copies extended): letter by letter
        f, g = gen(f), gen(g)
        c = f[1] & g[1]
        return ('pp', c, ((f[1] | f[2]) & (g[1] | g[2])) - c)
    return ('const' if f[0] == 'const' and g[0] == 'const' else 'cap', f[1] & g[1])


def app(f, x):
    if f[0] == 'pp':                    # ADDED (copies extended)
        return f[1] | (f[2] & x)
    return f[1] if f[0] == 'const' else f[1] & x


def limit_of(d, own, level):
    """ADDED (copies extended): what a copied document d may take away at `level`, as a document to
    meet with the file's own (`own`, or None where the file holds no version of the object)."""
    d = gen(d)
    allowed = d[1] | d[2]
    if level == 'cap':
        return d
    if level == 'verb':
        return ('pp', allowed, frozenset())
    if level == 'nothing':
        return d if not allowed else ('pp', UNIV, frozenset())
    o = gen(own) if own is not None else ('pp', frozenset(), UNIV)
    keep = set(UNIV)
    for p in S.PROFS:
        L = frozenset(p + '.' + v for v in 'rw')
        if L & d[2]:
            continue                    # the copy does not name the profile
        g = d[1] & L
        if (not (L & o[2]) and g <= o[1]) or not g:
            keep -= L - g
    return ('pp', frozenset(keep), frozenset())


def wider_than(r, r0):
    """ADDED (copies extended): None when the answer r grants nothing the answer r0 does not; else
    what is wider. Both are answers of read()."""
    if r['status'] == 'ok' and r['served'] and not (r0['status'] == 'ok' and r0['served']):
        return 'serves %s' % r['served']
    for c, profs in r['adm'].items():
        if c not in r0['adm']:
            return 'client %s admitted' % c
        if not profs <= r0['adm'][c]:
            return 'client %s holds %s' % (c, sorted(profs - r0['adm'][c]))
    for n, w in r['words'].items():
        if not w <= r0['words'].get(n, frozenset()):
            return 'a node grants more'
    for c, a in r['renew'].items():
        if a == 'ask' and r0['renew'].get(c) == 'forever' and r['status'] == 'ok' and r['served']:
            return 'renewal of %s asked' % c
    return None


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
        if STARTS and c.kind == 'format':     # ADDED (started again): the number in the identifier
            height = int(c.d['format'])
        return m == c.mac and c.u == S.ident(key, c.kind, S.subject_of(c.kind, c.d), height, m)
    except (KeyError, ValueError):
        return False


def heads_items(h):
    return [json.loads(v) for k, v in sorted(h.extra.items())]


def subject_item(it):
    """ADDED (seal restates): whether an item of a revocation, a retirement or a removal names its
    subject; an item that does not holds the identifier of a deletion record, `id`, or its keyed tag
    and subject hash, `hash`, and its kind."""
    return {'revoked': 'client', 'retired': 'authority', 'removed': 'name'}[it['kind']] in it


def read(dump, history=False, copies=None, ext=None):     # ADDED (two readings): `copies`; (copies extended): `ext`
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
    # ADDED (copies extended): with `key`, a store key is also taken from a copy of an authority or
    # of a retirement: its content passes under the key it holds, whatever the entry's identifier
    copied_keys = 0
    if copies and ext and ext.get('key'):
        for c in cands:
            for v in (c.versions if history else [c]):
                if v.kind in ('authority', 'retired') and v.d and 'storeKey' in v.d and v.mac is not None:
                    try:
                        k = bytes.fromhex(v.d['storeKey'])
                    except ValueError:
                        continue
                    if k not in held and S.mac(k, v.kind, v.data, v.extra) == v.mac:
                        held.append(k)
                        copied_keys += 1
    els, passed, damage, by_history = [], 0, 0, 0
    damaged = []        # ADDED (seal again): (identifier, the kind its tag names, title)
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
            e = dump['entries'][c.u]     # ADDED (seal again)
            damaged.append((c.u, [kind for k in held for kind in S.KINDS if c.u[:6] == S.tag(k, kind)][0],
                            e.get('title', e['strings'].get('Title', ''))))
        else:
            passed += 1             # passed over and shown (change 3)
    # ADDED (started again): which keys stand; only what passes under the store key counts
    found, aside, kept_read, split, store_rels = list(held), [], [], False, []
    if STARTS:
        byname = {S.keyname(k): k for k in held}
        rels = []
        for c in els:
            if c.kind == 'kept' and c.d is not None:
                rels.append((c.key, byname.get(c.d.get('key')), c.u, frozenset(c.d.get('after', '').split()), 'record'))
            elif c.kind == 'head':
                for it in heads_items(c):
                    if it.get('kind') == 'kept':
                        rels.append((c.key, byname.get(it.get('key')), it.get('id'), frozenset(it.get('after', '').split()),
                                     'item'))
        for u in sorted(dels):
            for k in held:
                if u[:6] == S.tag(k, 'kept'):
                    for t in held:
                        if t != k and u[6:16] == S.subject_hash(k, 'key:' + S.keyname(t)):
                            rels.append((k, t, u, frozenset(), 'deletion'))
        standing, aside = mark_keys(held, [r[:4] for r in rels])
        kept_read = sorted(set(r[2] for r in rels))
        split = len(standing) > 1
        store_rels = [r for r in rels if r[0] in standing]
        els = [c for c in els if c.key in standing]
        damage = sum(1 for c in cands if c.key is None and any(
            c.u[:6] == S.tag(k, kind) for k in standing for kind in S.KINDS))
        held = standing
    # ADDED (two readings): a record that can only take access away, counted from a copy under
    # another identifier, as the record under the identifier its content gives
    fromcopy = {}
    # ADDED (copies extended): what the wider reading takes from copies, kept apart from the file's
    # own records: refusals as (kind, subject, the identifier the copy's content gives), removals as
    # (name, identifier), versions as (identifier, object, after, document)
    copy_refusals, copy_removals, copy_versions = [], [], []
    if copies and ext:
        have = set(c.u for c in els)
        for c in cands:
            if c.key is not None:
                continue
            for v in (c.versions if history else [c]):
                kinds = ('revoked', 'retired', 'removed', 'version') + (('head',) if ext.get('heads') else ())
                if v.kind not in kinds or v.d is None or v.mac is None:
                    continue
                canon = None
                for k in held:
                    try:
                        if S.mac(k, v.kind, v.data, v.extra) == v.mac:
                            height = int(v.d['height']) if v.kind == 'head' else 0
                            canon = S.ident(k, v.kind, S.subject_of(v.kind, v.d), height, v.mac)
                            break
                    except (KeyError, ValueError):
                        continue
                if canon is None:
                    continue
                if canon not in have:
                    if v.kind == 'revoked':
                        copy_refusals.append(('revoked', v.d['client'], canon))
                    elif v.kind == 'retired':
                        copy_refusals.append(('retired', v.d['authority'], canon))
                    elif v.kind == 'removed':
                        copy_removals.append((v.d['name'], canon))
                    elif v.kind == 'version':
                        copy_versions.append((canon, v.d['object'], frozenset(v.d['after'].split()), und(v.d['doc']), c.u))
                    else:       # a copy of a summary entry: its items, never its presence
                        for it in heads_items(v):
                            if it['kind'] == 'revoked':
                                copy_refusals.append(('revoked', it['client'], canon))
                            elif it['kind'] == 'retired':
                                copy_refusals.append(('retired', it['authority'], canon))
                            elif it['kind'] == 'removed':
                                copy_removals.append((it['name'], it['id']))
                            elif it['kind'] == 'version' and it['id'] not in have:
                                copy_versions.append((it['id'], it['object'], frozenset(it['after'].split()),
                                             und(it['doc']), c.u))
                    if v.kind != 'version':
                        fromcopy[canon] = c.u
                break
    if copies and not ext:
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
    by_id = []      # ADDED (seal restates): (kind, identifier or tag and subject hash) of an item with no subject
    for h in heads:
        for it in heads_items(h):
            if it['kind'] in ('revoked', 'retired', 'removed') and not subject_item(it):
                by_id.append((it['kind'], it.get('id') or it.get('hash')))
            elif it['kind'] == 'revoked':
                refusals.add(('revoked', it['client']))
            elif it['kind'] == 'retired':
                refusals.add(('retired', it['authority']))
            elif it['kind'] == 'removed':
                prem.add((it['name'], it['id']))
    # versions: entries not named by a record; and what the heads in force restate
    vers, supers = {}, set()
    lostsets, beside_obj = {}, {}   # ADDED (lost settled): what each version names in `lost`; a version beside its record
    for c in by.get('version', []):
        if c.u in named:
            supers.add(c.d['object'])
            beside_obj[c.u] = c.d['object']     # ADDED (lost settled)
        else:
            # ADDED (renewal answers): value_of for und
            vers.setdefault(c.d['object'], {})[c.u] = (frozenset(c.d['after'].split()),
                                                       value_of(c.d['object'], c.d['doc']))
            lostsets[c.u] = frozenset(c.d.get('lost', '').split())     # ADDED (lost settled)
    for h in heads_live:
        for it in heads_items(h):
            if it['kind'] == 'version':
                # ADDED (renewal answers): value_of for und
                vers.setdefault(it['object'], {})[it['id']] = (frozenset(it['after'].split()),
                                                               value_of(it['object'], it['doc']))
                lostsets[it['id']] = frozenset(it.get('lost', '').split())     # ADDED (lost settled)
    # what a deletion record still tells, by its keyed tag and hash (design 5.2 points 3 and 4)
    clients = set(c.d['client'] for c in by.get('client', [])) | set(s for (k, s) in refusals if k == 'revoked')
    clients |= set(s for (k, s, i) in copy_refusals if k == 'revoked')      # ADDED (copies extended)
    authorities = set(c.d['id'] for c in by.get('authority', [])) | set(s for (k, s) in refusals if k == 'retired')
    names = set(c.d['name'] for c in by.get('profile', []) + by.get('removed', [])) | set(n for (n, i) in prem)
    renewing = set(clients)
    if ANSWERS:     # ADDED (renewal answers): the client key of every answer present or restated; an
        # object of type renew in any other text than 64 lowercase hexadecimal characters is the object
        # of nothing (STORE.md 658), which no answer is read from
        renewing = set(c for c in clients if answer_key_form(c))
        renewing |= set(o[6:] for o in set(vers) | supers if o.startswith('renew:') and answer_key_form(o[6:]))
    objects = set(vers) | supers | set('renew:' + c for c in renewing)     # ADDED (renewal answers): renewing
    for g in dump['groups']:
        objects.add('rule:' + g)
    objects.add('rule:' + dump['root'])
    for u, e in dump['entries'].items():
        objects.add('rule:' + u)
    # ADDED (rules per profile): every node's switch and, in the form 'pair', every node's row for
    # each profile name the file knows: the names of the list's records and items, and the profile
    # of every row object a version or an item names
    row_names = set()
    if ROWS:
        row_names = set(names)
        for o in set(vers) | supers:
            if o.startswith('rule:') and '/' in o:
                row_names.add(o.split('/', 1)[1])
        nodes = [o[5:] for o in objects if o.startswith('rule:') and '/' not in o]
        for n in nodes:
            objects.add('switch:' + n)
            if S.ID_FORM == 'pair':
                for p in row_names:
                    objects.add('rule:%s/%s' % (n, p))
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
    profile_tags = {}      # ADDED (rules per profile): the form 'split', a profile's tag to its name
    if ROWS and S.ID_FORM == 'split':
        for k in held:
            for p in sorted(row_names):     # ADDED (owed forms): sorted, and every name of a tag kept
                profile_tags.setdefault((k, S.profile_tag(k, p)), []).append(p)
    present = set()     # ADDED (owed forms): the identifier of every version present or restated
    for V in vers.values():
        present |= set(V)
    after_of, lost_of = {}, {}      # ADDED (lost settled): what the versions in force of each object name
    for o, V in vers.items():
        for i, (after, f) in V.items():
            after_of.setdefault(o, set()).update(after)
            lost_of.setdefault(o, set()).update(lostsets.get(i, ()))

    def judged(u, k):
        """ADDED (lost settled): the objects a deletion record under the tag of `version` is lost
        to, under JUDGE."""
        if u in present:
            return []       # a version a summary entry in force restates: its text is held
        if u in beside_obj and JUDGE in ('object', 'lost'):
            told = [beside_obj[u]]      # told of by its own object, with nothing hashed
        else:
            what = subj.get((k, u[6:16]))
            if what is None or what[0] != 'object':
                return []
            o = what[1]
            if ROWS and S.ID_FORM == 'split' and o.startswith('rule:') and '/' not in o:
                told = ['%s/%s' % (o, p) for p in profile_tags.get((k, u[16:20]), [])]
                if u[16:20] == '0000' and o in set(vers) | supers:
                    told.append(o)
            else:
                told = [o]
        named = set()
        for x in told:
            named |= after_of.get(x, set())
        out = []
        for o in told:
            if JUDGE == 'union':
                ok = u in deadall
            elif JUDGE == 'object':
                ok = u in after_of.get(o, ())
            else:
                ok = u in named or u in lost_of.get(o, ())
            if not ok:
                out.append(o)
        return out

    # ADDED (seal restates): an item with no subject tells its decision of every subject the file names
    # whose hash under a key held is the one it holds; it restates the record of its identifier, and
    # with `hash` every record of its kind whose identifier begins with it
    c_ids, c_hashes = set(), set()
    for kind, u in by_id:
        if not u:
            continue
        (c_ids if len(u) > 16 else c_hashes).add(u)
        for kd, k in tags.get(u[:6], ()):
            what = subj.get((k, u[6:16])) if kd == kind else None
            if what is None:
                continue
            if kind == 'revoked' and what[0] == 'client':
                refusals.add(('revoked', what[1]))
            elif kind == 'retired' and what[0] == 'authority':
                refusals.add(('retired', what[1]))
            elif kind == 'removed' and what[0] == 'profile' and len(u) > 16:
                prem.add((what[1], u))
    stated = (set(refusals), set(prem))     # ADDED (seal again): what records and items state
    for u in dels:
        for kind, k in tags.get(u[:6], ()):
            if JUDGE is not None and kind == 'version':     # ADDED (lost settled)
                for o in judged(u, k):
                    lost.setdefault(o, set()).add(u)
                continue
            what = subj.get((k, u[6:16]))
            if what is None:
                continue
            whats = [what]      # ADDED (owed forms): a deletion record may tell of several rows
            # ADDED (rules per profile): in the form 'split' a row's version is told by the node's
            # rule and the profile's tag
            if profile_tags and kind == 'version' and what[0] == 'object' and what[1].startswith('rule:') \
                    and '/' not in what[1] and u[16:20] != '0000':
                ps = profile_tags.get((k, u[16:20]))
                if ps is None:
                    continue
                whats = [('object', what[1] + '/' + p)
                         for p in (ps if TAG_READ == 'each' else ps[:1] if TAG_READ == 'first' else ps[-1:])]
            for what in whats:
                if kind == 'revoked' and what[0] == 'client':
                    refusals.add(('revoked', what[1]))
                elif kind == 'retired' and what[0] == 'authority':
                    refusals.add(('retired', what[1]))
                elif kind == 'removed' and what[0] == 'profile':
                    prem.add((what[1], u))
                elif kind == 'version' and what[0] == 'object':
                    o = what[1]
                    if u not in deadall and (u not in present if TAG_READ == 'each' and profile_tags
                                             else u not in vers.get(o, {})):
                        lost.setdefault(o, set()).add(u)

    def freg_own(o):
        """The object's value by the file's own versions (moved up, unchanged, for the limits)."""
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

    # ADDED (copies extended): what copies add. The file's own refusals are kept apart; a copied
    # version limits its object unless a version the file reads, or another passing copy, names
    # it as replaced, or the file holds it among its own versions already.
    own_refusals = set(refusals)
    limits, limited, acknowledged, live_copies = {}, {}, set(), {}
    if copies and ext:
        for h in heads_live:
            for it in heads_items(h):
                if it['kind'] == 'copy':
                    acknowledged.add(it['id'])
        for (k, s, i) in copy_refusals:
            refusals.add((k, s))
        for (n, i) in copy_removals:
            prem.add((n, i))
        replaced_ids = set(deadall)
        if ext.get('among', True):      # a copy another passing copy names as replaced
            for (i, o, after, d, src) in copy_versions:
                replaced_ids |= after
        for (i, o, after, d, src) in copy_versions:
            if i in replaced_ids or i in vers.get(o, {}) or i in named:
                continue
            live_copies.setdefault(o, set()).add(i)
            e = limit_of(d, freg_own(o), ext.get('rules', 'nothing'))
            if meet(gen(freg_own(o) or IDENT), e) != gen(freg_own(o) or IDENT):
                limited.setdefault(o, []).append(i)
                fromcopy[i] = src
            limits[o] = e if o not in limits else meet(limits[o], e)

    def freg(o):
        f = freg_own(o)
        if o in limits:
            f = meet(f if f is not None else IDENT, limits[o])
        return f

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
                if it['kind'] in ('revoked', 'retired', 'removed') and not subject_item(it):
                    continue    # ADDED (seal restates): by identifier, below
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

        def by_ident(c):    # ADDED (seal restates): an item with no subject restates it
            return c.u in c_ids or (c.kind != 'removed' and c.u[:16] in c_hashes)

        for c in by.get('revoked', []):
            if ('revoked', c.d['client']) not in c_ref and held_to(c) and not by_ident(c):
                status = 'incomplete'
        for c in by.get('retired', []):
            if ('retired', c.d['authority']) not in c_ref and held_to(c) and not by_ident(c):
                status = 'incomplete'
        for c in by.get('removed', []):
            if (c.d['name'], c.u) not in c_prem and held_to(c) and not by_ident(c):
                status = 'incomplete'
        for c in by.get('version', []):
            if c.u not in named and c.u not in deadall and c.u not in c_regs and held_to(c):
                status = 'incomplete'
        # ADDED (copies extended): held to the check, a copy that takes away what no summary entry
        # in force restates stops the store, unless a Seal again acknowledged it
        if copies == 'held' and ext:
            for (k, s, i) in copy_refusals:
                if (k, s) not in c_ref and i not in acknowledged:
                    status = 'incomplete'
            for (n, i) in copy_removals:
                if (n, i) not in c_prem and i not in acknowledged:
                    status = 'incomplete'
            for o, ids in limited.items():
                if any(i not in c_regs and i not in acknowledged for i in ids):
                    status = 'incomplete'
    # ADDED (started again): a keep record and a version record a summary entry restates; the room;
    # then a split store, and an outdated store, ahead of every other state (STORE.md 656)
    room = None
    if STARTS:
        items_kept = set(it.get('id') for h in heads for it in heads_items(h) if it.get('kind') == 'kept')
        items_fmt = [int(it['format']) for h in heads for it in heads_items(h) if it.get('kind') == 'format']
        fmts = [int(c.d['format']) for c in by.get('format', [])]
        if status == 'ok' and (any(c.u not in items_kept for c in by.get('kept', []))
                               or any(n > max(items_fmt + [0]) for n in fmts)):
            status = 'incomplete'
        if ROOM and not split:
            told = set(u for u in dels if tagged(u, 'head'))
            nxt = set(told)
            for h in heads:
                nxt.add(h.u)
                nxt |= set(h.d['after'].split())
            height = max([int(h.d['height']) for h in heads] + [0]) + 1
            standing_vers = 0
            for V in vers.values():
                dd = set()
                for after, f in V.values():
                    dd |= after
                standing_vers += sum(1 for i in V if i not in dd)
            carried = set(r[2] for r in store_rels)
            n_items = 1 + len(refusals) + len(prem) + standing_vers + len(carried)
            content = (len(nxt) * 33 - 1 if nxt else 0) + len(S.dumps({'after': '', 'height': str(height),
                                                                        'nonce': '0' * 32, 'v': '2'}))
            room = dict(named=len(nxt), content=content, items=n_items,
                        has=content <= MAX_WRITTEN and n_items < MAX_ITEMS)
            if status == 'ok' and not room['has']:
                status = 'incomplete'
        if split:
            status = 'split'
        elif held and max(fmts + items_fmt + [0]) > 1:
            status = 'outdated'
    # the authority served (two standing: 503 undecided)
    auths = sorted(c.d['id'] for c in by.get('authority', []) if c.u not in named and ('retired', c.d['id']) not in refusals)
    served = auths[0] if len(auths) == 1 and status == 'ok' else None
    # ADDED (copies extended): without `settle`, which authority is served is decided by the file's
    # own records alone, and a retirement read from a copy can only take that authority away
    if copies and ext and not ext.get('settle'):
        own_auths = sorted(c.d['id'] for c in by.get('authority', [])
                           if c.u not in named and ('retired', c.d['id']) not in own_refusals)
        served = own_auths[0] if len(own_auths) == 1 and own_auths[0] in auths and status == 'ok' else None
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

    # the user tree: groups and entries outside the plugin's group, each walked from the root
    parent = {g: v['parent'] for g, v in dump['groups'].items()}
    for u, e in dump['entries'].items():
        parent[u] = e['parent']
    memo = {}

    # ADDED (S34 forms): what a node lets through for a profile, cut or checked by what its versions
    # that let through record, as crdt_model.dep_cut, over the file's tree
    def standing_ids(o):
        V = vers.get(o) or {}
        dd = set()
        for after, f in V.values():
            dd |= after
        return frozenset(i for i in V if i not in dd)

    def walk_above(n, p):
        ids, m = set(), n
        while m != dump['root']:
            m = parent[m]
            ids |= standing_ids('rule:%s/%s' % (m, p))
            r = freg('rule:%s/%s' % (m, p))
            if r is not None and (r[0] == 'const' or (r[1] is not None and not r[2])):
                break
            ids |= standing_ids('switch:' + m)
            if switch_refuses(freg('switch:' + m)):
                break
        return frozenset(ids)

    def dep_cut(L, r, sw, n, p):
        if S.DEP is None:
            return L
        for rec in deps_of(r):
            if rec[0] == 'deny':
                return set()
            if S.DEP == 'seen':
                L = L & set(p + '.' + w for w in rec[1])
            elif rec[1] != walk_above(n, p) | standing_ids('switch:' + n):
                return set()
        for rec in deps_of(sw):
            if S.DEP == 'seen':
                L = L & rec[1]
            elif dict(rec[1]).get(p, frozenset()) != walk_above(n, p):
                return set()
        return L

    def words(n):
        if n in memo:
            return memo[n]
        f = freg('rule:' + n) or IDENT
        if PP:      # ADDED (copies extended)
            f = gen(f)
        if ROWS:    # ADDED (rules per profile): the node's rule from its rows and its switch
            sw = freg('switch:' + n)
            deny = switch_refuses(sw)
            C, P = set(), set()
            for p in S.PROFS:
                r = freg('rule:%s/%s' % (n, p))
                if r is not None and r[0] == 'const':
                    continue
                if r is None or r[1] is None:
                    if not deny:
                        P |= dep_cut(set(p + '.' + w for w in LETTERS), r, sw, n, p)     # ADDED (owed forms): LETTERS; (S34 forms): dep_cut
                elif r[2]:
                    if not deny:
                        P |= dep_cut(set(p + '.' + w for w in r[1]), r, sw, n, p)     # ADDED (S34 forms): dep_cut
                else:
                    C |= set(p + '.' + w for w in r[1])
            f = ('pp', frozenset(C), frozenset(P))
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
    renew_detail = {}       # ADDED (renewal answers)
    for cl in sorted(renewing):     # ADDED (renewal answers): renewing for clients
        f = freg('renew:' + cl)
        if ANSWERS:     # ADDED (renewal answers): the chain, with no clock
            o = 'renew:' + cl
            if f is None:
                tok, why = 'ask', 'none'
            elif f[0] != 'ans':
                tok, why = 'forever', 'lost'
            else:
                a = answer_text(f[1])
                tok, why = ('forever', 'unreadable') if a is None else \
                    ('until:' + a[1] if a[0] == 'until' else a[0], 'stands')
            V = vers.get(o) or {}
            dd = set()
            for after, g in V.values():
                dd |= after
            renew[cl] = tok
            renew_detail[cl] = {'value': tok, 'cause': why, 'lost': sorted(lost.get(o, ())),
                                'standing': sorted(g[1] for i, (after, g) in V.items() if i not in dd)}
            continue
        if PP and f is not None:    # ADDED (copies extended)
            renew[cl] = 'forever' if not app(gen(f), UNIV) else 'ask'
            continue
        renew[cl] = 'forever' if f is not None and not app(f, ALL) else 'ask'
    counts = dict(elements=len(els), passed=passed, damage=damage, heads=len(heads),
                  heads_live=len(heads_live), records=len(dels), keys=len(held))
    if history:     # ADDED (decided store): records that count by a history item
        counts['byhistory'] = by_history
    if copies:      # ADDED (two readings): records that count from a copy
        counts['fromcopy'] = len(fromcopy)
    if copies and ext:      # ADDED (copies extended): objects a copy limits; keys taken from a copy
        counts['limited'] = len(limited)
        counts['copied_keys'] = copied_keys
    extra = {}
    if ROWS or SHOW:    # ADDED (rules per profile): each row's and switch's standing versions, as
        # text, and the keyed hashes the reading computed to tell what a deletion record is of
        counts['subject_hashes'] = len(subj) + len(profile_tags)
        standing = {}
        for o, V in vers.items():
            if o.startswith('rule:') or o.startswith('switch:'):
                dead = set()
                for after, f in V.values():
                    dead |= after
                standing[o] = sorted(S.doc(f) for i, (after, f) in V.items() if i not in dead)
        extra = dict(standing=standing, lost=sorted(lost), row_values={o: freg(o) for o in
                     set(vers) | set(lost) | supers if o.startswith('rule:') or o.startswith('switch:')})
        # ADDED (owed forms): the names whose tags meet, which the reading can see; the names the
        # file knows for a row; the names of the list's records and items
        extra['tag_meets'] = sorted(tuple(ps) for ps in profile_tags.values() if len(ps) > 1)
        extra['row_names'] = sorted(row_names)
        extra['known_names'] = sorted(names)
    if ANSWERS:     # ADDED (renewal answers)
        extra['renew_detail'] = renew_detail
    if SEAL:        # ADDED (seal again): what a stopped store lost, and what the seal writes
        reasons = []
        told_heads = []
        if store and not heads_live:
            reasons.append('no summary entry of the store is in force')
        if heads_live:
            known_h = set(h.u for h in heads_live)
            for h in heads_live:
                known_h |= set(h.d['after'].split())
            told_heads = sorted(u for u in dels if tagged(u, 'head') and u not in known_h)
            if told_heads:
                reasons.append('a summary entry was deleted that nothing in force replaces')
        if damaged:
            reasons.append('entries of the store have no version that passes')
        n_ref, n_prem, n_regs = set(), set(), set()
        for h in heads:
            for it in heads_items(h):
                if it['kind'] in ('revoked', 'retired', 'removed') and not subject_item(it):
                    continue    # ADDED (seal restates): by identifier, below
                if it['kind'] in ('revoked', 'retired'):
                    n_ref.add((it['kind'], it['client'] if it['kind'] == 'revoked' else it['authority']))
                elif it['kind'] == 'removed':
                    n_prem.add((it['name'], it['id']))
                elif it['kind'] == 'version':
                    n_regs.add(it['id'])

        def restated_by_id(c):  # ADDED (seal restates)
            return c.u in c_ids or (c.kind != 'removed' and c.u[:16] in c_hashes)
        not_restated = sorted(
            [c.u for c in by.get('revoked', []) if ('revoked', c.d['client']) not in n_ref and not restated_by_id(c)]
            + [c.u for c in by.get('retired', []) if ('retired', c.d['authority']) not in n_ref and not restated_by_id(c)]
            + [c.u for c in by.get('removed', []) if (c.d['name'], c.u) not in n_prem and not restated_by_id(c)]
            + [c.u for c in by.get('version', []) if c.u not in named and c.u not in deadall and c.u not in n_regs])
        if not_restated and heads:
            reasons.append('a record no summary entry restates')
        by_deletion = {
            'revoked': sorted(s for (k, s) in refusals - stated[0] if k == 'revoked'),
            'retired': sorted(s for (k, s) in refusals - stated[0] if k == 'retired'),
            'removed': sorted('%s %s' % (n, i) for (n, i) in prem - stated[1])}
        after_damage = set()
        for (u, kind, title) in damaged:
            if kind == 'version':
                for kd, k in tags.get(u[:6], ()):
                    if kd == 'version':
                        after_damage |= set(judged(u, k)) if JUDGE is not None else set()

        def granting_nothing(o):
            if o.startswith('renew:'):
                return 'refuse:forever'
            if o.startswith('switch:'):
                return S.doc(('sws', frozenset(['deny'])))
            if o.startswith('rule:') and '/' in o:
                return S.doc(('rows', frozenset(), False))
            return S.doc(('const', frozenset()))
        seal = []
        if status == 'incomplete' and held:
            for o, ids in sorted(lost.items()):
                V = vers.get(o, {})
                aft = set(V)
                for after, f in V.values():
                    aft |= after
                lst = set(ids) | lost_of.get(o, set())
                seal.append({'object': o, 'doc': granting_nothing(o), 'after': sorted(aft), 'lost': sorted(lst)})
        extra['losses'] = {
            'reasons': reasons, 'heads': told_heads,
            'damaged': [{'id': u, 'kind': kind, 'title': title} for (u, kind, title) in sorted(damaged)],
            'notRestated': not_restated, 'byDeletion': by_deletion,
            'lost': {o: sorted(ids) for o, ids in sorted(lost.items())},
            'refusesAfter': sorted(after_damage - set(lost)), 'seal': seal}
    if STARTS:      # ADDED (started again): the keys by the model's labels, and the room
        def label(k):
            return S.KEYLABEL.get(k, k.hex()[:8])
        extra['keys'] = dict(found=sorted(label(k) for k in found), standing=sorted(label(k) for k in held),
                             aside=sorted(label(k) for k in aside), kept_read=kept_read, room=room)
        if SEAL and 'losses' in extra:
            # a stop a start wrote: a keep record no summary entry restates that names a key not found,
            # which the seal's dialog shows as the store set aside by a start, and the keep record the
            # seal writes for it, keeping this store, naming that key and, in `after`, the stop
            ik = set(it.get('id') for h in heads for it in heads_items(h) if it.get('kind') == 'kept')
            by_start = sorted((c for c in by.get('kept', []) if c.u not in ik and c.d.get('key') not in byname),
                              key=lambda c: c.u)
            L = extra['losses']
            L['setAsideBy'] = [{'id': c.u, 'key': c.d.get('key')} for c in by_start]
            if by_start:
                L['notRestated'] = sorted(set(L['notRestated']) | set(c.u for c in by_start))
                if 'a record no summary entry restates' not in L['reasons']:
                    L['reasons'].append('a record no summary entry restates')
                if status == 'incomplete' and held:
                    L['seal'] = L['seal'] + [{'kind': 'kept', 'key': c.d.get('key'), 'after': [c.u]} for c in by_start]
    return dict(**extra,status=status, served=served, auths=auths, adm=adm if serve else {}, valid=valid,
                names=sorted(on), words={n: (w if serve else frozenset()) for n, w in raw.items()},
                words_raw=raw, renew=renew, refusals=refusals, counts=counts,
                # ADDED (two readings)
                byhist={c.u: c.hidx for c in els if c.by_history and c.copy is None},
                held=list(held), fromcopy=fromcopy,
                limited=sorted(limited), live_copies=live_copies)    # ADDED (copies extended)
