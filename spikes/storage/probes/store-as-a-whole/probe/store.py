"""The recommended store's records as real entries: what store.py writes, read.py reads.

An element of the model becomes an ordinary entry in the plugin's group (design 3.2): `KPG.Kind`,
`KPG.Data` as one flat JSON object of strings, `KPG.Mac` an HMAC-SHA256 under the store key over the
kind, a line feed, the data and, for a head, every `KPG.S.` string by name in ordinal order. The
identifier is 3 bytes of the kind's keyed tag, 5 bytes of a keyed hash of what the element is about,
2 bytes of height and the first 6 bytes of the MAC. Lists (`after`, `profiles`) are space-separated
identifiers or names inside a string, as the design writes them. A revocation is about the client's
key (change 5); a retirement that carries the store key holds it in a protected `KPG.Data`
(change 6); the authority's document holds the store key, protected. Nothing is written on a user's
node (decision 11).

The store keys are invented: SHA-256 of a fixed text and the model's key label.
"""
import hashlib
import hmac
import json

KINDS = ('authority', 'retired', 'revoked', 'client', 'profile', 'removed', 'version', 'head')


def keybytes(label):
    return hashlib.sha256(('kpg-store-as-a-whole/store-key/' + label).encode()).digest()


def hm(key, text):
    return hmac.new(key, text.encode('utf-8'), hashlib.sha256).digest()


def tag(key, kind):
    return hm(key, 'tag\n' + kind)[:3].hex()


def subject_hash(key, subject):
    return hm(key, 'subject\n' + subject)[:5].hex()


def mac(key, kind, data, extra):
    text = kind + '\n' + data + ''.join('\n%s\n%s' % (n, extra[n]) for n in sorted(extra))
    return hm(key, text).hex()


# ADDED (rules per profile): the identifier of a row's version. 'pair', the form run: the subject
# hash over the object, the node and the profile together. 'split', the form measured beside it for
# the cost of a deletion record (rows.py, the part `dels`): the subject hash over the node's rule
# alone, 'rule:<node>', and the two bytes of the height, which are 0 for a version, a keyed tag of
# the profile's name.
ID_FORM = 'pair'


def profile_tag(key, name):
    return hm(key, 'profile\n' + name)[:2].hex()


def ident(key, kind, subject, height, m):
    if ID_FORM == 'split' and kind == 'version' and subject.startswith('rule:') and '/' in subject:
        node, _, p = subject.partition('/')         # ADDED (rules per profile)
        return tag(key, kind) + subject_hash(key, node) + profile_tag(key, p) + m[:12]
    return tag(key, kind) + subject_hash(key, subject) + int(height).to_bytes(2, 'big').hex() + m[:12]


def dumps(d):
    return json.dumps(d, sort_keys=True, separators=(',', ':'), ensure_ascii=False)


def subject_of(kind, d):
    """What an element is about, from its data: the keyed hash in its identifier is over this."""
    if kind in ('authority',):
        return 'authority:' + d['id']
    if kind == 'retired':
        return 'authority:' + d['authority']
    if kind in ('revoked', 'client'):
        return 'client:' + d['client']
    if kind in ('profile', 'removed'):
        return 'profile:' + d['name']
    if kind == 'version':
        return d['object']
    if kind == 'head':
        return 'store'
    raise ValueError(kind)


PROFS = ('pf', 'pg')    # ADDED (copies extended): the profiles a rule may name
LETTERS = 'rw'          # ADDED (owed forms): the letters a row's version may hold
# ADDED (S34 forms): the texts of `S34-DESIGN.md` 2.2 and 2.3, with the probe's letters. 'seen': a
# rule taken off is 'off:' and the letters that arrived ('off:' alone, nothing), or 'off:deny'; the
# switch inheriting is 'sw:inherit' and, for each profile something arrived for, ' pf=rw'. 'above':
# a rule taken off is 'off:@' and the identifiers named, separated by commas, or 'off:deny'; the
# switch inheriting is 'sw:inherit' and, for each profile with a version above, ' pf=@' and its
# identifiers. The design puts the identifiers in a member `above`; the probe keeps them in `doc`,
# which its summary items restate whole as well.
DEP = None


def rec_doc(rec, uid=None):
    """ADDED (S34 forms): a record of a rule taken off, as text."""
    if rec[0] == 'deny':
        return 'deny'
    if rec[0] == 's':
        return ''.join(sorted(rec[1]))
    return '@' + ','.join(sorted(uid(i) if uid else i for i in rec[1]))


