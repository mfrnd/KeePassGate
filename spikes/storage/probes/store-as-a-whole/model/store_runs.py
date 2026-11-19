"""The recommended store in the design's model: the search, the replays, the cost of each change
taken by decision, and the sequences of EVALUATION.md section 6.

Run:  uv run --no-project python -I -X utf8 store_runs.py PART [COUNT], its output into out

Parts: search, ext, scenarios, ablations, section6. `count` is the number of random histories a
cell draws, 20,000 by default as in the design's `full`. Written for the family `store-as-a-whole`;
the model it runs is the family's copy of crdt_model.py and model_ext.py, with the switches listed
in RUN.md. Every random choice is drawn from a random.Random with a seed in this file.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crdt_model as M  # noqa: E402
import model_ext as X  # noqa: E402

STORE = M.STORE
# Each change taken by decision, and the store with that change undone.
DECISIONS = [
    ('mark', 'no mark on a user node (decision 11)', STORE.but(mark=True)),
    ('witness', 'no witness (decision 15)', STORE.but(T=True, witness=True)),
    ('revkey', 'a revocation names the key (decision 20, change 5)', STORE.but(revkey=False)),
    ('keyretire', 'the store key in the retirement (decision 12, change 6)', STORE.but(keyretire=False)),
    ('pending', 'a rule written at the next save (decision 13, change 4)', STORE.but(pending=False)),
    ('gates', 'several gates (decision 20)', STORE.but(gates=1)),
    ('repair', 'the repair after a Synchronize (decisions 18, 19, change 7)', STORE.but(repair=False)),
]


def fmt(op):
    return ' '.join(str(x) for x in op)


# ------------------------------------------------------------------ the search
def part_search(n):
    print('== the recommended store: the design\'s random search, its configurations and seeds,')
    print('   with the store\'s own acts added to the alphabet (renewal under the same key, Delete of')
    print('   the authority, Create, a save, KeePass ending before a save)')
    print('== lattice laws on the ideal transport: %d violations in %d random pairs of files'
          % (M.laws(n // 2, 7), n // 2))
    for nrep in (2, 3):
        for k, (name, cfg) in enumerate(M.CONFIGS):
            M.search(name, cfg, nrep, n, 9, 100 + k + 10 * nrep, STORE)
    print('== exhaustive, after the design\'s fixed divergence (each rule saved at once)')
    for name, cfg, depth in (('merges', '', 4), ('merges and deletions with a record', 'rec', 3),
                             ('merges and deletions without a record', 'trace', 3),
                             ('Synchronize and Keepass2Android only, deletions with a record', 'synconly rec', 3)):
        total, width, bad = M.exhaustive(2, cfg, depth, STORE)
        print('%-45s %d steps wide, depth %d: %7d histories  %s' % (
            name, width, depth, total,
            'no failure' if not bad else ', '.join('%s x%d' % (p, c) for p, (c, _, _) in sorted(bad.items()))))
        for p, (c, seq, f) in sorted(bad.items()):
            print('    shortest %s (%s): %s' % (p, f[2], ' ; '.join(fmt(op) for op in seq)))


def without_authority_losses():
    """The alphabet with no deletion or hand edit of the authority's entry: the losses of the store
    key that decision 9 accepts (row 17), after which a Create starts a new key and the oracle,
    which reads every key, still holds the old key's decisions."""
    orig = M.alphabet

    def alphabet(nrep, cfg, flags=None):
        acts, merges, faults = orig(nrep, cfg, flags)
        return acts, merges, [f for f in faults if not (f[0] in ('del', 'edit') and f[2] == ('auth',))]
    return orig, alphabet


def part_search_noauth(n):
    print('== the store\'s search again, with no deletion or hand edit of the authority\'s entry')
    orig, alt = without_authority_losses()
    M.alphabet = alt
    try:
        for nrep in (2, 3):
            for k, (name, cfg) in enumerate(M.CONFIGS):
                M.search(name, cfg, nrep, n, 9, 100 + k + 10 * nrep, STORE)
    finally:
        M.alphabet = orig


