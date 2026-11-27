"""ADDED (started again): the store started again in the model, STORE.md 632, 641 and 649, played before
anything of it is built in the product.

Run:  uv run --no-project python -I -X utf8 restart_runs.py PART [ARGS], its output into out\\starts

Parts:
  hand                        T1 to T16 of probe/restart.py in the model, every merge both ways, each
                              merged copy held to its own ideal, to every decision any gate made, to the
                              keys its ideal sets aside and to both copies just before
  laws COUNT SEED             the marking of STORE.md 649 step 4 over hand-made relations and COUNT
                              random sets of relations, held to its laws and to read.py's marking,
                              written apart
  search NREP [COUNT]         the design's twelve configurations over NREP files, COUNT histories each
                              (20,000 by default) with the design's seeds, the store started again and a
                              plugin of a later format in the alphabet; every history failing a property
                              that ends it shrunk and classed
  search-noauth NREP [COUNT]  the same, with no deletion or edit by hand of an entry that can carry a
                              store key, so that the classes behind the first are reached
  starts CONFIG COUNT SEED LO HI
                              random histories of the acts of a store started again: two (two copies),
                              three, hand (two, with deletions and edits by hand, Seal again, deletion
                              records cleared and imports), hand-noauth (hand with no entry that can carry
                              a store key deleted or edited)
  exhaustive DEPTH FAULTS     every sequence up to DEPTH of a small alphabet of a store started again over
                              two copies: FAULTS `none`, `faults` (the first and the newest authority and a
                              keep record deleted by hand) or `noauth` (a keep record alone)
  bridge CONFIG COUNT SEED    random histories of the acts of a store started again, every file written
                              by store.py's Encoder and read by read.py, held to the model's fold

The store is the store of answer_runs.py (the store as decided with the standing answers to a renewal,
a rule or an answer waiting for a save, the reading of lost versions) with crdt_model.STARTS on. Every
random choice is drawn from a random.Random with a seed given here or on the command line.
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
import answer_runs as AR  # noqa: E402

FLAGS = AR.FLAGS
INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)
ANSWER_KIND_OF = AR.kind_of     # the classes of answer_runs.py, before this module's are put in its place


def setup():
    AR.setup()
    M.STARTS = M.SEAL = True     # Seal again as designed beside it: the seal lifts a store a start stopped


def fmt(op):
    return ' '.join(str(x) for x in op)


def state(v):
    """A copy as the model reads it."""
    return '%s, served %s under %s, aside %s, admitted %s' % (
        v['status'], v['served'] or '-', '+'.join(sorted(v['held'])) or '-', '+'.join(sorted(v['aside'])) or '-',
        ' '.join(sorted(v['adm'])) or '-')


def global_ideal(W, i):
    r = M.Rep()
    r.nodes = W.reps[i].nodes
    for k in W.Kever:
        for eid in k:
            r.entries[eid] = [eid, 0, True]
    return W.fold(r, M.IDEAL)


def beyond(A, pres):
    """What A gives that none of the copies `pres` gave, item by item: an authority none served, a
    client's profile none admitted, a node's word none gave, an answer to a renewal below all of
    them on the chain. A copy that serves nothing gives nothing."""
    if A['status'] != 'ok' or not A['served']:
        return None
    pres = [p for p in pres if p['status'] == 'ok' and p['served']]
    if A['served'] not in [p['served'] for p in pres]:
        return 'serves %s' % A['served']
    for c, profs in sorted(A['adm'].items()):
        if not profs <= set().union(*[p['adm'].get(c, frozenset()) for p in pres]):
            return 'client %s' % c
    for n, w in sorted(A['words'].items()):
        if not w <= set().union(*[p['words'].get(n, frozenset()) for p in pres]):
            return 'node %s' % n
    for c, a in sorted(A['renew'].items()):
        if all(M.answer_rank(a) < M.answer_rank(p['renew'].get(c, M.ASK)) for p in pres):
            return 'renewal of %s' % c
    return None


# ------------------------------------------------------------------ hand
PROGRAMS = AR.PROGRAMS


def play(ops):
    import restart as R     # probe/restart.py, for model_op
    W = M.World(2, FLAGS.but(pending=False))
    W.maxclients = 6
    applied = [W.step(R.model_op(op)) for op in ops]
    return W, applied


def part_hand():
    import restart as R
    print('== T1 to T16 in the model, two copies, every merge both ways. Each cell: the merged copy, held to its')
    print('   own ideal, to every decision any gate made, to the keys its ideal sets aside and to both copies just')
    print('   before the merge; then the acts that follow in that copy. 11 edits are an edit here: damage at once.')
    tally = {}
    for hid, what, ops, then in R.histories():
        print('## %s: %s' % (hid, what))
        W, applied = play(ops)
        print('   %s' % ' ; '.join(fmt(op) + ('' if ok else ' (does not apply)') for op, ok in zip(ops, applied)))
        print('   before the merge: copy 0 %s | copy 1 %s' % (state(W.fold(W.reps[0])), state(W.fold(W.reps[1]))))
        for label, prog in PROGRAMS:
            for d, s in ((1, 0), (0, 1)):
                X = W.clone()
                X.maxclients = W.maxclients
                pre = (X.fold(X.reps[d]), X.fold(X.reps[s]))
                X.step(AR.merge_of(prog, d, s))
                v = X.fold(X.reps[d])
                I, G = X.ideal(d), global_ideal(X, d)
                flags = []
                w = M.narrower(v, I, X.reps[d].nodes)
                if w:
                    flags.append('WIDER THAN ITS IDEAL: ' + w)
                if v['status'] == 'ok' and v['served'] and v['held'] & I['aside']:
                    flags.append('SERVES UNDER A KEY ITS IDEAL SETS ASIDE')
                b = beyond(v, pre)
                if b:
                    flags.append('BEYOND BOTH COPIES: ' + b)
                g = M.narrower(v, G, X.reps[d].nodes)
                cls = 'wider than every decision' if g else ('as decided' if not M.narrower_than(v, G, X.reps[d].nodes)
                                                              else 'narrower or refusing')
                after = []
                for op in R.bound(then, d):
                    ok = X.step(R.model_op(op))
                    after.append('%s %s' % (' '.join(map(str, op[:1] + op[2:])), 'applies' if ok else 'refused'))
                for f in flags:
                    tally[f.split(':')[0]] = tally.get(f.split(':')[0], 0) + 1
                key = (hid + (' import' if prog == 'import' else ''), cls)
                tally[key] = tally.get(key, 0) + 1
                print('   %-44s into %d: %s; ideal aside %s; every decision: %s; then %s: %s%s' % (
                    label, d, state(v), '+'.join(sorted(I['aside'])) or '-', cls, ', '.join(after),
                    state(X.fold(X.reps[d])), ''.join('; ' + f for f in flags)))
    flagged = sorted((k, n) for k, n in tally.items() if isinstance(k, str))
    cells = sorted((k, n) for k, n in tally.items() if not isinstance(k, str))
    print('== flagged cells: ' + (', '.join('%s %d' % kv for kv in flagged) or 'none'))
    print('== cells by sequence and against every decision: ' + ', '.join('%s %s %d' % (k[0], k[1], n) for k, n in cells))


# ------------------------------------------------------------------ laws
def laws_hand():
    """(what, keys, relations, standing expected)."""
    A, B_, C_, D_ = 'KA', 'KB', 'KC', 'KD'
    return [
        ('no keep record: both keys stand', [A, B_], [], [A, B_]),
        ('A names B', [A, B_], [(A, B_, 'r1', ())], [A]),
        ('A names B, B names C: B set aside, so its record drops and C stands', [A, B_, C_],
         [(A, B_, 'r1', ()), (B_, C_, 'r2', ())], [A, C_]),
        ('A names B and C', [A, B_, C_], [(A, B_, 'r1', ()), (A, C_, 'r2', ())], [A]),
        ('a circle: A names B, B names A', [A, B_], [(A, B_, 'r1', ()), (B_, A, 'r2', ())], [A, B_]),
        ('a circle settled: then A names B again, naming both earlier records in after', [A, B_],
         [(A, B_, 'r1', ()), (B_, A, 'r2', ()), (A, B_, 'r3', ('r1', 'r2'))], [A]),
        ('a later record replaces an earlier, under another key: A names B; C names A, naming r1', [A, B_, C_],
         [(A, B_, 'r1', ()), (C_, A, 'r2', ('r1',))], [B_, C_]),
        ('a circle A, B names C outside it: C set aside by a key that stands', [A, B_, C_],
         [(A, B_, 'r1', ()), (B_, A, 'r2', ()), (A, C_, 'r3', ())], [A, B_]),
        ('a key that names itself stands', [A, B_], [(A, A, 'r1', ())], [A, B_]),
        ('a record naming a key not found marks nothing', [A, B_], [(A, None, 'r1', ())], [A, B_]),
        ('a deletion record tells of A naming B', [A, B_], [(A, B_, 'd1', ())], [A]),
        ('a three-circle, and a fourth key it names', [A, B_, C_, D_],
         [(A, B_, 'r1', ()), (B_, C_, 'r2', ()), (C_, A, 'r3', ()), (C_, D_, 'r4', ())], [A, B_, C_]),
        ('restarts in a chain: B names A; C names A and B naming r1', [A, B_, C_],
         [(B_, A, 'r1', ()), (C_, A, 'r2', ('r1',)), (C_, B_, 'r3', ('r1',))], [C_]),
        ('two restarts apart: B names A, C names A', [A, B_, C_], [(B_, A, 'r1', ()), (C_, A, 'r2', ())], [B_, C_]),
        ('two circles, A and B, C and D, and A names C: both circles marked in a single round, all stand',
         [A, B_, C_, D_], [(A, B_, 'r1', ()), (B_, A, 'r2', ()), (C_, D_, 'r3', ()), (D_, C_, 'r4', ()),
                           (A, C_, 'r5', ())], [A, B_, C_, D_]),
    ]


def check_laws(keys, rels, standing, aside):
    """The laws of STORE.md 649 step 4 over a marking, as (violations, circles), each a list of text: a
    violation is no key standing, or a key set aside by no counting record under a key that stands; a
    circle is a key that stands although a key that stands names it with no circle leading back, which
    the product's marking gives where circles are marked in the same round."""
    out, circles = [], []
    named = set()
    for r in rels:
        named |= set(r[3])
    edges = set((r[0], r[1]) for r in rels if r[1] in keys and r[2] not in named)
    if not standing:
        out.append('no key stands')
    for k in aside:
        if not any(t == k and s in standing for (s, t) in edges):
            out.append('%s set aside by no record under a key that stands' % k)
    for k in standing:
        for s in [s for (s, t) in edges if t == k and s in standing and s != k]:
            seen, todo, back = set(), [k], False
            while todo:
                x = todo.pop()
                for (a, b) in edges:
                    if a == x and b not in seen:
                        back = back or b == s
                        seen.add(b)
                        todo.append(b)
            if not back:
                circles.append('%s stands though %s, which stands, names it' % (k, s))
    return out, circles


