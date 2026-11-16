"""An independent model of option-crdt.md, written from the design's text alone.

Written 2026-11-15 for critique-crdt-formal.md. It does not import, copy or read the design's own
crdt_model.py. Every rule below names the section of option-crdt.md it is taken from; where the
text is ambiguous, the reading taken is said, and a switch in Opt lets the other reading be run.

What it adds to the design's own model (section 12.1 of the design lists these as not modeled):
  * where an entry sits: the store group, the recycle bin, any other group, with a location time
    that KeePass's Synchronize merges by LocationChanged (ground-keepass 2.2) and KeePassDX follows
    the arriving copy at equal times (measured-android 4.3, ground-android 5.4);
  * the 64-height cut of section 3.4 and points 4 and 5 of section 5.2, as a parameter K;
  * an unchanged save or a title edit that moves an entry's modification time (T3; measured-android
    3.3; an accidental title edit, measured-android 2.1 and 3.2), which decides whether KeePass's
    Synchronize applies or drops a deletion record (ground-keepass 5);
  * three and four files, a backup restored and written to again, longer histories.

Two oracles:
  IK  everything ever written into this file's lineage, by its own gate or through any merge
      into it (transitively), read with no loss and the full causal order. A failure against IK
      that is not against IE can be a decision the file was never physically given.
  IE  everything physically present in the file now, in any group, read with no loss and the
      order the file itself writes down (each element's own `after`). A design value wider than
      IE means the design threw away narrowing evidence that is still inside the file.

Run:  uv run --no-project python -I -X utf8 crdt_formal.py [ce|search|laws|all] [n]
"""

import random
import sys
from dataclasses import dataclass, field
from functools import reduce

# ----------------------------------------------------------------------------------------------
# The lattice of rule documents, design 4.3

WORDS = ('create', 'read', 'update', 'strip', 'delete', 'list', 'attachments', 'protected')
SWITCHES = frozenset({'attachments', 'protected'})
ALL = frozenset(WORDS)
NONE = frozenset()
PROFILES = ('p', 'q')
NODES = ('root', 'N')            # N is a child of root
CERTS = ('c0', 'c1', 'c2')

fs = frozenset
DOCS = {
    'rw': ('inherit', {'p': fs({'read', 'update', 'list'})}),
    'r': ('inherit', {'p': fs({'read', 'list'})}),
    'rA': ('inherit', {'p': fs({'read', 'list', 'attachments'}), 'q': fs({'read', 'list', 'protected'})}),
    'none': ('inherit', {'p': NONE}),
    'inherit': ('inherit', {}),
    'deny': ('deny', {}),
    'q': ('inherit', {'q': fs({'read', 'update', 'list', 'delete'})}),
    'pq': ('deny', {'p': fs({'read', 'list'}), 'q': fs({'read'})}),
}
DOCNAMES = tuple(DOCS)


def docf(doc, x):
    """One document as a function of what arrives from above, for profile x (design 4.3)."""
    default, named = DOCS[doc]
    if x in named:
        return ('G', named[x])
    if default == 'deny':
        return ('G', NONE)
    return ('L', ALL)


def meetf(f, g):
    """The meet table of design 4.3."""
    if f[0] == 'G' and g[0] == 'G':
        return ('G', f[1] & g[1])
    return ('L', f[1] & g[1])


def applyf(f, arriving):
    return f[1] if f[0] == 'G' else (arriving & f[1])


def grant(words):
    """What a set of words grants: verbs, each over the base and over every switch held."""
    verbs = words - SWITCHES
    scopes = {'base'} | (words & SWITCHES)
    return fs((v, s) for v in verbs for s in scopes)


STRICT = {'forever': 2, 'until': 1, 'allowed': 0}

# ----------------------------------------------------------------------------------------------
# Elements and files, design 3.2 to 3.4 and 5.1


@dataclass(frozen=True)
class El:
    kind: str          # auth retired client revoked profile removed rule renewal head
    subj: str          # what it is about: authority, certificate, name, node; the store for a head
    height: int
    nonce: int
    data: object = None
    after: frozenset = NONE   # as written: cut (design 3.4)
    tafter: frozenset = NONE  # the full causal past; the oracle's, never read by the design
    key: str = 'k0'           # the store key it is MAC'd under (design 7)

    @property
    def id(self):
        # The UUID layout of design 3.2: tag, keyed hash of the subject, height, MAC bytes.
        return (self.kind, self.subj, self.height, self.nonce, self.key)


@dataclass
class Entry:
    el: El
    mtime: int
    loc: str           # 'store', 'bin', 'other'
    loct: int


class File:
    def __init__(self):
        self.entries = {}
        self.recs = {}
        self.know = set()       # IK: every element in the lineage
        self.marked = set()     # nodes carrying the mark of design 8
        self.overwritten = False
        self.sealed = False

    def clone(self):
        g = File()
        g.entries = {i: Entry(e.el, e.mtime, e.loc, e.loct) for i, e in self.entries.items()}
        g.recs = dict(self.recs)
        g.know = set(self.know)
        g.marked = set(self.marked)
        g.overwritten = self.overwritten
        g.sealed = self.sealed
        return g


@dataclass
class Opt:
    K: object = None             # the 64-height cut; None = unbounded
    anywhere: bool = False       # a fix: count a verifying element wherever it sits
    bin_heads: bool = False      # the security critic's S3 change 2: a binned head is in force
    xc_bin_shared: bool = True   # KeePassXC: the bin group is the same group in both copies
    gate_writes_when_incomplete: bool = False
    rotate: bool = False         # the security critic's S1 change: a new store key at every Recreate
    named_by_present: bool = False  # 5.2 point 4 read so that versions in any head present name others


class World:
    def __init__(self, nfiles, opt):
        self.opt = opt
        self.clock = 0
        self.nonce = 0
        self.files = [File() for _ in range(nfiles)]

    def tick(self):
        self.clock += 1
        return self.clock

    def fresh(self):
        self.nonce += 1
        return self.nonce

# ----------------------------------------------------------------------------------------------
# The read rule, design 5.2, with the node rule of design 8 reduced to "a ruled node is marked"


