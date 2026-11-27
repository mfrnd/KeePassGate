"""ADDED (seal again): Seal again on real files, as PLAN 3.1 of the pull request that builds it
designs it, STORE.md 624 and 625, before anything of it is built in the product.

Run:  uv run --no-project python -I -X utf8 seal.py PART

Every part plays its sequences through bridge.Bridge under bridge.seal(): the store as decided
with the standing answers to a renewal and the reading of lost versions of `measured-lost-settled`
(bridge.answers()), and crdt_model.SEAL on: Create refused on a stopped store while a store key
stands, the seal's summary entry naming every damaged summary entry it deletes, and read.py's
listing of what a stopped store lost (read.SEAL). Every gate act is the model's write path, written
into a real .kdbx by KeePassLib 2.62; every edit, deletion and merge is KeePassLib's or
keepassxc-cli's; every file is read by read.py and held to every decision any gate made
(bridge.outcome) and to its own ideal.

Parts:
  edits    PLAN 4.1 item 1: each kind of record edited past KeePass's history limit (eleven dialog
           edits, a dialog edit and ten quick edits) and with no history kept, then Seal again, then
           each merge of KeePassLib's five methods and KeePassXC 2.8.0 both ways with the unedited
           copy; and a version and the summary entry both edited past the limit
  cells    R1 to R19: each way a store stops, Seal again, a seal beside an act in a single write, a
           seal composed over a store that changed since it was shown, Create on a stopped store,
           and the accepted class of STORE.md 639 row 5, each followed by every merge both ways by
           KeePassLib's five methods and four keepassxc-cli releases
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import crdt_model as M  # noqa: E402

INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)
XC_INTO_0, XC_INTO_1 = ('merge', 'xc', 0, 1, '2.8.0'), ('merge', 'xc', 1, 0, '2.8.0')


def seq(ops):
    return ' ; '.join(' '.join(map(str, op)) for op in ops)


def base(rows=False):
    """At copy 0: c0 revoked, the authority recreated, c1 paired, the name pg added and pf removed,
    N's rule emptied (rows: each profile's row at N an empty rule), c1's renewals refused forever;
    then synchronized into copy 1, which is the copy from before."""
    return [('revoke', 0, 'c0'), ('recreate', 0), ('pair', 0), ('padd', 0, 'pg'), ('prem', 0, 'pf'),
            ('rule', 0, 'N', 'none'), ('renew', 0, 'c1', 'forever'), INTO_1]


def rule_act(gate, letters, rows):
    """A rule set at N: the whole rule, or with rows pf's row."""
    if rows:
        return ('row', gate, 'N', 'pf', letters)
    return ('rule', gate, 'N', {'rw': 'rw', 'r': 'r', '': 'none'}[letters])


