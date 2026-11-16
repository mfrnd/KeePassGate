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


def brief(b, i):
    o, r = B.outcome(b, i)
    return '%s [store %s, from copies %d]' % (o, r['status'], r['counts'].get('fromcopy', 0))


def part_copies(host):
    print('== Reading 2 (%s): copies of store entries under another identifier that are not an import.' % B.COPIES)
    print('   Copy 0 holds the base and copy 1 is synchronized from it. Duplicate Entry is KeePass\'s')
    print('   Duplicate with no suffix (the same strings, a new identifier, the time of the copy).')
    print('   KeePass\'s copy and paste is stood in for by a duplicate moved to another group; the')
    print('   window\'s own paste was not run.')
    base = C.HIST_BASE
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in base))
    for kind_name, sel in COPY_KINDS:
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
        if sel in WIDEN:
            b = build(host, base, 'r2c')
            b.step(20, ('dupel', 0, sel))
            b.step(21, ('merge', 'kp_sync', 1, 0))
            b.step(22, WIDEN[sel])
            rows.append('%-62s %s' % ('Duplicate Entry, Synchronize into 1, then %s at 0' % ' '.join(map(str, WIDEN[sel])),
                                      brief(b, 0)))
            snap = Snap(b, 'c')
            for prog in ('Synchronize', 'CreateNewUuids', '2.8.0'):
                for d, s in ((1, 0), (0, 1)):
                    snap.restore()
                    b.step(23, C.merge_op(prog, d, s))
                    rows.append('   %-59s %s' % ('then %s into %d' % (prog if prog != '2.8.0' else 'KeePassXC 2.8.0', d),
                                                 brief(b, d)))
        for x in rows:
            print('   ' + x)
    # the reading of copies combined with writing such a record again under its own identifier
    print('== Combined with writing again: the histories whose import with Create new IDs into the copy')
    print('   that lacks the decisions ends wider in the store as decided (row 6). After the import: the')
    print('   gate\'s write (a profile name added; refused where the reading stops the store); then each')
    print('   record counted only from a copy written under the identifier its content gives, where the')
    print('   file holds neither an entry nor a deletion record under it, and a summary entry restating')
    print('   them (the model\'s Seal again stands for the summary entry such a write carries). Then')
    print('   Synchronize with copy 0 both ways.')
    for hid, what, hist in C.HISTORIES:
        if hid not in ROW6:
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


def main():
    rewriting = sys.argv[1] == '--rewrite'
    if rewriting:
        B.rewrite(sys.argv[2])
    elif sys.argv[1] == '--copies':
        B.copies(sys.argv[2])
    else:
        raise SystemExit('--rewrite FORM or --copies VARIANT first')
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
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