def part_ext(n):
    print('== the recommended store in the extended model of the survival critique: where an entry')
    print('   sits, the recycle bin, KeePassDroid\'s unchanged Save; every entry counts wherever it sits')
    print('   (binall, change 1). Its configurations and seeds; %d histories a cell' % n)
    for k, (name, progs, fk, kw) in enumerate(X.CONFIGS):
        kw = dict(kw)
        kw['binall'] = True
        for nrep in (2, 3):
            X.search(name, nrep, progs, fk, n, 9, 700 + 10 * k + nrep, flags=STORE, **kw)
    # KeePassDX, which the extended model's own configurations leave out
    for k, (name, fk) in enumerate([('DX Synchronize, K2A, KeePassDX; bin, delete with a record, Seal again',
                                     ['bin', 'rec', 'reseal']),
                                    ('DX2 the same and every fault of P2', ['bin', 'rec', 'reseal', 'touch', 'empty', 'unbin'])]):
        for nrep in (2, 3):
            X.search(name, nrep, ['kp_sync', 'k2a', 'dx'], fk, n, 9, 950 + 10 * k + nrep,
                     flags=STORE, binall=True)


# ------------------------------------------------------------------ the replays
def with_saves(ops):
    """Under change 4 a rule waits for a save; a replay that merges a copy after a rule saves
    first, so that the history means what the design's history means."""
    out = []
    for op in ops:
        out.append(op)
        if op[0] == 'rule':
            out.append(('save', op[1]))
    return out


def part_scenarios(n):
    cols = [('store', STORE)] + [(k, f) for k, _, f in DECISIONS]
    print('== the design\'s 20 replays under the recommended store, each rule saved at once')
    print('   columns: store, then the store with one decision undone: ' + ' | '.join(k for k, _ in cols[1:]))
    for k, (name, src, nrep, ops, i, ok) in enumerate(M.SCENARIOS):
        row = []
        for _, fl in cols:
            W = M.World(nrep, fl)
            for op in with_saves(ops):
                W.step(op)
            v = W.fold(W.reps[i])
            row.append(('holds' if ok(v) else 'FAILS') + ('' if v['status'] == 'ok' else ' (%s)' % v['status']))
        print('%2d. %s\n      [%s]\n   %s' % (k + 1, name, src, ' | '.join(row)))
    # The same, through the checks of the model after every step.
    print('== the same replays through the model\'s own checks after every step (store)')
    for k, (name, src, nrep, ops, i, ok) in enumerate(M.SCENARIOS):
        fails = M.run(with_saves(ops), nrep, STORE, check_conv=False)
        print('%2d. %s' % (k + 1, '; '.join('%s at step %d (%s)' % f for f in fails) or 'no failure of the checks'))


# ------------------------------------------------------------------ ablations
ABL_CONFIGS = [('B', 'rec'), ('C', 'rec trace'), ('D', 'rec skew'), ('E', 'rec trace clear'),
               ('F', 'rec foreign'), ('H', 'rec over'), ('I', 'rec skew trace clear over foreign strip'),
               ('J2', 'synconly rec skew trace')]


def part_ablations(n):
    orig, alt = without_authority_losses()
    M.alphabet = alt
    try:
        ablations(n)
    finally:
        M.alphabet = orig


def ablations(n):
    print('== the cost of each change taken by decision: the store, and the store with that change')
    print('   undone, over the same histories (%d a cell, 2 files, the design\'s seeds), with no deletion' % n)
    print('   or hand edit of the authority\'s entry (part search-noauth)')
    names = dict((c, nm) for nm, c in M.CONFIGS)
    seeds = dict((c, 100 + k + 20) for k, (nm, c) in enumerate(M.CONFIGS))
    for tag, cfg in ABL_CONFIGS:
        M.search('store, %s' % names[cfg], cfg, 2, n, 9, seeds[cfg], STORE)
        for key, label, fl in DECISIONS:
            M.search('  undone: %s' % label, cfg, 2, n, 9, seeds[cfg], fl)
    print('== the same for three files, configuration C and I')
    for tag, cfg in ABL_CONFIGS:
        if tag not in ('C', 'I'):
            continue
        M.search('store, %s' % names[cfg], cfg, 3, n, 9, seeds[cfg] + 10, STORE)
        for key, label, fl in DECISIONS:
            M.search('  undone: %s' % label, cfg, 3, n, 9, seeds[cfg] + 10, fl)


# ------------------------------------------------------------------ section 6
def global_ideal(W, i):
    """Every decision any gate made, on a perfect transport: the oracle of "wider than decided"."""
    r = M.Rep()
    r.nodes = W.reps[i].nodes
    for k in W.Kever:
        for eid in k:
            r.entries[eid] = [eid, 0, True]
    return W.fold(r, M.IDEAL)


