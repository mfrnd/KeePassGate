"""B.1 to B.6 of the family store-as-a-whole: the recommended store in real files.

Run:  uv run --no-project python -I -X utf8 cases.py PART
Parts: merges (B.1, B.3, B.4), dedupe (B.2), deletions (B.5), repair (B.6), slow (the KeePassXC cells
of `merges` again under the slow key derivation), random COUNT SEED (the bridge over random
histories, for C). Each part prints its table and writes nothing else; run.ps1 puts it into out.

Every cell: the history is played through bridge.Bridge, the cell's merge is done for real, and the
file the merge wrote is read by read.py and held to every decision any gate made (`outcome`): as
decided, narrower, refuses (and for what scope), or WIDER. Beside it, C: the steps at which the
file and the model's prediction disagreed, entry by entry (`entries`) and in what the read rule
answers (`read`).

ADDED (decided store): `cases.py --decided PART` runs a part under the store as decided on
2026-11-16 (bridge.decide: no repair, the history rule in every read), and adds the part `history`:
each kind of store entry edited by an operation of KeePass's own, read under the history rule and
under the plain stop, then merged with an unedited copy both ways.

ADDED (two readings): `cases.py --rewrite FORM PART` and `cases.py --copies VARIANT PART` run a part
under the two further configurations of bridge.py (`rewrite`, `copies`); `random` then also counts
the records written again, or the gate acts the reading of copies refused. Their own parts are in
readings.py.

ADDED (copies extended): `cases.py --reading LEVEL VARIANT PART` runs a part under bridge.extended:
the store as decided read with a wider reading of a copy (read.py's `ext`), rules per profile
switched on. `merges` then also runs the histories of PROFILE_HISTORIES, `random` also draws rules
per profile and the act `keep`, and every part ends with how each file's answer moved against the
store as decided on the same file, and how many answers gave more than the file's own records.
"""
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import kp  # noqa: E402
import read as RD  # noqa: E402
from crdt_model import words_text  # noqa: E402     ADDED (copies extended)

PROGRAMS = ([('KeePass ' + m, m) for m in B.METHODS] + [('KeePassXC ' + v, v) for v in B.RELEASES])


def merge_op(prog, d, s):
    if prog == 'Synchronize':
        return ('merge', 'kp_sync', d, s)
    if prog == 'CreateNewUuids':
        return ('merge', 'cni', d, s)
    if prog in B.METHODS:
        return ('merge', 'kp_other', d, s, prog)
    return ('merge', 'xc', d, s, prog)


def S(i):
    return ('save', i)


# B.1, B.3 and B.4: histories over two copies, each ending in every merge cell, both directions.
# Copy 1 is the copy that did not take the history's last decisions, unless both changed.
HISTORIES = [
    ('H1', 'an admission at copy 0', [('pair', 0)]),
    ('H2', 'a revocation by key at copy 0', [('revoke', 0, 'c0')]),
    ('H3', 'a revocation at gate 0; at gate 1, which has not seen it, the revoked key renewed',
     [('revoke', 0, 'c0'), ('renewcert', 1, 'c0')]),
    ('H4', 'a name added, removed and added again at copy 0; copy 1 holds the first addition',
     [('padd', 0, 'pg'), ('merge', 'kp_sync', 1, 0), ('prem', 0, 'pg'), ('padd', 0, 'pg')]),
    ('H5', 'a name removed at copy 0; copy 1 holds it', [('prem', 0, 'pf')]),
    ('H6', 'at copy 0 the authority recreated (key carried), deleted with none after, created again, a client paired',
     [('rule', 0, 'N', 'none'), ('merge', 'kp_sync', 1, 0), ('recreate', 0), ('delauth', 0), ('create', 0),
      ('padd', 0, 'pf'), ('pair', 0)]),
    ('H7', 'at copy 0 a rule set, widened, narrowed and emptied at N, and set, narrowed at the root; copy 1 holds the first',
     [('rule', 0, 'N', 'r'), ('rule', 0, 'root', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'rw'),
      ('rule', 0, 'N', 'r'), ('rule', 0, 'N', 'none'), ('rule', 0, 'root', 'r')]),
    ('H8', 'at copy 0 a rule at N set and then removed, the root emptied; copy 1 holds the rule at N',
     [('rule', 0, 'N', 'r'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'inherit'), ('rule', 0, 'root', 'none')]),
    ('H9', 'the same rule changed apart: read at copy 0, read and write at copy 1',
     [('rule', 0, 'N', 'r'), ('rule', 1, 'N', 'rw')]),
    ('H10', 'a rule narrowed at gate 0 and widened at gate 1, apart', [('rule', 0, 'N', 'none'), ('rule', 1, 'N', 'rw')]),
    ('H11', 'an admission at each gate, apart', [('pair', 0), ('pair', 1)]),
    ('H12', 'a renewal refused forever at copy 0', [('renew', 0, 'c0', 'forever')]),
    ('H13', 'B.3: a copy from before a Recreate (copy 1) and a copy after it (copy 0)',
     [('rule', 0, 'N', 'none'), ('pair', 0), ('merge', 'kp_sync', 1, 0), ('recreate', 0), ('pair', 0),
      ('rule', 0, 'N', 'r')]),
]

