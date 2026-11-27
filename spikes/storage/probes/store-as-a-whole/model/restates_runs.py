"""ADDED (seal restates): what a summary entry restates of a revocation, a retirement and a removal of a
name that a deletion record alone tells of, in the model, under each form of crdt_model.SEAL_RESTATES:
'all' (the control, the model's form before), 'named' (the product's form), 'ids' (the form proposed)
and 'hashes' (the form proposed with a revocation and a retirement restated by tag and subject hash).

Run:  uv run --no-project python -I -X utf8 restates_runs.py PART [ARGS], its output into out\\restates

Parts:
  hand                        R1 to R21 of probe/seal.py and the sequences of probe/restates.py, under
                              every form: copy 0 after the seal and what its summary entry restates,
                              then every merge both ways with nothing before it and with every deletion
                              record of copy 0 cleared before it; each merged copy held to its own ideal,
                              to every decision any gate made and to both copies just before, and
                              whether it is stopped; then every cell in which a form differs from the
                              control
  random WHAT ARGS            each history of a part of seal_runs.py played under every form: `seal
                              CONFIG COUNT SEED LO HI` (the seal's alphabet) or `search NREP COUNT` (the
                              design's twelve configurations with their seeds); each history held under
                              each form to the properties of crdt_model.run and to whether each copy ends
                              stopped, beside the control
  exhaustive DEPTH ALPHABET   every sequence up to DEPTH, under every form beside the control: ALPHABET
                              `seal` (the alphabet and divergence of seal_runs.py's exhaustive part) or
                              `told` (after each decision that a deletion record alone tells of once its
                              record and the summary entry are deleted, with copy 1 the copy from before:
                              the seal, deletion records cleared, every merge, the same decision made
                              apart at copy 1, a summary entry deleted, a pairing, a save)
  bridge COUNT SEED           random histories of the seal's alphabet under each form, every file written
                              by store.py's Encoder and read by read.py, held to the model's fold: whether
                              the store is whole, the objects a lost version makes refuse, every
                              revocation and retirement, every removal of a name, and the versions the
                              seal writes
  all OUT WORKERS [PARTS]     every part of PARTS below, each into OUT\\<part>.txt

Every random choice is drawn from a random.Random with a seed given here or on the command line, as in
seal_runs.py. A history differs from the control when, under the form, it fails a property of
crdt_model.run that ends a history and the control does not, or a copy ends stopped, wider than its
own ideal or granting less than under the control, where under the control it does not; the first of
each such way of differing are shrunk while they still differ that way and printed.
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
import seal_runs as SR  # noqa: E402

FORMS = ('all', 'named', 'ids', 'hashes')
FLAGS = SR.FLAGS
HAND_FLAGS = SR.HAND_FLAGS
fmt = SR.fmt
view = SR.view


def setup(form='all'):
    SR.setup()
    M.SEAL_RESTATES = form


def restated(W, i):
    """What the summary entries present in copy i restate of revocations, retirements and removals."""
    out = []
    for u, (eid, mt, ok) in sorted(W.reps[i].entries.items()):
        e = W.EL[eid]
        if e.kind != 'head' or u != eid:
            continue
        items = ['%s %s' % (k, s) for (k, s) in sorted(e.data['ref'])]
        items += ['removed %s' % n for (n, r) in sorted(e.data['prem'])]
        items += ['%s by identifier' % ('removed' if W.EL[x].kind == 'prem' else W.EL[x].data[0])
                  for x in sorted(e.data.get('refid', ()))]
        items += ['%s by hash' % k for (k, s) in sorted(e.data.get('refsub', ()))]
        out.append('%s: %s' % (u.split('.')[0], ', '.join(items) or 'nothing'))
    return '; '.join(out) or 'no summary entry'


# ------------------------------------------------------------------ hand
def hand_cells(form):
    """{(sequence, before, program, copy): (line, wider, stopped)} under the form, and lines."""
    import restates as RS      # probe/restates.py, for its sequences
    import seal as SL
    setup(form)
    lines, cells = [], {}
    lines.append('== form %s' % form)
    for hid, what, ops, then in RS.histories():
        W, applied = SR.play(ops)
        v = W.fold(W.reps[0])
        lines.append('## %s: %s' % (hid, what))
        lines.append('   %s' % ' ; '.join(fmt(op) + ('' if ok else ' (does not apply)') for op, ok in zip(ops, applied)))
        lines.append('   copy 0 after the sequence: %s; own ideal: %s; restates: %s' % (
            view(v), M.narrower(v, W.ideal(0), W.reps[0].nodes) or 'within', restated(W, 0)))
        for pre in ('nothing', 'cleared'):
            for label, prog in SR.PROGRAMS:
                for d, s in ((1, 0), (0, 1)):
                    X = W.clone()
                    if pre == 'cleared':
                        X.step(('clear', 0))
                    pres = (X.fold(X.reps[d]), X.fold(X.reps[s]))
                    X.step(SR.merge_of(prog, d, s))
                    x = X.fold(X.reps[d])
                    own = M.narrower(x, X.ideal(d), X.reps[d].nodes)
                    every = M.narrower(x, SR.global_ideal(X, d), X.reps[d].nodes)
                    over = SR.beyond(x, pres)
                    after = []
                    for op in SL.bound(then, d):
                        ok = X.step(op)
                        after.append('%s %s' % (op[0], 'applies' if ok else 'does not apply'))
                    acc = SL.ACCEPTED.get(hid) if (own or every) else None
                    flags = []
                    if own:
                        flags.append(('accepted beyond its own ideal' if acc else 'BEYOND ITS OWN IDEAL') + ': ' + own)
                    if every and prog == 'import' and not own:
                        flags.append('an import into a copy that stays stale (STORE.md 640): ' + every)
                    elif every:
                        flags.append(('accepted beyond every decision' if acc else 'BEYOND EVERY DECISION') + ': ' + every)
                    if over:
                        flags.append('BEYOND BOTH COPIES: ' + over)
                    line = '%s; then %s%s' % (view(x), ', '.join(after) or 'nothing', ''.join('; ' + f for f in flags))
                    wider = bool(own and not acc) or bool(every and not acc and prog != 'import')
                    cells[(hid, pre, label, d)] = (line, wider, x['status'] != 'ok', restated(X, d))
                    lines.append('   %-9s %-44s into %d: %s' % (pre, label, d, line))
    return cells, lines


def part_hand():
    res = {}
    for form in FORMS:
        res[form], lines = hand_cells(form)
        for x in lines:
            print(x)
    print('== every cell in which a form differs from the control (form all): the control, then the form')
    for form in FORMS[1:]:
        n = wider = stopped = cleared_wider = 0
        for key in sorted(res['all']):
            a, b = res['all'][key], res[form][key]
            if a[0] == b[0]:
                continue
            n += 1
            print('   %s | %s | %s | %s | into %d' % (form, key[0], key[1], key[2], key[3]))
            print('      all:   %s' % a[0])
            print('      %-6s %s' % (form + ':', b[0]))
            print('      restated after the merge: all: %s; %s: %s' % (a[3], form, b[3]))
            if b[1] and not a[1]:
                wider += 1
                cleared_wider += key[1] == 'cleared'
            if b[2] and not a[2]:
                stopped += 1
        total = len(res['all'])
        print('== form %s: of %d cells, %d differ from the control; wider where the control is not %d (%d of them with'
              ' the deletion records cleared); stopped where the control is whole %d' % (
                  form, total, n, wider, cleared_wider, stopped))
    for form in FORMS:
        print('== form %s: cells wider outside the accepted classes %d, stopped %d, of %d' % (
            form, sum(1 for c in res[form].values() if c[1]), sum(1 for c in res[form].values() if c[2]), len(res[form])))


# ------------------------------------------------------------------ one history under every form
def ending(ops, nrep, flags, world=None):
    """Under the form set: the properties of crdt_model.run that end a history, the copies that end
    stopped or wider than their own ideal, whether a summary entry holding an item with no subject was
    written, and each copy's reading at the end."""
    bad = set(f[0] for f in M.run(list(ops), nrep, flags, world=world) if M.ends(f[0]))
    W = world.clone() if world else M.World(nrep, flags)     # as crdt_model.run plays it
    for op in ops:
        W.step(op)
    stopped, wider, views = set(), set(), []
    for i in range(nrep):
        v = W.fold(W.reps[i])
        views.append((v, W.reps[i].nodes))
        if v['status'] != 'ok':
            stopped.add(i)
        if M.narrower(v, W.ideal(i), W.reps[i].nodes):
            wider.add(i)
    items = any(e.kind == 'head' and (e.data.get('refid') or e.data.get('refsub')) for e in W.EL.values())
    return frozenset(bad), frozenset(stopped), frozenset(wider), items, views