def classify(W, i):
    A = W.fold(W.reps[i])
    out = []
    for label, I in (('its own ideal', W.ideal(i)), ('every decision', global_ideal(W, i))):
        nodes = W.reps[i].nodes
        w = M.narrower(A, I, nodes)
        if w:
            out.append('%s: WIDER (%s)' % (label, w))
            continue
        if I['status'] == 'ok' and I['served'] and A['status'] != 'ok':
            out.append('%s: refuses, the whole database (store %s)' % (label, A['status']))
            continue
        if I['served'] and not A['served']:
            out.append('%s: refuses, nothing served' % label)
            continue
        t = M.narrower_than(A, I, nodes)
        out.append('%s: %s' % (label, 'narrower (%s)' % t if t else 'as decided'))
    # a copy of a ruled node read wider than its original: residue accepted by decision 16
    if A['status'] == 'ok' and A['served']:
        up = M.app(A['funcs']['root'], frozenset())
        for m, o in sorted(W.copies.items()):
            if m in A['words'] and o in A['funcs'] and not A['words'][m] <= M.app(M.limit(A['funcs'][o]), up):
                out.append('accepted: copy %s gives %s, its original %s gives %s' % (
                    m, ''.join(sorted(A['words'][m])), o, ''.join(sorted(A['words'][o]))))
    return out


def view(W, i):
    v = W.fold(W.reps[i])
    return 'file %d: %s, served %s, admitted %s, words %s, renewals %s, names %s' % (
        i, v['status'], v['served'], sorted(v['adm']), {n: ''.join(sorted(w)) for n, w in sorted(v['words'].items())},
        dict(v['renew']), sorted(v['names']))


def R(*ops):
    return list(ops)


