"""The lost versions a version settles, told apart from the versions it replaces: the three
behaviors of crdt_model.JUDGE side by side on the same sequences, in the model and in read.py over
the files store.py's Encoder writes of it.

Run:  uv run --no-project python -I -X utf8 lost_runs.py PART [ARGS], its output into out\\lost

Parts:
  tags                       the names the runs use: two whose tags meet under the model's key, one
                             whose tag does not, a name whose tag is 0, and the names the writes that
                             touch no rule add
  hand                       the sequences of the report, each step under each behavior
  search CONFIG COUNT SEED LO HI [WORKERS]
                             random histories over two copies; CONFIG is two (the root and N),
                             three (the root, G and N), zero (a name whose tag is 0 and the object
                             of N's rule with no slash) or hand (two, with a person's deletions and
                             Seal again)
  all OUT [WORKERS] [PARTS]  every part of PARTS below, each into OUT\\<part>.txt

Each behavior is played in a world of its own over the same acts, with the rows of `measured-rules-
per-profile` in the form by what arrived, a removed row taken off for real, as the vectors play
them (crdt_model.ROWS 'off', DEP 'seen'), and the identifier of a row's version in the form
`split` with the tag of crdt_model.TAG_OF. Beside each file the exact reading of the same file is
made (`World.fold(exact=True)`): every deletion record told of by the object of the element it
names, which the model knows and a file does not. Every random choice is drawn from a
random.Random with a seed given on the command line. Written for `measured-lost-settled.md`.
"""
import multiprocessing
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'probe'))
import crdt_model as M  # noqa: E402
import read as RD  # noqa: E402
import store as S  # noqa: E402

KEY = S.keybytes('K')       # the store key of the model's label K, as the Encoder derives it
JUDGES = ('union', 'object', 'lost')
FLAGS = M.STORE.but(repair=False, pending=False)
ROOT, G, N = 'aa' * 16, 'dd' * 16, 'bb' * 16
INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)
KEPT = ('kept0', 'kept1', 'kept2', 'kept3', 'kept4', 'kept5')     # names a write that touches no rule adds


def tag(name):
    return S.profile_tag(KEY, name)


def meeting():
    """The first two names q00000, q00001, ... whose tags meet under KEY, and the first name whose
    tag is 0000."""
    seen, pair, zero, i = {}, None, None, 0
    while pair is None or zero is None:
        name = 'q%05d' % i
        t = tag(name)
        if pair is None and t in seen:
            pair = (seen[t], name)
        seen.setdefault(t, name)
        if zero is None and t == '0000':
            zero = name
        i += 1
    return pair, zero, i


(P, Q), Z, TRIED = meeting()
ALIAS = {P: 'p', Q: 'q', 'pf': 'r', Z: 'z', 'pg': 'g'}


def al(name):
    return ALIAS.get(name, name)


def setup(profs, levels=2, root_rows=None):
    """The model, store.py and read.py configured: these profiles with read and write, two or three
    levels, the rows in the form by what arrived with a removed row taken off for real."""
    M.PARENT = {}
    M.configure(profs, 'rw', levels)
    M.ROWS, M.PP, M.DEP, M.ROOT_ROWS = 'off', True, 'seen', root_rows
    S.ID_FORM, S.DEP, S.PROFS, S.LETTERS = 'split', 'seen', tuple(profs), 'rw'
    RD.ROWS = RD.PP = True
    RD.LETTERS = 'rw'


def use(judge, tags=True):
    """A behavior: the judgment in the model and in read.py, and the tags of names (None: every
    deletion record told of by its own object, as though no two tags met)."""
    M.JUDGE = RD.JUDGE = judge
    M.TAG_OF = tag if tags else None


def nodes_of():
    return ['root', 'G', 'N'] if M.PARENT else ['root', 'N']


# ------------------------------------------------------------------ what a file answers
def gives(v, node, p):
    return frozenset(x.split('.', 1)[1] for x in v['words_raw'].get(node, ()) if x.split('.', 1)[0] == p)


def refusing(v):
    """The rows and switches whose value refuses: a lost version, or a version present beside its
    deletion record with nothing standing."""
    return frozenset(o for o, f in v['regval'].items() if f is not None and f[0] == 'const'
                     and (o.startswith('rule:') or o.startswith('sw:')))


def look(W, i):
    """File i under the behavior in use and under the exact reading."""
    rep = W.reps[i]
    return W.fold(rep), W.fold(rep, exact=True)


