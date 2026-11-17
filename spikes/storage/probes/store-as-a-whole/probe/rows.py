"""ADDED (rules per profile): a rule stored as a record for each node and profile, on real files.

Run:  uv run --no-project python -I -X utf8 rows.py PART

Every part plays its sequences through bridge.Bridge under bridge.rows(): every gate act is the
model's write path with crdt_model.ROWS set, written into a real .kdbx by KeePassLib 2.62; every
merge is KeePassLib's or keepassxc-cli's. A row is a profile's rule at a node, written `N/pf`; the
switch is the node's "Refuse any profile not named here". A row is read as one of: `r`, `rw` and so
on (a rule that grants), `empty` (an empty rule, which grants nothing from there down), `off` (the
rule taken off), or no version at all (never made). Parts:

  apart     two copies change different profiles' rows at the same node, then every merge both
            ways, in the whole-rule form and per profile: held to the two changes together, and the
            rows left to settle
  same      the same profile's row changed apart in two and in three copies; then a single version
            removed, in each of three forms of what the removal writes, with further merges
  states    an empty rule, a rule taken off and a row never made, through every merge, an edit by
            hand, a deletion by hand with and without a deletion record, and the history rule
  switch    the switch as a record of its own: on and off apart, beside a row changed apart, the
            sequence S34 of STORE.md under each way a removed row is written, and the sequences
            next to it
  dels      what a deletion record of a row's version tells, in the identifier's form run (`pair`)
            and in the form `split`, and what telling it costs the reading
  growth    the entries and summary items the same rule edits make in both forms, from the strings
            store.py writes

Each cell's file is read by read.py. Where the part holds a file to the decisions any gate made, it
names the oracle's class as cases.py does; where it holds a merged file to what the two copies gave
just before the merge, it names the letters the merged file gives that neither copy gave.
"""
import hashlib
import hmac
import os
import statistics
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import read as RD  # noqa: E402
import store as S  # noqa: E402
import crdt_model as M  # noqa: E402

SYNC = ('merge', 'kp_sync', 1, 0)
BACK = ('merge', 'kp_sync', 0, 1)
ROOT_RW = ('rule', 0, 'root', 'rw')


def seq(ops):
    return ' ; '.join(' '.join(map(str, op)) for op in ops)


def names(b):
    return {v: k for k, v in b.ids.items()}


def rows_of(b, r):
    """The standing versions of every row and switch, by node name: 'N/pf row:r'."""
    inv = names(b)
    out = []
    for o, docs in sorted(r.get('standing', {}).items()):
        kind, rest = o.split(':', 1)
        node, sep, p = rest.partition('/')
        label = inv.get(node, node[:8]) + sep + p if kind == 'rule' else inv.get(node, node[:8]) + ' switch'
        out.append('%s %s' % (label, ','.join(docs)))
    for o in r.get('lost', []):
        if o.startswith('rule:') or o.startswith('switch:'):
            kind, rest = o.split(':', 1)
            node, sep, p = rest.partition('/')
            out.append('%s%s lost' % (inv.get(node, node[:8]), sep + p if p else ' switch'))
    return '; '.join(out)


def gives(r):
    """What the rules give at the root and at N, each profile, as text."""
    m = r['words_raw']
    return 'root %s, N %s' % (M.words_text(m.get('root', frozenset())) or 'nothing',
                              M.words_text(m.get('N', frozenset())) or 'nothing')


def state(b, r, row='N/pf'):
    """The state of a row: E an empty rule, O the rule taken off, X no version, or the versions."""
    inv = names(b)
    node, p = row.split('/')
    o = 'rule:%s/%s' % (b.ids[node], p)
    if o in r.get('lost', []):
        return 'refuses (a version lost)'
    docs = r.get('standing', {}).get(o, [])
    if docs == ['row:']:
        return 'E'
    if docs == ['off:']:
        return 'O'
    if not docs:
        return 'X'
    del inv
    return '+'.join(docs)


def read_model(b, i):
    return b.as_model(B.rd(b.dump(i), B.DECIDED))


def pf_at(r, node='N', p='pf'):
    return frozenset(x for x in r['words_raw'].get(node, frozenset()) if x.startswith(p + '.'))


def wt(w):
    return ''.join(sorted(x.split('.')[1] for x in w)) or 'nothing'


def play(host, ops, nrep, tag):
    return C.run_ops(host, nrep, tag, ops)


