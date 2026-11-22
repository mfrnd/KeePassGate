"""ADDED (renewal answers): the standing answers to a renewal on real files, STORE.md 618 and S7.

Run:  uv run --no-project python -I -X utf8 answers.py PART [COUNT SEED]

Every part plays its sequences through bridge.Bridge under bridge.answers(): the store as decided
with an answer as a version of `renew:` and the client key, holding the product's text, every
certificate and client key written as 64 hexadecimal characters, and the reading of lost versions
of `measured-lost-settled` (JUDGE `lost`). Every gate act is the model's write path, written into a
real .kdbx by KeePassLib 2.62; every merge is KeePassLib's or keepassxc-cli's; every file is read by
read.py, whose reading of an answer knows nothing of the model's. The gate's clock is the model's
alone: a file holds no clock, and the answer read from it is read at each of the three positions of
crdt_model.CLOCK, before both moments, between them and after both.

An answer is printed as `ask`, `until-1` (the earlier moment, crdt_model.MOMENTS), `until-2` (the
later) or `forever`, with the positions of the clock at which it refuses (`012`, `0`, `-`), and the
cause where it is not a standing text: `lost` or `unreadable`. A merged file is held to the oracle
(every decision any gate made, bridge.global_ideal), under which a lift's `lost` counts as seen
(crdt_model.IDEAL_LOST), and to both copies just before the merge.

Parts:
  cells    A1 to A14, each a base, the acts of each copy, then every merge both ways (KeePassLib's
           five methods, four keepassxc-cli releases), and the acts that follow in the copy merged
           into
  losses   lost versions: an answer (until a moment, forever, a lift) whose version is deleted
           with and without a deletion record, with its summary entry kept, deleted with a
           deletion record or with none; the read; Seal again where the store stops; a lift where
           the answer refuses; then every merge both ways with the other copy, from before
  random   random histories of answers, lifts, dialogs, clocks, renewals and merges, COUNT of them
           from SEED, with Synchronize, Overwrite if newer and KeePassXC 2.8.0
"""
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge as B  # noqa: E402
import cases as C  # noqa: E402
import kp  # noqa: E402
import crdt_model as M  # noqa: E402

INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)


def seq(ops):
    return ' ; '.join(' '.join(map(str, op)) for op in ops)