SEQ = [
    # (row of section 6, what, files, history, file asked)
    ('2', 'a revocation deleted in both carriers where made, with records; KeePassXC into the other copy', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'xc', 1, 0)), 1),
    ('2', 'the same through an import method (Overwrite if newer, existing, Keep existing)', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'kp_other', 1, 0)), 1),
    ('2', 'the same through KeePassDX', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'dx', 1, 0)), 1),
    ('2', 'the same through Synchronize (the records travel)', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'kp_sync', 1, 0)), 1),
    ('2', 'a name removal deleted in both carriers where made; Overwrite if newer into the other copy', 2,
     R(('prem', 0, 'pf'), ('del', 0, ('head', 'new'), 1), ('del', 0, ('prem',), 1), ('merge', 'kp_other', 1, 0)), 1),
    ('2', 'the same with the deleting clock behind, through Synchronize', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 2), ('del', 0, ('head', 'new'), 2), ('merge', 'kp_sync', 1, 0)), 1),
    ('3', 'the decision\'s entry deleted where made, KeePassXC delivers the head alone, the head deleted there with no record', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('merge', 'xc', 1, 0), ('del', 1, ('head', 'new'), 0)), 1),
    ('3', 'two deletions with no record after KeePassDX merges', 2,
     R(('revoke', 0, 'c0'), ('merge', 'dx', 1, 0), ('del', 1, ('ref', 'revoked'), 0), ('del', 1, ('head', 'new'), 0)), 1),
    ('3', 'a narrowed rule: KeePassXC brings the new version beside the old, both new entries removed by a script', 2,
     R(('rule', 0, 'N', 'rw'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('save', 0),
       ('merge', 'xc', 1, 0), ('del', 1, ('ver', 'rule:N', 'new'), 0), ('del', 1, ('head', 'new'), 0)), 1),
    ('3', 'the same, and then a Synchronize with the copy that holds the decision', 2,
     R(('rule', 0, 'N', 'rw'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('save', 0),
       ('merge', 'xc', 1, 0), ('del', 1, ('ver', 'rule:N', 'new'), 0), ('del', 1, ('head', 'new'), 0),
       ('merge', 'kp_sync', 1, 0)), 1),
    ('4', 'records cleared while a loss stands unsettled, then a stale copy synchronized (design scenario 9)', 2,
     R(('rule', 0, 'N', 'rw'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('save', 0),
       ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0), ('merge', 'kp_sync', 0, 1)), 0),
    ('4', 'the same with Seal again pressed before the stale copy arrives (design scenario 10), without the mark', 2,
     R(('rule', 0, 'N', 'rw'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('save', 0),
       ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0), ('reseal', 0),
       ('merge', 'kp_sync', 0, 1)), 0),
    ('6', 'a copy of a narrowed node beside its original under a granting root', 2,
     R(('rule', 0, 'root', 'rw'), ('save', 0), ('rule', 0, 'N', 'none'), ('save', 0), ('copy', 0)), 0),
    ('7', 'a newer copy imported into a stale copy with Create new IDs', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('revoke', 0, 'c0'), ('import', 1, 0)), 1),
    ('9', 'a file made with the store key from a stale copy, granting, merged in', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('plantk', 1, 'rule'), ('merge', 'kp_sync', 0, 1)), 0),
    ('9', 'a file made with the store key from a current copy, granting, merged in', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('plantk', 1, 'rule'),
       ('merge', 'kp_sync', 0, 1)), 0),
    ('10', 'a revocation at gate 0; gate 1 has not synchronized', 2,
     R(('revoke', 0, 'c0'),), 1),
    ('10', 'the same, and gate 1 renews the revoked client\'s key; then Synchronize at gate 0', 2,
     R(('revoke', 0, 'c0'), ('renewcert', 1, 'c0'), ('merge', 'kp_sync', 0, 1)), 0),
    ('10', 'the same, then Synchronize at gate 1', 2,
     R(('revoke', 0, 'c0'), ('renewcert', 1, 'c0'), ('merge', 'kp_sync', 1, 0)), 1),
    ('10', 'the same through KeePassXC into gate 0', 2,
     R(('revoke', 0, 'c0'), ('renewcert', 1, 'c0'), ('merge', 'xc', 0, 1)), 0),
    ('11', 'a client\'s record deleted for good in another program to take it away; KeePassXC into the other copy', 2,
     R(('del', 0, ('rec',), 1), ('merge', 'xc', 1, 0)), 1),
    ('14', 'a revocation and the head deleted for good with no record; Seal again; KeePassXC into a stale copy', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0), ('del', 0, ('head', 'new'), 0), ('reseal', 0),
       ('merge', 'xc', 1, 0)), 1),
    ('14', 'the same with records', 2,
     R(('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 1), ('del', 0, ('head', 'new'), 1), ('reseal', 0),
       ('merge', 'xc', 1, 0)), 1),
    ('14', 'a narrowed node\'s version and the head deleted with no record; Seal again; KeePassXC into a stale copy', 2,
     R(('rule', 0, 'N', 'rw'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('save', 0),
       ('del', 0, ('ver', 'rule:N', 'new'), 0), ('del', 0, ('head', 'new'), 0), ('reseal', 0), ('merge', 'xc', 1, 0)), 1),
    ('14', 'a first rule narrowing a node, its version and the head deleted with no record; Seal again', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('del', 0, ('ver', 'rule:N', 'new'), 0), ('del', 0, ('head', 'new'), 0),
       ('reseal', 0)), 0),
    ('14', 'the same, then KeePassXC into a copy that never had the rule', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('del', 0, ('ver', 'rule:N', 'new'), 0), ('del', 0, ('head', 'new'), 0),
       ('reseal', 0), ('merge', 'xc', 1, 0)), 1),
    ('14', 'a renewal refused forever, its answer and the head deleted with no record; Seal again (design 6.4 item 5)', 2,
     R(('renew', 0, 'c0', 'forever'), ('del', 0, ('head', 'new'), 0), ('del', 0, ('ver', 'renew:c0', 'new'), 0),
       ('reseal', 0)), 0),
    ('15', 'the head deleted for good', 2, R(('del', 0, ('head', 'new'), 1),), 0),
    ('15', 'a refusal\'s KPG. string edited by hand', 2, R(('revoke', 0, 'c0'), ('edit', 0, ('ref', 'revoked'))), 0),
    ('16', 'Create new IDs, then Delete Duplicate Entries; a third copy synchronized with the result', 3,
     R(('import', 0, 1), ('dedupe', 0), ('merge', 'kp_sync', 2, 0)), 2),
    ('17', 'the authority\'s entry deleted for good by hand, the deletion synchronized', 2,
     R(('del', 0, ('auth',), 1), ('merge', 'kp_sync', 1, 0)), 1),
    ('18', 'a second authority arrives intact', 2, R(('plant', 0, 'auth'),), 0),
    ('19', 'a rule narrowed in a node\'s dialog; KeePass ends before a save', 2,
     R(('rule', 0, 'root', 'rw'), ('save', 0), ('rule', 0, 'root', 'none'), ('quit', 0)), 0),
    ('20', 'two gates change the same rule apart: read at one, read and write at the other', 2,
     R(('rule', 0, 'N', 'r'), ('save', 0), ('rule', 1, 'N', 'rw'), ('save', 1), ('merge', 'kp_sync', 0, 1)), 0),
    ('21', 'a name removed in one copy, removed and added again in the other (design scenario 4)', 2,
     R(('prem', 1, 'pf'), ('padd', 1, 'pf'), ('prem', 0, 'pf'), ('merge', 'kp_sync', 0, 1), ('merge', 'kp_sync', 1, 0)), 0),
    # the decisions' own cases
    ('-', 'the authority deleted on the tab, none created; a later Create', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('delauth', 0), ('create', 0)), 0),
    ('-', 'the same, then the root ruled again, granting', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('delauth', 0), ('create', 0), ('rule', 0, 'root', 'rw'), ('save', 0)), 0),
    ('-', 'the retirement that carries the key deleted for good by hand after a Delete; a later Create', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('delauth', 0), ('del', 0, ('ref', 'retired'), 1), ('create', 0)), 0),
    ('-', 'the same, then the root ruled again, granting', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('delauth', 0), ('del', 0, ('ref', 'retired'), 1), ('create', 0),
       ('rule', 0, 'root', 'rw'), ('save', 0)), 0),
    ('-', 'a widening decided at another gate that saw the narrowing, then Synchronize at this gate', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('rule', 1, 'N', 'rw'), ('save', 1),
       ('merge', 'kp_sync', 0, 1)), 0),
    ('-', 'a name added again at another gate that saw its removal, then Synchronize at this gate', 2,
     R(('prem', 0, 'pf'), ('merge', 'kp_sync', 1, 0), ('padd', 1, 'pf'), ('merge', 'kp_sync', 0, 1)), 0),
    ('-', 'a renewal allowed again at another gate that saw the refusal, then Synchronize at this gate', 2,
     R(('renew', 0, 'c0', 'forever'), ('merge', 'kp_sync', 1, 0), ('renew', 1, 'c0', 'allowed'), ('merge', 'kp_sync', 0, 1)), 0),
    ('-', 'a copy from before a Recreate synchronized into a copy after it', 2,
     R(('rule', 0, 'N', 'none'), ('save', 0), ('merge', 'kp_sync', 1, 0), ('recreate', 0), ('rule', 1, 'N', 'rw'),
       ('save', 1), ('merge', 'kp_sync', 0, 1)), 0),
]


