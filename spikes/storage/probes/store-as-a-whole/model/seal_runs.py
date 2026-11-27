"""Seal again in the model: PLAN 4.1 of the pull request that builds it, STORE.md 624 and 625, played
before anything of it is built in the product.

Run:  uv run --no-project python -I -X utf8 seal_runs.py PART [ARGS], its output into out\\seal

Parts:
  hand                        R1 to R21 of probe/seal.py in the model, every merge both ways, each
                              merged copy beside its own ideal, every decision any gate made and both
                              copies just before; then R15 with SEAL off, where Create writes on the
                              stopped store; R16 to R18 with the seal and the act written apart beside
                              the single write; and every sequence with the seal composed over what
                              its dialog showed (SEAL_COMPOSE 'shown', the control) beside the design
  search NREP [COUNT]         the design's twelve configurations over NREP files, COUNT histories each
                              (20,000 by default) with the design's seeds, the seal's dialog and the
                              seal beside an act in the alphabet where deletions by hand are
  seal CONFIG COUNT SEED LO HI
                              random histories of the seal's alphabet: two (two copies), three, long
                              (two, LO to HI steps)
  damage COUNT                the histories of configurations F, I and K over two files in which a seal
                              applied after an edit: how many leave an object refusing by the deletion
                              record of a damaged version the seal deleted (PLAN 4.1 item 2)
  exhaustive DEPTH            every sequence up to DEPTH of a narrow seal alphabet over two copies
                              after a fixed divergence
  bridge CONFIG COUNT SEED    random histories of the seal's alphabet, every file written by store.py's
                              Encoder and read by read.py with its listing, held to the model's fold
                              and to the seal the model writes next
  all OUT WORKERS [PARTS]     every part of PARTS below, each into OUT\\<part>.txt

The store is the configuration of `answer_runs.py`: the store as decided (crdt_model.STORE with no
repair), a rule, an answer, a lift and the seal waiting in the open database for the next save
(`pending`), rules as a version of a node's whole rule, the standing answers to a renewal and the
reading of lost versions (JUDGE `lost`); with crdt_model.SEAL on. Every random choice is drawn from a
random.Random with a seed given here or on the command line.
"""
import hashlib
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
HAND_FLAGS = FLAGS.but(pending=False)
CLIENTS = ('c0', 'c1')


def setup():
    M.ANSWERS = True
    M.JUDGE = 'lost'
    M.IDEAL_LOST = True
    M.SEAL = True
    M.SEAL_COMPOSE = 'write'
    # ADDED (seal restates): the form these parts were run in, the control, unless the environment
    # names another in SEAL_RESTATES (which reaches every worker a part starts)
    M.SEAL_RESTATES = os.environ.get('SEAL_RESTATES', 'all')


def fmt(op):
    return ' '.join(str(x) if not isinstance(x, tuple) else '(' + fmt(x) + ')' for x in op)


def view(v):
    """A copy's reading, as the parts print it."""
    if v['status'] != 'ok':
        return 'store %s' % v['status']
    return 'served %s, admitted %s, N %s, root %s, renewals %s, names %s' % (
        v['served'], ' '.join('%s:%s' % (c, ''.join(sorted(p))) for c, p in sorted(v['adm'].items())) or 'none',
        M.words_text(v['words'].get('N', frozenset())) or 'nothing', M.words_text(v['words'].get('root', frozenset())) or 'nothing',
        ' '.join('%s:%s' % (c, M.answer_label(v['renew'].get(c, M.ASK))) for c in CLIENTS if c in v['renew']),
        ' '.join(sorted(v['names'])) or 'none')


def global_ideal(W, i):
    r = M.Rep()
    r.nodes = W.reps[i].nodes
    for k in W.Kever:
        for eid in k:
            r.entries[eid] = [eid, 0, True]
    return W.fold(r, M.IDEAL)


def beyond(v, pre):
    """What a merged copy grants that neither copy granted just before the merge."""
    if v['status'] != 'ok' or not v['served']:
        return ''
    out = []
    for c, p in sorted(v['adm'].items()):
        if all(c not in x['adm'] or not p <= x['adm'][c] for x in pre):
            out.append('client %s admitted' % c)
    for n in ('root', 'N'):
        extra = v['words'].get(n, frozenset())
        for x in pre:
            extra = extra - (x['words'].get(n, frozenset()) if x['status'] == 'ok' else frozenset())
        if extra:
            out.append('%s gives %s' % (n, M.words_text(extra)))
    for c, tok in sorted(v['renew'].items()):
        if all(M.answer_rank(tok) < M.answer_rank(x['renew'].get(c, M.ASK)) for x in pre):
            out.append('renewal of %s %s' % (c, tok))
    return '; '.join(out)