def part_laws(count, seed):
    import read as RD
    print('== the marking of STORE.md 649 step 4: crdt_model.marked and read.py\'s mark_keys, written apart')
    bad = 0
    for what, keys, rels, want in laws_hand():
        st, aside = M.marked(keys, rels)
        st2, aside2 = RD.mark_keys(keys, [(a, b, c, frozenset(d)) for (a, b, c, d) in rels])
        v, circ = check_laws(keys, rels, st, aside)
        ok = sorted(st) == sorted(want) and sorted(st2) == sorted(want) and not v
        bad += not ok
        print('   %-100s stand %-14s %s%s' % (what, '+'.join(st), 'as stated' if ok else 'DIFFERS: read.py %s; %s' % (
            '+'.join(sorted(st2)), '; '.join(v)), ''.join('; ' + c for c in circ)))
    rng = random.Random(seed)
    viol, differ, perm, n_rel, nc = 0, 0, 0, 0, 0
    shapes = {}
    for _ in range(count):
        keys = ['K%d' % k for k in range(rng.randint(1, 6))]
        rels = []
        for j in range(rng.randint(0, 8)):
            after = tuple(r[2] for r in rels if rng.random() < 0.25)
            target = rng.choice(keys + [None]) if rng.random() < 0.95 else None
            rels.append((rng.choice(keys), target, 'r%d' % j, after))
        n_rel += len(rels)
        st, aside = M.marked(keys, rels)
        st2, _ = RD.mark_keys(keys, [(a, b, c, frozenset(d)) for (a, b, c, d) in rels])
        if sorted(st) != sorted(st2):
            differ += 1
        v, circ = check_laws(keys, rels, st, aside)
        viol += bool(v)
        nc += bool(circ)
        sh = list(rels)
        rng.shuffle(sh)
        if sorted(M.marked(list(reversed(keys)), sh + sh[:2])[0]) != sorted(st):
            perm += 1
        shapes[len(st)] = shapes.get(len(st), 0) + 1
    print('   %d random sets of 1 to 6 keys and 0 to 8 relations (%d relations): laws violated %d; read.py\'s marking'
          ' differs %d; the marking changed by the order of the keys, the order of the relations or a relation read'
          ' twice %d' % (count, n_rel, viol, differ, perm))
    print('   sets in which a key stands that a standing key names with no circle leading back, circles marked in a'
          ' single round: %d' % nc)
    print('   keys that stand: ' + ', '.join('%d: %d' % kv for kv in sorted(shapes.items())))
    print('   hand cases not as stated: %d' % bad)


