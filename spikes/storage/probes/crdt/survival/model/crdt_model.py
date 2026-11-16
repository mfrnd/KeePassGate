"""Executable model of option `crdt` (see ..\\option-crdt.md).

Run:  uv run --no-project python -I -X utf8 crdt_model.py [quick|full]

What is modeled, and on what basis each transport behavior rests, is in
option-crdt.md sections 1 and 9. Nothing here touches a real database: a "file"
is a dict of entries, a dict of deletion records and a small user tree.
"""
import copy
import itertools
import random
import sys
import time

# ----------------------------------------------------------------- the lattice
# What one node answers one profile: a function of what arrives from above.
#   ('const', S): the node names the profile and grants S, whatever is above.
#   ('cap',   S): the node gives what arrives from above, cut to S (a limit).
# identity (a silent node) is ('cap', ALL). The meet is pointwise intersection.
ALL = frozenset('rw')


def const(s=''):
    return ('const', frozenset(s))


def cap(s):
    return ('cap', frozenset(s))


IDENT = cap(ALL)
REFUSE = const('')


def meet(f, g):
    kind = 'const' if f[0] == 'const' and g[0] == 'const' else 'cap'
    return (kind, f[1] & g[1])


def limit(f):
    return ('cap', f[1])


def app(f, x):
    return f[1] if f[0] == 'const' else f[1] & x


DOCS = {'rw': const('rw'), 'r': const('r'), 'none': const(''), 'inherit': IDENT}


# -------------------------------------------------------------------- elements
class El:
    __slots__ = ('id', 'kind', 'obj', 'after', 'data', 'height', 'key')

    def __init__(self, id, kind, obj, after=(), data=None, height=0, key='K'):
        self.id, self.kind, self.obj = id, kind, obj
        self.after, self.data, self.height, self.key = frozenset(after), data, height, key


class Rep:
    """One copy of the file."""

    def __init__(self):
        self.entries = {}   # uuid -> [element id, modification time, content intact]
        self.dels = {}      # uuid -> deletion time (DeletedObjects)
        self.nodes = {}     # user node -> [marker (KPG.Node value or None), modification time]

    def clone(self):
        r = Rep()
        r.entries = {u: list(v) for u, v in self.entries.items()}
        r.dels = dict(self.dels)
        r.nodes = {n: list(v) for n, v in self.nodes.items()}
        return r


class Flags:
    """The parts of the read rule, so each can be switched off (ablation)."""

    def __init__(self, snapshot=True, T=True, recrule=True, headtag=True, causal_add=True, cover=True,
                 dtag=True):
        self.snapshot, self.T, self.recrule = snapshot, T, recrule
        self.headtag, self.causal_add, self.cover = headtag, causal_add, cover and snapshot
        self.dtag = dtag


FULL = Flags()


