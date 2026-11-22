"""The standing answers to a renewal in the model: STORE.md 618 and S7, played before anything of
them is built in the product.

Run:  uv run --no-project python -I -X utf8 answer_runs.py PART [ARGS], its output into out\\answers

Parts:
  hand                        A1 to A14 of probe/answers.py in the model, every merge both ways, each
                              merged copy read at each position of the clock beside its own ideal under
                              both oracles and every decision any gate made; the sequences whose two
                              writes are made apart played in both orders of those writes
  laws COUNT SEED             the meet over the chain: every pair and triple of a set of texts; and
                              COUNT random pairs of files under a perfect transport
  search NREP [COUNT]         the design's twelve configurations over NREP files, COUNT histories each
                              (20,000 by default) with the design's seeds, the answers' acts in the
                              alphabet; every history failing a property kept, each failing `safety`
                              shrunk and classed
  answers CONFIG COUNT SEED LO HI
                              random histories of the answers' acts alone: two (two copies), three,
                              hand (two, with deletions by hand, Seal again and deletion records
                              cleared), long (two, LO to HI steps); each played under both oracles
  exhaustive DEPTH FAULTS     every sequence up to DEPTH of the answers' alphabet over two copies, with
                              the deletions by hand where FAULTS is `faults`
  bridge CONFIG COUNT SEED    random histories of the answers' alphabet, every file written by
                              store.py's Encoder and read by read.py, held to the model's fold
  all OUT WORKERS [PARTS]     every part of PARTS below, each into OUT\\<part>.txt

The store is the store as decided (crdt_model.STORE with no repair), with a rule set on a node's
dialog and an answer or a lift waiting in memory for the next save (`pending`, with the acts `save`
and `quit`), rules as a version of a node's whole rule, and the reading of lost versions of
`measured-lost-settled` (JUDGE `lost`), so that a lift names in `lost` what it settles. Every
random choice is drawn from a random.Random with a seed given here or on the command line.

The clock is watched: World.clock is replaced by a list that counts every read, by the act or the
operation it is read in, and a read inside a fold, a merge, a write of a summary entry or an act
other than a dialog, an answer or a renewal is printed as a failure.
"""
import itertools
import multiprocessing
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'probe'))
import crdt_model as M  # noqa: E402

FLAGS = M.STORE.but(repair=False)
CLIENTS = ('c0', 'c1')
VALUES = ('forever', 'until1', 'until2', 'ask')
INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)


# ------------------------------------------------------------------ the clock, watched
CONTEXT = ['outside']
READS = {}


class WatchedClock(list):
    """A gate's clock that counts each read by the innermost operation it is read in."""

    def __getitem__(self, k):
        READS[CONTEXT[-1]] = READS.get(CONTEXT[-1], 0) + 1
        return list.__getitem__(self, k)


def watched(name, f):
    def g(self, *a, **kw):
        CONTEXT.append(name(a) if callable(name) else name)
        try:
            return f(self, *a, **kw)
        finally:
            CONTEXT.pop()
    return g


ALLOWED = ('act:see', 'act:renew', 'act:renewcert', 'act:clock', 'the oracle of a renewal issued')


def setup():
    """The model configured for the answers, and the clock watched."""
    M.ANSWERS = True
    M.JUDGE = 'lost'
    M.IDEAL_LOST = True
    if getattr(M.World, '_watched', False):
        return
    init = M.World.__init__

    def __init__(self, nrep, flags=M.FULL):
        init(self, nrep, flags)
        self.clock = WatchedClock(self.clock)

    clone = M.World.clone

    def clone_(self):
        w = clone(self)
        w.clock = WatchedClock(self.clock)
        return w
    M.World.__init__ = __init__
    M.World.clone = clone_
    M.World.fold = watched('fold', M.World.fold)
    M.World.merge = watched('merge', M.World.merge)
    M.World.write_head = watched('write of a summary entry', M.World.write_head)
    M.World.act = watched(lambda a: 'act:' + a[1][0], M.World.act)
    run = M.run

    def run_(*a, **kw):
        CONTEXT.append('the oracle of a renewal issued')
        try:
            return run(*a, **kw)
        finally:
            CONTEXT.pop()
    M.run = run_
    M.World._watched = True


