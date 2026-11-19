"""ADDED (owed forms): the forms STORE.md 644 item 24 left owed after the run of rules per profile,
on real files.

Run:  uv run --no-project python -I -X utf8 owed.py PART [COUNT SEED]

Every part plays its sequences through bridge.Bridge under bridge.rows('empty') (a removed row
written as decided) with bridge.owed(): every gate act is the model's write path, written into a
real .kdbx by KeePassLib 2.62; every merge is KeePassLib's or keepassxc-cli's; every file is read
by read.py. A row is a profile's rule at a node, written `N/pf`. Letters are printed in the order
of the mask CRUSDLAP: create, read, update, strip, delete, list, and the two switches, attachments
and protected values, which add to the verbs of the same rule and grant nothing alone. A merged
file is held to the oracle (every decision any gate made, read in the same form) and to what both
copies gave just before the merge, by letters and by capabilities (crdt_model.caps: a verb alone,
or a verb with a switch of the same rule).

Parts:
  merges4   item 1: histories with four profiles and the full mask over two copies, each ending in
            every merge both ways (KeePassLib's five methods, four keepassxc-cli releases)
  random4   item 1: random histories of acts on rows and switches with four profiles and the full
            mask, at the root and N
  levels    item 2: sequences with a group between the root and N (the group General, the model's
            node G), each ending in every merge both ways
  random3   item 2: random4 at the root, the group and N
  offlist   item 3: a row of a name off the list whose records and summary items are lost, then the
            name added again
  tags      item 4: two profile names whose keyed tags meet, in the identifier's form `split`
  afters    item 5: the `after` lists of a row edited many times, in a copy and in two copies
"""
import json
import math
import os
import statistics
import subprocess
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

FOUR = ('pf', 'pg', 'ph', 'pi')
MASK = 'crusdlap'
FULL_VALUES = ('crusdlap', 'crudl', 'rl', 'rla', 'rlp', 'cula', 'a', '', 'off')
SYNC = ('merge', 'kp_sync', 1, 0)
BACK = ('merge', 'kp_sync', 0, 1)
STATE_MERGES = [('Synchronize into 1', 1, 0, 'Synchronize'), ('Synchronize into 0', 0, 1, 'Synchronize'),
                ('KeePassXC 2.8.0 into 1', 1, 0, '2.8.0'), ('KeePassXC 2.8.0 into 0', 0, 1, '2.8.0')]


def setup(profs, letters, levels):
    B.rows('empty')
    B.PREFIX = 'qo-'
    B.owed(profs, letters, levels)
    RD.SHOW = True


def seq(ops):
    return ' ; '.join(' '.join(map(str, op)) for op in ops)


def ml(letters):
    order = MASK + 'w'
    return ''.join(sorted(letters, key=order.index))


def per_profile(w, only=False):
    """Letters per profile in the order of the mask, every profile of the configuration, or with
    `only` the profiles that have a letter."""
    out = []
    for p in M.PROFS:
        x = ml(v.split('.')[1] for v in w if v.split('.')[0] == p)
        if x or not only:
            out.append('%s:%s' % (p, x or '-'))
    return ' '.join(out)


def all_rows(spec):
    """A rule act's table naming every profile of the configuration with the same letters."""
    return ','.join('%s=%s' % (p, spec) for p in M.PROFS)


def answer(b, i):
    return b.as_model(B.rd(b.dump(i), B.DECIDED))


def beyond2(pre0, pre1, after, nodes):
    """What the merged file gives at each node beyond both copies just before it: letters, and
    capabilities that are not already a letter beyond."""
    lt, ct = [], []
    for n in nodes:
        a, x, y = (d.get(n, frozenset()) for d in (after, pre0, pre1))
        letters = a - x - y
        if letters:
            lt.append('%s %s' % (n, per_profile(letters, True)))
            continue
        c = M.caps(a) - M.caps(x) - M.caps(y)
        if c:
            ct.append('%s %s' % (n, M.caps_text(c)))
    return '; '.join(lt), '; '.join(ct)


def verdict(bl, bc):
    if bl:
        return 'BEYOND BOTH COPIES, letters: ' + bl
    if bc:
        return 'BEYOND BOTH COPIES, capabilities only: ' + bc
    return 'within what the copies gave'


def gives(r, nodes):
    return '; '.join('%s %s' % (n, per_profile(r['words_raw'].get(n, frozenset()))) for n in nodes)