def histories(rows=False):
    """(label, what, ops, then): the acts up to the merges, and the acts that follow in the copy
    merged into, 'D' standing for that copy. With `rows` the sequences of a row and of the switch
    are played on a row, as the vectors play them; without, on N's whole rule, as on files."""
    B0 = base(rows)
    HEADDEL = ('del', 0, ('head', 'new'), 1)
    VERDEL = ('del', 0, ('ver', 'rule:N', 'new'), 1)
    pair = [('pair', 'D')]
    out = [
        ('R1', 'the only summary entry in force deleted with a deletion record: no summary entry in force; '
               'Seal again restates every record present',
         B0 + [HEADDEL, ('sealsee', 0), ('reseal', 0)], pair),
        ('R2', 'a summary entry made apart in each copy and synchronized; at copy 0 one of the two deleted '
               'with a deletion record: a summary entry deleted that nothing in force replaces',
         B0 + [('padd', 1, 'ph'), ('padd', 0, 'pi'), INTO_0, ('del', 0, ('head', 'old'), 1),
               ('sealsee', 0), ('reseal', 0)], pair),
        ('R3', 'the newest version of N\'s rule deleted with a deletion record and the summary entry with '
               'one: a lost version; Seal again writes a version granting nothing',
         B0 + [rule_act(0, 'r', rows), VERDEL, HEADDEL, ('sealsee', 0), ('reseal', 0)], pair),
        ('R4', 'c1\'s refusal deleted with a deletion record and the summary entry with one: a lost answer; '
               'Seal again writes refuse:forever naming it in lost',
         B0 + [('del', 0, ('ver', 'renew:c1', 'new'), 1), HEADDEL, ('sealsee', 0), ('reseal', 0)],
         [('renewcert', 'D', 'c1')]),
        ('R5', 'the revocation of c0 deleted with a deletion record and the summary entry with one: known '
               'by the deletion record alone, restated by the seal',
         B0 + [('del', 0, ('ref', 'revoked'), 1), HEADDEL, ('sealsee', 0), ('reseal', 0)], pair),
        ('R6', 'the retirement deleted with a deletion record and the summary entry with one',
         B0 + [('del', 0, ('ref', 'retired'), 1), HEADDEL, ('sealsee', 0), ('reseal', 0)], pair),
        ('R7', 'the removal of pf deleted with a deletion record and the summary entry with one',
         B0 + [('del', 0, ('prem',), 1), HEADDEL, ('sealsee', 0), ('reseal', 0)], pair),
        ('R8', 'the revocation of c0 edited at copy 0: damage; Seal again deletes it with a deletion record',
         B0 + [('edit', 0, ('ref', 'revoked')), ('sealsee', 0), ('reseal', 0)], pair),
        ('R9', 'the admission of c1 edited: damage; Seal again deletes it, and c1 is refused',
         B0 + [('edit', 0, ('rec',)), ('sealsee', 0), ('reseal', 0)], pair),
        ('R10', 'the newest version of N\'s rule edited: damage whose text the summary entry carries',
         B0 + [rule_act(0, 'r', rows), ('edit', 0, ('ver', 'rule:N', 'new')), ('sealsee', 0), ('reseal', 0)], pair),
        ('R11', 'the summary entry edited: damage; Seal again deletes it and names it',
         B0 + [('edit', 0, ('head', 'new')), ('sealsee', 0), ('reseal', 0)], pair),
        ('R12', 'the newest version of N\'s rule and the summary entry both edited: a damaged version whose '
                'text no summary entry holds, which refuses after the seal by its own deletion record',
         B0 + [rule_act(0, 'r', rows), ('edit', 0, ('ver', 'rule:N', 'new')), ('edit', 0, ('head', 'new')),
               ('sealsee', 0), ('reseal', 0)], pair),
        ('R13', 'a revocation at copy 1 whose summary entry copy 1 deletes with no deletion record, merged '
                'into copy 0 by KeePassXC: a record no summary entry restates',
         B0 + [('pair', 0), INTO_1, ('revoke', 1, 'c2'), ('del', 1, ('head', 'new'), 0), XC_INTO_0,
               ('sealsee', 0), ('reseal', 0)], pair),
        ('R14', 'two at once: a lost version and the revocation of c0 edited',
         B0 + [rule_act(0, 'r', rows), VERDEL, HEADDEL, ('edit', 0, ('ref', 'revoked')),
               ('sealsee', 0), ('reseal', 0)], pair),
        ('R15', 'the authority deleted, none after it, and the summary entry deleted with a deletion '
                'record: stopped with the key held in the retirement and no authority standing; Create '
                'refused; Seal again; then Create under the key held, and a pairing',
         B0 + [('delauth', 0), HEADDEL, ('create', 0), ('sealsee', 0), ('reseal', 0), ('create', 0), ('pair', 0)],
         pair),
        ('R16', 'a lost version, then Seal again and a revocation of c1 in a single write',
         B0 + [rule_act(0, 'r', rows), VERDEL, HEADDEL, ('sealsee', 0), ('reseal+', 0, ('revoke', 0, 'c1'))], pair),
        ('R17', 'stopped with no authority standing, then Seal again and Create in a single write',
         B0 + [('delauth', 0), HEADDEL, ('sealsee', 0), ('reseal+', 0, ('create', 0))], pair),
        ('R18', 'a lost version, then Seal again and N\'s rule set again in a single write',
         B0 + [rule_act(0, 'r', rows), VERDEL, HEADDEL, ('sealsee', 0), ('reseal+', 0, rule_act(0, 'rw', rows))],
         pair),
        ('R19', 'the seal\'s dialog shows a lost version at copy 0; a KeePassXC merge from copy 1 brings a '
                'summary entry that restates it and names the one deleted while the dialog stands, beside '
                'damage that keeps the store stopped; the seal is composed at the write and writes nothing '
                'for the version',
         B0 + [rule_act(0, 'r', rows), INTO_1, ('padd', 1, 'ph'), VERDEL, HEADDEL, ('edit', 0, ('rec',)),
               ('sealsee', 0), XC_INTO_0, ('reseal', 0)], pair),
        ('R20', 'the seal\'s dialog shows the summary entry deleted at copy 0; a Synchronize from copy 1 '
                'brings a deletion record of a version whose content no file holds while the dialog stands; '
                'the seal is composed at the write and settles it',
         B0 + [HEADDEL, ('lose', 1, 'rule:N/pf' if rows else 'rule:N'), ('sealsee', 0), INTO_0, ('reseal', 0)], pair),
        ('R21', 'the accepted class of STORE.md 639 row 5: c0 revoked at copy 0, the revocation and the '
                'summary entry deleted with no deletion record, Seal again, then KeePassXC into the stale '
                'copy 1, which never held the revocation',
         [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0), ('del', 0, ('head', 'new'), 0),
          ('sealsee', 0), ('reseal', 0)], [('renewcert', 'D', 'c0')]),
    ]
    if rows:
        out.insert(4, ('R3s', 'N\'s switch set to refuse, its version deleted with a deletion record and the '
                              'summary entry with one: a lost version of the switch',
                       B0 + [('switch', 0, 'N', 'deny'), ('del', 0, ('ver', 'sw:N', 'new'), 1), HEADDEL,
                             ('sealsee', 0), ('reseal', 0)], pair))
    return out