@dataclass
class Val:
    whole: bool
    served: object
    admitted: dict
    plist: frozenset
    access: dict
    renew: dict
    ctx: dict = field(default_factory=dict)

    def reach(self):
        if not self.whole or self.served in (None, 'undecided'):
            return fs()
        out = set()
        for c, profs in self.admitted.items():
            for x in profs:
                for node in NODES:
                    for g in grant(self.access[(node, x)]):
                        out.add((c, x, node, g))
        return fs(out)

    def strict(self):
        if not self.whole:
            return {c: 2 for c in CERTS}
        return self.renew

    def show(self):
        if not self.whole:
            return 'INCOMPLETE'
        acc = ' '.join(f"{n}/{x}={''.join(sorted(w[0] for w in self.access[(n, x)])) or '-'}"
                       for n in NODES for x in PROFILES)
        adm = ','.join(f"{c}:{''.join(sorted(p))}" for c, p in sorted(self.admitted.items()))
        ren = ','.join(f"{c}:{s}" for c, s in sorted(self.renew.items()) if s)
        return f"auth={self.served} list={''.join(sorted(self.plist))} admitted[{adm}] {acc} renew[{ren}]"


def cut_versions(ids, K):
    if K is None:
        return fs(ids)
    return fs(sorted(ids, key=lambda i: -i[2])[:K])


def cut_heads(ids, top, K):
    if K is None:
        return fs(ids)
    return fs(i for i in ids if i[2] >= top - K)    # "within 64 heights", as the reader's tolerance


def keyed_view(f, opt):
    """Only what verifies under the served authority's store key is read (design 7). Retirements
    are read under every key an authority in force holds, since each is about an authority."""
    def counts(e):
        return e.loc == 'store' or opt.anywhere
    auth_inf = [e.el for i, e in f.entries.items() if e.el.kind == 'auth' and counts(e) and i not in f.recs]
    keys = {a.data for a in auth_inf}
    ret = set()
    for e in f.entries.values():
        el = e.el
        if el.key in keys and (counts(e) or e.loc == 'bin'):
            if el.kind == 'retired':
                ret.add(el.subj)
            if el.kind == 'head':
                ret |= {s for k, s in el.data[0] if k == 'retired'}
    for r in f.recs:
        if r[-1] in keys and ((r[0] == 'witness' and r[1] == 'retired') or r[0] == 'retired'):
            ret.add(r[2] if r[0] == 'witness' else r[1])
    cand = sorted({(a.subj, a.data) for a in auth_inf if a.subj not in ret})
    ks = cand[0][1] if len({c[0] for c in cand}) == 1 else None
    g = File()
    g.entries = {i: e for i, e in f.entries.items() if e.el.kind == 'auth' or e.el.key == ks}
    g.recs = {r: t for r, t in f.recs.items() if r[-1] == ks}
    g.marked = f.marked
    g.extra_ret = ret
    return g


def read(f, opt):
    if opt.rotate:
        f = keyed_view(f, opt)
    st, bn = {}, {}
    for i, e in f.entries.items():
        if e.loc == 'store' or opt.anywhere:
            st[i] = e.el
        elif e.loc == 'bin':
            bn[i] = e.el
    recs = f.recs
    inf = {i: el for i, el in st.items() if i not in recs}                     # 5.2 point 2
    present = list(st.values()) + list(bn.values())
    heads_present = [el for el in present if el.kind == 'head']
    hif = [el for el in inf.values() if el.kind == 'head']
    if opt.bin_heads:
        hif += [el for el in bn.values() if el.kind == 'head' and el.id not in recs]
    # point 3: refusals and removals, a union of every carrier
    ref = set()
    for el in present:
        if el.kind in ('revoked', 'retired'):
            ref.add((el.kind, el.subj))
    for h in heads_present:
        ref |= h.data[0]
    rem = {}
    for el in present:
        if el.kind == 'removed':
            rem[el.id] = el.subj
    for h in heads_present:
        for rid in h.data[1]:
            rem[rid] = rid[1]
    for r in recs:
        if r[0] == 'witness':
            ref.add((r[1], r[2]))
        elif r[0] in ('revoked', 'retired'):
            ref.add((r[0], r[1]))
        elif r[0] == 'removed':
            rem[r] = r[1]
    for x in getattr(f, 'extra_ret', ()):
        ref.add(('retired', x))
    # the authority, design 4.1
    auths = sorted({el.subj for el in inf.values() if el.kind == 'auth' and ('retired', el.subj) not in ref})
    served = auths[0] if len(auths) == 1 else (None if not auths else 'undecided')
    # point 4: registers
    vers, carried = {}, set()
    for el in inf.values():
        if el.kind in ('rule', 'renewal'):
            vers[el.id] = el
    for h in hif:
        for v in h.data[2]:
            vers[v.id] = v
            carried.add(v.id)
    named = set()
    for v in vers.values():
        named |= v.after
    if opt.named_by_present:          # the other reading of 5.2 point 4: a binned head's versions name
        for h in heads_present:
            for v in h.data[2]:
                named |= v.after
    standing = {}
    for v in vers.values():
        if v.id not in named:
            standing.setdefault((v.kind, v.subj), []).append(v)
    refused = set()
    for r in recs:
        if r[0] in ('rule', 'renewal'):
            obj = (r[0], r[1])
            if r in named or r in carried:
                continue
            if opt.K is not None and standing.get(obj) and r[2] < max(v.height for v in standing[obj]) - opt.K:
                continue                                    # "taken for one long replaced"
            refused.add(obj)
    for i, el in st.items():
        if el.kind in ('rule', 'renewal') and i in recs and not standing.get((el.kind, el.subj)):
            refused.add((el.kind, el.subj))
    # point 5: whole
    whole = bool(hif)
    hids = {h.id for h in hif}
    anc = set()
    for h in hif:
        anc |= h.after
    top = max((h.height for h in hif), default=0)
    for r in recs:
        if r[0] == 'head' and r not in hids and r not in anc:
            if opt.K is not None and r[2] < top - opt.K:
                continue                                    # "taken for an ancestor"
            whole = False
    c_ref, c_rem, c_ver, c_named = set(), set(), set(), set()
    for h in heads_present:
        c_ref |= h.data[0]
        c_rem |= h.data[1]
        for v in h.data[2]:
            c_ver.add(v.id)
            c_named |= v.after
    standing_ids = {v.id for vs in standing.values() for v in vs}
    for i, el in st.items():
        if el.kind in ('revoked', 'retired') and (el.kind, el.subj) not in c_ref:
            whole = False
        if el.kind == 'removed' and el.id not in c_rem:
            whole = False
        if el.kind in ('rule', 'renewal') and i in standing_ids and i not in c_ver and i not in c_named:
            whole = False
    # the profile list, design 4.1 and 4.2
    plist = set()
    for x in PROFILES:
        known = {rid for rid, n in rem.items() if n == x}
        if any(el.kind == 'profile' and el.subj == x and known <= el.after for el in inf.values()):
            plist.add(x)
    admitted = {}
    if served not in (None, 'undecided'):
        for el in inf.values():
            if el.kind == 'client' and el.data[0] == served and ('revoked', el.subj) not in ref:
                admitted[el.subj] = fs(el.data[1]) & fs(plist)
    access = node_access(standing, refused, f.marked)
    renew = {}
    for c in CERTS:
        obj = ('renewal', c)
        if obj in refused:
            renew[c] = 2
        elif standing.get(obj):
            renew[c] = max(STRICT[v.data] for v in standing[obj])
        else:
            renew[c] = 0
    skey = next((el.data for el in inf.values() if el.kind == 'auth' and el.subj == served), 'k0')
    ctx = dict(skey=skey, standing=standing, ref=ref, rem=rem, hif=hif, heads_present=heads_present,
               served=served, auths=auths, inf=inf, refused=refused, recs=recs)
    return Val(whole, served, admitted, fs(plist), access, renew, ctx)