# ------------------------------------------------------------------ hand
PROGRAMS = [('Synchronize', 'kp_sync'), ('Overwrite if newer, existing, keep existing', 'kp_other'),
            ('KeePassXC', 'xc'), ('Create new IDs', 'import')]


def merge_of(prog, d, s):
    return ('import', d, s) if prog == 'import' else ('merge', prog, d, s)


def play(ops, flags=HAND_FLAGS):
    W = M.World(2, flags)
    W.maxclients = 5
    applied = [W.step(op) for op in ops]
    return W, applied


def sealed_versions(W, before_ids):
    """The versions granting nothing a seal wrote: object, value, after and lost."""
    out = []
    for u, (eid, mt, ok) in sorted(W.reps[0].entries.items()):
        e = W.EL[eid]
        if u not in before_ids and e.kind == 'ver' and M.grants_nothing(e.obj, e.data):
            out.append('%s %s after %d lost %d' % (e.obj, M.row_text(e.data) if e.data[0] != 'ans' else e.data[1],
                                                   len(e.after), len(e.lost)))
    return out


def part_hand():
    import seal as SL      # probe/seal.py, for its sequences
    print('== R1 to R21 in the model, two copies, every merge both ways. Each sequence: copy 0 before the seal and')
    print('   after it, the versions the seal wrote, then each merge of copy 0 into copy 1 and back, the merged copy')
    print('   beside its own ideal, every decision any gate made (the oracle) and both copies just before.')
    tally = {}
    for hid, what, ops, then in SL.histories():
        print('## %s: %s' % (hid, what))
        cut = max(k for k, op in enumerate(ops) if op[0] in ('reseal', 'reseal+'))
        W0, ap0 = play(ops[:cut])
        W, applied = play(ops)
        print('   %s' % ' ; '.join(fmt(op) + ('' if ok else ' (does not apply)') for op, ok in zip(ops, applied)))
        v0 = W0.fold(W0.reps[0])
        print('   copy 0 before the seal: %s, lost %s' % (v0['status'], ', '.join(sorted(v0['lost'])) or 'nothing'))
        v = W.fold(W.reps[0])
        print('   copy 0 after the seal: %s; the seal wrote: %s; own ideal: %s' % (
            view(v), '; '.join(sealed_versions(W, set(W0.reps[0].entries))) or 'no version',
            M.narrower(v, W.ideal(0), W.reps[0].nodes) or 'within'))
        for label, prog in PROGRAMS:
            for d, s in ((1, 0), (0, 1)):
                X = W.clone()
                pre = (X.fold(X.reps[d]), X.fold(X.reps[s]))
                X.step(merge_of(prog, d, s))
                x = X.fold(X.reps[d])
                own = M.narrower(x, X.ideal(d), X.reps[d].nodes)
                every = M.narrower(x, global_ideal(X, d), X.reps[d].nodes)
                M.IDEAL_LOST = False
                alone = M.narrower(x, X.ideal(d), X.reps[d].nodes)
                M.IDEAL_LOST = True
                over = beyond(x, pre)
                after = []
                for op in SL.bound(then, d):
                    ok = X.step(op)
                    after.append('%s %s' % (op[0], 'applies' if ok else 'does not apply'))
                acc = SL.ACCEPTED.get(hid) if (own or every) else None
                flags = []
                if own:
                    flags.append(('accepted beyond its own ideal' if acc else 'BEYOND ITS OWN IDEAL') + ': ' + own)
                elif alone:
                    flags.append('beyond its own ideal by after alone: ' + alone)
                if every and prog == 'import' and not own:
                    flags.append('an import into a copy that stays stale (STORE.md 640): ' + every)
                elif every:
                    flags.append(('accepted beyond every decision' if acc else 'BEYOND EVERY DECISION') + ': ' + every)
                if over:
                    flags.append('BEYOND BOTH COPIES: ' + over)
                for f in flags:
                    k = f.split(':')[0]
                    tally[k] = tally.get(k, 0) + 1
                tally['cells'] = tally.get('cells', 0) + 1
                print('   %-44s into %d: %s; then %s%s' % (label, d, view(x), ', '.join(after) or 'nothing',
                                                         ''.join('; ' + f for f in flags)))
    print('== cells: %d; flagged: %s' % (tally.pop('cells', 0), ', '.join('%s %d' % kv for kv in sorted(tally.items())) or 'none'))
    # Create on a stopped store, with SEAL off and on (PLAN 4.1 item 3)
    print('== R15 with Seal again\'s rules off (SEAL off: the measured Create, which asks only whether an authority stands)'
          ' and on')
    seqs = dict((h[0], h) for h in SL.histories())
    hid, what, ops, then = seqs['R15']
    for on in (False, True):
        M.SEAL = on
        W, applied = play(ops)
        print('   SEAL %s: %s' % ('on' if on else 'off', ' ; '.join(fmt(op) + ('' if ok else ' (does not apply)')
                                                               for op, ok in zip(ops, applied))))
        print('      copy 0: %s' % view(W.fold(W.reps[0])))
        k = ops.index(('create', 0))
        Wc, apc = play(ops[:k + 1])
        print('      after the first Create: %s%s' % (view(Wc.fold(Wc.reps[0])),
                                                      '' if apc[-1] else ' (Create does not apply)'))
    M.SEAL = True
    # the seal's summary entry naming a damaged summary entry it deletes, and not naming it
    print('== R11 and R12 with the seal\'s summary entry naming the damaged summary entry it deletes (the design)'
          ' and not naming it (SEAL_NAME_DAMAGED off, as the model wrote every summary entry before)')
    for hid in ('R11', 'R12'):
        _, what, ops, then = seqs[hid]
        for named in (True, False):
            M.SEAL_NAME_DAMAGED = named
            W, applied = play(ops)
            print('   %s, %s: %s' % (hid, 'named' if named else 'not named', view(W.fold(W.reps[0]))))
        M.SEAL_NAME_DAMAGED = True
    # the seal and an act written apart beside the single write (PLAN 4.1 item 4)
    print('== R16 to R18: the seal and the act in a single write, beside the seal written alone and the act after it')
    for hid in ('R16', 'R17', 'R18'):
        _, what, ops, then = seqs[hid]
        W, _ = play(ops)
        last = ops[-1]
        apart = ops[:-1] + [('reseal', 0), last[2]]
        X, ap = play(apart)
        w = M.seal_with_differs(W, X, 0)
        heads = lambda w_: sorted(u for u, (eid, mt, ok) in w_.reps[0].entries.items() if w_.EL[eid].kind == 'head')
        hd = lambda w_: sorted(u for u in w_.reps[0].dels if u in w_.EL and w_.EL[u].kind == 'head')
        print('   %s: single write: %s; apart: %s; %s' % (hid, view(W.fold(W.reps[0])), view(X.fold(X.reps[0])),
                                                       'the same but for the summary entries' if not w else 'DIFFERS: ' + w))
        print('      summary entries present: single write %d, apart %d; their deletion records: %d, %d' % (
            len(heads(W)), len(heads(X)), len(hd(W)), len(hd(X))))
    # the seal composed over the store shown, the control (PLAN 4.1 item 5)
    print('== every sequence with the seal composed over what its dialog showed (the control), beside the design,'
          ' which composes it at the write: copy 0 after the seal, and what it names')
    for hid, what, ops, then in SL.histories():
        res = []
        for compose in ('write', 'shown'):
            M.SEAL_COMPOSE = compose
            cut = max(k for k, op in enumerate(ops) if op[0] in ('reseal', 'reseal+'))
            W0, _ = play(ops[:cut])
            W, _ = play(ops)
            v0 = W0.fold(W0.reps[0])
            known = set(v0['ents']) | set(x for V in v0['vers'].values() for x in V)
            for ids in v0['lost'].values():
                known |= ids
            for V in v0['vers'].values():
                for e in V.values():
                    known |= e.after | e.lost
            named, made = set(), set()
            for u, (eid, mt, ok) in W.reps[0].entries.items():
                e = W.EL[eid]
                if u not in W0.reps[0].entries and e.kind == 'ver':
                    named |= e.after | e.lost
                    made.add(u)
            for u in W.reps[0].dels:
                if u not in W0.reps[0].dels:
                    made.add(u)
            res.append((view(W.fold(W.reps[0])), sorted(named - known - made), sealed_versions(W, set(W0.reps[0].entries))))
        M.SEAL_COMPOSE = 'write'
        same = res[0] == res[1]
        print('   %s: %s' % (hid, 'the same' if same else 'differs'))
        for name, (vw, foreign, sv) in zip(('at the write', 'as shown'), res):
            if not same or name == 'at the write':
                print('      %-12s %s; wrote %s; names %d identifiers not of the store at the write' % (
                    name, vw, '; '.join(sv) or 'no version', len(foreign)))


