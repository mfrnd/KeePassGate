"""The design's gate over real files. Every gate act is the design's own write path (crdt_model.py,
unchanged, through model_ext.LWorld), applied to a real .kdbx as the entries and deletion records it
adds and removes. Every other step (merges, deletions, the bin, KeePassDroid's unchanged Save,
Empty Recycle Bin, Remove deleted objects information, an import with new identifiers, a hand
edit, a whole file written over another) is done by KeePassLib 2.62 or keepassxc-cli. After each
such step the model's prediction for the file is compared with the file, and the model then
continues from what the file holds, so the design's checks run against the real transport.

Usage: bridge.py random <count> <seed> | bridge.py replay
"""
import json
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'model'))
import kp  # noqa: E402
import crdt_model as M  # noqa: E402
import model_ext as X  # noqa: E402

WORK = os.path.join(kp.SURVIVAL, 'work', 'bridge')
OUT = os.path.join(kp.SURVIVAL, 'out')
KP_OTHER = ['OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting']


def data_of(eid):
    return '{"v":"2","id":"%s","nonce":"%s"}' % (eid, kp.uid(eid))


class Bridge:
    def __init__(self, host, nrep, tag, rng, binall=False):
        self.h, self.n, self.rng = host, nrep, rng
        self.W = X.LWorld(nrep, binall=binall)
        self.dir = os.path.join(WORK, tag)
        os.makedirs(self.dir, exist_ok=True)
        for x in os.listdir(self.dir):
            os.remove(os.path.join(self.dir, x))
        self.files = [os.path.join(self.dir, 'f%d.kdbx' % i) for i in range(nrep)]
        self.rev = {}
        self.log = []
        self.disagree = []
        base = self.W.reps[0]
        self.h.run('new', self.files[0], '%016x' % rng.getrandbits(64))
        lines = [self.addline(u, base.entries[u]) for u in base.entries]
        lines += [self.delline(u, t) for u, t in base.dels.items()]
        self.patch(0, lines)
        for i in range(1, nrep):
            kp.copy(self.files[0], self.files[i])

    # ------------------------------------------------------------------ writing
    def addline(self, u, ent):
        eid, mt, ok = ent
        self.rev[kp.uid(u)] = u
        e = self.W.EL[eid]
        extra = ''
        if e.kind == 'head':
            extra = ';'.join(sorted('%s %s' % r for r in e.data['ref'])) or 'none'
        return '\t'.join(['A', kp.uid(u), str(mt), e.kind, eid, data_of(eid), extra])

    def delline(self, u, t):
        self.rev[kp.uid(u)] = u
        return '\t'.join(['D', kp.uid(u), str(t)])

    def patch(self, i, lines):
        if not lines:
            return
        p = os.path.join(self.dir, 'spec.txt')
        with open(p, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write('\n'.join(lines) + '\n')
        self.h.run('patch', self.files[i], p)

    # ------------------------------------------------------------------ reading
    def real(self, i):
        d = self.h.dump(self.files[i])
        ents, loc, dels = {}, {}, {}
        for u, e in d['entries'].items():
            mu = self.rev.get(u)
            if mu is None or mu != e['id']:
                mu = 'X:' + u          # an entry under an identifier the gate never gave it
            ents[mu] = [e['id'], e['mt'], e['intact']]
            place = e['place']
            loc[mu] = ['B' if place.startswith('B') else ('S' if place == 'S' else 'O'), e['loc']]
        for u, t in d['dels'].items():
            dels[self.rev.get(u, 'U:' + u)] = t
        return ents, loc, dels, d

    def compare(self, i, pred, step):
        ents, loc, dels, d = self.real(i)
        pe = {u: tuple(v) for u, v in pred.entries.items() if not u.startswith('X')}
        re_ = {u: tuple(v) for u, v in ents.items() if not u.startswith('X:')}
        diffs = []
        for u in sorted(set(pe) | set(re_)):
            if pe.get(u) != re_.get(u):
                diffs.append('entry %s: model %s, file %s' % (u, pe.get(u), re_.get(u)))
            elif u in pe:
                pl = X.getloc(pred, u)[0]
                if pl != loc[u][0]:
                    diffs.append('entry %s: model in %s, file in %s' % (u, pl, loc[u][0]))
        px = sorted((v[0], v[1], v[2]) for u, v in pred.entries.items() if u.startswith('X'))
        rx = sorted((v[0], v[1], v[2]) for u, v in ents.items() if u.startswith('X:'))
        if px != rx:
            diffs.append('entries under new identifiers: model %s, file %s' % (px, rx))
        for u in sorted(set(pred.dels) | set(dels)):
            if pred.dels.get(u) != dels.get(u):
                diffs.append('record %s: model %s, file %s' % (u, pred.dels.get(u), dels.get(u)))
        if diffs:
            self.disagree.append((step, i, diffs))
        return ents, loc, dels

    def adopt_real(self, i, ents, loc, dels):
        rep = self.W.reps[i]
        rep.entries = {u: list(v) for u, v in ents.items()}
        rep.loc = {u: list(v) for u, v in loc.items()}
        rep.dels = dict(dels)

    # ------------------------------------------------------------------ one step
    def step(self, k, op):
        W = self.W
        kind = op[0]
        gate = kind in ('pair', 'revoke', 'recreate', 'padd', 'prem', 'rule', 'renew', 'adopt', 'save', 'reseal')
        if gate:
            i = op[1]
            before = W.reps[i].clone()
            if not W.step(op):
                return False
            after = W.reps[i]
            lines = [self.addline(u, after.entries[u]) for u in after.entries if u not in before.entries]
            lines += ['\t'.join(['R', kp.uid(u)]) for u in before.entries if u not in after.entries]
            lines += [self.delline(u, t) for u, t in after.dels.items() if before.dels.get(u) != t]
            self.patch(i, lines)
            ents, loc, dels, _ = self.real(i)
            self.adopt_real(i, ents, loc, dels)
            self.log.append('%2d %s' % (k, ' '.join(map(str, op))))
            return True
        if kind in ('copy', 'strip'):
            ok = W.step(op)
            if ok:
                self.log.append('%2d %s (the tree only)' % (k, ' '.join(map(str, op))))
            return ok
        # transport and faults: predicted by the model, done for real
        P = W.clone()
        t = W.t + 10
        target = op[2] if kind == 'merge' else op[1]
        uid = None
        if kind in ('del', 'touch', 'edit'):
            u = W.pick(W.reps[op[1]], op[2])
            if not u:
                return False
            uid = kp.uid(u) if not u.startswith('X:') else u[2:]
        if kind == 'bin':
            u = W.pick_at(W.reps[op[1]], op[2], 'S')
            if not u:
                return False
            uid = kp.uid(u) if not u.startswith('X:') else u[2:]
        if not P.step(op):
            return False
        f = self.files
        note = ''
        if kind == 'merge':
            prog, d, s = op[1], op[2], op[3]
            tmp = os.path.join(self.dir, 'merged.kdbx')
            if prog in ('kp_sync', 'k2a'):
                self.h.run('merge', f[d], f[s], 'Synchronize', tmp)
                os.replace(tmp, f[d])
            elif prog == 'kp_other':
                m = self.rng.choice(KP_OTHER)
                note = ' [' + m + ']'
                self.h.run('merge', f[d], f[s], m, tmp)
                os.replace(tmp, f[d])
            elif prog == 'xc':
                v = self.rng.choice(sorted(kp.XC))
                note = ' [keepassxc-cli ' + v + ']'
                rc, txt = kp.xc_merge(v, f[d], f[s])
                if rc != 0:
                    raise RuntimeError('keepassxc-cli: ' + txt)
            else:
                raise ValueError(op)
        elif kind == 'del':
            if op[3] == 1:
                self.h.run('delrec', f[op[1]], uid, t)
            else:
                lines = ['\t'.join(['R', uid])]
                if op[3] == 2:
                    lines.append('\t'.join(['D', uid, '0']))
                self.patch(op[1], lines)
        elif kind == 'bin':
            self.h.run('bin', f[op[1]], uid, t)
        elif kind == 'touch':
            self.h.run('touch', f[op[1]], uid, t)
        elif kind == 'edit':
            self.h.run('edit', f[op[1]], uid, t)
        elif kind == 'empty':
            self.h.run('empty', f[op[1]], t)
        elif kind == 'clear':
            self.h.run('clear', f[op[1]])
        elif kind == 'over':
            kp.copy(f[op[2]], f[op[1]])
        elif kind == 'import':
            tmp = os.path.join(self.dir, 'merged.kdbx')
            self.h.run('merge', f[op[1]], f[op[2]], 'CreateNewUuids', tmp)
            os.replace(tmp, f[op[1]])
        else:
            raise ValueError(op)
        self.W = P
        ents, loc, dels = self.compare(target, P.reps[target], k)
        self.adopt_real(target, ents, loc, dels)
        self.log.append('%2d %s%s' % (k, ' '.join(map(str, op)), note))
        return True

    def check(self, k, op, before):
        W = self.W
        fails = []
        for i in range(self.n):
            A = W.fold(W.reps[i])
            w = M.narrower(A, W.ideal(i), W.reps[i].nodes)
            if w:
                fails.append(('safety', k, 'file %d: %s' % (i, w)))
        if before is not None:
            A = W.fold(W.reps[op[1]])
            w = M.narrower(A, before, W.reps[op[1]].nodes)
            if w:
                fails.append(('one-loss', k, 'file %d: %s' % (op[1], w)))
        return fails


def play(host, ops, nrep, tag, seed, binall=False):
    b = Bridge(host, nrep, tag, random.Random(seed), binall=binall)
    fails = []
    for k, op in enumerate(ops):
        before = None
        if op[0] in ('del', 'edit', 'bin', 'touch', 'empty'):
            before = b.W.fold(b.W.reps[op[1]])
        if not b.step(k, op):
            continue
        fails += b.check(k, op, before)
        if fails:
            break
    return b, fails


def views(b):
    out = []
    for i in range(b.n):
        v = b.W.fold(b.W.reps[i])
        out.append('file %d: %s, admitted %s, words %s, renewals %s, names %s' % (
            i, v['status'], sorted(v['adm']), {n: ''.join(sorted(w)) for n, w in sorted(v['words'].items())},
            dict(v['renew']), sorted(v['names'])))
    return out


# ---------------------------------------------------------------------- histories
def alphabet(nrep, rng):
    acts, merges, _ = X.alphabet(nrep, ['kp_sync', 'kp_other', 'xc'], [])
    R = range(nrep)
    faults = []
    for r in R:
        faults += [('del', r, s, k) for s in X.SELS for k in (0, 1, 2)]
        faults += [('bin', r, s) for s in X.SELS]
        faults += [('touch', r, s) for s in X.SELS]
        faults += [('edit', r, s) for s in X.SELS[:4]]
        faults += [('empty', r), ('clear', r), ('reseal', r)]
        faults += [('import', d, r) for d in R if d != r]
    return acts, merges, faults


def random_runs(count, seed):
    rng = random.Random(seed)
    host = kp.Host()
    t0 = time.time()
    lines, nsteps, ndis, nfail = [], 0, 0, 0
    try:
        for h in range(count):
            nrep = 2 if rng.random() < 0.6 else 3
            acts, merges, faults = alphabet(nrep, rng)
            ops = []
            for _ in range(rng.randint(4, 9)):
                x = rng.random()
                ops.append(rng.choice(acts if x < 0.4 else merges if x < 0.7 else faults))
            b, fails = play(host, ops, nrep, 'r', seed * 100000 + h)
            nsteps += len(b.log)
            if b.disagree or fails:
                lines.append('## history %d (%d files)' % (h, nrep))
                lines += ['   ' + x for x in b.log]
                for step, i, diffs in b.disagree:
                    ndis += 1
                    lines.append('   DISAGREE at step %d, file %d:' % (step, i))
                    lines += ['      ' + d for d in diffs]
                for f in fails:
                    nfail += 1
                    lines.append('   FAIL %s at step %d: %s' % f)
                lines += ['   ' + x for x in views(b)]
    finally:
        host.close()
    head = ['random histories: %d, steps done: %d, steps where a file disagreed with the model: %d, failures of the design\'s checks: %d, %.0f s'
            % (count, nsteps, ndis, nfail, time.time() - t0)]
    print(head[0])
    with open(os.path.join(OUT, 'bridge-random-%d.txt' % seed), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(head + lines) + '\n')


REPLAYS = [
    ('R1 revocation: its entry deleted for good where it was made, a KeePassXC merge into a stale copy, the head binned there', 2,
     [('revoke', 0, 'c0'), ('bin', 0, ('ref', 'revoked')), ('empty', 0), ('merge', 'xc', 1, 0), ('bin', 1, ('head', 'new'))]),
    ('R2 rule narrowed at the root, the new head binned, a KeePassXC merge into a stale copy, the new version binned there', 2,
     [('rule', 0, 'root', 'inherit'), ('bin', 0, ('head', 'new')), ('merge', 'xc', 1, 0), ('bin', 1, ('ver', 'rule:root', 'new'))]),
    ('R3 a rule narrowed; KeePassDroid saves the old head and the old version unchanged on a stale phone copy; Synchronize; both new entries binned', 2,
     [('rule', 0, 'root', 'none'), ('touch', 1, ('head', 'old')), ('touch', 1, ('ver', 'rule:root', 'new')),
      ('merge', 'kp_sync', 0, 1), ('bin', 0, ('head', 'new')), ('bin', 0, ('ver', 'rule:root', 'new'))]),
    ('R4 a renewal refused forever in one gate copy, its head and answer binned, a concurrent write in another gate copy, Synchronize', 2,
     [('renew', 1, 'c0', 'forever'), ('bin', 1, ('head', 'old')), ('rule', 0, 'root', 'r'), ('bin', 1, ('ver', 'renew:c0', 'new')),
      ('merge', 'kp_sync', 1, 0)]),
    ('R5 the design\'s own scenario 7: newest version and head deleted with records, then a stale copy by KeePassXC', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'xc', 0, 1)]),
    ('R6 the design\'s scenario 9: the same, deletion records cleared, Synchronize with the stale copy', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0),
      ('merge', 'kp_sync', 0, 1)]),
    ('R7 the design\'s scenario 18: both carriers of a revocation deleted, Seal again, KeePassXC into a stale copy', 2,
     [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0), ('del', 0, ('head', 'new'), 0),
      ('reseal', 0), ('merge', 'xc', 1, 0)]),
    ('R8 the design\'s scenario 20: an import with new identifiers of a stale copy', 2,
     [('rule', 0, 'N', 'none'), ('revoke', 0, 'c0'), ('import', 0, 1)]),
    ('R9 three copies: a narrowing reaches a third copy through a KeePassXC copy, and two bins there', 3,
     [('rule', 0, 'root', 'none'), ('merge', 'xc', 1, 0), ('merge', 'kp_sync', 2, 1),
      ('bin', 2, ('head', 'new')), ('bin', 2, ('ver', 'rule:root', 'new'))]),
]


def replays():
    host = kp.Host()
    lines = []
    try:
        for name, nrep, ops in REPLAYS:
            for binall in (False, True):
                b, fails = play(host, ops, nrep, 'replay', 7, binall=binall)
                lines.append('## %s%s' % (name, '   [with the bin read for every kind]' if binall else ''))
                lines += ['   ' + x for x in b.log]
                for step, i, diffs in b.disagree:
                    lines.append('   DISAGREE at step %d, file %d:' % (step, i))
                    lines += ['      ' + d for d in diffs]
                lines += ['   FAIL %s at step %d: %s' % f for f in fails] or ['   no failure of the checks']
                lines += ['   ' + x for x in views(b)]
                lines.append('')
    finally:
        host.close()
    print('\n'.join(lines))
    with open(os.path.join(OUT, 'bridge-replays.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    if sys.argv[1] == 'random':
        random_runs(int(sys.argv[2]), int(sys.argv[3]))
    else:
        replays()
