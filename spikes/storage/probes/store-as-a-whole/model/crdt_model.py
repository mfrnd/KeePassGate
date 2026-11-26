"""Executable model of option `crdt` (see ..\\option-crdt.md).

Run:  uv run --no-project python -I -X utf8 crdt_model.py [quick|full]

What is modeled, and on what basis each transport behavior rests, is in
option-crdt.md sections 1 and 9. Nothing here touches a real database: a "file"
is a dict of entries, a dict of deletion records and a small user tree.

COPY for the family `store-as-a-whole`. Every line this copy adds or changes carries the mark
"ADDED (store-as-a-whole)" on it or on the line above it, and RUN.md of the family lists each
addition. With every new switch at its default the copy is the design's model, and run.ps1 holds
its output to the design's record. The recommended store is the preset STORE below Flags.
"""
import copy
import datetime     # ADDED (renewal answers)
import itertools
import random
import sys
import time

# ----------------------------------------------------------------- the lattice
# What one node answers one profile: a function of what arrives from above.
#   ('const', S): the node names the profile and grants S, whatever is above.
#   ('cap',   S): the node gives what arrives from above, cut to S (a limit).
# identity (a silent node) is ('cap', ALL). The meet is pointwise intersection.
ALL = frozenset('rw')


def const(s=''):
    return ('const', frozenset(s))


def cap(s):
    return ('cap', frozenset(s))


IDENT = cap(ALL)
REFUSE = const('')


# ADDED (copies extended): rules per profile. A document may name each profile with its own words
# and leave a profile unnamed, which then inherits. Such a document is ('pp', C, P): C the letters
# it grants whatever is above, P the letters it passes on from above, each letter a profile, a dot
# and a word ('pf.r'); every other letter it refuses. A document of the two forms above reads, where
# it meets a document of this form, as naming every profile of PROFS alike. PP is set by the
# configurations that write such documents; with it off none exists and nothing here differs.
PP = False
PROFS = ('pf', 'pg')


def lift(x):
    """A set of words as the same words for every profile; letters of a profile stay."""
    return frozenset(p + '.' + v for v in x if len(v) == 1 for p in PROFS) | frozenset(v for v in x if len(v) > 1)


def gen(f):
    if f[0] == 'pp':
        return f
    s = lift(f[1])
    return ('pp', s, frozenset()) if f[0] == 'const' else ('pp', frozenset(), s)


# ADDED (rules per profile): a rule stored as a record for each node and profile, and the node's
# switch "Refuse any profile not named here" as a record of its own (STORE.md 617). With ROWS set,
# a rule act writes a version of the object 'rule:<node>/<profile>' for each row it changes, and
# 'sw:<node>' for the switch; a node's rule is put together from those objects (`rows_node`). The
# value of a row's versions is ('rows', G, off): G the letters the granting versions all hold
# (None where no version grants), `off` whether a version takes the rule off. A version holds
# ('rows', letters, False), an empty rule ('rows', frozenset(), False), or the rule taken off
# ROW_OFF. ROW_VOID, a version that holds nothing of its own, is written only by the removal of a
# single version in the form 'void' (the part `same` of rows.py). The switch's value is ('sws',
# the values its standing versions hold). ROWS names how a removed row is written: 'empty', as
# decided, an empty rule where the gate reads the switch as refusing, and nothing written where the
# row is an empty rule already; 'again' the same, but removing an empty rule under a refusing switch
# takes the rule off; 'off', the control, the rule taken off always. None: a version of a node's
# whole rule, as every configuration before.
ROWS = None
ROW_ACTS = False     # the alphabet holds the acts on a single row and on the switch (store_runs.py)
ROW_OFF = ('rows', None, True)
ROW_VOID = ('rows', None, False)


def row(letters):
    return ('rows', frozenset(letters), False)


# ADDED (S34 forms): what a version that lets through records of what stood above it, the design
# `S34-DESIGN.md` section 8. DEP None: nothing, the store as decided. 'seen' (form B, by what
# arrived): a version that takes a rule off is ('rows', None, True, D) and a version of the switch
# that inherits ('sws', {'inherit'}, D), D a frozenset holding a record for each version met: for a
# row ('s', letters) or ('deny',) for a rule taken off while the node's own switch refused; for a
# switch ('s', the letters of each profile as 'pf.r'), a profile left out having seen nothing.
# 'above' (form A, by identity): for a row ('a', identifiers) or ('deny',); for a switch ('a',
# pairs of a profile and its identifiers). MOVES: a node carries its parent, and the act `nmove`
# moves N between G and the root, the later move winning a merge.
DEP = None
MOVES = False


def is_off(x):
    """ADDED (S34 forms): a version, or versions met, that take the rule off and grant nothing."""
    return x is not None and x[0] == 'rows' and x[1] is None and x[2]


def deps_of(x):
    """ADDED (S34 forms): the records a value holds."""
    if x is None:
        return frozenset()
    if x[0] == 'rows' and len(x) > 3:
        return x[3]
    if x[0] == 'sws' and len(x) > 2:
        return x[2]
    return frozenset()


def meet(f, g):
    if f[0] == 'rows' and g[0] == 'rows':   # ADDED (rules per profile): the versions of a row
        G = f[1] if g[1] is None else g[1] if f[1] is None else f[1] & g[1]
        D = deps_of(f) | deps_of(g)         # ADDED (S34 forms): the records of every version
        return ('rows', G, f[2] or g[2], D) if D else ('rows', G, f[2] or g[2])
    if f[0] == 'sws' and g[0] == 'sws':     # ADDED (rules per profile): the versions of a switch
        D = deps_of(f) | deps_of(g)         # ADDED (S34 forms)
        return ('sws', f[1] | g[1], D) if D else ('sws', f[1] | g[1])
    if f[0] == 'ans' and g[0] == 'ans':     # ADDED (renewal answers): the stricter, by text alone
        return max(f, g, key=lambda x: (answer_key(x[1]), x[1]))
    if f[0] == 'pp' or g[0] == 'pp':    # ADDED (copies extended): letter by letter
        f, g = gen(f), gen(g)
        c = f[1] & g[1]
        return ('pp', c, ((f[1] | f[2]) & (g[1] | g[2])) - c)
    kind = 'const' if f[0] == 'const' and g[0] == 'const' else 'cap'
    return (kind, f[1] & g[1])


def limit(f):
    if f[0] == 'pp':                    # ADDED (copies extended)
        return ('pp', frozenset(), f[1] | f[2])
    return ('cap', f[1])


def app(f, x):
    if f[0] == 'pp' or any(len(v) > 1 for v in x):     # ADDED (copies extended)
        f = gen(f)
        return f[1] | (f[2] & lift(x))
    return f[1] if f[0] == 'const' else f[1] & x


DOCS = {'rw': const('rw'), 'r': const('r'), 'none': const(''), 'inherit': IDENT}


# ADDED (copies extended): a rule act's document: a name of DOCS, or 'pf=rw,pg=' naming each profile
# with its words, a profile left out inheriting
def doc_of(spec):
    if spec in DOCS:
        return DOCS[spec]
    named = {}
    for part in spec.split(','):
        p, w = part.split('=')
        if p not in PROFS or not set(w) <= ALL:
            raise ValueError(spec)
        named[p] = w
    return ('pp', frozenset(p + '.' + v for p, w in named.items() for v in w),
            frozenset(p + '.' + v for p in PROFS if p not in named for v in ALL))


# ADDED (copies extended): words as text. The same words for every profile print as they did
# before there were profiles; else each profile with its words, '-' for none.
def words_text(w):
    if not any(len(v) > 1 for v in w):
        return ''.join(sorted(w))
    per = {p: ''.join(sorted(v[-1] for v in w if v.startswith(p + '.'))) for p in PROFS}
    if len(set(per.values())) == 1:
        return per[PROFS[0]]
    return ' '.join('%s:%s' % (p, per[p] or '-') for p in PROFS)


# ADDED (rules per profile): a rule act's table: for each profile, the letters of its row, or None
# where the act names no row for it (a row taken off, or never made)
def table_of(spec):
    if spec in DOCS:
        f = DOCS[spec]
        return {p: (f[1] if f[0] == 'const' else None) for p in PROFS}
    named = {}
    for part in spec.split(','):
        p, w = part.split('=')
        if p not in PROFS or not set(w) <= ALL:
            raise ValueError(spec)
        named[p] = frozenset(w)
    return {p: named.get(p) for p in PROFS}


# ADDED (rules per profile): what a switch's value means
def switch_refuses(s):
    """No version of the switch standing inherits; a refusal (a version lost), or any standing
    version that refuses, refuses: versions made apart refuse while they differ."""
    return s is not None and (s[0] == 'const' or 'deny' in s[1])


def dep_cut(L, r, sw, p, walk):
    """ADDED (S34 forms): the letters of profile p a node lets through, `L`, cut by what its
    versions that let through record. 'seen': to the letters of each version of the row that takes
    the rule off (none for `deny`) and of each version of the switch that holds a record. 'above':
    nothing unless each names exactly what stands now: for a row's version the versions the walk
    reads above the node and the node's own switch, for a switch's version those above. `walk(p)`
    answers (the identifiers above, the identifiers of the node's own switch)."""
    if DEP is None:
        return L
    for rec in deps_of(r):
        if rec[0] == 'deny':
            return set()
        if DEP == 'seen':
            L = L & set(p + '.' + w for w in rec[1])
        else:
            above, own = walk(p)
            if rec[1] != above | own:
                return set()
    for rec in deps_of(sw):
        if DEP == 'seen':
            L = L & rec[1]
        elif dict(rec[1]).get(p, frozenset()) != walk(p)[0]:
            return set()
    return L


def rows_node(freg, n, depth, walk=None):
    """ADDED (rules per profile): a node's rule put together from its rows and its switch, as a
    document of the form with profiles. A row that grants gives its letters whatever arrives from
    above; a row whose versions grant and take the rule off gives what arrives cut to the granted
    letters where the switch inherits, and nothing where it refuses; a profile with no row standing,
    or with every version taking the rule off, is a profile the node does not name: what arrives
    where the switch inherits, nothing where it refuses; a row that refuses (a version lost) gives
    nothing. ADDED (S34 forms): what passes is cut by `dep_cut`."""
    sw = freg('sw:' + n, depth)
    deny = switch_refuses(sw)
    C, P = set(), set()
    for p in PROFS:
        r = freg('rule:%s/%s' % (n, p), depth)
        L = set(p + '.' + w for w in ALL)
        if r is not None and r[0] == 'const':
            continue
        if r is None or r[1] is None:
            if not deny:
                P |= dep_cut(L, r, sw, p, walk)     # ADDED (S34 forms): dep_cut
        elif r[2]:
            if not deny:
                P |= dep_cut(set(p + '.' + w for w in r[1]), r, sw, p, walk)     # ADDED (S34 forms)
        else:
            C |= set(p + '.' + w for w in r[1])
    return ('pp', frozenset(C), frozenset(P))


def refusal_of(obj):
    """ADDED (rules per profile): what Seal again writes for an object whose version was lost."""
    if ROWS and obj.startswith('sw:'):
        return ('sws', frozenset(['deny']))
    if ROWS and obj.startswith('rule:') and '/' in obj:
        return row('')
    if ANSWERS and obj.startswith('renew:'):    # ADDED (renewal answers): refuse:forever
        return ('ans', FOREVER)
    return REFUSE


def obj_match(obj, sel):
    """ADDED (rules per profile): a selection by a node's rule names every row of that node."""
    return obj == sel or (ROWS is not None and sel.startswith('rule:') and obj.startswith(sel + '/'))


def row_text(f):
    """ADDED (rules per profile): a row's or a switch's value as text, for the parts that print it."""
    if f is None:
        return 'none'
    if f[0] == 'const':
        return 'refuses'
    if f[0] == 'sws':
        return '/'.join(sorted(f[1])) + rec_text(f)     # ADDED (S34 forms): rec_text
    if f[1] is None:
        return ('off' if f[2] else 'void') + rec_text(f)    # ADDED (S34 forms): rec_text
    return (''.join(sorted(f[1])) or 'empty') + ('|off' if f[2] else '') + rec_text(f)


def rec_text(f):
    """ADDED (S34 forms): the records a value holds, as text; nothing where it holds none."""
    out = []
    for rec in sorted(deps_of(f), key=repr):
        if rec[0] == 'deny':
            out.append('deny')
        elif f[0] == 'rows':
            out.append(''.join(sorted(rec[1])) if rec[0] == 's' else '@%d' % len(rec[1]))
        elif rec[0] == 's':
            out.append(' '.join(sorted(rec[1])))
        else:
            out.append(' '.join('%s@%d' % (p, len(i)) for p, i in sorted(rec[1])))
    return '[%s]' % '|'.join(out) if out else ''


# ADDED (owed forms): the forms STORE.md 644 item 24 left owed. Each switch at its default is the
# configuration of rules per profile as it was run. `configure` sets more profiles, the letters of a
# rule, and a group 'G' between the root and N (PARENT: a node's parent; empty, every node under the
# root). KNOWN: a deletion record of a removal of a name, or of a row's version, is told only for a
# name the file knows, as read.py tells it (the names of the list's records and items, and for a
# row the profile of every row object a version present names); without it the model reads every
# deletion record by the element it names. ADD_FORM, under KNOWN: how an addition of a name names
# the removals of that name: 'text' those the reading before the write holds, 'name' those as well
# that a deletion record tells once the name being added is counted as known. TAG_OF, the form
# `split`: a name's keyed tag, so that a deletion record of a row's version is told for the names
# the file knows whose tag is that name's tag; TAG_READ 'first' or 'last' tells it for a single
# such name, the first or the last in ordinal order (read.py's reading as the form was run kept a
# single name, in an order its hash seed decided), and 'each' for every such name.
PARENT = {}
ROOT_ROWS = None    # the profiles the root's rule names at the start; None, every profile
KNOWN = False
ADD_FORM = 'text'
TAG_OF = None
TAG_READ = 'last'
SWITCH_LETTERS = frozenset('ap')     # attachments, protected values: "and those too"
REMOVE_VERBS = frozenset('sd')       # strip, delete: neither switch is asked about for a removal

# ADDED (lost settled): how a deletion record of a version whose content is nowhere is answered,
# and how an act names the lost versions the gate's reading shows for the row it sets. None: as
# every configuration before, a row act naming no lost version and the reading of the branch.
#   'union'   the measured reading (read.py's `deadall`): a deletion record is answered for every
#             object it tells of once a version in force of any object names it in `after`; an act
#             names the lost versions its row shows in `after`, as the product's acts do
#   'object'  the product's reading as built: answered for an object only where a version in force
#             of that object names it in `after`; a version present beside its deletion record is
#             told of by its own object alone; the acts as for 'union'
#   'lost'    the refinement: a version names in `after` the versions of its own object it replaces
#             and what they named in `after`, and in `lost` the lost versions its row shows and what
#             the versions it replaces named in `lost`; a deletion record is answered for every
#             object it tells of where a version in force of an object it tells of names it in
#             `after`, and for an object alone where a version in force of that object names it in
#             `lost`; a version present beside its deletion record is told of by its own object,
#             answered where a version in force of that object names it in `after` or in `lost`
# With JUDGE set and TAG_OF set, a deletion record under a tag of 0 at a node's rows tells of the
# row of every name the file knows whose tag is 0, and of the object `rule:` and the node with no
# slash where the file holds a version of it (STORE.md 609); with TAG_OF None every deletion record
# is told of by the object of the element it names, which is the exact reading.
JUDGE = None

# ADDED (renewal answers): the standing answers to a renewal as the store keeps them, a version of
# the object `renew:` and the client key whose `doc` is the product's text: `refuse:forever`,
# `refuse:until:` and a moment as RFC 3339 in UTC to the second, or `ask`, the lift. A text is in
# form only where composing what was read gives the same bytes; a text out of form refuses forever
# and stops nothing. The answer is the strictest standing version: forever, then the later moment,
# then the earlier, then ask; a lost version refuses forever. Whether an answer refuses is a reading
# against the gate's clock (`World.clock`, a position of CLOCK for each copy), made where a renewal is
# answered (`renewcert`) and where a dialog shows (`see`), and nowhere else. ANSWERS False: every
# renewal answer as before, `forever` and `allowed` as stand-in documents. With it on:
#   - a refusal is given only where the gate's reading asks at its clock, for a client admitted there,
#     and a lift only where the reading refuses at its clock (Allow renewal is offered only there);
#   - an answer names in `after` the standing versions its writer was shown (`see`, or the reading of
#     the write where no `see` came first) that still stand, with what those name in `after`; a lift
#     names in `lost` the lost versions it was shown that are still lost, with what the versions it
#     replaces name in `lost`, and comes to nothing where it names nothing;
#   - a deletion record of an answer is told only for a client key the file knows: the client of a
#     record of kind `client` present, a revocation in a record or a summary item, and the object of a
#     version present or restated, as read.py tells it;
#   - IDEAL_LOST: in the oracle, a version that a version of the same answer names in `lost` counts as
#     seen and replaced, a lift that settled a lost version having been shown it lost. Off, the oracle
#     replaces by `after` alone, as for every other object.
ANSWERS = False
FOREVER, ASK, UNTIL = 'refuse:forever', 'ask', 'refuse:until:'
MOMENTS = ('2026-12-14T23:00:00Z', '2027-03-31T22:00:00Z')     # midnights where the operator is, in UTC
CLOCK = ('2026-11-22T12:00:00Z', '2027-01-15T12:00:00Z', '2027-06-01T12:00:00Z')    # before, between, after
ANSWER_TEXTS = {'forever': FOREVER, 'until1': UNTIL + MOMENTS[0], 'until2': UNTIL + MOMENTS[1],
                'ask': ASK, 'allowed': ASK}