class World:
    def __init__(self, nrep, flags=FULL):
        self.EL = {}
        self.n = 0
        self.t = 10
        self.flags = flags
        self.copies = {}            # copy node -> node it was copied from, when that node was ruled
        self.ncopy = 0
        self.clients = ['c0']
        self.reps = [Rep() for _ in range(nrep)]
        self.K = [set() for _ in range(nrep)]       # elements this file has been given
        self.Kever = [set() for _ in range(nrep)]   # the same, not reset by an overwrite
        base = self.reps[0]
        base.nodes = {'root': ['root', 1], 'N': [None, 1]}
        els = [El('auth:a0', 'auth', 'a0'),
               self.mk('padd', 'pf', height=1),
               El('rec:c0', 'rec', 'c0', data=('a0', frozenset(['pf']))),
               self.mk('ver', 'rule:root', data=DOCS['r'])]
        for e in els:
            self.EL[e.id] = e
            base.entries[e.id] = [e.id, 1, True]
        self.write_head(base, set())
        ids = set(base.entries)
        for i in range(nrep):
            self.reps[i] = base.clone()
            self.K[i] = set(ids)
            self.Kever[i] = set(ids)

    def clone(self):
        w = World.__new__(World)
        w.EL, w.n, w.t, w.flags = dict(self.EL), self.n, self.t, self.flags
        w.copies, w.clients, w.ncopy = dict(self.copies), list(self.clients), self.ncopy
        w.reps = [r.clone() for r in self.reps]
        w.K = [set(k) for k in self.K]
        w.Kever = [set(k) for k in self.Kever]
        return w

    def now(self):
        self.t += 10
        return self.t

    def mk(self, kind, obj, after=(), data=None, height=0, key='K'):
        self.n += 1
        if kind == 'head':
            id = 'H%03d.%d' % (height, self.n)      # keyed tag and height, readable off the uuid
        else:
            id = '%s:%s#%d' % (kind, obj, self.n)   # digest over content and a nonce
        e = El(id, kind, obj, after, data, height, key)
        self.EL[id] = e
        return e

    # ----------------------------------------------------------- the read rule
    def fold(self, rep, flags=None):
        fl = flags or self.flags
        EL = self.EL
        raw = [EL[eid] for u, (eid, mt, ok) in rep.entries.items() if ok and u == eid]
        keyknown = any(e.kind == 'auth' and e.key == 'K' for e in raw)
        ents = {e.id: e for e in raw if e.key == 'K'} if keyknown else {}
        named = set(rep.dels) if fl.recrule else set()
        heads = [e for e in ents.values() if e.kind == 'head']
        heads_live = [h for h in heads if h.id not in named]
        refusals, prem = set(), set()
        for e in ents.values():
            if e.kind == 'ref':
                refusals.add(e.data)
            elif e.kind == 'prem':
                prem.add((e.obj, e.id))
        if fl.snapshot:
            for h in heads:
                refusals |= h.data['ref']
                prem |= h.data['prem']
        if fl.T and keyknown:
            for u in rep.dels:
                if u.startswith('T|'):
                    p = u.split('|')
                    refusals.add((p[1], p[2]))
        vers, supers, lost = {}, set(), {}
        for e in ents.values():
            if e.kind in ('ver', 'pin'):
                if e.id in named:
                    supers.add(e.obj)
                else:
                    vers.setdefault(e.obj, {})[e.id] = e
        if fl.snapshot:
            for h in heads_live:
                for vid in h.data['regs']:
                    e = EL[vid]
                    vers.setdefault(e.obj, {})[vid] = e
        if fl.dtag and keyknown:
            # A decision's identifier carries its kind and a keyed hash of what it is about, so
            # the deletion record of one still tells which object lost a decision. (The model
            # reads that off the identifier's text; a foreign file cannot make such a record.)
            deadall = set()
            for V in vers.values():
                for e in V.values():
                    deadall |= e.after
            for u in rep.dels:
                e = EL.get(u)
                if e is None or e.key != 'K':
                    continue
                if e.kind == 'ref':
                    refusals.add(e.data)                # a deleted refusal still refuses
                elif e.kind == 'prem':
                    prem.add((e.obj, e.id))             # a deleted removal still removes
                elif e.kind in ('ver', 'pin') and u not in deadall and u not in vers.get(e.obj, {}):
                    lost.setdefault(e.obj, set()).add(u)   # a version nothing replaced or carries
        store = bool(ents) or any(u[0] in 'HT' for u in rep.dels)
        status = 'ok'
        if not heads_live:
            status = 'incomplete' if store else 'nostore'
        elif fl.headtag:
            # a head was deleted here that is neither in force nor an ancestor of one in force
            known = set(h.id for h in heads_live)
            for h in heads_live:
                known |= h.after
            if any(u[0] == 'H' and u not in known for u in rep.dels):
                status = 'incomplete'
        if status == 'ok' and fl.cover:
            # an entry of the store that does not verify: damage is never passed over
            if any(not ok for u, (eid, mt, ok) in rep.entries.items() if u == eid):
                status = 'incomplete'
        if status == 'ok' and fl.cover:
            # a decision that no head here accounts for: its head is missing
            c_ref, c_prem, c_regs, deadall = set(), set(), set(), set()
            for h in heads:
                c_ref |= h.data['ref']
                c_prem |= h.data['prem']
                c_regs |= h.data['regs']
            for V in vers.values():
                for e in V.values():
                    deadall |= e.after
            for e in ents.values():
                if (e.kind == 'ref' and e.data not in c_ref) or \
                        (e.kind == 'prem' and (e.obj, e.id) not in c_prem) or \
                        (e.kind in ('ver', 'pin') and e.id not in named and e.id not in deadall
                         and e.id not in c_regs):
                    status = 'incomplete'
        # authorities: a foreign authority that arrived intact counts toward "undecided"
        auths = [e.obj for e in raw if e.kind == 'auth' and e.id not in named
                 and ('retired', e.obj) not in refusals]
        served = auths[0] if len(auths) == 1 and status == 'ok' and EL['auth:' + auths[0]].key == 'K' else None
        # the profile list: an addition counts when it has seen every removal of its name
        names = set()
        for e in ents.values():
            if e.kind == 'padd' and e.id not in named:
                if fl.causal_add:
                    ok = set(r for (n, r) in prem if n == e.obj) <= e.after
                else:
                    # the scheme the critics broke: an addition and its removal share a
                    # generation number, and an addition counts when no removal carries its number
                    ok = e.height not in set(EL[r].height for (n, r) in prem if n == e.obj)
                if ok:
                    names.add(e.obj)
        valid, adm = {}, {}
        for e in ents.values():
            if e.kind == 'rec' and e.id not in named:
                issuer, profs = e.data
                if ('revoked', e.obj) in refusals or ('retired', issuer) in refusals:
                    continue
                valid[e.obj] = profs & names
                if served == issuer:
                    adm[e.obj] = profs & names
        memo = {}

        def freg(obj, depth):
            V = vers.get(obj)
            if obj in lost:
                return REFUSE
            if not V:
                return REFUSE if obj in supers else None
            dead = set()
            for e in V.values():
                dead |= e.after
            f = None
            for i, e in V.items():
                if i in dead:
                    continue
                g = limit(fnode(e.data[1], depth + 1)) if e.kind == 'pin' else e.data
                f = g if f is None else meet(f, g)
            return f

        def fnode(n, depth=0):
            if n in memo:
                return memo[n]
            if depth > 8:
                return REFUSE
            f = freg('rule:' + n, depth)
            m = rep.nodes[n][0] if n in rep.nodes else None
            if f is None:
                f = REFUSE if m == n else IDENT   # marked, and no rule known: refuse
            if m and m != n:
                f = meet(f, limit(fnode(m, depth + 1)))   # a copy: the original's rule caps it
            memo[n] = f
            return f

        # words_raw: what the rules say. words: what a client gets, which needs a store that is
        # whole and an authority that is served.
        words, words_raw, funcs = {}, {}, {}
        for n in rep.nodes:
            f = fnode(n)
            funcs[n] = f
            up = app(fnode('root'), frozenset()) if n != 'root' else frozenset()
            words_raw[n] = app(f, up)
            words[n] = words_raw[n] if status == 'ok' and served else frozenset()
        renew = {}
        for c in self.clients:
            f = freg('renew:' + c, 0)
            renew[c] = 'forever' if f is not None and not app(f, ALL) else 'ask'
        return dict(status=status, served=served, refusals=refusals, prem=prem, names=names,
                    valid=valid, adm=adm, words=words, words_raw=words_raw, funcs=funcs,
                    renew=renew, vers=vers, lost=lost,
                    ents=ents, heads=heads, heads_live=heads_live)

    def ideal(self, i, ever=False):
        """The same file, had the transport been a perfect grow-only set."""
        r = Rep()
        r.nodes = self.reps[i].nodes
        for eid in (self.Kever[i] if ever else self.K[i]):
            r.entries[eid] = [eid, 0, True]
        return self.fold(r, FULL)

    # --------------------------------------------------------- the write path
    def write_head(self, rep, new_ids, reseal=False):
        t = self.t
        v = self.fold(rep)
        ents = v['ents']
        # prune: what a present element replaces goes, with the deletion record KeePass leaves
        dead = set()
        for obj, V in v['vers'].items():
            for e in V.values():
                dead |= e.after
        for e in ents.values():
            if e.kind == 'padd':
                rids = set(r for (n, r) in v['prem'] if n == e.obj)
                if not rids <= e.after:
                    dead.add(e.id)
            if e.kind == 'rec' and (('revoked', e.obj) in v['refusals']
                                    or ('retired', e.data[0]) in v['refusals']):
                dead.add(e.id)
            if e.kind == 'auth' and ('retired', e.obj) in v['refusals']:
                dead.add(e.id)
        old_heads = [h for h in v['heads']]
        anc = set(u for u in rep.dels if u[0] == 'H')   # every head known to have been deleted here
        for h in old_heads:
            anc.add(h.id)
            anc |= h.after
        dead |= anc
        for u, (eid, mt, ok) in list(rep.entries.items()):
            if u == eid and not ok:      # damage goes with the write that seals over it
                del rep.entries[u]
                rep.dels[u] = t
        for d in dead:
            if d in rep.entries and rep.entries[d][0] == d:
                del rep.entries[d]
            rep.dels[d] = t      # also for what is not here: the record keeps it out and dead
        for (rk, subj) in v['refusals']:
            rep.dels['T|%s|%s' % (rk, subj)] = t
        hs = [h.height for h in old_heads] + [int(u[1:4]) for u in rep.dels if u[0] == 'H']
        regs = set()
        for obj, V in v['vers'].items():
            dd = set()
            for e in V.values():
                dd |= e.after
            regs |= set(i for i in V if i not in dd)
        h = self.mk('head', 'store', after=anc, height=1 + max(hs + [0]),
                    data={'ref': frozenset(v['refusals']), 'prem': frozenset(v['prem']),
                          'regs': frozenset(regs)})
        rep.entries[h.id] = [h.id, t, True]
        return h.id

    def act(self, i, op):
        """A decision made through the gate on file i. False when it does not apply."""
        rep = self.reps[i]
        v = self.fold(rep)
        kind = op[0]
        if kind == 'reseal':
            if v['status'] != 'incomplete' or not v['ents']:
                return False
            t = self.now()
            for obj, ids in sorted(v['lost'].items()):
                # a version was deleted here and nothing holds its text: the press writes the
                # refusal down, naming what was lost
                after = set(ids) | set(v['vers'].get(obj, {}))
                for e in v['vers'].get(obj, {}).values():
                    after |= e.after
                e = self.mk('ver', obj, after=after, data=REFUSE)
                rep.entries[e.id] = [e.id, t, True]
                self.K[i].add(e.id)
            for n, (m, mt) in sorted(rep.nodes.items()):
                if m == n and not v['vers'].get('rule:' + n):
                    # the node says it is ruled and no rule is left: the press writes that down,
                    # so that an older rule arriving later meets it and cannot take over
                    e = self.mk('ver', 'rule:' + n, data=REFUSE)
                    rep.entries[e.id] = [e.id, t, True]
                    self.K[i].add(e.id)
            self.K[i].add(self.write_head(rep, set()))
            self.Kever[i] |= self.K[i]
            return True
        if v['status'] != 'ok':
            return False
        new = []
        if kind == 'pair':
            if not v['served'] or len(self.clients) >= 3:
                return False
            c = 'c%d' % len(self.clients)
            self.clients.append(c)
            e = El('rec:' + c, 'rec', c, data=(v['served'], frozenset(['pf', 'pg'])))
            self.EL[e.id] = e
            new.append(e)
        elif kind == 'revoke':
            c = op[2]
            if c not in v['adm']:
                return False
            new.append(self.mk('ref', c, data=('revoked', c)))
        elif kind == 'recreate':
            if not v['served']:
                return False
            new.append(self.mk('ref', v['served'], data=('retired', v['served'])))
            self.n += 1
            a = 'a%d' % self.n
            e = El('auth:' + a, 'auth', a)
            self.EL[e.id] = e
            new.append(e)
        elif kind == 'padd':
            n = op[2]
            if n in v['names']:
                return False
            rids = [r for (m, r) in v['prem'] if m == n]
            gen = 1 + max([self.EL[r].height for r in rids] + [0])
            new.append(self.mk('padd', n, after=rids, height=gen))
        elif kind == 'prem':
            n = op[2]
            if n not in v['names']:
                return False
            gens = [e.height for e in v['ents'].values() if e.kind == 'padd' and e.obj == n]
            new.append(self.mk('prem', n, height=max(gens + [0])))
        elif kind in ('rule', 'renew', 'adopt'):
            if kind == 'rule':
                obj, doc, node = 'rule:' + op[2], DOCS[op[3]], op[2]
            elif kind == 'adopt':
                cs = sorted(c for c in self.copies if c in rep.nodes)
                if not cs:
                    return False
                node = cs[op[2] % len(cs)]
                obj, doc = 'rule:' + node, DOCS[op[3]]
            else:
                if op[2] not in self.clients:
                    return False
                obj, doc, node = 'renew:' + op[2], DOCS['none' if op[3] == 'forever' else 'rw'], None
            V = v['vers'].get(obj, {})
            after = set(V)
            for e in V.values():
                after |= e.after
            new.append(self.mk('ver', obj, after=after, data=doc))
            if node and node in rep.nodes and rep.nodes[node][0] != node:
                rep.nodes[node] = [node, self.now()]    # the marker, once in the node's life
        elif kind == 'save':
            for n, (m, mt) in sorted(rep.nodes.items()):
                if m and m != n and not any(e.kind == 'pin' and e.data[1] == m
                                            for e in v['vers'].get('rule:' + n, {}).values()):
                    new.append(self.mk('pin', 'rule:' + n, data=('pinref', m)))
            if not new:
                return False
        else:
            raise ValueError(op)
        t = self.now()
        for e in new:
            rep.entries[e.id] = [e.id, t, True]
            self.K[i].add(e.id)
        self.K[i].add(self.write_head(rep, set(e.id for e in new)))
        self.Kever[i] |= self.K[i]
        return True

    # ------------------------------------------------- other programs, people
    def pick(self, rep, sel):
        """A store entry by description, so that a history stays replayable when shortened."""
        EL = self.EL
        present = [EL[eid] for u, (eid, mt, ok) in rep.entries.items() if u == eid and EL[eid].key == 'K']
        k = sel[0]
        if k == 'head':
            hs = sorted((e for e in present if e.kind == 'head'), key=lambda e: (e.height, e.id))
            if not hs:
                return None
            return (hs[-1] if sel[1] == 'new' else hs[0]).id
        if k == 'ref':
            c = [e for e in present if e.kind == 'ref' and e.data[0] == sel[1]]
        elif k == 'ver':
            V = [e for e in present if e.kind in ('ver', 'pin') and e.obj == sel[1]]
            dead = set()
            for e in V:
                dead |= e.after
            c = [e for e in V if (e.id not in dead) == (sel[2] == 'new')]
        else:
            c = [e for e in present if e.kind == k]
        c.sort(key=lambda e: e.id)
        return c[-1].id if c else None

    def other(self, op):
        k = op[0]
        reps = self.reps
        if k == 'del':      # a person deletes one store entry: 1 with a deletion record, 0 without
            rep = reps[op[1]]             # (pykeepass), 2 with a record dated before the entry
            u = self.pick(rep, op[2])
            if not u:
                return False
            del rep.entries[u]
            if op[3]:
                rep.dels[u] = self.now() if op[3] == 1 else 0
            return True
        if k == 'clear':    # Database Maintenance: remove deleted objects information
            if not reps[op[1]].dels:
                return False
            reps[op[1]].dels = {}
            return True
        if k == 'over':     # a whole file written over another
            reps[op[1]] = reps[op[2]].clone()
            self.K[op[1]] = set(self.K[op[2]])
            self.Kever[op[1]] |= self.Kever[op[2]]
            return True
        if k == 'edit':     # a person or a program changes a store entry's content
            rep = reps[op[1]]
            u = self.pick(rep, op[2])
            if not u or not rep.entries[u][2]:
                return False
            rep.entries[u] = [u, self.now(), False]
            return True
        if k == 'copy':     # a copy of node N in any program: new identifier, same strings
            rep = reps[op[1]]
            if 'N' not in rep.nodes or self.ncopy >= 2:
                return False
            self.ncopy += 1
            m = 'M%d' % self.ncopy
            if rep.nodes['N'][0]:
                self.copies[m] = 'N'    # the promise is about a copy of a ruled node
            rep.nodes[m] = [rep.nodes['N'][0], self.now()]
            return True
        if k == 'strip':    # the carried reference removed by hand, or by Delete Duplicate Entries
            rep = reps[op[1]]
            cs = sorted(c for c in self.copies if c in rep.nodes and rep.nodes[c][0])
            if not cs:
                return False
            rep.nodes[cs[0]] = [None, self.now()]
            return True
        if k == 'import':   # KeePass Create new IDs: every arriving entry under a new identifier
            dst, src = reps[op[1]], reps[op[2]]
            for u, ent in list(src.entries.items()):
                self.n += 1
                dst.entries['X%d' % self.n] = list(ent)
            return True
        if k == 'plant':    # a foreign file merged in, built by somebody who read no entry of ours
            rep = reps[op[1]]
            t = self.now()
            what = op[2]
            if what == 'rule':
                e = self.mk('ver', 'rule:N', data=DOCS['rw'], key='F')
            elif what == 'retire':
                e = self.mk('ref', 'a0', data=('retired', 'a0'), key='F')
            elif what == 'record':
                e = El('rec:c9', 'rec', 'c9', data=('a0', frozenset(['pf'])), key='F')
                self.EL[e.id] = e
                if 'c9' not in self.clients:
                    self.clients.append('c9')
            elif what == 'profile':
                e = self.mk('padd', 'pg', key='F')
            else:
                self.n += 1
                e = El('auth:f%d' % self.n, 'auth', 'f%d' % self.n, key='F')
                self.EL[e.id] = e
            rep.entries[e.id] = [e.id, t, True]
            return True
        if k == 'merge':
            return self.merge(op[1], op[2], op[3])
        raise ValueError(op)

    def merge(self, prog, d, s):
        if d == s:
            return False
        dst, src = self.reps[d], self.reps[s]
        self.now()
        if prog in ('kp_sync', 'k2a'):
            # read 2.62 PwDatabase.cs, measured X1-X4; Keepass2Android measured 2026-11-15
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            for u, t in src.dels.items():
                dst.dels[u] = max(t, dst.dels.get(u, t))
            self.apply_dels(dst, dst.dels, drop=True)
        elif prog == 'kp_other':
            # Overwrite if newer, Overwrite existing, Keep existing: no arriving record, own applied
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            self.apply_dels(dst, dst.dels, drop=True)
        elif prog == 'xc':
            # measured: never applies or copies a deletion; the target keeps its own records
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
        elif prog == 'dx':
            # read 4.5.8 DatabaseKDBXMerger.kt: arriving records applied and not kept
            for u, ent in src.entries.items():
                if u not in dst.entries:
                    if not (u in dst.dels and dst.dels[u] > ent[1]):
                        dst.entries[u] = list(ent)
                elif ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            self.apply_dels(dst, src.dels, drop=False)
        else:
            raise ValueError(prog)
        for n, nd in src.nodes.items():
            if n not in dst.nodes or nd[1] > dst.nodes[n][1]:
                dst.nodes[n] = list(nd)
        self.K[d] |= self.K[s]
        self.Kever[d] |= self.Kever[s]
        return True

    @staticmethod
    def apply_dels(rep, dels, drop):
        for u, t in list(dels.items()):
            if u in rep.entries:
                if rep.entries[u][1] <= t:
                    del rep.entries[u]
                elif drop:
                    del rep.dels[u]     # the object is present and newer: the record goes

    def step(self, op):
        if op[0] in ('pair', 'revoke', 'recreate', 'padd', 'prem', 'rule', 'renew', 'adopt',
                     'save', 'reseal'):
            return self.act(op[1], op)
        return self.other(op)


