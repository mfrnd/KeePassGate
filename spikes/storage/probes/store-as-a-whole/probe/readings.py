"""ADDED (two readings): the two readings of the store as decided that were left open, on real files.

Run:  uv run --no-project python -I -X utf8 readings.py --rewrite FORM PART
      uv run --no-project python -I -X utf8 readings.py --copies VARIANT copies

Reading 1, `--rewrite backup` or `--rewrite cleared` (bridge.rewrite): at every write of the plugin's
own, every record that counts only by its history is written again, so that its written version is
current again. `--rewrite none` is the control: the same parts with nothing written again. Parts:

  one        each kind of store entry and each operation of `b-history`, then one write of the
             gate's own: the read, the entry's versions, how many further edits it survives, and a
             merge with the unedited copy both ways by Synchronize and by KeePassXC 2.8.0
  alternate  edits and the gate's own writes alternating, past KeePass's default limit of 10
             history items, and the gate's writes alone after one edit
  two        two gates: the same edited record written again in both copies; one copy writing it
             again while the other holds a later edit of it, both orders; then every merge both
             ways; and three rounds of edit, both writes and Synchronize both ways
  prune      two gates: a record edited at copy 0, a later write at copy 1 that takes it out (a
             revocation, a Recreate, a name removed, a rule widened, renewals allowed again), and
             copy 0's write that writes it again, both orders; then every merge both ways

Reading 2, `--copies held` or `--copies exempt` (bridge.copies): part `copies`, copies of each kind
of store entry that are not an import, and the reading of copies combined with writing such a
record again under the identifier its content gives. The model holds no reading of copies, so its
disagreements with the file are not listed in this part.

ADDED (copies extended): `readings.py --reading LEVEL VARIANT PART`, the wider readings of a copy
(bridge.extended, read.py's `ext`). Parts:

  copies     the part above under the wider reading, with a rule per profile among its kinds; a
             replaced narrowing and what clears a limit a copy sets; and the reading combined with
             the decided rewrite, the plugin writing nothing from a copy, with Seal again after a
             stop that a copy caused, and the copies then deleted by hand. The combination that
             writes a copied record under its own identifier follows as the control.
  kinds      the three kinds whose reading from a copy is switched on and off on the same file: a
             retirement that settles which authority is served, a summary entry's items, and a
             store key taken from a copy

Every cell's file is read by read.py under the configuration's rule and held to every decision any
gate made, as in cases.py. Letters in the sequences: A as decided, n narrower, R the whole database
refuses with the store key held, K the whole database refuses with no store key held, x nothing
served, W wider.
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import read as RD  # noqa: E402


def letter(o, r):
    if o.startswith('WIDER'):
        return 'W'
    if o.startswith('refuses: the whole'):
        return 'K' if r['status'] == 'nostore' else 'R'
    if o.startswith('refuses'):
        return 'x'
    if o.startswith('narrower'):
        return 'n'
    return 'A'


def look(b, i, u):
    """The outcome of file i, and the state of entry u there: how many history items it holds and,
    current first and then the history newest first, which versions pass (P) under a key held."""
    d = b.dump(i)
    o, r = B.outcome(b, i, dump=d)
    raw = B.rd(d, True)
    e = d['entries'].get(u)
    if e is None:
        state = 'entry gone'
    else:
        vs = [e['strings']] + [h['strings'] for h in reversed(e.get('history', []))]
        marks = ''
        for v in vs:
            c = RD.version(v)
            c.u = u
            marks += 'P' if any(RD.verifies(c, k) for k in raw['held']) else '-'
        state = 'history %d, versions %s' % (len(vs) - 1, marks)
    return o, r, state


def build(host, base, tag):
    b = B.Bridge(host, 2, tag)
    for k, op in enumerate(base):
        if not b.step(k, op):
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b


def edit(b, host, i, u, edits, field):
    """An operation of C.HIST_OPS on entry u of file i, as part_history makes it."""
    f = b.files[i]
    t = b.W.t + 10
    if edits == 'move':
        out = host.run('move', f, u, 'General', t)
    elif edits == 'bin':
        out = host.run('bin', f, u, t)
    elif edits == 'delrec':
        out = host.run('delrec', f, u, t)
    else:
        for e in edits:
            out = host.run('history-edit', f, u, t, e, field)
            t += 60
    b.W.t = t + 10
    b.sync_file(i)
    return out


class Snap:
    """Both files and the bridge's state, kept so that each column starts from the same place."""

    def __init__(self, b, name):
        self.b = b
        self.paths = [f + '.' + name for f in b.files]
        for f, p in zip(b.files, self.paths):
            kp.copy(f, p)
        self.W = b.W.clone()
        self.enc = (dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))
        self.nrw = len(b.rewrites)

    def restore(self):
        b = self.b
        for f, p in zip(b.files, self.paths):
            kp.copy(p, f)
        b.W = self.W.clone()
        b.enc.W = b.W
        b.enc.uuid, b.enc.eid, b.macs = dict(self.enc[0]), dict(self.enc[1]), dict(self.enc[2])
        del b.rewrites[self.nrw:]