OUT_OF_FORM = UNTIL + '2026-12-15'      # what `unreadable` writes: a day with no time
IDEAL_LOST = True
MOMENT_FORMAT = '%Y-%m-%dT%H:%M:%SZ'

# ADDED (seal again): Seal again as PLAN 3.1 of the pull request that builds it designs it, played
# before anything of it is built in the product. SEAL False: every act as before. With it on:
#   - Create is refused on a stopped store while a store key stands (STORE.md 624), as the product
#     refuses it; where no key stands it starts a store as before;
#   - the seal's write names in the new summary entry's `after` every damaged summary entry it
#     deletes, so that the deletion record it leaves for one tells of a summary entry named;
#   - under `pending` the seal, and an act written with it, waits in the open database for the next
#     save, as every act of the settings dialog's OK does (PLAN 3.1, what the write is believed on);
#   - the act `sealsee` (gate) fixes what the seal's dialog shows, the reading of a stopped store, and
#     `reseal+` (gate, act) writes the seal and the act after it in a single write, the act composed
#     over the sealed store, with one summary entry (PLAN 1, §625 point 6);
#   - SEAL_COMPOSE 'write' composes the seal over the reading at the write, as designed (§625 point
#     5); 'shown', the control, over what the last `sealsee` showed.
SEAL = False
SEAL_COMPOSE = 'write'
SEAL_NAME_DAMAGED = True    # off: a damaged summary entry the seal deletes left unnamed, the control


def grants_nothing(obj, f):
    """ADDED (seal again): whether an object's value grants nothing: an empty rule for a row, the
    switch refusing, refuse:forever for an answer, a rule that grants nothing whatever arrives."""
    if f is None:
        return False
    if obj.startswith('renew:') and ANSWERS:
        return answer_token(f)[0] == 'forever'
    if f[0] == 'rows':
        return f[1] is not None and not f[1]
    if f[0] == 'sws':
        return 'deny' in f[1]
    if f[0] == 'ans':
        return answer_token(f)[0] == 'forever'
    return all(not app(f, x) for x in (frozenset(), frozenset('r'), frozenset('w'), ALL))


# ADDED (started again): the keys of STORE.md 649 and the command of 632 that starts the store again.
# STARTS False: every store key a file holds is read at once, as every configuration before. With it on:
#   - a key is found in an authority's entry, or a retirement that carries it, that passes under that
#     very key, whatever lineage the key is of (a foreign file's key among them); the oracle finds every
#     key of the lineage, as before;
#   - a keep record is an element of kind `kept` under a key, its object the key it names, its `after`
#     the keep records its writer had read; it counts as a record, as an item of a summary entry present
#     (`kept` in the head's data, each with its `after`) and as a deletion record that tells of it, the
#     last naming nothing in `after`, under a key found and naming a key found;
#   - which keys stand is worked out as the product works it out (`marked`): a keep record another names
#     in `after` counts for nothing, under whichever key either is; a key no counting record names, or
#     only records under keys set aside, stands; a key a counting record under a standing key names is
#     set aside; keys left that lead back to themselves stand, a circle, and the two rules run again;
#   - a single standing key is the store key, and only what passes under it counts; several standing keys
#     are a split store, which serves nothing (status `split`); the oracle reads under every standing key;
#   - a version record is an element of kind `fmt` whose data is the format number, restated by a summary
#     entry's `fmt`; a store whose number is above 1 is outdated (status `outdated`), not in the oracle;
#   - `restart` starts the store again: a new key, an authority under it, a keep record under it for every
#     other key found naming in `after` every keep record read under any key found, a version record of
#     format 1 and a summary entry of height 1 with an empty `after`; it deletes nothing. `newer` is a
#     plugin of a later format, holding the store key, raising the format to 2. A pairing and a renewal
#     are written under the key that stands, and a write of the plugin's own looks at nothing under any
#     other key.
STARTS = False


def marked(keys, rels):
    """ADDED (started again): the keys that stand and the keys set aside, as the product's reading marks
    them (`StoreReading.Judgment.Standing`). `rels` is (source, target, identifier, after) for every keep
    record, item and deletion record read; a relation whose target is no key found marks nothing."""
    keys = sorted(keys)
    named = set()
    for r in rels:
        named |= set(r[3])
    names = set((r[0], r[1]) for r in rels if r[1] in keys and r[0] in keys and r[2] not in named)
    mark = dict((k, None) for k in keys)
    while True:
        changed = True
        while changed:
            changed = False
            for k in keys:
                if mark[k] is not None:
                    continue
                srcs = [s for (s, t) in names if t == k]
                if any(mark[s] == 'standing' for s in srcs):
                    mark[k], changed = 'aside', True
                elif all(mark[s] == 'aside' for s in srcs):
                    mark[k], changed = 'standing', True
        left = [k for k in keys if mark[k] is None]
        if not left:
            break
        circled = []
        for k in left:
            seen, todo = set(), [k]
            while todo:
                x = todo.pop()
                for (s, t) in names:
                    if s == x and mark[t] is None:
                        if t == k:
                            circled.append(k)
                            todo = []
                            break
                        if t not in seen:
                            seen.add(t)
                            todo.append(t)
        if not circled:
            break
        for k in circled:
            mark[k] = 'standing'
    return [k for k in keys if mark[k] != 'aside'], [k for k in keys if mark[k] == 'aside']


def answer_read(text):
    """ADDED (renewal answers): a text read strictly: ('forever',), ('until', moment), ('ask',), or None
    for a text out of form."""
    if text == FOREVER:
        return ('forever',)
    if text == ASK:
        return ('ask',)
    if isinstance(text, str) and text.startswith(UNTIL):
        m = text[len(UNTIL):]
        try:
            d = datetime.datetime.strptime(m, MOMENT_FORMAT)
        except ValueError:
            return None
        if d.strftime(MOMENT_FORMAT) == m:
            return ('until', m)
    return None


def answer_key(text):
    """ADDED (renewal answers): a text's place on the chain, the stricter the greater; a text out of
    form above forever, since it refuses forever whatever else stands."""
    a = answer_read(text)
    if a is None:
        return (3, '')
    return {'ask': (0, ''), 'forever': (2, '')}.get(a[0], (1, a[-1]))


def answer_token(f):
    """ADDED (renewal answers): what an answer's value gives: (token, cause). The token is `ask`,
    `until:` and the moment, or `forever`; the cause `none` (no version), `stands`, `lost` (a lost
    version, or a version beside its deletion record) or `unreadable` (a text out of form)."""
    if f is None:
        return 'ask', 'none'
    if f[0] != 'ans':
        return 'forever', 'lost'
    a = answer_read(f[1])
    if a is None:
        return 'forever', 'unreadable'
    return ('until:' + a[1] if a[0] == 'until' else a[0]), 'stands'


def answer_rank(token):
    """ADDED (renewal answers): a token's place on the chain."""
    if token.startswith('until:'):
        return (1, token[6:])
    return {'ask': (0, ''), 'forever': (2, '')}[token]


def answer_refuses(token, clock):
    """ADDED (renewal answers): whether a token refuses at a moment of the clock."""
    if token.startswith('until:'):
        return clock < token[6:]
    return token == 'forever'


def answer_label(token):
    """ADDED (renewal answers): a token as the runs print it: the moments by their place."""
    if token.startswith('until:') and token[6:] in MOMENTS:
        return 'until-%d' % (MOMENTS.index(token[6:]) + 1)
    return token


def refusing_at(token):
    """ADDED (renewal answers): the clock positions at which a token refuses, as text."""
    return ''.join(str(p) for p, c in enumerate(CLOCK) if answer_refuses(token, c)) or '-'


def place_of(obj):
    """ADDED (lost settled): the ten keyed bytes of a version's identifier, as (subject, number): a
    row's subject is its node's rule and its number its name's tag; every other object is its own
    subject with 0."""
    if obj.startswith('rule:') and '/' in obj and TAG_OF is not None:
        node, p = obj[5:].split('/', 1)
        return ('rule:' + node, TAG_OF(p))
    return (obj, '0000')


def configure(profs=None, letters=None, levels=None):
    """ADDED (owed forms): more profiles, the letters of a rule, and three levels."""
    global PROFS, ALL, IDENT, PARENT
    if profs:
        PROFS = tuple(profs)
    if letters:
        ALL = frozenset(letters)
        IDENT = cap(ALL)
        DOCS['inherit'] = IDENT
    if levels == 3:
        PARENT = {'G': 'root', 'N': 'G'}


def caps(words):
    """ADDED (owed forms): what a set of letters lets a profile do, as (profile, verb, scope): each
    verb alone, and with each switch of the same rule for a verb that reads or writes; a removal
    reaches attachments and protected values without a switch; a switch without a verb grants
    nothing. With the letters read and write and no switch, a letter is a verb and nothing more."""
    per = {}
    for x in words:
        p, _, w = x.rpartition('.')
        per.setdefault(p, set()).add(w)
    out = set()
    for p, ws in per.items():
        verbs = ws - SWITCH_LETTERS
        sw = ws & SWITCH_LETTERS
        for v in verbs:
            out.add((p, v, ''))
            for s in (SWITCH_LETTERS if v in REMOVE_VERBS else sw):
                out.add((p, v, s))
    return frozenset(out)


def caps_text(c):
    """ADDED (owed forms): capabilities as text, 'pf r+a' for read with attachments."""
    return ' '.join('%s %s%s' % (p, v, '+' + s if s else '') for p, v, s in sorted(c))


# -------------------------------------------------------------------- elements
class El:
    # ADDED (store-as-a-whole): `carry`, the store key a retirement carries (change 6)
    # ADDED (lost settled): `lost`, the lost versions at its place a version settles for its object
    __slots__ = ('id', 'kind', 'obj', 'after', 'data', 'height', 'key', 'carry', 'lost')

    def __init__(self, id, kind, obj, after=(), data=None, height=0, key='K', carry=None, lost=()):
        self.id, self.kind, self.obj = id, kind, obj
        self.after, self.data, self.height, self.key = frozenset(after), data, height, key
        self.carry = carry
        self.lost = frozenset(lost)     # ADDED (lost settled)


# ADDED (store-as-a-whole): a client is a key; its certificates are `c0`, `c0.1`, `c0.2`.
def ckey(cert):
    return cert.split('.')[0]


# ADDED (store-as-a-whole): what a refusal refuses: a revocation by the client's key (change 5) or
# by the certificate, as the design has it.
def rtuple(data, fl):
    if data[0] == 'revoked' and fl.revkey:
        return ('revoked', ckey(data[1]))
    return (data[0], data[1])


# ADDED (store-as-a-whole): a store key of this database's own lineage, never a foreign file's.
def own(label):
    return label.startswith('K')


class Rep:
    """One copy of the file."""

    def __init__(self):
        self.entries = {}   # uuid -> [element id, modification time, content intact]
        self.dels = {}      # uuid -> deletion time (DeletedObjects)
        self.nodes = {}     # user node -> [marker (KPG.Node value or None), modification time]
        self.disk = None    # ADDED (store-as-a-whole): the file while a rule waits for a save (change 4)

    def clone(self):
        r = Rep()
        r.entries = {u: list(v) for u, v in self.entries.items()}
        r.dels = dict(self.dels)
        r.nodes = {n: list(v) for n, v in self.nodes.items()}
        r.disk = self.disk.clone() if self.disk is not None else None   # ADDED (store-as-a-whole)
        return r


class Flags:
    """The parts of the read rule, so each can be switched off (ablation)."""

    # ADDED (store-as-a-whole): the switches after `dtag`. Each default is the design as written.
    #   witness    the gate writes the witnesses of design 5.3 (T reads them)
    #   mark       the mark, the link, the pin and Adopt of design 8, and the half of Seal again's
    #              seventh change that writes a refusal for a marked node
    #   revkey     a revocation refuses the client's key, not one certificate (change 5)
    #   keyretire  a retirement with no authority after it carries the store key (change 6)
    #   pending    a rule set in a node's dialog waits in memory for the next save (change 4)
    #   repair     after a Synchronize in its own KeePass the gate adds, inside that save, what takes
    #              access away again (change 7)
    #   gates      how many of the files are gates that write: 0 is every file
    #   newacts    the alphabet holds the store's other acts: a renewal under the same key, Delete
    #              of the authority, Create, a save and KeePass ending before a save
    #   idealkeys  the oracle reads under every store key of the lineage (for the ideal only)
    def __init__(self, snapshot=True, T=True, recrule=True, headtag=True, causal_add=True, cover=True,
                 dtag=True, witness=True, mark=True, revkey=False, keyretire=False, pending=False,
                 repair=False, gates=0, newacts=False, idealkeys=False):
        self.snapshot, self.T, self.recrule = snapshot, T, recrule
        self.headtag, self.causal_add, self.cover = headtag, causal_add, cover and snapshot
        self.dtag = dtag
        # ADDED (store-as-a-whole)
        self.witness, self.mark, self.revkey, self.keyretire = witness, mark, revkey, keyretire
        self.pending, self.repair, self.gates, self.newacts = pending, repair, gates, newacts
        self.idealkeys = idealkeys

    # ADDED (store-as-a-whole)
    def but(self, **kw):
        f = Flags.__new__(Flags)
        f.__dict__.update(self.__dict__)
        f.__dict__.update(kw)
        if 'cover' in kw or 'snapshot' in kw:
            f.cover = f.cover and f.snapshot
        return f


FULL = Flags()
# ADDED (store-as-a-whole): the oracle. Under the design's alphabet it reads exactly as FULL does:
# certificates and keys have the same names there, and every element is under the one key `K`.
IDEAL = Flags(revkey=True, idealkeys=True)
# ADDED (store-as-a-whole): the recommended store, EVALUATION.md 4.1 as read on 2026-11-16. Changes
# 1 to 3 are what this model already does: it has no place, no limit of 64, and counts as damage
# only an entry under its own element's identifier.
STORE = Flags(T=False, witness=False, mark=False, revkey=True, keyretire=True, pending=True,
              repair=True, gates=0, newacts=True)
GATE_ACTS = ('pair', 'revoke', 'recreate', 'padd', 'prem', 'rule', 'renew', 'adopt', 'save', 'reseal',
             'renewcert', 'delauth', 'create',   # ADDED (store-as-a-whole): the last three
             'keep',                             # ADDED (copies extended): no alphabet holds it
             'row', 'switch', 'drop',            # ADDED (rules per profile): with ROWS only
             'lift',                             # ADDED (S34 forms): with DEP only
             'settle', 'plain',                  # ADDED (lost settled)
             'see', 'clock',                     # ADDED (renewal answers): with ANSWERS only
             'sealsee', 'reseal+',               # ADDED (seal again): with SEAL only
             'restart', 'newer')                 # ADDED (started again): with STARTS only


