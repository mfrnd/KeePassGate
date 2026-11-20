"""ADDED (S34 forms): the forms of `S34-DESIGN.md` on real files, each beside the store as decided.

Run:  uv run --no-project python -I -X utf8 s34.py PART FORM

FORM is `seen` (form B, by what arrived) or `above` (form A, by identity). Every part plays its
sequences through bridge.Bridge under bridge.rows() and bridge.owed(), and under bridge.dep(): the
model's write path writes the form's texts into a real .kdbx by KeePassLib 2.62, every merge is
KeePassLib's or keepassxc-cli's, and every file is read by read.py. Each sequence is played under
the form, with a removed row taken off for real (`off`) unless a part names the other ways, and
again under the store as decided (no form, a removed row under a refusing switch written as an
empty rule), on the same steps. A merged file is held to what both copies gave just before the
merge, by letters and by capabilities, and to the store as decided's file of the same cell.

Parts:
  hand     H1 to H4, H9, H10 and H11 and W3 of the design, two profiles, read and write
  cells4   O1 to O8 of measured-rules-owed-forms, four profiles and the full mask, every merge both
           ways
  cells3   L1 to L10 of the same, the root, G and N
  switch   W1 to W8 of measured-rules-per-profile, a removed row written as decided, as `again` and
           taken off for real, every merge both ways
  moves    a node moved between G and the root (H5, H13 and a node with no rule, row 9 of STORE.md
           639), and a profile's name put back on the list apart from a row widened (H16)
  afters   the `after` lists of a version below that lets through, under a rule above edited 400
           times in a copy and 200 times apart in two copies
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import owed as O  # noqa: E402
import rows  # noqa: E402
import crdt_model as M  # noqa: E402

SYNC = ('merge', 'kp_sync', 1, 0)
BACK = ('merge', 'kp_sync', 0, 1)
DECIDED = (None, 'empty')
FORM = None        # the form a part runs, set by main()


def setup(profs, letters, levels, moves=False):
    B.rows('empty')
    M.PARENT = {}
    M.MOVES = moves
    B.owed(profs, letters, levels)
    if levels == 2:
        B.NODES, B.JUDGE = ('N', 'root'), ('root', 'N')
    B.PREFIX = 'qs%s-' % (FORM[0] if FORM else '')
    O.RD.SHOW = True


def name(cfg):
    if cfg == DECIDED:
        return 'decided'
    return '%s, %s' % (cfg[0] or 'no form', {'off': 'taken off', 'again': 'again', 'empty': 'empty rule'}[cfg[1]])


def against(form, dec, nodes):
    """The form's words against the store as decided's, by capabilities, node by node."""
    more, less = [], []
    for n in nodes:
        f, d = M.caps(form.get(n, frozenset())), M.caps(dec.get(n, frozenset()))
        if f - d:
            more.append('%s %s' % (n, M.caps_text(f - d)))
        if d - f:
            less.append('%s %s' % (n, M.caps_text(d - f)))
    if more and less:
        return 'WIDER and narrower than the store as decided: %s; %s' % ('; '.join(more), '; '.join(less))
    if more:
        return 'WIDER than the store as decided: ' + '; '.join(more)
    if less:
        return 'narrower than the store as decided: ' + '; '.join(less)
    return 'as the store as decided'


def play(host, cfg, ops, last, nrep, tag, nodes):
    """The sequence under a configuration: what the copies give before `last`, after it, and the
    oracle's class."""
    B.dep(*cfg)
    b = C.run_ops(host, nrep, tag, ops)
    if last is None:
        r = O.answer(b, 0)
        return None, r['words_raw'], B.cat(B.outcome(b, 0)[0]), b
    d, s = (last[2], last[3]) if last[0] == 'merge' else (last[1], last[1])
    pre = (O.answer(b, d)['words_raw'], O.answer(b, s)['words_raw'])
    b.step(len(ops), last)
    o, r = B.outcome(b, d)
    return pre, r['words_raw'], B.cat(o), b