def node_access(standing, refused, marked):
    fn = {}
    for node in NODES:
        obj = ('rule', node)
        for x in PROFILES:
            if obj in refused:
                fx = ('G', NONE)
            elif standing.get(obj):
                fx = reduce(meetf, [docf(v.data, x) for v in standing[obj]])
            elif node in marked:
                fx = ('G', NONE)                            # design 8: marked, no rule: refuse
            else:
                fx = ('L', ALL)
            fn[(node, x)] = fx
    access = {}
    for x in PROFILES:
        a_root = applyf(fn[('root', x)], NONE)
        access[('root', x)] = a_root
        access[('N', x)] = applyf(fn[('N', x)], a_root)
    return access


def ideal(els, marked, causal=True):
    """No loss, no records, every location; `causal` uses the full past, else what is written."""
    els = list(els)
    heads = [e for e in els if e.kind == 'head']
    ref = {(e.kind, e.subj) for e in els if e.kind in ('revoked', 'retired')}
    rem = {e.id: e.subj for e in els if e.kind == 'removed'}
    vers = {e.id: e for e in els if e.kind in ('rule', 'renewal')}
    for h in heads:
        ref |= h.data[0]
        for rid in h.data[1]:
            rem[rid] = rid[1]
        for v in h.data[2]:
            vers[v.id] = v
    auths = sorted({e.subj for e in els if e.kind == 'auth' and ('retired', e.subj) not in ref})
    served = auths[0] if len(auths) == 1 else (None if not auths else 'undecided')
    named = set()
    for v in vers.values():
        named |= (v.tafter if causal else v.after)
    standing = {}
    for v in vers.values():
        if v.id not in named:
            standing.setdefault((v.kind, v.subj), []).append(v)
    plist = set()
    for x in PROFILES:
        known = {rid for rid, n in rem.items() if n == x}
        if any(e.kind == 'profile' and e.subj == x and known <= e.after for e in els):
            plist.add(x)
    admitted = {}
    if served not in (None, 'undecided'):
        for e in els:
            if e.kind == 'client' and e.data[0] == served and ('revoked', e.subj) not in ref:
                admitted[e.subj] = fs(e.data[1]) & fs(plist)
    access = node_access(standing, set(), marked)
    renew = {}
    for c in CERTS:
        vs = standing.get(('renewal', c))
        renew[c] = max(STRICT[v.data] for v in vs) if vs else 0
    return Val(True, served, admitted, fs(plist), access, renew, dict(ref=ref))

# ----------------------------------------------------------------------------------------------
# The gate's acts, design 4, 5.1, 5.3, 5.4, 9 and 10