class World:
    def __init__(self, nrep, flags=FULL):
        self.EL = {}
        self.n = 0
        self.t = 10
        self.flags = flags
        self.copies = {}            # copy node -> node it was copied from, when that node was ruled
        self.ncopy = 0
        self.clients = ['c0']
        self.reps = [Rep() for _ in range(nrep)]
        self.K = [set() for _ in range(nrep)]       # elements this file has been given
        self.Kever = [set() for _ in range(nrep)]   # the same, not reset by an overwrite
        self.Kdisk = [None] * nrep      # ADDED (store-as-a-whole): K of the file on disk (change 4)
        self.stats = {}                 # ADDED (store-as-a-whole): counts of what the repair did
        self.ncert = 0                  # ADDED (store-as-a-whole): certificates renewed so far
        self.clock = [0] * nrep         # ADDED (renewal answers): each gate's clock, a position of CLOCK
        self.seen = {}                  # ADDED (renewal answers): (gate, client) -> what a dialog showed
        base = self.reps[0]
        base.nodes = {'root': ['root', 1], 'N': [None, 1]}
        if PARENT:      # ADDED (owed forms): the group between the root and N
            base.nodes['G'] = [None, 1]
            if MOVES:   # ADDED (S34 forms): N carries its parent, G at the start
                base.nodes['N'] = [None, 1, 'G']
        els = [El('auth:a0', 'auth', 'a0'),
               self.mk('padd', 'pf', height=1),
               El('rec:c0', 'rec', 'c0', data=('a0', frozenset(['pf'])))]
        if ROWS is None:
            els.append(self.mk('ver', 'rule:root', data=DOCS['r']))
        else:       # ADDED (rules per profile): the root's rule of read, a row for each profile
            # ADDED (owed forms): ROOT_ROWS, the profiles the root names at the start
            els += [self.mk('ver', 'rule:root/' + p, data=row('r')) for p in (ROOT_ROWS or PROFS)]
        for e in els:
            self.EL[e.id] = e
            base.entries[e.id] = [e.id, 1, True]
        self.write_head(base, set())
        ids = set(base.entries)
        for i in range(nrep):
            self.reps[i] = base.clone()
            self.K[i] = set(ids)
            self.Kever[i] = set(ids)

    def clone(self):
        w = World.__new__(World)
        w.EL, w.n, w.t, w.flags = dict(self.EL), self.n, self.t, self.flags
        w.copies, w.clients, w.ncopy = dict(self.copies), list(self.clients), self.ncopy
        w.reps = [r.clone() for r in self.reps]
        w.K = [set(k) for k in self.K]
        w.Kever = [set(k) for k in self.Kever]
        # ADDED (store-as-a-whole)
        w.Kdisk = [set(k) if k is not None else None for k in self.Kdisk]
        w.stats, w.ncert = dict(self.stats), self.ncert
        w.wkey = getattr(self, 'wkey', 'K')
        w.loses = getattr(self, 'loses', frozenset())      # ADDED (lost settled)
        w.clock, w.seen = list(self.clock), dict(self.seen)     # ADDED (renewal answers)
        w.sealshown = dict(getattr(self, 'sealshown', {}))     # ADDED (seal again)
        return w

    # ADDED (store-as-a-whole): the file as another program reads it, and what it holds (change 4)
    def view(self, i):
        r = self.reps[i]
        return r.disk if r.disk is not None else r

    def Kview(self, i):
        return self.Kdisk[i] if self.reps[i].disk is not None else self.K[i]

    def flush(self, i):
        self.reps[i].disk = None
        self.Kdisk[i] = None

    def now(self):
        self.t += 10
        return self.t

    # ADDED (store-as-a-whole): the key an element is written under is the store key the gate holds
    # (`wkey`, set by every fold of a write); under the design's alphabet that is always 'K'
    def mk(self, kind, obj, after=(), data=None, height=0, key=None, lost=()):     # ADDED (lost settled): `lost`
        if key is None:
            key = getattr(self, 'wkey', 'K')
        self.n += 1
        if kind == 'head':
            id = 'H%03d.%d' % (height, self.n)      # keyed tag and height, readable off the uuid
        else:
            id = '%s:%s#%d' % (kind, obj, self.n)   # digest over content and a nonce
        e = El(id, kind, obj, after, data, height, key, lost=lost)     # ADDED (lost settled): `lost`
        self.EL[id] = e
        return e

    # ----------------------------------------------------------- the read rule
    def fold(self, rep, flags=None, exact=False):     # ADDED (lost settled): `exact`, judge_dels
        fl = flags or self.flags
        EL = self.EL
        raw = [EL[eid] for u, (eid, mt, ok) in rep.entries.items() if ok and u == eid]
        # ADDED (store-as-a-whole): the store keys this file holds: every own authority's, and with
        # change 6 the key a retirement carries. Under the design's alphabet that is {'K'} exactly
        # when the design's `keyknown` holds. The oracle reads under every key of the lineage.
        if fl.idealkeys:
            held = set(e.key for e in raw if own(e.key))
        else:
            held = set(e.key for e in raw if e.kind == 'auth' and own(e.key))
            if fl.keyretire:
                held |= set(e.carry for e in raw if e.kind == 'ref' and e.carry and own(e.key))
        keys = None     # ADDED (started again)
        if STARTS:      # ADDED (started again): the keys that stand, STORE.md 649
            keys = self.keys_of(rep, raw, fl)
            held = set(keys['standing'])
        keyknown = bool(held)
        ents = {e.id: e for e in raw if e.key in held}
        named = set(rep.dels) if fl.recrule else set()
        heads = [e for e in ents.values() if e.kind == 'head']
        heads_live = [h for h in heads if h.id not in named]
        refusals, prem = set(), set()
        for e in ents.values():
            if e.kind == 'ref':
                refusals.add(rtuple(e.data, fl))    # ADDED (store-as-a-whole): rtuple
            elif e.kind == 'prem':
                prem.add((e.obj, e.id))
        if fl.snapshot:
            for h in heads:
                refusals |= h.data['ref']
                prem |= h.data['prem']
        if fl.T and keyknown:
            for u in rep.dels:
                if u.startswith('T|'):
                    p = u.split('|')
                    refusals.add((p[1], p[2]))
        vers, supers, lost = {}, set(), {}
        for e in ents.values():
            if e.kind in ('ver', 'pin'):
                if e.id in named:
                    supers.add(e.obj)
                else:
                    vers.setdefault(e.obj, {})[e.id] = e
        if fl.snapshot:
            for h in heads_live:
                for vid in h.data['regs']:
                    e = EL[vid]
                    vers.setdefault(e.obj, {})[vid] = e
        if fl.dtag and keyknown:
            # A decision's identifier carries its kind and a keyed hash of what it is about, so
            # the deletion record of one still tells which object lost a decision. (The model
            # reads that off the identifier's text; a foreign file cannot make such a record.)
            deadall = set()
            for V in vers.values():
                for e in V.values():
                    deadall |= e.after
            # ADDED (owed forms): the names the file knows, as read.py has them
            known = rown = present = None
            if KNOWN or TAG_OF is not None:
                known = set(e.obj for e in ents.values() if e.kind in ('padd', 'prem')) | set(n for (n, r) in prem)
                rown = set(known)
                for o in set(vers) | supers:
                    if o.startswith('rule:') and '/' in o:
                        rown.add(o.split('/', 1)[1])
                present = set()
                for V in vers.values():
                    present |= set(V)
            # ADDED (renewal answers): the client keys the file knows
            known_keys = None
            if ANSWERS:
                known_keys = set(ckey(e.obj) for e in ents.values() if e.kind == 'rec')
                known_keys |= set(s for (k, s) in refusals if k == 'revoked')
                known_keys |= set(o[6:] for o in set(vers) | supers if o.startswith('renew:'))
            for u in rep.dels:
                e = EL.get(u)
                if e is None or e.key not in held:      # ADDED (store-as-a-whole): `not in held`
                    continue
                if e.kind == 'ref':
                    refusals.add(rtuple(e.data, fl))    # a deleted refusal still refuses (ADDED: rtuple)
                elif e.kind == 'prem':
                    if known is None or e.obj in known:     # ADDED (owed forms): a name the file knows
                        prem.add((e.obj, e.id))             # a deleted removal still removes
                elif e.kind in ('ver', 'pin') and (JUDGE is not None or exact):     # ADDED (lost settled)
                    continue    # told below, by judge_dels
                elif e.kind in ('ver', 'pin') and known is not None and e.obj.startswith('rule:') and '/' in e.obj:
                    # ADDED (owed forms): a row's version, told by a name the file knows; in the
                    # form `split` by every name the file knows whose tag is the name's tag
                    node, p = e.obj[5:].split('/', 1)
                    if TAG_OF is None:
                        told = [p] if p in rown else []
                    else:
                        told = sorted(q for q in rown if TAG_OF(q) == TAG_OF(p))
                        if TAG_READ in ('first', 'last'):
                            told = told[:1] if TAG_READ == 'first' else told[-1:]
                    for q in told:
                        o = 'rule:%s/%s' % (node, q)
                        if u not in deadall and (u not in present if TAG_READ == 'each' and TAG_OF is not None
                                                 else u not in vers.get(o, {})):
                            lost.setdefault(o, set()).add(u)
                elif known_keys is not None and e.obj.startswith('renew:') and e.obj[6:] not in known_keys:
                    continue    # ADDED (renewal answers): an answer of a client key the file does not know
                elif e.kind in ('ver', 'pin') and u not in deadall and u not in vers.get(e.obj, {}):
                    lost.setdefault(e.obj, set()).add(u)   # a version nothing replaced or carries
            if JUDGE is not None or exact:      # ADDED (lost settled)
                lost.update(self.judge_dels(rep, ents, vers, supers, held, known, rown, exact,
                                            known_keys))     # ADDED (renewal answers): known_keys
        store = bool(ents) or any(u[0] in 'HT' for u in rep.dels)
        if STARTS:      # ADDED (started again): with no key found nothing is the store's, STORE.md 632
            store = bool(ents) or any(u[0] == 'H' and u in EL and EL[u].key in held for u in rep.dels)
        status = 'ok'
        if not heads_live:
            status = 'incomplete' if store else 'nostore'
        elif fl.headtag:
            # a head was deleted here that is neither in force nor an ancestor of one in force
            known = set(h.id for h in heads_live)
            for h in heads_live:
                known |= h.after
            # ADDED (store-as-a-whole): a head's record is read by its keyed tag, so only under a
            # key held; under the design's alphabet every head is under the one key held
            if any(u[0] == 'H' and u not in known and EL[u].key in held for u in rep.dels):
                status = 'incomplete'
        if status == 'ok' and fl.cover:
            # an entry of the store that does not verify: damage is never passed over
            # ADDED (started again): under STARTS, only damage under the key that stands
            if any(not ok and (not STARTS or EL[eid].key in held)
                   for u, (eid, mt, ok) in rep.entries.items() if u == eid):
                status = 'incomplete'
        if status == 'ok' and fl.cover:
            # a decision that no head here accounts for: its head is missing
            c_ref, c_prem, c_regs, deadall = set(), set(), set(), set()
            for h in heads:
                c_ref |= h.data['ref']
                c_prem |= h.data['prem']
                c_regs |= h.data['regs']
            for V in vers.values():
                for e in V.values():
                    deadall |= e.after
            for e in ents.values():
                # ADDED (renewal answers): with ANSWERS a revocation is held to what it refuses, the
                # client's key, which is what the summary entry restates; read as its certificate, a
                # revocation of a client whose certificate was renewed stopped the store in the model
                # alone (no earlier output changes, since the switch is off in every earlier part)
                if (e.kind == 'ref' and (rtuple(e.data, fl) if ANSWERS else e.data) not in c_ref) or \
                        (e.kind == 'prem' and (e.obj, e.id) not in c_prem) or \
                        (e.kind in ('ver', 'pin') and e.id not in named and e.id not in deadall
                         and e.id not in c_regs):
                    status = 'incomplete'
                # ADDED (started again): a keep record and a version record no summary entry restates
                if STARTS and ((e.kind == 'kept' and e.id not in set(x for h in heads for (x, a) in h.data.get('kept', ())))
                               or (e.kind == 'fmt' and e.data > max([h.data.get('fmt', 0) for h in heads] + [0]))):
                    status = 'incomplete'
        if STARTS and not fl.idealkeys:     # ADDED (started again): split, then outdated, STORE.md 656
            if len(held) > 1:
                status = 'split'
            elif held and max([e.data for e in ents.values() if e.kind == 'fmt']
                              + [h.data.get('fmt', 0) for h in heads] + [0]) > 1:
                status = 'outdated'
        # authorities: a foreign authority that arrived intact counts toward "undecided"
        auths = [e.obj for e in (list(ents.values()) if STARTS else raw)    # ADDED (started again): ents
                 if e.kind == 'auth' and e.id not in named and ('retired', e.obj) not in refusals]
        # ADDED (store-as-a-whole): `in held` for `== 'K'`
        served = auths[0] if len(auths) == 1 and status == 'ok' and EL['auth:' + auths[0]].key in held else None
        # the profile list: an addition counts when it has seen every removal of its name
        names = set()
        for e in ents.values():
            if e.kind == 'padd' and e.id not in named:
                if fl.causal_add:
                    ok = set(r for (n, r) in prem if n == e.obj) <= e.after
                else:
                    # the scheme the critics broke: an addition and its removal share a
                    # generation number, and an addition counts when no removal carries its number
                    ok = e.height not in set(EL[r].height for (n, r) in prem if n == e.obj)
                if ok:
                    names.add(e.obj)
        valid, adm = {}, {}
        for e in ents.values():
            if e.kind == 'rec' and e.id not in named:
                issuer, profs = e.data
                # ADDED (store-as-a-whole): rtuple
                if rtuple(('revoked', e.obj), fl) in refusals or ('retired', issuer) in refusals:
                    continue
                valid[e.obj] = profs & names
                if served == issuer:
                    adm[e.obj] = profs & names
        memo = {}

        def freg(obj, depth):
            V = vers.get(obj)
            if obj in lost:
                return REFUSE
            if not V:
                return REFUSE if obj in supers else None
            dead = set()
            for e in V.values():
                dead |= e.after
                # ADDED (renewal answers): in the oracle, what a lift settled in `lost` it saw
                # ADDED (seal again): with SEAL, of every object: what the seal's version settled in
                # `lost` its dialog showed lost, and a version set after it carries that on
                if ANSWERS and IDEAL_LOST and fl.idealkeys and (obj.startswith('renew:') or SEAL):
                    dead |= e.lost
            f = None
            for i, e in V.items():
                if i in dead:
                    continue
                g = limit(fnode(e.data[1], depth + 1)) if e.kind == 'pin' else e.data
                f = g if f is None else meet(f, g)
            return f

        # ADDED (S34 forms): a node's parent, carried by the node under MOVES
        def parent_of(n):
            nd = rep.nodes.get(n)
            if MOVES and nd is not None and len(nd) > 2:
                return nd[2]
            return PARENT.get(n, 'root')

        # ADDED (S34 forms): the standing versions of an object, and the versions the walk reads
        # above a node for a profile: at each node up the path the profile's row and, where the row
        # does not answer by itself, the node's switch, until a node answers or the root is passed
        def standing_ids(obj):
            V = vers.get(obj) or {}
            dd = set()
            for e in V.values():
                dd |= e.after
            return frozenset(i for i in V if i not in dd)

        def answers(m, p):
            r = freg('rule:%s/%s' % (m, p), 0)
            return r is not None and (r[0] == 'const' or (r[1] is not None and not r[2]))

        def walk_above(n, p):
            ids, m = set(), n
            while m != 'root':
                m = parent_of(m)
                ids |= standing_ids('rule:%s/%s' % (m, p))
                if answers(m, p):
                    break
                ids |= standing_ids('sw:' + m)
                if switch_refuses(freg('sw:' + m, 0)):
                    break
            return frozenset(ids)

        def walk(n):
            return lambda p: (walk_above(n, p), standing_ids('sw:' + n))

        def fnode(n, depth=0):
            if n in memo:
                return memo[n]
            if depth > 8:
                return REFUSE
            f = freg('rule:' + n, depth)
            if ROWS is not None:    # ADDED (rules per profile)
                f = rows_node(freg, n, depth, walk(n) if DEP == 'above' else None)     # ADDED (S34 forms): walk
            m = rep.nodes[n][0] if n in rep.nodes else None
            if not fl.mark:     # ADDED (store-as-a-whole): without the mark a node reads no mark
                m = None
            if f is None:
                f = REFUSE if m == n else IDENT   # marked, and no rule known: refuse
            if m and m != n:
                f = meet(f, limit(fnode(m, depth + 1)))   # a copy: the original's rule caps it
            memo[n] = f
            return f

        # words_raw: what the rules say. words: what a client gets, which needs a store that is
        # whole and an authority that is served.
        words, words_raw, funcs = {}, {}, {}

        def raw_of(n):      # ADDED (owed forms): what arrives at n down the walk from the root
            if n == 'root':
                return frozenset()
            p = parent_of(n)        # ADDED (S34 forms): parent_of for PARENT.get(n, 'root')
            if p not in words_raw:
                words_raw[p] = app(fnode(p), raw_of(p))
                if PP:
                    words_raw[p] = lift(words_raw[p])
            return words_raw[p]

        for n in rep.nodes:
            f = fnode(n)
            funcs[n] = f
            up = app(fnode('root'), frozenset()) if n != 'root' else frozenset()
            if PARENT or MOVES:      # ADDED (owed forms); (S34 forms): MOVES
                up = raw_of(n)
            words_raw[n] = app(f, up)
            if PP:      # ADDED (copies extended): every word as a profile's letter
                words_raw[n] = lift(words_raw[n])
            words[n] = words_raw[n] if status == 'ok' and served else frozenset()
        renew, renew_why = {}, {}
        for c in self.clients:
            f = freg('renew:' + c, 0)
            if ANSWERS:     # ADDED (renewal answers): the chain, read with no clock
                renew[c], renew_why[c] = answer_token(f)
                continue
            renew[c] = 'forever' if f is not None and not app(f, ALL) else 'ask'
        # ADDED (store-as-a-whole): `held`, `auths`, the register values
        regval = {}
        for obj in set(vers) | set(lost) | supers:
            regval[obj] = freg(obj, 0)
        out = dict(status=status, served=served, refusals=refusals, prem=prem, names=names,
                   valid=valid, adm=adm, words=words, words_raw=words_raw, funcs=funcs,
                   renew=renew, vers=vers, lost=lost,
                   ents=ents, heads=heads, heads_live=heads_live,
                   held=held, auths=auths, regval=regval)
        if DEP is not None:     # ADDED (S34 forms): what the acts of a form read
            out.update(parent_of=parent_of, walk_above=walk_above, standing_ids=standing_ids)
        if ANSWERS:     # ADDED (renewal answers)
            out['renew_why'] = renew_why
        if STARTS:      # ADDED (started again)
            out.update(keys)
        return out

    def keys_of(self, rep, raw, fl):
        """ADDED (started again): the keys found, the keep records read, and which keys stand. A key is
        found in an authority, or a retirement that carries it, passing under that very key; the oracle
        finds every key of the lineage. The relations are read under every key found: each keep record,
        each item of a summary entry present, and each deletion record of a keep record under a key found
        naming another key found, which names nothing in `after`."""
        EL = self.EL
        if fl.idealkeys:
            found = set(e.key for e in raw if own(e.key))
        else:
            found = set(e.key for e in raw if e.kind == 'auth')
            if fl.keyretire:
                found |= set(e.carry for e in raw if e.kind == 'ref' and e.carry and e.carry == e.key)
        rels = []
        for e in sorted(raw, key=lambda x: x.id):
            if e.key not in found:
                continue
            if e.kind == 'kept':
                rels.append((e.key, e.obj, e.id, e.after, 'record'))
            elif e.kind == 'head':
                for (x, a) in sorted(e.data.get('kept', ())):
                    rels.append((e.key, EL[x].obj, x, a, 'item'))
        for u in sorted(rep.dels):
            e = EL.get(u)
            if e is not None and e.kind == 'kept' and e.key in found and e.obj in found and e.obj != e.key:
                rels.append((e.key, e.obj, u, frozenset(), 'deletion'))
        standing, aside = marked(found, [r[:4] for r in rels])
        return dict(found=frozenset(found), standing=frozenset(standing), aside=frozenset(aside),
                    keptread=frozenset(r[2] for r in rels), rels=tuple(rels))

    def judge_dels(self, rep, ents, vers, supers, held, known, rown, exact, known_keys=None):     # ADDED (renewal answers): known_keys
        """ADDED (lost settled): the objects each deletion record of a version is lost to, under
        JUDGE, as {object: identifiers}. A deletion record of a version a summary entry in force
        restates is lost to nothing: its text is held. With `exact`, the reading that knows whose
        each version was: a deletion record is told of by the object of the element it names alone,
        and answered where a version in force of that object names it in `after` or in `lost`."""
        EL = self.EL
        out = {}
        after_of, lost_of, deadall, present = {}, {}, set(), set()
        for o, V in vers.items():
            present |= set(V)
            for e in V.values():
                after_of.setdefault(o, set()).update(e.after)
                lost_of.setdefault(o, set()).update(e.lost)
                deadall |= e.after
        objs = set(vers) | supers

        def at_place(obj):
            """Every object the file knows at the place of a version of `obj`."""
            if known_keys is not None and obj.startswith('renew:'):     # ADDED (renewal answers)
                return [obj] if obj[6:] in known_keys else []
            if not obj.startswith('rule:'):
                return [obj]
            if TAG_OF is None:
                if '/' in obj and known is not None and obj.split('/', 1)[1] not in rown:
                    return []
                return [obj]
            subj, num = place_of(obj)
            told = ['%s/%s' % (subj, q) for q in sorted(rown) if TAG_OF(q) == num]
            if num == '0000' and subj in objs:
                told.append(subj)
            return told

        for u in sorted(rep.dels):
            e = EL.get(u)
            if e is None or e.key not in held or e.kind not in ('ver', 'pin'):
                continue
            if u in present:
                continue
            beside = u in ents
            if exact:
                told = [e.obj]
            elif beside and JUDGE in ('object', 'lost'):
                told = [e.obj]      # told of by its own object, with nothing hashed
            else:
                told = at_place(e.obj)
            if exact:
                named = set(after_of.get(e.obj, ())) | lost_of.get(e.obj, set())
            elif JUDGE == 'lost':
                named = set()
                for o in told:
                    named |= after_of.get(o, set())
            for o in told:
                if exact:
                    ok = u in named
                elif JUDGE == 'union':
                    ok = u in deadall
                elif JUDGE == 'object':
                    ok = u in after_of.get(o, ())
                else:
                    ok = u in named or u in lost_of.get(o, ())
                if not ok:
                    out.setdefault(o, set()).add(u)
        return out

    def ideal(self, i, ever=False):
        """The same file, had the transport been a perfect grow-only set."""
        r = Rep()
        r.nodes = self.reps[i].nodes
        for eid in (self.Kever[i] if ever else self.K[i]):
            r.entries[eid] = [eid, 0, True]
        return self.fold(r, IDEAL)      # ADDED (store-as-a-whole): IDEAL for FULL

    # --------------------------------------------------------- the write path
    def write_head(self, rep, new_ids, reseal=False):
        t = self.t
        v = self.fold(rep)
        self.wkey = sorted(v['held'])[0] if v['held'] else 'K'     # ADDED (store-as-a-whole)
        ents = v['ents']
        # prune: what a present element replaces goes, with the deletion record KeePass leaves
        dead = set()
        for obj, V in v['vers'].items():
            for e in V.values():
                dead |= e.after
        for e in ents.values():
            if e.kind == 'padd':
                rids = set(r for (n, r) in v['prem'] if n == e.obj)
                if not rids <= e.after:
                    dead.add(e.id)
            if e.kind == 'rec' and (rtuple(('revoked', e.obj), self.flags) in v['refusals']   # ADDED: rtuple
                                    or ('retired', e.data[0]) in v['refusals']):
                dead.add(e.id)
            if e.kind == 'auth' and ('retired', e.obj) in v['refusals']:
                dead.add(e.id)
        old_heads = [h for h in v['heads']]
        anc = set(u for u in rep.dels if u[0] == 'H')   # every head known to have been deleted here
        if STARTS:      # ADDED (started again): only under the key that stands
            anc = set(u for u in anc if self.EL[u].key in v['held'])
        for h in old_heads:
            anc.add(h.id)
            anc |= h.after
        dead |= anc
        for u, (eid, mt, ok) in list(rep.entries.items()):
            if u == eid and not ok:      # damage goes with the write that seals over it
                if STARTS and self.EL[eid].key not in v['held']:    # ADDED (started again)
                    continue
                del rep.entries[u]
                rep.dels[u] = t
                # ADDED (seal again): a damaged summary entry deleted here is named by the new one
                if SEAL and SEAL_NAME_DAMAGED and self.EL[u].kind == 'head' and own(self.EL[u].key):
                    anc.add(u)
        for d in dead:
            if d in rep.entries and rep.entries[d][0] == d:
                del rep.entries[d]
            rep.dels[d] = t      # also for what is not here: the record keeps it out and dead
        if self.flags.witness:      # ADDED (store-as-a-whole): the witness can be left out
            for (rk, subj) in v['refusals']:
                rep.dels['T|%s|%s' % (rk, subj)] = t
        hs = [h.height for h in old_heads] + [int(u[1:4]) for u in rep.dels if u[0] == 'H'
                                              and (not STARTS or self.EL[u].key in v['held'])]     # ADDED (started again)
        regs = set()
        for obj, V in v['vers'].items():
            dd = set()
            for e in V.values():
                dd |= e.after
            regs |= set(i for i in V if i not in dd)
        h = self.mk('head', 'store', after=anc, height=1 + max(hs + [0]),
                    data={'ref': frozenset(v['refusals']), 'prem': frozenset(v['prem']),
                          'regs': frozenset(regs)})
        # ADDED (copies extended): the copies a Seal again acknowledged are carried by every later
        # summary entry; no item where there is none, so that every other head is as it was
        ack = frozenset(x for o in old_heads for x in o.data.get('ack', ()))
        if ack:
            h.data['ack'] = ack
        if STARTS and len(v['held']) == 1:
            # ADDED (started again): every keep record under the key that stands, from an item, else
            # from its record, else from a deletion record that tells of it with nothing in `after`;
            # and the format, the highest a version record or a summary entry states
            kept = {}
            for how in ('item', 'record', 'deletion'):
                for (s, tk, x, a, w) in v['rels']:
                    if w == how and s in v['held'] and x not in kept:
                        kept[x] = frozenset(a)
            if kept:
                h.data['kept'] = frozenset(kept.items())
            fmt = max([e.data for e in ents.values() if e.kind == 'fmt'] + [o.data.get('fmt', 0) for o in old_heads] + [0])
            if fmt:
                h.data['fmt'] = fmt
        rep.entries[h.id] = [h.id, t, True]
        return h.id

    def act(self, i, op):
        """A decision made through the gate on file i. False when it does not apply."""
        if op[0] == 'clock':    # ADDED (renewal answers): ('clock', gate, position), the gate's clock set
            if not ANSWERS or self.clock[i] == op[2]:
                return False
            self.clock[i] = op[2]
            return True
        rep = self.reps[i]
        v = self.fold(rep)
        self.wkey = sorted(v['held'])[0] if v['held'] else 'K'     # ADDED (store-as-a-whole)
        kind = op[0]
        if kind == 'see':       # ADDED (renewal answers): ('see', gate, client), a dialog that shows the
            # client's answer opens: what it shows is kept for the gate's next answer about the client
            if not ANSWERS or v['status'] != 'ok' or op[2] not in self.clients:
                return False
            self.seen[(i, op[2])] = self.shown(v, i, op[2])
            return True
        if kind == 'settle':    # ADDED (lost settled): ('settle', gate, node, profile), a row that
            # refuses for a lost version set again to the letters its standing versions all grant,
            # an empty rule where none grants: the operator choosing the row on the tab again
            obj = 'rule:%s/%s' % (op[2], op[3])
            cur = v['regval'].get(obj)
            if ROWS is None or cur is None or cur[0] != 'const':
                return False
            V = v['vers'].get(obj, {})
            dd = set()
            for e in V.values():
                dd |= e.after
            grants = [e.data[1] for e in V.values() if e.id not in dd and e.data[0] == 'rows' and e.data[1] is not None]
            op = ('row', op[1], op[2], op[3], ''.join(sorted(frozenset.intersection(*grants) if grants else ())))
            kind = 'row'
        if kind == 'sealsee':   # ADDED (seal again): ('sealsee', gate), the seal's dialog opens and shows
            # the reading of the stopped store; a seal composed under SEAL_COMPOSE 'shown' composes over it
            if not SEAL or v['status'] != 'incomplete' or not v['ents']:
                return False
            self.sealshown = dict(getattr(self, 'sealshown', {}))
            self.sealshown[i] = v
            return True
        if kind == 'reseal+':   # ADDED (seal again): ('reseal+', gate, act), the seal and an act in one write
            return self.seal_with(i, op[2])
        if kind == 'reseal':
            if v['status'] != 'incomplete' or not v['ents']:
                return False
            if SEAL:    # ADDED (seal again): a write that would leave no entry holding the store key is
                # refused, as every write of the product is (StoreWrite.Plan): played, and put back
                snap = (rep.clone(), set(self.K[i]), set(self.Kever[i]),
                        set(self.Kdisk[i]) if self.Kdisk[i] is not None else None)
                was = (self.n, self.t, getattr(self, 'wkey', 'K'), set(self.EL))
                self.seal_write(i, v)
                left = self.fold(rep)['held']
                rep.__dict__.update(snap[0].__dict__)       # put back in place: the copy stays this object
                self.K[i], self.Kever[i], self.Kdisk[i] = snap[1:]
                self.n, self.t, self.wkey = was[:3]         # and the play leaves no trace: no number,
                for x in set(self.EL) - was[3]:             # no moment and no element of it is kept
                    del self.EL[x]
                if not left:
                    self.stats['seal-refused-for-the-store-key'] = self.stats.get('seal-refused-for-the-store-key', 0) + 1
                    return False
            # ADDED (seal again): written into the open database and kept by the next save
            sealwait = SEAL and self.flags.pending and rep.disk is None
            if sealwait:
                rep.disk = rep.clone()
                self.Kdisk[i] = set(self.K[i])
            self.seal_write(i, v)       # ADDED (seal again): the press's write, moved into a method
            if not (SEAL and self.flags.pending):    # ADDED (seal again): under SEAL it waits for a save
                self.flush(i)       # ADDED (store-as-a-whole): a write of the plugin's own is saved
            return True
        return self.act_rest(i, op, rep, v, kind)

    def seal_write(self, i, v):
        """The press's write over the reading `v` of copy i, as it stood in `act` (ADDED (seal again):
        moved here unchanged but for `shown`, so that the guard above can play it first)."""
        rep = self.reps[i]
        if True:
            shown = v
            if SEAL and SEAL_COMPOSE == 'shown':   # ADDED (seal again): the control, over the store shown
                shown = getattr(self, 'sealshown', {}).get(i, v)
            t = self.now()
            for obj, ids in sorted(shown['lost'].items()):     # ADDED (seal again): shown for v
                # a version was deleted here and nothing holds its text: the press writes the
                # refusal down, naming what was lost
                after = set(ids) | set(shown['vers'].get(obj, {}))     # ADDED (seal again): shown for v
                for e in shown['vers'].get(obj, {}).values():
                    after |= e.after
                lst = frozenset()   # ADDED (lost settled): under 'lost' what was lost is named in `lost`
                if JUDGE == 'lost':
                    after = (after - set(ids)) | set(shown['vers'].get(obj, {}))
                    lst = frozenset(set(ids).union(*(e.lost for e in shown['vers'].get(obj, {}).values())))
                e = self.mk('ver', obj, after=after, data=refusal_of(obj), lost=lst)     # ADDED (rules per profile): refusal_of; (lost settled): lost
                rep.entries[e.id] = [e.id, t, True]
                self.K[i].add(e.id)
            for n, (m, mt) in sorted(rep.nodes.items()):
                # ADDED (store-as-a-whole): only with the mark
                if self.flags.mark and m == n and not v['vers'].get('rule:' + n):
                    # the node says it is ruled and no rule is left: the press writes that down,
                    # so that an older rule arriving later meets it and cannot take over
                    e = self.mk('ver', 'rule:' + n, data=REFUSE)
                    rep.entries[e.id] = [e.id, t, True]
                    self.K[i].add(e.id)
            self.K[i].add(self.write_head(rep, set()))
            self.Kever[i] |= self.K[i]

    def act_rest(self, i, op, rep, v, kind):
        """The rest of `act` (ADDED (seal again): split from it unchanged, so that the press's write
        can be a method of its own)."""
        new = []
        # ADDED (store-as-a-whole): Create, when no authority stands: under the key the store holds,
        # or under a new key when it holds none
        if kind == 'create':
            if v['served'] or v['auths']:
                return False
            # ADDED (seal again): refused on a stopped store while a store key stands (STORE.md 624)
            if SEAL and v['status'] == 'incomplete' and v['held']:
                return False
            if STARTS and v['status'] in ('split', 'outdated'):    # ADDED (started again)
                return False
            key = sorted(v['held'])[0] if v['held'] else 'K%d' % (self.n + 1)
            self.n += 1
            a = 'a%d' % self.n
            e = El('auth:' + a, 'auth', a, key=key)
            self.EL[e.id] = e
            new.append(e)
        # ADDED (started again): the store started again, wherever a key is found and Create has nothing
        # to do (a whole store with no authority, or with several, is Create's or the chooser's)
        elif kind == 'restart':
            if not STARTS or not v['found'] or (v['status'] == 'ok' and not v['served']):
                return False
            self.n += 1
            key, a = 'K%d' % self.n, 'a%d' % self.n
            e = El('auth:' + a, 'auth', a, key=key)
            self.EL[e.id] = e
            new.append(e)
            new += [self.mk('kept', k, after=v['keptread'], key=key) for k in sorted(v['found'])]
            new.append(self.mk('fmt', 'format', data=1, key=key))
        # ADDED (started again): a plugin of a later format, holding the store key, raises the format
        elif kind == 'newer':
            if not STARTS or v['status'] != 'ok' or len(v['held']) != 1:
                return False
            new.append(self.mk('fmt', 'format', data=2))
        elif v['status'] != 'ok':
            return False
        # ADDED (store-as-a-whole): a rule waits in memory for the next save (change 4)
        just = False    # ADDED (rules per profile): a rule act that changes no row writes nothing
        # ADDED (rules per profile): the row acts as well; ADDED (S34 forms): lift; ADDED (renewal
        # answers): an answer and a lift, written into the open database and saved by KeePass or by
        # a later write of the plugin's own that saves, as a rule set on a node's dialog is (STORE.md 629)
        if (kind == 'rule' or (ROWS is not None and kind in ('row', 'switch', 'drop', 'lift'))
                or (ANSWERS and kind == 'renew')) \
                and self.flags.pending and rep.disk is None:
            rep.disk = rep.clone()
            self.Kdisk[i] = set(self.K[i])
            just = True
        if kind in ('create', 'restart', 'newer'):     # ADDED (started again): restart, newer
            pass
        elif kind == 'pair':
            # ADDED (store-as-a-whole): `maxclients`, 3 unless the growth probe raises it
            if not v['served'] or len(self.clients) >= getattr(self, 'maxclients', 3):
                return False
            c = 'c%d' % len(self.clients)
            self.clients.append(c)
            e = El('rec:' + c, 'rec', c, data=(v['served'], frozenset(['pf', 'pg'])),
                   key=self.wkey if STARTS else 'K')    # ADDED (started again): the key that stands
            self.EL[e.id] = e
            new.append(e)
        elif kind == 'revoke':
            # ADDED (store-as-a-whole): the client is a key; every certificate of it admitted here
            # is revoked, which under the design's alphabet is the one certificate `c`
            certs = sorted(x for x in v['adm'] if ckey(x) == op[2])
            if not certs:
                return False
            for c in certs:
                new.append(self.mk('ref', c, data=('revoked', c)))
        elif kind == 'recreate':
            if not v['served']:
                return False
            new.append(self.mk('ref', v['served'], data=('retired', v['served'])))
            self.n += 1
            a = 'a%d' % self.n
            # ADDED (store-as-a-whole): the key carried forward, named; under the design it is 'K'
            e = El('auth:' + a, 'auth', a, key=self.EL['auth:' + v['served']].key)
            self.EL[e.id] = e
            new.append(e)
        # ADDED (store-as-a-whole): a renewal, or a pairing again, under a key already admitted here
        elif kind == 'renewcert':
            certs = sorted(x for x in v['adm'] if ckey(x) == op[2])
            if not certs or v['renew'].get(op[2]) == 'forever':
                return False
            # ADDED (renewal answers): refused where the gate's reading refuses at its clock
            if ANSWERS and answer_refuses(v['renew'].get(op[2], ASK), CLOCK[self.clock[i]]):
                return False
            self.ncert += 1
            c = '%s.%d' % (op[2], self.ncert)
            profs = [e.data[1] for e in v['ents'].values() if e.kind == 'rec' and e.obj == certs[-1]][0]
            e = El('rec:' + c, 'rec', c, data=(v['served'], profs),
                   key=self.wkey if STARTS else 'K')    # ADDED (started again): the key that stands
            self.EL[e.id] = e
            new.append(e)
        # ADDED (store-as-a-whole): Delete of the authority with none after it; with change 6 the
        # retirement carries the store key
        elif kind == 'delauth':
            if not v['served']:
                return False
            a = v['served']
            e = self.mk('ref', a, data=('retired', a))
            e.carry = self.EL['auth:' + a].key if self.flags.keyretire else None
            new.append(e)
        elif kind == 'padd':
            n = op[2]
            if n in v['names']:
                return False
            rids = [r for (m, r) in v['prem'] if m == n]
            if KNOWN and ADD_FORM == 'name':    # ADDED (owed forms): the name being added counted as known
                rids += sorted(u for u in rep.dels if u not in rids and self.EL.get(u) is not None
                               and self.EL[u].kind == 'prem' and self.EL[u].obj == n and self.EL[u].key in v['held'])
            gen = 1 + max([self.EL[r].height for r in rids] + [0])
            new.append(self.mk('padd', n, after=rids, height=gen))
        elif kind == 'prem':
            n = op[2]
            if n not in v['names']:
                return False
            gens = [e.height for e in v['ents'].values() if e.kind == 'padd' and e.obj == n]
            new.append(self.mk('prem', n, height=max(gens + [0])))
        # ADDED (copies extended): where several authorities stand and none is served, the operator
        # keeps the first of them: a retirement for each of the others
        elif kind == 'keep':
            if v['served'] or len(v['auths']) < 2:
                return False
            for a in sorted(v['auths'], key=lambda x: (int(x[1:]) if x[1:].isdigit() else 0, x))[1:]:
                new.append(self.mk('ref', a, data=('retired', a)))
        # ADDED (rules per profile): a rule act writes a version for each row it changes; `row` sets
        # one profile's row ('off' takes it off); `switch` sets the node's switch; `drop` removes a
        # single version of a row that stands in several (rows.py, the part `same`)
        elif kind == 'plain':   # ADDED (lost settled): ('plain', gate, node, letters), a version of the
            # object `rule:` and the node with no slash, which is no node's rule (STORE.md 609, 658)
            # and whose identifier lies at the place of the rows of a name whose tag is 0
            obj = 'rule:' + op[2]
            V = v['vers'].get(obj, {})
            after = set(V)
            for e in V.values():
                after |= e.after
            after, lst = self.names_lost(v, obj, list(V.values()), after)
            new.append(self.mk('ver', obj, after=after, data=row(op[3]), lost=lst))
        elif ROWS is not None and kind in ('rule', 'row', 'switch', 'drop', 'lift'):     # ADDED (S34 forms): lift
            new += self.dep_act(i, v, op) if DEP is not None else self.rows_act(v, op)     # ADDED (S34 forms): dep_act
            if not new:
                if just:
                    self.flush(i)
                return False
        elif kind == 'renew' and ANSWERS:     # ADDED (renewal answers)
            new += self.answer_act(i, v, op)
            if not new:
                if just:
                    self.flush(i)
                return False
        elif kind in ('rule', 'renew', 'adopt'):
            if kind == 'rule':
                obj, doc, node = 'rule:' + op[2], doc_of(op[3]), op[2]     # ADDED (copies extended): doc_of
            elif kind == 'adopt':
                if not self.flags.mark:     # ADDED (store-as-a-whole): Adopt goes with the mark
                    return False
                cs = sorted(c for c in self.copies if c in rep.nodes)
                if not cs:
                    return False
                node = cs[op[2] % len(cs)]
                obj, doc = 'rule:' + node, DOCS[op[3]]
            else:
                if op[2] not in self.clients:
                    return False
                obj, doc, node = 'renew:' + op[2], DOCS['none' if op[3] == 'forever' else 'rw'], None
            V = v['vers'].get(obj, {})
            after = set(V)
            for e in V.values():
                after |= e.after
            lst = frozenset()
            if SEAL and kind == 'rule':     # ADDED (seal again): a node's whole rule names what it
                # settles in `lost`, as a row's version does (names_lost), so that a rule set after
                # the seal carries on the losses the seal's version named
                after, lst = self.names_lost(v, obj, list(V.values()), after)
            new.append(self.mk('ver', obj, after=after, data=doc, lost=lst))     # ADDED (seal again): lost
            # ADDED (store-as-a-whole): `self.flags.mark and`
            if self.flags.mark and node and node in rep.nodes and rep.nodes[node][0] != node:
                rep.nodes[node] = [node, self.now()]    # the marker, once in the node's life
        elif kind == 'save':
            for n, (m, mt) in sorted(rep.nodes.items()):
                if not self.flags.mark:     # ADDED (store-as-a-whole): no pin without the mark
                    break
                if m and m != n and not any(e.kind == 'pin' and e.data[1] == m
                                            for e in v['vers'].get('rule:' + n, {}).values()):
                    new.append(self.mk('pin', 'rule:' + n, data=('pinref', m)))
            if not new:
                # ADDED (store-as-a-whole): a save writes what waits in memory (change 4)
                if rep.disk is not None:
                    self.flush(i)
                    return True
                return False
        else:
            raise ValueError(op)
        t = self.now()
        for e in new:
            rep.entries[e.id] = [e.id, t, True]
            self.K[i].add(e.id)
        self.K[i].add(self.write_head(rep, set(e.id for e in new)))
        self.Kever[i] |= self.K[i]
        # ADDED (store-as-a-whole); ADDED (rules per profile): the row acts as well
        if not ((kind == 'rule' or (ROWS is not None and kind in ('row', 'switch', 'drop', 'lift'))     # ADDED (S34 forms): lift
                 or (ANSWERS and kind == 'renew'))      # ADDED (renewal answers)
                and self.flags.pending):
            self.flush(i)
        return True

    def seal_with(self, i, op):
        """ADDED (seal again): Seal again and the act `op` at gate i in a single write, as the settings
        dialog's OK writes them (PLAN 3.1): the seal composed over the stopped store, the act composed
        over the sealed store, and one summary entry over both. Played as the seal and the act each
        written alone, which composes each over what it is composed over in the single write; then
        the file is put back as it was before the seal and the elements those two writes made, every
        summary entry left out, are written in a single write of their own, which deletes damage,
        prunes and writes the deletion records as any write does. An act that does not apply over the
        sealed store is left out, and the seal is written alone."""
        if not SEAL:
            return False
        rep = self.reps[i]
        v = self.fold(rep)
        if v['status'] != 'incomplete' or not v['ents'] or op[1] != i:
            return False
        snap = rep.clone()
        K0, Kever0 = set(self.K[i]), set(self.Kever[i])
        Kdisk0 = set(self.Kdisk[i]) if self.Kdisk[i] is not None else None
        if not self.act(i, ('reseal', i)):
            return False
        if not self.act(i, op):
            return True
        new = set(u for u in rep.entries if u not in snap.entries and self.EL[u].kind != 'head')
        rep.entries = {u: list(x) for u, x in snap.entries.items()}
        rep.dels = dict(snap.dels)
        rep.disk = snap.disk.clone() if snap.disk is not None else None
        self.K[i], self.Kever[i] = set(K0), set(Kever0)
        self.Kdisk[i] = set(Kdisk0) if Kdisk0 is not None else None
        if self.flags.pending and rep.disk is None:
            rep.disk = snap.clone()
            rep.disk.disk = None
            self.Kdisk[i] = set(K0)
        t = self.now()
        for u in sorted(new):
            rep.entries[u] = [u, t, True]
            self.K[i].add(u)
        self.K[i].add(self.write_head(rep, new))
        self.Kever[i] |= self.K[i]
        if not self.flags.pending:
            self.flush(i)
        return True

    def shown(self, v, i, c):
        """ADDED (renewal answers): what a dialog at gate i shows of client c's answer, from the gate's
        reading `v`: the standing versions, the lost versions, whether the answer refuses at the gate's
        clock, and whether a certificate of the client is admitted there."""
        obj = 'renew:' + c
        V = v['vers'].get(obj, {})
        dead = set()
        for e in V.values():
            dead |= e.after
        refusing = answer_refuses(v['renew'].get(c, ASK), CLOCK[self.clock[i]])
        return (frozenset(x for x in V if x not in dead), frozenset(v['lost'].get(obj, ())), refusing,
                any(ckey(x) == c for x in v['adm']))

    def answer_act(self, i, v, op):
        """ADDED (renewal answers): ('renew', gate, client, value), value forever, until1, until2 or ask
        (allowed as well, the stand-in of the runs before). A refusal is given on the renewal dialog,
        raised where the gate's reading asks at its clock for a client admitted there, and a moment only
        where it is still ahead at that clock; a lift is pressed where the page showed the answer
        refusing at the gate's clock, for a client admitted there. Each names what its writer was shown,
        by the last `see` of that gate and client or by the reading of the write."""
        c, value = op[2], op[3]
        if c not in self.clients or value not in ANSWER_TEXTS:
            return []
        obj = 'renew:' + c
        st, lost, refusing, admitted = self.seen.pop((i, c), None) or self.shown(v, i, c)
        text = ANSWER_TEXTS[value]
        lift = text == ASK
        if not admitted or refusing != lift:
            return []
        if text.startswith(UNTIL) and not CLOCK[self.clock[i]] < text[len(UNTIL):]:
            return []
        V = v['vers'].get(obj, {})
        dead = set()
        for e in V.values():
            dead |= e.after
        named = set(x for x in st if x in V and x not in dead)     # shown, and still standing
        after = set(named)
        for x in named:
            after |= self.EL[x].after
        lst = set()
        if lift:
            lst = set(x for x in lost if x in v['lost'].get(obj, ()))
            for x in named:
                lst |= self.EL[x].lost
            lst -= after
            if not after and not lst:
                return []       # it comes to nothing where it names nothing
        return [self.mk('ver', obj, after=after, data=('ans', text), lost=lst)]

    def names_lost(self, v, obj, replaced, after):
        """ADDED (lost settled): what a version of `obj` names beside the versions it replaces,
        whose identifiers and names in `after` are `after`: under 'union' and 'object' the lost
        versions the gate's reading `v` shows for the object as well, in `after`, as the product's
        acts name them; under 'lost' those in `lost`, with what the versions it replaces
        (`replaced`) name in `lost`. (after, lost)."""
        shown = set(v['lost'].get(obj, ()))
        if JUDGE in ('union', 'object'):
            return set(after) | shown, frozenset()
        if JUDGE == 'lost':
            lst = set(shown)
            for e in replaced:
                lst |= e.lost
            return set(after), frozenset(lst)
        return set(after), frozenset()

    # ADDED (rules per profile): the versions a rule act, a row act, a switch act or the removal of a
    # single version writes, from the gate's read `v` of its own file
    def rows_act(self, v, op):
        kind, node = op[0], op[2]

        def standing(obj):
            V = v['vers'].get(obj, {})
            dead, after = set(), set(V)
            for e in V.values():
                dead |= e.after
                after |= e.after
            return [e for i, e in sorted(V.items()) if i not in dead], after

        def refused(obj):
            cur = v['regval'].get(obj)
            return cur is not None and cur[0] == 'const'

        deny = switch_refuses(v['regval'].get('sw:' + node))
        out = []

        def set_row(p, want):
            obj = 'rule:%s/%s' % (node, p)
            st, after = standing(obj)
            vals = [e.data for e in st]
            real = [x for x in vals if x != ROW_VOID]
            shown = [x for x in real if x != ROW_OFF]
            gone = refused(obj)
            if want is None:
                if not shown and not gone:
                    return          # no row to take off
                val = ROW_OFF
                if ROWS != 'off' and deny:
                    # decided: a row removed where the switch refuses is written as an empty rule
                    val = row('')
                    if real == [row('')] and not gone:
                        if ROWS == 'empty':
                            return  # the row is an empty rule already: nothing changes
                        val = ROW_OFF   # 'again': removing the empty rule takes the rule off
            else:
                val = row(want)
                if vals == [val] and not gone:
                    return          # the row is unchanged
            after, lst = self.names_lost(v, obj, v['vers'].get(obj, {}).values(), after)     # ADDED (lost settled)
            out.append(self.mk('ver', obj, after=after, data=val, lost=lst))     # ADDED (lost settled): lost

        if kind == 'rule':
            for p, want in sorted(table_of(op[3]).items()):
                set_row(p, want)
        elif kind == 'row':
            set_row(op[3], None if op[4] == 'off' else frozenset(op[4]))
        elif kind == 'switch':
            obj = 'sw:' + node
            st, after = standing(obj)
            val = ('sws', frozenset([op[3]]))
            if not refused(obj) and ([e.data for e in st] == [val] or (not st and op[3] == 'inherit')):
                return []
            after, lst = self.names_lost(v, obj, v['vers'].get(obj, {}).values(), after)     # ADDED (lost settled)
            out.append(self.mk('ver', obj, after=after, data=val, lost=lst))     # ADDED (lost settled): lost
        elif kind == 'drop':
            # op: ('drop', gate, node, profile, which, form). The standing versions are ordered by
            # their value as the editor orders its rows, then by identifier; `which` is removed.
            obj = 'rule:%s/%s' % (node, op[3])
            st, _ = standing(obj)
            st = sorted(st, key=lambda e: (row_text(e.data), e.id))
            if len(st) < 2:
                return []
            gone = st[op[4] % len(st)]
            keep = [e for e in st if e is not gone]
            base = set([gone.id]) | gone.after
            form = op[5]
            if form == 'again':     # each version left written again, naming the removed version
                for e in keep:
                    after, lst = self.names_lost(v, obj, [gone, e], base | set([e.id]) | e.after)     # ADDED (lost settled)
                    out.append(self.mk('ver', obj, after=after, data=e.data, lost=lst))     # ADDED (lost settled): names_lost
            elif form == 'void':    # a version that names the removed version and holds nothing
                after, lst = self.names_lost(v, obj, [gone], base)     # ADDED (lost settled)
                out.append(self.mk('ver', obj, after=after, data=ROW_VOID, lost=lst))     # ADDED (lost settled)
            elif form == 'limit':   # a version that names the removed version and holds what the
                m = keep[0].data    # versions left allow together
                for e in keep[1:]:
                    m = meet(m, e.data)
                after, lst = self.names_lost(v, obj, [gone], base)     # ADDED (lost settled)
                out.append(self.mk('ver', obj, after=after, data=m, lost=lst))     # ADDED (lost settled)
            else:
                raise ValueError(op)
        return out

    # ADDED (S34 forms): the acts on rules under a form, `S34-DESIGN.md` 2.2, 2.3 and 8. What a
    # version that lets through records, the versions of the node's switch or rows it writes anew,
    # the press that settles (`lift`), and the write above: after an act at a node with nodes below,
    # each node below, the nearer first, that lets a profile through once the act is written, where
    # what arrives (or the versions read above) differs before and after the act and every version
    # it is cut by held everything that arrived before (or named what stood before), has those
    # versions written anew by itself. A written version names the version it renews and everything
    # that version names. `v` is the gate's reading of its own file before the act.
    def dep_act(self, i, v, op):
        rep = self.reps[i]
        kind, node = op[0], op[2]
        out = []

        def cur():
            tmp = rep.clone()
            for e in out:
                tmp.entries[e.id] = [e.id, 0, True]
            return self.fold(tmp)

        def st(R, obj):
            V = R['vers'].get(obj, {})
            dead = set()
            for e in V.values():
                dead |= e.after
            return [e for k, e in sorted(V.items()) if k not in dead]

        def rec(e):
            return next(iter(deps_of(e.data)))

        def prof(x):
            return x.split('.')[0]

        def arr(R, n, p):
            if n == 'root':
                return frozenset()
            return frozenset(x.split('.')[1] for x in R['words_raw'].get(R['parent_of'](n), frozenset())
                             if prof(x) == p)

        def refuses(R, n):
            return switch_refuses(R['regval'].get('sw:' + n))

        def answers(R, n, p):
            r = R['regval'].get('rule:%s/%s' % (n, p))
            return r is not None and (r[0] == 'const' or (r[1] is not None and not r[2]))

        def own(R, n):
            return R['standing_ids']('sw:' + n)

        def off_rec(R, n, p):
            if DEP == 'seen':
                return ('s', arr(R, n, p))
            return ('a', R['walk_above'](n, p) | own(R, n))

        def sw_want(R, n, p):
            if DEP == 'seen':
                return frozenset(p + '.' + w for w in arr(R, n, p))
            return R['walk_above'](n, p)

        def sw_get(r, p):
            if DEP == 'seen':
                return frozenset(x for x in r[1] if prof(x) == p)
            return dict(r[1]).get(p, frozenset())

        def sw_set(r, p, val):
            if DEP == 'seen':
                return ('s', frozenset(x for x in r[1] if prof(x) != p) | val)
            d = dict(r[1])
            d[p] = val
            return ('a', frozenset((q, x) for q, x in d.items() if x))

        def off_val(r):
            return ('rows', None, True, frozenset([r]))

        def sw_val(r):
            return ('sws', frozenset(['inherit']), frozenset([r]))

        def renew(e, data):
            ne = self.mk('ver', e.obj, after=set([e.id]) | e.after, data=data, lost=e.lost)     # ADDED (lost settled): lost
            out.append(ne)
            return ne

        def is_sw_id(x, n):
            return x.startswith('ver:sw:%s#' % n)

        def settle_switch(n, ps):
            """The node's switch versions that inherit and hold, for a profile of `ps`, another
            record than what arrives (or stands above) now are written anew, a version each, with
            those profiles changed. Then, under 'above', every version at the node that took a rule
            off and named the switch as it stood names it as it stands."""
            R = cur()
            if refuses(R, n):
                return
            before = own(R, n)
            for s in st(R, 'sw:' + n):
                if s.data[0] != 'sws' or not deps_of(s.data):
                    continue
                r = rec(s)
                for p in ps:
                    r = sw_set(r, p, sw_want(R, n, p))
                if r != rec(s):
                    renew(s, sw_val(r))
            follow_own(n, before)

        def follow_own(n, before):
            if DEP != 'above':
                return
            R = cur()
            now = own(R, n)
            if now == before:
                return
            for p in PROFS:
                for e in st(R, 'rule:%s/%s' % (n, p)):
                    if not is_off(e.data) or not deps_of(e.data) or rec(e)[0] == 'deny':
                        continue
                    part = frozenset(x for x in rec(e)[1] if is_sw_id(x, n))
                    if part == before:
                        renew(e, off_val(('a', (rec(e)[1] - part) | now)))

        if kind in ('rule', 'row'):
            targets = sorted(table_of(op[3]).items()) if kind == 'rule' else \
                [(op[3], None if op[4] == 'off' else frozenset(op[4]))]
            for p, want in targets:
                obj = 'rule:%s/%s' % (node, p)
                stl = st(v, obj)
                after = set(e.id for e in stl)
                for e in stl:
                    after |= e.after
                vals = [e.data for e in stl]
                real = [x for x in vals if x != ROW_VOID]
                shown = [x for x in real if not is_off(x)]
                gone = v['regval'].get(obj) is not None and v['regval'][obj][0] == 'const'
                if want is None:
                    if not shown and not gone:
                        continue        # no row to take off
                    if not refuses(v, node):
                        # the rule taken off records what arrives; the switch's versions that hold
                        # another record for the profile are written anew first
                        settle_switch(node, [p])
                        val = off_val(off_rec(cur(), node, p))
                    elif ROWS == 'off':
                        val = off_val(('deny',))
                    else:
                        val = row('')
                        if real == [row('')] and not gone:
                            if ROWS == 'empty':
                                continue    # the row is an empty rule already: nothing changes
                            val = off_val(('deny',))    # 'again': removing the empty rule
                else:
                    val = row(want)
                    if vals == [val] and not gone:
                        continue        # the row is unchanged
                after, lst = self.names_lost(v, obj, stl, after)     # ADDED (lost settled)
                out.append(self.mk('ver', obj, after=after, data=val, lost=lst))     # ADDED (lost settled): lost
        elif kind == 'switch':
            obj = 'sw:' + node
            stl = st(v, obj)
            after = set(e.id for e in stl)
            for e in stl:
                after |= e.after
            gone = v['regval'].get(obj) is not None and v['regval'][obj][0] == 'const'
            after, lst = self.names_lost(v, obj, stl, after)     # ADDED (lost settled)
            if op[3] == 'deny':
                val = ('sws', frozenset(['deny']))
                if not gone and [e.data for e in stl] == [val]:
                    return []
                out.append(self.mk('ver', obj, after=after, data=val, lost=lst))     # ADDED (lost settled): lost
            else:
                if not gone and (not stl or (len(stl) == 1 and stl[0].data[1] == frozenset(['inherit']))):
                    return []
                if DEP == 'seen':
                    r = ('s', frozenset(p + '.' + w for p in PROFS for w in arr(v, node, p)))
                else:
                    r = ('a', frozenset((p, v['walk_above'](node, p)) for p in PROFS if v['walk_above'](node, p)))
                out.append(self.mk('ver', obj, after=after, data=sw_val(r), lost=lst))     # ADDED (lost settled): lost
                # every version at the node that takes a rule off is written anew with what arrives
                # for its profile, `deny` among them
                R = cur()
                for p in PROFS:
                    want = off_rec(R, node, p)
                    for e in st(R, 'rule:%s/%s' % (node, p)):
                        if is_off(e.data) and deps_of(e.data) and rec(e) != want:
                            renew(e, off_val(want))
        elif kind == 'drop':
            out += self.rows_act(v, op)
        elif kind == 'lift':
            p = op[3]
            if refuses(v, node):
                return []
            settle_switch(node, [p])
            R = cur()
            want = off_rec(R, node, p)
            for e in st(R, 'rule:%s/%s' % (node, p)):
                if is_off(e.data) and deps_of(e.data) and rec(e) != want:
                    renew(e, off_val(want))
        if not out:
            return out
        # the write above
        R = cur()
        below = []
        for m in R['words_raw']:
            if m == node:
                continue
            d, a = 0, m
            while a != 'root' and a != node:
                a = R['parent_of'](a)
                d += 1
            if a == node:
                below.append((d, m))
        for d, m in sorted(below):
            R = cur()
            if refuses(R, m):
                continue
            sws = [s for s in st(R, 'sw:' + m) if s.data[0] == 'sws' and deps_of(s.data)]
            todo = {}
            for p in PROFS:
                if answers(R, m, p):
                    continue
                offs = [e for e in st(R, 'rule:%s/%s' % (m, p)) if is_off(e.data) and deps_of(e.data)]
                if not offs and not sws:
                    continue
                if DEP == 'seen':
                    old, now = arr(v, m, p), arr(R, m, p)
                    oldl = frozenset(p + '.' + w for w in old)
                    whole = all(rec(e)[0] == 's' and rec(e)[1] >= old for e in offs) and \
                        all(sw_get(rec(s), p) >= oldl for s in sws)
                else:
                    old, now = v['walk_above'](m, p), R['walk_above'](m, p)
                    whole = all(rec(e)[0] == 'a' and rec(e)[1] == old | own(v, m) for e in offs) and \
                        all(sw_get(rec(s), p) == old for s in sws)
                if old != now and whole:
                    todo[p] = now
            if not todo:
                continue
            before = own(R, m)
            for s in sws:
                r = rec(s)
                for p, now in sorted(todo.items()):
                    r = sw_set(r, p, frozenset(p + '.' + w for w in now) if DEP == 'seen' else now)
                if r != rec(s):
                    renew(s, sw_val(r))
            R2 = cur()
            after_own = own(R2, m)
            for p in PROFS:
                for e in st(R2, 'rule:%s/%s' % (m, p)):
                    if not is_off(e.data) or not deps_of(e.data) or rec(e)[0] == 'deny':
                        continue
                    if p in todo:
                        want = ('s', todo[p]) if DEP == 'seen' else ('a', todo[p] | after_own)
                    elif DEP == 'above' and after_own != before:
                        part = frozenset(x for x in rec(e)[1] if is_sw_id(x, m))
                        if part != before:
                            continue
                        want = ('a', (rec(e)[1] - part) | after_own)
                    else:
                        continue
                    if rec(e) != want:
                        renew(e, off_val(want))
        return out

    # ------------------------------------------------- other programs, people
    def pick(self, rep, sel):
        """A store entry by description, so that a history stays replayable when shortened."""
        EL = self.EL
        # ADDED (store-as-a-whole): own(...) for == 'K'
        present = [EL[eid] for u, (eid, mt, ok) in rep.entries.items() if u == eid and own(EL[eid].key)]
        k = sel[0]
        if k == 'head':
            hs = sorted((e for e in present if e.kind == 'head'), key=lambda e: (e.height, e.id))
            if not hs:
                return None
            return (hs[-1] if sel[1] == 'new' else hs[0]).id
        if k == 'ref':
            c = [e for e in present if e.kind == 'ref' and e.data[0] == sel[1]]
        elif k == 'ver':
            # ADDED (rules per profile): obj_match for ==
            V = [e for e in present if e.kind in ('ver', 'pin') and obj_match(e.obj, sel[1])]
            dead = set()
            for e in V:
                dead |= e.after
            c = [e for e in V if (e.id not in dead) == (sel[2] == 'new')]
        else:
            c = [e for e in present if e.kind == k]
            if k == 'auth' and len(sel) > 1:    # ADDED (started again): an authority by its name
                c = [e for e in c if e.obj == sel[1]]
        c.sort(key=lambda e: e.id)
        return c[-1].id if c else None

    # ADDED (store-as-a-whole): Kever as another program reads the file (change 4)
    def Keverview(self, i):
        if self.reps[i].disk is None:
            return self.Kever[i]
        return self.Kever[i] - (self.K[i] - self.Kdisk[i])

    def other(self, op):
        k = op[0]
        reps = self.reps
        # ADDED (store-as-a-whole): while a rule waits in memory, only the gate's own KeePass
        # changes that copy: a Synchronize or an import there, a file merged in there, or KeePass
        # ending before a save. Another program's act on that file is outside this model.
        tgt = op[2] if k == 'merge' else op[1]
        if reps[tgt].disk is not None and k not in ('quit', 'import', 'plant') and not (
                k == 'merge' and op[1] in ('kp_sync', 'kp_other')):
            return False
        if k == 'lose':     # ADDED (lost settled): ('lose', i, object), a deletion record of a version of
            # the object whose content is nowhere: what a version made in a copy, deleted there with
            # its summary entry by a script that leaves no record of the summary entry, leaves in a
            # copy it never reached once a Synchronize carries its deletion record
            rep = reps[op[1]]
            held = self.fold(rep)['held']
            if not held:
                return False
            data = ('sws', frozenset(['inherit'])) if op[2].startswith('sw:') else row('r')
            if op[2].startswith('renew:'):      # ADDED (renewal answers): a refusal forever
                if not ANSWERS:
                    return False
                data = ('ans', FOREVER)
            e = self.mk('ver', op[2], data=data, key=sorted(held)[0])
            self.loses = getattr(self, 'loses', frozenset()) | {e.id}     # the identifiers `lose` made
            rep.dels[e.id] = self.now()
            return True
        if k == 'nmove':    # ADDED (S34 forms): ('nmove', i, node, parent), KeePass moves a node
            nd = reps[op[1]].nodes.get(op[2])
            if not MOVES or nd is None or len(nd) < 3 or nd[2] == op[3]:
                return False
            reps[op[1]].nodes[op[2]] = [nd[0], self.now(), op[3]]
            return True
        if k == 'quit':     # ADDED (store-as-a-whole): KeePass ends before the next save
            i = op[1]
            if reps[i].disk is None:
                return False
            removed = self.K[i] - self.Kdisk[i]
            reps[i] = reps[i].disk
            self.K[i] = self.Kdisk[i]
            self.Kever[i] -= removed
            self.Kdisk[i] = None
            return True
        if k == 'del':      # a person deletes one store entry: 1 with a deletion record, 0 without
            rep = reps[op[1]]             # (pykeepass), 2 with a record dated before the entry
            u = self.pick(rep, op[2])
            if not u:
                return False
            del rep.entries[u]
            if op[3]:
                rep.dels[u] = self.now() if op[3] == 1 else 0
            if op[2][0] == 'ver':   # ADDED (lost settled): a version deleted by hand is a loss
                self.loses = getattr(self, 'loses', frozenset()) | {u}
            return True
        if k == 'clear':    # Database Maintenance: remove deleted objects information
            if not reps[op[1]].dels:
                return False
            reps[op[1]].dels = {}
            return True
        if k == 'over':     # a whole file written over another
            # ADDED (store-as-a-whole): the source as it is on disk
            reps[op[1]] = self.view(op[2]).clone()
            self.K[op[1]] = set(self.Kview(op[2]))
            self.Kever[op[1]] |= self.Keverview(op[2])
            return True
        if k == 'edit':     # a person or a program changes a store entry's content
            rep = reps[op[1]]
            u = self.pick(rep, op[2])
            if not u or not rep.entries[u][2]:
                return False
            rep.entries[u] = [u, self.now(), False]
            return True
        if k == 'copy':     # a copy of node N in any program: new identifier, same strings
            rep = reps[op[1]]
            if 'N' not in rep.nodes or self.ncopy >= 2:
                return False
            self.ncopy += 1
            m = 'M%d' % self.ncopy
            # ADDED (store-as-a-whole): without the mark, a copy of a node ruled in the store is
            # counted, so that a copy wider than its original (accepted, decision 16) shows
            # ADDED (rules per profile): any row of N
            if rep.nodes['N'][0] or (not self.flags.mark and any(
                    obj_match(o, 'rule:N') and V for o, V in self.fold(rep)['vers'].items())):
                self.copies[m] = 'N'    # the promise is about a copy of a ruled node
            rep.nodes[m] = [rep.nodes['N'][0], self.now()]
            return True
        if k == 'strip':    # the carried reference removed by hand, or by Delete Duplicate Entries
            rep = reps[op[1]]
            cs = sorted(c for c in self.copies if c in rep.nodes and rep.nodes[c][0])
            if not cs:
                return False
            rep.nodes[cs[0]] = [None, self.now()]
            return True
        if k == 'import':   # KeePass Create new IDs: every arriving entry under a new identifier
            dst, src = reps[op[1]], self.view(op[2])    # ADDED (store-as-a-whole): view
            for u, ent in list(src.entries.items()):
                self.n += 1
                dst.entries['X%d' % self.n] = list(ent)
            self.flush(op[1])       # ADDED (store-as-a-whole): KeePass saves
            return True
        if k == 'plant':    # a foreign file merged in, built by somebody who read no entry of ours
            rep = reps[op[1]]
            t = self.now()
            what = op[2]
            if what == 'rule' and ROWS is not None:     # ADDED (rules per profile): a row
                e = self.mk('ver', 'rule:N/pf', data=row('rw'), key='F')
            elif what == 'rule':
                e = self.mk('ver', 'rule:N', data=DOCS['rw'], key='F')
            elif what == 'retire':
                e = self.mk('ref', 'a0', data=('retired', 'a0'), key='F')
            elif what == 'record':
                e = El('rec:c9', 'rec', 'c9', data=('a0', frozenset(['pf'])), key='F')
                self.EL[e.id] = e
                if 'c9' not in self.clients:
                    self.clients.append('c9')
            elif what == 'profile':
                e = self.mk('padd', 'pg', key='F')
            else:
                self.n += 1
                e = El('auth:f%d' % self.n, 'auth', 'f%d' % self.n, key='F')
                self.EL[e.id] = e
            rep.entries[e.id] = [e.id, t, True]
            self.flush(op[1])       # ADDED (store-as-a-whole): KeePass saves
            return True
        # ADDED (renewal answers): ('unreadable', i, client), somebody who holds the store key writes an
        # answer about the client whose text is out of form, naming nothing, and a summary entry
        if k == 'unreadable':
            rep = reps[op[1]]
            v = self.fold(rep)
            if not ANSWERS or not v['held'] or op[2] not in self.clients:
                return False
            e = self.mk('ver', 'renew:' + op[2], data=('ans', OUT_OF_FORM), key=sorted(v['held'])[0])
            rep.entries[e.id] = [e.id, self.now(), True]
            self.write_head(rep, {e.id})
            return True
        # ADDED (store-as-a-whole): a file made by somebody who holds the store key, merged in: a
        # version of N's rule granting read and write that names every version present
        if k == 'plantk':
            rep = reps[op[1]]
            v = self.fold(rep)
            obj, data = ('rule:N/pf', row('rw')) if ROWS is not None else ('rule:N', DOCS['rw'])     # ADDED (rules per profile)
            V = v['vers'].get(obj, {})
            after = set(V)
            for e in V.values():
                after |= e.after
            e = self.mk('ver', obj, after=after, data=data, key=sorted(v['held'])[0] if v['held'] else 'K')
            rep.entries[e.id] = [e.id, self.now(), True]
            self.write_head(rep, {e.id})    # whoever holds the key can write the head as well
            return True
        # ADDED (store-as-a-whole): KeePass's Delete Duplicate Entries after an import with new
        # identifiers: of an element and its copy, the original goes, with a record (facts F5)
        if k == 'dedupe':
            rep = reps[op[1]]
            copied = set(ent[0] for u, ent in rep.entries.items() if u != ent[0])
            gone = [u for u, ent in rep.entries.items() if u == ent[0] and u in copied]
            if not gone:
                return False
            t = self.now()
            for u in gone:
                del rep.entries[u]
                rep.dels[u] = t
            return True
        if k == 'merge':
            return self.merge_step(op[1], op[2], op[3])     # ADDED (store-as-a-whole): merge_step
        raise ValueError(op)

    # ADDED (store-as-a-whole): a merge as a step of a history. KeePass saves after its own merges,
    # and after a Synchronize in the gate's KeePass the repair of change 7 runs inside that save.
    def merge_step(self, prog, d, s):
        before = self.fold(self.reps[d]) if self.flags.repair and prog == 'kp_sync' else None
        if not self.merge(prog, d, s):
            return False
        if prog in ('kp_sync', 'kp_other'):
            self.flush(d)
        if before is not None:
            self.repair(d, before)
        return True

    # ADDED (store-as-a-whole): change 7. What was in force before the Synchronize and is not after
    # it is written again: a refusal, a removal of a name that some removal held off the list, and
    # for a register that held a value before and gives more after, a version restating the earlier
    # value that names nothing, so that it meets whatever stands. Nothing that grants. These are not
    # decisions, so the oracle does not hold them.
    def repair(self, d, before):
        rep = self.reps[d]
        after = self.fold(rep)
        self.wkey = sorted(after['held'])[0] if after['held'] else 'K'
        new = []
        for t in sorted(before['refusals'] - after['refusals']):
            new.append(self.mk('ref', t[1], data=t))
        for n in sorted(after['names'] - before['names']):
            if any(m == n for (m, r) in before['prem']):
                gens = [e.height for e in after['ents'].values() if e.kind == 'padd' and e.obj == n]
                new.append(self.mk('prem', n, height=max(gens + [0])))
        for obj, fb in sorted(before['regval'].items()):
            if fb is None:
                continue
            fa = after['regval'].get(obj) or IDENT
            if any(not app(fa, x) <= app(fb, x) for x in INPUTS):
                # the earlier value met with what stands now, so that the version takes away and
                # never grants, even where nothing stands after the merge (a first form wrote the
                # earlier value itself, which then granted alone; RUN.md has the history)
                new.append(self.mk('ver', obj, data=meet(fb, fa)))
        if not new:
            return
        I = self.ideal(d)
        wide0 = narrower(after, I, rep.nodes)
        thin0 = narrower_than(after, I, rep.nodes)
        t = self.now()
        for e in new:
            rep.entries[e.id] = [e.id, t, True]
        self.write_head(rep, set(e.id for e in new))
        A = self.fold(rep)
        wide1 = narrower(A, I, rep.nodes)
        thin1 = narrower_than(A, I, rep.nodes)
        self.stats['repair-writes'] = self.stats.get('repair-writes', 0) + 1
        if wide0 and not wide1:
            self.stats['repair-closed'] = self.stats.get('repair-closed', 0) + 1
        elif wide0:
            self.stats['repair-left-wider'] = self.stats.get('repair-left-wider', 0) + 1
        if thin1 and not thin0:
            self.stats['repair-narrower'] = self.stats.get('repair-narrower', 0) + 1

    def merge(self, prog, d, s):
        if d == s:
            return False
        # ADDED (store-as-a-whole): the source as it is on disk
        dst, src = self.reps[d], self.view(s)
        self.now()
        if prog in ('kp_sync', 'k2a'):
            # read 2.62 PwDatabase.cs, measured X1-X4; Keepass2Android measured 2026-11-15
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            for u, t in src.dels.items():
                dst.dels[u] = max(t, dst.dels.get(u, t))
            self.apply_dels(dst, dst.dels, drop=True)
        elif prog == 'kp_other':
            # Overwrite if newer, Overwrite existing, Keep existing: no arriving record, own applied
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            self.apply_dels(dst, dst.dels, drop=True)
        elif prog == 'xc':
            # measured: never applies or copies a deletion; the target keeps its own records
            for u, ent in src.entries.items():
                if u not in dst.entries or ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
        elif prog == 'dx':
            # read 4.5.8 DatabaseKDBXMerger.kt: arriving records applied and not kept
            for u, ent in src.entries.items():
                if u not in dst.entries:
                    if not (u in dst.dels and dst.dels[u] > ent[1]):
                        dst.entries[u] = list(ent)
                elif ent[1] > dst.entries[u][1]:
                    dst.entries[u] = list(ent)
            self.apply_dels(dst, src.dels, drop=False)
        else:
            raise ValueError(prog)
        for n, nd in src.nodes.items():
            if n not in dst.nodes or nd[1] > dst.nodes[n][1]:
                dst.nodes[n] = list(nd)
        self.K[d] |= self.Kview(s)          # ADDED (store-as-a-whole): Kview
        self.Kever[d] |= self.Keverview(s)  # ADDED (store-as-a-whole): Keverview
        return True

    @staticmethod
    def apply_dels(rep, dels, drop):
        for u, t in list(dels.items()):
            if u in rep.entries:
                if rep.entries[u][1] <= t:
                    del rep.entries[u]
                elif drop:
                    del rep.dels[u]     # the object is present and newer: the record goes

    def step(self, op):
        # ADDED (store-as-a-whole): GATE_ACTS, and only the first `gates` files write when it is set
        if op[0] in GATE_ACTS:
            if self.flags.gates and op[1] >= self.flags.gates:
                return False
            return self.act(op[1], op)
        return self.other(op)