def cell_line(host, cfg, ops, last, nrep, tag, nodes, dec):
    pre, after, oc, b = play(host, cfg, ops, last, nrep, tag, nodes)
    bl, bc = O.beyond2(pre[0], pre[1], after, nodes) if pre and last[0] == 'merge' else ('', '')
    kind = 'letters' if bl else 'capabilities only' if bc else 'within'
    text = '%s; %s; %s; oracle %s [%s]' % (O.gives({'words_raw': after}, nodes),
                                            O.verdict(bl, bc) if last and last[0] == 'merge' else 'no merge',
                                            against(after, dec, nodes) if dec is not None else 'the store as decided',
                                            oc, C.c_note(b))
    return text, kind, after, C.c_lines(b, '      ')


# ------------------------------------------------------------------ hand
def hand_histories():
    R = ('rule', 0, 'root', 'rw')
    return [
        ('H1', 'S34 with a row: N holds an empty rule; copy 0 takes it off; copy 1 widens the root',
         [('row', 0, 'N', 'pf', ''), SYNC, ('row', 0, 'N', 'pf', 'off'), ('row', 1, 'root', 'pf', 'rw')], SYNC),
        ('H2', 'S34 with the switch: N refuses, synchronized; copy 1 widens the root; copy 0 turns the switch off',
         [('switch', 0, 'N', 'deny'), SYNC, ('row', 1, 'root', 'pf', 'rw'), ('switch', 0, 'N', 'inherit')], SYNC),
        ('H3', 'a profile N never named: N refuses, synchronized; copy 0 turns it off; copy 1 widens the root for pg',
         [('switch', 0, 'N', 'deny'), SYNC, ('switch', 0, 'N', 'inherit'), ('row', 1, 'root', 'pg', 'rw')], SYNC),
        ('H4', 'a narrowing above arrives in a single direction and is widened again apart',
         [('row', 0, 'N', 'pf', ''), SYNC, ('row', 0, 'N', 'pf', 'off'), ('row', 1, 'root', 'pf', ''), BACK,
          ('row', 1, 'root', 'pf', 'r')], BACK),
        ('H9', 'removed for real, then opened by the copy that holds it (W6 in a single copy, no merge)',
         [R, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pf', 'off'),
          ('switch', 0, 'N', 'inherit')], None),
        ('H10', 'removed for real under a refusing switch, the switch turned off apart, the root widened after',
         [('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), SYNC, ('row', 0, 'N', 'pf', 'off'),
          ('switch', 1, 'N', 'inherit'), BACK, ('row', 0, 'root', 'pf', 'rw')], SYNC),
        ('H11', 'the widening copy never saw the node closed: copy 0 sets an empty rule at N and takes it off;'
         ' copy 1 widens the root', [('row', 0, 'N', 'pf', ''), ('row', 0, 'N', 'pf', 'off'),
                                     ('row', 1, 'root', 'pf', 'rw')], SYNC),
        ('W3', 'S34 at a single node: under a refusing switch pf removed at copy 0; the switch off at copy 1',
         [R, ('switch', 0, 'N', 'deny'), ('row', 0, 'N', 'pf', 'r'), ('row', 0, 'N', 'pg', 'r'), SYNC,
          ('row', 0, 'N', 'pf', 'off'), ('switch', 1, 'N', 'inherit')], SYNC),
    ]


def part_hand(host, form):
    print('== The hand histories of S34-DESIGN.md 5 on files, two profiles, read and write, the root and N. Each')
    print('   under the store as decided, under no form with every removed row taken off (the control), and under')
    print('   the form `%s` with every removed row taken off; the last step is the merge held to both copies, or for' % form)
    print('   H9 the last act of a single copy.')
    nodes = ('root', 'N')
    for hid, what, ops, last in hand_histories():
        print('## %s: %s' % (hid, what))
        print('   %s%s' % (O.seq(ops), ' ; then ' + ' '.join(map(str, last)) if last else ''))
        dec = None
        for cfg in (DECIDED, (None, 'off'), (form, 'off')):
            text, kind, after, cl = cell_line(host, cfg, ops, last, 2, 'hd', nodes, dec)
            if cfg == DECIDED:
                dec = after
                pre = play(host, cfg, ops, last, 2, 'hd', nodes)[0]
                if pre:
                    print('   before the merge, as decided: copy %d %s | copy %d %s' % (
                        last[2], O.gives({'words_raw': pre[0]}, nodes), last[3], O.gives({'words_raw': pre[1]}, nodes)))
            print('   %-22s %s' % (name(cfg), text))
            for x in cl:
                print(x)


# ------------------------------------------------------------------ cells
def part_cells(host, form, histories, nodes, tag, title):
    print(title)
    print('   Each cell: under the form `%s` with every removed row taken off, what the merged file gives, against' % form)
    print('   both copies just before the merge, against the same cell under the store as decided, and the oracle.')
    tally = {}
    for hid, what, base, c0, c1, nrep in histories:
        ops = base + c0 + c1
        print('## %s: %s' % (hid, what))
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                last = C.merge_op(prog, d, s)
                dtext, dkind, dec, _ = cell_line(host, DECIDED, ops, last, nrep, tag, nodes, None)
                text, kind, after, cl = cell_line(host, (form, 'off'), ops, last, nrep, tag, nodes, dec)
                imp = ' import' if prog == 'CreateNewUuids' else ''
                vs = against(after, dec, nodes).split(':')[0]
                key = (hid + imp, 'decided ' + dkind, 'form ' + kind, vs)
                tally[key] = tally.get(key, 0) + 1
                cells.append('into %d: %s' % (d, text))
                cells += cl
            print('   %-22s %s' % (label, cells[0]))
            for x in cells[1:]:
                print('   %-22s %s' % ('', x))
    print('== tally (history, the store as decided against both copies, the form against both copies, the form'
          ' against the store as decided):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    for which in ('letters', 'capabilities only'):
        print('   cells beyond both copies by %s: as decided %d, under the form %d' % (
            which, sum(n for k, n in tally.items() if k[1] == 'decided ' + which),
            sum(n for k, n in tally.items() if k[2] == 'form ' + which)))
    print('   cells under the form narrower than the store as decided %d, wider %d, of %d' % (
        sum(n for k, n in tally.items() if k[3].startswith('narrower')),
        sum(n for k, n in tally.items() if k[3].startswith('WIDER')), sum(tally.values())))


# ------------------------------------------------------------------ switch
def part_switch(host, form):
    print('== W1 to W8 (measured-rules-per-profile 7), two profiles, read and write, every merge both ways. Each')
    print('   cell under the form `%s` with a removed row written as decided (`empty`), as `again`, and taken off' % form)
    print('   for real (`off`); held to both copies just before the merge and to the same cell under the store as')
    print('   decided.')
    nodes = ('root', 'N')
    tally = {}
    for wid, what, base, c0, c1 in rows.SWITCH:
        if wid == 'W9':
            continue
        ops = base + c0 + c1
        print('## %s: %s' % (wid, what))
        print('   %s' % O.seq(ops))
        decs = {}
        for label, prog in C.PROGRAMS:
            for d, s in ((1, 0), (0, 1)):
                last = C.merge_op(prog, d, s)
                dtext, dkind, dec, _ = cell_line(host, DECIDED, ops, last, 2, 'sw', nodes, None)
                decs[(label, d)] = (dtext, dkind, dec)
        for variant in ('empty', 'again', 'off'):
            for label, prog in C.PROGRAMS:
                cells = []
                for d, s in ((1, 0), (0, 1)):
                    last = C.merge_op(prog, d, s)
                    dtext, dkind, dec = decs[(label, d)]
                    text, kind, after, cl = cell_line(host, (form, variant), ops, last, 2, 'sw', nodes, dec)
                    imp = ' import' if prog == 'CreateNewUuids' else ''
                    key = (wid + imp, variant, 'decided ' + dkind, 'form ' + kind, against(after, dec, nodes).split(':')[0])
                    tally[key] = tally.get(key, 0) + 1
                    cells.append('into %d: %s' % (d, text))
                    cells += cl
                print('   %-6s %-22s %s' % (variant, label, cells[0]))
                for x in cells[1:]:
                    print('   %-6s %-22s %s' % ('', '', x))
    print('== tally (sequence, how a removed row is written, the store as decided against both copies, the form'
          ' against both copies, the form against the store as decided):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    for variant in ('empty', 'again', 'off'):
        print('   %s: cells beyond both copies, as decided %d, under the form %d; under the form narrower than the store'
              ' as decided %d, wider %d, of %d' % (
                  variant, sum(n for k, n in tally.items() if k[1] == variant and k[2] != 'decided within'),
                  sum(n for k, n in tally.items() if k[1] == variant and k[3] != 'form within'),
                  sum(n for k, n in tally.items() if k[1] == variant and k[4].startswith('narrower')),
                  sum(n for k, n in tally.items() if k[1] == variant and k[4].startswith('WIDER')),
                  sum(n for k, n in tally.items() if k[1] == variant)))


# ------------------------------------------------------------------ moves
def part_moves(host, form):
    print('== A node moved between G and the root, and a name put back on the list apart from a row widened,')
    print('   two profiles, read and write, the root, G and N, N under G at the start. Each under the store as')
    print('   decided, and under the form `%s` with every removed row taken off.' % form)
    nodes = ('root', 'G', 'N')
    seqs = [
        ('H7', 'a node with no rule moved apart from a change above (row 9 of STORE.md 639): G and the root give r;'
         ' copy 0 moves N under the root; copy 1 widens the root',
         [('row', 0, 'root', 'pf', 'r'), ('row', 0, 'G', 'pf', 'r'), SYNC, ('nmove', 0, 'N', 'root'),
          ('row', 1, 'root', 'pf', 'rw')], SYNC),
        ('H5', 'the same through a move: N under the root, its empty rule taken off at copy 0 seeing rw; copy 1 moves'
         ' N under G, which grants r; copy 0 takes the move in; copy 1 widens G',
         [('nmove', 0, 'N', 'root'), ('row', 0, 'root', 'pf', 'rw'), ('row', 0, 'G', 'pf', 'r'), ('row', 0, 'N', 'pf', ''),
          SYNC, ('row', 0, 'N', 'pf', 'off'), ('nmove', 1, 'N', 'G'), BACK, ('row', 1, 'G', 'pf', 'rw')], SYNC),
        ('H13', 'a node whose rule was once taken off moved into a wider group in a single copy (no merge)',
         [('nmove', 0, 'N', 'root'), ('row', 0, 'root', 'pf', 'r'), ('row', 0, 'G', 'pf', 'rw'), ('row', 0, 'N', 'pf', ''),
          ('row', 0, 'N', 'pf', 'off'), ('nmove', 0, 'N', 'G')], None),
    ]
    for hid, what, ops, last in seqs:
        print('## %s: %s' % (hid, what))
        print('   %s%s' % (O.seq(ops), ' ; then ' + ' '.join(map(str, last)) if last else ''))
        dec = None
        for cfg in (DECIDED, (form, 'off')):
            text, kind, after, cl = cell_line(host, cfg, ops, last, 2, 'mv', nodes, dec)
            if cfg == DECIDED:
                dec = after
            print('   %-22s %s' % (name(cfg), text))
            for x in cl:
                print(x)
    # H16: the name put back, apart from a row widened; client c1 holds pf and pg
    print('## H16: a name put back on the list apart from a row widened: c1 admitted with pf and pg; pg added,'
          ' the root gives pg r, both copies; pg removed, both copies; copy 0 puts pg back; copy 1 widens the'
          ' root\'s row for pg to rw; merge. Held at what c1 reaches at the root.')
    ops = [('pair', 0), ('padd', 0, 'pg'), ('row', 0, 'root', 'pg', 'r'), SYNC, ('prem', 0, 'pg'), SYNC,
           ('padd', 0, 'pg'), ('row', 1, 'root', 'pg', 'rw')]
    print('   %s ; then merge kp_sync 1 0' % O.seq(ops))
    for cfg in (DECIDED, (form, 'off')):
        B.dep(*cfg)
        b = C.run_ops(host, 2, 'mv', ops)
        reach = []
        for i in (0, 1):
            r = O.answer(b, i)
            adm = r['adm'].get('c1') or frozenset()
            reach.append(frozenset(x for x in r['words'].get('root', frozenset()) if x.split('.')[0] in adm))
        b.step(len(ops), SYNC)
        r = O.answer(b, 1)
        adm = r['adm'].get('c1') or frozenset()
        got = frozenset(x for x in r['words'].get('root', frozenset()) if x.split('.')[0] in adm)
        extra = got - reach[0] - reach[1]
        print('   %-22s c1 reaches at the root: copy 0 %s | copy 1 %s | merged %s; %s [%s]' % (
            name(cfg), O.per_profile(reach[0]), O.per_profile(reach[1]), O.per_profile(got),
            'BEYOND BOTH COPIES: ' + O.per_profile(extra, True) if extra else 'within what the copies gave', C.c_note(b)))


# ------------------------------------------------------------------ afters
def part_afters(host, form):
    print('== The `after` lists of a version below that lets through, under a rule above edited many times. N/pf')
    print('   taken off under the root; then the root\'s row for pf set alternately to read and to read and write. A:')
    print('   400 edits in copy 0. C: 200 edits, an edit in each copy apart, then Synchronize both ways, each round.')
    print('   Each line read at copy 0, as measured-rules-owed-forms 7 reads N/pf. Under the form `%s`.' % form)
    B.dep(form, 'off')
    vals = ('r', 'rw')
    print('## A: edits above in copy 0')
    b = B.Bridge(host, 2, 'af')
    b.step(0, ('row', 0, 'N', 'pf', ''))
    b.step(1, ('row', 0, 'N', 'pf', 'off'))
    for k in range(1, 401):
        b.step(k + 1, ('row', 0, 'root', 'pf', vals[k % 2]))
        if k in O.CHECKS:
            print(O.afters_line(k, O.measure(b, 0)))
    for x in C.c_lines(b, '   '):
        print(x)
    print('## C: an edit above in each copy apart, then Synchronize both ways')
    b = B.Bridge(host, 2, 'af')
    b.step(0, ('row', 0, 'N', 'pf', ''))
    b.step(1, ('row', 0, 'N', 'pf', 'off'))
    b.step(2, SYNC)
    step = 3
    for k in range(2, 201, 2):
        b.step(step, ('row', 0, 'root', 'pf', 'r'))
        b.step(step + 1, ('row', 1, 'root', 'pf', 'rw'))
        b.step(step + 2, BACK)
        b.step(step + 3, SYNC)
        step += 4
        if k in O.CHECKS:
            print(O.afters_line(k, O.measure(b, 0)))
    for x in C.c_lines(b, '   '):
        print(x)


def main():
    global FORM
    part, form = sys.argv[1], sys.argv[2]
    FORM = form
    sys.stdout.reconfigure(line_buffering=True)
    t0 = time.time()
    if part in ('hand', 'switch', 'afters'):
        setup(('pf', 'pg'), 'rw', 2)
    elif part == 'cells4':
        setup(O.FOUR, O.MASK, 2)
    elif part == 'cells3':
        setup(O.FOUR, O.MASK, 3)
    elif part == 'moves':
        setup(('pf', 'pg'), 'rw', 3, moves=True)
    host = kp.Host()
    try:
        if part == 'hand':
            part_hand(host, form)
        elif part == 'cells4':
            part_cells(host, form, O.merges4_histories(), ('root', 'N'), 'c4',
                       '== O1 to O8, four profiles and the full mask, every merge both ways')
        elif part == 'cells3':
            part_cells(host, form, O.levels_histories(), ('root', 'G', 'N'), 'c3',
                       '== L1 to L10, the root, G and N, four profiles and the full mask, every merge both ways')
        elif part == 'switch':
            part_switch(host, form)
        elif part == 'moves':
            part_moves(host, form)
        elif part == 'afters':
            part_afters(host, form)
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