def part_section6(n):
    cols = [('store', STORE)] + [(k, f) for k, _, f in DECISIONS]
    for row, what, nrep, ops, i in SEQ:
        print('## row %s: %s' % (row, what))
        print('   history: ' + ' ; '.join(fmt(op) for op in ops) + '   (file asked: %d)' % i)
        for name, fl in cols:
            W = M.World(nrep, fl)
            done = []
            for op in ops:
                done.append('+' if W.step(op) else '-')
            res = classify(W, i)
            line = '   %-9s %s  [%s]' % (name, ' | '.join(res), ''.join(done))
            if name == 'store':
                print(line)
                for j in range(nrep):
                    print('             ' + view(W, j))
                fails = M.run(ops, nrep, fl, check_conv=False)
                print('             the model\'s checks: %s' % ('; '.join('%s at step %d (%s)' % f for f in fails) or 'none failed'))
            else:
                print(line)
        print()


def part_beyond(n):
    """ADDED (rules per profile): how often a merge gives, at a node, a letter that neither copy gave
    just before it. Random histories over two copies of acts on rules and merges (KeePass's
    Synchronize, its other methods, KeePassXC), 4 to 10 steps; at each merge that applies, the
    rules' answer at the root and at N of the merged copy is held to both copies' answers before
    it. Per profile: the acts on a single row and on the switch. The whole-rule form: rule acts of
    whole tables, every profile named or some left out."""
    import random
    rng = random.Random(34)
    flags = M.STORE.but(repair=False, pending=False)
    R = range(2)
    acts = []
    for r in R:
        for node in ('root', 'N'):
            if M.ROWS is not None:
                acts += [('switch', r, node, x) for x in ('deny', 'inherit')]
                acts += [('row', r, node, p, x) for p in M.PROFS for x in ('rw', 'r', '', 'off')]
            else:
                acts += [('rule', r, node, d) for d in list(M.DOCS) + ['pf=r', 'pg=r', 'pf=rw,pg=', 'pf=,pg=rw']]
    merges = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in R for s in R if d != s]
    hists, events, by_node, shortest = 0, 0, {}, {}
    for _ in range(n):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(4, 10))]
        W = M.World(2, flags)
        hit = False
        for k, op in enumerate(ops):
            pre = None
            if op[0] == 'merge':
                pre = (W.fold(W.reps[op[2]])['words_raw'], W.fold(W.reps[op[3]])['words_raw'])
            if not W.step(op):
                continue
            if pre is None:
                continue
            after = W.fold(W.reps[op[2]])['words_raw']
            for node in ('root', 'N'):
                extra = after.get(node, frozenset()) - pre[0].get(node, frozenset()) - pre[1].get(node, frozenset())
                if extra:
                    events += 1
                    hit = True
                    by_node[node] = by_node.get(node, 0) + 1
                    kind = 'with a switch act' if any(o[0] == 'switch' for o in ops[:k]) else 'without a switch act'
                    if kind not in shortest or k + 1 < len(shortest[kind][0]):
                        shortest[kind] = (ops[:k + 1], node, M.words_text(extra))
        hists += hit
    print('== %d histories over two copies; histories with a merge that gave a letter neither copy gave just'
          ' before it: %d; such merges: %d (%s)' % (n, hists, events, ', '.join(
              'at %s %d' % kv for kv in sorted(by_node.items())) or 'none'))
    for kind, (ops, node, w) in sorted(shortest.items()):
        print('   shortest %s: at %s, %s: %s' % (kind, node, w, ' ; '.join(fmt(op) for op in ops)))