def gate(w, fi, act, *args):
    f = w.files[fi]
    opt = w.opt
    v = read(f, opt)
    c = v.ctx
    if act == 'seal':
        if v.whole:
            return False
    elif not v.whole and not opt.gate_writes_when_incomplete:
        return False
    now = w.tick()
    height = 1 + max((h.height for h in c['heads_present']), default=0)
    new, prune, witnesses = [], set(), []
    standing = {k: list(vs) for k, vs in c['standing'].items()}

    cur = {'key': c['skey']}

    def mk(kind, subj, data=None, after=NONE, tafter=NONE):
        return El(kind, subj, height, w.fresh(), data, fs(after), fs(tafter), cur['key'])

    def version(kind, subj, data, extra_after=()):
        old = standing.get((kind, subj), [])
        after = set(extra_after)
        tafter = set(extra_after)
        for o in old:
            after |= {o.id} | o.after
            tafter |= {o.id} | o.tafter
        el = mk(kind, subj, data, cut_versions(after, opt.K), tafter)
        new.append(el)
        for i, e in f.entries.items():                                   # design 9, row 1
            if e.loc == 'store' and i not in f.recs and e.el.kind == kind and e.el.subj == subj and i in el.after:
                prune.add(i)
        standing[(kind, subj)] = [el]
        return el

    if act == 'rule':
        node, doc = args
        version('rule', node, doc)
        f.marked.add(node)
    elif act == 'renew':
        cert, answer = args
        version('renewal', cert, answer)
    elif act == 'revoke':
        (cert,) = args
        new.append(mk('revoked', cert))
        witnesses.append(('witness', 'revoked', cert, cur['key']))
        for i, e in f.entries.items():                                   # design 9, row 5
            if e.loc == 'store' and e.el.kind == 'client' and e.el.subj == cert and i not in f.recs:
                prune.add(i)
    elif act == 'pair':
        cert, profs = args
        if c['served'] in (None, 'undecided'):
            return False
        if cert == 'new':
            cert = f"n{w.fresh()}"          # a pairing always issues a certificate of its own
        new.append(mk('client', cert, (c['served'], fs(profs))))
        for x in profs:
            if x not in read(f, opt).plist:
                known = {rid for rid, n in c['rem'].items() if n == x}
                new.append(mk('profile', x, None, known))
    elif act == 'prem':
        (x,) = args
        new.append(mk('removed', x))
        for i, e in f.entries.items():                                   # design 9, row 3
            if e.loc == 'store' and e.el.kind == 'profile' and e.el.subj == x and i not in f.recs:
                prune.add(i)
    elif act == 'padd':
        (x,) = args
        known = {rid for rid, n in c['rem'].items() if n == x}
        new.append(mk('profile', x, None, known))
        for i, e in f.entries.items():                                   # design 9, row 4
            if e.loc == 'store' and e.el.kind == 'removed' and e.el.subj == x and i not in f.recs:
                prune.add(i)
    elif act in ('recreate', 'retire'):
        old = c['served']
        if old in (None, 'undecided'):
            return False
        if act == 'recreate' and opt.rotate:
            cur['key'] = f'k{w.fresh()}'                               # critic S1: a new store key
        new.append(mk('retired', old))
        witnesses.append(('witness', 'retired', old, cur['key']))
        for i, e in f.entries.items():                                   # design 9, rows 5 and 7
            if e.loc == 'store' and i not in f.recs and (
                    (e.el.kind == 'auth' and e.el.subj == old) or (e.el.kind == 'client' and e.el.data[0] == old)):
                prune.add(i)
        if act == 'recreate':
            new.append(mk('auth', f'A{w.fresh()}', cur['key']))
            if opt.rotate:                     # every decision in force written again under the new key
                for (k, s_), vs in list(standing.items()):
                    for v in vs:
                        version(k, s_, v.data)
                for (k, s_) in c['ref']:
                    if k == 'revoked':
                        new.append(mk('revoked', s_))
                remap = {}
                for rid, x in c['rem'].items():
                    nr = mk('removed', x)
                    new.append(nr)
                    remap[rid] = nr.id
                for el in c['inf'].values():
                    if el.kind == 'profile':
                        new.append(mk('profile', el.subj, None, {remap.get(r, r) for r in el.after}))
    elif act == 'seal':                                                  # design 5.4
        f.sealed = True
        for node in NODES:
            obj = ('rule', node)
            if (node in f.marked and not standing.get(obj)) or obj in c['refused']:
                dead = {r for r in f.recs if r[0] == 'rule' and r[1] == node}
                version('rule', node, 'none', dead)
        for cert in CERTS:
            obj = ('renewal', cert)
            if obj in c['refused']:
                dead = {r for r in f.recs if r[0] == 'renewal' and r[1] == cert}
                version('renewal', cert, 'forever', dead)
    else:
        raise ValueError(act)

    # the head, design 5.1: every refusal, every removal, every standing version with its text
    ref = set(c['ref'])
    rem = set(c['rem'])
    for el in new:
        if el.kind in ('revoked', 'retired'):
            ref.add((el.kind, el.subj))
        if el.kind == 'removed':
            rem.add(el.id)
    vers = fs(v for vs in standing.values() for v in vs)
    hafter = set()
    htafter = set()
    for h in c['hif']:
        hafter |= {h.id} | h.after
        htafter |= {h.id} | h.tafter
    if act == 'seal':
        dead_heads = {r for r in f.recs if r[0] == 'head'}
        hafter |= dead_heads
        htafter |= dead_heads
        for h in c['heads_present']:
            hafter.add(h.id)
            htafter.add(h.id)
    if opt.rotate and act == 'recreate':
        rem = {el.id for el in new if el.kind == 'removed'}
    head = El('head', 'store', height, w.fresh(), (fs(ref), fs(rem), vers),
              cut_heads(hafter, height, opt.K), fs(htafter), cur['key'])
    new.append(head)
    for h in c['hif']:
        for i, e in f.entries.items():
            if e.el.id == h.id and e.loc == 'store':
                prune.add(i)
    for i in prune:
        if i in f.entries:
            del f.entries[i]
        f.recs[i] = now
    for wid in witnesses:
        f.recs[wid] = now
    for el in new:
        f.entries[el.id] = Entry(el, now, 'store', now)
        f.know.add(el)
    return True

# ----------------------------------------------------------------------------------------------
# Other programs and people, design section 2 (T1 to T15), with locations added


def kp_sync(w, a, b, both=True):
    """KeePass Synchronize (both files written) or Keepass2Android's merge (both=False: into a)."""
    fa, fb = w.files[a], w.files[b]
    ents = {}
    for i in set(fa.entries) | set(fb.entries):
        ea, eb = fa.entries.get(i), fb.entries.get(i)
        if ea and eb:
            m = max(ea.mtime, eb.mtime)
            loc = ea if ea.loct >= eb.loct else eb
            ents[i] = Entry(ea.el, m, loc.loc, loc.loct)
        else:
            e = ea or eb
            ents[i] = Entry(e.el, e.mtime, e.loc, e.loct)
    recs = dict(fa.recs)
    for i, t in fb.recs.items():
        recs[i] = max(t, recs.get(i, t))
    for i, t in list(recs.items()):
        if i in ents:
            if ents[i].mtime <= t:
                del ents[i]
            else:
                del recs[i]                                              # T5: newer entry, record goes
    know = fa.know | fb.know
    marked = fa.marked | fb.marked
    for fi in ((a, b) if both else (a,)):
        f = w.files[fi]
        f.entries = {i: Entry(e.el, e.mtime, e.loc, e.loct) for i, e in ents.items()}
        f.recs = dict(recs)
        f.know = set(know)
        f.marked = set(marked)
    if both:
        fa.overwritten = fb.overwritten = fa.overwritten or fb.overwritten
        fa.sealed = fb.sealed = fa.sealed or fb.sealed
    else:
        fa.overwritten = fa.overwritten or fb.overwritten
        fa.sealed = fa.sealed or fb.sealed