# ------------------------------------------------------------------ apart (item 2)
# Each case: the base, copy 0's change, copy 1's change, and what N gives each profile when both
# changes hold (the root gives read and write throughout).
APART = [
    ('A1', 'pf narrowed at copy 0, pg narrowed at copy 1',
     [ROOT_RW, ('rule', 0, 'N', 'pf=rw,pg=rw'), SYNC], [('rule', 0, 'N', 'pf=r,pg=rw')], [('rule', 1, 'N', 'pf=rw,pg=r')],
     {'pf': 'r', 'pg': 'r'}),
    ('A2', 'pf widened at copy 0, pg widened at copy 1',
     [ROOT_RW, ('rule', 0, 'N', 'pf=r,pg=r'), SYNC], [('rule', 0, 'N', 'pf=rw,pg=r')], [('rule', 1, 'N', 'pf=r,pg=rw')],
     {'pf': 'rw', 'pg': 'rw'}),
    ('A3', 'pf widened at copy 0, pg narrowed at copy 1',
     [ROOT_RW, ('rule', 0, 'N', 'pf=r,pg=rw'), SYNC], [('rule', 0, 'N', 'pf=rw,pg=rw')], [('rule', 1, 'N', 'pf=r,pg=r')],
     {'pf': 'rw', 'pg': 'r'}),
    ('A4', 'pf taken off at copy 0, pg widened at copy 1',
     [ROOT_RW, ('rule', 0, 'N', 'pf=r,pg=r'), SYNC], [('rule', 0, 'N', 'pg=r')], [('rule', 1, 'N', 'pf=r,pg=rw')],
     {'pf': 'rw', 'pg': 'rw'}),
    ('A5', 'pf emptied at copy 0, pg taken off at copy 1',
     [ROOT_RW, ('rule', 0, 'N', 'pf=r,pg=r'), SYNC], [('rule', 0, 'N', 'pf=,pg=r')], [('rule', 1, 'N', 'pf=r')],
     {'pf': '', 'pg': 'rw'}),
    ('A6', 'pf widened at copy 0, a row for pg made at copy 1 where none was',
     [ROOT_RW, ('rule', 0, 'N', 'pf=r'), SYNC], [('rule', 0, 'N', 'pf=rw')], [('rule', 1, 'N', 'pf=r,pg=w')],
     {'pf': 'rw', 'pg': 'w'}),
    ('A7', 'under a refusing switch: pf widened at copy 0, pg removed at copy 1 (per profile only)',
     [ROOT_RW, ('switch', 0, 'N', 'deny'), ('rule', 0, 'N', 'pf=r,pg=r'), SYNC], [('rule', 0, 'N', 'pf=rw,pg=r')],
     [('rule', 1, 'N', 'pf=r')], {'pf': 'rw', 'pg': ''}),
]


def intended(n_words):
    """Both changes together: the root read and write for every profile, N as the case states."""
    return {'root': frozenset(p + '.' + w for p in M.PROFS for w in 'rw'),
            'N': frozenset(p + '.' + w for p, ws in n_words.items() for w in ws)}


def against(r, want):
    got = {n: r['words_raw'].get(n, frozenset()) for n in want}
    if got == want:
        return 'as decided'
    more = {n: got[n] - want[n] for n in want if got[n] - want[n]}
    if more:
        return 'WIDER: ' + ', '.join('%s gives %s' % (n, M.words_text(w)) for n, w in sorted(more.items()))
    less = {n: want[n] - got[n] for n in want if want[n] - got[n]}
    return 'narrower: ' + ', '.join('%s lacks %s' % (n, M.words_text(w)) for n, w in sorted(less.items()))


def to_settle(b, r, form):
    """Rows (per profile) or node rules (whole) at N that stand in more than a single version."""
    n = b.ids['N']
    st = r.get('standing', {})
    if form == 'whole':
        k = len(st.get('rule:' + n, []))
        return 'the node\'s rule in %d versions' % k if k > 1 else 'nothing to settle'
    many = ['%s in %d versions' % (o.split('/')[1], len(d)) for o, d in sorted(st.items())
            if o.startswith('rule:' + n + '/') and len(d) > 1]
    if len(st.get('switch:' + n, [])) > 1:
        many.append('the switch in %d versions' % len(st['switch:' + n]))
    return ', '.join(many) or 'nothing to settle'