# ------------------------------------------------------------------ merges of whole histories
def part_cells(host, title, histories, nodes, tag):
    """Each history: the base, copy 0's acts, copy 1's acts; then every merge both ways between
    copies 0 and 1, each merged file held to the oracle and to both copies just before it."""
    print(title)
    tally, beyond = {}, {}
    for hid, what, base, c0, c1, nrep in histories:
        ops = base + c0 + c1
        print('## %s: %s' % (hid, what))
        print('   base: %s; copy 0: %s; copy 1: %s' % (seq(base), seq(c0), seq(c1)))
        b0 = C.run_ops(host, nrep, tag, ops)
        r0, r1 = answer(b0, 0), answer(b0, 1)
        print('   before the merge: copy 0 gives %s | copy 1 gives %s' % (gives(r0, nodes), gives(r1, nodes)))
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                b = C.run_ops(host, nrep, tag, ops)
                pre = (answer(b, d)['words_raw'], answer(b, s)['words_raw'])
                b.step(len(ops), C.merge_op(prog, d, s))
                o, r = B.outcome(b, d)
                bl, bc = beyond2(pre[0], pre[1], r['words_raw'], nodes)
                imp = ' import' if prog == 'CreateNewUuids' else ''
                key = (hid + imp, B.cat(o))
                tally[key] = tally.get(key, 0) + 1
                kind = 'letters' if bl else 'capabilities only' if bc else 'within'
                beyond[(hid + imp, kind)] = beyond.get((hid + imp, kind), 0) + 1
                cells.append('into %d: %s; %s; oracle %s [%s]' % (d, gives(r, nodes), verdict(bl, bc), B.cat(o),
                                                                   C.c_note(b)))
                cells += C.c_lines(b, '      ')
            print('   %-22s %s' % (label, cells[0]))
            for x in cells[1:]:
                print('   %-22s %s' % ('', x))
    print('== tally (history, the oracle\'s class): ' + ', '.join('%s %s %d' % (h, c, n) for (h, c), n in sorted(tally.items())))
    print('== tally (history, against both copies just before the merge): ' + ', '.join(
        '%s %s %d' % (h, c, n) for (h, c), n in sorted(beyond.items())))
    for kind in ('letters', 'capabilities only', 'within'):
        print('   %s: %d cells, %d of them imports' % (kind, sum(n for (h, k), n in beyond.items() if k == kind),
                                                      sum(n for (h, k), n in beyond.items() if k == kind and h.endswith('import'))))


def root_all():
    return ('rule', 0, 'root', all_rows('crusdlap'))


def merges4_histories():
    R = root_all()
    rl = ','.join('%s=rl' % p for p in FOUR[1:])
    return [
        ('O1', 'pf\'s row at N: attachments dropped at copy 0, protected values added at copy 1, apart',
         [R, ('row', 0, 'N', 'pf', 'rla'), SYNC], [('row', 0, 'N', 'pf', 'rl')], [('row', 1, 'N', 'pf', 'rlap')], 2),
        ('O2', 'pg\'s row at N: read with attachments at copy 0, update with attachments at copy 1, apart; met, a switch'
         ' alone', [R, ('row', 0, 'N', 'pg', 'rla'), SYNC], [('row', 0, 'N', 'pg', 'ra')], [('row', 1, 'N', 'pg', 'ua')], 2),
        ('O3', 'ph\'s row at N: the switches crossed, attachments at copy 0 and protected values at copy 1, apart',
         [R, ('row', 0, 'N', 'ph', 'rlap'), SYNC], [('row', 0, 'N', 'ph', 'rla')], [('row', 1, 'N', 'ph', 'rlp')], 2),
        ('O4', 'four profiles at N: pf widened with both switches and ph taken off at copy 0; pg emptied and attachments'
         ' added to pi at copy 1, apart',
         [R, ('rule', 0, 'N', 'pf=rl,pg=rla,ph=rlp,pi=crudl'), SYNC], [('row', 0, 'N', 'pf', 'rlap'), ('row', 0, 'N', 'ph', 'off')],
         [('row', 1, 'N', 'pg', ''), ('row', 1, 'N', 'pi', 'crudla')], 2),
        ('O5', 'pf\'s row at N: attachments dropped at copy 0, verbs widened at copy 1, apart',
         [R, ('row', 0, 'N', 'pf', 'rla'), SYNC], [('row', 0, 'N', 'pf', 'rl')], [('row', 1, 'N', 'pf', 'crula')], 2),
        ('O6', 'across nodes, a combination: the root gives pf update with attachments, N reads; N\'s row taken off at copy'
         ' 0; the root set to read with attachments at copy 1, apart',
         [('rule', 0, 'root', 'pf=ua,' + rl), ('row', 0, 'N', 'pf', 'r'), SYNC], [('row', 0, 'N', 'pf', 'off')],
         [('row', 1, 'root', 'pf', 'ra')], 2),
        ('O7', 'across nodes, the switch: N refuses pf, which it does not name; copy 0 turns the switch off where the root'
         ' gives update with attachments; copy 1 sets the root to read with attachments, apart',
         [('rule', 0, 'root', 'pf=ua,' + rl), ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pg', 'rl'), SYNC],
         [('switch', 0, 'N', 'inherit')], [('row', 1, 'root', 'pf', 'ra')], 2),
        ('O8', 'S34 with switches: under a refusing switch, pf\'s row (read, list, attachments) removed at copy 0; the'
         ' switch turned off at copy 1, apart',
         [R, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'rla'), ('row', 0, 'N', 'pg', 'rlp'), SYNC],
         [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'N', 'inherit')], 2),
    ]