def merge_into(w, a, b, how):
    """KeePass's other four methods ('kpo'), KeePassXC ('xc'), KeePassDX ('dx'): into a only."""
    fa, fb = w.files[a], w.files[b]
    if how == 'kpo':                                   # T6: no arriving record; own records applied
        for i, e in fb.entries.items():
            if i in fa.entries:
                if e.mtime > fa.entries[i].mtime:
                    fa.entries[i].mtime = e.mtime
                continue
            if i in fa.recs and e.mtime <= fa.recs[i]:
                continue
            if i in fa.recs:
                del fa.recs[i]
            fa.entries[i] = Entry(e.el, e.mtime, e.loc, e.loct)
    elif how == 'xc':                                  # T6: nothing applied, nothing copied
        for i, e in fb.entries.items():
            loc = e.loc
            if loc == 'bin' and not w.opt.xc_bin_shared:
                loc = 'other'                          # ground-keepassxc 10: a foreign bin is a group
            if i in fa.entries:
                ea = fa.entries[i]
                if e.loct > ea.loct:
                    ea.loc, ea.loct = loc, e.loct
                ea.mtime = max(ea.mtime, e.mtime)
            else:
                fa.entries[i] = Entry(e.el, e.mtime, loc, e.loct)
    elif how == 'dx':                                  # T7, measured-android 4.3
        for i, t in fb.recs.items():
            if i in fa.entries and fa.entries[i].mtime <= t:
                del fa.entries[i]
        for i, e in fb.entries.items():
            if i in fa.entries:
                ea = fa.entries[i]
                if e.mtime > ea.mtime:
                    ea.mtime, ea.loc, ea.loct = e.mtime, e.loc, e.loct
                elif e.mtime == ea.mtime:
                    ea.loc, ea.loct = e.loc, e.loct    # follows the arriving copy at equal times
                continue
            if i in fa.recs and e.mtime <= fa.recs[i]:
                continue
            if i in fb.recs and e.mtime <= fb.recs[i]:
                continue
            fa.entries[i] = Entry(e.el, e.mtime, e.loc, e.loct)
    else:
        raise ValueError(how)
    fa.know |= fb.know
    fa.marked |= fb.marked
    fa.overwritten = fa.overwritten or fb.overwritten
    fa.sealed = fa.sealed or fb.sealed


def pick(f, sel):
    """sel = (kind, which) with which 'new' (highest), 'old' (lowest) or an index."""
    kind, which = sel
    cands = sorted((i for i, e in f.entries.items() if e.el.kind == kind and (e.loc == 'store' or which == 'any')),
                   key=lambda i: (i[2], i[3]))
    if not cands:
        return None
    if which == 'old':
        return cands[0]
    return cands[-1]


def fault(w, fi, what, sel=None):
    f = w.files[fi]
    now = w.tick()
    if what == 'clear':
        f.recs = {}
        return True
    i = pick(f, sel) if sel else None
    if i is None:
        return False
    e = f.entries[i]
    if what == 'delr':           # T4: deleted for good, with a deletion record (any program)
        del f.entries[i]
        f.recs[i] = now
    elif what == 'del0':         # T4: deleted leaving no record (a script; see the critique)
        del f.entries[i]
    elif what == 'bin':          # T4: Delete with the bin on, in KeePass, KeePassXC, K2A ("No"), KeePassDroid
        e.loc, e.loct = 'bin', now
    elif what == 'dxbin':        # KeePassDX's Delete: to the bin and the time touched (ground-android 4.2)
        e.loc, e.loct, e.mtime = 'bin', now, now
    elif what == 'move':         # a drag or Move to another group: KeePass, KeePassXC, K2A
        e.loc, e.loct = 'other', now
    elif what == 'touch':        # T3: KeePassDroid's Save with nothing changed, or a title edit
        e.mtime = now
    else:
        raise ValueError(what)
    return True


def over(w, a, b):
    """T10: file b written over file a (KeePassDroid's save, K2A's overwrite, a restore)."""
    g = w.files[b].clone()
    g.overwritten = True
    w.files[a] = g

# ----------------------------------------------------------------------------------------------
# Histories


def setup(nfiles, opt):
    """The common start: one authority, c0 (p, q) and c1 (p), names p and q, root r, N rw."""
    w = World(nfiles, opt)
    f = w.files[0]
    now = w.tick()
    base = [El('auth', 'A0', 0, w.fresh(), 'k0')]
    for x in PROFILES:
        base.append(El('profile', x, 0, w.fresh()))
    base.append(El('client', 'c0', 0, w.fresh(), ('A0', fs({'p', 'q'}))))
    base.append(El('client', 'c1', 0, w.fresh(), ('A0', fs({'p'}))))
    rroot = El('rule', 'root', 0, w.fresh(), 'r')
    rn = El('rule', 'N', 0, w.fresh(), 'rw')
    head = El('head', 'store', 0, w.fresh(), (fs(), fs(), fs({rroot, rn})))
    for el in base + [rroot, rn, head]:
        f.entries[el.id] = Entry(el, now, 'store', now)
        f.know.add(el)
    f.marked = {'root', 'N'}
    for k in range(1, nfiles):
        w.files[k] = f.clone()
    return w


def apply_step(w, step):
    op = step[0]
    if op == 'gate':
        return gate(w, *step[1:])
    if op == 'sync':
        kp_sync(w, step[1], step[2], True)
        return True
    if op == 'k2a':
        kp_sync(w, step[1], step[2], False)
        return True
    if op in ('kpo', 'xc', 'dx'):
        merge_into(w, step[1], step[2], op)
        return True
    if op == 'fault':
        return fault(w, *step[1:])
    if op == 'over':
        over(w, step[1], step[2])
        return True
    raise ValueError(op)


def fmt(step):
    if step[0] == 'gate':
        return f"gate {step[1]} {step[2]} " + ' '.join(
            ''.join(sorted(a)) if isinstance(a, frozenset) else str(a) for a in step[3:])
    if step[0] == 'fault':
        return f"{step[2]} {step[1]}" + (f" {step[3][0]}({step[3][1]})" if len(step) > 3 and step[3] else '')
    return f"{step[0]} {step[1]} {step[2]}"


def leq(d, o):
    """d is no wider than o: reach included, every renewal at least as strict."""
    if not d.reach() <= o.reach():
        return False
    ds, os_ = d.strict(), o.strict()
    for c in CERTS:
        if c in o.admitted and ds[c] < os_[c]:
            return False
    return True


def revocation_undone(d, f):
    """A certificate admitted that IK holds revoked, or issued by an authority IK holds retired."""
    if not d.whole or d.served in (None, 'undecided'):
        return False
    ik = ideal(f.know, f.marked)
    for c in d.admitted:
        if ('revoked', c) in ik.ctx['ref']:
            return True
    if ('retired', d.served) in ik.ctx['ref']:
        return bool(d.admitted)
    return False


