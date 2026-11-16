"""The design's own model (crdt_model.py, copied unchanged) with what it leaves out of the transport:

- where an entry sits: the store group or the recycle bin, with its LocationChanged;
- the recycle bin: Delete in KeePass, KeePassXC, Keepass2Android and KeePassDroid moves the entry into
  the bin, moves LocationChanged and leaves the modification time alone (measured, facts F3; android
  2.1, 3.2); Empty Recycle Bin deletes each with a record;
- a merge relocates: KeePass Synchronize and Keepass2Android by the later LocationChanged (facts F3;
  read PwDatabase.cs:1189-1231), KeePassXC by the later LocationChanged keeping the target's time
  (facts F3), KeePassDX to the arriving copy's place at equal modification times (android 4.3);
- KeePassDroid's editor Save with nothing changed: a new modification time, content unchanged
  (android 3.3); measured to drop the gate's records in every KeePass method (facts F2);
- KeePassLib applies a record only to an object strictly older (PwDatabase.cs:1083-1084; facts F1),
  where the design's model deletes at equal times.

The read rule is the design's, unchanged, applied to what sits directly in the store group, plus
refusals and removals in the bin (option-crdt.md 3.1, 5.2 point 3). With binall=True, everything in
the bin counts, which is the change the critique proposes.
"""
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402


class LRep(M.Rep):
    def __init__(self):
        super().__init__()
        self.loc = {}       # uuid -> [place 'S' or 'B', LocationChanged]

    def clone(self):
        r = LRep()
        r.entries = {u: list(v) for u, v in self.entries.items()}
        r.dels = dict(self.dels)
        r.nodes = {n: list(v) for n, v in self.nodes.items()}
        r.loc = {u: list(v) for u, v in self.loc.items()}
        return r


def getloc(rep, u):
    if u not in rep.loc:
        rep.loc[u] = ['S', rep.entries[u][1]]
    return rep.loc[u]