def levels_histories():
    R = root_all()
    rl = ','.join('%s=rl' % p for p in FOUR[1:])
    THIRD = ('merge', 'kp_sync', 2, 0)
    return [
        ('L1', 'nearest wins per profile, apart at two levels: pf narrowed at N at copy 0; pf widened at G at copy 1',
         [R, ('row', 0, 'G', 'pf', 'rl'), SYNC], [('row', 0, 'N', 'pf', 'r')], [('row', 1, 'G', 'pf', 'crudla')], 2),
        ('L2', 'two profiles at two levels: pg widened at G at copy 0; pf widened at N at copy 1, apart',
         [R, ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'G', 'pg', 'rl'), ('row', 0, 'N', 'pf', 'rla'), SYNC],
         [('row', 0, 'G', 'pg', 'rla')], [('row', 1, 'N', 'pf', 'rlap')], 2),
        ('L3', 'pf\'s rule at N taken off at copy 0 and narrowed at copy 2, apart, and the two met in copy 0; pf widened'
         ' at G at copy 1',
         [R, ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'N', 'pf', 'rla'), SYNC, THIRD],
         [('row', 0, 'N', 'pf', 'off'), ('row', 2, 'N', 'pf', 'r'), ('merge', 'kp_sync', 0, 2)],
         [('row', 1, 'G', 'pf', 'crusdlap')], 3),
        ('L4', 'L3 with pf narrowed at G at copy 1',
         [R, ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'N', 'pf', 'rla'), SYNC, THIRD],
         [('row', 0, 'N', 'pf', 'off'), ('row', 2, 'N', 'pf', 'r'), ('merge', 'kp_sync', 0, 2)],
         [('row', 1, 'G', 'pf', 'l')], 3),
        ('L5', 'G\'s switch turned on at copy 0, N\'s switch inheriting; pg\'s row at N taken off at copy 1, apart',
         [R, ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'N', 'pg', 'r'), SYNC], [('switch', 0, 'G', 'deny')],
         [('row', 1, 'N', 'pg', 'off')], 2),
        ('L6', 'G\'s switch turned on at copy 0, and on then off at copy 1, apart',
         [R, ('row', 0, 'G', 'pf', 'rl'), SYNC], [('switch', 0, 'G', 'deny')],
         [('switch', 1, 'G', 'deny'), ('switch', 1, 'G', 'inherit')], 2),
        ('L7', 'S34 at G: under G\'s refusing switch, pf\'s row at G removed at copy 0; G\'s switch turned off at copy 1,'
         ' apart', [R, ('switch', 0, 'G', 'deny'), ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'G', 'pg', 'rl'), SYNC],
         [('row', 0, 'G', 'pf', 'off')], [('switch', 1, 'G', 'inherit')], 2),
        ('L8', 'across levels: pf\'s narrow rule at G taken off at copy 0; the root widened for pf at copy 1, apart',
         [('rule', 0, 'root', 'pf=rl,' + rl), ('row', 0, 'G', 'pf', 'r'), SYNC], [('row', 0, 'G', 'pf', 'off')],
         [('row', 1, 'root', 'pf', 'crusdlap')], 2),
        ('L9', 'across levels, the node between: pf\'s narrow rule at N taken off at copy 0; G widened for pf at copy 1,'
         ' apart', [R, ('row', 0, 'G', 'pf', 'rl'), ('row', 0, 'N', 'pf', 'r'), SYNC], [('row', 0, 'N', 'pf', 'off')],
         [('row', 1, 'G', 'pf', 'crudl')], 2),
        ('L10', 'G refuses pf, which it does not name, N\'s switch inherits and N names pf; pf\'s row at N taken off at'
         ' copy 0; G\'s switch turned off at copy 1, apart',
         [R, ('switch', 0, 'G', 'deny'), ('row', 0, 'G', 'pg', 'rl'), ('row', 0, 'N', 'pf', 'rl'), SYNC],
         [('row', 0, 'N', 'pf', 'off')], [('switch', 1, 'G', 'inherit')], 2),
    ]


# ------------------------------------------------------------------ random histories
def part_random(host, count, seed, nodes, tag):
    import random
    rng = random.Random(seed)
    t0 = time.time()
    outs, found_n, lines, nsteps, ndis, nread = {}, {}, [], 0, 0, 0
    for h in range(count):
        nrep = 2 if rng.random() < 0.6 else 3
        R = range(nrep)
        acts = []
        for r in R:
            for n in nodes:
                acts += [('switch', r, n, x) for x in ('deny', 'inherit')]
                acts += [('row', r, n, p, x) for p in M.PROFS for x in FULL_VALUES]
        merges = [m for d in R for s in R if d != s for m in
                  [('merge', 'kp_sync', d, s), ('merge', 'kp_other', d, s, 'OverwriteIfNewer'),
                   ('merge', 'xc', d, s, '2.8.0')]]
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(4, 10))]
        b = B.Bridge(host, nrep, tag)
        found = []
        for k, op in enumerate(ops):
            pre = None
            if op[0] == 'merge':
                pre = (answer(b, op[2])['words_raw'], answer(b, op[3])['words_raw'])
            if not b.step(k, op):
                b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
                continue
            nsteps += 1
            if pre is not None:
                bl, bc = beyond2(pre[0], pre[1], answer(b, op[2])['words_raw'], nodes)
                if bl or bc:
                    found.append((k, op, verdict(bl, bc)))
                    kind = 'letters' if bl else 'capabilities only'
                    found_n[kind] = found_n.get(kind, 0) + 1
        for i in R:
            o, r = B.outcome(b, i)
            outs[B.cat(o)] = outs.get(B.cat(o), 0) + 1
        ndis += len(b.disagree)
        nread += len(b.readdiff)
        if found or b.disagree or b.readdiff:
            lines.append('## history %d (%d files): %s' % (h, nrep, seq(ops)))
            for k, op, w in found:
                lines.append('   at step %d (%s): %s' % (k, ' '.join(map(str, op)), w))
            lines += C.c_lines(b)
    print('== random histories of rows and switches, %d profiles, the mask %s, nodes %s, the removal written as %s:'
          ' %d histories, steps %d, entry disagreements %d, read differences %d, %.0f s' % (
              len(M.PROFS), MASK, ', '.join(nodes), B.ROWS, count, nsteps, ndis, nread, time.time() - t0))
    print('   files at the end against every decision: ' + ', '.join('%s %d' % kv for kv in sorted(outs.items())))
    print('   merges that gave more than both copies just before: by letters %d, by capabilities only %d' % (
        found_n.get('letters', 0), found_n.get('capabilities only', 0)))
    for x in lines:
        print(x)


