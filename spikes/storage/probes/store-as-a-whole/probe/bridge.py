"""The recommended store over real files, beside the model's prediction.

A copy in spirit of probes\\crdt\\survival\\probe\\bridge.py, rewritten for the store as decided.
Every gate act is the model's write path (the family's crdt_model.py under STORE, through
model_ext.LWorld with every entry counted wherever it sits), written into a real .kdbx as entries
with a keyed check (store.py). Every other step is done on the file by KeePassLib 2.62 or
keepassxc-cli. After each step:

- the model's prediction for the file is compared with the file, entry by entry and record by
  record (as the survival critique's bridge did), and the read rule of read.py is applied to the
  file and compared with the model's read of its prediction (C of the brief);
- the model then continues from what the file holds.

After a Synchronize at a gate, the repair of change 7 runs on what the file holds: the gate's state
before the merge against the merged file, and what takes access away again is written into the file
before it is saved (and, with both=True, into the other file of the Synchronize too, as KeePass
writes the merged database to both; that order is read, not run).

ADDED (decided store): `decide()` switches the module to the store as decided on 2026-11-16: no
repair after a Synchronize (decision 14), and every read through the history rule (decision 7):
read.py with `history`, over dumps that carry each entry's history items; the model adopts an entry
as intact when any of its versions passes the check, which is what the rule counts. Its databases go
to `work\\d-<tag>`, so that both configurations can run at once.

ADDED (two readings): two further configurations of the store as decided. `rewrite(form)`: at every
write of the plugin's own (a gate act, never a merge and never a read), after the act, every record
of that file that counts only by its history is written again so that its written version is
current again (Store.cs `rewrite`, form 'backup' or 'cleared'). `copies(variant)`: every read by
read.py's reading of a copy under another identifier ('held' or 'exempt'), and a gate act refused
where that reading stops the store while the model, which holds no such reading, would write.
`recanon(i)` writes a record counted from a copy under the identifier its content gives. Their
databases go to `work\\w<form>-<tag>` and `work\\c<variant>-<tag>`.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'model'))
import kp  # noqa: E402
import read as RD  # noqa: E402
import store as S  # noqa: E402
import crdt_model as M  # noqa: E402
import model_ext as X  # noqa: E402

WORK = os.path.join(kp.FAMILY, 'work')
TREE = 'ab112233445566778899aabbccddeeff'
# The model's switches in the bridge: the store, with change 4 off (a rule written at the dialog's
# OK, since no file shows a rule waiting in memory) and the repair done here on real files.
FLAGS = M.STORE.but(pending=False, repair=False)
METHODS = ('Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids')
RELEASES = ('2.4.2', '2.6.6', '2.7.12', '2.8.0')
DECIDED = False     # ADDED (decided store): set by decide()
REWRITE = None      # ADDED (two readings): 'backup' or 'cleared', set by rewrite()
COPIES = None       # ADDED (two readings): 'held' or 'exempt', set by copies()
PREFIX = ''         # ADDED (two readings): the configuration's directories under work


def decide():
    """ADDED (decided store): no repair, and the history rule in every read."""
    global DECIDED, PREFIX
    DECIDED = True
    PREFIX = 'd-'   # ADDED (two readings): what was written inline before


def rewrite(form):
    """ADDED (two readings): the store as decided, and an edited record written again at the
    plugin's next own write."""
    global REWRITE, PREFIX
    decide()
    REWRITE, PREFIX = form, 'w%s-' % form[0]
    if form == 'none':      # the control: the same parts with nothing written again
        REWRITE = None


def copies(variant):
    """ADDED (two readings): the store as decided, read with a record that can only take access away
    counted from a copy under another identifier."""
    global COPIES, PREFIX
    decide()
    COPIES, PREFIX = variant, 'c%s-' % variant[0]


def rd(dump, history):
    """ADDED (two readings): read.py's read in the configuration."""
    return RD.read(dump, history=history, copies=COPIES)


def versions(e):
    """ADDED (decided store): an entry's versions as the reading takes them: the current, then,
    with the history rule, its history items newest first."""
    return [e['strings']] + ([h['strings'] for h in reversed(e.get('history', []))] if DECIDED else [])