def own_write(b, i, key_carrier, name, k=30):
    """A write of the gate's own at file i: Create where no authority stands for the key carrier,
    else a profile name added."""
    op = ('create', i) if key_carrier and not b.W.fold(b.W.reps[i])['served'] else ('padd', i, name)
    n0 = len(b.rewrites)
    ok = b.step(k, op)
    return ok, len(b.rewrites) - n0, ' '.join(map(str, op))


# ------------------------------------------------------------------ reading 1, one gate
def part_one(host):
    print('== Reading 1 (%s): each kind of store entry edited at copy 0 by each operation of' % (B.REWRITE or 'none, the control'))
    print('   b-history, then one write of the gate\'s own at copy 0 (a profile name added; Create for')
    print('   the retirement that holds the store key, where no authority stands), which writes again')
    print('   every record that counts only by its history. Rows: after the edit; after the write;')
    print('   further edits after the write (a dialog edit of the payload, then dialog edits of the')
    print('   title) until the store refuses, at most 12; and the write\'s file merged with the unedited')
    print('   copy 1 both ways. "versions" lists the current version and then the history newest first,')
    print('   P where a version passes the check under a key the file holds.')
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in C.HIST_BASE))
    print('   base of the last kind: ' + ' ; '.join(' '.join(map(str, op)) for op in C.HIST_BASE_KEY))
    tally = {}
    for kind_name, sel, field, base in C.HIST_KINDS:
        key_carrier = sel == ('carry',)
        print('## %s' % kind_name)
        for oid, what, edits in C.HIST_OPS:
            b = build(host, base, 'r1o')
            if oid == 'off':
                for x in b.files:
                    host.run('limits', x, 0)
            u = b.uid(C.hist_pick(b, sel))
            out = edit(b, host, 0, u, edits, field)
            o, r, st = look(b, 0, u)
            print('   %s: %s (%s)' % (oid, what, out))
            print('      %-28s %s [store %s, by history %d] %s' % ('after the edit', o, r['status'], r['counts']['byhistory'], st))
            ok, nrw, opname = own_write(b, 0, key_carrier, 'ph')
            o, r, st = look(b, 0, u)
            print('      %-28s %s%s [store %s, by history %d, written again %d] %s' % (
                'the gate writes (' + opname + ')', '' if ok else 'cannot write; ', o, r['status'],
                r['counts']['byhistory'], nrw, st))
            tally[letter(o, r)] = tally.get(letter(o, r), 0) + 1
            snap = Snap(b, 'w')
            # further edits after the write, until the store refuses
            if not ok or st == 'entry gone':
                print('      %-28s %s' % ('further edits', 'not run: %s' % ('the gate could not write' if not ok else 'the entry is gone')))
            else:
                seq, n = '', 0
                for n in range(1, 13):
                    b.W.t += 10
                    host.run('history-edit', b.files[0], u, b.W.t, 'char' if n == 1 else 'title', field)
                    b.sync_file(0)
                    o2, r2, st2 = look(b, 0, u)
                    seq += letter(o2, r2)
                    if letter(o2, r2) in 'RKxW':
                        break
                stop = 'refuses at edit %d' % n if seq[-1] in 'RKx' else ('WIDER at edit %d' % n if seq[-1] == 'W' else 'reads on after 12')
                print('      %-28s %s (%s) %s' % ('further edits', seq, stop, st2))
            for label, d, s, prog in C.HIST_MERGES:
                snap.restore()
                b.step(40, C.merge_op(prog, d, s))
                o, r, st = look(b, d, u)
                print('      %-28s %s [store %s, by history %d] %s' % (label, o, r['status'], r['counts']['byhistory'], st))
                tally[letter(o, r)] = tally.get(letter(o, r), 0) + 1
            for x in C.c_lines(b, '      '):
                print(x)
    print('== tally of the reads after the write and after each merge: ' + ', '.join('%s %d' % kv for kv in sorted(tally.items())))


# ------------------------------------------------------------------ reading 1, alternating
PATTERNS = [(1, 22), (3, 8), (10, 3), (11, 2), (0, 12)]