# ------------------------------------------------------------------ offlist (item 3)
WAYS = [('delete', 'deleted for good, with deletion records'),
        ('unrecorded', 'deleted for good, no deletion record'),
        ('bin', 'deleted to the recycle bin, the bin then emptied'),
        ('clear', 'deleted for good with deletion records, then every deletion record removed')]


def lose(host, b, i, eids, way, k):
    """The entries of the model's elements `eids` lost from file i in the way named, at step k; the
    model takes what the file holds, and its read is compared with read.py's."""
    f = b.files[i]
    t = b.W.t + 10
    us = [b.uid(e) for e in eids]
    if way in ('delete', 'clear'):
        for u in us:
            host.run('delrec', f, u, t)
        if way == 'clear':
            host.run('clear', f)
    elif way == 'unrecorded':
        b.patch(i, ['\t'.join(['R', u]) for u in us])
    elif way == 'bin':
        for u in us:
            host.run('bin', f, u, t)
        host.run('empty', f, t + 5)
    b.W.t = t + 20
    b.readcheck(i, b.sync_file(i), k)


def present(b, i, test):
    EL = b.W.EL
    return sorted(u for u, (eid, mt, ok) in b.W.reps[i].entries.items() if u == eid and test(EL[eid]))


def row_state(b, r, node, p):
    o = 'rule:%s/%s' % (b.ids[node], p)
    if o in r.get('lost', []):
        return 'refuses (a version lost)'
    val = r.get('row_values', {}).get(o)
    if val is not None and val[0] == 'const':
        return 'refuses (a version beside its deletion record)'
    docs = r.get('standing', {}).get(o, [])
    if docs == ['row:']:
        return 'E'
    if docs == ['off:']:
        return 'O'
    if not docs:
        return 'X'
    return '+'.join(docs)


def letters_of(w, node, p):
    return frozenset(x for x in w.get(node, frozenset()) if x.split('.')[0] == p)


def offlist_look(b, i, last):
    """The file read: the state of N/pg, what N gives pg, the names, what client c1 reaches at N
    against `last`, the reach the operator's last decisions give c1 at N."""
    r = answer(b, i)
    o, _ = B.outcome(b, i)
    adm = r['adm'].get('c1')
    reach = frozenset(x for x in r['words'].get('N', frozenset()) if adm and x.split('.')[0] in adm)
    more, less = reach - last, last - reach
    against = ('WIDER than the last decision: c1 reaches %s' % per_profile(more) if more else
               'narrower than the last decision: c1 lacks %s' % per_profile(less) if less else 'as the last decision')
    return ('store %s; N/pg %s, gives pg %s; names %s; c1 holds %s; names known from the list %s, for rows %s;'
            ' %s; oracle %s' % (
                r['status'], row_state(b, r, 'N', 'pg'),
                ml(x.split('.')[1] for x in letters_of(r['words_raw'], 'N', 'pg')) or '-',
                ' '.join(r['names']) or '-', ' '.join(sorted(adm)) if adm is not None else 'not admitted',
                ' '.join(r.get('known_names', [])) or '-', ' '.join(r.get('row_names', [])) or '-', against,
                B.cat(o)), bool(more))


def snapshot(b):
    f = b.files
    for x in f:
        kp.copy(x, x + '.kept')
    return (b.W.clone(), dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))


def restore(b, snap):
    for x in b.files:
        kp.copy(x + '.kept', x)
    b.W = snap[0].clone()
    b.enc.W = b.W
    b.enc.uuid, b.enc.eid, b.macs = dict(snap[1]), dict(snap[2]), dict(snap[3])
    for i in range(len(b.files)):
        b.sync_file(i)


LOSSES = [('row', 'the versions of N/pg and every summary entry'),
          ('row and name', 'the versions of N/pg, every summary entry, and pg\'s additions and removals'),
          ('every row and name', 'the versions of every row of pg, every summary entry, and pg\'s additions and removals')]