class LWorld(M.World):
    def __init__(self, nrep, flags=M.FULL, binall=False, strict=True, binheads=False):
        super().__init__(nrep, flags)
        self.binall, self.strict, self.binheads = binall, strict, binheads
        reps = []
        for r in self.reps:
            x = LRep()
            x.entries, x.dels, x.nodes = r.entries, r.dels, r.nodes
            reps.append(x)
        self.reps = reps

    def clone(self):
        w = super().clone()
        w.__class__ = LWorld
        w.binall, w.strict, w.binheads = self.binall, self.strict, self.binheads
        return w

    # ------------------------------------------------------------ the read rule, by place
    def fold(self, rep, flags=None):
        if not isinstance(rep, LRep) or self.binall:
            return super().fold(rep, flags)
        v = M.Rep()
        v.nodes, v.dels = rep.nodes, rep.dels
        for u, ent in rep.entries.items():
            place = getloc(rep, u)[0]
            e = self.EL.get(ent[0])
            kind = e.kind if e is not None else None
            if place == 'S' or (place == 'B' and (kind in ('ref', 'prem') or (self.binheads and kind == 'head'))):
                v.entries[u] = ent
        return super().fold(v, flags)

    def apply_dels_real(self, rep, dels, drop):
        for u, t in list(dels.items()):
            if u in rep.entries:
                older = rep.entries[u][1] < t if self.strict else rep.entries[u][1] <= t
                if older:
                    del rep.entries[u]
                    rep.loc.pop(u, None)
                elif drop:
                    del rep.dels[u]

    def merge(self, prog, d, s):
        if d == s:
            return False
        dst, src = self.reps[d], self.reps[s]
        self.now()
        for u in list(src.entries):
            getloc(src, u)
        for u in list(dst.entries):
            getloc(dst, u)
        if prog in ('kp_sync', 'k2a', 'kp_other', 'xc'):
            for u, ent in src.entries.items():
                sl = src.loc[u]
                if prog == 'kp_other' and u not in dst.entries and u in dst.dels and not (
                        dst.dels[u] <= ent[1] if self.strict else dst.dels[u] < ent[1]):
                    pass  # added, then removed again by the copy's own record below
                if u not in dst.entries:
                    dst.entries[u] = list(ent)
                    dst.loc[u] = list(sl)
                    continue
                dl = dst.loc[u]
                if ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
                if prog in ('kp_sync', 'k2a') and sl[1] > dl[1]:
                    dst.loc[u] = list(sl)
                elif prog == 'xc' and sl[1] > dl[1]:
                    dst.loc[u] = [sl[0], dl[1]]
            if prog in ('kp_sync', 'k2a'):
                for u, t in src.dels.items():
                    dst.dels[u] = max(t, dst.dels.get(u, t))
            if prog != 'xc':
                self.apply_dels_real(dst, dst.dels, drop=True)
        elif prog == 'dx':
            for u, ent in src.entries.items():
                sl = src.loc[u]
                if u not in dst.entries:
                    if not (u in dst.dels and dst.dels[u] > ent[1]):
                        dst.entries[u] = list(ent)
                        dst.loc[u] = list(sl)
                elif ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
                    dst.loc[u] = list(sl)
                elif ent[1] == dst.entries[u][1]:
                    dst.loc[u] = [sl[0], dst.loc[u][1]]
            self.apply_dels_real(dst, src.dels, drop=False)
        else:
            raise ValueError(prog)
        for n, nd in src.nodes.items():
            if n not in dst.nodes or nd[1] > dst.nodes[n][1]:
                dst.nodes[n] = list(nd)
        self.K[d] |= self.K[s]
        self.Kever[d] |= self.Kever[s]
        return True

    def pick_at(self, rep, sel, place):
        v = M.Rep()
        v.entries = {u: e for u, e in rep.entries.items() if getloc(rep, u)[0] == place}
        return self.pick(v, sel)

    def other(self, op):
        k = op[0]
        if k in ('bin', 'bindx'):     # Delete in KeePass, KeePassXC, K2A, KeePassDroid; 'bindx' also touches
            rep = self.reps[op[1]]
            u = self.pick_at(rep, op[2], 'S')
            if not u:
                return False
            t = self.now()
            rep.loc[u] = ['B', t]
            if k == 'bindx':
                rep.entries[u][1] = t
            return True
        if k == 'unbin':
            rep = self.reps[op[1]]
            u = self.pick_at(rep, op[2], 'B')
            if not u:
                return False
            rep.loc[u] = ['S', self.now()]
            return True
        if k == 'touch':              # KeePassDroid's editor Save with nothing changed
            rep = self.reps[op[1]]
            u = self.pick(rep, op[2])
            if not u:
                return False
            getloc(rep, u)
            rep.entries[u][1] = self.now()
            return True
        if k == 'empty':              # Empty Recycle Bin (KeePass); K2A and KeePassDroid: one by one
            rep = self.reps[op[1]]
            inbin = [u for u in rep.entries if getloc(rep, u)[0] == 'B']
            if not inbin:
                return False
            t = self.now()
            for u in inbin:
                del rep.entries[u]
                rep.loc.pop(u, None)
                rep.dels[u] = t
            return True
        if k == 'del':
            rep = self.reps[op[1]]
            r = super().other(op)
            for u in list(rep.loc):
                if u not in rep.entries:
                    del rep.loc[u]
            return r
        if k == 'over':
            r = super().other(op)
            src = self.reps[op[2]]
            x = LRep()
            x.entries, x.dels, x.nodes = src.clone().entries, dict(src.dels), {n: list(v) for n, v in src.nodes.items()}
            x.loc = {u: list(v) for u, v in src.loc.items()}
            self.reps[op[1]] = x
            return r
        return super().other(op)


def run(ops, nrep, flags=M.FULL, check_conv=True, world=None, **kw):
    W = world.clone() if world else LWorld(nrep, flags, **kw)
    fails = []
    sealed = False
    for k, op in enumerate(ops):
        before = None
        if op[0] in ('del', 'edit', 'bin', 'bindx', 'touch', 'empty'):
            before = W.fold(W.reps[op[1]])
        if not W.step(op):
            continue
        sealed = sealed or op[0] == 'reseal'
        for i in range(nrep):
            A = W.fold(W.reps[i])
            w = M.narrower(A, W.ideal(i), W.reps[i].nodes)
            if w:
                fails.append(('sealed-after-a-loss' if sealed else 'safety', k, 'file %d: %s' % (i, w)))
            elif M.narrower(A, W.ideal(i, True), W.reps[i].nodes):
                fails.append(('rollback-by-overwrite', k, 'file %d' % i))
        if before is not None:
            A = W.fold(W.reps[op[1]])
            w = M.narrower(A, before, W.reps[op[1]].nodes)
            if w:
                fails.append(('one-loss', k, 'file %d: %s' % (op[1], w)))
        if fails:
            break
    return fails, W


def shrink(ops, nrep, prop, **kw):
    changed = True
    while changed:
        changed = False
        for k in range(len(ops)):
            cand = ops[:k] + ops[k + 1:]
            if any(f[0] == prop for f in run(cand, nrep, **kw)[0]):
                ops, changed = cand, True
                break
    return ops