def doc(f, uid=None):       # ADDED (S34 forms): `uid`, an element's identifier on the file
    # ADDED (rules per profile): a row's version: 'row:<letters>' (an empty rule is 'row:'), 'off:'
    # the rule taken off, 'void:' a version holding nothing of its own, 'cut:<letters>' letters
    # granted and the rule taken off together; a switch's version: 'sw:deny' or 'sw:inherit'
    if f[0] == 'rows':
        if f[1] is None:
            if f[2] and len(f) > 3 and DEP is not None:     # ADDED (S34 forms): a written version's record
                return 'off:' + '|'.join(sorted(rec_doc(r, uid) for r in f[3]))
            return 'off:' if f[2] else 'void:'
        return ('cut:' if f[2] else 'row:') + ''.join(sorted(f[1]))
    if f[0] == 'sws':
        if len(f) > 2 and DEP is not None:      # ADDED (S34 forms): the switch's record
            out = []
            for r in sorted(f[2], key=repr):
                if r[0] == 's':
                    per = {}
                    for x in r[1]:
                        p, w = x.split('.')
                        per.setdefault(p, []).append(w)
                    out.append(''.join(' %s=%s' % (p, ''.join(sorted(per[p]))) for p in sorted(per)))
                else:
                    out.append(''.join(' %s=@%s' % (p, ','.join(sorted(uid(i) if uid else i for i in ids)))
                                       for p, ids in sorted(r[1])))
            return 'sw:' + '/'.join(sorted(f[1])) + '|'.join(out)
        return 'sw:' + '/'.join(sorted(f[1]))
    if f[0] == 'pp':    # ADDED (copies extended): 'pp:pf=rw,pg=', a profile left out inheriting
        named = [p for p in PROFS if not any(x.startswith(p + '.') for x in f[2])]
        return 'pp:' + ','.join('%s=%s' % (p, ''.join(sorted(x[-1] for x in f[1] if x.startswith(p + '.'))))
                                for p in named)
    return '%s:%s' % (f[0], ''.join(sorted(f[1])))


def undoc(s):
    k, w = s.split(':', 1)
    if k == 'off' and DEP is not None:      # ADDED (S34 forms): what a rule taken off recorded
        if w == 'deny':
            return ('rows', None, True, frozenset([('deny',)]))
        if DEP == 'above':
            if not w.startswith('@'):
                raise ValueError(s)
            return ('rows', None, True, frozenset([('a', frozenset(x for x in w[1:].split(',') if x))]))
        if not set(w) <= set(LETTERS):
            raise ValueError(s)
        return ('rows', None, True, frozenset([('s', frozenset(w))]))
    if k == 'sw' and DEP is not None and w.split(' ')[0] == 'inherit':     # ADDED (S34 forms)
        items = w.split(' ')[1:]
        if DEP == 'seen':
            letters = set()
            for x in items:
                p, ws = x.split('=')
                if not ws or not set(ws) <= set(LETTERS):
                    raise ValueError(s)
                letters |= set(p + '.' + c for c in ws)
            return ('sws', frozenset(['inherit']), frozenset([('s', frozenset(letters))]))
        pairs = []
        for x in items:
            p, ids = x.split('=')
            if not ids.startswith('@'):
                raise ValueError(s)
            pairs.append((p, frozenset(i for i in ids[1:].split(',') if i)))
        return ('sws', frozenset(['inherit']), frozenset([('a', frozenset(pairs))]))
    if k in ('row', 'cut', 'off', 'void'):      # ADDED (rules per profile)
        if not set(w) <= set(LETTERS) or (k in ('off', 'void') and w):     # ADDED (owed forms): LETTERS
            raise ValueError(s)
        if k == 'off':
            return ('rows', None, True)
        if k == 'void':
            return ('rows', None, False)
        return ('rows', frozenset(w), k == 'cut')
    if k == 'sw':       # ADDED (rules per profile)
        vals = frozenset(w.split('/'))
        if not vals or not vals <= {'deny', 'inherit'}:
            raise ValueError(s)
        return ('sws', vals)
    if k == 'pp':       # ADDED (copies extended)
        named = dict(part.split('=') for part in w.split(',')) if w else {}
        if not set(named) <= set(PROFS) or not all(set(x) <= set('rw') for x in named.values()):
            raise ValueError(s)
        return ('pp', frozenset(p + '.' + v for p, x in named.items() for v in x),
                frozenset(p + '.' + v for p in PROFS if p not in named for v in 'rw'))
    if k not in ('const', 'cap') or not set(w) <= set('rw'):
        raise ValueError(s)
    return (k, frozenset(w))