def part_offlist(host):
    print('== Item 3: a row of a name off the list whose records and summary items are lost. Client c1 is admitted')
    print('   with pf and pg; pg is added to the list and given read at N; copy 1 takes everything; pg is removed from')
    print('   the list at copy 0, and copy 1 takes that too. Two trees: the root gives pf and pg read and write, so that')
    print('   pg is also named at the root (`root`), or the root names pf alone and pg is named at N alone (`N alone`).')
    print('   Then at copy 0 what each loss names is lost, in each way; Seal again; pg added again, its addition naming')
    print('   the removals the reading holds (`text`) or also those a deletion record tells once pg is counted as known')
    print('   (`name`); a further write of the plugin\'s own (the name ph added); pg added a second time; then each')
    print('   merge with copy 1. Each line: the state of N/pg (E an empty rule, O the rule taken off, X no version),')
    print('   what N gives pg, the list, what c1 holds, the names the file knows from the list\'s records and items,')
    print('   and what c1 reaches at N against the operator\'s last decisions (pf read and write; pg read once pg is')
    print('   added again). The control: pg never removed, the row and every summary entry lost in each way.')
    M.KNOWN = True
    last_on = frozenset(['pf.r', 'pf.w', 'pg.r'])
    last_off = frozenset(['pf.r', 'pf.w'])
    tally = {}
    cases = []
    for tree in ('root', 'N alone'):
        for loss, lwhat in LOSSES:
            if tree == 'N alone' and loss == 'every row and name':
                continue
            for way, what in WAYS:
                for form in ('text', 'name'):
                    cases.append((tree, 'off', loss, lwhat, way, what, form))
    for way, what in WAYS:
        cases.append(('root', 'on', 'row', LOSSES[0][1], way, what, None))
    bases = {'root': [('rule', 0, 'root', 'rw'), ('pair', 0), ('padd', 0, 'pg'), ('row', 0, 'N', 'pg', 'r'), SYNC],
             'N alone': [('row', 0, 'root', 'pf', 'rw'), ('pair', 0), ('padd', 0, 'pg'), ('row', 0, 'N', 'pg', 'r'), SYNC]}
    for tree, ops in bases.items():
        print('   base, %s: %s; then, but in the control, prem 0 pg ; merge kp_sync 1 0' % (tree, seq(ops)))
    for tree, name_state, loss, lwhat, way, what, form in cases:
        M.ADD_FORM = form or 'text'
        M.ROOT_ROWS = ('pf',) if tree == 'N alone' else None
        ops = bases[tree] + ([('prem', 0, 'pg'), SYNC] if name_state == 'off' else [])
        b = C.run_ops(host, 2, 'off', ops)
        k = len(ops)
        eids = present(b, 0, lambda e: (e.kind == 'ver' and e.obj == 'rule:N/pg') or e.kind == 'head')
        if loss != 'row':
            eids += present(b, 0, lambda e: e.kind in ('padd', 'prem') and e.obj == 'pg')
        if loss == 'every row and name':
            eids += present(b, 0, lambda e: e.kind == 'ver' and e.obj == 'rule:root/pg')
        print('## %s; pg %s the list; lost: %s; %s%s' % (tree, name_state, lwhat, what,
                                                       '; the addition as `%s`' % form if form else ''))
        lines = []
        before = last_on if name_state == 'on' else last_off
        lose(host, b, 0, eids, way, k)
        lines.append('after the loss: ' + offlist_look(b, 0, before)[0])
        sealed = b.step(k, ('reseal', 0))
        k += 1
        lines.append('Seal again%s: %s' % ('' if sealed else ' (does not apply)', offlist_look(b, 0, before)[0]))
        steps = [('a further write', ('padd', 0, 'ph'))]
        if name_state == 'off':
            steps = [('pg added again', ('padd', 0, 'pg'))] + steps + [('pg added a second time', ('padd', 0, 'pg'))]
        for label, op in steps:
            done = b.step(k, op)
            k += 1
            t, wide = offlist_look(b, 0, last_on)
            lines.append('%s%s: %s' % (label, '' if done else ' (does not apply)', t))
            key = (tree, name_state, loss, way, form, label, 'WIDER' if wide else 'not wider')
            tally[key] = tally.get(key, 0) + 1
        snap = snapshot(b)
        for label, d, s, prog in STATE_MERGES:
            restore(b, snap)
            b.step(k, C.merge_op(prog, d, s))
            t, wide = offlist_look(b, d, last_on)
            lines.append('%s: %s' % (label, t))
            key = (tree, name_state, loss, way, form, 'merged', 'WIDER' if wide else 'not wider')
            tally[key] = tally.get(key, 0) + 1
        for x in lines:
            print('   ' + x)
        for x in C.c_lines(b, '   '):
            print(x)
    M.KNOWN = False
    M.ROOT_ROWS = None
    print('== tally (tree, pg on or off the list, what was lost, the way, the addition, when, against the last'
          ' decision):')
    for key, n in sorted(tally.items(), key=lambda kv: tuple(str(x) for x in kv[0])):
        print('   %s: %d' % (' | '.join(str(x) for x in key), n))
    wider = sum(n for key, n in tally.items() if key[-1] == 'WIDER')
    print('== reads wider than the last decision: %d of %d' % (wider, sum(tally.values())))