def part_apart(host):
    print('== Item 2: two copies change different profiles\' rows at one node, apart; then every merge')
    print('   both ways. Each cell: the merged file held to both changes together (as decided, narrower,')
    print('   WIDER), what stands in more than one version, and the oracle\'s class (every decision any')
    print('   gate made, read by the same form).')
    tally = {}
    for hid, what, base, c0, c1, nw in APART:
        want = intended(nw)
        print('## %s: %s' % (hid, what))
        print('   base: %s; copy 0: %s; copy 1: %s' % (seq(base), seq(c0), seq(c1)))
        print('   both changes together: %s' % 'root %s, N %s' % (M.words_text(want['root']), M.words_text(want['N'])))
        for form in ('whole', 'rows'):
            if form == 'whole' and any(op[0] == 'switch' for op in base):
                continue
            B.use(form)
            for label, prog in C.PROGRAMS:
                cells = []
                for d, s in ((1, 0), (0, 1)):
                    b = play(host, base + c0 + c1 + [C.merge_op(prog, d, s)], 2, 'ap' + form[0])
                    o, r = B.outcome(b, d)
                    a = against(r, want)
                    cells.append('into %d: %s; %s; oracle %s [%s]' % (d, a, to_settle(b, r, form), B.cat(o), C.c_note(b)))
                    key = (form + (' import' if prog == 'CreateNewUuids' else ''), a.split(':')[0],
                           to_settle(b, r, form) == 'nothing to settle')
                    tally[key] = tally.get(key, 0) + 1
                    cells += C.c_lines(b, '      ')
                print('   %-5s %-22s %s' % (form, label, cells[0]))
                for x in cells[1:]:
                    print('   %-5s %-22s %s' % ('', '', x))
    B.use('rows')
    print('== tally (form, against both changes, nothing left to settle): ' + ', '.join(
        '%s %s %s %d' % (f, a, 'settled' if s else 'to settle', n) for (f, a, s), n in sorted(tally.items())))


# ------------------------------------------------------------------ same (item 3)
FORMS = ('again', 'void', 'limit')


def standing_pf(b, r, row='N/pf'):
    node, p = row.split('/')
    return r.get('standing', {}).get('rule:%s/%s' % (b.ids[node], p), [])


def cell_text(b, i, before=None):
    o, r = B.outcome(b, i)
    t = 'N/pf %s, gives pf %s; %s' % (','.join(standing_pf(b, r)) or 'no version', wt(pf_at(r)), B.cat(o))
    return t, r