def wider(v, x):
    """(node, profile) where the behavior gives a capability the exact reading of the same file
    does not."""
    out = []
    for n in nodes_of():
        for p in M.PROFS:
            c = M.caps(frozenset(p + '.' + w for w in gives(v, n, p))) - \
                M.caps(frozenset(p + '.' + w for w in gives(x, n, p)))
            if c:
                out.append((n, p))
    return out


def wrongly_refused(W, v, x):
    """Rows that refuse under the behavior and not under the exact reading, where no deletion record
    they refuse for is of a loss: a version whose content is nowhere (the act `lose`) or that a
    person deleted (the act `del`), or a version replaced only by versions a person deleted, whose
    trace is then that deletion record alone. That is a row that refuses with no lost version of its
    own, at its place, behind it."""
    loses = getattr(W, 'loses', frozenset())

    def loss(u):
        return u in loses or any(e.id in loses for e in W.EL.values() if e.kind == 'ver' and u in e.after)
    return sorted(o for o, ids in v['lost'].items()
                  if o not in x['lost'] and not any(loss(u) for u in ids)
                  and (o.startswith('sw:') or (o.startswith('rule:') and '/' in o)))


def beyond(after, pre):
    """(node, profile) where a merged copy gives a capability neither copy gave just before."""
    out = []
    for n in nodes_of():
        for p in M.PROFS:
            def c(v):
                return M.caps(frozenset(p + '.' + w for w in gives(v, n, p)))
            if c(after) - c(pre[0]) - c(pre[1]):
                out.append((n, p))
    return out


def plain_write(W, i, v):
    """A write of the plugin's own that touches no rule, on a clone of W: a name of KEPT added, or
    taken off the list where every name is on it. The clone, or None where the store does not
    take a write."""
    if v['status'] != 'ok':
        return None
    C = W.clone()
    for n in KEPT:
        if n not in v['names']:
            return C if C.step(('padd', i, n)) else None
    return C if C.step(('prem', i, KEPT[0])) else None


def changed_by_write(W, i):
    """Whether a write that touches no rule changes what file i gives or refuses."""
    v = W.fold(W.reps[i])
    C = plain_write(W, i, v)
    if C is None:
        return []
    a = C.fold(C.reps[i])
    out = []
    for n in nodes_of():
        for p in M.PROFS:
            if gives(v, n, p) != gives(a, n, p):
                out.append('%s %s %s to %s' % (n, al(p), ml(gives(v, n, p)), ml(gives(a, n, p))))
    if refusing(v) != refusing(a):
        out.append('refusing %s to %s' % (objs(refusing(v)), objs(refusing(a))))
    return out


def ml(w):
    return ''.join(sorted(w)) or '-'


def obj_text(o):
    if o.startswith('rule:') and '/' in o:
        node, p = o[5:].split('/', 1)
        return '%s/%s' % (node, al(p))
    if o.startswith('sw:'):
        return o[3:] + '/switch'
    return o[5:] + '/(no slash)' if o.startswith('rule:') else o


def objs(s):
    return ', '.join(obj_text(o) for o in sorted(s)) or 'none'


def op_text(op):
    out = []
    for x in op:
        if isinstance(x, tuple):
            out.append('(%s)' % ' '.join(str(y) for y in x))
        elif isinstance(x, str) and ('/' in x or x in ALIAS):
            out.append(obj_text(x) if x.startswith(('rule:', 'sw:')) else al(x))
        elif isinstance(x, str) and '=' in x:
            out.append(','.join('%s=%s' % (al(a.split('=')[0]), a.split('=')[1]) for a in x.split(',')))
        else:
            out.append(str(x))
    return ' '.join(out)


# ------------------------------------------------------------------ files: Encoder and read.py
def tree():
    if M.PARENT:
        return {'root': ROOT, 'G': G, 'N': N}, {G: ROOT, N: G}
    return {'root': ROOT, 'N': N}, {N: ROOT}


def file_of(W, enc, i):
    """File i as store.py's Encoder writes it, as a dump read.py reads."""
    nodes, groups = tree()
    rep = W.reps[i]
    entries = {}
    for eid in sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid and ok):
        u, title, s, prot = enc.entry(eid)
        entries[u] = {'strings': s, 'history': [], 'place': 'S', 'parent': ROOT}
    return {'root': ROOT, 'groups': {g: {'parent': p} for g, p in groups.items()},
            'dels': {enc.u(x): 0 for x in rep.dels}, 'entries': entries}