# ADDED (store-as-a-whole): the inputs a node's function is tried on
INPUTS = (frozenset(), frozenset('r'), frozenset('w'), ALL)


# ------------------------------------------------------------------ properties
def narrower(A, I, nodes, renewals=True, against='its ideal'):     # ADDED (renewal answers): `renewals`, `against`
    """None when A grants nothing I does not; else what is wider."""
    for c, profs in A['adm'].items():
        if c not in I['valid']:
            return 'client %s admitted' % c
        if not profs <= I['valid'][c]:
            return 'client %s holds %s' % (c, sorted(profs - I['valid'][c]))
    for n in nodes:
        if n in A['words'] and not A['words'][n] <= I['words_raw'].get(n, frozenset()):
            return 'node %s grants %s' % (n, words_text(A['words'][n]))      # ADDED (copies extended): words_text
    if ANSWERS:     # ADDED (renewal answers)
        return narrower_renewals(A, I, against) if renewals else None
    for c, a in A['renew'].items():
        if a == 'ask' and I['renew'].get(c) == 'forever' and A['status'] == 'ok' and A['served']:
            return 'renewal of %s asked' % c
    return None


def narrower_renewals(A, I, against='its ideal'):
    """ADDED (renewal answers): None when no answer of A is below its answer in I on the chain, which,
    the chain being total and refusing being monotone in it, is: wherever I refuses at a position of
    the clock, A refuses as well; else the first answer below. Only for a store that serves.
    `against` names what I is, in the answer."""
    if A['status'] != 'ok' or not A['served']:
        return None
    for c, a in sorted(A['renew'].items()):
        b = I['renew'].get(c, ASK)
        if answer_rank(a) < answer_rank(b):
            return 'renewal of %s %s where %s is %s (refusing at %s against %s)' % (
                c, answer_label(a), against, answer_label(b), refusing_at(a), refusing_at(b))
    return None