def against(a, b):
    """What b leaves that a does not, and what a leaves that b does not: properties failed, copies
    stopped, copies wider than their own ideal, and copies that grant less at the end than the
    other's same copy grants."""
    def less(x, y):
        return frozenset(i for i, ((v, n), (w, m)) in enumerate(zip(x[4], y[4])) if M.narrower_than(v, w, n))
    return ((b[0] - a[0], b[1] - a[1], b[2] - a[2], less(b, a)),
            (a[0] - b[0], a[1] - b[1], a[2] - b[2], less(a, b)))


def differs(ops, nrep, flags, form, world=None):
    """What the form leaves that the control does not: properties failed, copies stopped, copies wider."""
    M.SEAL_RESTATES = 'all'
    a = ending(ops, nrep, flags, world)
    M.SEAL_RESTATES = form
    b = ending(ops, nrep, flags, world)
    M.SEAL_RESTATES = 'all'
    return against(a, b)


def what_of(d):
    out = []
    if d[0]:
        out.append('fails ' + ', '.join(sorted(d[0])))
    if d[1]:
        out.append('stopped at ' + ', '.join(str(i) for i in sorted(d[1])))
    if d[2]:
        out.append('wider at ' + ', '.join(str(i) for i in sorted(d[2])))
    if d[3]:
        out.append('granting less at ' + ', '.join(str(i) for i in sorted(d[3])))
    return '; '.join(out)


