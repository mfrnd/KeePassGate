"""ADDED (started again): the store started again on real files, STORE.md 632, 641 and 649.

Run:  uv run --no-project python -I -X utf8 restart.py PART [COUNT SEED]

Every part plays its sequences through bridge.Bridge under bridge.starts(): the store as decided with
the standing answers to a renewal, the reading of lost versions, every certificate and client key in
hexadecimal, and the keys of STORE.md 649 with the command of 632 that starts the store again, in the
model (crdt_model.STARTS) and in read.py (read.STARTS, read.ROOM). Every gate act is the model's write
path, written into a real .kdbx by KeePassLib 2.62; every merge is KeePassLib's or keepassxc-cli's;
every file is read by read.py, whose reading of the keys knows nothing of the model's.

A copy is printed as its state, the authority it serves and the key that stands, the keys set aside
and the clients admitted. A merged file is held to the oracle (every decision any gate made,
bridge.global_ideal), to its own ideal (the same file had every record it received arrived by a
perfect transport, crdt_model.World.ideal), to both copies just before the merge, and to the keys its
ideal sets aside: a copy that serves under a key its ideal sets aside is flagged.

Parts:
  cells    T1 to T12, each a base, then every merge both ways (KeePassLib's five methods, four
           keepassxc-cli releases), and the acts that follow in the copy merged into; T11 and T12
           replay the class the model's search found, the key of the store started again lost
  room     a store at the ceiling of the items of a summary entry, and a store at the ceiling of the
           `after` of its content, each read, started again and read again
  random   random histories of the acts of a store started again, deletions by hand and merges,
           COUNT of them from SEED, with Synchronize, Overwrite if newer and KeePassXC 2.8.0
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
import read as RD  # noqa: E402
import crdt_model as M  # noqa: E402

INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)
EDITS = ['char'] + ['title'] * 10     # the dialog edit and 10 more: past KeePass's history limit
MAXCLIENTS = 6


def seq(ops):
    return ' ; '.join(' '.join(map(str, op)) for op in ops)


def histories():
    """(label, what, ops, then): the acts up to the merge of the cell, and the acts that follow it in
    the copy merged into, 'D' standing for that copy. The model's World starts with the authority a0
    under the key K, the name pf, the client c0 admitted and the root's rule of read in both copies."""
    then = [('pair', 'D'), ('renewcert', 'D', 'c0')]
    return [
        ('T1', 'copy 0 starts the store again on a whole store, then a name, a pairing and a rule under the '
               'new key; copy 1 holds the store from before',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('rule', 0, 'N', 'rw')], then),
        ('T2', 'the newest summary entry deleted at copy 0 with a deletion record, so the store stops there; '
               'copy 0 starts it again, adds a name and pairs',
         [('del', 0, ('head', 'new'), 1), ('restart', 0), ('padd', 0, 'pf'), ('pair', 0)], then),
        ('T3', 'two first authorities made apart: at copy 1 the authority\'s entry deleted with no deletion '
               'record and Create, synchronized both ways, so both copies are split; copy 0 starts the store '
               'again, adds a name and pairs',
         [('del', 1, ('auth', 'a0'), 0), ('create', 1), INTO_0, INTO_1, ('restart', 0), ('padd', 0, 'pf'),
          ('pair', 0)], then),
        ('T4', 'copy 0 starts the store again and pairs c1; copy 1, stale, still serves the earlier store and '
               'pairs c2',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('pair', 1)],
         [('renewcert', 'D', 'c2'), ('renewcert', 'D', 'c1')]),
        ('T5', 'copy 0 starts the store again and pairs; the earlier authority\'s entry deleted at copy 0 with '
               'a deletion record; copy 1 still holds it',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('del', 0, ('auth', 'a0'), 1)], then),
        ('T6', 'both copies start the store again apart; then, in the copy merged into, a start again of the '
               'merged file, a name and a pairing',
         [('restart', 0), ('restart', 1)], [('restart', 'D'), ('padd', 'D', 'pf'), ('pair', 'D')]),
        ('T7', 'copy 0 starts the store again and pairs, synchronized into copy 1; copy 0 starts it again and '
               'pairs again',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), INTO_1, ('restart', 0), ('padd', 0, 'pf'),
          ('pair', 0)], [('renewcert', 'D', 'c1'), ('pair', 'D')]),
        ('T8', 'a plugin of a later format raises the store\'s format at copy 0, synchronized into copy 1, so '
               'both are outdated; copy 0 starts the store again, adds a name and pairs',
         [('newer', 0), INTO_1, ('restart', 0), ('padd', 0, 'pf'), ('pair', 0)], then),
        ('T9', 'copy 0 starts the store again, then deletes the new authority with none after it, so the '
               'retirement carries the new key, then Create, a name and a pairing',
         [('restart', 0), ('delauth', 0), ('create', 0), ('padd', 0, 'pf'), ('pair', 0)], then),
        ('T10', 'copy 0 starts the store again and pairs; at copy 1 the earlier authority\'s entry, which '
                'carries the earlier key, edited 11 times, past KeePass\'s history limit',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('edit11', 1, ('auth', 'a0'))], then),
        # the class the model's search found, replayed on files: the key of the store started again lost
        ('T11', 'copy 0 starts the store again and pairs; the new authority\'s entry, which carries the new key, '
                'deleted at copy 0 for good with a deletion record',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('del', 0, ('auth',), 1)], then),
        ('T12', 'copy 0 starts the store again and pairs; the new authority\'s entry edited 11 times at copy 0, past '
                'KeePass\'s history limit',
         [('restart', 0), ('padd', 0, 'pf'), ('pair', 0), ('edit11', 0, ('auth',))], then),
    ]