def seal_with_differs(W, X, i):
    """ADDED (seal again): None when copy i of W, the seal and an act in a single write, holds what
    copy i of X holds, the seal and the act written apart, but for the summary entries: the same
    records present, the same deletion records of records, and the same reading; else what differs."""
    def body(w):
        rep = w.reps[i]
        ents = frozenset(u for u, (eid, mt, ok) in rep.entries.items() if w.EL[eid].kind != 'head')
        dels = frozenset(u for u in rep.dels if u not in w.EL or w.EL[u].kind != 'head')
        return ents, dels
    va, vb = W.fold(W.reps[i]), X.fold(X.reps[i])
    if summary(va) != summary(vb) or va['regval'] != vb['regval'] or va['lost'] != vb['lost']:
        return 'reading: single write %r, apart %r' % (summary(va), summary(vb))
    a, b = body(W), body(X)
    if a[0] != b[0]:
        # every record one holds and the other does not, and whether each is out of force there
        named = W.reps[i].dels
        return 'info:records present: single write %s, apart %s' % (
            ', '.join('%s%s' % (u, ' beside its deletion record' if u in named else '') for u in sorted(a[0] - b[0])) or 'none',
            ', '.join(sorted(b[0] - a[0])) or 'none')
    if a[1] != b[1]:
        return 'info:deletion records: single write %s, apart %s' % (sorted(a[1] - b[1]), sorted(b[1] - a[1]))
    return None