# ------------------------------------------------------------------ properties
def narrower(A, I, nodes):
    """None when A grants nothing I does not; else what is wider."""
    for c, profs in A['adm'].items():
        if c not in I['valid']:
            return 'client %s admitted' % c
        if not profs <= I['valid'][c]:
            return 'client %s holds %s' % (c, sorted(profs - I['valid'][c]))
    for n in nodes:
        if n in A['words'] and not A['words'][n] <= I['words_raw'].get(n, frozenset()):
            return 'node %s grants %s' % (n, ''.join(sorted(A['words'][n])))
    for c, a in A['renew'].items():
        if a == 'ask' and I['renew'].get(c) == 'forever' and A['status'] == 'ok' and A['served']:
            return 'renewal of %s asked' % c
    return None


def summary(v):
    return (v['status'], v['served'], tuple(sorted((c, tuple(sorted(p))) for c, p in v['adm'].items())),
            tuple(sorted((n, ''.join(sorted(w))) for n, w in v['words'].items())),
            tuple(sorted(v['renew'].items())), tuple(sorted(v['names'])))


def run(ops, nrep, flags=FULL, check_conv=True, world=None):
    """Replays a history; returns the list of (property, step, detail) that failed."""
    W = world.clone() if world else World(nrep, flags)
    fails = []
    sealed = False      # Seal again was pressed: what was lost in every carrier before it is accepted
    for k, op in enumerate(ops):
        before = None
        if op[0] in ('del', 'edit'):
            before = W.fold(W.reps[op[1]])
        if not W.step(op):
            continue
        sealed = sealed or op[0] == 'reseal'
        for i in range(nrep):
            A = W.fold(W.reps[i])
            w = narrower(A, W.ideal(i), W.reps[i].nodes)
            if w:
                fails.append(('sealed-after-a-loss' if sealed else 'safety', k, 'file %d: %s' % (i, w)))
            elif narrower(A, W.ideal(i, True), W.reps[i].nodes):
                fails.append(('rollback-by-overwrite', k, 'file %d' % i))
            for m, o in W.copies.items():       # a copy never gets more than its original's rule allows
                if m in A['words'] and o in A['funcs'] and A['status'] == 'ok':
                    own = W.reps[i].nodes[m][0] == m
                    up = app(A['funcs']['root'], frozenset())
                    if not own and A['served'] and not A['words'][m] <= app(limit(A['funcs'][o]), up):
                        fails.append(('copy-cap', k, 'file %d: %s' % (i, m)))
        if before is not None:
            A = W.fold(W.reps[op[1]])
            w = narrower(A, before, W.reps[op[1]].nodes)
            if w:
                fails.append(('one-loss', k, 'file %d: %s' % (op[1], w)))
        if fails:
            break
    if not fails and check_conv:
        for _ in range(2):
            for d, s in itertools.permutations(range(nrep), 2):
                W.merge('kp_sync', d, s)
        views = [summary(W.fold(r)) for r in W.reps]
        if any(x != views[0] for x in views):
            fails.append(('convergence', len(ops), repr(views)))
        elif 'clean' in getattr(flags, 'mode', ''):
            if views[0] != summary(W.ideal(0)):
                fails.append(('convergence-to-ideal', len(ops), '%r vs %r' % (views[0], summary(W.ideal(0)))))
    return fails