def part_same(host):
    print('== Item 3: the same profile\'s row changed apart. Each cell: the versions of N/pf that stand in')
    print('   the merged file, what N gives pf, and the oracle\'s class.')
    base2 = [ROOT_RW, ('row', 0, 'N', 'pf', ''), SYNC]
    two = base2 + [('row', 0, 'N', 'pf', 'r'), ('row', 1, 'N', 'pf', 'rw')]
    print('## two copies: base %s; copy 0 sets pf r, copy 1 sets pf rw, apart' % seq(base2))
    for label, prog in C.PROGRAMS:
        cells = []
        for d, s in ((1, 0), (0, 1)):
            b = play(host, two + [C.merge_op(prog, d, s)], 2, 'sm2')
            t, r = cell_text(b, d)
            cells.append('into %d: %s [%s]' % (d, t, C.c_note(b)))
        print('   %-22s %s' % (label, ' | '.join(cells)))
    for sw in ('inherit', 'deny'):
        base3 = [ROOT_RW] + ([('switch', 0, 'N', 'deny')] if sw == 'deny' else []) + \
            [('row', 0, 'N', 'pf', ''), SYNC, ('merge', 'kp_sync', 2, 0)]
        three = base3 + [('row', 0, 'N', 'pf', 'r'), ('row', 1, 'N', 'pf', 'rw'), ('row', 2, 'N', 'pf', 'off'), BACK]
        print('## three copies, the switch %s: base %s; copies 0, 1, 2 set pf r, rw, and take it off, apart;'
              ' Synchronize 0 from 1; then 0 from 2 by each program' % (sw, seq(base3)))
        for label, prog in C.PROGRAMS:
            b = C.run_ops(host, 3, 'sm3', three + [C.merge_op(prog, 0, 2)])
            t, r = cell_text(b, 0)
            print('   %-22s into 0: %s [%s]' % (label, t, C.c_note(b)))
    # the removal of a single version, in three forms
    both = two + [BACK, SYNC]
    narrow = [('rule', 0, 'root', 'r'), ('row', 0, 'N', 'pf', 'r'), SYNC, ('row', 0, 'N', 'pf', ''),
              ('row', 1, 'N', 'pf', 'w'), BACK, SYNC]
    three_all = [ROOT_RW, ('row', 0, 'N', 'pf', ''), SYNC, ('merge', 'kp_sync', 2, 0), ('row', 0, 'N', 'pf', 'r'),
                 ('row', 1, 'N', 'pf', 'rw'), ('row', 2, 'N', 'pf', 'w'), BACK, ('merge', 'kp_sync', 0, 2), SYNC,
                 ('merge', 'kp_sync', 2, 0)]
    seqs = [
        ('D1', 'both copies hold r and rw; copy 0 removes r; copy 1 still holds both', both,
         [('drop', 0, 'N', 'pf', 0, None)], 2, True),
        ('D2', 'both copies hold r and rw; copy 0 removes r; copy 1 sets pf w, apart', both,
         [('drop', 0, 'N', 'pf', 0, None), ('row', 1, 'N', 'pf', 'w')], 2, True),
        ('D3', 'the root reads; both copies hold empty and w; copy 0 removes w, copy 1 removes empty, apart', narrow,
         [('drop', 0, 'N', 'pf', 1, None), ('drop', 1, 'N', 'pf', 0, None)], 2, True),
        ('D4', 'three copies hold r, rw and w; copy 0 removes r; copy 1 still holds the three', three_all,
         [('drop', 0, 'N', 'pf', 0, None)], 3, True),
        ('D5', 'three copies hold r, rw and w; copy 0 removes r, copy 1 removes w, apart', three_all,
         [('drop', 0, 'N', 'pf', 0, None), ('drop', 1, 'N', 'pf', 2, None)], 3, True),
    ]
    tally = {}
    for sid, what, pre, acts, nrep, every in seqs:
        print('## %s: %s' % (sid, what))
        print('   before: %s' % seq(pre))
        for form in FORMS:
            ops = pre + [op[:5] + (form,) if op[0] == 'drop' else op for op in acts]
            b0 = C.run_ops(host, nrep, 'smd', ops)
            r0 = [read_model(b0, i) for i in (0, 1)]
            print('   form %s: %s' % (form, seq([op for op in ops[len(pre):]])))
            print('      copy 0 before the merge: N/pf %s, gives pf %s; copy 1: N/pf %s, gives pf %s' % (
                ','.join(standing_pf(b0, r0[0])) or 'no version', wt(pf_at(r0[0])),
                ','.join(standing_pf(b0, r0[1])) or 'no version', wt(pf_at(r0[1]))))
            for label, prog in C.PROGRAMS:
                cells = []
                for d, s in ((1, 0), (0, 1)):
                    b = C.run_ops(host, nrep, 'smd', ops)
                    pre_w = (C.answer_words(b, d), C.answer_words(b, s))
                    b.step(len(ops), C.merge_op(prog, d, s))
                    t, r = cell_text(b, d)
                    w = C.beyond(pre_w[0], pre_w[1], C.answer_words(b, d))
                    if w:
                        t += '; BEYOND BOTH COPIES: ' + w
                    key = (sid, form, 'beyond both' if w else 'within')
                    tally[key] = tally.get(key, 0) + 1
                    cells.append('into %d: %s [%s]' % (d, t, C.c_note(b)))
                    cells += C.c_lines(b, '      ')
                print('      %-22s %s' % (label, cells[0]))
                for x in cells[1:]:
                    print('      %-22s %s' % ('', x))
            # both directions by Synchronize, then each copy: do they converge
            b = C.run_ops(host, nrep, 'smd', ops + [BACK, SYNC])
            ends = [cell_text(b, i)[0] for i in range(2)]
            print('      Synchronize 0 from 1, then 1 from 0: copy 0 %s | copy 1 %s' % (ends[0], ends[1]))
    print('== tally of the merge cells (sequence, form, against both copies just before): ' + ', '.join(
        '%s %s %s %d' % (a, f, w, n) for (a, f, w), n in sorted(tally.items())))


# ------------------------------------------------------------------ states (item 4)
STATE_OPS = [
    ('none', 'nothing more', None),
    ('dialog', 'the version\'s text changed in the entry dialog', ['char']),
    ('edits11', 'the dialog edit and 10 more dialog edits of the title', ['char'] + ['title'] * 10),
    ('delete', 'deleted for good, with a deletion record', 'delrec'),
    ('unrecorded', 'deleted for good, no deletion record', 'norec'),
    ('bin', 'deleted to the recycle bin', 'bin'),
    ('lost', 'deleted for good with a deletion record, the summary entry too, then Seal again', 'lost'),
]
STATE_MERGES = [('Synchronize into 1', 1, 0, 'Synchronize'), ('Synchronize into 0', 0, 1, 'Synchronize'),
                ('KeePassXC 2.8.0 into 1', 1, 0, '2.8.0'), ('KeePassXC 2.8.0 into 0', 0, 1, '2.8.0')]