ACCEPTED = {'R21': 'STORE.md 639 row 5: Seal again after a loss in both carriers with no deletion record, '
                   'then KeePassXC into a stale copy'}


def bound(ops, d):
    return [tuple(d if x == 'D' else x for x in op) for op in ops]


def setup():
    B.seal()


# The field of KPG.Data whose first character the dialog edit changes, by the kind edited.
EDIT_FIELDS = {'ref': None, 'rec': 'client', 'auth': 'id', 'padd': 'name', 'prem': 'name', 'ver': 'object',
               'head': 'height'}


def edit_past_limit(b, k, op):
    """The model's `edit` on a file: the entry edited past KeePass's history limit, eleven edits of
    the entry dialog as Store.cs history-edit makes them (the payload, then the title ten times), so
    that no version of it passes; the model, which holds no history, reads an edit as damage at once."""
    eid = b.W.pick(b.W.reps[op[1]], op[2])
    if not eid:
        return False
    field = EDIT_FIELDS[op[2][0]] or ('client' if op[2][1] == 'revoked' else 'authority')
    u = b.uid(eid)
    t = b.W.t + 10
    for e in ['char'] + ['title'] * 10:
        b.h.run('history-edit', b.files[op[1]], u, t, e, field)
        t += 60
    b.W.t = t + 10
    b.sync_file(op[1])
    b.readcheck(op[1], b.dump(op[1]), k)
    b.log.append('%2d %s (eleven edits of the entry dialog)' % (k, ' '.join(map(str, op))))
    return True