def clock_lines():
    bad = sorted((k, n) for k, n in READS.items() if k not in ALLOWED)
    out = ['   the clock read: ' + ', '.join('%s %d' % kv for kv in sorted(READS.items()) if kv[0] in ALLOWED)
           + '; elsewhere: ' + (', '.join('%s %d FAILS' % kv for kv in bad) if bad else 'never')]
    return out


# ------------------------------------------------------------------ reading
def fmt(op):
    return ' '.join(str(x) for x in op)


def token(v, c):
    tok = v['renew'].get(c, M.ASK)
    why = v.get('renew_why', {}).get(c, 'none')
    return '%s (%s)%s' % (M.answer_label(tok), M.refusing_at(tok), ' ' + why if why in ('lost', 'unreadable') else '')


def global_ideal(W, i, lost=True):
    r = M.Rep()
    r.nodes = W.reps[i].nodes
    for k in W.Kever:
        for eid in k:
            r.entries[eid] = [eid, 0, True]
    keep = M.IDEAL_LOST
    M.IDEAL_LOST = lost
    try:
        return W.fold(r, M.IDEAL)
    finally:
        M.IDEAL_LOST = keep


def own_ideal(W, i, lost=True):
    keep = M.IDEAL_LOST
    M.IDEAL_LOST = lost
    try:
        return W.ideal(i)
    finally:
        M.IDEAL_LOST = keep


def below(v, I, c):
    return v['status'] == 'ok' and v['served'] and \
        M.answer_rank(v['renew'].get(c, M.ASK)) < M.answer_rank(I['renew'].get(c, M.ASK))


# ------------------------------------------------------------------ hand
PROGRAMS = [('Synchronize', 'kp_sync'), ('Overwrite if newer, existing, keep existing', 'kp_other'),
            ('KeePassXC', 'xc'), ('Create new IDs', 'import')]


def merge_of(prog, d, s):
    return ('import', d, s) if prog == 'import' else ('merge', prog, d, s)


def play_hand(ops):
    W = M.World(2, FLAGS.but(pending=False))
    W.maxclients = 4
    applied = []
    for op in ops:
        applied.append(W.step(op))
    return W, applied


def apart_orders(hist):
    """A sequence whose writes about the client are made apart in the two copies, also played with
    those writes in the other order, so that the other version is the later by its time."""
    out = []
    for hid, what, ops, then in hist:
        out.append((hid, what, ops, then))
        if hid in ('A2', 'A3', 'A11'):
            k = [n for n, op in enumerate(ops) if op[0] == 'renew'][:2]
            swapped = list(ops)
            swapped[k[0]], swapped[k[1]] = ops[k[1]], ops[k[0]]
            out.append((hid + "'", what + '; the two writes in the other order', swapped, then))
    return out


def zeroed(rep):
    """The same file with every modification time and every deletion record's time set to 0."""
    r = rep.clone()
    r.entries = {u: [e[0], 0, e[2]] for u, e in r.entries.items()}
    r.dels = {u: 0 for u in r.dels}
    return r


def part_hand():
    import answers as A     # probe/answers.py, for its sequences
    print('== A1 to A14 in the model, two copies, every merge both ways; A2, A3 and A11 also with their two')
    print('   writes in the other order (marked \'), so that the other version is the later by its time. Each')
    print('   cell: the merged copy\'s answer, the positions of the clock at which it refuses (0 before both')
    print('   moments, 1 between, 2 after), its own ideal with a lift\'s lost counted as seen and with after')
    print('   alone, every decision any gate made, both copies just before; then the acts that follow.')
    print('   Moments: %s; clock: %s' % (', '.join(M.MOMENTS), ', '.join(M.CLOCK)))
    play_cells(apart_orders(A.histories()))


def part_losses():
    import answers as A
    print('== Lost versions in the model, as answers.py plays them on files: an answer at copy 0 (until the later')
    print('   moment, forever, a lift of forever), synchronized into copy 1, its newest version deleted at copy 0')
    print('   with a deletion record (1) or with none (0), the newest summary entry kept (-), deleted with a')
    print('   deletion record (1) or with none (0); then Seal again where the store stops, the dialog, and a lift')
    print('   where the answer refuses, each where it applies; then every merge both ways with copy 1, from')
    print('   before. Each cell as in the part hand.')
    seqs = []
    for kind_name, kind_ops in A.ANSWER_KINDS:
        for version_way, version_text in A.VERSION_WAYS:
            for head_way, head_text in A.HEAD_WAYS:
                seqs.append(('%s, version %d, summary entry %s' % (kind_name, version_way, '-' if head_way is None else head_way),
                             '%s; %s; %s' % (kind_name, version_text, head_text), A.loss_ops(kind_ops, version_way, head_way),
                             [('renewcert', 'D', 'c0')]))
    play_cells(seqs)