def bridge(W, enc, i, v):
    """Where read.py's reading of file i as written differs from the model's fold of it: what each
    node gives each profile, and the rows and switches lost."""
    nodes, groups = tree()
    r = RD.read(file_of(W, enc, i), history=True)
    out = []
    for n in nodes_of():
        for p in M.PROFS:
            a = gives(v, n, p)
            b = frozenset(x.split('.', 1)[1] for x in r['words_raw'].get(nodes[n], ()) if x.split('.', 1)[0] == p)
            if a != b:
                out.append('%s %s: model %s, file %s' % (n, al(p), ml(a), ml(b)))
    ml_ = sorted(enc.obj(o) for o in v['lost'])
    if ml_ != sorted(r['lost']):
        out.append('lost: model %s, file %s' % (ml_, sorted(r['lost'])))
    if (v['status'] == 'ok') != (r['status'] == 'ok'):
        out.append('status: model %s, file %s' % (v['status'], r['status']))
    return out


# ------------------------------------------------------------------ the sequences of the report
def hand_sequences():
    """(label, what, profiles, levels, root rows, acts). A step `('check',)` is no act."""
    rp, rq, rz = 'rule:N/' + P, 'rule:N/' + Q, 'rule:N/' + Z
    two = ('pf', P, Q)
    seqs = []
    seqs.append(('F1', 'the fail-open sequence: the root grants p every letter, N holds an empty rule '
                 'for p made in copy 0 after the copies parted, deleted there for good with a deletion '
                 'record and its summary entry deleted with none, synchronized into copy 1, which wrote '
                 'a summary entry of its own meanwhile; copy 1 then sets q\'s row alone',
                 two, 2, None,
                 [('row', 0, 'root', P, 'rw'), INTO_1, ('padd', 1, 'kept0'), ('row', 0, 'N', P, ''),
                  ('del', 0, ('ver', rp, 'new'), 1), ('del', 0, ('head', 'new'), 0), INTO_1,
                  ('row', 1, 'N', Q, 'r'), ('padd', 1, 'kept1')]))
    seqs.append(('E1', 'ordinary edits of q at N where p holds a rule: q set, changed, taken off, set '
                 'again, in copy 0; synchronized; q emptied in copy 1; both ways',
                 two, 2, None,
                 [('row', 0, 'N', P, 'rw'), ('row', 0, 'N', Q, 'r'), ('row', 0, 'N', Q, 'rw'),
                  ('row', 0, 'N', Q, 'off'), ('row', 0, 'N', Q, 'r'), INTO_1, ('row', 1, 'N', Q, ''),
                  INTO_0, INTO_1]))
    seqs.append(('E2', 'E1 with p and q swapped',
                 two, 2, None,
                 [('row', 0, 'N', Q, 'rw'), ('row', 0, 'N', P, 'r'), ('row', 0, 'N', P, 'rw'),
                  ('row', 0, 'N', P, 'off'), ('row', 0, 'N', P, 'r'), INTO_1, ('row', 1, 'N', P, ''),
                  INTO_0, INTO_1]))
    seqs.append(('E3', 'ordinary edits of q at N and at the group G above it, where p holds a rule at '
                 'both; then of q in copy 1 at both, both ways',
                 two, 3, None,
                 [('row', 0, 'G', P, 'r'), ('row', 0, 'N', P, 'rw'), ('row', 0, 'G', Q, 'rw'),
                  ('row', 0, 'G', Q, 'r'), ('row', 0, 'N', Q, 'r'), ('row', 0, 'N', Q, 'off'),
                  ('row', 0, 'G', Q, 'off'), INTO_1, ('row', 1, 'G', Q, 'rw'), ('row', 1, 'N', Q, 'rw'),
                  INTO_0, INTO_1]))
    base = [('row', 0, 'N', P, 'r'), ('row', 0, 'N', Q, 'r'), INTO_1, ('lose', 0, rp), INTO_1]
    seqs.append(('L1', 'a true loss of p\'s version at N (a deletion record at the place of p\'s row '
                 'whose content is nowhere), in both copies; q set first, then p', two, 2, None,
                 base + [('row', 0, 'N', Q, 'rw'), ('row', 0, 'N', P, 'rw'), INTO_1, INTO_0]))
    seqs.append(('L2', 'the same loss; p set first, then q', two, 2, None,
                 base + [('row', 0, 'N', P, 'rw'), ('row', 0, 'N', Q, 'rw'), INTO_1, INTO_0]))
    seqs.append(('L3', 'the same loss; both set in a single write', two, 2, None,
                 base + [('rule', 0, 'N', '%s=rw,%s=rw' % (P, Q)), INTO_1, INTO_0]))
    seqs.append(('L4', 'the same loss; q set in copy 0 and p in copy 1, then both ways', two, 2, None,
                 base + [('row', 0, 'N', Q, 'rw'), ('row', 1, 'N', P, 'rw'), INTO_0, INTO_1]))
    four = ('pf', 'pg', P, Q)
    seqs.append(('T1', 'the sequence of the product\'s test of two names whose tags meet: both rows set '
                 'at N in a single write in copy 0, synchronized into copy 1, which then writes a name of '
                 'its own as the test\'s next write does; a loss at p\'s place in copy 0; p set, then q, '
                 'in copy 0; synchronized into copy 1, and its next write', four, 2, ('pf', 'pg'),
                 [('rule', 0, 'N', '%s=r,%s=r' % (P, Q)), INTO_1, ('padd', 1, 'kept0'), ('lose', 0, rp),
                  ('row', 0, 'N', P, 'rw'), ('row', 0, 'N', Q, 'rw'), INTO_1, ('padd', 1, 'kept1')]))
    seqs.append(('T2', 'T1 with q set before p', four, 2, ('pf', 'pg'),
                 [('rule', 0, 'N', '%s=r,%s=r' % (P, Q)), INTO_1, ('padd', 1, 'kept0'), ('lose', 0, rp),
                  ('row', 0, 'N', Q, 'rw'), ('row', 0, 'N', P, 'rw'), INTO_1, ('padd', 1, 'kept1')]))
    seqs.append(('T3', 'T1 with the copies exchanged', four, 2, ('pf', 'pg'),
                 [('rule', 1, 'N', '%s=r,%s=r' % (P, Q)), INTO_0, ('padd', 0, 'kept0'), ('lose', 1, rp),
                  ('row', 1, 'N', P, 'rw'), ('row', 1, 'N', Q, 'rw'), INTO_0, ('padd', 0, 'kept1')]))
    zero = ('pf', Z)
    seqs.append(('Z1', 'a name z whose tag is 0 and the object of N\'s rule with no slash: each set and '
                 'changed, synchronized, z set in copy 1, both ways', zero, 2, None,
                 [('row', 0, 'N', Z, 'r'), ('plain', 0, 'N', 'r'), ('plain', 0, 'N', 'rw'),
                  ('row', 0, 'N', Z, 'rw'), ('plain', 0, 'N', ''), INTO_1, ('row', 1, 'N', Z, 'r'),
                  INTO_0, INTO_1]))
    seqs.append(('Z2', 'a loss at the place of z\'s row, which the object with no slash shares; z set '
                 'again', zero, 2, None,
                 [('row', 0, 'N', Z, 'r'), ('plain', 0, 'N', 'r'), ('lose', 0, rz), ('row', 0, 'N', Z, 'rw'),
                  INTO_1]))
    seqs.append(('W1', 'the switch of N edited, then a loss of a version of it, and the switch set again',
                 two, 2, None,
                 [('row', 0, 'N', P, 'r'), ('switch', 0, 'N', 'deny'), ('switch', 0, 'N', 'inherit'),
                  ('switch', 0, 'N', 'deny'), INTO_1, ('lose', 0, 'sw:N'), ('switch', 0, 'N', 'deny'),
                  INTO_1]))
    return seqs