def part_alternate(host):
    print('== Reading 1 (%s): at copy 0, edits of one entry and writes of the gate\'s own alternating.' % (B.REWRITE or 'none, the control'))
    print('   "k edits then a write": a dialog edit of the payload and k-1 dialog edits of the title,')
    print('   then a profile name added (Create first for the retirement that holds the store key).')
    print('   "0": one dialog edit, then the gate\'s writes alone. Each cycle: the letter after the')
    print('   edits, the letter after the write, the records written again, and the entry\'s history')
    print('   items and the place of the newest passing version (0 the current, 1 the newest history')
    print('   item; - none). A cycle after which the store refuses ends the sequence. KeePass\'s default')
    print('   history limits. C: the steps where the model and the file disagreed, counted.')
    for kind_name, sel, field, base in C.HIST_KINDS:
        key_carrier = sel == ('carry',)
        print('## %s' % kind_name)
        for k, cycles in PATTERNS:
            b = build(host, base, 'r1a')
            cells = []
            names = iter('q%d' % n for n in range(1, 99))
            eid0 = C.hist_pick(b, sel)
            u = b.uid(eid0)
            if k == 0:
                edit(b, host, 0, u, ['char'], field)
            for c in range(cycles):
                if k:
                    # the same entry every cycle; the summary entry the newest, since every write
                    # replaces it
                    eid = C.hist_pick(b, sel) if sel[0] == 'head' else eid0
                    if eid is None or eid not in b.W.reps[0].entries:     # the write before took it out
                        cells.append('the entry is gone')
                        break
                    u = b.uid(eid)
                    edit(b, host, 0, u, ['char'] + ['title'] * (k - 1), field)
                    o, r, st = look(b, 0, u)
                    le = letter(o, r)
                else:
                    le = '-'
                ok, nrw, _ = own_write(b, 0, key_carrier, next(names), k=60 + c)
                o, r, st = look(b, 0, u)
                lw = letter(o, r) if ok else 'X'
                where = '-'
                if st.startswith('history'):
                    marks = st.split('versions ')[1]
                    where = str(marks.index('P')) if 'P' in marks else '-'
                    st = 'h%s@%s' % (st.split(',')[0].split()[1], where)
                else:
                    st = 'gone'
                cells.append('%s%s+%d %s' % (le, lw, nrw, st))
                if lw in 'RKxX' or le in 'RKx':
                    break
            print('   %2d edits then a write, %2d cycles: %s [C %s]' % (k, cycles, ' | '.join(cells), C.c_note(b)))


# ------------------------------------------------------------------ reading 1, two gates
TWO_MERGES = [('Synchronize into 0', 0, 1, 'Synchronize'), ('Synchronize into 1', 1, 0, 'Synchronize')]
for _m in ('OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids'):
    TWO_MERGES += [('Import %s into 0' % _m, 0, 1, _m), ('Import %s into 1' % _m, 1, 0, _m)]
TWO_MERGES += [('KeePassXC 2.8.0 into 0', 0, 1, '2.8.0'), ('KeePassXC 2.8.0 into 1', 1, 0, '2.8.0')]


def merges(b, host, u, snap, out, tally):
    """Every merge of TWO_MERGES from the snapshot, then a write of the gate's own at the target."""
    for label, d, s, prog in TWO_MERGES:
        snap.restore()
        b.step(50, C.merge_op(prog, d, s))
        o, r, st = look(b, d, u)
        tally[letter(o, r)] = tally.get(letter(o, r), 0) + 1
        ok, nrw, _ = own_write(b, d, False, 'pj', k=51)
        o2, r2, st2 = look(b, d, u)
        out.append('      %-36s %s [%s] | the gate at %d writes: %s%s, written again %d [%s]' % (
            label, o, st, d, '' if ok else 'cannot write; ', o2, nrw, st2))


def part_two(host):
    print('== Reading 1 (%s), two gates. Each kind\'s entry R edited at copy 0 (a dialog edit of the' % (B.REWRITE or 'none, the control'))
    print('   payload), copy 1 synchronized from copy 0, then:')
    print('   same:    each gate writes (a profile name added), so each writes R again apart')
    print('   later:   gate 0 writes (R written again), then copy 1 edits R again (a later edit)')
    print('   earlier: copy 1 edits R again, then gate 0 writes (R written again, later)')
    print('   Then each merge both ways, and the gate at the target writing once more. "versions" as')
    print('   in the part `one`. Before the merges each copy is read against every decision any gate')
    print('   made, one the other gate made and the copy has not received included; the merges are the')
    print('   cells the tally counts.')
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in C.HIST_BASE))
    tally = {}
    for kind_name, sel, field, base in C.HIST_KINDS[:9]:
        print('## %s' % kind_name)
        for case in ('same', 'later', 'earlier'):
            b = build(host, base, 'r1t')
            u = b.uid(C.hist_pick(b, sel))
            edit(b, host, 0, u, ['char'], field)
            b.step(20, ('merge', 'kp_sync', 1, 0))
            if case == 'same':
                w0 = own_write(b, 0, False, 'ph', k=21)
                w1 = own_write(b, 1, False, 'ph', k=22)
                pre = 'gate 0 wrote again %d, gate 1 wrote again %d' % (w0[1], w1[1])
            elif case == 'later':
                w0 = own_write(b, 0, False, 'ph', k=21)
                edit(b, host, 1, u, ['char'], field)
                pre = 'gate 0 wrote again %d, then copy 1 edited' % w0[1]
            else:
                edit(b, host, 1, u, ['char'], field)
                w0 = own_write(b, 0, False, 'ph', k=21)
                pre = 'copy 1 edited, then gate 0 wrote again %d' % w0[1]
            o0, r0, s0 = look(b, 0, u)
            o1, r1, s1 = look(b, 1, u)
            out = ['   %s: %s; copy 0 %s [%s]; copy 1 %s [%s]' % (case, pre, o0, s0, o1, s1)]
            snap = Snap(b, 't')
            merges(b, host, u, snap, out, tally)
            for x in out:
                print(x)
            for x in C.c_lines(b, '      '):
                print(x)
        # rounds: does history pile up across merges?
        b = build(host, base, 'r1t')
        rounds = []
        eid0 = C.hist_pick(b, sel)
        for n in range(1, 6):
            # the same entry every round; the summary entry the newest, since every write replaces it
            u = b.uid(C.hist_pick(b, sel) if sel[0] == 'head' else eid0)
            edit(b, host, 0, u, ['char'], field)
            b.step(100 + n, ('merge', 'kp_sync', 1, 0))
            w0 = own_write(b, 0, False, 'r%d' % n, k=110 + n)
            w1 = own_write(b, 1, False, 'r%d' % n, k=120 + n)
            b.step(130 + n, ('merge', 'kp_sync', 0, 1))
            b.step(140 + n, ('merge', 'kp_sync', 1, 0))
            o0, r0, s0 = look(b, 0, u)
            o1, r1, s1 = look(b, 1, u)
            rounds.append('round %d: written again %d and %d; copy 0 %s [%s]; copy 1 %s [%s]' % (
                n, w0[1], w1[1], letter(o0, r0), s0, letter(o1, r1), s1))
        print('   five rounds of: R edited at 0, Synchronize into 1, both gates write, Synchronize into 0 and into 1')
        for x in rounds:
            print('      ' + x)
    print('== tally of the merge cells before the gate\'s next write: ' + ', '.join('%s %d' % kv for kv in sorted(tally.items())))