# ------------------------------------------------------------------ tags (item 4)
def meeting_names(key):
    """The first two names q0000, q0001, ... whose tags meet under the key, and the first name
    whose tag is 0000, the value of every other version's height."""
    seen, pair, zero = {}, None, None
    i = 0
    while pair is None or zero is None:
        name = 'q%05d' % i
        t = S.profile_tag(key, name)
        if pair is None and t in seen:
            pair = (seen[t], name, t)
        seen.setdefault(t, name)
        if zero is None and t == '0000':
            zero = name
        i += 1
    return pair, zero, i


def tags_look(b, i, profs, last):
    r = answer(b, i)
    o, _ = B.outcome(b, i)
    out, wider = [], []
    for p in profs:
        got = ml(x.split('.')[1] for x in letters_of(r['words_raw'], 'N', p))
        out.append('%s %s gives %s' % (p, row_state(b, r, 'N', p), got or '-'))
        if set(got) - set(last[p]):
            wider.append('%s gets %s, its last decision %s' % (p, got, last[p] or '-'))
    return ('store %s; %s; tags that meet among the names the file knows: %s; %s; oracle %s' % (
        r['status'], ', '.join(out), ', '.join('/'.join(x) for x in r.get('tag_meets', [])) or 'none',
        'WIDER: ' + '; '.join(wider) if wider else 'no profile above its last decision', B.cat(o)), bool(wider))


def part_tags(host):
    key = S.keybytes('K')
    (a, b_, t), z, tried = meeting_names(key)
    print('== Item 4: two profile names whose keyed tags meet, in the form `split` (the subject hash over the node\'s')
    print('   rule, the profile\'s keyed tag in the two bytes of the height). Found by search under the store key:')
    print('   %s and %s, tag %s, after %d names tried; %s, the first name whose tag is 0000.' % (a, b_, t, tried, z))
    print('   How likely two of n names meet under a key, 1 - (1 - 1/65536)(1 - 2/65536)...(1 - (n-1)/65536):')
    for n in (2, 4, 8, 16, 32, 64, 100, 256, 1000):
        p = 1 - math.prod(1 - k / 65536 for k in range(n))
        print('      n = %4d: %.6f' % (n, p))
    print('   A name\'s tag is 0000 with chance 1/65536 = %.6f for each name.' % (1 / 65536))
    # The reading as the form was run: a dictionary from a tag to a name, filled by iterating the set
    # of names the file knows for a row, keeps the name the set yields last. The order of a set of
    # strings follows the hashes of the strings, which Python draws afresh for each process unless
    # PYTHONHASHSEED fixes them; the same file is read with either name kept, by process.
    names = ['pf', a, b_]
    kept = {}
    for seed in range(40):
        env = dict(os.environ, PYTHONHASHSEED=str(seed))
        out = subprocess.run([sys.executable, '-S', '-X', 'utf8', '-c',
                              'import sys; d = {}\nfor p in set(sys.argv[1:]):\n    d[len(p)] = p\nprint(d[6])',
                              ] + names, env=env, capture_output=True, text=True, check=True).stdout.strip()
        kept[out] = kept.get(out, 0) + 1
    print('   The reading as the form was run fills a dictionary from a tag to a name by iterating the set of names')
    print('   the file knows (here pf, %s, %s), and keeps the name the set yields last. Over the hash seeds 0 to 39 of'
          % (a, b_))
    print('   this Python, the name kept for the tag %s: %s. So the same file reads differently from process to'
          % (t, ', '.join('%s in %d' % kv for kv in sorted(kept.items()))))
    print('   process. Below, `first` and `last` are the two answers that reading gives, with the names sorted so')
    print('   that each run is the same; `each` is the reading proposed: every name of the tag, where no version')
    print('   present or restated carries the deletion record\'s identifier.')
    S.ID_FORM = 'split'
    M.TAG_OF = lambda name: S.profile_tag(key, name)
    profs = ('pf', a, b_)
    setup(profs, 'rw', 2)
    base = [('rule', 0, 'root', 'rw'), ('padd', 0, a), ('padd', 0, b_), ('row', 0, 'N', a, 'r'), ('row', 0, 'N', b_, ''), SYNC]
    tally = {}
    for reading in ('first', 'last', 'each'):
        M.TAG_READ = RD.TAG_READ = reading
        print('## the reading `%s`: a deletion record whose tag meets several names the file knows is told for %s' % (
            reading, 'the first of them in ordinal order, %s' % a if reading == 'first' else
            'the last of them in ordinal order, %s' % b_ if reading == 'last'
            else 'each of them, where no version present or restated carries its identifier'))
        hist = [('G1', 'row acts on both names apart: %s widened at copy 0, %s set to read at copy 1' % (a, b_),
                 base, [('row', 0, 'N', a, 'rw')], [('row', 1, 'N', b_, 'r')], 2)]
        part_cells(host, '   every merge both ways after G1', hist, ('root', 'N'), 'tg')
        last = {'pf': 'rw', a: 'r', b_: ''}
        for gid, who, with_head in (('G2', a, True), ('G3', b_, True), ('G4', a, False), ('G5', b_, False)):
            ops = list(base)
            bb = C.run_ops(host, 2, 'tg', ops)
            k = len(ops)
            eids = present(bb, 0, lambda e: e.kind == 'ver' and e.obj == 'rule:N/' + who)
            if with_head:
                eids += present(bb, 0, lambda e: e.kind == 'head')
            print('## %s: the version of N/%s deleted for good with a deletion record%s, at copy 0' % (
                gid, who, ', with every summary entry' if with_head else '; the summary entry restating it kept'))
            lose(host, bb, 0, eids, 'delete', k)
            t, w = tags_look(bb, 0, profs, last)
            print('   after the deletion: ' + t)
            tally[(reading, gid, 'after the deletion')] = 'WIDER' if w else 'not wider'
            if bb.step(k, ('reseal', 0)):
                k += 1
                t, w = tags_look(bb, 0, profs, last)
                print('   Seal again: ' + t)
                tally[(reading, gid, 'Seal again')] = 'WIDER' if w else 'not wider'
            snap = snapshot(bb)
            for label, d, s, prog in STATE_MERGES:
                restore(bb, snap)
                bb.step(k, C.merge_op(prog, d, s))
                t, w = tags_look(bb, d, profs, last)
                print('   %s: %s' % (label, t))
                tally[(reading, gid, label)] = 'WIDER' if w else 'not wider'
            for x in C.c_lines(bb, '   '):
                print(x)
    print('## G6: the name %s, whose tag is 0000, with read at N; its version and every summary entry deleted for good'
          ' with deletion records, at copy 0 (the reading `each`)' % z)
    setup(('pf', z), 'rw', 2)
    bb = C.run_ops(host, 2, 'tg', [('rule', 0, 'root', 'rw'), ('padd', 0, z), ('row', 0, 'N', z, 'r'), SYNC])
    eids = present(bb, 0, lambda e: (e.kind == 'ver' and e.obj == 'rule:N/' + z) or e.kind == 'head')
    lose(host, bb, 0, eids, 'delete', 4)
    t, w = tags_look(bb, 0, ('pf', z), {'pf': 'rw', z: 'r'})
    print('   after the deletion: ' + t)
    tally[('each', 'G6', 'after the deletion')] = 'WIDER' if w else 'not wider'
    if bb.step(5, ('reseal', 0)):
        t, w = tags_look(bb, 0, ('pf', z), {'pf': 'rw', z: 'r'})
        print('   Seal again: ' + t)
        tally[('each', 'G6', 'Seal again')] = 'WIDER' if w else 'not wider'
    for x in C.c_lines(bb, '   '):
        print(x)
    S.ID_FORM = 'pair'
    M.TAG_OF = None
    M.TAG_READ = RD.TAG_READ = 'last'
    print('== tally (reading, sequence: the reads wider than a last decision, of the reads)')
    for reading, gid in sorted(set(k[:2] for k in tally)):
        whens = [w for (r, g, w) in tally if (r, g) == (reading, gid)]
        wide = [w for w in whens if tally[(reading, gid, w)] == 'WIDER']
        print('   %s %s: %d of %d%s' % (reading, gid, len(wide), len(whens), ' (%s)' % ', '.join(sorted(wide)) if wide else ''))