# ADDED (copies extended): histories with rules per profile, and two authorities standing. Run under
# --reading only, after the thirteen above. A rule is written 'pf=rw,pg=r': each profile it names
# with its words; a profile left out inherits.
WIDE = ('rule', 0, 'root', 'pf=rw,pg=rw')
SYNC = ('merge', 'kp_sync', 1, 0)
PROFILE_HISTORIES = [
    ('H14', 'a version that narrows one profile and widens the other at copy 0; copy 1 holds the version before',
     [WIDE, ('rule', 0, 'N', 'pf=rw,pg=r'), SYNC, ('rule', 0, 'N', 'pf=r,pg=rw')]),
    ('H15', 'a rule that grants nothing, then widened for one profile at copy 0; copy 1 holds the empty rule',
     [WIDE, ('rule', 0, 'N', 'pf=,pg='), SYNC, ('rule', 0, 'N', 'pf=rw,pg=')]),
    ('H16', 'a narrowing set at copy 0 on a node below a wider rule; copy 1 holds no rule of the node',
     [WIDE, SYNC, ('rule', 0, 'N', 'pf=r')]),
    ('H17', 'a rule removed at copy 0 where the node above gives less; copy 1 holds the rule',
     [('rule', 0, 'root', 'pf=r,pg=r'), ('rule', 0, 'N', 'pf=rw,pg=rw'), SYNC, ('rule', 0, 'N', 'inherit')]),
    ('H18', 'a rule removed at copy 0 where the node above gives more; copy 1 holds the rule',
     [WIDE, ('rule', 0, 'N', 'pf=r,pg='), SYNC, ('rule', 0, 'N', 'inherit')]),
    ('H19', 'a rule changed at copy 0 to other words, neither within the other; copy 1 holds the first',
     [WIDE, ('rule', 0, 'N', 'pf=w,pg=rw'), SYNC, ('rule', 0, 'N', 'pf=r,pg=rw')]),
    ('H20', 'a rule narrowed for one profile at gate 0 and widened for the other at gate 1, apart',
     [WIDE, ('rule', 0, 'N', 'pf=rw,pg=r'), SYNC, ('rule', 0, 'N', 'pf=r,pg=r'), ('rule', 1, 'N', 'pf=rw,pg=rw')]),
    ('H21', 'at copy 0 a rule that names one profile less, which inherits again below a narrower rule, and empties the other; copy 1 holds the rule',
     [('rule', 0, 'root', 'pf=r,pg=r'), ('rule', 0, 'N', 'pf=rw,pg=rw'), SYNC, ('rule', 0, 'N', 'pg=')]),
    ('H22', 'both gates recreate the authority apart, so two stand; gate 0 keeps its own; copy 1 holds both standing',
     [('recreate', 0), ('recreate', 1), ('merge', 'kp_sync', 0, 1), SYNC, ('keep', 0)]),
]
PP_SPECS = ('pf=r,pg=rw', 'pf=rw,pg=', 'pf=', 'pg=r', 'pf=w,pg=w')

# H14: a user node under a rule, moved, copied, deleted and restored (real files only).
TREE = [
    ('T1', 'N moved to the group Other in copy 1', [('move', 1, 'Other')]),
    ('T2', 'N duplicated in copy 1 (a copy beside it)', [('dup', 1, 'cd000000000000000000000000000001')]),
    ('T3', 'N deleted to the bin in copy 1', [('tbin', 1)]),
    ('T4', 'N deleted to the bin and restored in copy 1', [('tbin', 1), ('restore', 1)]),
    ('T5', 'N deleted for good in copy 1', [('tdel', 1)]),
]


def run_cell(host, hist, prog, d, s, tag, speed='fast'):
    ops = list(hist) + ([('kdf', speed)] if speed != 'fast' else []) + [merge_op(prog, d, s)]
    b = B.Bridge(host, 2, tag)
    for k, op in enumerate(ops):
        if op[0] == 'kdf':
            for f in b.files:
                host.run('kdf', f, op[1])
            b.log.append('%2d both copies saved under the %s key derivation' % (k, op[1]))
            continue
        if op[0] in ('tbin', 'tdel'):
            t = b.W.now()
            host.run('bin' if op[0] == 'tbin' else 'delrec', b.files[op[1]], b.ids['N'], t)
            b.log.append('%2d %s (a user node)' % (k, ' '.join(map(str, op))))
            continue
        if not b.step(k, op):
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b


def c_note(b):
    return 'entries %d, read %d' % (len(b.disagree), len(b.readdiff))


SLOW = ('H3', 'H6', 'H13')     # the histories `slow` runs again
SLOW_TREE = ('T2',)