def play_cells(seqs):
    import answers as A
    tally, same, times = {}, {}, [0, 0]
    for hid, what, ops, then in seqs:
        print('## %s: %s' % (hid, what))
        W, applied = play_hand(ops)
        print('   %s' % ' ; '.join(fmt(op) + ('' if ok else ' (does not apply)') for op, ok in zip(ops, applied)))
        print('   before the merge: copy 0 %s | copy 1 %s' % (
            ', '.join('%s %s' % (c, token(W.fold(W.reps[0]), c)) for c in W.clients),
            ', '.join('%s %s' % (c, token(W.fold(W.reps[1]), c)) for c in W.clients)))
        for label, prog in PROGRAMS:
            for d, s in ((1, 0), (0, 1)):
                X = W.clone()
                pre = (X.fold(X.reps[d]), X.fold(X.reps[s]))
                X.step(merge_of(prog, d, s))
                v = X.fold(X.reps[d])
                times[0] += 1
                z = X.fold(zeroed(X.reps[d]))
                if (z['status'], z['renew'], z['renew_why']) != (v['status'], v['renew'], v['renew_why']):
                    times[1] += 1
                I, Is, G = own_ideal(X, d), own_ideal(X, d, False), global_ideal(X, d)
                after = []
                for op in A.bound(then, d):
                    ok = X.step(op)
                    if op[0] == 'renewcert':
                        after.append('%s %s %s' % (op[0], op[2], 'issued' if ok else 'refused'))
                flags = []
                for c in X.clients:
                    if c not in ('c0', 'c1'):
                        continue
                    if below(v, I, c):
                        flags.append('BELOW ITS IDEAL for %s' % c)
                    if below(v, Is, c) and not below(v, I, c):
                        flags.append('below its ideal by after alone for %s' % c)
                    a = M.answer_rank(v['renew'].get(c, M.ASK))
                    if v['status'] == 'ok' and all(a < M.answer_rank(p['renew'].get(c, M.ASK)) for p in pre):
                        flags.append('BEYOND BOTH COPIES for %s' % c)
                key = (hid.rstrip("'"), label, d)
                answer = ', '.join('%s %s' % (c, token(v, c)) for c in X.clients if c in ('c0', 'c1'))
                same.setdefault(key, set()).add(answer + v['status'])
                for f in flags:
                    tally[f.split(' for ')[0]] = tally.get(f.split(' for ')[0], 0) + 1
                print('   %-44s into %d: %s, store %s; ideal %s, by after alone %s, every decision %s; '
                      'then %s%s' % (
                          label, d, answer, v['status'],
                          ', '.join('%s %s' % (c, M.answer_label(I['renew'].get(c, M.ASK))) for c in CLIENTS if c in X.clients),
                          ', '.join('%s %s' % (c, M.answer_label(Is['renew'].get(c, M.ASK))) for c in CLIENTS if c in X.clients),
                          ', '.join('%s %s' % (c, M.answer_label(G['renew'].get(c, M.ASK))) for c in CLIENTS if c in X.clients),
                          ', '.join(after) or 'nothing', ''.join('; ' + f for f in flags)))
    print('== flagged cells: ' + (', '.join('%s %d' % kv for kv in sorted(tally.items())) or 'none'))
    diff = sorted(k for k, xs in same.items() if len(xs) > 1)
    pairs = [k for k in same if any(h[0] == k[0] + "'" for h in seqs)]
    if pairs:
        print('== cells of A2, A3 and A11 whose answer differs between the two orders of the writes: %d of %d' % (
            len([k for k in diff if k in pairs]), len(pairs)))
    for k in diff:
        print('   %s %s into %d: %s' % (k[0], k[1], k[2], ' | '.join(sorted(same[k]))))
    print('== merged copies read again with every modification time and every deletion record\'s time set to 0:'
          ' %d, whose answer or store differs %d' % tuple(times))
    for x in clock_lines():
        print(x)