def summary(v):
    return (v['status'], v['served'], tuple(sorted((c, tuple(sorted(p))) for c, p in v['adm'].items())),
            tuple(sorted((n, ''.join(sorted(w))) for n, w in v['words'].items())),
            tuple(sorted(v['renew'].items())), tuple(sorted(v['names'])))


# ADDED (store-as-a-whole): the other direction of `narrower`: None when A grants everything the
# oracle I grants; else the first thing A does not grant. Only for an oracle that serves.
def narrower_than(A, I, nodes):
    if I['status'] != 'ok' or not I['served']:
        return None
    if A['status'] != 'ok':
        return 'refuses: store %s' % A['status']
    if not A['served']:
        return 'refuses: no authority served'
    for c, profs in I['adm'].items():
        if c not in A['adm']:
            return 'client %s refused' % c
        if not profs <= A['adm'][c]:
            return 'client %s lacks %s' % (c, sorted(profs - A['adm'][c]))
    for n in nodes:
        mine = A['words'].get(n, frozenset())
        if not I['words_raw'].get(n, frozenset()) <= mine:
            return 'node %s lacks %s' % (n, words_text(I['words_raw'][n] - mine))    # ADDED (copies extended): words_text
    for c, a in A['renew'].items():
        if ANSWERS:     # ADDED (renewal answers): above its ideal on the chain
            if answer_rank(a) > answer_rank(I['renew'].get(c, ASK)):
                return 'renewal of %s refused' % c
            continue
        if a == 'forever' and I['renew'].get(c) == 'ask':
            return 'renewal of %s refused' % c
    return None