PRUNES = [
    ('an admission record (c1); gate 1 revokes c1', ('rec',), 'client', ('revoke', 1, 'c1')),
    ('the authority; gate 1 recreates it', ('auth',), 'id', ('recreate', 1)),
    ('a profile addition (pg); gate 1 removes pg', ('padd',), 'name', ('prem', 1, 'pg')),
    ('a rule version (N, nothing); gate 1 sets N to read and write', ('ver', 'rule:N', 'new'), 'object', ('rule', 1, 'N', 'rw')),
    ('a renewal answer (c1, forever); gate 1 allows renewals of c1 again', ('ver', 'renew:c1', 'new'), 'object',
     ('renew', 1, 'c1', 'allowed')),
]


def part_prune(host):
    print('== Reading 1 (%s), two gates: copy 0 edits R (a dialog edit of the payload); gate 1, which' % (B.REWRITE or 'none, the control'))
    print('   holds R unedited, makes a decision that takes R out of its copy (with a deletion record);')
    print('   gate 0 writes (a profile name added), writing R again. Both orders: gate 0\'s write after')
    print('   gate 1\'s decision, and before it. Then each merge both ways, and the gate at the target')
    print('   writing once more. Before the merges each copy is read against every decision any gate')
    print('   made, so copy 0, which has not received gate 1\'s decision, can read WIDER there; the')
    print('   merges are the cells the tally counts.')
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in C.HIST_BASE))
    tally = {}
    for what, sel, field, act in PRUNES:
        print('## %s' % what)
        for order in ('the rewrite after gate 1\'s decision', 'the rewrite before gate 1\'s decision'):
            b = build(host, C.HIST_BASE, 'r1p')
            u = b.uid(C.hist_pick(b, sel))
            edit(b, host, 0, u, ['char'], field)
            if order.startswith('the rewrite after'):
                ok1 = b.step(21, act)
                w0 = own_write(b, 0, False, 'ph', k=22)
            else:
                w0 = own_write(b, 0, False, 'ph', k=21)
                b.W.t += 10
                ok1 = b.step(22, act)
            o0, r0, s0 = look(b, 0, u)
            o1, r1, s1 = look(b, 1, u)
            out = ['   %s: gate 1 %s; gate 0 wrote again %d; copy 0 %s [%s]; copy 1 %s [%s]' % (
                order, 'decided' if ok1 else 'could not decide', w0[1], o0, s0, o1, s1)]
            snap = Snap(b, 'p')
            merges(b, host, u, snap, out, tally)
            for x in out:
                print(x)
            for x in C.c_lines(b, '      '):
                print(x)
    print('== tally of the merge cells before the gate\'s next write: ' + ', '.join('%s %d' % kv for kv in sorted(tally.items())))


# ------------------------------------------------------------------ reading 2
COPY_KINDS = [
    ('a revocation (c0)', ('ref', 'revoked')),
    ('a retirement (from Recreate)', ('ref', 'retired')),
    ('a profile removal (pf)', ('prem',)),
    ('a rule version (N, nothing)', ('ver', 'rule:N', 'new')),
    ('a renewal answer (c1, forever)', ('ver', 'renew:c1', 'new')),
    ('an admission record (c1), which never counts from a copy', ('rec',)),
    ('a profile addition (pg), which never counts from a copy', ('padd',)),
    ('the authority, which never counts from a copy', ('auth',)),
    ('the summary entry, which never counts from a copy', ('head', 'new')),
]
WIDEN = {('ver', 'rule:N', 'new'): ('rule', 0, 'N', 'rw'), ('ver', 'renew:c1', 'new'): ('renew', 0, 'c1', 'allowed')}
ROW6 = ('H2', 'H3', 'H5', 'H6', 'H7', 'H8', 'H9', 'H10', 'H12', 'H13')
# ADDED (copies extended): a kind with a rule per profile, on a base of its own, and the histories
# with rules per profile whose import into copy 1 ends wider in the store as decided
PP_BASE = C.HIST_BASE[:-1] + [('rule', 0, 'root', 'pf=rw,pg=rw'), ('rule', 0, 'N', 'pf=r,pg=rw'),
                              ('merge', 'kp_sync', 1, 0)]