# ------------------------------------------------------------------ laws
TEXTS = [M.FOREVER, M.ASK, M.UNTIL + M.MOMENTS[0], M.UNTIL + M.MOMENTS[1], M.UNTIL + '2026-12-31T23:00:00Z',
         M.OUT_OF_FORM, M.UNTIL + '2026-12-14T23:00:00.000Z', 'refuse:Forever']


def part_laws(count, seed):
    print('== the meet over the chain: %d texts, %d in form' % (len(TEXTS), sum(M.answer_read(t) is not None for t in TEXTS)))
    bad = 0
    n = 0
    for k in (1, 2, 3):
        for combo in itertools.product(TEXTS, repeat=k):
            n += 1
            vals = [('ans', t) for t in combo]
            outs = set()
            for perm in itertools.permutations(vals):
                f = perm[0]
                for g in perm[1:]:
                    f = M.meet(f, g)
                outs.add(f)
            if len(outs) != 1:
                bad += 1
                continue
            f = outs.pop()
            if M.meet(f, f) != f:
                bad += 1
            top = max(M.answer_key(t) for t in combo)
            if M.answer_key(f[1]) != top:
                bad += 1
            tok, why = M.answer_token(f)
            for c in M.CLOCK:   # the meet refuses wherever any of the texts refuses
                if any(M.answer_refuses(M.answer_token(('ans', t))[0], c) for t in combo) and not M.answer_refuses(tok, c):
                    bad += 1
    print('   %d sequences of 1 to 3 texts in every order: %d violations (order, idempotence, the stricter, '
          'refusing wherever a text refuses)' % (n, bad))
    mono = 0
    for t in TEXTS:
        for u in TEXTS:
            a, b = M.answer_token(('ans', t))[0], M.answer_token(('ans', u))[0]
            if M.answer_rank(a) <= M.answer_rank(b) and any(M.answer_refuses(a, c) and not M.answer_refuses(b, c)
                                                            for c in M.CLOCK):
                mono += 1
    print('   refusing is monotone in the chain at every position of the clock: %d violations of %d pairs' % (
        mono, len(TEXTS) ** 2))
    rng = random.Random(seed)
    acts = []
    for r in (0, 1):
        acts += [('renew', r, c, x) for c in CLIENTS for x in VALUES] + [('pair', r), ('clock', r, 1), ('clock', r, 0)]
    bad, checked = 0, 0
    for _ in range(count):
        W = M.World(2, FLAGS.but(pending=False))
        for _ in range(rng.randint(2, 9)):
            W.step(rng.choice(acts if rng.random() < 0.7 else [INTO_1, INTO_0]))
        r = M.Rep()
        r.nodes = dict(W.reps[0].nodes)
        for eid in W.K[0] | W.K[1]:
            r.entries[eid] = [eid, 0, True]
        j = W.fold(r, M.IDEAL)
        sides = [W.ideal(0), W.ideal(1)]
        for c in W.clients:
            obj = 'renew:' + c
            named = set()
            for e in j['vers'].get(obj, {}).values():
                named |= e.after | e.lost
            for k, side in enumerate(sides):
                for vid, e in side['vers'].get(obj, {}).items():
                    sd = set()
                    for x in side['vers'][obj].values():
                        sd |= x.after | x.lost
                    if vid in sd or vid in named:
                        continue
                    checked += 1
                    if M.answer_rank(j['renew'][c]) < M.answer_rank(M.answer_token(e.data)[0]):
                        bad += 1
    print('   %d random pairs of files under a perfect transport: %d versions standing in a copy and named by '
          'nothing of the other; the joined file below such a version: %d' % (count, checked, bad))


# ------------------------------------------------------------------ the search over the design's configurations
def answer_alphabet(nrep, config):
    R = range(nrep)
    acts = []
    for r in R:
        acts += [('renew', r, c, x) for c in CLIENTS for x in VALUES]
        acts += [('see', r, c) for c in CLIENTS] + [('clock', r, p) for p in range(len(M.CLOCK))]
        acts += [('renewcert', r, c) for c in CLIENTS] + [('pair', r), ('revoke', r, 'c0'), ('save', r), ('quit', r)]
        if config == 'hand':
            for c in CLIENTS:
                acts += [('del', r, ('ver', 'renew:' + c, 'new'), rec) for rec in (0, 1)]
            acts += [('del', r, ('head', 'new'), rec) for rec in (0, 1)] + [('reseal', r), ('clear', r)]
            acts += [('lose', r, 'renew:' + c) for c in CLIENTS] + [('unreadable', r, 'c0')]
    merges = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in R for s in R if d != s]
    return acts, merges