class Encoder:
    """Model elements as entries. `nodes` maps the model's node names to real identifiers."""

    def __init__(self, W, nodes):
        self.W, self.nodes = W, nodes
        self.uuid, self.eid = {}, {}

    def obj(self, o):
        if o.startswith('rule:'):
            n = o[5:]
            # ADDED (rules per profile): 'rule:<node>/<profile>', a row; the node mapped, the
            # profile's name as it is
            n, sep, p = n.partition('/')
            return 'rule:' + self.nodes.get(n, n) + sep + p
        if o.startswith('sw:'):     # ADDED (rules per profile): the node's switch
            n = o[3:]
            return 'switch:' + self.nodes.get(n, n)
        return o

    def key(self, e):
        return keybytes(e.key)

    def u(self, eid):
        if eid not in self.uuid:
            self.entry(eid)
        return self.uuid[eid]

    def entry(self, eid):
        """(uuid, title, {name: value}, protected names)."""
        W = self.W
        e = W.EL[eid]
        key = self.key(e)
        nonce = hashlib.sha256(('nonce/' + eid).encode()).hexdigest()[:32]
        extra, prot, height = {}, set(), 0
        if e.kind == 'auth':
            kind, d = 'authority', {'v': '2', 'id': e.obj, 'storeKey': key.hex(), 'nonce': nonce}
            prot.add('KPG.Data')
        elif e.kind == 'ref' and e.data[0] == 'revoked':
            from crdt_model import ckey
            kind, d = 'revoked', {'v': '2', 'certificate': e.data[1], 'client': ckey(e.data[1]), 'nonce': nonce}
        elif e.kind == 'ref':
            kind, d = 'retired', {'v': '2', 'authority': e.data[1], 'nonce': nonce}
            if e.carry:
                d['storeKey'] = keybytes(e.carry).hex()
                prot.add('KPG.Data')
        elif e.kind == 'rec':
            from crdt_model import ckey
            kind, d = 'client', {'v': '2', 'certificate': e.obj, 'client': ckey(e.obj), 'issuer': e.data[0],
                                 'profiles': ' '.join(sorted(e.data[1])), 'nonce': nonce}
        elif e.kind == 'padd':
            kind, d = 'profile', {'v': '2', 'name': e.obj, 'after': ' '.join(sorted(self.u(x) for x in e.after)),
                                  'nonce': nonce}
        elif e.kind == 'prem':
            kind, d = 'removed', {'v': '2', 'name': e.obj, 'nonce': nonce}
        elif e.kind == 'ver':
            kind, d = 'version', {'v': '2', 'object': self.obj(e.obj), 'after': ' '.join(sorted(self.u(x) for x in e.after)),
                                  'doc': doc(e.data, self.u), 'nonce': nonce}     # ADDED (S34 forms): self.u
        elif e.kind == 'head':
            height = e.height
            kind, d = 'head', {'v': '2', 'height': str(e.height), 'after': ' '.join(sorted(self.u(x) for x in e.after)),
                               'nonce': nonce}
            items = []
            for t in sorted(e.data['ref']):
                items.append({'kind': 'revoked', 'client': t[1]} if t[0] == 'revoked' else {'kind': 'retired', 'authority': t[1]})
            for (n, pid) in sorted(e.data['prem']):
                items.append({'kind': 'removed', 'name': n, 'id': self.u(pid)})
            for vid in sorted(e.data['regs']):
                v = W.EL[vid]
                items.append({'kind': 'version', 'id': self.u(vid), 'object': self.obj(v.obj),
                              'after': ' '.join(sorted(self.u(x) for x in v.after)), 'doc': doc(v.data, self.u)})     # ADDED (S34 forms): self.u
            # ADDED (copies extended): the copies a Seal again acknowledged, by the identifier
            # each copy's content gives
            for cid in sorted(e.data.get('ack', ())):
                items.append({'kind': 'copy', 'id': cid})
            for k, it in enumerate(sorted(items, key=dumps)):
                extra['KPG.S.%04d' % (k + 1)] = dumps(it)
        else:
            raise ValueError(e.kind)
        data = dumps(d)
        m = mac(key, kind, data, extra)
        u = ident(key, kind, subject_of(kind, d), height, m)
        strings = {'KPG.Kind': kind, 'KPG.Data': data, 'KPG.Mac': m}
        strings.update(extra)
        self.uuid[eid], self.eid[u] = u, eid
        return u, 'KPG %s %s' % (kind, u[:8]), strings, prot