def run_ops(host, tag, ops):
    b = B.Bridge(host, 2, tag)
    b.W.maxclients = 5
    for k, op in enumerate(ops):
        ok = edit_past_limit(b, k, op) if op[0] == 'edit' else b.step(k, op)
        if not ok:
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b


def read_now(b, i):
    """read.py's answer for file i in the model's names."""
    return b.as_model(B.rd(b.dump(i), B.DECIDED))


def losses_line(r):
    """read.py's listing of what a stopped store lost, as text."""
    x = r.get('losses')
    if not x:
        return 'no listing'
    out = ['reasons: ' + ('; '.join(x['reasons']) or 'none')]
    if x['lost']:
        out.append('lost: ' + ', '.join('%s (%d)' % (o, len(ids)) for o, ids in sorted(x['lost'].items())))
    if x['damaged']:
        out.append('damaged: ' + ', '.join('%s %s' % (d['kind'] or '?', d['id'][:8]) for d in x['damaged']))
    if x['heads']:
        out.append('summary entries told of: ' + ', '.join(h[:8] for h in x['heads']))
    for kind in ('revoked', 'retired', 'removed'):
        if x['byDeletion'].get(kind):
            out.append('%s by a deletion record alone: %s' % (kind, ', '.join(v[:8] for v in x['byDeletion'][kind])))
    if x['notRestated']:
        out.append('not restated: ' + ', '.join(x['notRestated']))
    if x['refusesAfter']:
        out.append('refuses after the seal: ' + ', '.join(x['refusesAfter']))
    return '; '.join(out)


def own_line(b, i, r):
    w = M.narrower(r, b.W.ideal(i), B.JUDGE)
    return 'BEYOND ITS OWN IDEAL: ' + w if w else 'within its own ideal'


def part_cells(host, hist, tag):
    print('== R1 to R21 on files, two copies: each sequence, the read and read.py\'s listing before the seal, the')
    print('   copy after the seal, then every merge both ways; each merged file held to every decision any gate made')
    print('   (the oracle), to its own ideal, and to both copies just before the merge. C: steps where the file and')
    print('   the model disagreed, entry by entry and in the read.')
    tally = {}
    for hid, what, ops, then in hist:
        print('## %s: %s' % (hid, what))
        print('   %s' % seq(ops))
        cut = max(k for k, op in enumerate(ops) if op[0] in ('reseal', 'reseal+'))
        b0 = run_ops(host, tag, ops[:cut])
        r = read_now(b0, 0)
        print('   before the seal at copy 0: store %s; %s' % (r['status'], losses_line(r)))
        print('   %s' % B.view(r))
        b = run_ops(host, tag, ops)
        for line in b.log:
            if 'does not apply' in line:
                print('   step %s' % line.strip())
        o, r = B.outcome(b, 0)
        print('   after the seal at copy 0: %s; %s [%s]' % (o, own_line(b, 0, r), C.c_note(b)))
        print('   %s' % B.view(r))
        for x in C.c_lines(b, '      '):
            print(x)
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                bb = run_ops(host, tag, ops)
                pre = (read_now(bb, d), read_now(bb, s))
                bb.step(len(ops), C.merge_op(prog, d, s))
                o, r = B.outcome(bb, d)
                own = M.narrower(r, bb.W.ideal(d), B.JUDGE)
                over = beyond(r, pre)
                after = []
                for k, op in enumerate(bound(then, d)):
                    ok = bb.step(len(ops) + 1 + k, op)
                    after.append('%s %s' % (' '.join(map(str, op[:1] + op[2:])), 'applies' if ok else 'does not apply'))
                acc = ACCEPTED.get(hid) if o.startswith('WIDER') or own else None
                if not acc and not own and o.startswith('WIDER') and prog == 'CreateNewUuids':
                    acc = 'an import into a copy that stays stale (STORE.md 640)'
                key = (hid, B.cat(o) + (' (accepted)' if acc else ''), 'BEYOND' if over else 'within',
                       ('beyond its own ideal (accepted)' if acc else 'BEYOND ITS OWN IDEAL') if own else 'own ideal')
                tally[key] = tally.get(key, 0) + 1
                cells.append('into %d: %s; %s; %s; then %s [%s]' % (
                    d, o + (' {accepted: %s}' % acc if acc else ''),
                    'BEYOND BOTH COPIES: ' + over if over else 'within the copies',
                    ('beyond its own ideal (accepted): ' if acc else 'BEYOND ITS OWN IDEAL: ') + own if own
                    else 'within its own ideal',
                    ', '.join(after) or 'nothing', C.c_note(bb)))
                cells += C.c_lines(bb, '      ')
                if o.startswith('WIDER'):
                    cells.append('      ' + B.view(r))
            print('   %-22s %s' % (label, cells[0]))
            for x in cells[1:]:
                print('   %-22s %s' % ('', x))
    print('== tally (sequence, the oracle\'s class, against both copies just before the merge, against the copy\'s'
          ' own ideal):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    print('   cells beyond both copies: %d of %d; beyond their own ideal outside the accepted classes: %d; wider than '
          'the oracle outside the accepted classes: %d' % (
              sum(n for k, n in tally.items() if k[2] == 'BEYOND'), sum(tally.values()),
              sum(n for k, n in tally.items() if k[3] == 'BEYOND ITS OWN IDEAL'),
              sum(n for k, n in tally.items() if k[1] == 'WIDER')))