def kind_of(ops, prop):
    """The class a failing history falls in, by what it holds once shrunk: a class STORE.md 639 or
    decision 9 accepts, named by its row, or OTHER, which is a counterexample."""
    acts = set(op[0] for op in ops)
    deleted = [op[2] for op in ops if op[0] in ('del', 'edit')]
    if any(x == ('auth',) for x in deleted) and 'create' in acts:
        return 'a store key lost: the authority\'s entry deleted or edited, then Create (decision 9)'
    if 'over' in acts:
        return 'a whole file written over another (STORE.md 639 row 1)'
    decision = any(x[0] in ('ver', 'ref', 'rec', 'prem', 'padd') for x in deleted) or 'lose' in acts
    head = any(x[0] == 'head' for x in deleted)
    plain = any(op[0] == 'merge' and op[1] in ('kp_other', 'xc', 'dx') for op in ops) or 'import' in acts
    if prop == 'sealed-after-a-loss':
        return 'Seal again after a loss in both carriers with no deletion record (STORE.md 639 row 5)'
    if 'clear' in acts and decision:
        return 'every deletion record removed while a loss stands (STORE.md 639 row 4)'
    if decision and head and plain:
        return ('a decision and its summary entry deleted, and a merge that carries no deletion record '
                '(STORE.md 639 rows 2 and 3)')
    if decision and head and all(op[3] == 0 for op in ops if op[0] == 'del'):
        return 'a decision and its summary entry deleted with no deletion record (STORE.md 639 row 3)'
    return 'OTHER: ' + ', '.join(sorted(acts))


def config_chunk(args):
    name, cfg, nrep, count, seed, first, n, what = args
    setup()
    READS.clear()
    flags = FLAGS.but()
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    rng = random.Random(seed)
    if what == 'design':
        acts, merges, faults = M.alphabet(nrep, cfg, flags)
    else:
        acts, merges = answer_alphabet(nrep, cfg)
        faults = []
    counts, kept, shortest = {}, [], {}
    lo, hi = (3, 9) if what == 'design' else what
    for h in range(first + n):
        ops = []
        for _ in range(rng.randint(lo, hi)):
            x = rng.random()
            if what == 'design':
                pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            else:
                pool = acts if x < 0.6 else merges
            ops.append(rng.choice(pool))
        if h < first:
            continue
        sides = (True, False) if what != 'design' else (True,)
        for lost in sides:
            M.IDEAL_LOST = lost
            once = set()
            for f in M.run(ops, nrep, flags):
                if not M.ends(f[0]):
                    if f[0] in once:
                        continue
                    once.add(f[0])
                key = (lost, f[0])
                counts[key] = counts.get(key, 0) + 1
                if key not in shortest or len(ops) < len(shortest[key]):
                    shortest[key] = ops
                if M.ends(f[0]):
                    kept.append((lost, h, ops, f))
        M.IDEAL_LOST = True
    return counts, kept, shortest, dict(READS)


def shrink(ops, nrep, flags, prop, lost):
    M.IDEAL_LOST = lost
    try:
        return M.shrink(ops, nrep, flags, prop)
    finally:
        M.IDEAL_LOST = True


def replay(ops, nrep, flags):
    """A history step by step: what each copy answers about each client, and its own ideal."""
    W = M.World(nrep, flags)
    out = []
    for k, op in enumerate(ops):
        ok = W.step(op)
        cells = []
        for i in range(nrep):
            v, I = W.fold(W.reps[i]), W.ideal(i)
            cells.append('copy %d %s %s, ideal %s' % (i, v['status'], ', '.join(
                '%s %s' % (c, M.answer_label(v['renew'].get(c, M.ASK))) for c in W.clients if c in CLIENTS),
                ', '.join('%s %s' % (c, M.answer_label(I['renew'].get(c, M.ASK))) for c in W.clients if c in CLIENTS)))
        out.append('          %d. %s%s: %s' % (k + 1, fmt(op), '' if ok else ' (does not apply)', '; '.join(cells)))
    return out