# ADDED (store-as-a-whole): what a property name means for the search. `accepted:` is a residue a
# decision accepts and `info:` a count; neither ends a history.
def ends(prop):
    return not (prop.startswith('accepted:') or prop.startswith('info:'))


def run(ops, nrep, flags=FULL, check_conv=True, world=None):
    """Replays a history; returns the list of (property, step, detail) that failed."""
    W = world.clone() if world else World(nrep, flags)
    fails = []
    seen = set()        # ADDED (store-as-a-whole): accepted and info properties, once a history
    sealed = False      # Seal again was pressed: what was lost in every carrier before it is accepted
    for k, op in enumerate(ops):
        before = None
        if op[0] in ('del', 'edit', 'quit'):    # ADDED (store-as-a-whole): 'quit'
            before = W.fold(W.reps[op[1]])
        # ADDED (renewal answers): a renewal issued is held to the gate's ideal just before it, at the
        # gate's clock
        renewal = None
        if ANSWERS and op[0] == 'renewcert' and op[1] < len(W.reps):
            renewal = (W.ideal(op[1]), CLOCK[W.clock[op[1]]])
        stats0 = dict(W.stats)      # ADDED (store-as-a-whole)
        # ADDED (seal again): the copy before a seal, its damaged versions, and the seal and the act
        # written apart, for the single write of `reseal+`
        sealing = apart = alone = None
        if SEAL and op[0] in ('reseal', 'reseal+') and op[1] < len(W.reps):
            r0 = W.reps[op[1]]
            sealing = (W.fold(r0), set(u for u, (eid, mt, ok) in r0.entries.items()
                                       if u == eid and not ok and W.EL[eid].kind in ('ver', 'pin')))
            if op[0] == 'reseal+':
                apart = W.clone()
                if apart.step(('reseal', op[1])):
                    alone = apart.clone()
                    apart.step(op[2])
                else:
                    apart = None
        if not W.step(op):
            # ADDED (seal again): a seal refused for leaving no entry holding the store key, counted
            if SEAL and W.stats.get('seal-refused-for-the-store-key', 0) != stats0.get('seal-refused-for-the-store-key', 0) \
                    and 'info:seal-refused-for-the-store-key' not in seen:
                seen.add('info:seal-refused-for-the-store-key')
                fails.append(('info:seal-refused-for-the-store-key', k, 'file %d' % op[1]))
            continue
        if sealing is not None:     # ADDED (seal again): what the seal has to leave
            i = op[1]
            A = W.fold(W.reps[i])
            if A['status'] != 'ok':
                fails.append(('seal-left-stopped', k, 'file %d: store %s after the seal' % (i, A['status'])))
            if alone is not None:   # the seal of a single write held as written alone
                A = alone.fold(alone.reps[i])
                if A['status'] != 'ok':
                    fails.append(('seal-left-stopped', k, 'file %d: store %s after the seal alone' % (i, A['status'])))
            for obj in sorted(sealing[0]['lost']):
                if not grants_nothing(obj, A['regval'].get(obj)):
                    fails.append(('seal-granted-a-lost-object', k, 'file %d: %s' % (i, obj)))
            told = sorted(o for o, ids in A['lost'].items() if ids & sealing[1])
            if told and 'info:seal-damaged-version-refuses' not in seen:
                seen.add('info:seal-damaged-version-refuses')
                fails.append(('info:seal-damaged-version-refuses', k, 'file %d: %s' % (i, ', '.join(told))))
            if apart is not None:
                w = seal_with_differs(W, apart, i)
                if w and w.startswith('info:'):     # the same reading, another file: counted
                    if 'info:seal-with-keeps-other-records' not in seen:
                        seen.add('info:seal-with-keeps-other-records')
                        fails.append(('info:seal-with-keeps-other-records', k, 'file %d: %s' % (i, w[5:])))
                elif w:
                    fails.append(('seal-with-reads-otherwise', k, 'file %d: %s' % (i, w)))
        if renewal is not None and answer_refuses(renewal[0]['renew'].get(op[2], ASK), renewal[1]):
            fails.append(('safety', k, 'file %d: renewal of %s issued where its ideal is %s' % (
                op[1], op[2], answer_label(renewal[0]['renew'].get(op[2], ASK)))))
        elif renewal is not None and 'info:renewal-at-a-clock-ahead' not in seen and any(
                answer_refuses(renewal[0]['renew'].get(op[2], ASK), CLOCK[c]) for c in W.clock):
            # issued at the gate's clock, where the ideal still refuses at another copy's clock: the
            # operator approved a renewal that a clock behind it would have refused without asking
            seen.add('info:renewal-at-a-clock-ahead')
            fails.append(('info:renewal-at-a-clock-ahead', k, 'file %d: renewal of %s' % (op[1], op[2])))
        sealed = sealed or op[0] in ('reseal', 'reseal+')    # ADDED (seal again): reseal+
        for i in range(nrep):
            A = W.fold(W.reps[i])
            w = narrower(A, W.ideal(i), W.reps[i].nodes)
            if w:
                fails.append(('sealed-after-a-loss' if sealed else 'safety', k, 'file %d: %s' % (i, w)))
            elif narrower(A, W.ideal(i, True), W.reps[i].nodes):
                fails.append(('rollback-by-overwrite', k, 'file %d' % i))
            # ADDED (started again): nothing under a key set aside counts again in a copy the keep record
            # reached: a copy that serves does so under no key its ideal sets aside
            if STARTS and A['status'] == 'ok' and A['served']:
                gone = A['held'] & W.ideal(i)['aside']
                if gone:
                    fails.append(('aside-counts', k, 'file %d: served under %s, which its ideal sets aside'
                                  % (i, ', '.join(sorted(gone)))))
            for m, o in W.copies.items():       # a copy never gets more than its original's rule allows
                if m in A['words'] and o in A['funcs'] and A['status'] == 'ok':
                    own = W.reps[i].nodes[m][0] == m
                    up = app(A['funcs']['root'], frozenset())
                    if not own and A['served'] and not A['words'][m] <= app(limit(A['funcs'][o]), up):
                        # ADDED (store-as-a-whole): without the mark this is accepted residue
                        p = 'copy-cap' if W.flags.mark else 'accepted:copy-wider'
                        if p == 'copy-cap' or p not in seen:
                            seen.add(p)
                            fails.append((p, k, 'file %d: %s' % (i, m)))
        if before is not None and ANSWERS and op[0] == 'quit':
            # ADDED (renewal answers): KeePass ending before a save discards an answer or a lift that
            # waits with every other unsaved change, an accepted class, counted apart from a rule that waits
            A = W.fold(W.reps[op[1]])
            lift = narrower_renewals(before, A, 'the copy after the quit')    # the stricter way
            for p, w in (('accepted:unsaved-answer-lost', narrower_renewals(A, before, 'the copy before the quit')),
                         ('accepted:unsaved-rule-lost', narrower(A, before, W.reps[op[1]].nodes, False)),
                         ('info:unsaved-lift-lost', lift and 'before the quit, ' + lift)):
                if w and p not in seen:
                    seen.add(p)
                    fails.append((p, k, 'file %d: %s' % (op[1], w)))
        elif before is not None:
            A = W.fold(W.reps[op[1]])
            w = narrower(A, before, W.reps[op[1]].nodes, against='the copy before the step')     # ADDED (renewal answers): against
            if w:
                # ADDED (store-as-a-whole): KeePass ending before a save is accepted (decision 13)
                p = 'accepted:unsaved-rule-lost' if op[0] == 'quit' else 'one-loss'
                if p == 'one-loss' or p not in seen:
                    seen.add(p)
                    fails.append((p, k, 'file %d: %s' % (op[1], w)))
        for s, c in sorted(W.stats.items()):    # ADDED (store-as-a-whole)
            if c != stats0.get(s, 0):
                fails.append(('info:' + s, k, 'file %d' % (op[2] if op[0] == 'merge' else op[1])))
        if any(ends(f[0]) for f in fails):      # ADDED (store-as-a-whole): `ends`
            break
    if not any(ends(f[0]) for f in fails) and check_conv:     # ADDED (store-as-a-whole): `ends`
        for i in range(nrep):       # ADDED (store-as-a-whole): every copy saves what waits
            W.flush(i)
        for _ in range(2):
            for d, s in itertools.permutations(range(nrep), 2):
                W.merge('kp_sync', d, s)
        views = [summary(W.fold(r)) for r in W.reps]
        if any(x != views[0] for x in views):
            fails.append(('convergence', len(ops), repr(views)))
        elif 'clean' in getattr(flags, 'mode', ''):
            if views[0] != summary(W.ideal(0)):
                # ADDED (store-as-a-whole): with the store's acts, which way it differs
                p = 'convergence-to-ideal'
                if flags.newacts:
                    A0 = W.fold(W.reps[0])
                    p += ' (wider)' if narrower(A0, W.ideal(0), W.reps[0].nodes) else ' (not wider)'
                fails.append((p, len(ops), '%r vs %r' % (views[0], summary(W.ideal(0)))))
    return fails


def shrink(ops, nrep, flags, prop):
    """Greedy: drop every step whose removal keeps the same property failing."""
    changed = True
    while changed:
        changed = False
        for k in range(len(ops)):
            cand = ops[:k] + ops[k + 1:]
            if any(f[0] == prop for f in run(cand, nrep, flags)):
                ops, changed = cand, True
                break
    return ops