def shrink_differs(ops, nrep, flags, form, sign, world=None):
    """Greedy: drop every step whose removal keeps the history differing the same way."""
    want = sign
    ops = list(ops)
    changed = True
    while changed:
        changed = False
        for k in range(len(ops)):
            cand = ops[:k] + ops[k + 1:]
            d = differs(cand, nrep, flags, form, world)[0]
            if d[('fails', 'stopped', 'wider', 'less').index(want)]:
                ops, changed = cand, True
                break
    return ops


def tally_history(ops, nrep, flags, out, h, world=None):
    """Each form against the control, into `out`: {form: {sign: [count, examples]}}."""
    M.SEAL_RESTATES = 'all'
    a = ending(ops, nrep, flags, world)
    for form in FORMS[1:]:
        M.SEAL_RESTATES = form
        b = ending(ops, nrep, flags, world)
        M.SEAL_RESTATES = 'all'
        more, less = against(a, b)
        o = out.setdefault(form, {})
        if b[3]:    # a summary entry holding an item with no subject was written
            x = o.setdefault('items', [0, [], {}])
            x[0] += 1
        for sign, d in (('fails', more[0]), ('stopped', more[1]), ('wider', more[2]), ('less', more[3]),
                        ('control fails', less[0]), ('control stopped', less[1]), ('control wider', less[2]),
                        ('control less', less[3])):
            if not d:
                continue
            x = o.setdefault(sign, [0, [], {}])
            x[0] += 1
            for p in (d if sign.endswith('fails') else ['copy']):
                x[2][p] = x[2].get(p, 0) + 1
            if len(x[1]) < 40:
                x[1].append((h, list(ops), what_of(more if not sign.startswith('control') else less)))