# ------------------------------------------------------------------ classes
def kind_of(ops, prop, detail=''):
    """The class a failing history falls in, by what it holds once shrunk: a class STORE.md 639 or
    decision 9 accepts, named by its row, or OTHER, which is a counterexample."""
    acts = set(op[0] for op in ops)
    deleted = [op[2] for op in ops if op[0] in ('del', 'edit')]
    plain = any(op[0] == 'merge' and op[1] in ('kp_other', 'xc', 'dx') for op in ops) or 'import' in acts
    untraced = any(op[0] == 'del' and op[3] != 1 for op in ops) or 'clear' in acts or 'over' in acts or plain
    if prop.startswith('seal-'):
        return 'OTHER: a property of the seal'
    if prop == 'one-loss' and 'renewal of' in detail and any(op[0] == 'del' and op[2] == ('rec',) for op in ops):
        return ('a client\'s admission deleted by hand with no other record of its key, so that a deletion '
                'record of its answer tells of nothing (STORE.md 618): the client admitted nowhere in that copy')
    if prop == 'sealed-after-a-loss':
        if untraced:
            return 'Seal again after a loss in both carriers with no deletion record (STORE.md 639 row 5)'
        return 'OTHER: sealed, with no loss that left no record: ' + ', '.join(sorted(acts))
    if 'create' in acts and (any(x == ('auth',) for x in deleted)
                             or ('delauth' in acts and any(x == ('ref', 'retired') for x in deleted))):
        return 'a store key lost: the entry holding it deleted or edited, then Create (decision 9)'
    if 'over' in acts:
        return 'a whole file written over another (STORE.md 639 row 1)'
    decision = any(x[0] in ('ver', 'ref', 'rec', 'prem', 'padd') for x in deleted) or 'lose' in acts
    head = any(x[0] == 'head' for x in deleted)
    if 'clear' in acts and decision:
        return 'every deletion record removed while a loss stands (STORE.md 639 row 4)'
    if decision and head and plain:
        return ('a decision and its summary entry deleted, and a merge that carries no deletion record '
                '(STORE.md 639 rows 2 and 3)')
    if decision and head and all(op[3] == 0 for op in ops if op[0] == 'del'):
        return 'a decision and its summary entry deleted with no deletion record (STORE.md 639 row 3)'
    return 'OTHER: ' + ', '.join(sorted(acts))