# ------------------------------------------------------------------ alphabets
def alphabet(nrep, cfg, flags=None):     # ADDED (store-as-a-whole): `flags`
    R = range(nrep)
    acts, faults = [], []
    for r in R:
        acts += [('pair', r), ('revoke', r, 'c0'), ('revoke', r, 'c1'), ('recreate', r),
                 ('padd', r, 'pf'), ('padd', r, 'pg'), ('prem', r, 'pf'), ('prem', r, 'pg'),
                 ('renew', r, 'c0', 'forever'), ('renew', r, 'c0', 'allowed'), ('save', r),
                 ('adopt', r, 0, 'r'), ('copy', r)]
        if ANSWERS:     # ADDED (renewal answers): the four answers about two clients, a dialog, the clock
            acts = [a for a in acts if not (a[0] == 'renew' and a[1] == r)]
            acts += [('renew', r, c, x) for c in ('c0', 'c1') for x in ('forever', 'until1', 'until2', 'ask')]
            acts += [('see', r, c) for c in ('c0', 'c1')] + [('clock', r, p) for p in range(len(CLOCK))]
        for n in ('root', 'N'):
            for d in DOCS:
                acts.append(('rule', r, n, d))
            if ROW_ACTS:     # ADDED (rules per profile): a single row, and the switch
                acts += [('switch', r, n, x) for x in ('deny', 'inherit')]
                acts += [('row', r, n, p, x) for p in PROFS for x in ('rw', 'r', '', 'off')]
        # ADDED (store-as-a-whole): the store's other acts
        if flags is not None and flags.newacts:
            acts += [('renewcert', r, 'c0'), ('renewcert', r, 'c1'), ('delauth', r), ('create', r)]
        if flags is not None and flags.pending:
            acts += [('quit', r)]
        if STARTS:      # ADDED (started again): the store started again, and a plugin of a later format
            acts += [('restart', r), ('newer', r)]
    progs =['kp_sync', 'k2a'] if 'synconly' in cfg else ['kp_sync', 'kp_other', 'xc', 'k2a', 'dx']
    merges = [('merge', p, d, s) for p in progs for d in R for s in R if d != s]
    sels = [('head', 'new'), ('head', 'old'), ('ref', 'revoked'), ('ref', 'retired'),
            ('ver', 'rule:N', 'new'), ('ver', 'rule:N', 'old'), ('ver', 'rule:root', 'new'),
            ('ver', 'renew:c0', 'new'), ('rec',), ('auth',), ('padd',), ('prem',)]
    if ANSWERS:     # ADDED (renewal answers): an answer replaced, and the other client's answer
        sels += [('ver', 'renew:c0', 'old'), ('ver', 'renew:c1', 'new')]
    if STARTS:      # ADDED (started again): a keep record, and the first authority by its name
        sels += [('kept',), ('auth', 'a0')]
    if ANSWERS and 'foreign' in cfg:     # ADDED (renewal answers): a text out of form, by a key holder
        faults += [('unreadable', r, c) for r in R for c in ('c0', 'c1')]
    if 'rec' in cfg:
        faults += [('del', r, s, 1) for r in R for s in sels]
        faults += [('reseal', r) for r in R]
        if SEAL:    # ADDED (seal again): the seal's dialog, and the seal beside an act in a single write
            faults += [('sealsee', r) for r in R]
            faults += [('reseal+', r, a) for r in R for a in
                       (('revoke', r, 'c0'), ('create', r), ('rule', r, 'N', 'r'), ('padd', r, 'pg'))]
    if 'skew' in cfg:
        faults += [('del', r, s, 2) for r in R for s in sels]
    if 'trace' in cfg:
        faults += [('del', r, s, 0) for r in R for s in sels]
    if 'clear' in cfg:
        faults += [('clear', r) for r in R]
    if 'over' in cfg:
        faults += [('over', d, s) for d in R for s in R if d != s]
    if 'foreign' in cfg:
        faults += [('edit', r, s) for r in R for s in sels]
        faults += [('import', d, s) for d in R for s in R if d != s]
        faults += [('plant', r, w) for r in R for w in ('rule', 'retire', 'record', 'profile', 'auth')]
    if 'strip' in cfg:
        faults += [('strip', r) for r in R]
    return acts, merges, faults


CONFIGS = [
    ('A merges only (every program)', ''),
    ('B + deletions by hand with a deletion record', 'rec'),
    ('C + deletions that leave no record', 'rec trace'),
    ('D + records dated before the entry (a clock behind)', 'rec skew'),
    ('E + deletion records cleared', 'rec trace clear'),
    ('F + foreign: edits, import with new identifiers, planted entries', 'rec foreign'),
    ('G + a copy stripped of its reference', 'rec strip'),
    ('H + a whole file written over another', 'rec over'),
    ('I everything at once', 'rec skew trace clear over foreign strip'),
    ('J only KeePass Synchronize and Keepass2Android; deletions with a record', 'synconly rec'),
    ('J2 the same; every kind of deletion', 'synconly rec skew trace'),
    ('K the same, and foreign edits, imports and planted entries', 'synconly rec skew trace foreign'),
]


def search(name, cfg, nrep, count, length, seed, flags=FULL, quiet=False):
    rng = random.Random(seed)
    acts, merges, faults = alphabet(nrep, cfg, flags)   # ADDED (store-as-a-whole): flags
    flags.mode = 'clean' if cfg in ('', 'synconly') else ''
    found = {}
    counts = {}
    for _ in range(count):
        ops = []
        for _ in range(rng.randint(3, length)):
            x = rng.random()
            pool = acts if x < 0.45 else merges if x < 0.75 or not faults else faults
            ops.append(rng.choice(pool))
        once = set()    # ADDED (store-as-a-whole): accepted and info counted once a history
        for f in run(ops, nrep, flags):
            if not ends(f[0]):
                if f[0] in once:
                    continue
                once.add(f[0])
            counts[f[0]] = counts.get(f[0], 0) + 1
            if f[0] not in found or len(ops) < len(found[f[0]][0]):
                found[f[0]] = (ops, f)
    out = []
    for prop, (ops, f) in sorted(found.items()):
        if prop.startswith('info:'):    # ADDED (store-as-a-whole): a count, not shrunk
            out.append((prop, counts[prop], ops, f))
            continue
        small = shrink(ops, nrep, flags, prop)
        detail = [x for x in run(small, nrep, flags) if x[0] == prop][0]
        out.append((prop, counts[prop], small, detail))
    if not quiet:
        print('%-66s %d files %6d histories  %s' % (name, nrep, count,
              'no failure' if not out else ', '.join('%s x%d' % (p, c) for p, c, _, _ in out)))
        for prop, c, small, detail in out:
            print('    shortest %s (%s):' % (prop, detail[2]))
            for op in small:
                print('        ' + ' '.join(str(x) for x in op))
    return out


def exhaustive(nrep, cfg, depth, flags=FULL):
    """Every sequence of transport and fault steps up to `depth`, after a fixed divergence:
    file 0 revoked c0, narrowed N and the root, removed profile pf, refused renewals; file 1 is stale."""
    prefix = [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
              ('revoke', 0, 'c0'), ('prem', 0, 'pf'), ('renew', 0, 'c0', 'forever'),
              ('rule', 0, 'root', 'none')]
    acts, merges, faults = alphabet(nrep, cfg)
    steps = merges + faults
    flags.mode = ''
    total, bad = 0, {}
    if flags.pending:   # ADDED (store-as-a-whole): the divergence saves each rule it sets
        prefix = [x for op in prefix for x in ([op, ('save', 0)] if op[0] == 'rule' else [op])]
    W0 = World(nrep, flags)
    for op in prefix:
        assert W0.step(op), op
    for d in range(1, depth + 1):
        for seq in itertools.product(steps, repeat=d):
            total += 1
            for f in run(list(seq), nrep, flags, check_conv=False, world=W0):
                key = f[0]
                if key not in bad or len(seq) < len(bad[key][1]):
                    bad[key] = (bad.get(key, (0,))[0], seq, f)
                bad[key] = (bad[key][0] + 1, bad[key][1], bad[key][2])
    return total, len(steps), bad


def laws(count, seed):
    """The value is a function of the set of elements; adding an element that has not seen
    the present heads of its object never widens (ideal transport, no faults)."""
    rng = random.Random(seed)
    acts, merges, _ = alphabet(2, '')
    bad = 0
    for _ in range(count):
        W = World(2)
        for _ in range(rng.randint(2, 7)):
            W.step(rng.choice(acts if rng.random() < 0.6 else [m for m in merges if m[1] == 'kp_sync']))
        a, b = W.ideal(0), W.ideal(1)
        r = Rep()
        r.nodes = dict(W.reps[0].nodes)
        for n, nd in W.reps[1].nodes.items():
            r.nodes.setdefault(n, nd)
        for eid in W.K[0] | W.K[1]:
            r.entries[eid] = [eid, 0, True]
        j = W.fold(r, FULL)
        # the join of two files is at most each side for whatever the other side never saw:
        # anything wider than one side must come from elements that side lacks and whose
        # causal context covers that side's heads; checked through registers
        for obj, V in j['vers'].items():
            dead = set()
            for e in V.values():
                dead |= e.after
            live = [e for i, e in V.items() if i not in dead and e.kind == 'ver']
            if obj.startswith('rule:') and live:
                f = j['funcs'].get(obj[5:])
                for e in live:
                    for x in (frozenset(), frozenset('r'), ALL):
                        if f is not None and not app(f, x) <= app(e.data, x):
                            bad += 1
        for (rk, subj) in a['refusals'] | b['refusals']:
            if (rk, subj) not in j['refusals']:
                bad += 1
            if rk == 'revoked' and subj in j['adm']:
                bad += 1
    return bad


SCENARIOS = [
    # (what happens, the finding it replays, files, history, file asked, what must hold of its view)
    ('a revocation, a KeePassXC merge into the stale copy, the revocation entry deleted there',
     'store-entries survival P1 A', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('the same, and the head deleted there too, both with deletion records',
     'store-entries survival P1 B', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 1),
      ('del', 1, ('head', 'new'), 1)],
     1, lambda v: 'c0' not in v['adm']),
    ('the same two deletions leaving no record (limit: that copy is a stale copy again)',
     'store-entries survival P1, what is left', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('del', 1, ('ref', 'revoked'), 0),
      ('del', 1, ('head', 'new'), 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('a name removed in one copy; removed and added again in the other; merged',
     'store-entries survival P2', 2,
     [('prem', 1, 'pf'), ('padd', 1, 'pf'), ('prem', 0, 'pf'), ('merge', 'kp_sync', 0, 1),
      ('merge', 'kp_sync', 1, 0)],
     0, lambda v: 'pf' not in v['names']),
    ('a name added in each copy on its own survives the merge', 'store-entries survival P2, other side', 2,
     [('padd', 0, 'pg'), ('padd', 1, 'pg'), ('prem', 1, 'pf'), ('merge', 'xc', 0, 1)],
     0, lambda v: 'pg' in v['names'] and 'pf' not in v['names']),
    ('a rule set, taken away, and the same rule set again in a copy that saw neither',
     'rules-on-node survival 2.4; rules-marker-entry survival P1', 2,
     [('rule', 0, 'N', 'none'), ('rule', 0, 'N', 'inherit'), ('rule', 1, 'N', 'none'),
      ('merge', 'kp_sync', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['words']['N']),
    ('the newest rule version and the head deleted in the copy that wrote them, then a stale copy by KeePassXC',
     'rules-linked survival P2 C3', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('merge', 'xc', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, the deletions leaving no record', 'rules-linked survival P2 C3, no trace', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 0), ('del', 0, ('head', 'new'), 0), ('merge', 'xc', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, then deletion records cleared, then Synchronize with the stale copy (limit)',
     'rules-linked survival P2 C3c', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0),
      ('merge', 'kp_sync', 0, 1)],
     0, lambda v: not v['words'].get('N')),
    ('the same, but the store sealed again by a press before the stale copy arrives',
     'rules-linked survival P2 C3c, with the press', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('head', 'new'), 1), ('clear', 0),
      ('reseal', 0), ('merge', 'kp_sync', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['words'].get('N')),
    ('one rule version deleted by hand, the deletion synchronized', 'rules-linked survival P3 C5', 2,
     [('rule', 0, 'N', 'none'), ('del', 0, ('ver', 'rule:N', 'new'), 1), ('merge', 'kp_sync', 1, 0)],
     1, lambda v: v['status'] == 'ok' and not v['words']['N'] and bool(v['words']['root'])),
    ('two concurrent rule versions, then both entries deleted by hand',
     'rules-marker-entry survival P2', 2,
     [('rule', 0, 'N', 'none'), ('rule', 1, 'N', 'rw'), ('merge', 'kp_sync', 0, 1),
      ('del', 0, ('ver', 'rule:N', 'new'), 1), ('del', 0, ('ver', 'rule:N', 'new'), 1)],
     0, lambda v: not v['words']['N']),
    ('a foreign file plants a rule, a retirement, a record and a profile',
     'rules-central security S1; store-entries security S1; rules-linked security S5', 2,
     [('plant', 0, 'rule'), ('plant', 0, 'retire'), ('plant', 0, 'record'), ('plant', 0, 'profile')],
     0, lambda v: v['served'] == 'a0' and v['words']['N'] == frozenset('r') and 'c9' not in v['adm']
     and 'pg' not in v['names']),
    ('a foreign authority arrives intact: nothing is served until the operator chooses',
     'CLAUDE.md, 503 undecided', 2,
     [('plant', 0, 'auth')], 0, lambda v: v['served'] is None and not v['adm']),
    ('a copy of a sealed node; a save; the copy then loses its carried reference',
     'rules-linked survival P1 C1b, C2', 2,
     [('rule', 0, 'N', 'none'), ('copy', 0), ('save', 0), ('strip', 0)],
     0, lambda v: not v['words']['M1']),
    ('the same with no save between the copy and the loss (limit)',
     'rules-linked survival P1, what is left', 2,
     [('rule', 0, 'N', 'none'), ('copy', 0), ('strip', 0)],
     0, lambda v: not v['words']['M1']),
    ('everything of the plugin deleted without a trace in the copy that wrote it, then a stale copy by KeePassXC',
     'rules-linked survival P2 C3b', 2,
     [('rule', 0, 'N', 'rw'), ('merge', 'kp_sync', 1, 0), ('rule', 0, 'N', 'none'), ('revoke', 0, 'c0')]
     + [('del', 0, x, 0) for x in (('head', 'new'), ('ver', 'rule:N', 'new'), ('ver', 'rule:root', 'new'),
                                   ('ref', 'revoked'), ('auth',), ('padd',))]
     + [('merge', 'xc', 0, 1)],
     0, lambda v: not v['adm'] and not v['words'].get('N')),
    ('both carriers of a revocation deleted, the store sealed again by a press, merged into a stale copy by KeePassXC',
     'what the deletion-record witness is for', 2,
     [('revoke', 0, 'c0'), ('del', 0, ('ref', 'revoked'), 0), ('del', 0, ('head', 'new'), 0),
      ('reseal', 0), ('merge', 'xc', 1, 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('a store entry edited by hand, and the edit synchronized into every copy',
     'store-entries survival P7', 2,
     [('revoke', 0, 'c0'), ('merge', 'xc', 1, 0), ('edit', 0, ('ref', 'revoked')), ('merge', 'kp_sync', 1, 0)],
     1, lambda v: 'c0' not in v['adm']),
    ('an import with new identifiers of a stale copy', 'rules-linked survival P3 C6', 2,
     [('rule', 0, 'N', 'none'), ('revoke', 0, 'c0'), ('import', 0, 1)],
     0, lambda v: v['status'] == 'ok' and not v['adm'] and not v['words']['N']),
]


def scenarios():
    abl = [('as written', FULL), ('no snapshot', Flags(snapshot=False)), ('no cover', Flags(cover=False)),
           ('no witness', Flags(T=False)), ('no record rule', Flags(recrule=False)),
           ('no head tag', Flags(headtag=False)), ('no decision tag', Flags(dtag=False)),
           ('generations', Flags(causal_add=False))]
    print('   columns: ' + ' | '.join(n for n, _ in abl))
    for k, (name, src, nrep, ops, i, ok) in enumerate(SCENARIOS):
        name = '%2d. %s' % (k + 1, name)
        row = []
        for _, fl in abl:
            W = World(nrep, fl)
            for op in ops:
                W.step(op)
            v = W.fold(W.reps[i])
            row.append(('holds' if ok(v) else 'FAILS') + ('' if v['status'] == 'ok' else ' (%s)' % v['status']))
        print('%s\n      [%s]\n   %s' % (name, src, ' | '.join(row)))


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'quick'
    sys.stdout.reconfigure(line_buffering=True)
    n = 4000 if mode == 'quick' else 20000
    t0 = time.time()
    print('== lattice laws on the ideal transport: %d violations in %d random pairs of files'
          % (laws(n // 2, 7), n // 2))
    print('== the sequences of the critiques, replayed; and each part of the read rule switched off')
    scenarios()
    if mode == 'scenarios':
        return
    print('== random histories, the design as written')
    for nrep in (2, 3):
        for k, (name, cfg) in enumerate(CONFIGS):
            search(name, cfg, nrep, n, 9, 100 + k + 10 * nrep)
    print('== exhaustive, after a fixed divergence (file 0 newer in every way, file 1 stale)')
    for name, cfg, depth in (('merges', '', 4), ('merges and deletions with a record', 'rec', 3),
                             ('merges and deletions without a record', 'trace', 3),
                             ('Synchronize and Keepass2Android only, deletions with a record', 'synconly rec', 3)):
        if mode == 'quick':
            depth -= 1
        total, width, bad = exhaustive(2, cfg, depth)
        print('%-45s %d steps wide, depth %d: %7d histories  %s' % (
            name, width, depth, total,
            'no failure' if not bad else ', '.join('%s x%d' % (p, c) for p, (c, _, _) in sorted(bad.items()))))
        for p, (c, seq, f) in sorted(bad.items()):
            print('    shortest %s (%s): %s' % (p, f[2], ' ; '.join(' '.join(str(x) for x in op) for op in seq)))
    print('== ablations: one part of the read rule switched off, random histories, 2 files')
    abl = [('no head snapshot (entries only)', Flags(snapshot=False), 'rec'),
           ('no deletion-record witnesses for refusals', Flags(T=False), 'rec trace'),
           ('a deletion record does not take an element out of force', Flags(recrule=False), 'rec trace'),
           ('a deleted head is not recognized', Flags(headtag=False), 'rec'),
           ('a decision without its head is accepted', Flags(cover=False), 'rec trace'),
           ('the deletion record of a decision tells nothing', Flags(dtag=False), 'rec'),
           ('profile additions by generation, not by what they saw', Flags(causal_add=False), '')]
    for name, fl, cfg in abl:
        base = search(name, cfg, 2, n, 9, 555, FULL, quiet=True)
        search(name + ' [' + (cfg or 'merges only') + ']', cfg, 2, n, 9, 555, fl)
        print('    the design as written, same histories: %s' % (
            'no failure' if not base else ', '.join('%s x%d' % (p, c) for p, c, _, _ in base)))
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