def shrink(ops, nrep, flags, prop):
    """Greedy: drop every step whose removal keeps the same property failing."""
    changed = True
    while changed:
        changed = False
        for k in range(len(ops)):
            cand = ops[:k] + ops[k + 1:]
            if any(f[0] == prop for f in run(cand, nrep, flags)):
                ops, changed = cand, True
                break
    return ops


# ------------------------------------------------------------------ alphabets
def alphabet(nrep, cfg):
    R = range(nrep)
    acts, faults = [], []
    for r in R:
        acts += [('pair', r), ('revoke', r, 'c0'), ('revoke', r, 'c1'), ('recreate', r),
                 ('padd', r, 'pf'), ('padd', r, 'pg'), ('prem', r, 'pf'), ('prem', r, 'pg'),
                 ('renew', r, 'c0', 'forever'), ('renew', r, 'c0', 'allowed'), ('save', r),
                 ('adopt', r, 0, 'r'), ('copy', r)]
        for n in ('root', 'N'):
            for d in DOCS:
                acts.append(('rule', r, n, d))
    progs = ['kp_sync', 'k2a'] if 'synconly' in cfg else ['kp_sync', 'kp_other', 'xc', 'k2a', 'dx']
    merges = [('merge', p, d, s) for p in progs for d in R for s in R if d != s]
    sels = [('head', 'new'), ('head', 'old'), ('ref', 'revoked'), ('ref', 'retired'),
            ('ver', 'rule:N', 'new'), ('ver', 'rule:N', 'old'), ('ver', 'rule:root', 'new'),
            ('ver', 'renew:c0', 'new'), ('rec',), ('auth',), ('padd',), ('prem',)]
    if 'rec' in cfg:
        faults += [('del', r, s, 1) for r in R for s in sels]
        faults += [('reseal', r) for r in R]
    if 'skew' in cfg:
        faults += [('del', r, s, 2) for r in R for s in sels]
    if 'trace' in cfg:
        faults += [('del', r, s, 0) for r in R for s in sels]
    if 'clear' in cfg:
        faults += [('clear', r) for r in R]
    if 'over' in cfg:
        faults += [('over', d, s) for d in R for s in R if d != s]
    if 'foreign' in cfg:
        faults += [('edit', r, s) for r in R for s in sels]
        faults += [('import', d, s) for d in R for s in R if d != s]
        faults += [('plant', r, w) for r in R for w in ('rule', 'retire', 'record', 'profile', 'auth')]
    if 'strip' in cfg:
        faults += [('strip', r) for r in R]
    return acts, merges, faults