# ------------------------------------------------------------------ afters (item 5)
def measure(b, i):
    """From the dump of file i: the standing versions of N/pf, the longest `after` among them and
    its content's length; the summary entry in force, its items, its item for N/pf, its strings'
    bytes and its own `after`; the deletion records; the history items of the store's entries; the
    file's bytes; the read's time."""
    d = b.dump(i)
    nid = b.ids['N']
    obj = 'rule:%s/pf' % nid
    named = set(d['dels'])
    vers, heads = {}, []
    for u, e in d['entries'].items():
        s = e['strings']
        if s.get('KPG.Kind') == 'version':
            data = json.loads(s['KPG.Data'])
            if data['object'] == obj:
                vers[u] = (data, len(s['KPG.Data']))
        elif s.get('KPG.Kind') == 'head' and u not in named:
            heads.append((u, s))
    dead = set()
    for u, (data, n) in vers.items():
        dead |= set(data['after'].split())
    standing = [(u, data, n) for u, (data, n) in vers.items() if u not in dead and u not in named]
    longest = max(standing, key=lambda x: len(x[1]['after'].split())) if standing else None
    item, hsize, hafter, nitems = 0, 0, 0, 0
    for u, s in heads:
        hsize = max(hsize, sum(len(k) + len(v) for k, v in s.items() if k.startswith('KPG.')))
        hafter = max(hafter, len(json.loads(s['KPG.Data'])['after'].split()))
        its = [v for k, v in s.items() if k.startswith('KPG.S.')]
        nitems = max(nitems, len(its))
        for v in its:
            it = json.loads(v)
            if it.get('object') == obj:
                item = max(item, len(v))
    hist = max([e.get('hist', 0) for e in d['entries'].values() if 'KPG.Kind' in e['strings']] + [0])
    times = []
    for _ in range(3):
        t = time.perf_counter()
        RD.read(d, history=True)
        times.append(time.perf_counter() - t)
    return dict(standing=len(standing), after=len(longest[1]['after'].split()) if longest else 0,
                content=longest[2] if longest else 0, item=item, heads=len(heads), hsize=hsize, hafter=hafter,
                items=nitems, dels=len(d['dels']), hist=hist, file=os.path.getsize(b.files[i]),
                ms=1000 * statistics.median(times))