def part_tags():
    print('== The names (the profile tag of STORE.md 609 under the store key of the model\'s label K):')
    print('   p = %s and q = %s, tag %s: the first two names q00000, q00001, ... whose tags meet, after %d'
          ' names tried' % (P, Q, tag(P), TRIED))
    print('   r = pf, tag %s; g = pg, tag %s; z = %s, tag %s, the first such name whose tag is 0000' % (
        tag('pf'), tag('pg'), Z, tag(Z)))
    print('   names a write that touches no rule adds: %s' % ', '.join('%s %s' % (n, tag(n)) for n in KEPT))


def part_hand():
    part_tags()
    print('== Each sequence played three times, a world for each behavior, over the same acts. After each')
    print('   step, the copy it changed: what N (and G) gives each profile; the rows and switches that')
    print('   refuse; WIDER where the behavior gives a capability the exact reading of the same file does')
    print('   not; REFUSED where a row refuses under the behavior and not under the exact reading for no')
    print('   loss whose content is nowhere; BEYOND at a merge that gives a capability neither copy gave')
    print('   just before; CHANGED where a write that touches no rule, made on a clone after a merge,')
    print('   changes what the copy gives or refuses; and read.py over the file the Encoder writes,')
    print('   with any difference from the model\'s fold.')
    tally = {}
    for label, what, profs, levels, root_rows, ops in hand_sequences():
        setup(profs, levels, root_rows)
        print()
        print('## %s: %s' % (label, what))
        print('   profiles %s; nodes %s' % (', '.join(al(p) for p in profs), ', '.join(nodes_of())))
        worlds = {}
        for j in JUDGES:
            use(j)
            worlds[j] = (M.World(2, FLAGS), None)
            W = worlds[j][0]
            W.maxclients = 4
            worlds[j] = (W, S.Encoder(W, tree()[0]))
        for k, op in enumerate(ops):
            print('   %d. %s' % (k + 1, op_text(op)))
            for j in JUDGES:
                use(j)
                W, enc = worlds[j]
                i = op[2] if op[0] == 'merge' else op[1]
                pre = None
                if op[0] == 'merge':
                    pre = (W.fold(W.reps[op[2]]), W.fold(W.reps[op[3]]))
                ok = W.step(op)
                v, x = look(W, i)
                flags = []
                wd = wider(v, x)
                if wd:
                    flags.append('WIDER at %s' % ', '.join('%s for %s (%s, exact %s)' % (
                        n, al(p), ml(gives(v, n, p)), ml(gives(x, n, p))) for n, p in wd))
                    tally[(j, 'wider')] = tally.get((j, 'wider'), 0) + 1
                wr = wrongly_refused(W, v, x)
                if wr:
                    flags.append('REFUSED %s' % objs(wr))
                    tally[(j, 'refused')] = tally.get((j, 'refused'), 0) + 1
                if pre is not None and ok:
                    bd = beyond(v, pre)
                    if bd:
                        flags.append('BEYOND at %s' % ', '.join('%s for %s' % (n, al(p)) for n, p in bd))
                        tally[(j, 'beyond')] = tally.get((j, 'beyond'), 0) + 1
                    ch = changed_by_write(W, i)
                    if ch:
                        flags.append('CHANGED by a write that touches no rule: %s' % '; '.join(ch))
                        tally[(j, 'changed')] = tally.get((j, 'changed'), 0) + 1
                br = bridge(W, enc, i, v)
                if br:
                    flags.append('FILE DIFFERS: %s' % '; '.join(br))
                    tally[(j, 'file')] = tally.get((j, 'file'), 0) + 1
                gv = '; '.join('%s %s' % (n, ' '.join('%s:%s' % (al(p), ml(gives(v, n, p))) for p in profs))
                               for n in nodes_of() if n != 'root')
                print('      %-6s copy %d%s: %s; refusing %s%s' % (
                    j, i, '' if ok else ' (not played)', gv, objs(refusing(v)),
                    ''.join('; ' + f for f in flags)))
    print()
    print('== tally: steps flagged, by behavior')
    for j in JUDGES:
        print('   %-6s wider %d, refused %d, beyond %d, changed %d, file differs %d' % (
            j, tally.get((j, 'wider'), 0), tally.get((j, 'refused'), 0), tally.get((j, 'beyond'), 0),
            tally.get((j, 'changed'), 0), tally.get((j, 'file'), 0)))