def report(title, results, nrep, flags, sides, per=None):
    """Counts, the shortest of each property, and every failing `safety` history shrunk and classed."""
    counts, kept, shortest, reads = {}, [], {}, {}
    for c, k, s, r in results:
        for key, n in c.items():
            counts[key] = counts.get(key, 0) + n
        kept += k
        for key, ops in s.items():
            if key not in shortest or len(ops) < len(shortest[key]):
                shortest[key] = ops
        for key, n in r.items():
            reads[key] = reads.get(key, 0) + n
    lines = []
    for lost in sides:
        name = 'a lift\'s lost counted as seen' if lost else 'after alone'
        cs = sorted((p, n) for (l_, p), n in counts.items() if l_ == lost)
        lines.append('%s; oracle %s: %s' % (title, name, ', '.join('%s x%d' % x for x in cs) or 'no failure'))
        for p, n in cs:
            if M.ends(p):
                continue
            ops = shortest[(lost, p)]
            small = shrink(ops, nrep, flags, p, lost) if p.startswith('accepted:') else ops
            lines.append('    shortest %s: %s' % (p, ' ; '.join(fmt(op) for op in small)))
        # every history that failed a property that ends it, shrunk by that property and classed; each
        # distinct shrunk history printed once, a counterexample with every step
        classes, seen = {}, {}
        for l_, h, ops, f in kept:
            if l_ != lost:
                continue
            small = shrink(ops, nrep, flags, f[0], lost)
            M.IDEAL_LOST = lost
            detail = [x for x in M.run(small, nrep, flags) if x[0] == f[0]][0][2]
            M.IDEAL_LOST = True
            subject = 'a renewal answer' if 'renewal' in detail else 'something else'
            k = kind_of(small, f[0])
            classes.setdefault((f[0], subject, k), []).append(h)
            seen.setdefault((f[0], subject, k, tuple(small)), (h, detail, ops))
        for (p, subject, k), hs in sorted(classes.items()):
            mine = [(key, x) for key, x in sorted(seen.items(), key=lambda x: (len(x[0][3]), x[1][0]))
                    if key[:3] == (p, subject, k)]
            lines.append('    %s, about %s, %s: %d histories, %d distinct once shrunk%s' % (
                p, subject, k, len(hs), len(mine), '' if k.startswith('OTHER') or len(mine) <= 3 else
                ', the three shortest'))
            for n, ((p2, s2, k2, small), (h, detail, ops)) in enumerate(mine):
                if n >= 3 and not k.startswith('OTHER'):
                    break
                lines.append('        #%d, shrunk to %d steps: %s   (%s)' % (h, len(small), ' ; '.join(fmt(op) for op in small), detail))
                if k.startswith('OTHER'):
                    lines.append('        COUNTEREXAMPLE, drawn: %s' % ' ; '.join(fmt(op) for op in ops))
                    M.IDEAL_LOST = lost
                    lines += replay(list(small), nrep, flags)
                    M.IDEAL_LOST = True
    return lines, reads


def part_search(nrep, count, workers):
    setup()
    print('== the store as decided with the answers: the design\'s search, its twelve configurations over %d files,'
          ' %d histories each, its seeds; the answers\' acts in the alphabet (four answers about c0 and c1, a'
          ' dialog, the clock), a rule and an answer waiting for a save, the reading of lost versions' % (nrep, count))
    t0 = time.time()
    per = 1000
    jobs = []
    for k, (name, cfg) in enumerate(M.CONFIGS):
        for c in range(0, count, per):
            jobs.append((name, cfg, nrep, count, 100 + k + 10 * nrep, c, min(per, count - c), 'design'))
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(config_chunk, jobs)
    reads = {}
    for k, (name, cfg) in enumerate(M.CONFIGS):
        mine = [r for j, r in zip(jobs, res) if j[0] == name]
        lines, rd = report('%-66s %d files %6d histories' % (name, nrep, count), mine, nrep, FLAGS, (True,))
        for x in lines:
            print(x)
        for key, n in rd.items():
            reads[key] = reads.get(key, 0) + n
    READS.clear()
    READS.update(reads)
    for x in clock_lines():
        print(x)
    print('== %.0f s' % (time.time() - t0))