PP_KIND = ('a rule version (N, read for pf, read and write for pg, below a root that gives both)',
           ('ver', 'rule:N', 'new'))
PP_WIDEN = ('rule', 0, 'N', 'pf=rw,pg=rw')
ROW6_PP = ('H14', 'H16', 'H17', 'H19', 'H20', 'H21')


def brief(b, i):
    o, r = B.outcome(b, i)
    return '%s [store %s, from copies %d]' % (o, r['status'], r['counts'].get('fromcopy', 0))


def part_copies(host):
    print('== Reading 2 (%s): copies of store entries under another identifier that are not an import.' % B.COPIES)
    print('   Copy 0 holds the base and copy 1 is synchronized from it. Duplicate Entry is KeePass\'s')
    print('   Duplicate with no suffix (the same strings, a new identifier, the time of the copy).')
    print('   KeePass\'s copy and paste is stood in for by a duplicate moved to another group; the')
    print('   window\'s own paste was not run.')
    if B.EXT:       # ADDED (copies extended)
        print('   The wider reading: a copied rule version limits by "%s"%s.' % (
            B.EXT['rules'], ', and the items of a copied summary entry count' if B.EXT['heads'] else ''))
    base = C.HIST_BASE
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in base))
    kinds = [(k, sel, C.HIST_BASE, WIDEN.get(sel)) for k, sel in COPY_KINDS]
    if B.EXT:       # ADDED (copies extended)
        print('   base of the kind with a rule per profile: ' + ' ; '.join(' '.join(map(str, op)) for op in PP_BASE))
        kinds.insert(5, (PP_KIND[0], PP_KIND[1], PP_BASE, PP_WIDEN))
    for kind_name, sel, base, widen in kinds:
        print('## %s' % kind_name)
        rows = []
        # the copy as it sits, in another group, in the recycle bin
        for label, after in (('Duplicate Entry', None), ('Duplicate Entry, moved to the group General', 'move'),
                             ('Duplicate Entry, deleted to the recycle bin', 'bin')):
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            cu = 'cd%030x' % b.ncopy
            if after == 'move':
                host.run('move', b.files[0], cu, 'General', b.W.now())
            elif after == 'bin':
                host.run('bin', b.files[0], cu, b.W.now())
            b.sync_file(0)
            rows.append('%-62s %s' % (label, brief(b, 0)))
        # Delete Duplicate Entries after it
        b = build(host, base, 'r2c')
        b.step(20, ('dupel', 0, sel))
        b.step(21, ('dde', 0))
        rows.append('%-62s %s' % ('Duplicate Entry, then Delete Duplicate Entries', brief(b, 0)))
        for x in b.log:
            if x.startswith('   '):
                rows.append('   ' + x.strip())
        # the original deleted by hand, the copy left
        for label, how in (('Duplicate Entry, the original deleted for good with a record', 'rec'),
                           ('Duplicate Entry, the original deleted for good with no record', 'none')):
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            u = b.uid(C.hist_pick(b, sel))
            if how == 'rec':
                host.run('delrec', b.files[0], u, b.W.now())
            else:
                b.patch(0, ['R\t' + u])
            b.sync_file(0)
            rows.append('%-62s %s' % (label, brief(b, 0)))
        # a copy brought back by KeePassXC beside its deletion record
        # the copy the only carrier: the original deleted with a record, the copy then deleted too
        for prog, label in (('2.8.0', 'KeePassXC 2.8.0'), ('Synchronize', 'Synchronize')):
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            cu = 'cd%030x' % b.ncopy
            host.run('delrec', b.files[0], b.uid(C.hist_pick(b, sel)), b.W.now())
            b.sync_file(0)
            b.step(21, ('merge', 'kp_sync', 1, 0))
            o1 = brief(b, 1)
            host.run('delrec', b.files[0], cu, b.W.now())
            b.sync_file(0)
            o0 = brief(b, 0)
            b.step(22, C.merge_op(prog, 0, 1))
            d = b.dump(0)
            where = 'the copy back beside its record' if cu in d['entries'] and cu in d['dels'] else (
                'the copy present' if cu in d['entries'] else 'the copy gone')
            rows.append('%-62s copy 1 %s; copy 0 %s' % (
                'Duplicate Entry, the original deleted with a record, Synchronize into 1, the copy deleted at 0', o1, o0))
            rows.append('   %-59s %s' % ('then %s into 0 (%s)' % (label, where), brief(b, 0)))
        # a copy whose original the plugin later replaced
        if widen:
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            b.step(21, ('merge', 'kp_sync', 1, 0))
            b.step(22, widen)
            rows.append('%-62s %s' % ('Duplicate Entry, Synchronize into 1, then %s at 0' % ' '.join(map(str, widen)),
                                      brief(b, 0)))
            snap = Snap(b, 'c')
            for prog in ('Synchronize', 'CreateNewUuids', '2.8.0'):
                for d, s in ((1, 0), (0, 1)):
                    snap.restore()
                    b.step(23, C.merge_op(prog, d, s))
                    rows.append('   %-59s %s' % ('then %s into %d' % (prog if prog != '2.8.0' else 'KeePassXC 2.8.0', d),
                                                 brief(b, d)))
        if B.EXT:       # ADDED (copies extended): the gate's own write beside a copy
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            n0 = len(b.dump(0)['entries'])
            ok = b.step(21, ('padd', 0, 'ph'))
            d = b.dump(0)
            rows.append('%-62s %s; the copy %s; entries %d, then %d' % (
                'Duplicate Entry, then the gate writes (a profile name added)', brief(b, 0),
                'present' if ('cd%030x' % b.ncopy) in d['entries'] else 'gone', n0, len(d['entries'])))
        for x in rows:
            print('   ' + x)
    if B.EXT:       # ADDED (copies extended)
        replaced(host)
        combine_never(host)
        print('== The control: what the rule above forbids.')
    # the reading of copies combined with writing such a record again under its own identifier
    print('== Combined with writing again: the histories whose import with Create new IDs into the copy')
    print('   that lacks the decisions ends wider in the store as decided (row 6). After the import: the')
    print('   gate\'s write (a profile name added; refused where the reading stops the store); then each')
    print('   record counted only from a copy written under the identifier its content gives, where the')
    print('   file holds neither an entry nor a deletion record under it, and a summary entry restating')
    print('   them (the model\'s Seal again stands for the summary entry such a write carries). Then')
    print('   Synchronize with copy 0 both ways.')
    for hid, what, hist in C.HISTORIES + (C.PROFILE_HISTORIES if B.EXT else []):
        if hid not in ROW6 + (ROW6_PP if B.EXT else ()):     # ADDED (copies extended): ROW6_PP
            continue
        b = C.run_cell(host, hist, 'CreateNewUuids', 1, 0, 'r2x')
        rows = ['after the import: %s' % brief(b, 1)]
        ok0 = b.step(30, ('padd', 1, 'ph'))
        rows.append('the gate writes: %s; %s' % ('writes' if ok0 else 'cannot write (the store is stopped)', brief(b, 1)))
        rows += b.recanon(1)
        ok = b.step(31, ('reseal', 1))
        rows.append('a summary entry restating them (the model\'s Seal again): %s; %s' % (
            'written' if ok else 'not written', brief(b, 1)))
        snap = Snap(b, 'x')
        for d, s in ((0, 1), (1, 0)):
            snap.restore()
            b.step(40, ('merge', 'kp_sync', d, s))
            rows.append('then Synchronize into %d: %s' % (d, brief(b, d)))
        print('## %s: %s' % (hid, what))
        for x in rows:
            print('   ' + x.strip())