def setup():
    B.starts()


def bound(ops, d):
    return [tuple(d if x == 'D' else x for x in op) for op in ops]


def model_op(op):
    """The step as the model plays it: 11 edits are an edit, which the model reads as damage at once."""
    return ('edit', op[1], op[2]) if op[0] == 'edit11' else op


def step(b, k, op):
    """A step of a sequence on files: `edit11` is the dialog edit and 10 more through KeePassLib, after
    which the entry has no version that passes; every other step is bridge.Bridge.step."""
    b.W.maxclients = MAXCLIENTS     # a World cloned for a step keeps no ceiling of its own
    if op[0] != 'edit11':
        return b.step(k, op)
    W = b.W
    i = op[1]
    u = W.pick(W.reps[i], op[2])
    if not u:
        return False
    P = W.clone()
    P.step(('edit', i, op[2]))
    t = W.t + 10
    for e in EDITS:
        b.h.run('history-edit', b.files[i], b.uid(u), t, e, 'id')
        t += 60
    P.t = t + 10
    b.W = P
    b.enc.W = P
    ents, loc, dels, d = b.real(i)
    b.adopt_real(i, ents, loc, dels)
    b.readcheck(i, d, k)
    b.log.append('%2d %s' % (k, ' '.join(map(str, op))))
    return True


def run_ops(host, nrep, tag, ops):
    b = B.Bridge(host, nrep, tag)
    for k, op in enumerate(ops):
        if not step(b, k, op):
            b.log.append('%2d %s (does not apply)' % (k, ' '.join(map(str, op))))
    return b


def read(b, i):
    return b.as_model(B.rd(b.dump(i), B.DECIDED))


def line(r):
    """A copy as read.py reads it."""
    ks = r['keys']
    return '%s, served %s under %s, aside %s, admitted %s' % (
        r['status'], r['served'] or '-', '+'.join(ks['standing']) or '-', '+'.join(ks['aside']) or '-',
        ' '.join(sorted(r['adm'])) or '-')


def aside_flag(b, i, r):
    """The keys the copy serves under that its own ideal sets aside: a keep record that reached it."""
    if r['status'] != 'ok' or not r['served']:
        return []
    gone = sorted(set(r['keys']['standing']) & set(b.W.ideal(i)['aside']))
    return ['SERVES UNDER %s, WHICH ITS OWN IDEAL SETS ASIDE' % '+'.join(gone)] if gone else []


def beyond(r, pre0, pre1):
    """What the merged copy gives that neither copy gave just before the merge, item by item: an
    authority neither served, a client's profile neither admitted, a node's word neither gave, an
    answer to a renewal below both on the chain. A copy that serves nothing gives nothing."""
    if r['status'] != 'ok' or not r['served']:
        return None
    pres = [p for p in (pre0, pre1) if p['status'] == 'ok' and p['served']]
    if r['served'] not in [p['served'] for p in pres]:
        return 'serves %s' % r['served']
    for c, profs in sorted(r['adm'].items()):
        if not profs <= set().union(*[p['adm'].get(c, frozenset()) for p in pres]):
            return 'client %s holds %s' % (c, ' '.join(sorted(profs)))
    for n, w in sorted(r['words'].items()):
        if not w <= set().union(*[p['words'].get(n, frozenset()) for p in pres]):
            return 'node %s grants %s' % (n, M.words_text(w))
    for c, a in sorted(r['renew'].items()):
        if all(M.answer_rank(a) < M.answer_rank(p['renew'].get(c, M.ASK)) for p in pres):
            return 'renewal of %s %s' % (c, M.answer_label(a))
    return None