def histories():
    """(label, what, ops, then): the acts up to the merge of the cell, and the acts that follow it
    in the copy merged into, 'D' standing for that copy. The model's World starts with the client
    c0 admitted in both copies; c1 is paired where a sequence needs it."""
    renewal = [('renewcert', 'D', 'c0')]
    return [
        ('A1', 'forever at copy 0, nothing at copy 1',
         [('renew', 0, 'c0', 'forever')], renewal),
        ('A2', 'forever at copy 0, until the later moment at copy 1, apart',
         [('renew', 0, 'c0', 'forever'), ('renew', 1, 'c0', 'until2')], renewal),
        ('A3', 'until the earlier moment at copy 0, until the later at copy 1, apart; then the clock of '
               'the copy merged into moved between the moments, and past both',
         [('renew', 0, 'c0', 'until1'), ('renew', 1, 'c0', 'until2')],
         [('renewcert', 'D', 'c0'), ('clock', 'D', 1), ('renewcert', 'D', 'c0'), ('clock', 'D', 2),
          ('renewcert', 'D', 'c0')]),
        ('A4', 'forever at copy 0, synchronized, lifted at copy 1 which saw it',
         [('renew', 0, 'c0', 'forever'), INTO_1, ('renew', 1, 'c0', 'ask')], renewal),
        ('A5', 'until the earlier moment at copy 0, synchronized; copy 0, its clock past that moment, '
               'refuses forever, which copy 1 never sees; copy 1 lifts the answer it holds',
         [('renew', 0, 'c0', 'until1'), INTO_1, ('clock', 0, 1), ('renew', 0, 'c0', 'forever'),
          ('renew', 1, 'c0', 'ask')], renewal),
        ('A6', 'copy 0 shows ask on the renewal dialog, a Synchronize brings forever from copy 1 while it '
               'stands, copy 0 then refuses until the later moment, naming what it showed',
         [('renew', 1, 'c0', 'forever'), ('see', 0, 'c0'), INTO_0, ('renew', 0, 'c0', 'until2')], renewal),
        ('A7', 'forever at copy 0, synchronized; at copy 0 its version and the summary entry deleted for '
               'good with deletion records, the store sealed again, the answer lifted; copy 1 from before',
         [('renew', 0, 'c0', 'forever'), INTO_1, ('del', 0, ('ver', 'renew:c0', 'new'), 1),
          ('del', 0, ('head', 'new'), 1), ('reseal', 0), ('renew', 0, 'c0', 'ask')], renewal),
        ('A8', 'forever at copy 0, synchronized, lifted at copy 0; the lift\'s version and the summary '
               'entry deleted for good at copy 0 with deletion records',
         [('renew', 0, 'c0', 'forever'), INTO_1, ('renew', 0, 'c0', 'ask'),
          ('del', 0, ('ver', 'renew:c0', 'new'), 1), ('del', 0, ('head', 'new'), 1)], renewal),
        ('A9', 'c1 paired and synchronized; c0 revoked at copy 0 and refused until the later moment at '
               'copy 1; c1 refused forever at copy 0 and its certificate renewed at copy 1, which has not '
               'seen the refusal; then the renewed certificate asks for a renewal in the copy merged into',
         [('pair', 0), INTO_1, ('revoke', 0, 'c0'), ('renew', 1, 'c0', 'until2'),
          ('renew', 0, 'c1', 'forever'), ('renewcert', 1, 'c1')],
         [('renewcert', 'D', 'c1'), ('renewcert', 'D', 'c0')]),
        ('A10', 'c1 paired; c0 refused forever and c1 until the earlier moment at copy 0, the clock of copy '
                '0 moved past every moment; c0 asks for a renewal, then c1, then c1 is refused forever, '
                'naming the answer whose moment has passed; synchronized',
         [('pair', 0), ('renew', 0, 'c0', 'forever'), ('renew', 0, 'c1', 'until1'), ('clock', 0, 2),
          ('renewcert', 0, 'c0'), ('renewcert', 0, 'c1'), ('renew', 0, 'c1', 'forever')],
         [('clock', 'D', 2), ('renewcert', 'D', 'c0'), ('renewcert', 'D', 'c1')]),
        ('A11', 'forever at copy 0 and until the later moment at copy 1, apart; copy 0\'s settings dialog '
                'shows forever (see), a Synchronize brings the other refusal while it stands, and the '
                'press lifts what it showed; the refusal made apart stands',
         [('renew', 0, 'c0', 'forever'), ('renew', 1, 'c0', 'until2'), ('see', 0, 'c0'), INTO_0,
          ('renew', 0, 'c0', 'ask')], renewal),
        ('A12', 'forever at copy 0, synchronized; copy 0\'s settings dialog shows it (see); copy 1 lifts it '
                'and refuses until the later moment; a Synchronize brings both while the dialog stands, and '
                'the press, whose version was replaced since, comes to nothing',
         [('renew', 0, 'c0', 'forever'), INTO_1, ('see', 0, 'c0'), ('renew', 1, 'c0', 'ask'),
          ('renew', 1, 'c0', 'until2'), INTO_0, ('renew', 0, 'c0', 'ask')], renewal),
        ('A13', 'a deletion record of a refusal whose content no file holds reaches copy 0 (crdt_model\'s '
                '`lose`); copy 0 lifts, naming it in lost',
         [('lose', 0, 'renew:c0'), ('renew', 0, 'c0', 'ask')], renewal),
        ('A14', 'a text out of form written by somebody holding the store key at copy 0, beside until the '
                'later moment; copy 0 lifts both',
         [('renew', 0, 'c0', 'until2'), ('unreadable', 0, 'c0'), ('renew', 0, 'c0', 'ask')], renewal),
    ]


