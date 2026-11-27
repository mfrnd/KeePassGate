"""ADDED (seal restates): what a summary entry restates of a decision that a deletion record alone tells
of, on real files, under each form of crdt_model.SEAL_RESTATES.

Run:  uv run --no-project python -I -X utf8 restates.py PART FORM [SEQUENCES]

FORM is 'all' (the control, the model's form before: every such decision restated by its subject),
'named' (the product's form: only a decision whose subject the file names is restated, as read.py
tells a deletion record), 'ids' (the form proposed: as 'named', and a decision whose subject nothing
names restated by the identifier of its deletion record, an item holding the identifier and the
kind) or 'hashes' (as 'ids', a revocation and a retirement by the identifier's keyed tag and subject
hash alone).

Parts:
  cells    R1 to R21 of seal.py and the sequences of FINDINGS below, each played through
           bridge.Bridge under bridge.seal() with the form set; the copy sealed, then every merge
           both ways by KeePassLib's five methods and four keepassxc-cli releases, each with nothing
           before it and with every deletion record of the sealed copy cleared before it (Database
           Maintenance, which is also what a program that keeps no deletion record leaves); each
           merged file held to every decision any gate made, to its own ideal and to both copies
           just before the merge, and whether it is stopped. compare_restates.py holds each form's
           cells to the control's, cell by cell.
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import seal as SL  # noqa: E402
import crdt_model as M  # noqa: E402

FORMS = ('all', 'named', 'ids', 'hashes')
HEADDEL = ('del', 0, ('head', 'new'), 1)
SEALED = [('sealsee', 0), ('reseal', 0)]
PAIR = [('pair', 'D')]

# Copy 1 is the copy from before the decision: it never held it, and still holds what the decision
# took away. In the sequences ending in r, copy 1 makes a decision of the same kind about the same
# subject on its own before the merge, so that a record of that decision with another identifier
# arrives.
FINDINGS = [
    ('F5', 'c0 revoked at copy 0 alone, the revocation and the summary entry deleted with a deletion '
           'record each, Seal again; copy 1 the copy from before, holding c0\'s admission',
     [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), HEADDEL] + SEALED, PAIR),
    ('F6', 'the authority recreated at copy 0 alone, the retirement and the summary entry deleted with a '
           'deletion record each, Seal again; copy 1 the copy from before, holding the authority retired',
     [('recreate', 0), ('del', 0, ('ref', 'retired'), 1), HEADDEL] + SEALED, PAIR),
    ('F7', 'pf removed at copy 0 alone, the removal and the summary entry deleted with a deletion record '
           'each, Seal again; copy 1 the copy from before, holding pf\'s addition',
     [('prem', 0, 'pf'), ('del', 0, ('prem',), 1), HEADDEL] + SEALED, PAIR),
    ('F5r', 'as F5, and c0 revoked at copy 1 on its own: a revocation of c0 under another identifier',
     [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), HEADDEL] + SEALED + [('revoke', 1, 'c0')], PAIR),
    ('F6r', 'as F6, and the authority recreated at copy 1 on its own: a retirement of a0 under another '
            'identifier',
     [('recreate', 0), ('del', 0, ('ref', 'retired'), 1), HEADDEL] + SEALED + [('recreate', 1)], PAIR),
    ('F7r', 'as F7, and pf removed at copy 1 on its own: a removal of pf under another identifier',
     [('prem', 0, 'pf'), ('del', 0, ('prem',), 1), HEADDEL] + SEALED + [('prem', 1, 'pf')], PAIR),
]


def histories():
    """R1 to R21 of seal.py, then FINDINGS."""
    return SL.histories() + FINDINGS


def setup(form):
    B.seal()
    M.SEAL_RESTATES = form
    B.PREFIX = 'rs-'


def part_cells(host, form, hist):
    print('== form %s: each sequence, copy 0 after the seal, then every merge both ways by KeePassLib\'s five' % form)
    print('   methods and four keepassxc-cli releases, with nothing before the merge and with every deletion record of')
    print('   copy 0 cleared before it; each merged file held to every decision any gate made (the oracle), to its own')
    print('   ideal and to both copies just before the merge. C: steps where the file and the model disagreed.')
    print('   A line CELL is sequence | before the merge | program | the copy merged into: the oracle\'s class;')
    print('   against both copies; against its own ideal; the store; what follows.')
    tally = {}
    for hid, what, ops, then in hist:
        print('## %s: %s' % (hid, what))
        print('   %s' % SL.seq(ops))
        b = SL.run_ops(host, form, ops)
        for line in b.log:
            if 'does not apply' in line:
                print('   step %s' % line.strip())
        o, r = B.outcome(b, 0)
        print('   copy 0 after the sequence: %s; %s; %s [%s]' % (o, SL.own_line(b, 0, r), SL.losses_line(r), C.c_note(b)))
        print('   %s' % B.view(r))
        for x in C.c_lines(b, '      '):
            print(x)
        f = b.files
        saved = [x + '.kept' for x in f]
        for x, y in zip(f, saved):
            kp.copy(x, y)
        W0 = b.W.clone()
        enc0 = (dict(b.enc.uuid), dict(b.enc.eid), dict(b.macs))
        k0 = len(ops)
        for pre in ('nothing', 'cleared'):
            for label, prog in C.PROGRAMS:
                for d, s in ((1, 0), (0, 1)):
                    for x, y in zip(saved, f):
                        kp.copy(x, y)
                    b.W = W0.clone()
                    b.enc.W = b.W
                    b.enc.uuid, b.enc.eid, b.macs = dict(enc0[0]), dict(enc0[1]), dict(enc0[2])
                    b.disagree, b.readdiff = [], []
                    k = k0
                    if pre == 'cleared':
                        if not b.step(k, ('clear', 0)):
                            b.log.append('%2d clear 0 (does not apply)' % k)
                        k += 1
                    pre_r = (SL.read_now(b, d), SL.read_now(b, s))
                    b.step(k, C.merge_op(prog, d, s))
                    o, r = B.outcome(b, d)
                    own = M.narrower(r, b.W.ideal(d), B.JUDGE)
                    over = SL.beyond(r, pre_r)
                    after = []
                    for j, op in enumerate(SL.bound(then, d)):
                        ok = b.step(k + 1 + j, op)
                        after.append('%s %s' % (' '.join(map(str, op[:1] + op[2:])), 'applies' if ok else 'does not apply'))
                    acc = SL.ACCEPTED.get(hid) if o.startswith('WIDER') or own else None
                    if not acc and not own and o.startswith('WIDER') and prog == 'CreateNewUuids':
                        acc = 'an import into a copy that stays stale (STORE.md 640)'
                    key = (B.cat(o) + (' (accepted)' if acc else ''), 'BEYOND' if over else 'within',
                           ('beyond its own ideal (accepted)' if acc else 'BEYOND ITS OWN IDEAL') if own else 'own ideal',
                           'store ' + r['status'])
                    tally[key] = tally.get(key, 0) + 1
                    print('   CELL %s | %s | %s | %d: %s; %s; %s; store %s; then %s [%s]' % (
                        hid, pre, label, d, o + (' {accepted: %s}' % acc if acc else ''),
                        'BEYOND BOTH COPIES: ' + over if over else 'within the copies',
                        ('beyond its own ideal (accepted): ' if acc else 'BEYOND ITS OWN IDEAL: ') + own if own
                        else 'within its own ideal', r['status'], ', '.join(after) or 'nothing', C.c_note(b)))
                    for x in C.c_lines(b, '      '):
                        print(x)
                    if o.startswith('WIDER') or r['status'] != 'ok':
                        print('      %s; %s' % (B.view(r), SL.losses_line(r)))
    print('== tally (the oracle\'s class, against both copies just before the merge, against the copy\'s own ideal,'
          ' the store):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    print('   cells %d; beyond both copies %d; beyond their own ideal outside the accepted classes %d; wider than the'
          ' oracle outside the accepted classes %d; stopped %d' % (
              sum(tally.values()), sum(n for k, n in tally.items() if k[1] == 'BEYOND'),
              sum(n for k, n in tally.items() if k[2] == 'BEYOND ITS OWN IDEAL'),
              sum(n for k, n in tally.items() if k[0] == 'WIDER'),
              sum(n for k, n in tally.items() if k[3] != 'store ok')))


def main():
    sys.stdout.reconfigure(line_buffering=True, newline='\n')
    part, form = sys.argv[1], sys.argv[2]
    if form not in FORMS:
        raise SystemExit('unknown form ' + form)
    setup(form)
    only = sys.argv[3].split(',') if len(sys.argv) > 3 else None
    host = kp.Host()
    t0 = time.time()
    try:
        if part == 'cells':
            part_cells(host, form, [h for h in histories() if not only or h[0] in only])
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