def part_merges(host, speed='fast', only_xc=False):
    print('== B.1, B.3, B.4: every history, every merge, both directions; the file the merge wrote,')
    print('   read by read.py and held to every decision any gate made. C: steps where the file and the')
    print('   model disagreed, entry by entry and in the read. Key derivation: %s' % speed)
    if only_xc:
        print('   Only the KeePassXC cells, of the histories %s and %s.' % (', '.join(SLOW), ', '.join(SLOW_TREE)))
    tally = {}
    # ADDED (copies extended): the histories with rules per profile, under --reading only
    for hid, what, hist in HISTORIES + (PROFILE_HISTORIES if B.EXT else []):
        if only_xc and hid not in SLOW:
            continue
        print('## %s: %s' % (hid, what))
        print('   history: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
        for label, prog in PROGRAMS:
            if only_xc and prog not in B.RELEASES:
                continue
            cells = []
            for d, s in ((1, 0), (0, 1)):
                b = run_cell(host, hist, prog, d, s, 'm-' + speed, speed)
                o, r = B.outcome(b, d)
                cells.append('into %d: %s [%s]' % (d, o, c_note(b)))
                tally[B.cat(o)] = tally.get(B.cat(o), 0) + 1     # ADDED (copies extended): cat
                for x in b.disagree:
                    cells.append('   C entries, step %d, file %d: %s' % (x[0], x[1], '; '.join(x[2])))
                for x in b.readdiff:
                    cells.append('   C read, step %d, file %d: %s' % x)
                if o.startswith('WIDER'):
                    cells.append('   ' + B.view(r))
                    cells += ['   ' + x for x in b.log]
            print('   %-22s %s' % (label, cells[0]))
            for c in cells[1:]:
                print('   %-22s %s' % ('', c))
    print('== tally: ' + ', '.join('%s %d' % kv for kv in sorted(tally.items())))
    print('== B.1 for a user node under a rule (real files only; the model has no tree)')
    base = [('rule', 0, 'N', 'none'), ('rule', 0, 'root', 'rw'), ('merge', 'kp_sync', 1, 0)]
    for tid, what, ops in TREE:
        if only_xc and tid not in SLOW_TREE:
            continue
        print('## %s: %s' % (tid, what))
        for label, prog in PROGRAMS:
            if only_xc and prog not in B.RELEASES:
                continue
            cells = []
            for d, s in ((0, 1), (1, 0)):
                b = run_cell(host, base + ops, prog, d, s, 't-' + speed, speed)
                cells.append('into %d: %s' % (d, tree_view(b, d)))
            print('   %-22s %s' % (label, ' | '.join(cells)))


def tree_view(b, i):
    dump = b.dump(i)
    r = B.rd(dump, B.DECIDED)   # ADDED (two readings): rd
    out = []
    nid = b.ids['N']
    for u, e in sorted(dump['entries'].items()):
        if e['title'] == 'N':
            where = 'the bin' if e['place'].startswith('B') else dump['groups'].get(e['parent'], {}).get('name', 'Root')
            out.append('%s in %s gives %s' % ('N' if u == nid else 'a copy of N', where,
                                               words_text(r['words'].get(u, frozenset())) or 'nothing'))
    if not any(e['title'] == 'N' for e in dump['entries'].values()):
        out.append('N gone')
    # sorted by what it reads, since a copy's identifier is drawn at random
    return '%s; %s' % (r['status'], ', '.join(sorted(out)))


# B.2: Create new IDs, then Delete Duplicate Entries, in both orders of copies, then a third copy.
def part_dedupe(host):
    print('== B.2: Create new IDs, then Delete Duplicate Entries, both orders; then a third copy')
    print('   synchronized with the result. Copy 0 holds the newer decisions, copy 1 is stale.')
    hist = [('rule', 0, 'N', 'none'), ('revoke', 0, 'c0'), ('pair', 0)]
    for d, s in ((0, 1), (1, 0)):
        b = B.Bridge(host, 3, 'dd')
        ops = hist + [('merge', 'cni', d, s), ('dedupe', d), ('merge', 'kp_sync', 2, d)]
        for k, op in enumerate(ops):
            if not b.step(k, op):
                b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
        print('## import into copy %d from copy %d, then Delete Duplicate Entries there' % (d, s))
        for x in b.log:
            print('   ' + x)
        for i in (d, 2):
            o, r = B.outcome(b, i)
            print('   file %d: %s | %s | %s' % (i, o, B.view(r), r['counts']))
        print('   C: ' + c_note(b))
        for x in b.disagree:
            print('   C entries, step %d, file %d: %s' % (x[0], x[1], '; '.join(x[2])))
        for x in b.readdiff:
            print('   C read, step %d, file %d: %s' % x)


# B.5: each kind of record deleted by hand, each way; the read; Seal again; a stale copy merged.
KINDS = [('ref', 'revoked'), ('ref', 'retired'), ('rec',), ('auth',), ('padd',), ('prem',),
         ('ver', 'rule:N', 'new'), ('ver', 'renew:c0', 'new'), ('head', 'new')]
WAYS = [('bin', 'to the recycle bin'), ('rec', 'for good, with a record'), ('none', 'for good, no record'),
        ('empty', 'to the bin, the bin emptied')]


def part_deletions(host):
    print('== B.5: each kind of record deleted by hand at copy 0, each way; then what the read rule')
    print('   answers; then Seal again (without the mark and the witness) when the store is')
    print('   incomplete, and what the store then holds; then a merge with copy 1, a stale copy that')
    print('   still holds what was deleted, both ways, by Synchronize and by KeePassXC 2.8.0.')
    hist = [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
            ('rule', 0, 'N', 'none'), ('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0)]
    print('   history: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
    for sel in KINDS:
        for way, words in WAYS:
            row = []
            for mi, (d, s, prog) in enumerate(((0, 1, 'Synchronize'), (1, 0, 'Synchronize'),
                                                (0, 1, '2.8.0'), (1, 0, '2.8.0'))):
                b = B.Bridge(host, 2, 'del')
                ops = list(hist)
                if way == 'bin':
                    ops.append(('bin', 0, sel))
                elif way == 'rec':
                    ops.append(('del', 0, sel, 1))
                elif way == 'none':
                    ops.append(('del', 0, sel, 0))
                else:
                    ops += [('bin', 0, sel), ('empty', 0)]
                for k, op in enumerate(ops):
                    if not b.step(k, op):
                        b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
                o1, r1 = B.outcome(b, 0)
                sealed = ''
                if r1['status'] != 'ok':
                    k = len(ops)
                    if b.step(k, ('reseal', 0)):
                        o2, r2 = B.outcome(b, 0)
                        sealed = '%s (holds %d elements, %d heads)' % (o2, r2['counts']['elements'], r2['counts']['heads'])
                    else:
                        sealed = 'Seal again does not apply'
                    ops.append(('reseal', 0))
                k = len(ops)
                b.step(k, merge_op(prog, d, s))
                o3, r3 = B.outcome(b, d)
                if mi == 0:
                    row.append('after the deletion: %s' % o1)
                    row.append('Seal again: %s' % (sealed or 'not needed'))
                row.append('%s into %d: %s [%s]' % ('Synchronize' if prog == 'Synchronize' else 'KeePassXC 2.8.0', d, o3, c_note(b)))
                for x in b.disagree:
                    row.append('   C entries, step %d, file %d: %s' % (x[0], x[1], '; '.join(x[2])))
                for x in b.readdiff:
                    row.append('   C read, step %d, file %d: %s' % x)
                if o3.startswith('WIDER') or o1.startswith('WIDER'):
                    row.append('   ' + B.view(r3))
                    row += ['   ' + x for x in b.log]
            print('## %s, %s' % (' '.join(sel), words))
            for x in row:
                print('   ' + x)


def run_ops(host, nrep, tag, ops, **kw):
    b = B.Bridge(host, nrep, tag, **kw)
    for k, op in enumerate(ops):
        if not b.step(k, op):
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b


def c_lines(b, indent='   '):
    out = []
    for x in b.disagree:
        out.append('%sC entries, step %d, file %d: %s' % (indent, x[0], x[1], '; '.join(x[2])))
    for x in b.readdiff:
        out.append('%sC read, step %d, file %d: %s' % ((indent,) + x))
    return out


# Added from the desktop measurement (1): every deletion record removed at once, as Database
# Maintenance's "Deleted objects information", Delete does, after each kind of deletion of B.5 and
# after a prune; the read; then a merge with a stale copy that still holds what was deleted.
CLEAR_MERGES = [(0, 1, 'Synchronize'), (1, 0, 'Synchronize'), (0, 1, 'OverwriteIfNewer'),
                (0, 1, '2.8.0'), (1, 0, '2.8.0')]
PRUNES = [
    ('P1', 'a rule narrowed (the older version and head pruned)', [('rule', 0, 'N', 'rw')], [('rule', 0, 'N', 'none')]),
    ('P2', 'a client revoked (its record pruned beside the revocation)', [], [('revoke', 0, 'c0')]),
    ('P3', 'the authority recreated (the retired document pruned)', [], [('recreate', 0)]),
    ('P4', 'a name removed (its addition pruned)', [], [('prem', 0, 'pf')]),
    ('P5', 'renewals refused, then allowed again (the refusal pruned)', [('renew', 0, 'c0', 'forever')],
     [('renew', 0, 'c0', 'allowed')]),
    ('P6', 'the authority deleted, none after (the document pruned, the key in the retirement)', [], [('delauth', 0)]),
]


def part_clear(host):
    print('== Added from the desktop measurement: every deletion record removed at once (Database')
    print('   Maintenance, Deleted objects information, Delete), then the read, then a merge with a stale')
    print('   copy that still holds what was deleted: Synchronize both ways, Overwrite if newer into 0,')
    print('   KeePassXC 2.8.0 both ways. No Seal again before the merge.')
    hist = [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
            ('rule', 0, 'N', 'none'), ('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0)]
    print('## after each kind of deletion of B.5; history: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
    for sel in KINDS:
        for way, words in WAYS:
            dele = {'bin': [('bin', 0, sel)], 'rec': [('del', 0, sel, 1)], 'none': [('del', 0, sel, 0)],
                    'empty': [('bin', 0, sel), ('empty', 0)]}[way]
            row = []
            for mi, (d, s, prog) in enumerate(CLEAR_MERGES):
                b = run_ops(host, 2, 'clr', hist + dele + [('clear', 0), merge_op(prog, d, s)])
                if mi == 0:
                    b0 = run_ops(host, 2, 'clr0', hist + dele + [('clear', 0)])
                    row.append('after the deletion and the clearing: %s' % B.outcome(b0, 0)[0])
                o, r = B.outcome(b, d)
                row.append('%s into %d: %s [%s]' % (prog if prog in B.METHODS else 'KeePassXC ' + prog, d, o, c_note(b)))
                row += c_lines(b)
                if o.startswith('WIDER'):
                    row.append('   ' + B.view(r))
                    row += ['   ' + x for x in b.log]
            print('## %s, %s, then every record removed' % (' '.join(sel), words))
            for x in row:
                print('   ' + x)
    print('## after a prune: copy 1 taken before the act, the act at copy 0, every record removed there')
    for pid, what, pre, act in PRUNES:
        row = []
        base = pre + [('merge', 'kp_sync', 1, 0)] + act
        b0 = run_ops(host, 2, 'clr0', base + [('clear', 0)])
        row.append('after the clearing: %s' % B.outcome(b0, 0)[0])
        for d, s, prog in CLEAR_MERGES:
            b = run_ops(host, 2, 'clr', base + [('clear', 0), merge_op(prog, d, s)])
            o, r = B.outcome(b, d)
            row.append('%s into %d: %s [%s]' % (prog if prog in B.METHODS else 'KeePassXC ' + prog, d, o, c_note(b)))
            row += c_lines(b)
            if o.startswith('WIDER'):
                row.append('   ' + B.view(r))
                row += ['   ' + x for x in b.log]
        print('## %s: %s; history: %s' % (pid, what, ' ; '.join(' '.join(map(str, op)) for op in base)))
        for x in row:
            print('   ' + x)


# Added from the desktop measurement (2): Delete Empty Groups and Delete Duplicate Entries on the
# store as it normally sits, and after what can give it two entries with equal content.
def part_dupes(host):
    print('== Added from the desktop measurement: Delete Empty Groups and Delete Duplicate Entries')
    hist = [('revoke', 0, 'c0'), ('pair', 0), ('prem', 0, 'pf'), ('padd', 0, 'pg'), ('rule', 0, 'N', 'none'),
            ('renew', 0, 'c0', 'forever')]
    print('   history at copy 0: ' + ' ; '.join(' '.join(map(str, op)) for op in hist))
    cases = [('the store as it sits: Delete Empty Groups', [('deg', 0)]),
             ('the store as it sits: Delete Duplicate Entries', [('dde', 0)]),
             ('two gates revoke the same client apart, Synchronize, Delete Duplicate Entries',
              [('merge', 'kp_sync', 1, 0), ('revoke', 0, 'c1'), ('revoke', 1, 'c1'), ('merge', 'kp_sync', 0, 1),
               ('dde', 0)]),
             ('Duplicate Group of the plugin\'s group with " - Copy", Delete Duplicate Entries',
              [('dupgroup', 0, True), ('dde', 0)]),
             ('Duplicate Group of the plugin\'s group without the suffix, Delete Duplicate Entries',
              [('dupgroup', 0, False), ('dde', 0)])]
    for sel in KINDS:
        cases.append(('Duplicate Entry of %s without the suffix, Delete Duplicate Entries' % ' '.join(sel),
                      [('dupel', 0, sel), ('dde', 0)]))
    for what, ops in cases:
        pre = hist
        b = run_ops(host, 2, 'dup', pre + ops)
        o, r = B.outcome(b, 0)
        print('## %s' % what)
        print('   read: %s | %s | %s' % (o, B.view(r), r['counts']))
        for x in b.log:
            if x.startswith('   '):
                print('   ' + x.strip())
        if r['status'] != 'ok':
            k = len(pre + ops)
            if b.step(k, ('reseal', 0)):
                o2, r2 = B.outcome(b, 0)
                print('   Seal again: %s | %s | %s' % (o2, B.view(r2), r2['counts']))
            else:
                print('   Seal again does not apply (%s)' % r['status'])
        print('   C: ' + c_note(b))
        for x in c_lines(b):
            print(x)


# B.6: the repair after a Synchronize, with and without it.
REPAIR = [
    ('R1', 'records cleared after an unsettled loss, then the stale copy synchronized at gate 0 (row 4)',
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('del', 0, ('ver', 'rule:N', 'new'), 1),
      ('del', 0, ('head', 'new'), 1), ('clear', 0), ('merge', 'kp_sync', 0, 1)]),
    ('R2', 'copy 1 removes a narrowed rule\'s new version and head by script after a KeePassXC merge; gate 0 synchronizes with it',
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('merge', 'xc', 1, 0, '2.8.0'),
      ('del', 1, ('ver', 'rule:N', 'new'), 0), ('del', 1, ('head', 'new'), 0), ('merge', 'kp_sync', 0, 1)]),
    ('R3', 'a revocation\'s entry and head deleted in copy 1 with records dated before them; gate 0 synchronizes',
     [('revoke', 0, 'c0'), ('merge', 'kp_sync', 1, 0), ('del', 1, ('ref', 'revoked'), 2), ('del', 1, ('head', 'new'), 2),
      ('merge', 'kp_sync', 0, 1)]),
    ('R4', 'a name removal\'s entry and head deleted in copy 1 by script; gate 0 synchronizes',
     [('prem', 0, 'pf'), ('merge', 'kp_sync', 1, 0), ('del', 1, ('prem',), 0), ('del', 1, ('head', 'new'), 0),
      ('merge', 'kp_sync', 0, 1)]),
    ('R5', 'a widening decided at gate 1, which saw the narrowing; gate 0 synchronizes (the repair\'s cost)',
     [('rule', 0, 'N', 'none'), ('merge', 'kp_sync', 1, 0), ('rule', 1, 'N', 'rw'), ('merge', 'kp_sync', 0, 1)]),
    ('R6', 'a name added again at gate 1, which saw its removal; gate 0 synchronizes (the repair\'s cost)',
     [('prem', 0, 'pf'), ('merge', 'kp_sync', 1, 0), ('padd', 1, 'pf'), ('merge', 'kp_sync', 0, 1)]),
    ('R7', 'renewals allowed again at gate 1, which saw the refusal; gate 0 synchronizes (the repair\'s cost)',
     [('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0), ('renew', 1, 'c0', 'allowed'), ('merge', 'kp_sync', 0, 1)]),
    ('R8', 'a version granting read and write at N made with the store key in a current copy (row 9); gate 0 synchronizes',
     [('rule', 0, 'N', 'none'), ('merge', 'kp_sync', 1, 0), ('plantk', 1, 'rule'), ('merge', 'kp_sync', 0, 1)]),
]


def part_repair(host, extra=()):
    print('== B.6: the repair after a Synchronize at a gate. Each history ends with KeePass\'s')
    print('   Synchronize at gate 0. Modes: the repair written inside the save, and the merged')
    print('   database then written to the other file as well (as KeePass writes both; read, not run);')
    print('   the same with the other file written before the repair; and no repair (the control).')
    print('   Added from the desktop measurement: a Synchronize with the trigger system off is announced')
    print('   to no plugin, so it is the mode "no repair, both files"; a repair made at the save\'s own')
    print('   event is the mode "repair, both files". File, Import raises no event: the same merge as an')
    print('   import with each method, the open file only, without the repair and with the repair made')
    print('   at the person\'s next save against the state the plugin held before.')
    modes = [('repair, both files', dict(repair=True, both='after'), None),
             ('repair, the other file before it', dict(repair=True, both='before'), None),
             ('no repair, both files', dict(repair=False, both='after'), None)]
    for m in B.METHODS:
        modes.append(('import with %s, no repair' % m, dict(repair=True, both='after'), (m, False)))
        modes.append(('import with %s, the repair at the next save' % m, dict(repair=True, both='after'), (m, True)))
    if B.DECIDED:   # ADDED (decided store): no repair runs anywhere; the modes it decides are left out
        print('   The store as decided: no repair. Only the modes without it are run.')
        modes = [(m, dict(kw, repair=False), imp) for m, kw, imp in modes
                 if not kw['repair'] or (imp is not None and not imp[1])]
    for rid, what, ops0 in list(REPAIR) + list(extra):
        print('## %s: %s' % (rid, what))
        print('   history: ' + ' ; '.join(' '.join(map(str, op)) for op in ops0))
        for mode, kw, imp in modes:
            ops = list(ops0)
            if imp is not None:
                last = ops[-1]
                ops[-1] = ('kimport', last[2], last[3], imp[0], imp[1])
            b = B.Bridge(host, 2, 'rep', **kw)
            for k, op in enumerate(ops):
                if not b.step(k, op):
                    b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
            res = []
            for i in (0, 1):
                o, r = B.outcome(b, i)
                res.append('file %d: %s | %s' % (i, o, B.view(r)))
            print('   %s [%s]' % (mode, c_note(b)))
            for x in res:
                print('      ' + x)
            for x in b.log:
                if x.startswith('   '):
                    print('      ' + x.strip())
            for x in b.disagree:
                print('      C entries, step %d, file %d: %s' % (x[0], x[1], '; '.join(x[2])))
            for x in b.readdiff:
                print('      C read, step %d, file %d: %s' % x)


# C over random histories: the bridge, as the survival critique ran it, on the store as decided.
def alphabet(nrep):
    import model_ext as X
    acts, _, _ = X.alphabet(nrep, [], [], B.FLAGS)
    acts = [a for a in acts if a[0] not in ('quit',)]
    R = range(nrep)
    merges = []
    for d in R:
        for s in R:
            if d != s:
                merges.append(('merge', 'kp_sync', d, s))
                merges += [('merge', 'kp_other', d, s, m) for m in ('OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting')]
                merges += [('merge', 'xc', d, s, v) for v in B.RELEASES]
    if B.EXT:       # ADDED (copies extended): rules per profile, and keeping a single authority of several
        for r in R:
            acts += [('rule', r, n, d) for n in ('root', 'N') for d in PP_SPECS] + [('keep', r)]
    faults = []
    for r in R:
        faults += [('del', r, s, k) for s in X.SELS for k in (0, 1, 2)]
        faults += [('bin', r, s) for s in X.SELS]
        faults += [('touch', r, s) for s in X.SELS]
        faults += [('edit', r, s) for s in X.SELS[:4]]
        faults += [('empty', r), ('clear', r), ('reseal', r)]
        faults += [('import', d, r) for d in R if d != r]
    return acts, merges, faults


def part_random(host, count, seed):
    import crdt_model as M
    rng = random.Random(seed)
    t0 = time.time()
    nsteps, ndis, nread, nfail = 0, 0, 0, 0
    lines, outs = [], {}
    nrewrites, nstops = 0, 0    # ADDED (two readings)
    for h in range(count):
        nrep = 2 if rng.random() < 0.6 else 3
        acts, merges, faults = alphabet(nrep)
        ops = []
        for _ in range(rng.randint(4, 9)):
            x = rng.random()
            ops.append(rng.choice(acts if x < 0.4 else merges if x < 0.7 else faults))
        b = B.Bridge(host, nrep, 'rnd')
        fails = []
        for k, op in enumerate(ops):
            if not b.step(k, op):
                continue
            nsteps += 1
            for i in range(nrep):
                w = M.narrower(b.W.fold(b.W.reps[i]), b.W.ideal(i), b.W.reps[i].nodes)
                if w:
                    fails.append(('safety', k, 'file %d: %s' % (i, w)))
            if fails:
                break
        for i in range(nrep):
            o, r = B.outcome(b, i)
            outs[B.cat(o)] = outs.get(B.cat(o), 0) + 1       # ADDED (copies extended): cat
        nrewrites, nstops = nrewrites + len(b.rewrites), nstops + b.copy_stops     # ADDED (two readings)
        if b.disagree or b.readdiff or fails:
            lines.append('## history %d (%d files)' % (h, nrep))
            lines += ['   ' + x for x in b.log]
            for step, i, diffs in b.disagree:
                ndis += 1
                lines.append('   DISAGREE at step %d, file %d: %s' % (step, i, '; '.join(diffs)))
            for x in b.readdiff:
                nread += 1
                lines.append('   READ DIFFERS at step %d, file %d: %s' % x)
            for f in fails:
                nfail += 1
                lines.append('   FAIL %s at step %d: %s' % f)
    print('random histories: %d, steps done: %d, steps where a file disagreed with the model: %d, '
          'steps where the read differed: %d, failures of the model\'s safety check: %d, %.0f s'
          % (count, nsteps, ndis, nread, nfail, time.time() - t0))
    print('outcomes of every file at the end, against every decision: ' + ', '.join('%s %d' % kv for kv in sorted(outs.items())))
    if B.REWRITE:   # ADDED (two readings)
        print('records written again at the gate\'s own writes: %d' % nrewrites)
    if B.COPIES:    # ADDED (two readings)
        print('gate acts refused because the reading of copies stops the store: %d' % nstops)
    for x in lines:
        print(x)


# ADDED (decided store): the history rule with the summary entries. Each kind of store entry in
# turn is edited at copy 0 by an operation of KeePass's own, as KeePassLib does them; copy 1 is the
# same database unedited. Then: copy 0 read as it is; each copy merged with the other, by
# Synchronize and by KeePassXC 2.8.0, both ways; and copy 0 after the gate writes again. Each is
# read under the history rule and, beside it, under the plain stop the run with the repair had.
HIST_BASE = [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
             ('rule', 0, 'N', 'none'), ('renew', 0, 'c1', 'forever'), ('merge', 'kp_sync', 1, 0)]
# The retirement that holds the store key: the authority deleted with none after it (change 6).
HIST_BASE_KEY = HIST_BASE[:-1] + [('delauth', 0), ('merge', 'kp_sync', 1, 0)]
# kind, which entry, the field of KPG.Data whose first character the dialog edit changes, base
HIST_KINDS = [
    ('the authority', ('auth',), 'id', HIST_BASE),
    ('a retirement (from Recreate)', ('ref', 'retired'), 'authority', HIST_BASE),
    ('an admission record (c1)', ('rec',), 'client', HIST_BASE),
    ('a revocation (c0)', ('ref', 'revoked'), 'client', HIST_BASE),
    ('a profile addition (pg)', ('padd',), 'name', HIST_BASE),
    ('a profile removal (pf)', ('prem',), 'name', HIST_BASE),
    ('a rule version (N, nothing)', ('ver', 'rule:N', 'new'), 'object', HIST_BASE),
    ('a renewal answer (c1, forever)', ('ver', 'renew:c1', 'new'), 'object', HIST_BASE),
    ('the summary entry', ('head', 'new'), 'height', HIST_BASE),
    ('the retirement that holds the store key', ('carry',), 'authority', HIST_BASE_KEY),
]
# operation, what it does: a list of history-edit kinds, or a tree operation; 'off' first sets no history
HIST_OPS = [
    ('dialog', 'a character of the payload changed in the entry dialog', ['char']),
    ('field', 'the payload string deleted in the entry dialog', ['field']),
    ('quick', 'a quick edit, Expires: Now (nothing the check covers)', ['quick']),
    ('move', 'moved to the group General', 'move'),
    ('bin', 'deleted to the recycle bin', 'bin'),
    ('delete', 'deleted for good, with a deletion record', 'delrec'),
    ('edits10', 'the dialog edit and 9 more dialog edits (of the title): 10 edits', ['char'] + ['title'] * 9),
    ('edits11', 'the dialog edit and 10 more dialog edits (of the title): 11 edits', ['char'] + ['title'] * 10),
    ('quick10', 'the dialog edit and 10 quick edits: 11 edits', ['char'] + ['quick'] * 10),
    ('off', 'history kept by neither copy (0 items), then the dialog edit', ['char']),
]
HIST_MERGES = [('Synchronize into 1', 1, 0, 'Synchronize'), ('Synchronize into 0', 0, 1, 'Synchronize'),
               ('KeePassXC 2.8.0 into 1', 1, 0, '2.8.0'), ('KeePassXC 2.8.0 into 0', 0, 1, '2.8.0')]


def hist_pick(b, sel):
    if sel == ('carry',):
        W = b.W
        c = sorted(eid for u, (eid, mt, ok) in W.reps[0].entries.items()
                   if u == eid and W.EL[eid].kind == 'ref' and W.EL[eid].carry)
        return c[-1] if c else None
    return b.W.pick(b.W.reps[0], sel)


def hist_cell(b, i, key_carrier):
    """The history rule's outcome, the plain stop's, and the entry counts; with the retirement that
    holds the key, then Create at that file and the outcome after it."""
    o, r = B.outcome(b, i, history=True)
    p, _ = B.outcome(b, i, history=False)
    txt = '%s [store %s, by history %d] | plain stop: %s' % (o, r['status'], r['counts']['byhistory'], p)
    cat = o.split(':')[0]
    if key_carrier:
        b.sync_file(i)
        if b.step(90 + i, ('create', i)):
            o2, r2 = B.outcome(b, i, history=True)
            txt += ' || then Create at %d: %s [keys %d]' % (i, o2, r2['counts']['keys'])
            cat = o2.split(':')[0]
        else:
            txt += ' || then Create at %d: does not apply' % i
            cat = 'Create does not apply'
    return txt, cat


def part_history(host):
    print('== B of the decided store: the history rule with the summary entries. Each kind of store')
    print('   entry edited at copy 0 by one operation; copy 1 the same database unedited. Columns: copy')
    print('   0 as it is; each merge with the other copy; copy 0 after the gate writes again. Each read')
    print('   under the history rule, and beside it under the plain stop. With the retirement that')
    print('   holds the store key no authority is served, so each read is followed by Create there.')
    print('   History limits: KeePass\'s defaults (10 items, 6 MiB) unless the operation names 0.')
    print('   base: ' + ' ; '.join(' '.join(map(str, op)) for op in HIST_BASE))
    print('   base of the last kind: ' + ' ; '.join(' '.join(map(str, op)) for op in HIST_BASE_KEY))
    tally, per_kind = {}, {}
    for kind_name, sel, field, base in HIST_KINDS:
        key_carrier = sel == ('carry',)
        print('## %s' % kind_name)
        for oid, what, edits in HIST_OPS:
            b = B.Bridge(host, 2, 'hst')
            for k, op in enumerate(base):
                if not b.step(k, op):
                    b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
            f = b.files
            if oid == 'off':
                for x in f:
                    host.run('limits', x, 0)
            eid = hist_pick(b, sel)
            u = b.uid(eid)
            t = b.W.t + 10
            if edits == 'move':
                out = host.run('move', f[0], u, 'General', t)
            elif edits == 'bin':
                out = host.run('bin', f[0], u, t)
            elif edits == 'delrec':
                out = host.run('delrec', f[0], u, t)
            else:
                for e in edits:
                    out = host.run('history-edit', f[0], u, t, e, field)
                    t += 60
            b.W.t = t + 10
            b.sync_file(0)
            e0, e1 = f[0] + '.edited', f[1] + '.edited'
            kp.copy(f[0], e0)
            kp.copy(f[1], e1)
            W0 = b.W.clone()
            enc0 = (dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))

            def restore():
                kp.copy(e0, f[0])
                kp.copy(e1, f[1])
                b.W = W0.clone()
                b.enc.W = b.W
                b.enc.uuid, b.enc.eid, b.macs = dict(enc0[0]), dict(enc0[1]), dict(enc0[2])

            hist = {x: len(e.get('history', [])) for x, e in host.dump(f[0], history=True)['entries'].items()}
            rows = []
            txt, cat = hist_cell(b, 0, key_carrier)
            rows.append(('copy 0 as it is', txt, cat))
            for label, d, s, prog in HIST_MERGES:
                restore()
                b.step(20, merge_op(prog, d, s))
                txt, cat = hist_cell(b, d, key_carrier)
                rows.append((label, txt, cat))
            restore()
            b.sync_file(0)
            if key_carrier:
                rows.append(('copy 0, the gate writes again', '(the Create above)', None))
            else:
                ok = b.step(30, ('padd', 0, 'ph'))
                o, r = B.outcome(b, 0, history=True)
                rows.append(('copy 0, the gate writes again',
                             '%s%s [store %s, by history %d]' % ('' if ok else 'cannot write; ', o, r['status'],
                                                                 r['counts']['byhistory']),
                             o.split(':')[0]))
            print('   %s: %s (%s; history items of the entry at copy 0: %s)' % (
                oid, what, out, hist.get(u, 'gone')))
            for label, txt, cat in rows:
                print('      %-30s %s' % (label, txt))
                if cat:
                    tally[cat] = tally.get(cat, 0) + 1
                    per_kind.setdefault(kind_name, {})[cat] = per_kind.get(kind_name, {}).get(cat, 0) + 1
            for x in c_lines(b, '      '):
                print(x)
            if b.by_history:
                print('      the model read %d entries as damaged that the file counts by history' % b.by_history)
    print('== tally of every cell under the history rule (after Create for the last kind): '
          + ', '.join('%s %d' % kv for kv in sorted(tally.items())))
    for kind_name, c in per_kind.items():
        print('   %-42s %s' % (kind_name, ', '.join('%s %d' % kv for kv in sorted(c.items()))))


def main():
    # ADDED (decided store): --decided runs a part under the store as decided (bridge.decide)
    if sys.argv[1] == '--decided':
        B.decide()
        del sys.argv[1]
    # ADDED (two readings): --rewrite FORM and --copies VARIANT, each the store as decided as well
    elif sys.argv[1] == '--rewrite':
        B.rewrite(sys.argv[2])
        del sys.argv[1:3]
    elif sys.argv[1] == '--copies':
        B.copies(sys.argv[2])
        del sys.argv[1:3]
    # ADDED (copies extended): --reading LEVEL VARIANT
    elif sys.argv[1] == '--reading':
        B.extended(sys.argv[2], sys.argv[3])
        del sys.argv[1:4]
    part = sys.argv[1]
    sys.stdout.reconfigure(line_buffering=True)
    host = kp.Host()
    t0 = time.time()
    try:
        if part == 'merges':
            part_merges(host)
        elif part == 'slow':
            part_merges(host, 'slow', only_xc=True)
        elif part == 'dedupe':
            part_dedupe(host)
        elif part == 'deletions':
            part_deletions(host)
        elif part == 'repair':
            part_repair(host)
        elif part == 'clear':
            part_clear(host)
        elif part == 'dupes':
            part_dupes(host)
        elif part == 'random':
            part_random(host, int(sys.argv[2]), int(sys.argv[3]))
        elif part == 'history' and B.DECIDED:
            part_history(host)
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    for x in B.moved():     # ADDED (copies extended)
        print(x)
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