# ------------------------------------------------------------------ the search
CONFIGS = {
    'two': (('pf', P, Q), 2),
    'three': (('pf', P, Q), 3),
    'zero': (('pf', Z), 2),
    'hand': (('pf', P, Q), 2),
}
VALUES = ('rw', 'r', '', 'off')
MERGES = [('merge', p, d, s) for p in ('kp_sync', 'kp_other', 'xc') for d in (0, 1) for s in (0, 1) if d != s]


def alphabet(config):
    profs, levels = CONFIGS[config]
    nodes = ['root', 'G', 'N'] if levels == 3 else ['root', 'N']
    acts = []
    for r in (0, 1):
        for node in nodes:
            acts += [('switch', r, node, x) for x in ('deny', 'inherit')]
            acts += [('row', r, node, p, x) for p in profs for x in VALUES]
            acts += [('settle', r, node, p) for p in profs]
            acts += [('lose', r, 'rule:%s/%s' % (node, p)) for p in profs]
            acts.append(('lose', r, 'sw:' + node))
        if config == 'zero':
            acts += [('plain', r, 'N', x) for x in ('rw', 'r', '')]
            acts.append(('lose', r, 'rule:N'))
        if config == 'hand':    # a person's deletions, and Seal again where the store stops
            for node in nodes:
                acts += [('del', r, ('ver', 'rule:%s/%s' % (node, p), 'new'), rec) for p in profs for rec in (0, 1)]
            acts += [('del', r, ('head', 'new'), rec) for rec in (0, 1)]
            acts.append(('reseal', r))
    return acts