def setup():
    B.answers()
    B.PREFIX = 'an-'


def bound(ops, d):
    return [tuple(d if x == 'D' else x for x in op) for op in ops]


def answer_line(r, clients=('c0', 'c1')):
    """What a reading answers for each client: the token as the runs print it, the positions at which
    it refuses, and the cause where it is not a standing text."""
    out = []
    for c in clients:
        if c not in r['renew']:
            continue
        tok = r['renew'][c]
        why = r.get('renew_why', {}).get(c, 'stands')
        out.append('%s %s (%s)%s' % (c, M.answer_label(tok), M.refusing_at(tok),
                                     ' ' + why if why in ('lost', 'unreadable') else ''))
    return ', '.join(out) or 'no answer'


def read_answer(b, i):
    """read.py's answer for file i in the model's names, with the causes."""
    r = b.as_model(B.rd(b.dump(i), B.DECIDED))
    r['renew_why'] = {c: x['cause'] for c, x in r.get('renew_detail', {}).items()}
    return r


def beyond(after, pre0, pre1):
    """Each client whose merged answer is below both copies' just before the merge on the chain:
    refusing at fewer positions of the clock than either copy did."""
    out = []
    for c, tok in sorted(after['renew'].items()):
        a, x, y = (M.answer_rank(r['renew'].get(c, M.ASK)) for r in (after, pre0, pre1))
        if a < x and a < y:
            out.append(c)
    return out


def part_cells(host, title, hist, tag):
    print(title)
    print('   Each cell: what the merged file answers, the positions of the clock at which it refuses, held to')
    print('   both copies just before the merge and to the oracle, then what the acts after the merge do in')
    print('   that copy. C: steps where the file and the model disagreed, entry by entry and in the read.')
    tally = {}
    for hid, what, ops, then in hist:
        print('## %s: %s' % (hid, what))
        print('   %s' % seq(ops))
        b0 = C.run_ops(host, 2, tag, ops)
        print('   before the merge: copy 0 %s | copy 1 %s [%s]' % (
            answer_line(read_answer(b0, 0)), answer_line(read_answer(b0, 1)), C.c_note(b0)))
        for x in C.c_lines(b0, '      '):
            print(x)
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                b = C.run_ops(host, 2, tag, ops)
                pre = (read_answer(b, d), read_answer(b, s))
                b.step(len(ops), C.merge_op(prog, d, s))
                o, _ = B.outcome(b, d)
                r = read_answer(b, d)
                over = beyond(r, pre[0], pre[1])
                own = M.narrower_renewals(r, b.W.ideal(d))
                after = []
                for k, op in enumerate(bound(then, d)):
                    ok = b.step(len(ops) + 1 + k, op)
                    if op[0] == 'renewcert':
                        after.append('%s %s %s' % (op[0], op[2], 'issued' if ok else 'refused'))
                imp = ' import' if prog == 'CreateNewUuids' else ''
                key = (hid + imp, B.cat(o), 'BEYOND' if over else 'within', 'BELOW ITS OWN IDEAL' if own else 'own ideal')
                tally[key] = tally.get(key, 0) + 1
                cells.append('into %d: %s; %s; %s; oracle %s; then %s [%s]' % (
                    d, answer_line(r), 'BEYOND BOTH COPIES: ' + ', '.join(over) if over else 'within the copies',
                    'BELOW ITS OWN IDEAL: ' + own if own else 'not below its own ideal', o,
                    ', '.join(after) or 'nothing', C.c_note(b)))
                cells += C.c_lines(b, '      ')
            print('   %-22s %s' % (label, cells[0]))
            for x in cells[1:]:
                print('   %-22s %s' % ('', x))
    print('== tally (sequence, the oracle\'s class, against both copies just before the merge, against the copy\'s'
          ' own ideal):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    print('   cells beyond both copies: %d of %d; below their own ideal: %d' % (
        sum(n for k, n in tally.items() if k[2] == 'BEYOND'), sum(tally.values()),
        sum(n for k, n in tally.items() if k[3] != 'own ideal')))