def part_cells(host, tag, only=None):
    print('== T1 to T12 on files, two copies, every merge both ways. Each cell: what the merged file reads as,')
    print('   held to both copies just before the merge, to its own ideal, to the keys its ideal and the oracle')
    print('   set aside, and to the oracle; then what the acts after the merge do in that copy. C: steps where')
    print('   the file and the model disagreed, entry by entry and in the read.')
    tally = {}
    for hid, what, ops, then in histories():
        if only and hid not in only:
            continue
        print('## %s: %s' % (hid, what))
        print('   %s' % seq(ops))
        b0 = run_ops(host, 2, tag, ops)
        for x in b0.log:
            if 'does not apply' in x:
                print('   step %s' % x.strip())
        print('   before the merge: copy 0 %s | copy 1 %s [%s]' % (line(read(b0, 0)), line(read(b0, 1)), C.c_note(b0)))
        for x in C.c_lines(b0, '      '):
            print(x)
        for label, prog in C.PROGRAMS:
            cells = []
            for d, s in ((1, 0), (0, 1)):
                b = run_ops(host, 2, tag, ops)
                pre = (read(b, d), read(b, s))
                step(b, len(ops), C.merge_op(prog, d, s))
                o, _ = B.outcome(b, d)
                r = read(b, d)
                over = beyond(r, pre[0], pre[1])
                own = M.narrower(r, b.W.ideal(d), B.JUDGE)
                flags = aside_flag(b, d, r)
                after = []
                for k, op in enumerate(bound(then, d)):
                    ok = step(b, len(ops) + 1 + k, op)
                    after.append('%s %s' % (' '.join(map(str, op[:1] + op[2:])), 'applies' if ok else 'refused'))
                r2 = read(b, d)
                imp = ' import' if prog == 'CreateNewUuids' else ''
                key = (hid + imp, B.cat(o), 'BEYOND' if over else 'within', 'WIDER THAN ITS OWN IDEAL' if own else 'own ideal',
                       'ASIDE COUNTS' if flags else 'aside holds')
                tally[key] = tally.get(key, 0) + 1
                cells.append('into %d: %s; %s; %s; %s; oracle %s; then %s: %s [%s]' % (
                    d, line(r), 'BEYOND BOTH COPIES: ' + over if over else 'within the copies',
                    'WIDER THAN ITS OWN IDEAL: ' + own if own else 'not wider than its own ideal',
                    '; '.join(flags) or 'nothing served under a key set aside', o, ', '.join(after) or 'nothing',
                    line(r2), C.c_note(b)))
                cells += C.c_lines(b, '      ')
            print('   %-22s %s' % (label, cells[0]))
            for x in cells[1:]:
                print('   %-22s %s' % ('', x))
    print('== tally (sequence, the oracle\'s class, against both copies just before the merge, against its own ideal,'
          ' the keys set aside):')
    for key, n in sorted(tally.items()):
        print('   %s: %d' % (' | '.join(key), n))
    print('   cells %d; beyond both copies %d; wider than their own ideal %d; serving under a key set aside %d' % (
        sum(tally.values()), sum(n for k, n in tally.items() if k[2] == 'BEYOND'),
        sum(n for k, n in tally.items() if k[3] != 'own ideal'), sum(n for k, n in tally.items() if k[4] != 'aside holds')))


# ------------------------------------------------------------------ room
def head_line(r):
    room = r['keys']['room']
    return '%s; next summary entry names %d summary entries in %d characters of content, %d items: %s' % (
        line(r), room['named'], room['content'], room['items'], 'room' if room['has'] else 'NO ROOM')


def part_room(host, tag):
    print('== No room on files: a store composed at the ceiling of the items of a summary entry, and a store at')
    print('   the ceiling of the after of its content (StoreFormat.MaxItems %d, MaxWritten %d), each read, started'
          % (RD.MAX_ITEMS, RD.MAX_WRITTEN))
    print('   again and read again. The model holds no ceiling, so C names the read before the start.')
    beside = len(B.S.dumps({'after': '', 'height': '2', 'nonce': '0' * 32, 'v': '2'}))
    for what in ('items', 'after'):
        b = B.Bridge(host, 1, tag + what[0])
        W = b.W
        rep = W.reps[0]
        before = rep.clone()
        t = W.now()
        if what == 'items':
            # the next summary entry holds the format item, every revocation and the root's rule
            n = RD.MAX_ITEMS - 2
            for k in range(n):
                e = W.mk('ref', 'cx%d' % k, data=('revoked', 'cx%d' % k))
                rep.entries[e.id] = [e.id, t, True]
                W.K[0].add(e.id)
            print('## %d revocations, so that the next summary entry would hold %d items' % (n, n + 2))
        else:
            # a summary entry whose after fits; the next would name it as well and not fit
            n = (RD.MAX_WRITTEN + 1 - beside) // 33
            for k in range(n - 1):
                e = W.mk('head', 'store', height=1, data={'ref': frozenset(), 'prem': frozenset(), 'regs': frozenset()})
                rep.dels[e.id] = t
            print('## %d summary entries deleted with deletion records, so that the summary entry over them names %d'
                  ' and the next would name %d' % (n - 1, n, n + 1))
        W.K[0].add(W.write_head(rep, set()))
        W.Kever[0] |= W.K[0]
        d = b.gate_write(0, before)
        b.readcheck(0, d, 0)
        print('   written; read: %s [%s]' % (head_line(read(b, 0)), C.c_note(b)))
        for x in C.c_lines(b, '      '):
            print(x)
        b.disagree, b.readdiff = [], []
        ok = step(b, 1, ('restart', 0))
        print('   started again: %s; read: %s [%s]' % ('applies' if ok else 'refused', head_line(read(b, 0)), C.c_note(b)))
        ok = step(b, 2, ('padd', 0, 'pf')) and step(b, 3, ('pair', 0))
        print('   a name and a pairing: %s; read: %s [%s]' % ('apply' if ok else 'refused', head_line(read(b, 0)), C.c_note(b)))
        for x in C.c_lines(b, '      '):
            print(x)