# ------------------------------------------------------------------ classes
def kind_of(ops, prop):
    """The class a failing history falls in, by what it holds once shrunk: a class STORE.md 639, 641 or
    decision 9 accepts, named by its row, a new class, named NEW, or OTHER, a counterexample."""
    acts = set(op[0] for op in ops)
    deleted = [op[2] for op in ops if op[0] in ('del', 'edit')]
    carrier = any(x[0] == 'auth' or x == ('ref', 'retired') for x in deleted)
    first = min([k for k, op in enumerate(ops) if op[0] == 'restart'] or [len(ops)])
    # a loss after a start again of an entry that can carry the new key: the newest authority, or a retirement
    newest = any(carrier_loss(op) and op[2] != ('auth', 'a0') for op in ops[first:])
    if prop.startswith('convergence-to-ideal') and 'not wider' in prop and 'restart' in acts:
        return ('a store started again beside another key that stands, so the copies are split and serve nothing'
                ' until the store is started again or kept (STORE.md 649)')
    if prop.startswith('convergence-to-ideal') and 'not wider' in prop and 'newer' in acts:
        return 'a store raised to a later format, outdated until started again (STORE.md 651, 632)'
    stop_lost = any(op[0] in ('del', 'edit') and op[2] == ('kept',) for op in ops[first:])
    lost_at = min([k for k, op in enumerate(ops) if k >= first and carrier_loss(op) and op[2] != ('auth', 'a0')]
                  or [len(ops)])
    if 'restart' in acts and any(op[0] == 'reseal' for op in ops[lost_at:]) and not stop_lost:
        return ('Seal again keeps the earlier store once the new key is lost, as a store a start stopped is lifted: what was'
                ' decided under the new key is out of it, while the oracle reads the two keys of the circle')
    if 'restart' in acts and stop_lost and not newest and any(op[0] == 'del' and op[2] == ('head', 'new')
                                                             for op in ops[first:]):
        return ('the keep record of a start and the summary entry restating it deleted, so that the stop\'s deletion'
                ' record sets the new key aside (STORE.md 639 rows 2 and 3)')
    if newest and stop_lost and 'create' not in acts:
        return ('NEW: the entry that carries the key of a store started again and the stop under the earlier key'
                ' deleted or edited by hand, so that nothing stops the earlier store, which stands alone again')
    if newest and 'create' not in acts:
        return ('NEW: the entry that carries the key of a store started again deleted or edited, so that key is'
                ' lost and the keep records under it are read no more: the earlier store stands alone again')
    if carrier and 'restart' in acts and 'create' not in acts:
        return ('a store key lost: the earlier authority\'s entry deleted or edited, then the store started again,'
                ' under which what the lost key held is passed over (decision 9)')
    if carrier and 'create' in acts:
        return 'a store key lost: the authority\'s entry deleted or edited, then Create (decision 9)'
    if 'over' in acts:
        return 'a whole file written over another (STORE.md 639 row 1)'
    if any(x == ('kept',) for x in deleted):
        return 'NEW: a keep record deleted by hand'
    return ANSWER_KIND_OF(ops, prop)