def part_answers(config, count, seed, lo, hi, workers):
    setup()
    nrep = 3 if config == 'three' else 2
    acts, merges = answer_alphabet(nrep, config)
    print('== %d histories of the answers\' acts alone over %d copies, %d to %d steps, seed %d: %d acts (four answers'
          ' about c0 and c1, a dialog, the clock, a renewal, a pairing, a revocation, a save, KeePass ending before'
          ' a save%s) and %d merges (Synchronize, Overwrite if newer, KeePassXC), an act drawn with 0.6; each history'
          ' under both oracles' % (count, nrep, lo, hi, seed, len(acts), ', deletions by hand of an answer\'s'
          ' newest version and of the newest summary entry with a deletion record or with none, Seal again, every'
          ' deletion record cleared, a lost answer, a text out of form' if config == 'hand' else '', len(merges)))
    t0 = time.time()
    per = 500
    jobs = [(config, config, nrep, count, seed * 100003 + c, 0, min(per, count - c * per), (lo, hi))
            for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(config_chunk, jobs)
    for j, r in zip(jobs, res):     # each chunk numbers its histories from 0: renumber them
        base = (j[4] - seed * 100003) * per
        r[1][:] = [(l_, h + base, ops, f) for (l_, h, ops, f) in r[1]]
    lines, rd = report('   histories', res, nrep, FLAGS, (True, False))
    for x in lines:
        print(x)
    READS.clear()
    READS.update(rd)
    for x in clock_lines():
        print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ exhaustive
def exhaustive_steps(faults):
    steps = []
    for r in (0, 1):
        steps += [('renew', r, 'c0', x) for x in VALUES] + [('see', r, 'c0'), ('clock', r, 1), ('clock', r, 2),
                                                          ('renewcert', r, 'c0'), ('save', r), ('quit', r)]
        if faults:
            steps += [('del', r, ('ver', 'renew:c0', 'new'), rec) for rec in (0, 1)]
            steps += [('del', r, ('head', 'new'), rec) for rec in (0, 1)] + [('reseal', r)]
    steps += [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in (0, 1) for s in (0, 1) if d != s]
    return steps


def exhaustive_chunk(args):
    depth, faults, prefix = args
    setup()
    READS.clear()
    steps = exhaustive_steps(faults)
    total, bad = 0, {}
    for rest in itertools.product(steps, repeat=depth - 1):
        seq = (prefix,) + rest
        total += 1
        for f in M.run(list(seq), 2, FLAGS, check_conv=True):
            key = f[0]
            if key not in bad or len(seq) < len(bad[key][1]):
                bad[key] = (bad.get(key, (0,))[0], seq, f)
            bad[key] = (bad[key][0] + 1, bad[key][1], bad[key][2])
    return total, bad, dict(READS)


def part_exhaustive(depth, faults, workers):
    setup()
    steps = exhaustive_steps(faults)
    t0 = time.time()
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(exhaustive_chunk, [(depth, faults, s) for s in steps])
    total, bad, reads = 0, {}, {}
    for t, b, r in res:
        total += t
        for key, (n, s, f) in b.items():
            if key not in bad:
                bad[key] = (n, s, f)
            elif len(s) < len(bad[key][1]):
                bad[key] = (bad[key][0] + n, s, f)
            else:
                bad[key] = (bad[key][0] + n, bad[key][1], bad[key][2])
        for key, n in r.items():
            reads[key] = reads.get(key, 0) + n
    print('== every sequence of %d steps of the answers\' alphabet over two copies, client c0: %d steps wide%s, '
          'from the store the model starts with, the convergence checked at the end: %d histories  %s' % (
              depth, len(steps), ' with the deletions by hand and Seal again' if faults else '', total,
              'no failure' if not bad else ', '.join('%s x%d' % (p, c) for p, (c, _, _) in sorted(bad.items()))))
    for p, (c, s, f) in sorted(bad.items()):
        print('    first shortest %s (%s): %s' % (p, f[2], ' ; '.join(fmt(op) for op in s)))
    READS.clear()
    READS.update(reads)
    for x in clock_lines():
        print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ the bridge to read.py
def bridge_chunk(args):
    config, seed, count, lo, hi = args
    setup()
    READS.clear()
    import read as RD
    import store as S
    RD.ANSWERS, RD.JUDGE, S.HEX = True, 'lost', True
    rng = random.Random(seed)
    acts, merges = answer_alphabet(2, config)
    compared, differs = 0, []
    nodes = {'root': 'aa' * 16, 'N': 'bb' * 16}
    for _ in range(count):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(lo, hi))]
        W = M.World(2, FLAGS)
        enc = S.Encoder(W, nodes)
        for op in ops:
            i = op[2] if op[0] == 'merge' else op[1]
            if not W.step(op):
                continue
            compared += 1
            rep = W.view(i)
            entries = {}
            for eid in sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid and ok):
                u, title, s, prot = enc.entry(eid)
                entries[u] = {'strings': s, 'history': [], 'place': 'S', 'parent': nodes['root']}
            d = {'root': nodes['root'], 'groups': {nodes['N']: {'parent': nodes['root']}},
                 'dels': {enc.u(x): 0 for x in rep.dels}, 'entries': entries}
            r = RD.read(d, history=True)
            v = W.fold(rep)
            mine = sorted((c, a, v['renew_why'][c]) for c, a in v['renew'].items() if a != 'ask')
            theirs = sorted((S.NAMES.get(c, c), a, r['renew_detail'][c]['cause']) for c, a in r['renew'].items()
                            if a != 'ask')
            if mine != theirs or (v['status'] == 'ok') != (r['status'] == 'ok'):
                differs.append((ops, op, mine, theirs, v['status'], r['status']))
    return compared, differs