def part_states(host):
    print('== Item 4: an empty rule (E), a rule taken off (O) and a row never made (X), at N for pf, under')
    print('   a root that grants read and write and a switch that inherits. Each cell: the state of N/pf')
    print('   as read, what N gives pf, and the oracle\'s class. Expected: E gives nothing, O and X give')
    print('   what arrives (rw); O and X are told apart by the version O holds.')
    base = [ROOT_RW, ('row', 0, 'N', 'pg', 'r'), ('row', 0, 'N', 'pf', 'r'), SYNC]
    states = [('E', base + [('row', 0, 'N', 'pf', '')]), ('O', base + [('row', 0, 'N', 'pf', 'off')]),
              ('X', [ROOT_RW, ('row', 0, 'N', 'pg', 'r'), SYNC, ('row', 0, 'N', 'pg', 'rw')])]
    print('   base: %s' % seq(base))
    tally = {}
    for st, ops in states:
        print('## %s at copy 0: %s' % (st, seq(ops[len(base):] if st != 'X' else ops)))
        print('   every merge, copy 1 holding the earlier row:')
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                b = play(host, ops + [C.merge_op(prog, d, s)], 2, 'st')
                o, r = B.outcome(b, d)
                got = state(b, r)
                cells.append('into %d: %s, gives pf %s, %s [%s]' % (d, got, wt(pf_at(r)), B.cat(o), C.c_note(b)))
                key = (st, 'merge', got)
                tally[key] = tally.get(key, 0) + 1
            print('   %-22s %s' % (label, ' | '.join(cells)))
        if st == 'X':
            continue
        for oid, what, edits in STATE_OPS[1:]:
            b = play(host, ops, 2, 'st')
            sel = ('ver', 'rule:N/pf', 'new')
            u = b.uid(b.W.pick(b.W.reps[0], sel))
            t = b.W.t + 10
            f = b.files
            if edits == 'delrec':
                host.run('delrec', f[0], u, t)
            elif edits == 'norec':
                b.patch(0, ['\t'.join(['R', u])])
            elif edits == 'bin':
                host.run('bin', f[0], u, t)
            elif edits == 'lost':
                b.step(30, ('del', 0, sel, 1))
                b.step(31, ('del', 0, ('head', 'new'), 1))
                b.step(32, ('reseal', 0))
                t = b.W.t
            else:
                for e in edits:
                    host.run('history-edit', f[0], u, t, e, 'doc')
                    t += 60
            b.W.t = t + 10
            b.sync_file(0)
            e0, e1 = f[0] + '.edited', f[1] + '.edited'
            kp.copy(f[0], e0)
            kp.copy(f[1], e1)
            W0 = b.W.clone()
            enc0 = (dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))
            r = read_model(b, 0)
            o, _ = B.outcome(b, 0)
            line = ['copy 0: %s, gives pf %s, store %s, %s' % (state(b, r), wt(pf_at(r)), r['status'], B.cat(o))]
            for label, d, s, prog in STATE_MERGES:
                kp.copy(e0, f[0])
                kp.copy(e1, f[1])
                b.W = W0.clone()
                b.enc.W = b.W
                b.enc.uuid, b.enc.eid, b.macs = dict(enc0[0]), dict(enc0[1]), dict(enc0[2])
                b.sync_file(0)
                b.sync_file(1)
                b.step(40, C.merge_op(prog, d, s))
                o, r = B.outcome(b, d)
                got = state(b, r)
                line.append('%s: %s, gives pf %s, store %s, %s' % (label, got, wt(pf_at(r)), r['status'], B.cat(o)))
                key = (st, oid, got if r['status'] == 'ok' else 'store ' + r['status'])
                tally[key] = tally.get(key, 0) + 1
            print('   %s: %s' % (oid, what))
            for x in line:
                print('      ' + x)
    print('## E made at copy 0 and O made at copy 1, apart, from the row r both held')
    for label, prog in C.PROGRAMS:
        cells = []
        for d, s in ((1, 0), (0, 1)):
            b = play(host, base + [('row', 0, 'N', 'pf', ''), ('row', 1, 'N', 'pf', 'off'), C.merge_op(prog, d, s)], 2, 'st')
            o, r = B.outcome(b, d)
            cells.append('into %d: %s, gives pf %s, %s' % (d, state(b, r), wt(pf_at(r)), B.cat(o)))
        print('   %-22s %s' % (label, ' | '.join(cells)))
    print('== tally (state made, operation, state read after the merge): ' + ', '.join(
        '%s %s %s %d' % (a, o, g, n) for (a, o, g), n in sorted(tally.items())))