CONFIGS = [
    ('A merges only (every program)', ''),
    ('B + deletions by hand with a deletion record', 'rec'),
    ('C + deletions that leave no record', 'rec trace'),
    ('D + records dated before the entry (a clock behind)', 'rec skew'),
    ('E + deletion records cleared', 'rec trace clear'),
    ('F + foreign: edits, import with new identifiers, planted entries', 'rec foreign'),
    ('G + a copy stripped of its reference', 'rec strip'),
    ('H + a whole file written over another', 'rec over'),
    ('I everything at once', 'rec skew trace clear over foreign strip'),
    ('J only KeePass Synchronize and Keepass2Android; deletions with a record', 'synconly rec'),
    ('J2 the same; every kind of deletion', 'synconly rec skew trace'),
    ('K the same, and foreign edits, imports and planted entries', 'synconly rec skew trace foreign'),
]


def search(name, cfg, nrep, count, length, seed, flags=FULL, quiet=False):
    rng = random.Random(seed)
    acts, merges, faults = alphabet(nrep, cfg)
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    found = {}
    counts = {}
    for _ in range(count):
        ops = []
        for _ in range(rng.randint(3, length)):
            x = rng.random()
            pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            ops.append(rng.choice(pool))
        for f in run(ops, nrep, flags):
            counts[f[0]] = counts.get(f[0], 0) + 1
            if f[0] not in found or len(ops) < len(found[f[0]][0]):
                found[f[0]] = (ops, f)
    out = []
    for prop, (ops, f) in sorted(found.items()):
        small = shrink(ops, nrep, flags, prop)
        detail = [x for x in run(small, nrep, flags) if x[0] == prop][0]
        out.append((prop, counts[prop], small, detail))
    if not quiet:
        print('%-66s %d files %6d histories  %s' % (name, nrep, count,
              'no failure' if not out else ', '.join('%s x%d' % (p, c) for p, c, _, _ in out)))
        for prop, c, small, detail in out:
            print('    shortest %s (%s):' % (prop, detail[2]))
            for op in small:
                print('        ' + ' '.join(str(x) for x in op))
    return out