def seal_involved(ops, nrep, flags, k):
    """A history that ends beyond its ideal after a seal: played again with every seal left out, and
    where it then fails `safety` the seal is not what made it fail, which is the case where the seal
    waited in the open database and the merge that widened read the file from before it."""
    plain = [op for op in ops if op[0] not in ('reseal', 'reseal+', 'sealsee')]
    fails = [f for f in M.run(plain, nrep, flags) if f[0] == 'safety']
    if fails:
        return kind_of(plain, 'safety') + ', the seal not involved (the history fails the same with no seal)'
    return k


def replay(ops, nrep, flags):
    W = M.World(nrep, flags)
    out = []
    for k, op in enumerate(ops):
        ok = W.step(op)
        cells = []
        for i in range(nrep):
            v, I = W.fold(W.reps[i]), W.ideal(i)
            cells.append('copy %d %s; ideal %s' % (i, view(v), view(I)))
        out.append('          %d. %s%s: %s' % (k + 1, fmt(op), '' if ok else ' (does not apply)', ' | '.join(cells)))
    return out


# ------------------------------------------------------------------ the searches
def seal_alphabet(nrep):
    R = range(nrep)
    acts, faults = [], []
    sels = [('head', 'new'), ('head', 'old'), ('ref', 'revoked'), ('ref', 'retired'), ('ver', 'rule:N', 'new'),
            ('ver', 'rule:root', 'new'), ('ver', 'renew:c0', 'new'), ('rec',), ('padd',), ('prem',), ('auth',)]
    for r in R:
        acts += [('pair', r), ('revoke', r, 'c0'), ('revoke', r, 'c1'), ('recreate', r), ('delauth', r), ('create', r),
                 ('padd', r, 'pg'), ('prem', r, 'pf'), ('save', r), ('quit', r)]
        acts += [('rule', r, n, d) for n in ('root', 'N') for d in ('none', 'r', 'rw', 'inherit')]
        acts += [('renew', r, c, x) for c in CLIENTS for x in ('forever', 'ask')] + [('renewcert', r, 'c0')]
        acts += [('sealsee', r), ('reseal', r), ('reseal', r)]
        acts += [('reseal+', r, a) for a in (('revoke', r, 'c0'), ('revoke', r, 'c1'), ('create', r),
                                              ('rule', r, 'N', 'r'), ('rule', r, 'N', 'none'), ('padd', r, 'pg'),
                                              ('prem', r, 'pf'), ('renew', r, 'c1', 'forever'))]
        faults += [('del', r, s, x) for s in sels for x in (0, 1)]
        faults += [('edit', r, s) for s in sels]
        faults += [('clear', r), ('lose', r, 'rule:N'), ('lose', r, 'renew:c0')]
    merges = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in R for s in R if d != s]
    merges += [('import', d, s) for d in R for s in R if d != s]
    return acts, merges, faults