# ------------------------------------------------------------------ switch (item 5)
SWITCH = [
    ('W1', 'the switch turned on at copy 0, and on then off at copy 1, apart',
     [ROOT_RW, ('row', 0, 'N', 'pf', 'r'), SYNC], [('switch', 0, 'N', 'deny')],
     [('switch', 1, 'N', 'deny'), ('switch', 1, 'N', 'inherit')]),
    ('W2', 'the switch turned on at copy 0; pf\'s row widened at copy 1, apart',
     [ROOT_RW, ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pg', 'r'), SYNC], [('switch', 0, 'N', 'deny')],
     [('row', 1, 'N', 'pf', 'rw')]),
    ('W3', 'S34: under a refusing switch, pf\'s row removed at copy 0; the switch turned off at copy 1, apart',
     [ROOT_RW, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pg', 'r'), SYNC],
     [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'N', 'inherit')]),
    ('W4', 'the switch turned on at copy 0; a row made for pg, unnamed until then, at copy 1, apart',
     [ROOT_RW, ('row', 0, 'N', 'pf', 'r'), SYNC], [('switch', 0, 'N', 'deny')], [('row', 1, 'N', 'pg', 'r')]),
    ('W5', 'under a switch that inherits, pf\'s row removed at copy 0; the switch turned on at copy 1, apart',
     [ROOT_RW, ('row', 0, 'N', 'pf', 'r'), SYNC], [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'N', 'deny')]),
    ('W6', 'under a refusing switch, pf\'s row removed and synchronized; then the row removed again at copy 0'
     ' while copy 1 turns the switch off, apart',
     [ROOT_RW, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pf', 'off'), SYNC],
     [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'N', 'inherit')]),
    ('W7', 'under a refusing switch, pf\'s row removed at copy 0; at copy 1 the switch turned off and pf\'s row'
     ' then removed, apart',
     [ROOT_RW, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), SYNC],
     [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'N', 'inherit'), ('row', 1, 'N', 'pf', 'off')]),
    ('W8', 'across nodes: pf\'s empty rule at N taken off at copy 0; the root widened for pf at copy 1, apart',
     [('rule', 0, 'root', 'r'), ('row', 0, 'N', 'pf', ''), SYNC], [('row', 0, 'N', 'pf', 'off')],
     [('row', 1, 'root', 'pf', 'rw')]),
    ('W9', 'W8 in the whole-rule form: N\'s rule naming pf with nothing taken off at copy 0; the root widened'
     ' for pf at copy 1, apart',
     [('rule', 0, 'root', 'r'), ('rule', 0, 'N', 'pf='), SYNC], [('rule', 0, 'N', 'inherit')],
     [('rule', 1, 'root', 'pf=rw,pg=r')]),
]
VARIANTS = ('empty', 'again', 'off')


def part_switch(host):
    print('== Item 5: the switch "Refuse any profile not named here" as a record of its own. Each sequence')
    print('   under each way a removed row is written: `empty` (decided: an empty rule where the gate reads')
    print('   the switch as refusing, nothing written where the row is an empty rule already), `again`')
    print('   (the same, but removing an empty rule under a refusing switch takes the rule off), `off`')
    print('   (the control: the rule taken off always). Each cell: what each copy gave before the merge,')
    print('   what the merged file gives, and the letters it gives that neither copy gave (fail-open).')
    tally = {}
    for wid, what, base, c0, c1 in SWITCH:
        print('## %s: %s' % (wid, what))
        print('   base: %s; copy 0: %s; copy 1: %s' % (seq(base), seq(c0), seq(c1)))
        for variant in (VARIANTS if wid != 'W9' else ('whole-rule',)):
            B.ROWS = variant
            B.use('rows' if wid != 'W9' else 'whole')
            b0 = play(host, base + c0 + c1, 2, 'sw' + variant[0])
            r = [read_model(b0, i) for i in (0, 1)]
            print('   %-6s copy 0 gives %s (%s) | copy 1 gives %s (%s)' % (
                variant, gives(r[0]), rows_of(b0, r[0]), gives(r[1]), rows_of(b0, r[1])))
            for label, prog in C.PROGRAMS:
                cells = []
                for d, s in ((1, 0), (0, 1)):
                    b = play(host, base + c0 + c1, 2, 'sw' + variant[0])
                    pre = (C.answer_words(b, d), C.answer_words(b, s))
                    b.step(50, C.merge_op(prog, d, s))
                    rr = read_model(b, d)
                    o, _ = B.outcome(b, d)
                    w = C.beyond(pre[0], pre[1], C.answer_words(b, d))
                    verdict = 'BEYOND BOTH COPIES: ' + w if w else 'within what the copies gave'
                    key = (wid, variant, 'fail-open' if w else 'within')
                    tally[key] = tally.get(key, 0) + 1
                    cells.append('into %d: %s (%s); %s; oracle %s [%s]' % (
                        d, gives(rr), rows_of(b, rr), verdict, B.cat(o), C.c_note(b)))
                    cells += C.c_lines(b, '         ')
                print('   %-6s %-22s %s' % ('', label, cells[0]))
                for x in cells[1:]:
                    print('   %-6s %-22s %s' % ('', '', x))
    B.ROWS = 'empty'
    B.use('rows')
    print('== tally (sequence, variant, against both copies): ' + ', '.join(
        '%s %s %s %d' % (a, v, w, n) for (a, v, w), n in sorted(tally.items())))