def exhaustive(nrep, cfg, depth, flags=FULL):
    """Every sequence of transport and fault steps up to `depth`, after a fixed divergence:
    file 0 revoked c0, narrowed N and the root, removed profile pf, refused renewals; file 1 is stale."""
    prefix = [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
              ('revoke', 0, 'c0'), ('prem', 0, 'pf'), ('renew', 0, 'c0', 'forever'),
              ('rule', 0, 'root', 'none')]
    acts, merges, faults = alphabet(nrep, cfg)
    steps = merges + faults
    flags.mode = ''
    total, bad = 0, {}
    W0 = World(nrep, flags)
    for op in prefix:
        assert W0.step(op), op
    for d in range(1, depth + 1):
        for seq in itertools.product(steps, repeat=d):
            total += 1
            for f in run(list(seq), nrep, flags, check_conv=False, world=W0):
                key = f[0]
                if key not in bad or len(seq) < len(bad[key][1]):
                    bad[key] = (bad.get(key, (0,))[0], seq, f)
                bad[key] = (bad[key][0] + 1, bad[key][1], bad[key][2])
    return total, len(steps), bad


def laws(count, seed):
    """The value is a function of the set of elements; adding an element that has not seen
    the present heads of its object never widens (ideal transport, no faults)."""
    rng = random.Random(seed)
    acts, merges, _ = alphabet(2, '')
    bad = 0
    for _ in range(count):
        W = World(2)
        for _ in range(rng.randint(2, 7)):
            W.step(rng.choice(acts if rng.random() < 0.6 else [m for m in merges if m[1] == 'kp_sync']))
        a, b = W.ideal(0), W.ideal(1)
        r = Rep()
        r.nodes = dict(W.reps[0].nodes)
        for n, nd in W.reps[1].nodes.items():
            r.nodes.setdefault(n, nd)
        for eid in W.K[0] | W.K[1]:
            r.entries[eid] = [eid, 0, True]
        j = W.fold(r, FULL)
        # the join of two files is at most each side for whatever the other side never saw:
        # anything wider than one side must come from elements that side lacks and whose
        # causal context covers that side's heads; checked through registers
        for obj, V in j['vers'].items():
            dead = set()
            for e in V.values():
                dead |= e.after
            live = [e for i, e in V.items() if i not in dead and e.kind == 'ver']
            if obj.startswith('rule:') and live:
                f = j['funcs'].get(obj[5:])
                for e in live:
                    for x in (frozenset(), frozenset('r'), ALL):
                        if f is not None and not app(f, x) <= app(e.data, x):
                            bad += 1
        for (rk, subj) in a['refusals'] | b['refusals']:
            if (rk, subj) not in j['refusals']:
                bad += 1
            if rk == 'revoked' and subj in j['adm']:
                bad += 1
    return bad