# What a history is flagged for, for a behavior. The first three are failures of the behavior; a
# merge beyond both copies is classed by the exact readings of the same files.
FAILS = ('wider', 'refused', 'changed')
BEYONDS = ('beyond, wider than the exact reading', 'beyond, the exact reading beyond both as well',
           'beyond, within what the copies give read exactly')


def play(config, ops, sides=JUDGES):
    """Each behavior's flags at each step: {side: [(step, kind, detail)]}. After every step the copy
    it changed is held to the exact reading of the same file (wider, refused); after every merge and
    every loss a write that touches no rule is made on a clone (changed); a merge that gives a
    capability neither copy gave just before (beyond) is classed by the exact readings."""
    profs, levels = CONFIGS[config]
    setup(profs, levels)
    found = {s: [] for s in sides}
    for side in sides:
        use(side)
        W = M.World(2, FLAGS)
        cur = [W.fold(W.reps[0]), W.fold(W.reps[1])]
        curx = [W.fold(W.reps[0], exact=True), W.fold(W.reps[1], exact=True)]
        for k, op in enumerate(ops):
            i = op[2] if op[0] == 'merge' else op[1]
            pre = (cur[op[2]], cur[op[3]], curx[op[2]], curx[op[3]]) if op[0] == 'merge' else None
            if not W.step(op):
                continue
            v, x = look(W, i)
            cur[i], curx[i] = v, x
            wd = wider(v, x)
            for n, p in wd:
                found[side].append((k, 'wider', (n, p)))
            for o in wrongly_refused(W, v, x):
                found[side].append((k, 'refused', o))
            if pre is not None:
                for n, p in beyond(v, pre[:2]):
                    if (n, p) in wd:
                        kind = BEYONDS[0]
                    elif (n, p) in beyond(x, pre[2:]):
                        kind = BEYONDS[1]
                    else:
                        kind = BEYONDS[2]
                    found[side].append((k, kind, (n, p, op[1])))
            if op[0] in ('merge', 'lose', 'del') and changed_by_write(W, i):
                found[side].append((k, 'changed', i))
    return found


def kinds_of(found, side):
    return set(kind for k, kind, d in found.get(side, []))


def draw(rng, acts, lo, hi):
    return [rng.choice(acts if rng.random() < 0.6 else MERGES) for _ in range(rng.randint(lo, hi))]


def chunk(args):
    """A chunk of the draws: the counts, the first shortest history of each behavior and kind, and
    every history failing under the refinement."""
    config, seed, count, lo, hi = args
    rng = random.Random(seed)
    acts = alphabet(config)
    hist, shortest, every, steps, losses, settles, programs = {}, {}, [], 0, 0, 0, {}
    for _ in range(count):
        ops = draw(rng, acts, lo, hi)
        steps += len(ops)
        losses += any(o[0] in ('lose', 'del') for o in ops)
        settles += any(o[0] == 'settle' for o in ops)
        found = play(config, ops)
        for side in JUDGES:
            kinds = kinds_of(found, side)
            for kind in kinds:
                hist[(side, kind)] = hist.get((side, kind), 0) + 1
                if (side, kind) not in shortest or len(ops) < len(shortest[(side, kind)]):
                    shortest[(side, kind)] = ops
                if side == 'lost' and kind in FAILS + BEYONDS[:1]:
                    every.append((kind, ops))
            if kinds & set(FAILS):
                hist[(side, 'any')] = hist.get((side, 'any'), 0) + 1
            for k, kind, d in found[side]:
                if kind in BEYONDS:
                    key = (side, kind, d[2])
                    programs[key] = programs.get(key, 0) + 1
    return hist, shortest, every, steps, losses, settles, programs