FOUR = ('pf', 'pg', 'ph', 'pi')     # ADDED (owed forms): four profiles
MASK = 'crusdlap'                   # ADDED (owed forms): the six verbs and the two switches
# ADDED (owed forms): the values a row act sets with the full mask: verbs alone, verbs with either
# switch or both, a switch alone (which grants nothing), an empty rule, and the rule taken off
FULL_VALUES = ('crusdlap', 'crudl', 'rl', 'rla', 'rlp', 'cula', 'a', '', 'off')
RW_VALUES = ('rw', 'r', '', 'off')  # the values `beyond` drew


def mask_text(w):
    """ADDED (owed forms): letters per profile in the order of the mask; `nothing` for no letter."""
    order = MASK + 'w'
    per = {}
    for x in w:
        p, _, v = x.rpartition('.')
        per.setdefault(p, []).append(v)
    return ' '.join('%s:%s' % (p, ''.join(sorted(per[p], key=order.index))) for p in sorted(per)) or 'nothing'


def node_value(vals, x, deny):
    """ADDED (owed forms): what a row whose standing versions are `vals` gives a profile, by the
    reading of crdt_model.rows_node, where `x` arrives from above. A version is a set of letters or
    None, the rule taken off."""
    grants = [v for v in vals if v is not None]
    off = len(grants) < len(vals)
    if not grants:
        return frozenset() if deny else x
    g = frozenset.intersection(*grants)
    if off:
        return frozenset() if deny else x & g
    return g


def part_combine(n):
    """ADDED (owed forms): versions of a row made apart, with the full mask. For every pair of
    versions (every set of the eight letters, and the rule taken off), every set of letters
    arriving from above and either switch of the node, the capabilities the row gives are held to
    the capabilities every version gives alone, met: what all versions allow. Then `n` random
    triples. A capability is a verb, alone or with a switch of the same rule (crdt_model.caps)."""
    import itertools
    import random
    sets = [frozenset(c) for k in range(len(MASK) + 1) for c in itertools.combinations(MASK, k)]
    caps = {s: M.caps(frozenset('p.' + w for w in s)) for s in sets}
    vals = sets + [None]
    print('== versions of a row made apart, the full mask: %d values of a version (every set of the eight letters,'
          ' and the rule taken off), %d sets arriving from above, the switch of the node refusing or inheriting'
          % (len(vals), len(sets)))
    print('   switch letters without a verb: %s grant %s' % (
        ', '.join(''.join(sorted(s, key=MASK.index)) for s in sets if s and s <= M.SWITCH_LETTERS),
        'nothing' if all(not caps[s] for s in sets if s <= M.SWITCH_LETTERS) else 'SOMETHING'))
    checked, wider, narrower, examples = 0, 0, 0, []
    for a, b in itertools.combinations_with_replacement(vals, 2):
        for deny in (False, True):
            for x in sets:
                got = caps[node_value([a, b], x, deny)]
                want = caps[node_value([a], x, deny)] & caps[node_value([b], x, deny)]
                checked += 1
                if got - want:
                    wider += 1
                    if len(examples) < 5:
                        examples.append((a, b, x, deny))
                elif want - got:
                    narrower += 1
    print('   pairs: %d combinations checked; the row gives more than what both versions allow: %d; less: %d'
          % (checked, wider, narrower))
    rng = random.Random(41)
    tw, tn = 0, 0
    for _ in range(n):
        vs = [rng.choice(vals) for _ in range(3)]
        x, deny = rng.choice(sets), rng.random() < 0.5
        got = caps[node_value(vs, x, deny)]
        want = caps[node_value([vs[0]], x, deny)] & caps[node_value([vs[1]], x, deny)] & caps[node_value([vs[2]], x, deny)]
        tw += bool(got - want)
        tn += bool(want - got)
    print('   random triples: %d; more than what all three allow: %d; less: %d' % (n, tw, tn))
    for a, b, x, deny in examples:
        print('   EXAMPLE %s and %s, arriving %s, switch %s' % (a, b, ''.join(sorted(x, key=MASK.index)), deny))
    # what letters alone would have answered: the letters of the met versions against the letters
    # both copies gave, and the capabilities against the capabilities both gave
    print('== a merge held to both copies by letters and by capabilities: a node value that is within the'
          ' union of two copies\' letters and still beyond the union of their capabilities')
    count, first = 0, None
    for a, b in itertools.product(sets, repeat=2):
        for g in sets:
            if g <= a | b and caps[g] - caps[a] - caps[b]:
                count += 1
                if first is None or len(a) + len(b) + len(g) < sum(len(s) for s in first):
                    first = (a, b, g)
    print('   triples of letter sets (copy 0, copy 1, merged) with the merged letters within the copies\' letters'
          ' and a capability neither copy gave: %d of %d; smallest: copy 0 %s, copy 1 %s, merged %s' % (
              count, len(sets) ** 3, ''.join(sorted(first[0], key=MASK.index)) or '-', ''.join(sorted(first[1], key=MASK.index)) or '-',
              ''.join(sorted(first[2], key=MASK.index))))