CARRIERS = (('auth',), ('auth', 'a0'), ('ref', 'retired'))


def carrier_loss(op):
    """A deletion or an edit by hand of an entry that can carry a store key."""
    return op[0] in ('del', 'edit') and op[2] in CARRIERS


def search_chunk(args):
    noauth, inner = args
    setup()
    if noauth and not getattr(M, '_noauth', False):
        orig = M.alphabet

        def alphabet(nrep, cfg, flags=None):
            acts, merges, faults = orig(nrep, cfg, flags)
            return acts, merges, [f for f in faults if not carrier_loss(f)]
        M.alphabet, M._noauth = alphabet, True
    return AR.config_chunk(inner)


def part_search(nrep, count, workers, noauth=False):
    setup()
    AR.kind_of = kind_of
    print('== the store as decided with the answers and the store started again: the design\'s search, its twelve'
          ' configurations over %d files, %d histories each, its seeds; restart and a plugin of a later format in the'
          ' alphabet, a keep record and the first authority among what a person deletes and edits%s' % (
              nrep, count, '; no deletion or edit by hand of an authority\'s entry or of a retirement, the entries'
              ' that carry a store key' if noauth else ''))
    t0 = time.time()
    per = 1000
    jobs = []
    for k, (name, cfg) in enumerate(M.CONFIGS):
        for c in range(0, count, per):
            jobs.append((noauth, (name, cfg, nrep, count, 100 + k + 10 * nrep, c, min(per, count - c), 'design')))
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(search_chunk, jobs)
    jobs = [j[1] for j in jobs]
    for k, (name, cfg) in enumerate(M.CONFIGS):
        mine = [r for j, r in zip(jobs, res) if j[0] == name]
        # the histories are played again as config_chunk played them: a configuration of merges alone
        # is also held to its ideal once the copies have converged
        flags = FLAGS.but()
        flags.mode = 'clean' if cfg in ('', 'synconly') else ''
        lines, _ = AR.report('%-66s %d files %6d histories' % (name, nrep, count), mine, nrep, flags, (True,))
        for x in lines:
            print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ the acts of a store started again