def check(w, prev_vals, step, opt):
    """Every check of the critique on every file after one step. Returns a list of class names."""
    out = []
    for fi, f in enumerate(w.files):
        d = read(f, opt)
        ie = ideal([e.el for e in f.entries.values()], f.marked, causal=False)
        if not leq(d, ie):
            out.append('wider-than-evidence')
        if revocation_undone(d, f) and not f.overwritten:
            out.append('revocation-undone')
        if not f.overwritten and not f.sealed:
            ik = ideal(f.know, f.marked)
            if not leq(d, ik):
                out.append('wider-than-knowledge')
                pv = prev_vals[fi]
                if pv is not None and not pv.whole and d.whole and step[0] in ('sync', 'k2a', 'kpo', 'xc', 'dx'):
                    out.append('detected-loss-healed-wider')
        if step[0] == 'fault' and step[1] == fi and prev_vals[fi] is not None and step[2] != 'clear':
            if not leq(d, prev_vals[fi]):
                out.append('one-loss')
    return out


def run(history, nfiles, opt, verbose=False):
    w = setup(nfiles, opt)
    prev = [read(f, opt) for f in w.files]
    found = []
    for k, step in enumerate(history):
        ok = apply_step(w, step)
        cls = check(w, prev, step, opt)
        if verbose:
            print(f"   {k + 1:2}. {fmt(step):38} {'' if ok else '(no effect)'}")
            for fi, f in enumerate(w.files):
                print(f"        file {fi}: {read(f, opt).show()}")
            if cls:
                print(f"        -> {sorted(set(cls))}")
        found += cls
        prev = [read(f, opt) for f in w.files]
    return set(found), w

# ----------------------------------------------------------------------------------------------
# The counterexamples, shortest found, replayed


def ce_list():
    N = 'N'
    out = []
    out.append(('CE1', 'two Deletes to the recycle bin, a gate write in a stale copy, KeePass Synchronize: '
                'a narrowing silently undone in both files', 2, Opt(), [
                    ('gate', 0, 'rule', N, 'r'),
                    ('gate', 1, 'pair', 'new', fs({'p'})),
                    ('fault', 0, 'bin', ('rule', 'new')),
                    ('fault', 0, 'bin', ('head', 'new')),
                    ('sync', 0, 1)]))
    out.append(('CE1-fixed', 'CE1 with every verifying element counted wherever it sits', 2, Opt(anywhere=True),
                out[-1][4]))
    out.append(('CE1-binheads', 'CE1 with the security critic\'s change: a binned head counts as in force', 2,
                Opt(bin_heads=True), out[-2][4]))
    out.append(('CE1-otherreading', 'CE1 under the other reading of 5.2 point 4 (a binned head\'s versions '
                'still name what they replace)', 2, Opt(named_by_present=True), out[-3][4]))
    out.append(('CE1-move', 'CE1 with Move to another group in place of Delete, under the other reading', 2,
                Opt(named_by_present=True), [
                    ('gate', 0, 'rule', N, 'r'),
                    ('gate', 1, 'pair', 'new', fs({'p'})),
                    ('fault', 0, 'move', ('rule', 'new')),
                    ('fault', 0, 'move', ('head', 'new')),
                    ('sync', 0, 1)]))
    out.append(('CE2','the same two Deletes, then a KeePassXC merge into the stale copy', 2, Opt(), [
        ('gate', 0, 'rule', N, 'r'),
        ('fault', 0, 'bin', ('rule', 'new')),
        ('fault', 0, 'bin', ('head', 'new')),
        ('xc', 1, 0)]))
    out.append(('CE3', 'a revocation, its entry and the head moved to another group, a KeePassXC merge into '
                'the stale copy: the revoked client admitted', 2, Opt(), [
                    ('gate', 0, 'revoke', 'c0'),
                    ('fault', 0, 'move', ('revoked', 'new')),
                    ('fault', 0, 'move', ('head', 'new')),
                    ('xc', 1, 0)]))
    out.append(('CE3-bin', 'CE3 with the recycle bin in place of the move, where KeePassXC does not merge the '
                'bin (ground-keepassxc 10)', 2, Opt(xc_bin_shared=False), [
                    ('gate', 0, 'revoke', 'c0'),
                    ('fault', 0, 'bin', ('revoked', 'new')),
                    ('fault', 0, 'bin', ('head', 'new')),
                    ('xc', 1, 0)]))
    out.append(('CE3-fixed', 'CE3 with every verifying element counted wherever it sits', 2, Opt(anywhere=True),
                out[-2][4]))
    out.append(('CE3-binheads', 'CE3 with the security critic\'s change alone (a binned head in force)', 2,
                Opt(bin_heads=True), out[-3][4]))
    out.append(('CE4', 'a retirement (Delete authority) moved out with the head, a KeePassXC merge: the retired '
                'authority served with its clients', 2, Opt(), [
                    ('gate', 0, 'retire'),
                    ('fault', 0, 'move', ('retired', 'new')),
                    ('fault', 0, 'move', ('head', 'new')),
                    ('xc', 1, 0)]))
    # CE5: the 64-height cut
    K = 64
    h = [('gate', 0, 'rule', N, 'r')]
    for k in range(K + 1):
        h.append(('gate', 1, 'renew', 'c1', 'allowed' if k % 2 else 'until'))
    h += [('gate', 1, 'rule', N, 'rw'),
          ('fault', 0, 'delr', ('rule', 'new')),
          ('fault', 0, 'delr', ('head', 'new')),
          ('sync', 0, 1)]
    out.append(('CE5', f'the {K}-height cut: a narrowing deleted for good with both records in a copy {K} '
                'writes behind, KeePass Synchronize: the records are taken for ancestors', 2, Opt(K=K), h))
    out.append(('CE5-unbounded', 'CE5 with the cut removed (every ancestor named)', 2, Opt(K=None), h))
    out.append(('CE6', 'T3: unchanged saves on the phone of two pruned elements defeat the records under '
                'Synchronize (design 6.2 row 5 and scenario 8)', 2, Opt(), [
                    ('gate', 0, 'rule', N, 'r'),
                    ('fault', 1, 'touch', ('rule', 'old')),
                    ('fault', 1, 'touch', ('head', 'old')),
                    ('fault', 0, 'del0', ('rule', 'new')),
                    ('fault', 0, 'del0', ('head', 'new')),
                    ('sync', 0, 1)]))
    out.append(('CE6-no-touch', 'CE6 without the two unchanged saves', 2, Opt(), [
        ('gate', 0, 'rule', N, 'r'),
        ('fault', 0, 'del0', ('rule', 'new')),
        ('fault', 0, 'del0', ('head', 'new')),
        ('sync', 0, 1)]))
    out.append(('CE7', 'a backup restored over the file, the gate writes, the phone (newer) bins two entries, '
                'Synchronize', 3, Opt(), [
                    ('over', 2, 0),                      # file 2 is the backup
                    ('gate', 0, 'rule', N, 'r'),
                    ('sync', 0, 1),                      # the phone has the narrowing
                    ('over', 0, 2),                      # the backup restored on the PC
                    ('gate', 0, 'pair', 'new', fs({'p'})),
                    ('fault', 1, 'bin', ('rule', 'new')),
                    ('fault', 1, 'bin', ('head', 'new')),
                    ('sync', 0, 1)]))
    out.append(('CE8', 'same elements, different verdicts: a record deleted by hand, KeePassXC both ways', 2,
                Opt(), [
                    ('fault', 0, 'delr', ('client', 'old')),
                    ('xc', 0, 1),
                    ('xc', 1, 0)]))
    out.append(('CE10', 'one loss widens: a copy that took the newest version in the bin through a merge '
                'without records; one Delete of the head there', 2, Opt(), [
                    ('gate', 1, 'rule', N, 'none'),
                    ('fault', 1, 'bin', ('rule', 'new')),
                    ('xc', 0, 1),
                    ('fault', 0, 'bin', ('head', 'new'))]))
    out.append(('CE11', 'the security critic\'s S1 change (a new store key at Recreate) against a concurrent '
                'narrowing: the narrowing under the old key is dropped, not met', 2, Opt(rotate=True), [
                    ('gate', 0, 'rule', N, 'r'),
                    ('gate', 1, 'recreate'),
                    ('gate', 1, 'pair', 'new', fs({'p', 'q'})),
                    ('sync', 0, 1)]))
    out.append(('CE11-design', 'CE11 with the key carried forward, as the design has it', 2, Opt(), out[-1][4]))
    out.append(('CE12', 'S1\'s change against a concurrent profile removal: the name comes back', 2,
                Opt(rotate=True), [
                    ('gate', 0, 'prem', 'q'),
                    ('gate', 1, 'rule', 'root', 'pq'),
                    ('gate', 1, 'recreate'),
                    ('gate', 1, 'pair', 'new', fs({'p', 'q'})),
                    ('sync', 0, 1)]))
    out.append(('CE12-design', 'CE12 with the key carried forward, as the design has it', 2, Opt(), out[-1][4]))
    out.append(('CE13', 'one Move in each of two copies, joined by a Keepass2Android merge: the revocation, '
                'spread by an import and a KeePassXC merge, undone', 3, Opt(), [
                    ('gate', 1, 'revoke', 'c1'),
                    ('kpo', 2, 1),
                    ('xc', 0, 2),
                    ('fault', 2, 'move', ('revoked', 'old')),
                    ('fault', 0, 'move', ('head', 'new')),
                    ('k2a', 2, 0)]))
    out.append(('CE9','KeePassDX: Delete moves an element to the bin and touches its time; a KeePassDX merge '
                'from a copy where it is unmoved follows the newer time (design 2: "not relied on")', 2,
                Opt(), [
                    ('gate', 0, 'rule', N, 'r'),
                    ('fault', 0, 'dxbin', ('rule', 'new')),
                    ('fault', 0, 'dxbin', ('head', 'new')),
                    ('dx', 1, 0)]))
    return out