# ------------------------------------------------------------------ random
def alphabet():
    R = (0, 1)
    acts = []
    for r in R:
        acts += [('restart', r), ('pair', r), ('revoke', r, 'c0'), ('revoke', r, 'c1'), ('padd', r, 'pf'),
                 ('prem', r, 'pf'), ('rule', r, 'N', 'rw'), ('rule', r, 'N', 'none'), ('renew', r, 'c0', 'forever'),
                 ('renew', r, 'c0', 'ask'), ('renewcert', r, 'c0'), ('delauth', r), ('create', r), ('newer', r),
                 ('del', r, ('auth', 'a0'), 1), ('del', r, ('auth',), 1), ('del', r, ('kept',), 0)]
    merges = [m for d in R for s in R if d != s for m in
              [('merge', 'kp_sync', d, s), ('merge', 'kp_other', d, s, 'OverwriteIfNewer'), ('merge', 'xc', d, s, '2.8.0')]]
    return acts, merges


def part_random(host, count, seed, tag):
    rng = random.Random(seed)
    acts, merges = alphabet()
    t0 = time.time()
    outs, nsteps, ndis, nread, held, beyond_n, wide, asides, lines = {}, 0, 0, 0, 0, 0, 0, 0, []
    for h in range(count):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(4, 12))]
        b = B.Bridge(host, 2, tag)
        found = []
        for k, op in enumerate(ops):
            pre = (read(b, op[2]), read(b, op[3])) if op[0] == 'merge' else None
            if not step(b, k, op):
                continue
            nsteps += 1
            i = op[2] if op[0] == 'merge' else op[1]
            r = read(b, i)
            own = M.narrower(r, b.W.ideal(i), B.JUDGE)
            if own:
                wide += 1
                found.append((k, op, 'WIDER THAN ITS OWN IDEAL: ' + own))
            fl = aside_flag(b, i, r)
            if fl:
                asides += 1
                found.append((k, op, '; '.join(fl)))
            if pre is not None:
                held += 1
                over = beyond(r, pre[0], pre[1])
                if over:
                    beyond_n += 1
                    found.append((k, op, 'BEYOND BOTH COPIES: ' + over))
        for i in (0, 1):
            o, r = B.outcome(b, i)
            outs[B.cat(o)] = outs.get(B.cat(o), 0) + 1
        ndis += len(b.disagree)
        nread += len(b.readdiff)
        if found or b.disagree or b.readdiff:
            lines.append('## history %d: %s' % (h, seq(ops)))
            for k, op, w in found:
                lines.append('   at step %d (%s): %s' % (k, ' '.join(map(str, op)), w))
            lines += C.c_lines(b)
    print('== random histories of a store started again, deletions by hand and merges, two copies: %d histories,'
          ' steps %d, entry disagreements %d, read differences %d, %.0f s' % (count, nsteps, ndis, nread, time.time() - t0))
    print('   files at the end against every decision: ' + ', '.join('%s %d' % kv for kv in sorted(outs.items())))
    print('   steps at which the copy changed read wider than its own ideal: %d; served under a key its ideal or the'
          ' oracle sets aside: %d' % (wide, asides))
    print('   merges held to both copies just before: %d; that gave what neither copy gave: %d' % (held, beyond_n))
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
            part_cells(host, 'sc', sys.argv[2].split(',') if len(sys.argv) > 2 else None)
        elif part == 'room':
            part_room(host, 'so')
        elif part == 'random':
            part_random(host, int(sys.argv[2]), int(sys.argv[3]), 'sr')
        else:
            raise SystemExit('unknown part ' + part)
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