# ------------------------------------------------------------------ losses
ANSWER_KINDS = [('until', [('renew', 0, 'c0', 'until2')]),
                ('forever', [('renew', 0, 'c0', 'forever')]),
                ('lift', [('renew', 0, 'c0', 'forever'), ('renew', 0, 'c0', 'ask')])]
VERSION_WAYS = [(1, 'the version deleted for good with a deletion record'),
                (0, 'the version deleted for good with no deletion record')]
HEAD_WAYS = [(None, 'the summary entry kept'), (1, 'the summary entry deleted with a deletion record'),
             (0, 'the summary entry deleted with no deletion record')]
LOSS_MERGES = [('Synchronize into 1', 1, 0, 'Synchronize'), ('Synchronize into 0', 0, 1, 'Synchronize'),
               ('Overwrite if newer into 1', 1, 0, 'OverwriteIfNewer'),
               ('Overwrite if newer into 0', 0, 1, 'OverwriteIfNewer'),
               ('KeePassXC 2.8.0 into 1', 1, 0, '2.8.0'), ('KeePassXC 2.8.0 into 0', 0, 1, '2.8.0')]


def loss_ops(kind_ops, version_way, head_way):
    """The answer at copy 0, synchronized into copy 1 (the copy from before), the losses at copy 0,
    then Seal again where the store stops and a lift where the answer refuses, each where it applies."""
    ops = list(kind_ops) + [INTO_1, ('del', 0, ('ver', 'renew:c0', 'new'), version_way)]
    if head_way is not None:
        ops.append(('del', 0, ('head', 'new'), head_way))
    return ops + [('reseal', 0), ('see', 0, 'c0'), ('renew', 0, 'c0', 'ask')]


def part_losses(host, tag):
    print('== Lost versions: an answer at copy 0, synchronized into copy 1, lost at copy 0 in each way; the read; Seal')
    print('   again where the store stops; a lift where the answer refuses; then each merge both ways with copy')
    print('   1, which holds the answer from before. Each step that does not apply is named so.')
    tally = {}
    for kind_name, kind_ops in ANSWER_KINDS:
        for version_way, version_text in VERSION_WAYS:
            for head_way, head_text in HEAD_WAYS:
                ops = loss_ops(kind_ops, version_way, head_way)
                print('## %s: %s, %s' % (kind_name, version_text, head_text))
                b = C.run_ops(host, 2, tag, ops)
                for line in b.log:
                    if 'does not apply' in line:
                        print('   step %s' % line.strip())
                print('   copy 0 after the losses and what applied of Seal again and the lift: %s, store %s [%s]' % (
                    answer_line(read_answer(b, 0)), read_answer(b, 0)['status'], C.c_note(b)))
                for x in C.c_lines(b, '      '):
                    print(x)
                for label, d, s, prog in LOSS_MERGES:
                    bb = C.run_ops(host, 2, tag, ops)
                    pre = (read_answer(bb, d), read_answer(bb, s))
                    bb.step(len(ops), C.merge_op(prog, d, s))
                    o, _ = B.outcome(bb, d)
                    r = read_answer(bb, d)
                    over = beyond(r, pre[0], pre[1]) if r['status'] == 'ok' else []
                    own = M.narrower_renewals(r, bb.W.ideal(d))
                    tally[(kind_name, B.cat(o))] = tally.get((kind_name, B.cat(o)), 0) + 1
                    print('   %-26s %s, store %s; %s; %s; oracle %s [%s]' % (
                        label, answer_line(r), r['status'],
                        'BEYOND BOTH COPIES: ' + ', '.join(over) if over else 'within the copies',
                        'BELOW ITS OWN IDEAL: ' + own if own else 'not below its own ideal', o, C.c_note(bb)))
                    for x in C.c_lines(bb, '      '):
                        print(x)
    print('== tally (answer, the oracle\'s class): ' + ', '.join('%s %s %d' % (k[0], k[1], n)
                                                                for k, n in sorted(tally.items())))