def replay_ces(names=None):
    for name, title, nf, opt, hist in ce_list():
        if names and name not in names:
            continue
        print(f"== {name}: {title}")
        print(f"   options: K={opt.K} anywhere={opt.anywhere} bin_heads={opt.bin_heads} "
              f"xc_bin_shared={opt.xc_bin_shared} rotate={opt.rotate} named_by_present={opt.named_by_present}; "
              f"files={nf}; steps={len(hist)}")
        long_ = len(hist) > 20
        if long_:
            found, w = run(hist, nf, opt, verbose=False)
            for k, step in enumerate(hist):
                if k < 2 or k >= len(hist) - 4:
                    print(f"   {k + 1:2}. {fmt(step)}")
                elif k == 2:
                    print(f"       ... {len(hist) - 6} more of the same ...")
            for fi, f in enumerate(w.files):
                print(f"        file {fi}: {read(f, opt).show()}")
        else:
            found, w = run(hist, nf, opt, verbose=True)
        for fi, f in enumerate(w.files):
            ik = ideal(f.know, f.marked)
            ie = ideal([e.el for e in f.entries.values()], f.marked, causal=False)
            print(f"        file {fi} IK: {ik.show()}")
            print(f"        file {fi} IE: {ie.show()}")
        print(f"   RESULT: {sorted(found) if found else 'no failure'}")
        print()

# ----------------------------------------------------------------------------------------------
# Random search


def random_step(rng, nf, cfg):
    r = rng.random()
    fi = rng.randrange(nf)
    gj = rng.randrange(nf)
    while gj == fi and nf > 1:
        gj = rng.randrange(nf)
    if r < 0.40:
        a = rng.choice(['rule', 'rule', 'renew', 'revoke', 'pair', 'prem', 'padd', 'recreate', 'seal'])
        if a == 'rule':
            return ('gate', fi, 'rule', rng.choice(NODES), rng.choice(DOCNAMES))
        if a == 'renew':
            return ('gate', fi, 'renew', rng.choice(CERTS), rng.choice(['forever', 'until', 'allowed']))
        if a == 'revoke':
            return ('gate', fi, 'revoke', rng.choice(CERTS))
        if a == 'pair':
            return ('gate', fi, 'pair', 'new', fs(rng.sample(PROFILES, rng.randint(1, 2))))
        if a in ('prem', 'padd'):
            return ('gate', fi, a, rng.choice(PROFILES))
        if a == 'recreate':
            return ('gate', fi, 'recreate')
        return ('gate', fi, 'seal')
    if r < 0.70 or not cfg['faults']:
        return (rng.choice(cfg['merges']), fi, gj)
    what = rng.choice(cfg['faults'])
    if what == 'clear':
        return ('fault', fi, 'clear')
    if what == 'over':
        return ('over', fi, gj)
    kind = rng.choice(['rule', 'rule', 'renewal', 'head', 'head', 'revoked', 'removed', 'profile', 'client'])
    return ('fault', fi, what, (kind, rng.choice(['new', 'old'])))