SCENARIOS = [
    # (what happens, the finding it replays, files, history, file asked, what must hold of its view)
    ('a revocation, a KeePassXC merge into the stale copy, the revocation entry deleted there',
     'store-entries survival P1 A', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('the same, and the head deleted there too, both with deletion records',
     'store-entries survival P1 B', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 1),
      ('del', 1, ('head', 'new'), 1)],
     1, lambda v: 'c0' not in v['adm']),
    ('the same two deletions leaving no record (limit: that copy is a stale copy again)',
     'store-entries survival P1, what is left', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 0),
      ('del', 1, ('head', 'new'), 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('a name removed in one copy; removed and added again in the other; merged',
     'store-entries survival P2', 2,
     [('prem', 1, 'pf'), ('padd', 1, 'pf'), ('prem', 0, 'pf'), ('merge', 'kp_sync', 0, 1),
      ('merge', 'kp_sync', 1, 0)],
     0, lambda v: 'pf' not in v['names']),
    ('a name added in each copy on its own survives the merge', 'store-entries survival P2, other side', 2,
     [('padd', 0, 'pg'), ('padd', 1, 'pg'), ('prem', 1, 'pf'), ('merge', 'xc', 0, 1)],
     0, lambda v: 'pg' in v['names'] and 'pf' not in v['names']),
    ('a rule set, taken away, and the same rule set again in a copy that saw neither',
     'rules-on-node survival 2.4; rules-marker-entry survival P1', 2,
     [('rule', 0, 'N', 'none'), ('rule', 0, 'N', 'inherit'), ('rule', 1, 'N', 'none'),
      ('merge', 'kp_sync', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['words']['N']),
    ('the newest rule version and the head deleted in the copy that wrote them, then a stale copy by KeePassXC',
     'rules-linked survival P2 C3', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'xc', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, the deletions leaving no record', 'rules-linked survival P2 C3, no trace', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 0), ('del', 0, ('head', 'new'), 0), ('merge', 'xc', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, then deletion records cleared, then Synchronize with the stale copy (limit)',
     'rules-linked survival P2 C3c', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0),
      ('merge', 'kp_sync', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, but the store sealed again by a press before the stale copy arrives',
     'rules-linked survival P2 C3c, with the press', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0),
      ('reseal', 0), ('merge', 'kp_sync', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['words'].get('N')),
    ('one rule version deleted by hand, the deletion synchronized', 'rules-linked survival P3 C5', 2,
     [('rule', 0, 'N', 'none'), ('del', 0, ('ver', 'rule:N', 'new'), 1), ('merge', 'kp_sync', 1, 0)],
     1, lambda v: v['status'] == 'ok' and not v['words']['N'] and bool(v['words']['root'])),
    ('two concurrent rule versions, then both entries deleted by hand',
     'rules-marker-entry survival P2', 2,
     [('rule', 0, 'N', 'none'), ('rule', 1, 'N', 'rw'), ('merge', 'kp_sync', 0, 1),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('ver', 'rule:N', 'new'), 1)],
     0, lambda v: not v['words']['N']),
    ('a foreign file plants a rule, a retirement, a record and a profile',
     'rules-central security S1; store-entries security S1; rules-linked security S5', 2,
     [('plant', 0, 'rule'), ('plant', 0, 'retire'), ('plant', 0, 'record'), ('plant', 0, 'profile')],
     0, lambda v: v['served'] == 'a0' and v['words']['N'] == frozenset('r') and 'c9' not in v['adm']
     and 'pg' not in v['names']),
    ('a foreign authority arrives intact: nothing is served until the operator chooses',
     'CLAUDE.md, 503 undecided', 2,
     [('plant', 0, 'auth')], 0, lambda v: v['served'] is None and not v['adm']),
    ('a copy of a sealed node; a save; the copy then loses its carried reference',
     'rules-linked survival P1 C1b, C2', 2,
     [('rule', 0, 'N', 'none'), ('copy', 0), ('save', 0), ('strip', 0)],
     0, lambda v: not v['words']['M1']),
    ('the same with no save between the copy and the loss (limit)',
     'rules-linked survival P1, what is left', 2,
     [('rule', 0, 'N', 'none'), ('copy', 0), ('strip', 0)],
     0, lambda v: not v['words']['M1']),
    ('everything of the plugin deleted without a trace in the copy that wrote it, then a stale copy by KeePassXC',
     'rules-linked survival P2 C3b', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('revoke', 0, 'c0')]
     + [('del', 0, x, 0) for x in (('head', 'new'), ('ver', 'rule:N', 'new'), ('ver', 'rule:root', 'new'),
                                   ('ref', 'revoked'), ('auth',), ('padd',))]
     + [('merge', 'xc', 0, 1)],
     0, lambda v: not v['adm'] and not v['words'].get('N')),
    ('both carriers of a revocation deleted, the store sealed again by a press, merged into a stale copy by KeePassXC',
     'what the deletion-record witness is for', 2,
     [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0), ('del', 0, ('head', 'new'), 0),
      ('reseal', 0), ('merge', 'xc', 1, 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('a store entry edited by hand, and the edit synchronized into every copy',
     'store-entries survival P7', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('edit', 0, ('ref', 'revoked')), ('merge', 'kp_sync', 1, 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('an import with new identifiers of a stale copy', 'rules-linked survival P3 C6', 2,
     [('rule', 0, 'N', 'none'), ('revoke', 0, 'c0'), ('import', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['adm'] and not v['words']['N']),
]


def scenarios():
    abl = [('as written', FULL), ('no snapshot', Flags(snapshot=False)), ('no cover', Flags(cover=False)),
           ('no witness', Flags(T=False)), ('no record rule', Flags(recrule=False)),
           ('no head tag', Flags(headtag=False)), ('no decision tag', Flags(dtag=False)),
           ('generations', Flags(causal_add=False))]
    print('   columns: ' + ' | '.join(n for n, _ in abl))
    for k, (name, src, nrep, ops, i, ok) in enumerate(SCENARIOS):
        name = '%2d. %s' % (k + 1, name)
        row = []
        for _, fl in abl:
            W = World(nrep, fl)
            for op in ops:
                W.step(op)
            v = W.fold(W.reps[i])
            row.append(('holds' if ok(v) else 'FAILS') + ('' if v['status'] == 'ok' else ' (%s)' % v['status']))
        print('%s\n      [%s]\n   %s' % (name, src, ' | '.join(row)))


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'quick'
    sys.stdout.reconfigure(line_buffering=True)
    n = 4000 if mode == 'quick' else 20000
    t0 = time.time()
    print('== lattice laws on the ideal transport: %d violations in %d random pairs of files'
          % (laws(n // 2, 7), n // 2))
    print('== the sequences of the critiques, replayed; and each part of the read rule switched off')
    scenarios()
    if mode == 'scenarios':
        return
    print('== random histories, the design as written')
    for nrep in (2, 3):
        for k, (name, cfg) in enumerate(CONFIGS):
            search(name, cfg, nrep, n, 9, 100 + k + 10 * nrep)
    print('== exhaustive, after a fixed divergence (file 0 newer in every way, file 1 stale)')
    for name, cfg, depth in (('merges', '', 4), ('merges and deletions with a record', 'rec', 3),
                             ('merges and deletions without a record', 'trace', 3),
                             ('Synchronize and Keepass2Android only, deletions with a record', 'synconly rec', 3)):
        if mode == 'quick':
            depth -= 1
        total, width, bad = exhaustive(2, cfg, depth)
        print('%-45s %d steps wide, depth %d: %7d histories  %s' % (
            name, width, depth, total,
            'no failure' if not bad else ', '.join('%s x%d' % (p, c) for p, (c, _, _) in sorted(bad.items()))))
        for p, (c, seq, f) in sorted(bad.items()):
            print('    shortest %s (%s): %s' % (p, f[2], ' ; '.join(' '.join(str(x) for x in op) for op in seq)))
    print('== ablations: one part of the read rule switched off, random histories, 2 files')
    abl = [('no head snapshot (entries only)', Flags(snapshot=False), 'rec'),
           ('no deletion-record witnesses for refusals', Flags(T=False), 'rec trace'),
           ('a deletion record does not take an element out of force', Flags(recrule=False), 'rec trace'),
           ('a deleted head is not recognized', Flags(headtag=False), 'rec'),
           ('a decision without its head is accepted', Flags(cover=False), 'rec trace'),
           ('the deletion record of a decision tells nothing', Flags(dtag=False), 'rec'),
           ('profile additions by generation, not by what they saw', Flags(causal_add=False), '')]
    for name, fl, cfg in abl:
        base = search(name, cfg, 2, n, 9, 555, FULL, quiet=True)
        search(name + ' [' + (cfg or 'merges only') + ']', cfg, 2, n, 9, 555, fl)
        print('    the design as written, same histories: %s' % (
            'no failure' if not base else ', '.join('%s x%d' % (p, c) for p, c, _, _ in base)))
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