# ------------------------------------------------------------------ ADDED (copies extended)
class Toggle:
    """read.py's `ext` with some switches set otherwise, for the reads inside the block."""

    def __init__(self, **kw):
        self.kw = kw

    def __enter__(self):
        self.old = dict(B.EXT)
        B.EXT.update(self.kw)

    def __exit__(self, *a):
        B.EXT.clear()
        B.EXT.update(self.old)


def copy_ids(b, i):
    """The entries of file i that hold a record's strings under another identifier."""
    ents = b.real(i)[0]
    return sorted(u[2:] for u in ents if u.startswith('X:'))


def replaced(host):
    print('== A replaced narrowing, and what clears a limit that a copy sets. Copy 0 holds the root')
    print('   giving read and write to both profiles. Each row: copy 1 against every decision.')
    wide = ('rule', 0, 'root', 'pf=rw,pg=rw')
    imp = ('merge', 'cni', 1, 0)
    sync = ('merge', 'kp_sync', 1, 0)
    rows = []
    # the narrowing arrives as a copy, then its widening arrives as a copy
    b = build(host, [wide, sync, ('rule', 0, 'N', 'pf=r,pg=r'), imp], 'r2r')
    rows.append('N narrowed at 0, imported into 1 with Create new IDs: %s' % brief(b, 1))
    b.step(10, ('rule', 0, 'N', 'pf=rw,pg=rw'))
    b.step(11, imp)
    rows.append('   N widened again at 0, imported into 1 again: %s' % brief(b, 1))
    with Toggle(among=False):
        rows.append('   the same file, a copy that another copy names as replaced still limiting: %s' % brief(b, 1))
    b.step(12, sync)
    rows.append('   then Synchronize into 1: %s' % brief(b, 1))
    # a narrowing made apart arrives as a copy; then it is replaced where it was made
    for names in (True, False):
        with Toggle(names=names):
            b = build(host, [wide, sync, ('rule', 0, 'N', 'pf=r,pg=r'), ('rule', 1, 'N', 'pf=rw,pg=rw'), imp], 'r2r')
            rows.append('N narrowed at 0 and widened at 1, apart; 0 imported into 1 (%s): %s' % (
                'a version the gate writes names the copies it read' if names else 'a version the gate writes names no copy',
                brief(b, 1)))
            b.step(10, ('rule', 0, 'N', 'pf=rw,pg=rw'))
            rows.append('   N widened at 0, which copy 1 has not received: %s' % brief(b, 1))
            snap = Snap(b, 'r')
            ok = b.step(11, ('padd', 1, 'ph'))
            rows.append('   the gate at 1 writes (a profile name added)%s: %s' % ('' if ok else ', refused', brief(b, 1)))
            snap.restore()
            ok = b.step(11, ('rule', 1, 'N', 'pf=rw,pg=rw'))
            rows.append('   the rule of N set again at 1%s: %s' % ('' if ok else ', refused', brief(b, 1)))
            snap.restore()
            n = b.seal_copies(1, 11)
            rows.append('   Seal again at 1 (acknowledging %d): %s' % (n, brief(b, 1)))
            ok = b.step(12, ('rule', 1, 'N', 'pf=rw,pg=rw'))
            rows.append('      then the rule of N set again at 1%s: %s' % ('' if ok else ', refused', brief(b, 1)))
            snap.restore()
            b.step(11, imp)
            rows.append('   0 imported into 1 again: %s' % brief(b, 1))
            snap.restore()
            b.step(11, sync)
            rows.append('   Synchronize into 1: %s' % brief(b, 1))
            snap.restore()
            for u in copy_ids(b, 1):
                host.run('delrec', b.files[1], u, b.W.now())
            b.sync_file(1)
            rows.append('   the copies deleted by hand at 1: %s' % brief(b, 1))
    for x in rows:
        print('   ' + x)