CLIENTS = ('c0', 'c1')


def start_alphabet(nrep, config):
    R = range(nrep)
    acts = []
    for r in R:
        acts += [('restart', r), ('pair', r), ('revoke', r, 'c0'), ('revoke', r, 'c1'), ('padd', r, 'pf'),
                 ('prem', r, 'pf'), ('rule', r, 'N', 'rw'), ('rule', r, 'N', 'none'), ('renew', r, 'c0', 'forever'),
                 ('renew', r, 'c0', 'ask'), ('renewcert', r, 'c0'), ('renewcert', r, 'c1'), ('delauth', r),
                 ('create', r), ('newer', r), ('recreate', r), ('save', r), ('quit', r)]
        if config in ('hand', 'hand-noauth'):
            for sel in (('auth', 'a0'), ('auth',), ('ref', 'retired'), ('kept',), ('head', 'new')):
                acts += [('del', r, sel, rec) for rec in (0, 1)]
            acts += [('edit', r, ('auth',)), ('edit', r, ('auth', 'a0')), ('reseal', r), ('clear', r)]
    if config == 'hand-noauth':
        acts = [a for a in acts if not carrier_loss(a)]
    merges = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in R for s in R if d != s]
    if config in ('hand', 'hand-noauth'):
        merges += [('import', d, s) for d in R for s in R if d != s]
    return acts, merges


def start_chunk(args):
    config, nrep, seed, n, lo, hi = args
    setup()
    flags = FLAGS.but()
    flags.mode = ''
    rng = random.Random(seed)
    acts, merges = start_alphabet(nrep, config)
    counts, kept, shortest = {}, [], {}
    for h in range(n):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(lo, hi))]
        once = set()
        for f in M.run(ops, nrep, flags):
            if not M.ends(f[0]):
                if f[0] in once:
                    continue
                once.add(f[0])
            key = (True, f[0])
            counts[key] = counts.get(key, 0) + 1
            if key not in shortest or len(ops) < len(shortest[key]):
                shortest[key] = ops
            if M.ends(f[0]):
                kept.append((True, h, ops, f))
    return counts, kept, shortest, {}