def shrink(config, ops, side, kind):
    """The shortest history, by removing one step at a time, still flagged `kind` for `side`."""
    def still(cand):
        return kind in kinds_of(play(config, cand, (side,)), side)
    changed = True
    while changed:
        changed = False
        for k in range(len(ops)):
            cand = ops[:k] + ops[k + 1:]
            if cand and still(cand):
                ops, changed = cand, True
                break
    return ops


def part_search(config, count, seed, lo, hi, workers):
    t0 = time.time()
    per = 500
    jobs = [(config, seed * 100003 + c, min(per, count - c * per), lo, hi) for c in range((count + per - 1) // per)]
    hist, shown, every, steps, losses, settles, programs = {}, {}, [], 0, 0, 0, {}
    with multiprocessing.Pool(workers) as pool:
        for h, sh, ev, s, l_, st, pr in pool.imap(chunk, jobs):
            for key, n in h.items():
                hist[key] = hist.get(key, 0) + n
            for key, n in pr.items():
                programs[key] = programs.get(key, 0) + n
            for key, ops in sh.items():
                if key not in shown or len(ops) < len(shown[key]):
                    shown[key] = ops
            every += ev
            steps += s
            losses += l_
            settles += st
    profs, levels = CONFIGS[config]
    setup(profs, levels)
    acts = alphabet(config)
    print('== %d histories over two copies, %d to %d steps, seed %d; profiles %s; nodes %s; read and write' % (
        count, lo, hi, seed, ', '.join(al(p) for p in profs), ', '.join(nodes_of())))
    print('   alphabet: %d acts (a row set to rw, r, empty or taken off, the switch, a row settled, a loss of a'
          ' version of a row or of the switch at each node%s, in either copy) and %d merges (Synchronize,'
          ' Overwrite if newer, KeePassXC, either way), an act drawn with 0.6' % (
              len(acts), {'zero': ', the object with no slash set and lost',
                          'hand': ', a person deleting a row\'s newest version or the newest summary entry with'
                                  ' a deletion record or with none, Seal again'}.get(config, ''), len(MERGES)))
    print('   steps drawn %d; histories holding a loss %d, a settle %d' % (steps, losses, settles))
    print('   a history fails where a copy, under the behavior, gives a capability the exact reading of the same'
          ' file does not, refuses a row with no loss at its place behind it, or answers otherwise after a write'
          ' that touches no rule; a merge beyond both copies is classed by the exact readings')
    for side in JUDGES:
        print('   %-6s histories failing %d: wider than the exact reading %d; a row refused with no lost version'
              ' of its own %d; an answer changed by a write that touches no rule %d' % (
                  side, hist.get((side, 'any'), 0), hist.get((side, 'wider'), 0), hist.get((side, 'refused'), 0),
                  hist.get((side, 'changed'), 0)))
        print('          histories with a merge beyond both copies: wider than the exact reading %d; the exact'
              ' reading beyond both as well %d; within what the copies give read exactly %d' % tuple(
                  hist.get((side, b), 0) for b in BEYONDS))
        for b in BEYONDS:
            by = ['%s %d' % (prog, programs[(side, b, prog)]) for prog in ('kp_sync', 'kp_other', 'xc')
                  if (side, b, prog) in programs]
            if by:
                print('          merges %s, by program: %s' % (b, ', '.join(by)))
    for (side, kind), ops in sorted(shown.items()):
        small = shrink(config, ops, side, kind)
        print('   shortest drawn, %s, %s (%d steps, shrunk from %d): %s' % (
            side, kind, len(small), len(ops), ' ; '.join(op_text(o) for o in small)))
    if every:
        print('   every history failing under lost, or beyond both copies and wider than the exact reading, shrunk:')
        seen = set()
        for kind, ops in every:
            small = tuple(shrink(config, ops, 'lost', kind))
            if (kind, small) not in seen:
                seen.add((kind, small))
                print('      %s (%d steps): %s' % (kind, len(small), ' ; '.join(op_text(o) for o in small)))
    print('   time %.0f s' % (time.time() - t0))


def bridge_chunk(args):
    """A chunk of random histories under each behavior, every file the Encoder writes read by
    read.py and held to the model's fold of it after every step: the steps compared, and the
    differences."""
    config, seed, count, lo, hi = args
    rng = random.Random(seed)
    profs, levels = CONFIGS[config]
    acts = alphabet(config)
    compared, differs = {}, {}
    for _ in range(count):
        ops = draw(rng, acts, lo, hi)
        setup(profs, levels)
        for side in JUDGES:
            use(side)
            W = M.World(2, FLAGS)
            enc = S.Encoder(W, tree()[0])
            for op in ops:
                i = op[2] if op[0] == 'merge' else op[1]
                if not W.step(op):
                    continue
                compared[side] = compared.get(side, 0) + 1
                d = bridge(W, enc, i, W.fold(W.reps[i]))
                if d:
                    differs.setdefault(side, []).append((ops, d))
    return compared, differs


def part_bridge(config, count, seed, lo, hi, workers):
    t0 = time.time()
    per = 100
    jobs = [(config, seed * 100003 + c, min(per, count - c * per), lo, hi) for c in range((count + per - 1) // per)]
    compared, differs = {}, {}
    with multiprocessing.Pool(workers) as pool:
        for c, d in pool.imap(bridge_chunk, jobs):
            for k, n in c.items():
                compared[k] = compared.get(k, 0) + n
            for k, xs in d.items():
                differs.setdefault(k, []).extend(xs)
    profs, levels = CONFIGS[config]
    setup(profs, levels)
    print('== %d histories over two copies, %d to %d steps, seed %d, the alphabet of the search %s: after every'
          ' step that applied, the file of the copy it changed written by store.py\'s Encoder and read by'
          ' read.py with the same behavior, held to the model\'s fold of it (what each node gives each'
          ' profile, the rows and switches lost, and whether the store is whole)' % (count, lo, hi, seed, config))
    for side in JUDGES:
        print('   %-6s steps compared %d; steps that differ %d' % (side, compared.get(side, 0), len(differs.get(side, []))))
        for ops, d in differs.get(side, [])[:5]:
            print('      %s: %s' % (' ; '.join(op_text(o) for o in ops), '; '.join(d)))
    print('   time %.0f s' % (time.time() - t0))


# The parts `all` runs, each into a file of its own: (file, part, arguments)
PARTS = [('tags', 'tags', ()), ('hand', 'hand', ()),
         ('search-two', 'search', ('two', 200000, 11, 4, 12)),
         ('search-three', 'search', ('three', 200000, 12, 4, 12)),
         ('search-zero', 'search', ('zero', 200000, 13, 4, 12)),
         ('search-hand', 'search', ('hand', 200000, 14, 4, 12)),
         ('search-long', 'search', ('two', 200000, 15, 8, 16)),
         ('bridge-two', 'bridge', ('two', 2000, 21, 4, 12)),
         ('bridge-three', 'bridge', ('three', 2000, 22, 4, 12)),
         ('bridge-zero', 'bridge', ('zero', 2000, 23, 4, 12)),
         ('bridge-hand', 'bridge', ('hand', 2000, 24, 4, 12))]


def part_all(out, workers, only=None):
    import contextlib
    os.makedirs(out, exist_ok=True)
    for name, part, args in PARTS:
        if only and name not in only:
            continue
        t0 = time.time()
        with open(os.path.join(out, name + '.txt'), 'w', encoding='utf-8', newline='\n') as f:
            with contextlib.redirect_stdout(f):
                if part == 'tags':
                    part_tags()
                elif part == 'hand':
                    part_hand()
                elif part == 'bridge':
                    part_bridge(*args, workers)
                else:
                    part_search(*args, workers)
        print('%s: %.0f s' % (name, time.time() - t0), flush=True)


def main():
    part = sys.argv[1] if len(sys.argv) > 1 else 'hand'
    sys.stdout.reconfigure(newline='\n')
    if part == 'tags':
        part_tags()
    elif part == 'hand':
        part_hand()
    elif part == 'search':
        config, count, seed, lo, hi = sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
        workers = int(sys.argv[7]) if len(sys.argv) > 7 else 1
        part_search(config, count, seed, lo, hi, workers)
    elif part == 'all':     # all OUT [WORKERS] [PART,PART]
        part_all(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1,
                 sys.argv[4].split(',') if len(sys.argv) > 4 else None)
    else:
        raise SystemExit('unknown part ' + part)


if __name__ == '__main__':
    main()