# ------------------------------------------------------------------ random
def part_random(host, count, seed, tag):
    rng = random.Random(seed)
    t0 = time.time()
    outs, nsteps, ndis, nread, merges_held, beyond_n, lines = {}, 0, 0, 0, 0, 0, []
    own_outs, own_steps = {}, 0
    for h in range(count):
        R = (0, 1)
        acts = []
        for r in R:
            acts += [('renew', r, c, x) for c in ('c0', 'c1') for x in ('forever', 'until1', 'until2', 'ask')]
            acts += [('see', r, c) for c in ('c0', 'c1')] + [('clock', r, p) for p in range(len(M.CLOCK))]
            acts += [('renewcert', r, c) for c in ('c0', 'c1')] + [('pair', r), ('revoke', r, 'c0')]
        merges = [m for d in R for s in R if d != s for m in
                  [('merge', 'kp_sync', d, s), ('merge', 'kp_other', d, s, 'OverwriteIfNewer'),
                   ('merge', 'xc', d, s, '2.8.0')]]
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(4, 12))]
        b = B.Bridge(host, 2, tag)
        found = []
        for k, op in enumerate(ops):
            pre = None
            if op[0] == 'merge':
                pre = (read_answer(b, op[2]), read_answer(b, op[3]))
            if not b.step(k, op):
                continue
            nsteps += 1
            i = op[2] if op[0] == 'merge' else op[1]
            own = M.narrower_renewals(read_answer(b, i), b.W.ideal(i))
            if own:
                own_steps += 1
                found.append((k, op, 'BELOW ITS OWN IDEAL: ' + own))
            if pre is not None:
                merges_held += 1
                r = read_answer(b, op[2])
                over = beyond(r, pre[0], pre[1]) if r['status'] == 'ok' and r['served'] else []
                if over:
                    beyond_n += 1
                    found.append((k, op, 'BEYOND BOTH COPIES: ' + ', '.join(over)))
        for i in R:
            o, r = B.outcome(b, i)
            outs[B.cat(o)] = outs.get(B.cat(o), 0) + 1
            mine = 'below its own ideal' if M.narrower_renewals(read_answer(b, i), b.W.ideal(i)) else 'not below'
            own_outs[mine] = own_outs.get(mine, 0) + 1
        ndis += len(b.disagree)
        nread += len(b.readdiff)
        if found or b.disagree or b.readdiff:
            lines.append('## history %d: %s' % (h, seq(ops)))
            for k, op, w in found:
                lines.append('   at step %d (%s): %s' % (k, ' '.join(map(str, op)), w))
            lines += C.c_lines(b)
    print('== random histories of answers, lifts, dialogs, clocks, renewals and merges, two copies: %d histories,'
          ' steps %d, entry disagreements %d, read differences %d, %.0f s' % (count, nsteps, ndis, nread,
                                                                             time.time() - t0))
    print('   files at the end against every decision: ' + ', '.join('%s %d' % kv for kv in sorted(outs.items())))
    print('   files at the end against their own ideal: ' + ', '.join('%s %d' % kv for kv in sorted(own_outs.items()))
          + '; steps at which the copy changed answered below its own ideal: %d' % own_steps)
    print('   merges held to both copies just before: %d; that gave an answer below both: %d' % (merges_held, beyond_n))
    for x in lines:
        print(x)


def main():
    sys.stdout.reconfigure(line_buffering=True, newline='\n')
    setup()
    host = kp.Host()
    t0 = time.time()
    try:
        part = sys.argv[1]
        if part == 'cells':
            part_cells(host, '== A1 to A14 on files, two copies, every merge both ways',
                       histories(), 'ac')
        elif part == 'losses':
            part_losses(host, 'al')
        elif part == 'random':
            part_random(host, int(sys.argv[2]), int(sys.argv[3]), 'ar')
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