CONFIGS = {
    'A merges only': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'], faults=[]),
    'B + bins and moves': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'], faults=['bin', 'move', 'dxbin']),
    'C + deletions with a record': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'], faults=['delr', 'bin', 'move']),
    'D + unchanged saves (T3)': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'], faults=['delr', 'bin', 'touch']),
    'E operator set: Sync and K2A, bins, records, T3': dict(merges=['sync', 'k2a'],
                                                            faults=['delr', 'bin', 'touch']),
    'F everything but overwrite': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'],
                                       faults=['delr', 'del0', 'bin', 'move', 'dxbin', 'touch', 'clear']),
    'G + overwrite and restore': dict(merges=['sync', 'k2a', 'kpo', 'xc', 'dx'],
                                      faults=['delr', 'bin', 'touch', 'over']),
}


def minimize(hist, nf, opt, cls):
    h = list(hist)
    changed = True
    while changed:
        changed = False
        for k in range(len(h)):
            cand = h[:k] + h[k + 1:]
            found, _ = run(cand, nf, opt)
            if cls in found:
                h = cand
                changed = True
                break
    return h


def search(n_per, seed=1):
    rng = random.Random(seed)
    for (label, opt) in [('as written, K unbounded', Opt()), ('as written, K=3 (stands for 64)', Opt(K=3)),
                         ('fix: count elements anywhere', Opt(anywhere=True)),
                         ('fix: binned heads in force (critic S3)', Opt(bin_heads=True))]:
        print(f"== random histories, {label}")
        for cname, cfg in CONFIGS.items():
            for nf in (3, 4):
                counts = {}
                shortest = {}
                for _ in range(n_per):
                    L = rng.randint(4, 14)
                    hist = [random_step(rng, nf, cfg) for _ in range(L)]
                    found, _ = run(hist, nf, opt)
                    for cls in found:
                        counts[cls] = counts.get(cls, 0) + 1
                        if cls not in shortest or len(hist) < len(shortest[cls]):
                            shortest[cls] = hist
                line = ', '.join(f"{k} x{v}" for k, v in sorted(counts.items())) or 'no failure'
                print(f"   {cname:48} {nf} files {n_per:6} histories: {line}")
                for cls in ('revocation-undone', 'detected-loss-healed-wider', 'wider-than-evidence', 'one-loss',
                            'wider-than-knowledge'):
                    if cls in shortest:
                        m = minimize(shortest[cls], nf, opt, cls)
                        print(f"      shortest {cls}: " + '; '.join(fmt(s) for s in m))
        print()

# ----------------------------------------------------------------------------------------------
# Laws of the value functions, checked by enumeration


def laws(n=200000, seed=2):
    rng = random.Random(seed)
    fams = [(k, S) for k in ('G', 'L') for S in (fs(c) for c in _subsets(WORDS))]
    print(f"== the document lattice: {len(fams)} functions per profile")
    bad = dict(closed=0, comm=0, assoc=0, idem=0, below=0, mono=0, compose=0)
    for _ in range(n):
        f, g, h = rng.choice(fams), rng.choice(fams), rng.choice(fams)
        x = fs(rng.sample(WORDS, rng.randint(0, 8)))
        y = x | fs(rng.sample(WORDS, rng.randint(0, 8)))
        m = meetf(f, g)
        if m not in fams:
            bad['closed'] += 1
        if meetf(f, g) != meetf(g, f):
            bad['comm'] += 1
        if meetf(meetf(f, g), h) != meetf(f, meetf(g, h)):
            bad['assoc'] += 1
        if meetf(f, f) != f:
            bad['idem'] += 1
        if not (grant(applyf(m, x)) <= grant(applyf(f, x)) and grant(applyf(m, x)) <= grant(applyf(g, x))):
            bad['below'] += 1
        if not grant(applyf(m, x)) <= grant(applyf(m, y)):
            bad['mono'] += 1
        # a narrower function at the parent never widens the child: f(g(x)) vs f(m(x))
        if not grant(applyf(h, applyf(m, x))) <= grant(applyf(h, applyf(g, x))):
            bad['compose'] += 1
        # the meet is exactly the pointwise meet of what the two give
        if applyf(m, x) != (applyf(f, x) & applyf(g, x)) and grant(applyf(m, x)) != (
                grant(applyf(f, x)) & grant(applyf(g, x))):
            bad['below'] += 1
    print(f"   {n} random triples and inputs: " + ', '.join(f"{k} {v}" for k, v in bad.items()))
    # expressibility: how many meets of two documents are no single AclDocument
    ex = 0
    tot = 0
    for d1 in DOCNAMES:
        for d2 in DOCNAMES:
            for x in PROFILES:
                m = meetf(docf(d1, x), docf(d2, x))
                tot += 1
                if m[0] == 'L' and m[1] != ALL:
                    ex += 1
    print(f"   meets of two of the model's documents, per profile, that are a limit L(S) with S not all: "
          f"{ex} of {tot} (no AclDocument writes these; the resolver must carry them)")
    # remove-wins set: union of states, order independence, and remove-wins against an unseen add
    print("== the remove-wins set and the register: functions of the union, checked over random states")
    viol = 0
    for _ in range(20000):
        els = _random_profile_state(rng)
        a = set(rng.sample(els, rng.randint(0, len(els))))
        b = set(rng.sample(els, rng.randint(0, len(els))))
        c = set(rng.sample(els, rng.randint(0, len(els))))
        v = lambda s: ideal(s, set()).plist
        if v(a | b) != v(b | a) or v((a | b) | c) != v(a | (b | c)) or v(a | a) != v(a):
            viol += 1
    print(f"   20000 random triples: {viol} violations (expected 0: the value is a function of the union)")


def _subsets(xs):
    xs = list(xs)
    for m in range(1 << len(xs)):
        yield [xs[k] for k in range(len(xs)) if m >> k & 1]


def _random_profile_state(rng):
    els = []
    removals = []
    for k in range(rng.randint(1, 6)):
        if rng.random() < 0.5:
            r = El('removed', 'p', k, 1000 + k)
            removals.append(r.id)
            els.append(r)
        else:
            seen = fs(rng.sample(removals, rng.randint(0, len(removals))))
            els.append(El('profile', 'p', k, 2000 + k, None, seen))
    return els


if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 3000
    if what in ('ce', 'all'):
        replay_ces()
    if what in ('laws', 'all'):
        laws()
        print()
    if what in ('search', 'all'):
        search(n)