def draw(rng, pools, lo, hi, weights):
    ops = []
    for _ in range(rng.randint(lo, hi)):
        x = rng.random()
        acc = 0
        for pool, w in zip(pools, weights):
            acc += w
            if x < acc and pool:
                ops.append(rng.choice(pool))
                break
        else:
            ops.append(rng.choice(pools[0]))
    return ops


def chunk(args):
    what, cfg, nrep, seed, first, n, lohi = args
    setup()
    flags = FLAGS.but()
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    rng = random.Random(seed)
    if what == 'design':
        acts, merges, faults = M.alphabet(nrep, cfg, flags)
        weights = (0.45, 0.30, 0.25)
    else:
        acts, merges, faults = seal_alphabet(nrep)
        weights = (0.45, 0.30, 0.25)
    counts, kept, shortest, sealed_hist = {}, [], {}, [0, 0]
    lo, hi = lohi
    for h in range(first + n):
        if what == 'design':
            ops = []
            for _ in range(rng.randint(lo, hi)):
                x = rng.random()
                pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
                ops.append(rng.choice(pool))
        else:
            ops = draw(rng, (acts, merges, faults), lo, hi, weights)
        if h < first:
            continue
        once = set()
        fails = M.run(ops, nrep, flags)
        if any(op[0] in ('reseal', 'reseal+') for op in ops):
            sealed_hist[0] += 1
        for f in fails:
            if not M.ends(f[0]):
                if f[0] in once:
                    continue
                once.add(f[0])
            counts[f[0]] = counts.get(f[0], 0) + 1
            if f[0] not in shortest or len(ops) < len(shortest[f[0]]):
                shortest[f[0]] = ops
            if M.ends(f[0]):
                kept.append((h, ops, f))
    return counts, kept, shortest, sealed_hist


def report(title, results, nrep, flags):
    counts, kept, shortest, sealed = {}, [], {}, [0, 0]
    for c, k, s, sh in results:
        for key, n in c.items():
            counts[key] = counts.get(key, 0) + n
        kept += k
        for key, ops in s.items():
            if key not in shortest or len(ops) < len(shortest[key]):
                shortest[key] = ops
        sealed[0] += sh[0]
    lines = ['%s: %s' % (title, ', '.join('%s x%d' % x for x in sorted(counts.items())) or 'no failure')]
    lines.append('    histories in which a seal was drawn: %d' % sealed[0])
    for p, n in sorted(counts.items()):
        if M.ends(p):
            continue
        ops = shortest[p]
        small = M.shrink(ops, nrep, flags, p) if p.startswith('accepted:') or p.startswith('info:seal') else ops
        lines.append('    shortest %s: %s' % (p, ' ; '.join(fmt(op) for op in small)))
    classes, seen = {}, {}
    for h, ops, f in kept:
        small = M.shrink(ops, nrep, flags, f[0])
        detail = [x for x in M.run(small, nrep, flags) if x[0] == f[0]][0][2]
        k = kind_of(small, f[0], detail)
        if f[0] == 'sealed-after-a-loss':
            k = seal_involved(small, nrep, flags, k)
        classes.setdefault((f[0], k), []).append(h)
        seen.setdefault((f[0], k, tuple(small)), (h, detail, ops))
    for (p, k), hs in sorted(classes.items()):
        mine = [(key, x) for key, x in sorted(seen.items(), key=lambda x: (len(x[0][2]), x[1][0])) if key[:2] == (p, k)]
        lines.append('    %s, %s: %d histories, %d distinct once shrunk%s' % (
            p, k, len(hs), len(mine), '' if k.startswith('OTHER') or len(mine) <= 3 else ', the three shortest'))
        for n, ((p2, k2, small), (h, detail, ops)) in enumerate(mine):
            if n >= 3 and not k.startswith('OTHER'):
                break
            lines.append('        #%d, shrunk to %d steps: %s   (%s)' % (h, len(small), ' ; '.join(fmt(op) for op in small), detail))
            if k.startswith('OTHER'):
                lines.append('        COUNTEREXAMPLE, drawn: %s' % ' ; '.join(fmt(op) for op in ops))
                lines += replay(list(small), nrep, flags)
    return lines


def part_search(nrep, count, workers):
    setup()
    print('== the store as decided with the answers and Seal again: the design\'s search, its twelve configurations'
          ' over %d files, %d histories each, its seeds; where deletions by hand are, the seal\'s dialog and the seal'
          ' beside an act (a revocation, Create, N\'s rule set, a name added) in the alphabet' % (nrep, count))
    t0 = time.time()
    per = 1000
    jobs = []
    for k, (name, cfg) in enumerate(M.CONFIGS):
        for c in range(0, count, per):
            jobs.append(('design', cfg, nrep, 100 + k + 10 * nrep, c, min(per, count - c), (3, 9)))
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(chunk, jobs)
    for k, (name, cfg) in enumerate(M.CONFIGS):
        mine = [r for j, r in zip(jobs, res) if j[1] == cfg]
        for x in report('%-66s %d files %6d histories' % (name, nrep, count), mine, nrep, FLAGS):
            print(x)
    print('== %.0f s' % (time.time() - t0))