def merge_tally(results):
    out = {}
    for r in results:
        for form, o in r.items():
            for sign, (n, ex, props) in o.items():
                x = out.setdefault(form, {}).setdefault(sign, [0, [], {}])
                x[0] += n
                x[1] += ex
                for p, c in props.items():
                    x[2][p] = x[2].get(p, 0) + c
    return out


def report(out, nrep, flags, total, world=None):
    lines = []
    for form in FORMS[1:]:
        o = out.get(form, {})
        if 'items' in o:
            lines.append('   form %s: histories in which a summary entry holding an item with no subject was written:'
                         ' %d of %d' % (form, o['items'][0], total))
        if not [k for k in o if k != 'items']:
            lines.append('   form %s: no history differs from the control (of %d)' % (form, total))
            continue
        for sign in ('fails', 'stopped', 'wider', 'less', 'control fails', 'control stopped', 'control wider',
                     'control less'):
            if sign not in o:
                continue
            n, ex, props = o[sign]
            shown = {'fails': 'fails', 'stopped': 'ends stopped', 'wider': 'ends wider than its own ideal',
                    'less': 'ends granting less'}[sign.replace('control ', '')]
            lines.append('   form %s, %s %s where the %s: %d histories of %d%s' % (
                form, 'the control' if sign.startswith('control') else 'the form', shown,
                'form does not' if sign.startswith('control') else 'control does not',
                n, total, ' (' + ', '.join('%s %d' % kv for kv in sorted(props.items())) + ')' if sign.endswith('fails') else ''))
            if sign.startswith('control'):
                for h, ops, w in sorted(ex, key=lambda x: (len(x[1]), x[0]))[:2]:
                    lines.append('        #%d: %s   (%s)' % (h, ' ; '.join(fmt(op) for op in ops), w))
                continue
            seen = {}
            for h, ops, w in sorted(ex, key=lambda x: (len(x[1]), x[0]))[:12]:
                small = shrink_differs(ops, nrep, flags, form, sign, world)
                key = tuple(small)
                if key in seen:
                    continue
                seen[key] = h
                M.SEAL_RESTATES = form
                fails = [f for f in M.run(small, nrep, flags, world=world) if M.ends(f[0])]
                M.SEAL_RESTATES = 'all'
                kind = SR.kind_of(small, fails[0][0], fails[0][2]) if fails else ''
                lines.append('        #%d, shrunk to %d steps: %s   (%s%s)' % (
                    h, len(small), ' ; '.join(fmt(op) for op in small),
                    what_of(differs(small, nrep, flags, form, world)[0]),
                    '; the seal\'s classes would put it in: ' + kind if kind else ''))
                if len(seen) >= 4:
                    break
    return lines


def random_chunk(args):
    what, cfg, nrep, seed, first, n, lohi = args
    setup()
    flags = FLAGS.but()
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    rng = random.Random(seed)
    if what == 'design':
        acts, merges, faults = M.alphabet(nrep, cfg, flags)
    else:
        acts, merges, faults = SR.seal_alphabet(nrep)
    out, counted, sealed = {}, 0, 0
    lo, hi = lohi
    for h in range(first + n):
        if what == 'design':
            ops = []
            for _ in range(rng.randint(lo, hi)):
                x = rng.random()
                pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
                ops.append(rng.choice(pool))
        else:
            ops = SR.draw(rng, (acts, merges, faults), lo, hi, (0.45, 0.30, 0.25))
        if h < first:
            continue
        counted += 1
        sealed += any(op[0] in ('reseal', 'reseal+') for op in ops)
        tally_history(ops, nrep, flags, out, h)
    return out, counted, sealed, (cfg, flags.mode)