def afters_line(k, m):
    return ('   %4d edits: standing %d, after %4d, content %6d; summary entries in force %d, its after %4d, items %d,'
            ' item of N/pf %6d, strings %7d; deletion records %4d; history items %d; file %7d bytes; read %.1f ms' % (
                k, m['standing'], m['after'], m['content'], m['heads'], m['hafter'], m['items'], m['item'], m['hsize'],
                m['dels'], m['hist'], m['file'], m['ms']))


CHECKS = (1, 2, 5, 10, 20, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500)


def part_afters(host, ka=400, kb=200):
    print('== Item 5: the `after` lists of a row edited many times. N/pf set alternately to read and to read and')
    print('   write. A: k edits in copy 0. B: edits alternately in copy 0 and copy 1, Synchronize after each into the')
    print('   other. C: an edit in each copy apart, then Synchronize both ways, each round. Each line read at copy 0:')
    print('   the standing versions of N/pf, the longest `after` among them (identifiers) and the length of its')
    print('   KPG.Data; the summary entry in force, its own `after`, its items, the length of its item for N/pf and')
    print('   the bytes of its KPG. strings; the deletion records; the most history items of any store entry; the')
    print('   file; the read (Python, median of 3).')
    setup(('pf', 'pg'), 'rw', 2)
    vals = ('r', 'rw')
    print('## A: edits in copy 0')
    b = B.Bridge(host, 2, 'af')
    for k in range(1, ka + 1):
        b.step(k, ('row', 0, 'N', 'pf', vals[k % 2]))
        if k in CHECKS:
            print(afters_line(k, measure(b, 0)))
    for x in C.c_lines(b, '   '):
        print(x)
    print('## B: edits alternately in copy 0 and copy 1, Synchronize after each')
    b = B.Bridge(host, 2, 'af')
    step = 0
    for k in range(1, kb + 1):
        i = (k + 1) % 2
        b.step(step, ('row', i, 'N', 'pf', vals[k % 2]))
        b.step(step + 1, ('merge', 'kp_sync', 1 - i, i))
        step += 2
        if k in CHECKS:
            print(afters_line(k, measure(b, 0)))
    for x in C.c_lines(b, '   '):
        print(x)
    print('## C: an edit in each copy apart, then Synchronize both ways')
    b = B.Bridge(host, 2, 'af')
    step = 0
    for k in range(2, kb + 1, 2):
        b.step(step, ('row', 0, 'N', 'pf', 'r'))
        b.step(step + 1, ('row', 1, 'N', 'pf', 'rw'))
        b.step(step + 2, BACK)
        b.step(step + 3, SYNC)
        step += 4
        if k in CHECKS:
            print(afters_line(k, measure(b, 0)))
    for x in C.c_lines(b, '   '):
        print(x)
    # past what was run: the lengths store.py writes for k identifiers in `after`
    print('## past the runs: the lengths store.py writes, for a version of N/pf naming k identifiers, and its item')
    key = S.keybytes('K')
    nid = B.node_ids()['N']
    ident = 'ab' * 16

    def content(k):
        d = {'v': '2', 'object': 'rule:%s/pf' % nid, 'after': ' '.join([ident] * k), 'doc': 'row:rw', 'nonce': ident}
        return len(S.dumps(d))

    def itemlen(k):
        return len(S.dumps({'kind': 'version', 'id': ident, 'object': 'rule:%s/pf' % nid,
                            'after': ' '.join([ident] * k), 'doc': 'row:rw'}))

    del key
    for ceiling, what in ((262144, 'the plugin\'s ceiling for a string it writes (STORE.md 643)'),
                          (1048576, 'the reader\'s ceiling for a string (STORE.md 607)')):
        kc = next(k for k in range(1, 40000) if content(k) > ceiling)
        ki = next(k for k in range(1, 40000) if itemlen(k) > ceiling)
        print('   %s, %d characters: a version\'s KPG.Data passes it at %d identifiers in `after`, its summary'
              ' item at %d' % (what, ceiling, kc, ki))
    print('   each identifier adds %d characters to both' % (content(2) - content(1)))


def main():
    part = sys.argv[1]
    sys.stdout.reconfigure(line_buffering=True)
    t0 = time.time()
    if part in ('merges4', 'random4'):
        setup(FOUR, MASK, 2)
    elif part in ('levels', 'random3'):
        setup(FOUR, MASK, 3)
    host = kp.Host()
    try:
        if part == 'merges4':
            part_cells(host, '== Item 1: four profiles and the full mask; each history over two copies, then every merge both'
                       ' ways', merges4_histories(), ('root', 'N'), 'm4')
        elif part == 'levels':
            part_cells(host, '== Item 2: the root, the group G (General) and N under it; each history then every merge'
                       ' both ways between copies 0 and 1', levels_histories(), ('root', 'G', 'N'), 'lv')
        elif part == 'random4':
            part_random(host, int(sys.argv[2]), int(sys.argv[3]), ('root', 'N'), 'r4')
        elif part == 'random3':
            part_random(host, int(sys.argv[2]), int(sys.argv[3]), ('root', 'G', 'N'), 'r3')
        elif part == 'offlist':
            setup(('pf', 'pg'), 'rw', 2)
            part_offlist(host)
        elif part == 'tags':
            part_tags(host)
        elif part == 'afters':
            part_afters(host)
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