def combine_never(host):
    print('== Combined with the decided rewrite, the plugin writing nothing from a copy: the histories')
    print('   whose import with Create new IDs into copy 1 ends wider in the store as decided. After the')
    print('   import: Seal again where the reading stops the store (a summary entry over the file\'s own')
    print('   records that names the copies it acknowledges); the authority\'s entry edited in the entry')
    print('   dialog at copy 1, so that it counts by its history; the gate\'s write (a profile name')
    print('   added), which writes again what counts only by its history and nothing that counts only')
    print('   from a copy. Then, each from that state: Synchronize both ways; and every copy deleted by hand.')
    for hid, what, hist in C.HISTORIES + C.PROFILE_HISTORIES:
        if hid not in ROW6 + ROW6_PP:
            continue
        b = C.run_cell(host, hist, 'CreateNewUuids', 1, 0, 'r2n')
        rows = ['after the import: %s' % brief(b, 1)]
        if B.rd(b.dump(1), True)['status'] != 'ok':
            n = b.seal_copies(1, 29)
            rows.append('Seal again, acknowledging %d copies: %s' % (n, brief(b, 1)))
        u = b.uid(b.W.pick(b.W.reps[1], ('auth',)))
        edit(b, host, 1, u, ['char'], 'id')
        r = B.rd(b.dump(1), True)
        rows.append('the authority\'s entry edited at 1: %s; counts by history %d, keys held %d' % (
            brief(b, 1), r['counts']['byhistory'], r['counts']['keys']))
        B.REWRITE = 'backup'
        n0 = len(b.rewrites)
        ok = b.step(30, ('padd', 1, 'ph'))
        B.REWRITE = None
        d = b.dump(1)
        r = B.rd(d, True)
        rows.append('the gate writes: %s, written again %d; %s; keys held %d, the authority\'s entry %s, copies %d' % (
            'writes' if ok else 'cannot write', len(b.rewrites) - n0, brief(b, 1), r['counts']['keys'],
            'present' if u in d['entries'] else 'GONE', len(copy_ids(b, 1))))
        snap = Snap(b, 'n')
        for d_, s_ in ((0, 1), (1, 0)):
            snap.restore()
            b.step(40, ('merge', 'kp_sync', d_, s_))
            rows.append('then Synchronize into %d: %s' % (d_, brief(b, d_)))
        snap.restore()
        for c in copy_ids(b, 1):
            host.run('delrec', b.files[1], c, b.W.now())
        b.sync_file(1)
        rows.append('then every copy deleted by hand at 1: %s' % brief(b, 1))
        print('## %s: %s' % (hid, what))
        for x in rows:
            print('   ' + x.strip())


def twice(b, i, label, **kw):
    """File i read with the switches of `kw` off and on."""
    out = []
    for on in (False, True):
        with Toggle(**{k: on for k in kw}):
            out.append('   %-58s %s' % ('%s, %s' % (label, 'counted' if on else 'not counted'), brief(b, i)))
    return out