def beyond(r, pre):
    """What the merged file grants that neither copy granted just before the merge: an admission, a
    profile of one, letters at a node, or a renewal answer below both on the chain."""
    out = []
    if r['status'] != 'ok' or not r['served']:
        return ''
    for c, p in sorted(r['adm'].items()):
        if all(c not in x['adm'] or not p <= x['adm'][c] for x in pre):
            out.append('client %s admitted' % c)
    for n in B.JUDGE:
        w = r['words'].get(n, frozenset())
        extra = w - pre[0]['words'].get(n, frozenset()) - pre[1]['words'].get(n, frozenset())
        if extra:
            out.append('%s gives %s' % (n, M.words_text(extra)))
    for c, tok in sorted(r['renew'].items()):
        if all(M.answer_rank(tok) < M.answer_rank(x['renew'].get(c, M.ASK)) for x in pre):
            out.append('renewal of %s %s' % (c, tok))
    return '; '.join(out)


# ------------------------------------------------------------------ edits past the limit
EDIT_OPS = [
    ('edits11', 'the dialog edit and 10 more dialog edits (of the title): 11 edits', ['char'] + ['title'] * 10),
    ('quick10', 'the dialog edit and 10 quick edits: 11 edits', ['char'] + ['quick'] * 10),
    ('off', 'history kept by neither copy (0 items), then the dialog edit', ['char']),
]
EDIT_MERGES = [('KeePass ' + m, m) for m in B.METHODS] + [('KeePassXC 2.8.0', '2.8.0')]