def part_bridge(config, count, seed, workers):
    setup()
    t0 = time.time()
    per = 100
    jobs = [(config, seed * 100003 + c, min(per, count - c * per), 4, 12) for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(bridge_chunk, jobs)
    compared = sum(c for c, d in res)
    differs = [x for c, d in res for x in d]
    print('== %d histories of the answers\' alphabet %s over two copies, 4 to 12 steps, seed %d: after every step that'
          ' applied, the file the copy it changed holds on disk written by store.py\'s Encoder, every certificate and'
          ' client key in hexadecimal, and read by read.py, held to the model\'s fold of it (every answer that is not'
          ' ask, with its cause, and whether the store is whole)' % (count, config, seed))
    print('   steps compared %d; steps that differ %d' % (compared, len(differs)))
    for ops, op, a, b, s0, s1 in differs[:5]:
        print('      %s, at %s: model %s %s, read.py %s %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), s0, a, s1, b))
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ all
PARTS = [('hand', 'hand', ()), ('losses', 'losses', ()), ('laws', 'laws', (20000, 7)),
         ('exhaustive-4', 'exhaustive', (4, True)), ('exhaustive-5', 'exhaustive', (5, False)),
         ('answers-two', 'answers', ('two', 200000, 31, 4, 12)),
         ('answers-three', 'answers', ('three', 200000, 32, 4, 12)),
         ('answers-hand', 'answers', ('hand', 200000, 33, 4, 12)),
         ('answers-long', 'answers', ('two', 200000, 34, 8, 16)),
         ('search-2', 'search', (2, 20000)), ('search-3', 'search', (3, 20000)),
         ('bridge-two', 'bridge', ('two', 2000, 41)), ('bridge-hand', 'bridge', ('hand', 2000, 42))]


def part_all(out, workers, only=None):
    import contextlib
    os.makedirs(out, exist_ok=True)
    for name, part, args in PARTS:
        if only and name not in only:
            continue
        t0 = time.time()
        READS.clear()
        with open(os.path.join(out, name + '.txt'), 'w', encoding='utf-8', newline='\n') as f:
            with contextlib.redirect_stdout(f):
                if part == 'hand':
                    part_hand()
                elif part == 'losses':
                    part_losses()
                elif part == 'laws':
                    part_laws(*args)
                elif part == 'exhaustive':
                    part_exhaustive(*args, workers)
                elif part == 'answers':
                    part_answers(*args, workers)
                elif part == 'search':
                    part_search(*args, workers)
                else:
                    part_bridge(*args, workers)
        print('%s: %.0f s' % (name, time.time() - t0), flush=True)


def main():
    sys.stdout.reconfigure(newline='\n')
    setup()
    part = sys.argv[1]
    if part == 'hand':
        part_hand()
    elif part == 'losses':
        part_losses()
    elif part == 'laws':
        part_laws(int(sys.argv[2]), int(sys.argv[3]))
    elif part == 'search':
        part_search(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 20000,
                    int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'answers':
        part_answers(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]),
                     int(sys.argv[7]) if len(sys.argv) > 7 else 1)
    elif part == 'exhaustive':
        part_exhaustive(int(sys.argv[2]), sys.argv[3] == 'faults', int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'bridge':
        part_bridge(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]) if len(sys.argv) > 5 else 1)
    elif part == 'all':
        part_all(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1,
                 sys.argv[4].split(',') if len(sys.argv) > 4 else None)
    else:
        raise SystemExit('unknown part ' + part)


if __name__ == '__main__':
    main()