# ------------------------------------------------------------------ dels (item 6)
def part_dels(host):
    print('== Item 6: a deletion record of a row\'s version. A version and the summary entry deleted for good')
    print('   with deletion records at copy 0, so that only the deletion record tells of the version;')
    print('   Seal again; then copy 1 merged in. Read with the identifier in the form run (`pair`: the subject')
    print('   hash over the node and the profile) and in the form `split` (the subject hash over the node,')
    print('   the profile\'s keyed tag in the two bytes of the height).')
    base = [ROOT_RW, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pg', ''),
            ('row', 0, 'root', 'pg', 'r'), SYNC]
    print('   base: %s' % seq(base))
    answers = {}
    for form in ('pair', 'split'):
        S.ID_FORM = form
        for sel, what in ((('ver', 'rule:N/pf', 'new'), 'N/pf'), (('ver', 'rule:N/pg', 'new'), 'N/pg'),
                          (('ver', 'sw:N', 'new'), 'N switch'), (('ver', 'rule:root/pg', 'new'), 'root/pg')):
            ops = base + [('del', 0, sel, 1), ('del', 0, ('head', 'new'), 1)]
            b = play(host, ops, 2, 'dl' + form[0])
            r1 = read_model(b, 0)
            b.step(len(ops), ('reseal', 0))
            r2 = read_model(b, 0)
            row = ['after the deletions: store %s, lost %s' % (r1['status'], rows_of(b, dict(standing={}, lost=r1.get('lost', [])))),
                   'Seal again: store %s, gives %s; %s' % (r2['status'], gives(r2), rows_of(b, r2))]
            for label, d, s, prog in STATE_MERGES:
                bb = play(host, ops + [('reseal', 0), C.merge_op(prog, d, s)], 2, 'dl' + form[0])
                rr = read_model(bb, d)
                o, _ = B.outcome(bb, d)
                row.append('%s: store %s, gives %s; %s; oracle %s [%s]' % (label, rr['status'], gives(rr), rows_of(bb, rr),
                                                                        B.cat(o), C.c_note(bb)))
            answers[(form, what)] = [x for x in row]
            print('## %s, the version of %s deleted' % (form, what))
            for x in row:
                print('   ' + x)
    S.ID_FORM = 'pair'
    same = sum(1 for (f, w), a in answers.items() if f == 'pair' and answers[('split', w)] == a)
    print('== the two forms give the same lines: %d of %d deletions' % (same, len(answers) // 2))
    # what telling a deletion record costs: the keyed hashes the reading computes, and its time
    print('== the cost of telling a deletion record, by the reading\'s own count of keyed hashes and its time')
    print('   (Python, median of 5, a dump of a real file with that many ruled nodes added as groups, each')
    print('   with four rows of four profile names and the switch named by a deletion record)')
    for form in ('pair', 'split'):
        S.ID_FORM = form
        b = play(host, base + [('padd', 0, 'p3'), ('padd', 0, 'p4')], 2, 'dc' + form[0])
        d0 = b.dump(0)
        key = S.keybytes('K')
        for n in (20, 200, 2000):
            d = dict(d0)
            d['groups'] = dict(d0['groups'])
            d['dels'] = dict(d0['dels'])
            for k in range(n):
                g = 'cc%030x' % k
                d['groups'][g] = {'parent': d0['root'], 'name': 'g%d' % k}
                d['dels'][S.tag(key, 'version') + S.subject_hash(key, 'switch:' + g) + '0000' + '%012x' % k] = 1
            times = []
            for _ in range(5):
                t = time.perf_counter()
                r = RD.read(d, history=True)
                times.append(time.perf_counter() - t)
            print('   %-5s %5d ruled nodes: %6d keyed hashes for subjects, read %.1f ms' % (
                form, n, r['counts']['subject_hashes'], 1000 * statistics.median(times)))
    S.ID_FORM = 'pair'
    print('   The names the file knows here: pf, p3, p4 on the list, and pg from its rows: four. The form')
    print('   `pair` computes for each node of the tree a hash for its rule, its switch and each name; the')
    print('   form `split` a hash for its rule and its switch, and a tag for each name once.')


# ------------------------------------------------------------------ growth (item 7)
def part_growth(host):
    print('== Item 7: the store\'s strings for the same rules in both forms, from store.py\'s own format')
    print('   (no file written). A ruled node holds k rows and the switch; in the whole-rule form a single')
    print('   version of the node\'s rule names its k profiles and the switch. Sizes are bytes of KPG.')
    print('   strings; an item\'s name is KPG.S. and four digits, so a summary entry holds 9,999 items at most.')
    key = S.keybytes('K')

    def entry(kind, d, extra=None):
        data = S.dumps(d)
        extra = extra or {}
        m = S.mac(key, kind, data, extra)
        u = S.ident(key, kind, S.subject_of(kind, d), int(d.get('height', 0)), m)
        size = len('KPG.Kind') + len(kind) + len('KPG.Data') + len(data.encode()) + len('KPG.Mac') + len(m)
        size += sum(len(k) + len(v.encode()) for k, v in extra.items())
        return u, size

    def nonce(s):
        return hashlib.sha256(s.encode()).hexdigest()[:32]

    for n in (20, 200, 2000):
        for k in (1, 4, 10):
            profs = ['p%02d' % j for j in range(k)]
            row = {}
            for form in ('whole', 'rows'):
                items, ents, entry_size = [], 0, 0
                for x in range(n):
                    node = 'dd%030x' % x
                    if form == 'whole':
                        objs = [('rule:' + node, 'pp:' + ','.join('%s=rw' % p for p in profs) + ';default=deny')]
                    else:
                        objs = [('rule:%s/%s' % (node, p), 'row:rw') for p in profs] + [('switch:' + node, 'sw:deny')]
                    for o, dc in objs:
                        d = {'v': '2', 'object': o, 'after': '', 'doc': dc, 'nonce': nonce(o)}
                        u, sz = entry('version', d)
                        ents += 1
                        entry_size += sz
                        items.append(S.dumps({'kind': 'version', 'id': u, 'object': o, 'after': '', 'doc': dc}))
                extra = {'KPG.S.%04d' % (j + 1): it for j, it in enumerate(sorted(items))}
                hd = {'v': '2', 'nonce': nonce('head'), 'height': '2', 'after': 'ab' * 16}
                _, head_size = entry('head', hd, extra)
                # a single edit: one row (or the node's rule) set again, naming the version it replaces
                node = 'dd%030x' % 0
                if form == 'whole':
                    o, dc = 'rule:' + node, 'pp:' + ','.join('%s=r' % p for p in profs) + ';default=deny'
                else:
                    o, dc = 'rule:%s/%s' % (node, profs[0]), 'row:r'
                d = {'v': '2', 'object': o, 'after': 'ab' * 16, 'doc': dc, 'nonce': nonce(o + '/2')}
                _, one = entry('version', d)
                row[form] = (ents, entry_size, len(items), head_size, one)
            w, r = row['whole'], row['rows']
            print('   %4d nodes, %2d rows: whole-rule %5d entries, %8d bytes, summary %5d items %9d bytes%s; '
                  'per profile %5d entries, %8d bytes, summary %5d items %9d bytes%s; a single edit adds an entry '
                  'of %d bytes (whole-rule) or %d (per profile), and the summary entry written with it changes by '
                  'the size of that item' % (
                      n, k, w[0], w[1], w[2], w[3], ' OVER 9,999' if w[2] > 9999 else '',
                      r[0], r[1], r[2], r[3], ' OVER 9,999' if r[2] > 9999 else '', w[4], r[4]))
    print('   The whole-rule document is written here as `pp:` with every profile and `;default=deny`, a')
    print('   stand-in of the same length as the probe\'s documents and the switch; the `after` lists are')
    print('   empty but in the single edit, which names one identifier.')


def main():
    if sys.argv[1] == '--rows':
        B.rows(sys.argv[2])
        RD.SHOW = True
        del sys.argv[1:3]
    else:
        raise SystemExit('--rows VARIANT first')
    part = sys.argv[1]
    sys.stdout.reconfigure(line_buffering=True)
    t0 = time.time()
    if part == 'growth':
        part_growth(None)
        print('== %.0f s' % (time.time() - t0))
        return
    host = kp.Host()
    try:
        {'apart': part_apart, 'same': part_same, 'states': part_states, 'switch': part_switch,
         'dels': part_dels}[part](host)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