def part_kinds(host):
    print('== Wider reading (%s%s, %s): three kinds read from a copy, each switched off and on over the' % (
        B.EXT['rules'], ' with the items of a copied summary entry' if B.EXT['heads'] else '', B.COPIES))
    print('   same file. An answer that gives more than the file\'s own records give is marked.')
    print('## a retirement that settles which authority is served')
    print('   Both gates recreate the authority apart, so two stand in both copies and nothing is served.')
    print('   Gate 0 keeps its own and pairs a client; copy 2 is taken then; gate 0 recreates again.')
    ops = [('recreate', 0), ('pair', 0), ('recreate', 1), ('merge', 'kp_sync', 0, 1), ('merge', 'kp_sync', 1, 0),
           ('keep', 0), ('merge', 'kp_sync', 2, 0), ('recreate', 0)]
    print('   history: ' + ' ; '.join(' '.join(map(str, op)) for op in ops))
    for src, what in ((2, 'copy 2, taken after the keeping'), (0, 'copy 0, the newest')):
        b = C.run_ops(host, 3, 'k1', ops + [('merge', 'cni', 1, src)])
        print('   %s, imported into copy 1 with Create new IDs:' % what)
        for x in twice(b, 1, 'the retirement settling', settle=True):
            print('   ' + x)
        for x in b.log:
            if 'does not apply' in x:
                print('      ' + x.strip())
    print('## a summary entry\'s items')
    print('   At copy 0 a record that takes access away is deleted by hand with no deletion record, so')
    print('   that only the summary entry restates it; then copy 1, which never held it, imports copy 0')
    print('   with Create new IDs.')
    for what, ops in (
            ('a revocation (c0)', [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0)]),
            ('a rule of nothing at N', [('rule', 0, 'N', 'none'), ('del', 0, ('ver', 'rule:N', 'new'), 0)]),
            ('a rule at N for one profile, below a wider root',
             [('rule', 0, 'root', 'pf=rw,pg=rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'pf=r'),
              ('del', 0, ('ver', 'rule:N', 'new'), 0)]),
            ('a profile removal (pf)', [('prem', 0, 'pf'), ('del', 0, ('prem',), 0)])):
        b = C.run_ops(host, 2, 'k2', ops + [('merge', 'cni', 1, 0)])
        print('   %s: copy 0 %s' % (what, brief(b, 0)))
        for x in twice(b, 1, 'copy 1, the items of a copied summary entry', heads=True):
            print('   ' + x)
    print('   An older summary entry copied (Duplicate Entry), then a later decision that widens, at copy 0:')
    for what, ops in (
            ('a rule of nothing at N, then read and write', [('rule', 0, 'N', 'none'), ('dupel', 0, ('head', 'new')), ('rule', 0, 'N', 'rw')]),
            ('renewals refused forever, then allowed', [('renew', 0, 'c0', 'forever'), ('dupel', 0, ('head', 'new')), ('renew', 0, 'c0', 'allowed')]),
            ('a name removed, then added again', [('prem', 0, 'pf'), ('dupel', 0, ('head', 'new')), ('padd', 0, 'pf')])):
        b = C.run_ops(host, 2, 'k2', ops)
        for x in twice(b, 0, what, heads=True):
            print('   ' + x)
    print('## a store key taken from a copy')
    print('   The authority\'s entry is duplicated, the original deleted for good with a record, and')
    print('   the store started again with Create and a name added. No rule is written under the new key.')
    ops = [('padd', 0, 'pg'), ('rule', 0, 'N', 'rw'), ('dupel', 0, ('auth',)), ('del', 0, ('auth',), 1)]
    b = C.run_ops(host, 2, 'k3', ops)
    for x in twice(b, 0, 'after the deletion, the key from the copy', key=True):
        print('   ' + x)
    for k, op in enumerate([('create', 0), ('padd', 0, 'pf')]):
        if not b.step(10 + k, op):
            print('      %s (does not apply)' % ' '.join(map(str, op)))
    for x in twice(b, 0, 'after the store is started again, the key from the copy', key=True):
        print('   ' + x)
    r0 = B.RD.read(b.dump(0), history=True, copies=B.COPIES, ext=dict(B.EXT, key=False))
    r1 = B.RD.read(b.dump(0), history=True, copies=B.COPIES, ext=dict(B.EXT, key=True))
    for label, r in (('not counted', r0), ('counted', r1)):
        print('      %s: keys held %d, served %s, names %s, the root gives %s, N gives %s' % (
            label, r['counts']['keys'], r['served'], r['names'], C.words_text(r['words'][b.ids['root']]) or 'nothing',
            C.words_text(r['words'][b.ids['N']]) or 'nothing'))


def main():
    rewriting = sys.argv[1] == '--rewrite'
    if rewriting:
        B.rewrite(sys.argv[2])
    elif sys.argv[1] == '--copies':
        B.copies(sys.argv[2])
    elif sys.argv[1] == '--reading':    # ADDED (copies extended)
        B.extended(sys.argv[2], sys.argv[3])
        del sys.argv[1]
    else:
        raise SystemExit('--rewrite FORM, --copies VARIANT or --reading LEVEL VARIANT first')
    part = sys.argv[3]
    sys.stdout.reconfigure(line_buffering=True)
    host = kp.Host()
    t0 = time.time()
    try:
        if part == 'one' and rewriting:
            part_one(host)
        elif part == 'alternate' and rewriting:
            part_alternate(host)
        elif part == 'two' and rewriting:
            part_two(host)
        elif part == 'prune' and rewriting:
            part_prune(host)
        elif part == 'copies' and B.COPIES:
            part_copies(host)
        elif part == 'kinds' and B.EXT:     # ADDED (copies extended)
            part_kinds(host)
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    if part != 'kinds':
        for x in B.moved():     # ADDED (copies extended)
            print(x)
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