def part_random(what, args, workers):
    setup()
    t0 = time.time()
    jobs = []
    if what == 'seal':
        config, count, seed, lo, hi = args
        nrep = 3 if config == 'three' else 2
        per = 500
        jobs = [('seal', config, nrep, seed * 100003 + c, 0, min(per, count - c * per), (lo, hi))
                for c in range((count + per - 1) // per)]
        print('== %d histories of the seal\'s alphabet over %d copies, %d to %d steps, seed %d (as seal_runs.py\'s'
              ' part seal %s): every history that holds a seal played under each form beside the control' % (
                  count, nrep, lo, hi, seed, config))
    else:
        nrep, count = args
        per = 1000
        for k, (name, cfg) in enumerate(M.CONFIGS):
            for c in range(0, count, per):
                jobs.append(('design', cfg, nrep, 100 + k + 10 * nrep, c, min(per, count - c), (3, 9)))
        print('== the design\'s search as seal_runs.py runs it, its twelve configurations over %d files, %d histories'
              ' each, its seeds: every history that holds a seal played under each form beside the control'
              % (nrep, count))
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(random_chunk, jobs)
    if what == 'seal':
        for j, r in zip(jobs, res):
            base = (j[3] - args[2] * 100003) * 500
            for o in r[0].values():
                for x in o.values():
                    x[1][:] = [(h + base, ops, w) for (h, ops, w) in x[1]]
        groups = [('   histories', res)]
    else:
        groups = []
        for name, cfg in M.CONFIGS:
            groups.append(('   ' + name, [r for j, r in zip(jobs, res) if j[1] == cfg]))
    for title, rs in groups:
        total = sum(r[1] for r in rs)
        sealed = sum(r[2] for r in rs)
        print('%s: %d histories, %d holding a seal' % (title, total, sealed))
        flags = FLAGS.but()
        flags.mode = rs[0][3][1] if rs else ''
        nrep = args[0] if what == 'design' else 3 if args[0] == 'three' else 2
        for x in report(merge_tally([r[0] for r in rs]), nrep, flags, total):
            print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ exhaustive
TOLD = {
    'revocation': [('rule', 0, 'N', 'r'), ('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1),
                   ('del', 0, ('head', 'new'), 1)],
    'retirement': [('rule', 0, 'N', 'r'), ('recreate', 0), ('del', 0, ('ref', 'retired'), 1),
                   ('del', 0, ('head', 'new'), 1)],
    'removal': [('rule', 0, 'N', 'r'), ('prem', 0, 'pf'), ('del', 0, ('prem',), 1), ('del', 0, ('head', 'new'), 1)],
}


def told_steps():
    steps = []
    for r in (0, 1):
        steps += [('sealsee', r), ('reseal', r), ('clear', r), ('del', r, ('head', 'new'), 0),
                  ('del', r, ('head', 'new'), 1), ('save', r), ('pair', r)]
    steps += [('revoke', 1, 'c0'), ('recreate', 1), ('prem', 1, 'pf')]
    steps += [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in (0, 1) for s in (0, 1) if d != s]
    steps += [('import', d, s) for d in (0, 1) for s in (0, 1) if d != s]
    return steps


def exhaustive_chunk(args):
    depth, alphabet, prefix, first = args
    setup()
    if alphabet == 'seal':
        steps = SR.exhaustive_steps()
        pre = SR.PREFIX
    else:
        steps = told_steps()
        pre = TOLD[prefix]
    W0 = M.World(2, FLAGS)
    W0.maxclients = 5
    for op in pre:
        assert W0.step(op), op
        if op[0] == 'rule':
            W0.step(('save', 0))
    total, out = 0, {}
    for rest in itertools.product(steps, repeat=depth - 1):
        seq = (first,) + rest
        total += 1
        tally_history(list(seq), 2, FLAGS, out, total, world=W0)
    return total, out, prefix


def part_exhaustive(depth, alphabet, workers):
    setup()
    t0 = time.time()
    steps = SR.exhaustive_steps() if alphabet == 'seal' else told_steps()
    prefixes = [None] if alphabet == 'seal' else sorted(TOLD)
    with multiprocessing.Pool(workers) as pool:
        res = pool.map(exhaustive_chunk, [(depth, alphabet, p, s) for p in prefixes for s in steps])
    for p in prefixes:
        mine = [r for r in res if r[2] == p]
        total = sum(r[0] for r in mine)
        pre = SR.PREFIX if alphabet == 'seal' else TOLD[p]
        print('== every sequence of %d steps of %s (%d steps wide) after %s, under each form beside the control:'
              ' %d histories' % (depth, 'the alphabet of seal_runs.py\'s exhaustive part' if alphabet == 'seal'
                                 else 'the seal, deletion records cleared, every merge, the decision made apart'
                                      ' at copy 1, a summary entry deleted, a pairing and a save',
                                 len(steps), ' ; '.join(fmt(op) for op in pre), total))
        W0 = M.World(2, FLAGS)
        W0.maxclients = 5
        for op in pre:
            W0.step(op)
            if op[0] == 'rule':
                W0.step(('save', 0))
        for x in report(merge_tally([r[1] for r in mine]), 2, FLAGS, total, world=W0):
            print(x)
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ the bridge to read.py
def hand_sequences():
    """Every cell of the part hand as a single sequence: the sequence, deletion records cleared or not,
    the merge, and what follows it."""
    import restates as RS
    import seal as SL
    out = []
    for hid, what, ops, then in RS.histories():
        for pre in ('nothing', 'cleared'):
            for label, prog in SR.PROGRAMS:
                for d, s in ((1, 0), (0, 1)):
                    out.append(list(ops) + ([('clear', 0)] if pre == 'cleared' else [])
                               + [SR.merge_of(prog, d, s)] + SL.bound(then, d))
    return out


def bridge_chunk(args):
    form, seed, count, lo, hi = args
    setup(form)
    if seed is None:
        seqs = hand_sequences()[lo:hi]
        flags, clients = HAND_FLAGS, 5
    else:
        rng = random.Random(seed)
        acts, merges, faults = SR.seal_alphabet(2)
        seqs = [SR.draw(rng, (acts, merges, faults), lo, hi, (0.45, 0.3, 0.25)) for _ in range(count)]
        flags, clients = FLAGS, None
    return bridge_seqs(seqs, flags, clients)


def bridge_seqs(seqs, flags, clients):
    import read as RD
    import store as S
    RD.ANSWERS, RD.JUDGE, RD.SEAL, S.HEX = True, 'lost', True, True
    compared, differ, sealcmp, sealdiff, items = 0, [], 0, [], 0
    nodes = {'root': 'aa' * 16, 'N': 'bb' * 16}
    for ops in seqs:
        W = M.World(2, flags)
        if clients:
            W.maxclients = clients
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
                if not rep.entries[eid][2]:
                    s = dict(s)
                    s['KPG.Data'] = s['KPG.Data'] + ' '
                entries[u] = {'strings': s, 'history': [], 'place': 'S', 'parent': nodes['root'], 'title': title}
                if W.EL[eid].kind == 'head' and (W.EL[eid].data.get('refid') or W.EL[eid].data.get('refsub')):
                    items += 1
            for u, (eid, mt, ok) in rep.entries.items():
                if u != eid:
                    uu, title, s, prot = enc.entry(eid)
                    entries['ee' + hashlib.sha256(u.encode()).hexdigest()[:30]] = {
                        'strings': s, 'history': [], 'place': 'S', 'parent': nodes['root'], 'title': title}
            d = {'root': nodes['root'], 'groups': {nodes['N']: {'parent': nodes['root']}},
                 'dels': {enc.u(x): 0 for x in rep.dels}, 'entries': entries}
            r = RD.read(d, history=True)
            v = W.fold(rep)
            mine = (v['status'] == 'ok', sorted(enc.obj(o) for o in v['lost']), sorted(v['refusals']))
            theirs = (r['status'] == 'ok', sorted(r['losses']['lost']),
                      sorted((k, S.NAMES.get(s, s)) for (k, s) in r['refusals']))
            if mine != theirs:
                differ.append((ops, op, mine, theirs))
            if v['status'] == 'incomplete' and v['ents']:
                X = W.clone()
                before = set(X.reps[i].entries)
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
    return compared, differ, sealcmp, sealdiff, items


def part_bridge(count, seed, workers):
    setup()
    t0 = time.time()
    if seed is None:
        count = len(hand_sequences())
        print('== every cell of the part hand, %d sequences, under each form: after every step that applied, the file'
              ' the copy it changed holds written by store.py\'s Encoder and read by read.py, held to the model\'s fold:'
              ' whether the store is whole, the objects a lost version makes refuse, and every revocation and'
              ' retirement either reads; where the copy is stopped, the versions read.py plans for the seal held to'
              ' those the model\'s seal writes' % count)
    else:
        print('== %d histories of the seal\'s alphabet over two copies, 4 to 12 steps, seed %d, under each form:'
              ' after every step that applied, the file the copy it changed holds written by store.py\'s Encoder and'
              ' read by read.py, held to the model\'s fold: whether the store is whole, the objects a lost version'
              ' makes refuse, and every revocation and retirement either reads; where the copy is stopped, the'
              ' versions read.py plans for the seal held to those the model\'s seal writes' % (count, seed))
    per = 100
    for form in FORMS:
        if seed is None:
            jobs = [(form, None, 0, c, c + per) for c in range(0, count, per)]
        else:
            jobs = [(form, seed * 100003 + c, min(per, count - c * per), 4, 12) for c in range((count + per - 1) // per)]
        with multiprocessing.Pool(workers) as pool:
            res = pool.map(bridge_chunk, jobs)
        differ = [x for r in res for x in r[1]]
        sealdiff = [x for r in res for x in r[3]]
        print('   form %s: steps compared %d, steps that differ %d; seals compared %d, seals that differ %d; summary'
              ' entries holding an item with no subject, counted at each step %d' % (
                  form, sum(r[0] for r in res), len(differ), sum(r[2] for r in res), len(sealdiff), sum(r[4] for r in res)))
        for ops, op, a, b in sorted(differ, key=lambda x: len(x[0]))[:3]:
            print('      %s, at %s: model %s, read.py %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), a, b))
        for ops, op, a, b in sealdiff[:3]:
            print('      %s, at %s: read.py plans %s, the model writes %s' % (' ; '.join(fmt(o) for o in ops), fmt(op), a, b))
    print('== %.0f s' % (time.time() - t0))


# ------------------------------------------------------------------ all
PARTS = [('hand', 'hand', ()),
         ('seal-two', 'random', ('seal', ('two', 200000, 61, 4, 12))),
         ('seal-three', 'random', ('seal', ('three', 100000, 62, 4, 12))),
         ('seal-long', 'random', ('seal', ('two', 100000, 63, 8, 16))),
         ('search-2', 'random', ('design', (2, 20000))), ('search-3', 'random', ('design', (3, 20000))),
         ('exhaustive-told-4', 'exhaustive', (4, 'told')),
         ('exhaustive-seal-4', 'exhaustive', (4, 'seal')),
         ('bridge', 'bridge', (2000, 71)), ('bridge-hand', 'bridge', (0, None))]


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
                elif part == 'random':
                    part_random(args[0], args[1], workers)
                elif part == 'exhaustive':
                    part_exhaustive(*args, workers)
                else:
                    part_bridge(*args, workers)
        print('%s: %.0f s' % (name, time.time() - t0), flush=True)


def main():
    sys.stdout.reconfigure(newline='\n')
    setup()
    part = sys.argv[1]
    if part == 'hand':
        part_hand()
    elif part == 'random':
        what = sys.argv[2]
        if what == 'seal':
            args = (sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]), int(sys.argv[7]))
            w = int(sys.argv[8]) if len(sys.argv) > 8 else 1
        else:
            args = (int(sys.argv[3]), int(sys.argv[4]))
            w = int(sys.argv[5]) if len(sys.argv) > 5 else 1
        part_random(what, args, w)
    elif part == 'exhaustive':
        part_exhaustive(int(sys.argv[2]), sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'bridge':
        part_bridge(int(sys.argv[2]), None if sys.argv[3] == 'hand' else int(sys.argv[3]),
                    int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif part == 'all':
        part_all(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1,
                 sys.argv[4].split(',') if len(sys.argv) > 4 else None)
    else:
        raise SystemExit('unknown part ' + part)


if __name__ == '__main__':
    main()