def part_seal(config, count, seed, lo, hi, workers):
    setup()
    nrep = 3 if config == 'three' else 2
    acts, merges, faults = seal_alphabet(nrep)
    print('== %d histories of the seal\'s alphabet over %d copies, %d to %d steps, seed %d: %d acts (a pairing, a'
          ' revocation, Recreate, Delete, Create, a name added and removed, N\'s and the root\'s rule, an answer and a'
          ' lift, a renewal, a save, KeePass ending before a save, the seal\'s dialog, Seal again, the seal beside an'
          ' act), %d merges (Synchronize, Overwrite if newer, KeePassXC, Create new IDs) and %d faults (a deletion by'
          ' hand of each kind with a deletion record or with none, an edit of each kind, every deletion record'
          ' cleared, a deletion record of a version whose content no file holds); an act drawn with 0.45, a merge'
          ' with 0.3, a fault with 0.25' % (count, nrep, lo, hi, seed, len(acts), len(merges), len(faults)))
    t0 = time.time()
    per = 500
    jobs = [('seal', config, nrep, seed * 100003 + c, 0, min(per, count - c * per), (lo, hi))
            for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(chunk, jobs)
    for j, r in zip(jobs, res):
        base = (j[3] - seed * 100003) * per
        r[1][:] = [(h + base, ops, f) for (h, ops, f) in r[1]]
    for x in report('   histories', res, nrep, FLAGS):
        print(x)
    print('== %.0f s' % (time.time() - t0))


def damage_chunk(args):
    cfg, seed, first, n = args
    setup()
    flags = FLAGS.but()
    flags.mode = ''
    rng = random.Random(seed)
    acts, merges, faults = M.alphabet(2, cfg, flags)
    out = [0, 0, 0, 0, []]
    for h in range(first + n):
        ops = []
        for _ in range(rng.randint(3, 9)):
            x = rng.random()
            pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            ops.append(rng.choice(pool))
        if h < first:
            continue
        if not any(op[0] == 'edit' for op in ops) or not any(op[0] in ('reseal', 'reseal+') for op in ops):
            continue
        out[0] += 1
        W = M.World(2, flags)
        found, applied, narrower_ever, wider = None, False, False, False
        for op in ops:
            pre = None
            if op[0] in ('reseal', 'reseal+') and op[1] < 2:
                r0 = W.reps[op[1]]
                pre = set(u for u, (eid, mt, ok) in r0.entries.items() if u == eid and not ok and W.EL[eid].kind == 'ver')
            if not W.step(op):
                continue
            if pre is not None:
                applied = True
                A = W.fold(W.reps[op[1]])
                told = sorted(o for o, ids in A['lost'].items() if ids & pre)
                if told and found is None:
                    found = (op, told)
        if applied:
            out[1] += 1
        if found:
            out[2] += 1
            if len(out[4]) < 3:
                out[4].append((ops, found))
    return out


def part_damage(count, workers):
    setup()
    print('== PLAN 4.1 item 2: a damaged version whose text no summary entry holds, sealed. Configurations F, I and K'
          ' over two files, %d histories each with the design\'s seeds; of the histories that hold an edit and a seal,'
          ' those in which a seal applied, and those after which an object refuses by the deletion record of a damaged'
          ' version the seal deleted' % count)
    t0 = time.time()
    per = 1000
    for k, (name, cfg) in enumerate(M.CONFIGS):
        if name[0] not in 'FIK':
            continue
        jobs = [(cfg, 100 + k + 20, c, min(per, count - c)) for c in range(0, count, per)]
        with multiprocessing.Pool(workers) as pool:
            res = pool.map(damage_chunk, jobs)
        tot = [sum(r[i] for r in res) for i in range(3)]
        ex = [x for r in res for x in r[4]][:3]
        print('   %-66s with an edit and a seal %d, a seal applied %d, an object refusing by a damaged version\'s'
              ' deletion record %d' % (name, tot[0], tot[1], tot[2]))
        for ops, (op, told) in ex:
            print('      %s  (at %s: %s)' % (' ; '.join(fmt(o) for o in ops), fmt(op), ', '.join(told)))
    print('   every such history is also held to the property of the search, `safety` among it: the counts of the'
          ' search part give any that fails')
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ exhaustive
PREFIX = [('rule', 0, 'N', 'r'), ('revoke', 0, 'c0'), ('pair', 0), ('renew', 0, 'c1', 'forever'),
          ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('prem', 0, 'pf')]


def exhaustive_steps():
    steps = []
    for r in (0, 1):
        steps += [('del', r, ('ver', 'rule:N', 'new'), x) for x in (0, 1)]
        steps += [('del', r, ('head', 'new'), x) for x in (0, 1)]
        steps += [('del', r, ('ref', 'revoked'), x) for x in (0, 1)]
        steps += [('edit', r, ('ver', 'rule:N', 'new')), ('edit', r, ('head', 'new')), ('edit', r, ('rec',))]
        steps += [('sealsee', r), ('reseal', r), ('reseal+', r, ('revoke', r, 'c1')), ('reseal+', r, ('create', r)),
                  ('reseal+', r, ('rule', r, 'N', 'rw')), ('create', r), ('save', r), ('quit', r)]
    steps += [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in (0, 1) for s in (0, 1) if d != s]
    return steps


def exhaustive_chunk(args):
    depth, first = args
    setup()
    steps = exhaustive_steps()
    W0 = M.World(2, FLAGS)
    W0.maxclients = 5
    for op in PREFIX:
        assert W0.step(op), op
        if op[0] == 'rule':
            W0.step(('save', 0))
    total, bad = 0, {}
    for rest in itertools.product(steps, repeat=depth - 1):
        seq = (first,) + rest
        total += 1
        for f in M.run(list(seq), 2, FLAGS, check_conv=True, world=W0):
            key = (f[0], kind_of(list(seq), f[0], f[2]) if M.ends(f[0]) else '')
            if key not in bad or len(seq) < len(bad[key][1]):
                bad[key] = (bad.get(key, (0,))[0], seq, f)
            bad[key] = (bad[key][0] + 1, bad[key][1], bad[key][2])
    return total, bad


def part_exhaustive(depth, workers):
    setup()
    steps = exhaustive_steps()
    t0 = time.time()
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(exhaustive_chunk, [(depth, s) for s in steps])
    total, bad = 0, {}
    for t, b in res:
        total += t
        for key, (n, s, f) in b.items():
            if key not in bad:
                bad[key] = (n, s, f)
            else:
                bad[key] = (bad[key][0] + n,) + ((s, f) if len(s) < len(bad[key][1]) else bad[key][1:])
    print('== every sequence of %d steps of a narrow seal alphabet over two copies (%d steps wide: deletions of N\'s'
          ' newest version, the newest summary entry and the revocation with a deletion record or with none, edits,'
          ' the seal\'s dialog, Seal again, the seal beside a revocation, Create and N\'s rule, Create, a save,'
          ' KeePass ending before a save, six merges), after the divergence %s, the convergence checked at the end:'
          ' %d histories' % (depth, len(steps), ' ; '.join(fmt(op) for op in PREFIX), total))
    if not bad:
        print('    no failure')
    for (p, k), (c, s, f) in sorted(bad.items()):
        print('    %s%s x%d; first shortest (%s): %s' % (p, ', ' + k if k else '', c, f[2], ' ; '.join(fmt(op) for op in s)))
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ the bridge to read.py
def bridge_chunk(args):
    config, seed, count, lo, hi = args
    setup()
    import read as RD
    import store as S
    RD.ANSWERS, RD.JUDGE, RD.SEAL, S.HEX = True, 'lost', True, True
    rng = random.Random(seed)
    acts, merges, faults = seal_alphabet(2)
    compared, differs, sealcmp, sealdiff = 0, [], 0, []
    nodes = {'root': 'aa' * 16, 'N': 'bb' * 16}
    for _ in range(count):
        ops = draw(rng, (acts, merges, faults), lo, hi, (0.45, 0.3, 0.25))
        W = M.World(2, FLAGS)
        enc = S.Encoder(W, nodes)
        for op in ops:
            i = op[2] if op[0] == 'merge' else op[1]
            if not W.step(op):
                continue
            compared += 1
            rep = W.view(i)
            entries = {}
            for eid in sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid):
                u, title, s, prot = enc.entry(eid)
                ok = rep.entries[eid][2]
                if not ok:      # damage: the check no longer passes
                    s = dict(s)
                    s['KPG.Data'] = s['KPG.Data'] + ' '
                entries[u] = {'strings': s, 'history': [], 'place': 'S', 'parent': nodes['root'], 'title': title}
            for u, (eid, mt, ok) in rep.entries.items():
                if u != eid:    # an entry under an identifier the gate never gave it
                    uu, title, s, prot = enc.entry(eid)
                    entries['ee' + hashlib.sha256(u.encode()).hexdigest()[:30]] = {'strings': s, 'history': [], 'place': 'S',
                                                                        'parent': nodes['root'], 'title': title}
            d = {'root': nodes['root'], 'groups': {nodes['N']: {'parent': nodes['root']}},
                 'dels': {enc.u(x): 0 for x in rep.dels}, 'entries': entries}
            r = RD.read(d, history=True)
            v = W.fold(rep)
            mine = (v['status'] == 'ok', sorted(enc.obj(o) for o in v['lost']))
            theirs = (r['status'] == 'ok', sorted(r['losses']['lost']))
            if mine != theirs:
                differs.append((ops, op, mine, theirs))
            if v['status'] == 'incomplete' and v['ents']:
                X = W.clone()
                before = set(X.reps[i].entries)
                M.SEAL_COMPOSE = 'write'
                if X.step(('reseal', i)):
                    sealcmp += 1
                    ex = S.Encoder(X, nodes)
                    ex.uuid, ex.eid = dict(enc.uuid), dict(enc.eid)
                    wrote = []
                    for u, (eid, mt, ok) in X.reps[i].entries.items():
                        e = X.EL[eid]
                        if u not in before and e.kind == 'ver':
                            wrote.append({'object': ex.obj(e.obj), 'doc': S.doc(e.data, ex.u),
                                          'after': sorted(ex.u(x) for x in e.after),
                                          'lost': sorted(ex.u(x) for x in e.lost)})
                    planned = sorted(r['losses']['seal'], key=lambda x: x['object'])
                    wrote.sort(key=lambda x: x['object'])
                    if planned != wrote:
                        sealdiff.append((ops, op, planned, wrote))
    return compared, differs, sealcmp, sealdiff