def part_beyond_owed(n, nodes, values, seed):
    """ADDED (owed forms): `beyond` with the configuration's profiles, letters and levels, the row
    acts drawing from `values`. Each merge is held to both copies just before it by letters and by
    capabilities. Each history in which a merge gives more is shrunk to the shortest history that
    still gives it at the same node, and that history is named by the nodes its acts touch."""
    import random
    rng = random.Random(seed)
    flags = M.STORE.but(repair=False, pending=False)
    R = range(2)
    acts = []
    for r in R:
        for node in nodes:
            acts += [('switch', r, node, x) for x in ('deny', 'inherit')]
            acts += [('row', r, node, p, x) for p in M.PROFS for x in values]
    merges = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in R for s in R if d != s]

    def extra(W, op, pre):
        after = W.fold(W.reps[op[2]])['words_raw']
        out = []
        for node in nodes:
            a, b0, b1 = (x.get(node, frozenset()) for x in (after, pre[0], pre[1]))
            letters = a - b0 - b1
            c = M.caps(a) - M.caps(b0) - M.caps(b1)
            if letters or c:
                out.append((node, 'letters' if letters else 'capabilities only', letters, c))
        return out

    def replay(ops):
        W = M.World(2, flags)
        found = []
        for k, op in enumerate(ops):
            pre = None
            if op[0] == 'merge':
                pre = (W.fold(W.reps[op[2]])['words_raw'], W.fold(W.reps[op[3]])['words_raw'])
            if not W.step(op):
                continue
            if pre is not None:
                found += [(k,) + e for e in extra(W, op, pre)]
        return found

    def shrink(ops, node, kind):
        changed = True
        while changed:
            changed = False
            for k in range(len(ops) - 1):
                cand = ops[:k] + ops[k + 1:]
                if any(f[0] == len(cand) - 1 and f[1] == node and f[2] == kind for f in replay(cand)):
                    ops, changed = cand, True
                    break
        return ops

    hists, events, by, shortest = {}, {}, {}, {}
    for _ in range(n):
        ops = [rng.choice(acts if rng.random() < 0.6 else merges) for _ in range(rng.randint(4, 10))]
        W = M.World(2, flags)
        hit = set()
        for k, op in enumerate(ops):
            pre = None
            if op[0] == 'merge':
                pre = (W.fold(W.reps[op[2]])['words_raw'], W.fold(W.reps[op[3]])['words_raw'])
            if not W.step(op):
                continue
            if pre is None:
                continue
            for node, kind, letters, c in extra(W, op, pre):
                events[kind] = events.get(kind, 0) + 1
                hit.add(kind)
                small = shrink(ops[:k + 1], node, kind)
                touched = sorted(set(o[2] for o in small if o[0] in ('row', 'switch')))
                where = 'at a single node' if touched == [node] else 'across nodes'
                key = (kind, node, where)
                by[key] = by.get(key, 0) + 1
                text = ' ; '.join(fmt(o) for o in small)
                if key not in shortest or (len(small), text) < (len(shortest[key][0]), shortest[key][1]):
                    shortest[key] = (small, text, letters, c)
        for kind in hit:
            hists[kind] = hists.get(kind, 0) + 1
        if hit:
            hists['any'] = hists.get('any', 0) + 1
    print('== %d histories over two copies, %d profiles, letters %s, nodes %s, row values %s' % (
        n, len(M.PROFS), ''.join(sorted(M.ALL, key=(MASK + 'w').index)), ', '.join(nodes),
        ', '.join(v or 'empty' for v in values)))
    print('   histories with a merge that gave more than both copies just before it: %d; by letters: %d; by'
          ' capabilities only (every letter given by a copy, a combination given by neither): %d' % (
              hists.get('any', 0), hists.get('letters', 0), hists.get('capabilities only', 0)))
    print('   such merges: by letters %d, by capabilities only %d' % (
        events.get('letters', 0), events.get('capabilities only', 0)))
    for key, c in sorted(by.items()):
        print('   %s at %s, %s: %d' % (key[0], key[1], key[2], c))
    for key, (small, text, letters, c) in sorted(shortest.items()):
        print('   shortest, %s at %s, %s (%d steps): gives %s; %s' % (
            key[0], key[1], key[2], len(small), mask_text(letters) if letters else M.caps_text(c), text))