def part_starts(config, count, seed, lo, hi, workers):
    setup()
    AR.kind_of = kind_of
    nrep = 3 if config == 'three' else 2
    acts, merges = start_alphabet(nrep, config)
    print('== %d histories of the acts of a store started again over %d copies, %d to %d steps, seed %d: %d acts'
          ' (a start again, a pairing, revocations, a name added and removed, rules, an answer and a lift, renewals,'
          ' Delete, Create, Recreate, a plugin of a later format, a save, KeePass ending before a save%s) and %d'
          ' merges (Synchronize, Overwrite if newer, KeePassXC%s), an act drawn with 0.6' % (
              count, nrep, lo, hi, seed, len(acts),
              ', the first and the newest authority, a retirement, a keep record and the newest summary entry deleted'
              ' with a deletion record or with none, the newest and the first authority edited, Seal again, every'
              ' deletion record cleared' if config == 'hand' else
              ', a keep record and the newest summary entry deleted with a deletion record or with none, Seal again,'
              ' every deletion record cleared; no entry that can carry a store key deleted or edited'
              if config == 'hand-noauth' else '', len(merges),
              ', Create new IDs' if config in ('hand', 'hand-noauth') else ''))
    t0 = time.time()
    per = 500
    jobs = [(config, nrep, seed * 100003 + c, min(per, count - c * per), lo, hi) for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(start_chunk, jobs)
    for j, r in zip(jobs, res):
        base = (j[2] - seed * 100003) * per
        r[1][:] = [(l_, h + base, ops, f) for (l_, h, ops, f) in r[1]]
    lines, _ = AR.report('   histories', res, nrep, FLAGS, (True,))
    for x in lines:
        print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ exhaustive
def exhaustive_steps(faults):
    """`faults`: 'none'; 'faults', the first and the newest authority deleted with a deletion record,
    Seal again and a keep record deleted with a deletion record or with none; 'noauth', the keep
    record alone."""
    steps = []
    for r in (0, 1):
        steps += [('restart', r), ('pair', r), ('rule', r, 'N', 'rw'), ('revoke', r, 'c0'), ('newer', r),
                  ('delauth', r), ('create', r)]
        if faults == 'faults':
            steps += [('del', r, ('auth', 'a0'), 1), ('del', r, ('auth',), 1), ('reseal', r)]
        if faults in ('faults', 'noauth'):
            steps += [('del', r, ('kept',), 0), ('del', r, ('kept',), 1)]
    steps += [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in (0, 1) for s in (0, 1) if d != s]
    return steps


def exhaustive_chunk(args):
    depth, faults, prefix = args
    setup()
    steps = exhaustive_steps(faults)
    total, bad, classes = 0, {}, {}
    for rest in itertools.product(steps, repeat=depth - 1):
        seq = (prefix,) + rest
        total += 1
        for f in M.run(list(seq), 2, FLAGS, check_conv=True):
            key = f[0]
            if key not in bad or len(seq) < len(bad[key][1]):
                bad[key] = (bad.get(key, (0,))[0], seq, f)
            bad[key] = (bad[key][0] + 1, bad[key][1], bad[key][2])
            if M.ends(key):     # each failing sequence classed as it stands, unshrunk
                c = (key, kind_of(list(seq), key))
                classes[c] = classes.get(c, 0) + 1
    return total, bad, classes


def part_exhaustive(depth, faults, workers):
    setup()
    steps = exhaustive_steps(faults)
    t0 = time.time()
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(exhaustive_chunk, [(depth, faults, s) for s in steps])
    total, bad, classes = 0, {}, {}
    for t, b, cl in res:
        total += t
        for c, n in cl.items():
            classes[c] = classes.get(c, 0) + n
        for key, (n, s, f) in b.items():
            if key not in bad:
                bad[key] = (n, s, f)
            elif len(s) < len(bad[key][1]):
                bad[key] = (bad[key][0] + n, s, f)
            else:
                bad[key] = (bad[key][0] + n, bad[key][1], bad[key][2])
    print('== every sequence of %d steps of the acts of a store started again over two copies: %d steps wide%s, from'
          ' the store the model starts with, the convergence checked at the end: %d histories  %s' % (
              depth, len(steps), {'faults': ' with the first and the newest authority deleted with a deletion record,'
                                            ' Seal again and a keep record deleted with a deletion record and with none',
                                  'noauth': ' with a keep record deleted with a deletion record and with none'}.get(faults, ''),
              total,
              'no failure' if not bad else ', '.join('%s x%d' % (p, c) for p, (c, _, _) in sorted(bad.items()))))
    for p, (c, s, f) in sorted(bad.items()):
        print('    first shortest %s (%s): %s   [%s]' % (p, f[2], ' ; '.join(fmt(op) for op in s), kind_of(list(s), p)))
    for (p, k), n in sorted(classes.items()):
        print('    %s, %s: %d sequences' % (p, k, n))
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ the bridge to read.py
def bridge_chunk(args):
    config, seed, count, lo, hi = args
    setup()
    import read as RD
    import store as S
    RD.ANSWERS, RD.JUDGE, S.HEX = True, 'lost', True
    RD.STARTS = RD.ROOM = RD.SEAL = True
    if 'kept' not in S.KINDS:
        S.KINDS = S.KINDS + S.STARTED
    rng = random.Random(seed)
    acts, merges = start_alphabet(2, config)
    compared, differs, states = 0, [], {}
    nodes = {'root': 'aa' * 16, 'N': 'bb' * 16}
    for _ in range(count):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(lo, hi))]
        W = M.World(2, FLAGS)
        W.maxclients = 6
        enc = S.Encoder(W, nodes)
        for op in ops:
            i = op[2] if op[0] == 'merge' else op[1]
            if not W.step(op):
                continue
            compared += 1
            rep = W.view(i)
            entries = {}
            for eid, ok in sorted((eid, ok) for u, (eid, mt, ok) in rep.entries.items() if u == eid):
                u, title, s, prot = enc.entry(eid)
                if not ok:      # an entry edited: its content changed, so that no version passes
                    s = dict(s, **{'KPG.Data': s['KPG.Data'] + ' '})
                entries[u] = {'strings': s, 'history': [], 'place': 'S', 'parent': nodes['root']}
            d = {'root': nodes['root'], 'groups': {nodes['N']: {'parent': nodes['root']}},
                 'dels': {enc.u(x): 0 for x in rep.dels}, 'entries': entries}
            r = RD.read(d, history=True)
            v = W.fold(rep)
            states[v['status']] = states.get(v['status'], 0) + 1
            mine = (v['status'], v['served'], sorted(v['held']), sorted(v['aside']), sorted(v['adm']))
            theirs = (r['status'], r['served'], r['keys']['standing'], r['keys']['aside'],
                      sorted(S.NAMES.get(c, c) for c in r['adm']))
            if mine != theirs:
                differs.append((ops, op, mine, theirs))
    return compared, differs, states