def part_edits(host, only=None):
    print('== PLAN 4.1 item 1: each kind of store entry edited at copy 0 past KeePass\'s history limit, or with no')
    print('   history kept, as KeePass\'s own commands edit it (Store.cs history-edit); copy 1 the same database')
    print('   unedited. Then the read and read.py\'s listing; Seal again where the store stops with its key held;')
    print('   then each merge of KeePassLib\'s five methods and KeePassXC 2.8.0 both ways with copy 1. Each file')
    print('   held to every decision any gate made, to its own ideal, and the copy sealed to what the edited')
    print('   entry held: does its decision stand after the seal.')
    print('   base: ' + seq(C.HIST_BASE))
    print('   base of the retirement that holds the store key: ' + seq(C.HIST_BASE_KEY))
    granting = C.HIST_BASE[:-1] + [('rule', 0, 'N', 'r'), C.HIST_BASE[-1]]     # N's rule granting read
    print('   base of the last two kinds: ' + seq(granting))
    kinds = list(C.HIST_KINDS) + [
        ('a rule version granting read (N, read)', ('ver', 'rule:N', 'new'), 'object', granting),
        ('the newest version of N\'s rule granting read and the summary entry, both', ('ver+head',), 'object',
         granting)]
    tally = {}
    for kind_name, sel, field, hb in kinds:
        if only and not any(kind_name.startswith(x) for x in only):     # a trial of some kinds
            continue
        print('## %s' % kind_name)
        for oid, what, edits in EDIT_OPS:
            b = run_ops(host, 'sed', hb)
            f = b.files
            if oid == 'off':
                for x in f:
                    host.run('limits', x, 0)
            sels = [('ver', 'rule:N', 'new'), ('head', 'new')] if sel == ('ver+head',) else [sel]
            t = b.W.t + 10
            for s in sels:
                eid = C.hist_pick(b, s)
                u = b.uid(eid)
                fld = field if s[0] != 'head' else 'height'
                for e in edits:
                    host.run('history-edit', f[0], u, t, e, fld)
                    t += 60
            b.W.t = t + 10
            b.sync_file(0)
            r0 = read_now(b, 0)
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

            print('   %s: %s' % (oid, what))
            print('      copy 0 edited: store %s, keys %d; %s' % (r0['status'], r0['counts']['keys'], losses_line(r0)))
            sealed = b.step(40, ('reseal', 0))
            o, r = B.outcome(b, 0)
            line = '%s; %s' % (o, own_line(b, 0, r))
            tally[('seal', 'applies' if sealed else 'does not apply', r['status'])] = \
                tally.get(('seal', 'applies' if sealed else 'does not apply', r['status']), 0) + 1
            print('      Seal again at 0: %s; store %s; %s [%s]' % ('applies' if sealed else 'does not apply',
                                                                    r['status'], line, C.c_note(b)))
            print('         %s' % B.view(r))
            for x in C.c_lines(b, '         '):
                print(x)
            if not sealed:
                continue
            e2 = f[0] + '.sealed'
            kp.copy(f[0], e2)
            W1 = b.W.clone()
            enc1 = (dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))
            for label, prog in EDIT_MERGES:
                cells = []
                for d, s in ((1, 0), (0, 1)):
                    kp.copy(e2, f[0])
                    kp.copy(e1, f[1])
                    b.W = W1.clone()
                    b.enc.W = b.W
                    b.enc.uuid, b.enc.eid, b.macs = dict(enc1[0]), dict(enc1[1]), dict(enc1[2])
                    b.disagree, b.readdiff = [], []
                    b.step(50, C.merge_op(prog, d, s))
                    o, r = B.outcome(b, d)
                    tally[(B.cat(o), 'own ideal' if not M.narrower(r, b.W.ideal(d), B.JUDGE) else 'BELOW')] = \
                        tally.get((B.cat(o), 'own ideal' if not M.narrower(r, b.W.ideal(d), B.JUDGE) else 'BELOW'), 0) + 1
                    cells.append('into %d: %s, store %s; %s [%s]' % (d, o, r['status'], own_line(b, d, r), C.c_note(b)))
                    cells += C.c_lines(b, '         ')
                print('      %-24s %s' % (label, cells[0]))
                for x in cells[1:]:
                    print('      %-24s %s' % ('', x))
            restore()
    print('== tally: ' + ', '.join('%s %d' % (' '.join(k), n) for k, n in sorted(tally.items())))


def main():
    sys.stdout.reconfigure(line_buffering=True, newline='\n')
    setup()
    host = kp.Host()
    t0 = time.time()
    try:
        part = sys.argv[1]
        if part == 'cells':
            only = sys.argv[2].split(',') if len(sys.argv) > 2 else None     # a trial of some sequences
            part_cells(host, [h for h in histories() if not only or h[0] in only], 'sc')
        elif part == 'edits':
            part_edits(host, sys.argv[2].split(',') if len(sys.argv) > 2 else None)
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