def main():
    # ADDED (owed forms): --owed combine [COUNT], or --owed PROFILES LETTERS LEVELS beyond COUNT SEED:
    # the forms STORE.md 644 item 24 left owed, each a rule per profile with a removed row written as
    # decided. PROFILES 2 or 4, LETTERS rw or the mask, LEVELS 2 or 3
    if sys.argv[1] == '--owed':
        sys.stdout.reconfigure(line_buffering=True)
        t0 = time.time()
        M.ROWS, M.PP = 'empty', True
        if sys.argv[2] == 'combine':
            part_combine(int(sys.argv[3]) if len(sys.argv) > 3 else 200000)
        else:
            profs, letters, levels = int(sys.argv[2]), sys.argv[3], int(sys.argv[4])
            M.configure(FOUR[:profs], letters, levels)
            nodes = ('root', 'G', 'N') if levels == 3 else ('root', 'N')
            part_beyond_owed(int(sys.argv[6]), nodes, RW_VALUES if letters == 'rw' else FULL_VALUES,
                             int(sys.argv[7]))
        print('== %.0f s' % (time.time() - t0))
        return
    # ADDED (decided store): --decided runs search, search-noauth or ext on the store as decided on
    # 2026-11-16, which has no repair (decision 14). The model holds no entry history, so an edit is
    # still read as damage there; the history rule is run on real files (cases.py history).
    global STORE
    if sys.argv[1] == '--decided':
        del sys.argv[1]
        if sys.argv[1] not in ('search', 'search-noauth', 'ext'):
            raise SystemExit('--decided runs search, search-noauth and ext only')
        STORE = M.STORE.but(repair=False)
        print('== the store as decided: no repair after a Synchronize')
    # ADDED (rules per profile): --rows VARIANT runs search on the store as decided with a rule
    # stored as a record for each node and profile (crdt_model.ROWS); `--rows VARIANT acts` also puts
    # the acts on a single row and on the switch into the alphabet
    elif sys.argv[1] == '--rows':
        # `--rows whole beyond`: the part `beyond` in the whole-rule form
        M.ROWS, M.PP = (sys.argv[2] if sys.argv[2] != 'whole' else None), True
        del sys.argv[1:3]
        if sys.argv[1] == 'acts':
            M.ROW_ACTS = True
            del sys.argv[1]
        if sys.argv[1] not in ('search', 'search-noauth', 'beyond'):
            raise SystemExit('--rows runs search, search-noauth and beyond only')
        STORE = M.STORE.but(repair=False)
        print('== the store as decided, a rule stored as a record for each node and profile, a removed row'
              ' written as %s%s' % (M.ROWS, '; the acts on a row and on the switch in the alphabet'
                                   if M.ROW_ACTS else ''))
    part = sys.argv[1]
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 20000
    sys.stdout.reconfigure(line_buffering=True)
    t0 = time.time()
    {'search': part_search, 'search-noauth': part_search_noauth, 'ext': part_ext, 'scenarios': part_scenarios,
     'ablations': part_ablations, 'section6': part_section6,
     'beyond': part_beyond}[part](n)     # ADDED (rules per profile): beyond
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