def part_bridge(config, count, seed, workers):
    setup()
    t0 = time.time()
    per = 100
    jobs = [(config, seed * 100003 + c, min(per, count - c * per), 4, 12) for c in range((count + per - 1) // per)]
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(bridge_chunk, jobs)
    compared = sum(r[0] for r in res)
    differs = [x for r in res for x in r[1]]
    sealcmp = sum(r[2] for r in res)
    sealdiff = [x for r in res for x in r[3]]
    print('== %d histories of the seal\'s alphabet over two copies, 4 to 12 steps, seed %d: after every step that'
          ' applied, the file the copy it changed holds written by store.py\'s Encoder, a damaged entry with its'
          ' check failing, every certificate and client key in hexadecimal, read by read.py with its listing, held'
          ' to the model\'s fold (whether the store is whole, and the objects a lost version makes refuse); and'
          ' where the copy is stopped, the versions read.py\'s listing plans for the seal held to the versions the'
          ' model\'s seal writes over the same copy' % (count, seed))
    print('   steps compared %d; steps that differ %d' % (compared, len(differs)))
    for ops, op, a, b in differs[:5]:
        print('      %s, at %s: model %s, read.py %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), a, b))
    print('   seals compared %d; seals that differ %d' % (sealcmp, len(sealdiff)))
    for ops, op, a, b in sealdiff[:5]:
        print('      %s, at %s: read.py plans %s, the model writes %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), a, b))
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ all
PARTS = [('hand', 'hand', ()),
         ('seal-two', 'seal', ('two', 200000, 61, 4, 12)),
         ('seal-three', 'seal', ('three', 100000, 62, 4, 12)),
         ('seal-long', 'seal', ('two', 100000, 63, 8, 16)),
         ('damage', 'damage', (20000,)),
         ('exhaustive-4', 'exhaustive', (4,)),
         ('search-2', 'search', (2, 20000)), ('search-3', 'search', (3, 20000)),
         ('bridge', 'bridge', ('two', 2000, 71))]


def part_all(out, workers, only=None):
    import contextlib
    os.makedirs(out, exist_ok=True)
    for name, part, args in PARTS:
        if only and name not in only:
            continue
        t0 = time.time()
        with open(os.path.join(out, name + '.txt'), 'w', encoding='utf-8', newline='\n') as f:
            with contextlib.redirect_stdout(f):
                if part == 'hand':
                    part_hand()
                elif part == 'seal':
                    part_seal(*args, workers)
                elif part == 'damage':
                    part_damage(*args, workers)
                elif part == 'exhaustive':
                    part_exhaustive(*args, workers)
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
    elif part == 'search':
        part_search(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 20000,
                    int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'seal':
        part_seal(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]),
                  int(sys.argv[7]) if len(sys.argv) > 7 else 1)
    elif part == 'damage':
        part_damage(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 1)
    elif part == 'exhaustive':
        part_exhaustive(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 1)
    elif part == 'bridge':
        part_bridge(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]) if len(sys.argv) > 5 else 1)
    elif part == 'all':
        part_all(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1,
                 sys.argv[4].split(',') if len(sys.argv) > 4 else None)
    else:
        raise SystemExit('unknown part ' + part)


if __name__ == '__main__':
    main()