def node_ids():
    root = TREE[0:2] + '00' + TREE[2:30]
    return {'root': root, 'General': TREE[0:2] + '01' + TREE[2:30], 'Other': TREE[0:2] + '02' + TREE[2:30],
            'N': TREE[0:2] + '03' + TREE[2:30]}


class Bridge:
    def __init__(self, host, nrep, tag, speed='fast', repair=None, both=False, flags=FLAGS):
        if repair is None:          # ADDED (decided store): the repair unless decided
            repair = not DECIDED
        self.h, self.n, self.speed, self.repair, self.both = host, nrep, speed, repair, both
        self.W = X.LWorld(nrep, flags, binall=True)
        self.ids = node_ids()
        self.enc = S.Encoder(self.W, {'N': self.ids['N'], 'root': self.ids['root']})
        self.macs = {}
        self.by_history = 0     # ADDED (decided store): an edit the model reads as damage, the file by history
        self.rewrites, self.copy_stops = [], 0     # ADDED (two readings)
        self.dir = os.path.join(WORK, PREFIX + tag)     # ADDED (two readings): PREFIX
        os.makedirs(self.dir, exist_ok=True)
        for x in os.listdir(self.dir):
            os.remove(os.path.join(self.dir, x))
        self.files = [os.path.join(self.dir, 'f%d.kdbx' % i) for i in range(nrep)]
        self.log, self.disagree, self.readdiff = [], [], []
        self.t0 = 0
        base = self.W.reps[0]
        self.h.run('new', self.files[0], '5a17c0de00000001', speed, TREE)
        lines = [self.addline(u, base.entries[u]) for u in base.entries]
        lines += [self.delline(u, t) for u, t in base.dels.items()]
        self.patch(0, lines)
        for i in range(1, nrep):
            kp.copy(self.files[0], self.files[i])

    # ------------------------------------------------------------------ writing
    def uid(self, mu):
        return self.enc.u(mu) if not mu.startswith('X:') else mu[2:]

    def addline(self, u, ent):
        eid, mt, ok = ent
        uu, title, strings, prot = self.enc.entry(eid)
        self.macs[strings['KPG.Mac']] = eid
        parts = ['A', uu, str(mt), kp.b64(title)]
        for k in sorted(strings):
            parts.append(('!' if k in prot else '') + k + '=' + kp.b64(strings[k]))
        return '\t'.join(parts)

    def delline(self, u, t):
        if u.startswith('T|'):
            raise ValueError('a witness in the store as decided')
        return '\t'.join(['D', self.uid(u), str(t)])

    def patch(self, i, lines):
        if not lines:
            return
        p = os.path.join(self.dir, 'spec.txt')
        with open(p, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write('\n'.join(lines) + '\n')
        self.h.run('patch', self.files[i], p)

    # ------------------------------------------------------------------ reading
    def dump(self, i):
        """ADDED (decided store): the file's dump, with history items when decided."""
        return self.h.dump(self.files[i], history=DECIDED)

    def read(self, i, history=None):
        """ADDED (decided store): read.py's answer for file i, by the configuration's rule unless
        `history` names a rule."""
        h = DECIDED if history is None else history
        return rd(self.h.dump(self.files[i], history=h), h)     # ADDED (two readings): rd

    def real(self, i):
        d = self.dump(i)
        ents, loc, dels = {}, {}, {}
        for u, e in d['entries'].items():
            vs = versions(e)        # ADDED (decided store): the current alone unless decided
            if not any('KPG.Kind' in x for x in vs):
                continue
            mu = self.enc.eid.get(u)
            eid = mu
            for x in vs:
                if eid is None:
                    eid = self.macs.get(x.get('KPG.Mac'))
            if eid is None:
                continue        # an entry the bridge never wrote, which no step here makes
            key = S.keybytes(self.W.EL[eid].key)
            intact = any(S.mac(key, x.get('KPG.Kind', ''), x.get('KPG.Data', ''),
                               {k: v for k, v in x.items() if k.startswith('KPG.S.')}) == x.get('KPG.Mac')
                         for x in vs)
            if mu is None:
                mu = 'X:' + u   # an entry under an identifier the gate never gave it
            ents[mu] = [eid, e['mt'], intact]
            place = e['place']
            loc[mu] = ['B' if place.startswith('B') else ('S' if place == 'S' else 'O'), e['loc']]
        for u, t in d['dels'].items():
            mu = self.enc.eid.get(u)
            dels[mu if mu is not None else 'U:' + u] = t
        return ents, loc, dels, d

    def compare(self, i, pred, step):
        ents, loc, dels, d = self.real(i)
        pe = {u: tuple(v) for u, v in pred.entries.items() if not u.startswith('X')}
        re_ = {u: tuple(v) for u, v in ents.items() if not u.startswith('X:')}
        if DECIDED:     # ADDED (decided store): the model reads an edit as damage, the rule by history
            for u, v in list(pe.items()):
                if not v[2] and re_.get(u) == (v[0], v[1], True):
                    pe[u] = re_[u]
                    self.by_history += 1
        diffs = []
        for u in sorted(set(pe) | set(re_)):
            if pe.get(u) != re_.get(u):
                diffs.append('entry %s: model %s, file %s' % (u, pe.get(u), re_.get(u)))
        px = sorted((v[0], v[1], v[2]) for u, v in pred.entries.items() if u.startswith('X'))
        rx = sorted((v[0], v[1], v[2]) for u, v in ents.items() if u.startswith('X:'))
        if px != rx:
            diffs.append('entries under new identifiers: model %s, file %s' % (px, rx))
        for u in sorted(set(pred.dels) | set(dels)):
            if u.startswith('U:'):
                continue    # a record of a user's node or group, which the model does not hold
            if pred.dels.get(u) != dels.get(u):
                diffs.append('record %s: model %s, file %s' % (u, pred.dels.get(u), dels.get(u)))
        if diffs:
            self.disagree.append((step, i, diffs))
        return ents, loc, dels, d

    def adopt_real(self, i, ents, loc, dels):
        rep = self.W.reps[i]
        rep.entries = {u: list(v) for u, v in ents.items()}
        rep.loc = {u: list(v) for u, v in loc.items()}
        rep.dels = dict(dels)

    def as_model(self, r):
        """read.py's answer in the model's names."""
        inv = {v: k for k, v in self.ids.items()}
        out = dict(r)
        out['words'] = {inv[n]: w for n, w in r['words'].items() if n in inv and inv[n] in ('N', 'root')}
        out['words_raw'] = {inv[n]: w for n, w in r['words_raw'].items() if n in inv and inv[n] in ('N', 'root')}
        return out

    def readcheck(self, i, d, step):
        """C: the model's read of what the file holds against read.py's read of the file."""
        v = self.W.fold(self.W.reps[i])
        r = self.as_model(rd(d, DECIDED))     # ADDED (two readings): rd
        mine = (v['status'], v['served'], sorted((c, sorted(p)) for c, p in v['adm'].items()),
                sorted((n, ''.join(sorted(w))) for n, w in v['words'].items() if n in ('N', 'root')),
                sorted(c for c, a in v['renew'].items() if a == 'forever'), sorted(v['names']))
        theirs = (r['status'], r['served'], sorted((c, sorted(p)) for c, p in r['adm'].items()),
                  sorted((n, ''.join(sorted(w))) for n, w in r['words'].items()),
                  sorted(c for c, a in r['renew'].items() if a == 'forever'), sorted(r['names']))
        if mine != theirs:
            self.readdiff.append((step, i, 'model %r; read.py %r' % (mine, theirs)))
        return r

    def sync_file(self, i):
        ents, loc, dels, d = self.real(i)
        self.adopt_real(i, ents, loc, dels)
        return d

    # ADDED (two readings)
    def rewrite_again(self, i):
        """At a write of the plugin's own on file i, after the act: every record that counts only
        by its history is written again from the history item it counts by. Answers the file's
        dump after it, or None when nothing was written, and lines for the log."""
        r = rd(self.dump(i), True)
        if not r['byhist']:
            return None, []
        t = self.W.t
        notes = []
        for u, idx in sorted(r['byhist'].items()):
            out = self.h.run('rewrite', self.files[i], u, t, REWRITE, idx)
            self.rewrites.append((i, u))
            notes.append('   the gate wrote again at %d: %s, from history item %d (%s)' % (i, u[:8], idx, out))
        return self.sync_file(i), notes

    def recanon(self, i):
        """A record counted from a copy written again under the identifier its content gives, where
        the file holds neither an entry nor a deletion record under it. Lines for the log."""
        d = self.dump(i)
        r = rd(d, True)
        notes = []
        for u, c in sorted(r['fromcopy'].items()):
            if u in d['entries'] or u in d['dels']:
                notes.append('   %s, counted from a copy: not written, the file holds %s' % (
                    u[:8], 'its entry' if u in d['entries'] else 'its deletion record'))
                continue
            out = self.h.run('recanon', self.files[i], c, u, self.W.t)
            # the copy's own identifier is drawn at random by the import, so it is not printed
            notes.append('   %s, counted from a copy: written under its own identifier (%s)' % (u[:8], out))
        self.sync_file(i)
        return notes

    # ------------------------------------------------------------------ one step
    def gate_write(self, i, before):
        after = self.W.reps[i]
        lines = [self.addline(u, after.entries[u]) for u in after.entries if u not in before.entries]
        lines += ['\t'.join(['R', self.uid(u)]) for u in before.entries if u not in after.entries]
        lines += [self.delline(u, t) for u, t in after.dels.items() if before.dels.get(u) != t]
        self.patch(i, lines)
        return self.sync_file(i)

    def step(self, k, op):
        W = self.W
        kind = op[0]
        if kind in M.GATE_ACTS:
            i = op[1]
            # ADDED (two readings): the plugin is stopped where the reading of copies stops the store
            if COPIES and kind != 'create' and op[1] < len(W.reps) and W.fold(W.reps[i])['status'] == 'ok':
                if rd(self.dump(i), DECIDED)['status'] != 'ok':
                    self.copy_stops += 1
                    return False
            before = W.reps[i].clone()
            if not W.step(op):
                return False
            d = self.gate_write(i, before)
            notes = []
            if REWRITE:     # ADDED (two readings): what counts only by its history, written again
                d2, notes = self.rewrite_again(i)
                d = d2 or d
            self.readcheck(i, d, k)
            self.log.append('%2d %s' % (k, ' '.join(map(str, op))))
            self.log += notes
            return True
        if kind in ('dup', 'move', 'restore'):
            return self.tree_step(k, op)
        if kind in ('dupel', 'dupgroup', 'dde', 'deg'):
            return self.maintenance(k, op)
        if kind == 'kimport':
            return self.kimport(k, op[1], op[2], op[3], op[4])
        if kind == 'plantk':        # somebody holding the store key writes a version and a head
            i = op[1]
            before = W.reps[i].clone()
            if not W.step(op):
                return False
            d = self.gate_write(i, before)
            self.readcheck(i, d, k)
            self.log.append('%2d %s' % (k, ' '.join(map(str, op))))
            return True
        P = W.clone()
        t = W.t + 10
        target = op[2] if kind == 'merge' else op[1]
        uid = None
        if kind in ('del', 'touch', 'edit'):
            u = W.pick(W.reps[op[1]], op[2])
            if not u:
                return False
            uid = self.uid(u)
        if kind == 'bin':
            u = W.pick_at(W.reps[op[1]], op[2], 'S')
            if not u:
                return False
            uid = self.uid(u)
        if kind == 'unbin':
            u = W.pick_at(W.reps[op[1]], op[2], 'B')
            if not u:
                return False
            uid = self.uid(u)
        mop = op
        if kind == 'merge':
            mop = op[:4]
            if op[1] == 'cni':
                mop = ('import', op[2], op[3])
        before_fold = W.fold(W.reps[target]) if kind == 'merge' and op[1] == 'kp_sync' else None
        if not P.step(mop):
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
                m = op[4]
                note = ' [' + m + ']'
                self.h.run('merge', f[d], f[s], m, tmp)
                os.replace(tmp, f[d])
            elif prog == 'cni':
                note = ' [CreateNewUuids]'
                self.h.run('merge', f[d], f[s], 'CreateNewUuids', tmp)
                os.replace(tmp, f[d])
            elif prog == 'xc':
                v = op[4]
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
        elif kind == 'unbin':
            self.h.run('restore', f[op[1]], uid, t)
            # the model puts it back in the store group; the file into the group it left
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
        elif kind == 'dedupe':
            self.h.run('dedupe', f[op[1]])
        else:
            raise ValueError(op)
        self.W = P
        self.enc.W = P
        ents, loc, dels, d = self.compare(target, P.reps[target], k)
        self.adopt_real(target, ents, loc, dels)
        self.readcheck(target, d, k)
        self.log.append('%2d %s%s' % (k, ' '.join(map(str, op[:4])), note))
        if before_fold is not None:
            self.after_sync(k, op[2], op[3], before_fold)
        return True

    def after_sync(self, k, d, s, before):
        """KeePass's Synchronize at gate d: the repair inside its save, then the other file."""
        W = self.W
        if self.both == 'before':          # the other file written before the plugin's additions
            kp.copy(self.files[d], self.files[s])
            W.other(('over', s, d))
            self.sync_file(s)
            self.log.append('   the other file of the Synchronize written from gate %d before the repair' % d)
        if self.repair:
            prev = W.reps[d].clone()
            n0 = dict(W.stats)
            W.repair(d, before)
            if W.stats != n0:
                self.gate_write(d, prev)
                self.log.append('   the repair at gate %d wrote: %s' % (d, ', '.join(
                    '%s +%d' % (x, W.stats[x] - n0.get(x, 0)) for x in sorted(W.stats) if W.stats[x] != n0.get(x, 0))))
                self.readcheck(d, self.dump(d), k)
        if self.both == 'after':           # KeePass writes the merged database, additions in, to both
            kp.copy(self.files[d], self.files[s])
            W.other(('over', s, d))
            self.sync_file(s)
            self.log.append('   the other file of the Synchronize written from gate %d' % d)

    # Added from the desktop measurement: maintenance commands over the store.
    def maintenance(self, k, op):
        """dupel i sel: Duplicate Entry with no suffix (equal strings, another identifier, later);
        dupgroup i suffix: Duplicate Group of the plugin's group; dde i: Delete Duplicate Entries,
        whose records KeePassLib dates at the moment it runs; deg i: Delete Empty Groups."""
        W = self.W
        P = W.clone()
        i = op[1]
        rep = P.reps[i]
        t = W.t + 10
        P.t = t
        f = self.files[i]
        if op[0] == 'dupel':
            u = W.pick(W.reps[i], op[2])
            if not u:
                return False
            self.ncopy = getattr(self, 'ncopy', 0) + 1
            nu = 'cd%030x' % self.ncopy
            self.h.run('dup', f, self.uid(u), nu, t)
            rep.entries['X:' + nu] = [rep.entries[u][0], t, True]
            rep.loc['X:' + nu] = ['S', t]
        elif op[0] == 'dupgroup':
            self.h.run('dupgroup', f, '1' if op[2] else '0', t)
            self.suffixed = getattr(self, 'suffixed', set())
            for u, ent in list(rep.entries.items()):
                if X.getloc(rep, u)[0] == 'S' and not u.startswith('X'):
                    self.ncopy = getattr(self, 'ncopy', 0) + 1
                    key = 'Xg%d' % self.ncopy
                    rep.entries[key] = [ent[0], t, ent[2]]
                    rep.loc[key] = ['O', t]
                    if op[2]:
                        self.suffixed.add(ent[0])
        elif op[0] == 'dde':
            out = self.h.run('dedupe', f)
            suffixed = getattr(self, 'suffixed', set())
            copied = set(ent[0] for u, ent in rep.entries.items() if u != ent[0] and ent[0] not in suffixed)
            for u in [u for u, ent in rep.entries.items() if u == ent[0] and u in copied]:
                del rep.entries[u]
                rep.loc.pop(u, None)
                rep.dels[u] = t
            self.log.append('   KeePassLib: Delete Duplicate Entries %s' % out)
        elif op[0] == 'deg':
            out = self.h.run('deg', f)
            self.log.append('   KeePassLib: Delete Empty Groups %s' % out)
        self.W = P
        self.enc.W = P
        ents, loc, dels, d = self.compare(i, P.reps[i], k)
        self.adopt_real(i, ents, loc, dels)
        self.readcheck(i, d, k)
        self.log.append('%2d %s' % (k, ' '.join(map(str, op))))
        return True

    def kimport(self, k, d, s, method, repair_at_save):
        """KeePass's File, Import of the other file with one of its methods: merged into the open
        file only, and no event a plugin sees (measured). With repair_at_save, the repair runs at the
        person's next save (FileSavingPre) against the state the plugin held before."""
        before = self.W.fold(self.W.reps[d])
        if method == 'Synchronize':
            op = ('merge', 'kp_sync', d, s)
        elif method == 'CreateNewUuids':
            op = ('merge', 'cni', d, s)
        else:
            op = ('merge', 'kp_other', d, s, method)
        keep = self.repair, self.both
        self.repair, self.both = False, False
        try:
            ok = self.step(k, op)
        finally:
            self.repair, self.both = keep
        if not ok:
            return False
        self.log[-1] += ' (as File, Import)'
        if repair_at_save:
            prev = self.W.reps[d].clone()
            n0 = dict(self.W.stats)
            self.W.repair(d, before)
            if self.W.stats != n0:
                self.gate_write(d, prev)
                self.log.append('   the repair at the next save of gate %d wrote: %s' % (d, ', '.join(
                    '%s +%d' % (x, self.W.stats[x] - n0.get(x, 0)) for x in sorted(self.W.stats)
                    if self.W.stats[x] != n0.get(x, 0))))
        return True

    def tree_step(self, k, op):
        """A user's node moved, duplicated or restored: real files only; the model has no tree."""
        f = self.files
        t = self.W.now()
        if op[0] == 'dup':
            self.h.run('dup', f[op[1]], self.ids['N'], op[2], t)
        elif op[0] == 'move':
            self.h.run('move', f[op[1]], self.ids['N'], op[2], t)
        elif op[0] == 'restore':
            self.h.run('restore', f[op[1]], self.ids['N'], t)
        self.log.append('%2d %s (a user node: the model has no tree)' % (k, ' '.join(map(str, op))))
        return True


def global_ideal(W, i):
    r = M.Rep()
    r.nodes = W.reps[i].nodes
    for k in W.Kever:
        for eid in k:
            r.entries[eid] = [eid, 0, True]
    return W.fold(r, M.IDEAL)


def outcome(b, i, history=None, dump=None):
    """The file's answer by read.py, held to every decision any gate made (the oracle). ADDED
    (decided store): read by the configuration's rule unless `history` names a rule. ADDED (two
    readings): `dump`, a dump of file i already made, read instead of a new one."""
    if dump is None:
        r = b.as_model(b.read(i, history))
    else:
        r = b.as_model(rd(dump, DECIDED if history is None else history))
    I = global_ideal(b.W, i)
    nodes = ('root', 'N')
    w = M.narrower(r, I, nodes)
    if w:
        return 'WIDER: ' + w, r
    if I['status'] == 'ok' and I['served'] and r['status'] != 'ok':
        return 'refuses: the whole database (store %s)' % r['status'], r
    if I['served'] and not r['served']:
        return 'refuses: nothing served (%d authorities stand)' % len(r['auths']), r
    t = M.narrower_than(r, I, nodes)
    return ('narrower: ' + t if t else 'as decided'), r


def view(r):
    return '%s, served %s, admitted %s, words %s, refused renewals %s, names %s' % (
        r['status'], r['served'], {c: ' '.join(sorted(p)) for c, p in sorted(r['adm'].items())},
        {n: ''.join(sorted(w)) for n, w in sorted(r['words'].items())},
        sorted(c for c, a in r['renew'].items() if a == 'forever'), r['names'])


def play(host, ops, nrep, tag, **kw):
    b = Bridge(host, nrep, tag, **kw)
    for k, op in enumerate(ops):
        if not b.step(k, op):
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b