SELS = [('head', 'new'), ('head', 'old'), ('ref', 'revoked'), ('ref', 'retired'),
        ('ver', 'rule:N', 'new'), ('ver', 'rule:N', 'old'), ('ver', 'rule:root', 'new'),
        ('ver', 'renew:c0', 'new'), ('rec',), ('auth',), ('padd',), ('prem',)]


def alphabet(nrep, progs, faults_kinds):
    R = range(nrep)
    acts, _, _ = M.alphabet(nrep, '')
    acts = [a for a in acts if a[0] not in ('copy', 'adopt')]   # the tree is not this lens
    merges = [('merge', p, d, s) for p in progs for d in R for s in R if d != s]
    faults = []
    for r in R:
        if 'bin' in faults_kinds:
            faults += [('bin', r, s) for s in SELS]
        if 'bindx' in faults_kinds:
            faults += [('bindx', r, s) for s in SELS]
        if 'touch' in faults_kinds:
            faults += [('touch', r, s) for s in SELS]
        if 'empty' in faults_kinds:
            faults += [('empty', r)]
        if 'rec' in faults_kinds:
            faults += [('del', r, s, 1) for s in SELS]
        if 'reseal' in faults_kinds:
            faults += [('reseal', r)]
        if 'clear' in faults_kinds:
            faults += [('clear', r)]
        if 'unbin' in faults_kinds:
            faults += [('unbin', r, s) for s in SELS]
    return acts, merges, faults


def search(name, nrep, progs, fk, count, length, seed, quiet=False, **kw):
    rng = random.Random(seed)
    acts, merges, faults = alphabet(nrep, progs, fk)
    found, counts = {}, {}
    for _ in range(count):
        ops = []
        for _ in range(rng.randint(3, length)):
            x = rng.random()
            pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            ops.append(rng.choice(pool))
        for f in run(ops, nrep, check_conv=False, **kw)[0]:
            counts[f[0]] = counts.get(f[0], 0) + 1
            if f[0] not in found or len(ops) < len(found[f[0]][0]):
                found[f[0]] = (ops, f)
    out = []
    for prop, (ops, f) in sorted(found.items()):
        small = shrink(ops, nrep, prop, **kw)
        detail = [x for x in run(small, nrep, **kw)[0] if x[0] == prop][0]
        out.append((prop, counts[prop], small, detail))
    line = '%-70s %d files %6d histories  %s' % (name, nrep, count,
                                                 'no failure' if not out else ', '.join('%s x%d' % (p, c) for p, c, _, _ in out))
    print(line)
    res = [line]
    for prop, c, small, detail in out:
        res.append('    shortest %s (%s):' % (prop, detail[2]))
        for op in small:
            res.append('        ' + ' '.join(str(x) for x in op))
    for x in res[1:]:
        print(x)
    return res


CONFIGS = [
    # name, merges, faults, options
    ('P  phone set: Synchronize, K2A; bin, delete with a record (also from the bin), Seal again',
     ['kp_sync', 'k2a'], ['bin', 'rec', 'reseal'], {}),
    ('P1 the same and KeePassDroid editor Save (touch)',
     ['kp_sync', 'k2a'], ['bin', 'rec', 'reseal', 'touch'], {}),
    ('P2 the same and Empty Recycle Bin, restore from the bin',
     ['kp_sync', 'k2a'], ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'], {}),
    ('P3 = P2, with KeePassLib\'s strict record rule turned back to the model\'s',
     ['kp_sync', 'k2a'], ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'], {'strict': False}),
    ('X  KeePassXC merges added to P; bin and delete with a record',
     ['kp_sync', 'k2a', 'xc'], ['bin', 'rec', 'reseal'], {}),
    ('X1 KeePassXC and the import methods added to P2',
     ['kp_sync', 'k2a', 'xc', 'kp_other'], ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'], {}),
    ('F  fix: the bin read for every kind; config X1',
     ['kp_sync', 'k2a', 'xc', 'kp_other'], ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'], {'binall': True}),
    ('F2 fix: the bin read for every kind; config P2',
     ['kp_sync', 'k2a'], ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'], {'binall': True}),
]


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 4000
    t0 = time.time()
    lines = []
    for k, (name, progs, fk, kw) in enumerate(CONFIGS):
        for nrep in (2, 3):
            lines += search(name, nrep, progs, fk, n, 9, 700 + 10 * k + nrep, **kw)
    lines.append('== %.0f s' % (time.time() - t0))
    print(lines[-1])
    with open(sys.argv[2] if len(sys.argv) > 2 else 'ext-results.txt', 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