def part_bridge(config, count, seed, workers):
    setup()
    t0 = time.time()
    per = 100
    jobs = [(config, seed * 100003 + c, min(per, count - c * per), 4, 12) for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(bridge_chunk, jobs)
    compared = sum(c for c, d, s in res)
    differs = [x for c, d, s in res for x in d]
    states = {}
    for c, d, s in res:
        for k, n in s.items():
            states[k] = states.get(k, 0) + n
    print('== %d histories of the acts of a store started again (%s) over two copies, 4 to 12 steps, seed %d: after'
          ' every step that applied, the file the copy it changed holds on disk written by store.py\'s Encoder and'
          ' read by read.py, held to the model\'s fold of it (the state, the authority served, the key that stands,'
          ' the keys set aside, the clients admitted)' % (count, config, seed))
    print('   steps compared %d (%s); steps that differ %d' % (
        compared, ', '.join('%s %d' % kv for kv in sorted(states.items())), len(differs)))
    for ops, op, a, b in differs[:5]:
        print('      %s, at %s: model %s, read.py %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), a, b))
    print('== %.0f s' % (time.time() - t0))


def main():
    sys.stdout.reconfigure(newline='\n')
    setup()
    part = sys.argv[1]
    if part == 'hand':
        part_hand()
    elif part == 'laws':
        part_laws(int(sys.argv[2]), int(sys.argv[3]))
    elif part in ('search', 'search-noauth'):
        part_search(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 20000,
                    int(sys.argv[4]) if len(sys.argv) > 4 else 1, part == 'search-noauth')
    elif part == 'starts':
        part_starts(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]),
                    int(sys.argv[7]) if len(sys.argv) > 7 else 1)
    elif part == 'exhaustive':
        part_exhaustive(int(sys.argv[2]), sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'bridge':
        part_bridge(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]) if len(sys.argv) > 5 else 1)
    else:
        raise SystemExit('unknown part ' + part)


if __name__ == '__main__':
    main()
