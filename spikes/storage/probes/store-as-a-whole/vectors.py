"""Vectors for the product's reading of the store, made by the measured code and by nothing else.

Run offline, from anywhere:

  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py
  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py --readings
  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py --records
  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py --meanings
  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py --writes

It starts no program and reads no file but `probe/store.py` and `probe/read.py` beside it, and the
model for part 7, below. Every value printed is an output of that code: `hm`, `tag`,
`subject_hash`, `mac`, `ident` and `dumps` of store.py, and `read` of read.py, with its history
rule on, over a dump composed here. The inputs are invented and written out beside each value. The
records of the kinds the measured code never wrote (`withdrawn`, `kept`, `format`) are composed
with the same functions, so their vectors hold the product to the measured check and identifier,
and to nothing the measured code ran of those kinds.

Without an argument it prints every vector. With `--readings` it prints the file the product keeps
as `tests/Shared/StoreReadingVectors.json`, byte for byte: the small stores of part 4 and what
read.py answered for each. That file renames and adds nothing but what `STATES` and `ABOUT` hold:
the three words of read.py's `status` are written as the names the product gives those states.

ADDED (vectors for the writes): three further files the product keeps, each printed by an argument
of its own and each opening with an `about` that names what made it and what is invented.
`--records` prints `tests/Shared/StoreRecordVectors.json`: records in the forms STORE.md 614, 615,
616 and 648 fix, and summary entries, composed as `rec` composes them (part 5). `--meanings` prints
`tests/Shared/StoreMeaningVectors.json`: small stores of those records and what read.py answers for
each about authorities, admissions, refusals and profile names (part 6). `--writes` prints
`tests/Shared/StoreWriteVectors.json`: the file after each act of four fixed sequences that the
model plays, written out by store.py's `Encoder`, with read.py's answer after each (part 7). Part
7 reads more than store.py and read.py: the model, `model/crdt_model.py`, and `probe/bridge.py`
for the switches the family's runs give it. The script imports both whatever it prints, and
starts no program for them either.

ADDED (rules per profile): the probe's rows switched on as decided, with the identifier of a row's
version in the form `split` and the product's texts in the place of the model's letters (below
the imports). `--records` adds versions of a profile's row and of a node's switch, and a summary
entry that restates them. `--meanings` adds small stores of such versions in a tree of three
groups, with what read.py answers about the rules of each. `--writes` plays every sequence with
the rows switched on, and adds the sequences of rows, switches and removed versions over two
copies of `measured-rules-per-profile` sections 4, 5 and 7.
"""
import base64
import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, 'probe'))
sys.path.insert(0, os.path.join(HERE, 'model'))     # ADDED (vectors for the writes)
import read as R      # noqa: E402
import store as S     # noqa: E402
import bridge as B    # noqa: E402  ADDED (vectors for the writes): its FLAGS, the store as decided
import crdt_model as M    # noqa: E402  ADDED (vectors for the writes)

# ADDED (rules per profile): the probe's rows switched on as decided. The identifier of a row's
# version in the form `split` of store.py, and the texts of store.py's `doc` with the model's two
# letters written as the product writes them: read as R and write as U, which CRUSDLAP orders as
# the model sorts r and w. A text of any other form is the model's own and passes as it is; a text
# of a row or a switch that the product's form does not have is refused, so that no vector holds
# such a text. Nothing of store.py changes but these two names, which its Encoder and read.py
# look up.
S.ID_FORM = 'split'
LETTERS = {'r': 'R', 'w': 'U'}
ORDER = 'CRUSDLAP'
MODEL_DOC, MODEL_UNDOC = S.doc, S.undoc


def letters(model):
    """The model's letters as the product's, in the order of CRUSDLAP."""
    return ''.join(sorted((LETTERS[x] for x in model), key=ORDER.index))


def product_doc(f):
    text = MODEL_DOC(f)
    if f[0] not in ('rows', 'sws'):
        return text
    kind, _, rest = text.partition(':')
    if kind == 'row':
        return 'row:' + letters(rest)
    if text in ('off:', 'sw:deny', 'sw:inherit'):
        return text
    raise ValueError('a text the product does not write: ' + text)


def model_undoc(text):
    kind, _, rest = text.partition(':')
    if kind == 'row':
        back = {v: k for k, v in LETTERS.items()}
        if rest != letters(back[x] for x in rest if x in back) or len(rest) != len(set(rest)):
            raise ValueError('a text the product does not write: ' + text)
        text = 'row:' + ''.join(sorted(back[x] for x in rest))
    return MODEL_UNDOC(text)


S.doc, S.undoc = product_doc, model_undoc
R.ROWS = R.PP = True    # as bridge.rows sets them: a node's rule read from its rows and its switch
M.ROWS, M.PP = 'empty', True    # as bridge.rows('empty') sets them: a removed row as decided

# read.py's status, and the state the product names for it.
STATES = {'ok': 'Whole', 'incomplete': 'Stopped', 'nostore': 'None'}
ABOUT = ('What probe/read.py of spikes/storage/probes/store-as-a-whole on spikes/storage-survival '
         'answers with its history rule on for small stores that probe/store.py composed there, '
         'made by vectors.py beside the probe and never from this side. Its three answers are '
         'written as the states they are: its ok as Whole, its incomplete as Stopped, and its '
         'answer for a file with no store as None. Each entry\'s history is oldest first, as the '
         'probe\'s dump numbers it.')

ONE, TWO = S.keybytes('one'), S.keybytes('two')
NONCE = '00112233445566778899aabbccddeeff'
out = {'keys': {'one': ONE.hex(), 'two': TWO.hex()}}


def rec(key, kind, d, extra=None, height=0, subject=None, number=None):
    """An entry of the store: identifier and strings, as store.py composes them."""
    extra = extra or {}
    data = S.dumps(d)
    m = S.mac(key, kind, data, extra)
    subject = subject if subject is not None else S.subject_of(kind, d)
    u = S.ident(key, kind, subject, number if number is not None else height, m)
    strings = {'KPG.Kind': kind, 'KPG.Data': data, 'KPG.Mac': m}
    strings.update(extra)
    return u, strings


# 1. The tags of the kinds the measured code never wrote, and each subject's hash.
out['tags'] = {k: [S.tag(ONE, k), S.tag(TWO, k)] for k in ('withdrawn', 'kept', 'format')}
subjects = ['authority:' + 'a1' * 32, 'client:' + 'c3' * 32, 'profile:reader', 'store',
            'rule:' + '0f' * 16, 'switch:' + '0f' * 16, 'renew:' + 'c3' * 32,
            'certificate:' + 'd4' * 32, 'format:1', 'format:2']
out['subjects'] = {s: S.subject_hash(ONE, s) for s in subjects}
out['key-names'] = {'one': S.hm(ONE, 'key').hex(), 'two': S.hm(TWO, 'key').hex()}

# 2. A summary entry: its height in bytes 9 and 10, its items in ordinal order of their names.
fmt = S.dumps({'format': '1', 'kind': 'format', 'plugin': '0.3.0'})
rev = S.dumps({'client': 'c3' * 32, 'kind': 'revoked'})
heads = {}
for height in (1, 258, 65535):
    u, s = rec(ONE, 'head', {'after': '', 'height': str(height), 'nonce': NONCE},
               {'KPG.S.0001': fmt, 'KPG.S.0002': rev}, height)
    heads[str(height)] = {'id': u, 'strings': s}
u, s = rec(ONE, 'head', {'after': '', 'height': '1', 'nonce': NONCE},
           {'KPG.S.%04d' % n: S.dumps({'kind': 'removed', 'name': 'n%d' % n, 'id': '%032x' % n})
            for n in (1, 2, 10, 11, 100)}, 1)
heads['ordinal'] = {'id': u, 'strings': s}
out['heads'] = heads

# 3. The kinds the measured code never wrote, in the form STORE.md 658 gives them.
k2 = S.hm(TWO, 'key').hex()
out['format'] = dict(zip(('id', 'strings'), rec(
    ONE, 'format', {'format': '1', 'nonce': NONCE, 'plugin': '0.3.0'}, subject='format:1', number=1)))
out['format-2'] = dict(zip(('id', 'strings'), rec(
    ONE, 'format', {'format': '2', 'nonce': NONCE, 'plugin': '0.4.0'}, subject='format:2', number=2)))
out['kept'] = dict(zip(('id', 'strings'), rec(
    ONE, 'kept', {'after': '', 'key': k2, 'nonce': NONCE}, subject='key:' + k2)))
out['withdrawn'] = dict(zip(('id', 'strings'), rec(
    ONE, 'withdrawn', {'certificate': 'd4' * 32, 'nonce': NONCE}, subject='certificate:' + 'd4' * 32)))

# 4. The measured reading over small stores: what read.py answers for each.
ROOT = 'aa' * 16


def dump(entries, dels=()):
    return {'root': ROOT, 'groups': {}, 'dels': {d: 0 for d in dels},
            'entries': {u: {'strings': s, 'history': [{'strings': h} for h in hist],
                            'place': 'S', 'parent': ROOT} for (u, s, hist) in entries}}


auth = rec(ONE, 'authority', {'id': 'a1' * 32, 'nonce': NONCE, 'storeKey': ONE.hex()})
revoked = rec(ONE, 'revoked', {'client': 'c3' * 32, 'nonce': NONCE})
head = rec(ONE, 'head', {'after': '', 'height': '1', 'nonce': NONCE},
           {'KPG.S.0001': fmt, 'KPG.S.0002': rev}, 1)
edited = dict(revoked[1]); edited['KPG.Data'] = edited['KPG.Data'].replace('c3', 'c4', 1)
stranger = ('99' * 16, {'KPG.Kind': 'revoked', 'KPG.Data': '{}', 'KPG.Mac': '00'}, [])
gone = S.ident(ONE, 'head', 'store', 7, 'ab' * 32)

cases = {
    'whole': dump([auth + ([],), revoked + ([],), head + ([],)]),
    'no summary entry': dump([auth + ([],), revoked + ([],)]),
    'a revocation not restated': dump([auth + ([],), revoked + ([],), rec(
        ONE, 'head', {'after': '', 'height': '1', 'nonce': NONCE}, {'KPG.S.0001': fmt}, 1) + ([],)]),
    'edited, the written version in the history': dump(
        [auth + ([],), (revoked[0], edited, [revoked[1]]), head + ([],)]),
    'edited, no version that passes': dump([auth + ([],), (revoked[0], edited, []), head + ([],)]),
    'a stranger entry': dump([auth + ([],), revoked + ([],), head + ([],), stranger]),
    'a deleted summary entry nothing names': dump([auth + ([],), revoked + ([],), head + ([],)], [gone]),
    'no key': dump([revoked + ([],), head + ([],)]),
}
out['readings'] = {}
for name, d in cases.items():
    r = R.read(d, history=True)
    out['readings'][name] = {'dump': d, 'status': r['status'], 'served': r['served'],
                             'counts': r['counts'], 'byhist': r['byhist']}


# 5. ADDED (vectors for the writes): records in the forms STORE.md 614, 615 and 648 fix, and
# summary entries in the form of 611, each with every input written out beside its values.
def invented(label, n):
    """n bytes that stand for a value this script cannot make: SHA-256 of a text and a counter."""
    made, k = b'', 0
    while len(made) < n:
        made += hashlib.sha256(('kpg-vectors/%s/%d' % (label, k)).encode()).digest()
        k += 1
    return made[:n]


def b64(data):
    return base64.b64encode(data).decode('ascii')


def tlv(tag, *parts):
    """A DER element: its tag, its length and its parts."""
    body = b''.join(parts)
    n = len(body)
    if n < 128:
        return bytes([tag, n]) + body
    size = n.to_bytes((n.bit_length() + 7) // 8, 'big')
    return bytes([tag, 0x80 | len(size)]) + size + body


def oid(text):
    a = [int(x) for x in text.split('.')]
    body = [40 * a[0] + a[1]]
    for x in a[2:]:
        b = [x & 0x7f]
        while x > 0x7f:
            x >>= 7
            b.insert(0, 0x80 | (x & 0x7f))
        body += b
    return tlv(0x06, bytes(body))


# The base point of P-256, a point of the curve that needs no arithmetic here.
P256_G = bytes.fromhex('6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296'
                       '4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5')


def certificate(subject, issuer, serial):
    """A certificate in DER that decodes, its key the base point and its signature checked by
    nobody: bytes an admission's `der` is, and its `certificate` the SHA-256 of."""
    def name(cn):
        return tlv(0x30, tlv(0x31, tlv(0x30, oid('2.5.4.3'), tlv(0x0c, cn.encode()))))
    alg = tlv(0x30, oid('1.2.840.10045.4.3.2'))
    key = tlv(0x30, tlv(0x30, oid('1.2.840.10045.2.1'), oid('1.2.840.10045.3.1.7')),
              tlv(0x03, b'\x00\x04' + P256_G))
    validity = tlv(0x30, tlv(0x17, b'260815091422Z'), tlv(0x17, b'270815091422Z'))
    tbs = tlv(0x30, tlv(0xa0, tlv(0x02, b'\x02')), tlv(0x02, bytes([serial])), alg, name(issuer),
              validity, name(subject), key)
    signature = tlv(0x30, tlv(0x02, b'\x01'), tlv(0x02, b'\x01'))
    return tlv(0x30, tbs, alg, tlv(0x03, b'\x00' + signature))


def numbered(items):
    """A summary entry's items as its strings, numbered in ordinal order of their text, as
    store.py's Encoder numbers them."""
    return {'KPG.S.%04d' % (k + 1): S.dumps(it) for k, it in enumerate(sorted(items, key=S.dumps))}


def record(kind, d, items=(), height=0, subject=None, number=None):
    """A record as rec composes it, with its inputs."""
    subject = subject if subject is not None else S.subject_of(kind, d)
    number = number if number is not None else height
    u, s = rec(ONE, kind, d, numbered(items), height, subject, number)
    made = {'key': ONE.hex(), 'kind': kind, 'members': dict(sorted(d.items())),
            'content': s['KPG.Data'], 'check': s['KPG.Mac'], 'subject': subject, 'number': number,
            'id': u, 'strings': dict(sorted(s.items()))}
    if items:
        made['items'] = list(items)
    return made


NONCE2 = 'ffeeddccbbaa99887766554433221100'
FORMAT = {'format': '1', 'kind': 'format', 'plugin': '0.3.0'}


def authority(aid):
    """An authority's content in the form of STORE.md 614, every value but the store key
    invented."""
    return {'ca': b64(invented('ca/' + aid, 96)), 'host': b64(invented('host/' + aid, 96)),
            'id': aid, 'nonce': NONCE, 'sniKey': b64(invented('sniKey/' + aid, 32)),
            'storeKey': ONE.hex()}


# The certificates of a client: paired with the first, the second renewed from it, the third and
# the fourth renewed from the second; each admission key the SHA-256 of a certificate.
CERTS = [certificate('agent-fictitious', 'KeePassGate', n) for n in (1, 2, 3, 4)]
K1, K2, K3, K4 = [hashlib.sha256(c).hexdigest() for c in CERTS]
A1 = invented('authority-certificate/1', 32).hex()
A2 = invented('authority-certificate/2', 32).hex()


def admission(n, issuer, frm=None):
    """An admission's content in the form of STORE.md 615 for certificate n of the client."""
    d = {'admitted': '2026-08-15T09:14:22Z', 'certificate': (K1, K2, K3, K4)[n],
         'client': K1, 'der': b64(CERTS[n]), 'issuer': issuer, 'nonce': NONCE,
         'profiles': 'reader writer', 'subject': 'CN=agent-fictitious'}
    if frm is not None:
        d['from'] = frm
    return d


RECORDS_ABOUT = (
    'Records in the forms STORE.md 614, 615 and 648 fix, and summary entries in the form of '
    '611, composed by dumps, mac and ident of probe/store.py of '
    'spikes/storage/probes/store-as-a-whole on spikes/storage-survival, as rec of vectors.py '
    'beside the probe composes them, and never from this side. Each record holds the store key, '
    'its kind, the members it was composed from, its content text, its check, the subject and '
    'the number in bytes 9 and 10 its identifier was made of, its identifier, and every string '
    'of its entry. A summary entry also holds its items in the order they were given; its '
    'strings number them in ordinal order of their text, as the probe\'s Encoder numbers them. '
    'The store key is keybytes(\'one\') of store.py. The values are invented. In an authority, '
    'ca and host are 96 invented bytes each in base64 and no PKCS#12, sniKey is 32 invented '
    'bytes in base64, and id is 32 invented bytes in hexadecimal and the digest of nothing in '
    'ca: the core reads such a record as an authority, and the reading of STORE.md 614 finds '
    'the authority not whole. Every der is a certificate in DER made here that decodes, with '
    'the base point of P-256 as its key and a signature nobody checks, and every admission key '
    'is the SHA-256 of a der, so an admission reads as STORE.md 615 has it. The client paired '
    'with the first certificate, renewed it to the second, and renewed the second to the third '
    'and the fourth. Every list, in profiles, renewed and after, is sorted, as the probe writes '
    'every list. No record holds v, as STORE.md 607 has it. An item withdrawn is in the form '
    'that S29 settles for STORE.md 611, 648 and 658: it restates a single withdrawn record of '
    'this file, holding that record\'s identifier as id and its certificate, renewed and from '
    'text for text, and no from where the record holds none. The second summary entry of '
    'height 2 restates both withdrawn records of this file.')

RENEWED = ' '.join(sorted([K3, K4]))
withdrawn = record('withdrawn', {'certificate': K2, 'from': K1, 'nonce': NONCE,
                                 'renewed': RENEWED}, subject='certificate:' + K2)
withdrawn0 = record('withdrawn', {'certificate': K1, 'nonce': NONCE, 'renewed': ''},
                    subject='certificate:' + K1)


def restating(w):
    """ADDED (S29 restating): the item that restates a withdrawn record: its identifier as `id`,
    and its `certificate`, `renewed` and `from` as the record holds them."""
    item = {k: v for k, v in w['members'].items() if k in ('certificate', 'renewed', 'from')}
    return dict(item, id=w['id'], kind='withdrawn')


head1 = record('head', {'after': '', 'height': '1', 'nonce': NONCE}, [FORMAT], 1)
head1b = record('head', {'after': '', 'height': '1', 'nonce': NONCE2}, [FORMAT], 1)
after2 = ' '.join(sorted([head1['id'], head1b['id']]))
head2 = record('head', {'after': after2, 'height': '2', 'nonce': NONCE},
               [FORMAT, {'authority': A1, 'kind': 'retired'}, {'client': K1, 'kind': 'revoked'},
                restating(withdrawn)], 2)
head2w = record('head', {'after': after2, 'height': '2', 'nonce': NONCE2},
                [FORMAT, restating(withdrawn), restating(withdrawn0)], 2)


# ADDED (profile names): an addition and a removal of a profile name in the form STORE.md 616
# fixes, an addition that names the removal, and a summary entry that restates the removal.
def restating_removal(r):
    """ADDED (profile names): the item that restates a removal: its name, and its identifier as
    `id`."""
    return {'id': r['id'], 'kind': 'removed', 'name': r['members']['name']}


addition = record('profile', {'after': '', 'name': 'reader', 'nonce': NONCE})
removal = record('removed', {'name': 'reader', 'nonce': NONCE})
added_again = record('profile', {'after': removal['id'], 'name': 'reader', 'nonce': NONCE})
head1r = record('head', {'after': '', 'height': '1', 'nonce': NONCE},
                [FORMAT, restating_removal(removal)], 1)
RECORDS_ABOUT += (
    ' An addition and a removal of a profile name are in the form STORE.md 616 fixes, each '
    'naming reader: the addition holds name and after, and the removal name alone. The second '
    'addition names the removal of this file in after, and the summary entry that restates a '
    'removal holds the item removed of STORE.md 611, with the removal\'s name and its identifier '
    'as id.')


# ADDED (rules per profile): versions of a profile's row at a node and of the node's switch, in the
# objects and texts of the rows switched on above, and a summary entry that restates them.
N = 'bb' * 16       # the model's node N, a group directly under the root ROOT
PF, PG, SW = 'rule:%s/pf' % N, 'rule:%s/pg' % N, 'switch:' + N
ROW_RU, ROW_R, ROW_U, ROW_EMPTY = (S.doc(M.row(x)) for x in ('rw', 'r', 'w', ''))
ROW_OFF = S.doc(M.ROW_OFF)
SW_DENY, SW_INHERIT = (S.doc(('sws', frozenset([x]))) for x in ('deny', 'inherit'))


def rule_record(obj, text, after=(), nonce=NONCE):
    """A version of a row or of a switch as `record` composes it. For a row, its subject and its
    number are those its identifier holds in the form `split`: the subject hash over the object
    before the slash, and the profile's tag read as a number."""
    made = record('version', {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': nonce,
                              'object': obj})
    node, _, profile = obj.partition('/')
    if profile:
        made['subject'], made['number'] = node, int(S.profile_tag(ONE, profile), 16)
    assert made['id'] == S.ident(ONE, 'version', made['subject'], made['number'], made['check'])
    return made


def restating_version(v):
    """The item that restates a version: its identifier as `id`, its object, after and text."""
    m = v['members']
    return {'after': m['after'], 'doc': m['doc'], 'id': v['id'], 'kind': 'version',
            'object': m['object']}


granting = rule_record(PF, ROW_RU)
emptied = rule_record(PG, ROW_EMPTY)
taken_off = rule_record(PF, ROW_OFF)
refusing = rule_record(SW, SW_DENY)
inheriting = rule_record(SW, SW_INHERIT)
settled = rule_record(PF, ROW_R, after=[granting['id'], taken_off['id']])
head1v = record('head', {'after': '', 'height': '1', 'nonce': NONCE},
                [FORMAT] + [restating_version(x) for x in (granting, emptied, taken_off, refusing,
                                                            inheriting)], 1)
RECORDS_ABOUT += (
    ' A version of a profile\'s row at a node and a version of a node\'s switch are records of '
    'kind version, with the store\'s rows switched on as decided and the identifier in the form '
    'split of store.py. The object of a row is rule:, the node\'s identifier in 32 lowercase '
    'hexadecimal characters, a slash and the profile\'s name; the object of a switch is switch: '
    'and the node\'s identifier. Each text is store.py\'s doc over the model\'s value with the '
    'model\'s letters read and write written R and U: row: and the letters granted, row: alone '
    'for an empty rule, off: for the rule taken off, sw:deny and sw:inherit. The model has no '
    'other letter. The identifier of a row\'s version holds the subject hash over the object '
    'before the slash and, in bytes 9 and 10, the profile\'s tag, the first two bytes of '
    'HMAC-SHA256 under the store key over profile, a line feed and the name; its subject and its '
    'number are those, the number the tag read as a big-endian number, and profileTags holds '
    'each tag. The identifier of a switch\'s version holds the subject hash over its object and '
    'zero. The node is bb repeated 16 times and the profiles are the model\'s pf and pg. The '
    'versions of pf that grant and that take the rule off were made apart, as were the two '
    'versions of the switch; the summary entry of height 1 that restates rules holds the item '
    'format and an item version for each of those four and for the empty rule of pg, each with '
    'the version\'s identifier as id and its object, after and text; and a further version of pf '
    'names in after the two versions of pf it replaces. The model holds a switch under an object '
    'of its own name, sw: and the node, which store.py writes as switch: and the node\'s '
    'identifier, and its acts compose a version at the write, with no buffer between a press '
    'and the write; the records here are composed by rec and by no act.')
out['records'] = {'about': RECORDS_ABOUT, 'key': ONE.hex(), 'records': {
    'authority': record('authority', authority(A1)),
    'retired': record('retired', {'authority': A1, 'nonce': NONCE}),
    'retired, holding the store key': record(
        'retired', {'authority': A1, 'nonce': NONCE, 'storeKey': ONE.hex()}),
    'client, paired': record('client', admission(0, A1)),
    'client, renewed': record('client', admission(2, A1, K2)),
    'revoked': record('revoked', {'client': K1, 'nonce': NONCE}),
    'withdrawn': withdrawn,
    'withdrawn, nothing renewed': withdrawn0,
    'head of height 1': head1,
    'head of height 1, made apart': head1b,
    'head of height 2': head2,
    'head of height 2, restating both withdrawn records': head2w,
    'profile': addition,                            # ADDED (profile names): the four below
    'profile, naming a removal': added_again,
    'removed': removal,
    'head of height 1, restating a removal': head1r,
    'version, a row that grants': granting,        # ADDED (rules per profile): the seven below
    'version, an empty rule': emptied,
    'version, a rule taken off': taken_off,
    'version, the switch refusing': refusing,
    'version, the switch inheriting': inheriting,
    'version, a row naming the versions it replaces': settled,
    'head of height 1, restating rules': head1v,
}, 'profileTags': {p: S.profile_tag(ONE, p) for p in M.PROFS}}     # ADDED (rules per profile)


# 6. ADDED (vectors for the writes): what read.py answers about authorities, admissions and
# refusals, over small stores of records in the forms STORE.md fixes.
def answer(r):
    """read.py's answer about authorities, admissions and refusals, each set sorted."""
    return {'status': r['status'], 'served': r['served'], 'auths': r['auths'],
            'adm': {c: sorted(p) for c, p in sorted(r['adm'].items())},
            'valid': {c: sorted(p) for c, p in sorted(r['valid'].items())},
            'refusals': sorted([k, s] for (k, s) in r['refusals']), 'names': r['names']}


def files(d):
    """A dump in the shape of the product's reading vectors: deletion records and entries."""
    return {'deletions': sorted(d['dels']),
            'entries': {u: {'strings': dict(sorted(e['strings'].items())),
                            'history': [dict(sorted(h['strings'].items()))
                                        for h in e['history']]}
                        for u, e in sorted(d['entries'].items())}}


def summary(*items):
    return rec(ONE, 'head', {'after': '', 'height': '1', 'nonce': NONCE},
               numbered((FORMAT,) + items), 1) + ([],)


def entry(kind, d):
    return rec(ONE, kind, d) + ([],)


MEANINGS_ABOUT = (
    'What probe/read.py of spikes/storage/probes/store-as-a-whole on spikes/storage-survival '
    'answers with its history rule on for small stores composed by vectors.py beside the probe '
    'with store.py\'s functions, and never from this side: status, served, auths, adm, valid, '
    'refusals and names, each as read.py names it. status is read.py\'s own word: ok, the state '
    'the product names Whole; incomplete, which it names Stopped; nostore, which it names None. '
    'served is the single authority standing in a store that is ok, or null; auths every '
    'authority standing; adm each certificate admitted under the authority served, with its '
    'profiles; valid each certificate whose record is in force, not revoked and not under a '
    'retired issuer, whatever is served; refusals each revocation and retirement in force, as a '
    'kind and a subject; names the profile names on the list. Every record is in the form '
    'STORE.md 614 and 615 fix and holds no v, and every summary entry holds the item format of '
    'STORE.md 611; read.py reads of each only the members it knows. Where read.py does not read '
    'what STORE.md fixes, the answer is read.py\'s: it judges no authority whole (STORE.md 614), '
    'and every authority here holds the invented ca, host and sniKey of the records file, so a '
    'reading that judges an authority would find each damaged and serve none; it holds no der '
    'to its certificate, as each der here is; and it asks no summary entry for its item format. '
    'The first authority, the admission of the client that paired, the retirement and the '
    'revocation are the records of the records file, under the same identifiers; the second '
    'authority is made as the first is. The admission names the profiles reader and writer, and '
    'a profile record puts reader on the list. Every entry\'s history is empty.')

AUTH1, AUTH2 = entry('authority', authority(A1)), entry('authority', authority(A2))
PROFILE = entry('profile', {'after': '', 'name': 'reader', 'nonce': NONCE})
ADMITTED = entry('client', admission(0, A1))
REVOKED = entry('revoked', {'client': K1, 'nonce': NONCE})
RETIRED = entry('retired', {'authority': A1, 'nonce': NONCE})
RETIRING = {'authority': A1, 'kind': 'retired'}
REVOKING = {'client': K1, 'kind': 'revoked'}
# ADDED (profile names): the removal of reader and the addition naming it of the records file, and
# a second removal and a second addition with an empty after, made apart under NONCE2
REMOVED = entry('removed', {'name': 'reader', 'nonce': NONCE})
REMOVED_APART = entry('removed', {'name': 'reader', 'nonce': NONCE2})
ADDED_AGAIN = entry('profile', {'after': REMOVED[0], 'name': 'reader', 'nonce': NONCE})
ADDED_APART = entry('profile', {'after': '', 'name': 'reader', 'nonce': NONCE2})
REMOVING = {'id': REMOVED[0], 'kind': 'removed', 'name': 'reader'}
REMOVING_APART = {'id': REMOVED_APART[0], 'kind': 'removed', 'name': 'reader'}
MEANINGS_ABOUT += (
    ' The stores about profile names are the store of an admission with further records or '
    'deletion records: the removal of reader and the addition naming it are those of the records '
    'file, under the same '
    'identifiers, and a second removal of reader and a second addition of it with an empty after '
    'differ from the first only in their nonce. An addition and a removal are in the form '
    'STORE.md 616 fixes. A removal is known as a record, as an item of a summary entry or as a '
    'deletion record, and names answers whether reader is on the list.')

meanings = {
    'an authority alone': dump([AUTH1, summary()]),
    'two authorities': dump([AUTH1, AUTH2, summary()]),
    'a retirement by record': dump([AUTH1, AUTH2, RETIRED, summary(RETIRING)]),
    'a retirement by item': dump([AUTH1, AUTH2, summary(RETIRING)]),
    'a retirement by deletion record': dump([AUTH1, AUTH2, summary()], [RETIRED[0]]),
    'an authority beside a deletion record naming it': dump([AUTH1, summary()], [AUTH1[0]]),
    'an admission': dump([AUTH1, PROFILE, ADMITTED, summary()]),
    'a revocation by record': dump([AUTH1, PROFILE, ADMITTED, REVOKED, summary(REVOKING)]),
    'a revocation by item': dump([AUTH1, PROFILE, ADMITTED, summary(REVOKING)]),
    'a revocation by deletion record': dump([AUTH1, PROFILE, ADMITTED, summary()],
                                            [REVOKED[0]]),
    'an admission beside a deletion record naming it': dump([AUTH1, PROFILE, ADMITTED, summary()],
                                                            [ADMITTED[0]]),
    'an admission under a retired issuer': dump([AUTH2, PROFILE, ADMITTED, RETIRED,
                                                 summary(RETIRING)]),
    'an admission under an authority that is not served': dump([AUTH2, PROFILE, ADMITTED,
                                                                summary()]),
    # ADDED (profile names)
    'a name removed by record': dump([AUTH1, PROFILE, ADMITTED, REMOVED, summary(REMOVING)]),
    'a name removed by item': dump([AUTH1, PROFILE, ADMITTED, summary(REMOVING)]),
    'a name removed by deletion record': dump([AUTH1, PROFILE, ADMITTED, summary()],
                                              [REMOVED[0]]),
    'a name added again, naming its removal': dump([AUTH1, PROFILE, ADMITTED, REMOVED,
                                                    ADDED_AGAIN, summary(REMOVING)]),
    'an addition naming a single removal of two': dump(
        [AUTH1, PROFILE, ADMITTED, REMOVED, REMOVED_APART, ADDED_AGAIN,
         summary(REMOVING, REMOVING_APART)]),
    'an addition beside a deletion record naming it': dump([AUTH1, PROFILE, ADMITTED, summary()],
                                                           [PROFILE[0]]),
    'a name added twice': dump([AUTH1, PROFILE, ADDED_APART, ADMITTED, summary()]),
}
stores = {}
for name, d in sorted(meanings.items()):
    stores[name] = dict(files(d), **answer(R.read(d, history=True)))
out['meanings'] = {'about': MEANINGS_ABOUT, 'key': ONE.hex(), 'meanings': stores}


# ADDED (rules per profile): what read.py answers about rules, with the rows switched on, over small
# stores of versions in the objects and texts of the records file, in a tree of three groups.
BELOW = 'cc' * 16       # a group directly under N
TREE = {N: ROOT, BELOW: N}
BPF, PX = 'rule:%s/pf' % BELOW, 'rule:%s/px' % N
NONCE3 = invented('nonce/3', 16).hex()


def ruled(obj, text, after=(), nonce=NONCE):
    return entry('version', {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': nonce,
                             'object': obj})


def apart(obj, *texts):
    """Versions of an object made apart, each with a nonce of its own."""
    return [ruled(obj, t, nonce=n) for t, n in zip(texts, (NONCE, NONCE2, NONCE3))]


def restates(v):
    d = json.loads(v[1]['KPG.Data'])
    return {'after': d['after'], 'doc': d['doc'], 'id': v[0], 'kind': 'version',
            'object': d['object']}


ROOT_PF, ROOT_PG = ruled('rule:%s/pf' % ROOT, ROW_RU), ruled('rule:%s/pg' % ROOT, ROW_RU)


def ruled_store(versions, dels=(), restated=None, more=()):
    """The authority, the root's rows of pf and pg granting RU, the versions given, and a summary
    entry restating every version present that nothing names, or the versions given."""
    present = [ROOT_PF, ROOT_PG] + list(versions)
    if restated is None:
        named = set(dels)
        for v in present:
            named |= set(json.loads(v[1]['KPG.Data'])['after'].split())
        restated = [v for v in present if v[0] not in named]
    d = dump([AUTH1] + present + list(more) + [summary(*(restates(v) for v in restated))], dels)
    d['groups'] = {g: {'parent': p} for g, p in TREE.items()}
    return d


def value(obj, f):
    """read.py's value of a row or a switch: whether it refuses, the letters all granting versions
    hold, and whether a version takes the rule off."""
    if obj.startswith('switch:'):
        return {'refuses': R.switch_refuses(f)}
    if f[0] == 'const':
        return {'grants': None, 'refuses': True, 'takenOff': False}
    return {'grants': None if f[1] is None else letters(f[1]), 'refuses': False, 'takenOff': f[2]}


def rules(r, nodes):
    """read.py's answer about rules: what each node gives each profile, the standing versions of
    each row and switch, the objects a deletion record shows lost, and the value of each."""
    return {'gives': {n: {p: letters(x.split('.')[1] for x in r['words_raw'].get(n, ())
                                     if x.startswith(p + '.')) for p in M.PROFS} for n in nodes},
            'lost': sorted(r['lost']),
            'standing': dict(sorted(r['standing'].items())),
            'values': {o: value(o, f) for o, f in sorted(r['row_values'].items())}}


DENY = [ruled(SW, SW_DENY)]
LOST = ruled(PF, ROW_R)         # a version of pf that a deletion record names
rule_meanings = {
    'a row that grants': [ruled(PF, ROW_R)],
    'an empty rule': [ruled(PF, ROW_EMPTY)],
    'a rule taken off': [ruled(PF, ROW_OFF)],
    'a row never made': [ruled(PG, ROW_R)],
    'two versions that grant': apart(PF, ROW_R, ROW_RU),
    'a version that grants beside a rule taken off': apart(PF, ROW_R, ROW_OFF),
    'two versions that grant beside a rule taken off': apart(PF, ROW_R, ROW_RU, ROW_OFF),
    'three versions that grant, with no letter all of them hold': apart(PF, ROW_RU, ROW_R, ROW_U),
    'two versions that take the rule off': apart(PF, ROW_OFF, ROW_OFF),
    'an empty rule beside a version that grants': apart(PF, ROW_EMPTY, ROW_R),
}
rule_stores = {}
for name, versions in rule_meanings.items():
    rule_stores['rules: ' + name] = ruled_store(versions)
    rule_stores['rules: %s, under a switch that refuses' % name] = ruled_store(DENY + versions)
rule_stores.update({
    'rules: versions of the switch that differ': ruled_store(apart(SW, SW_DENY, SW_INHERIT)
                                                             + [ruled(PF, ROW_R)]),
    'rules: a row lost by a deletion record': ruled_store([ruled(PG, ROW_R)], [LOST[0]]),
    'rules: a switch lost by a deletion record': ruled_store([ruled(PF, ROW_R)],
                                                             [ruled(SW, SW_INHERIT)[0]]),
    'rules: a deletion record of a version that a standing version names': ruled_store(
        [ruled(PF, ROW_RU, after=[LOST[0]])], [LOST[0]]),
    'rules: a deletion record of a version the summary entry restates': ruled_store(
        [], [LOST[0]], restated=[ROOT_PF, ROOT_PG, LOST]),
    'rules: a deletion record of a row of a name the file does not know': ruled_store(
        [], [ruled(PX, ROW_R)[0]]),
    'rules: a deletion record of a row of a name an addition makes known': ruled_store(
        [], [ruled(PX, ROW_R)[0]], more=[entry('profile', {'after': '', 'name': 'px',
                                                           'nonce': NONCE})]),
    'rules: a version present beside a deletion record naming it': ruled_store([LOST], [LOST[0]]),
    'rules: a version present beside a deletion record naming it, and a version that grants':
        ruled_store([LOST, ruled(PF, ROW_RU, nonce=NONCE2)], [LOST[0]]),
    'rules: a version present beside a deletion record naming it, and a version naming it':
        ruled_store([LOST, ruled(PF, ROW_RU, after=[LOST[0]])], [LOST[0]]),
    'rules: a limit at N above a group whose own row grants': ruled_store(
        apart(PF, ROW_R, ROW_OFF) + [ruled(BPF, ROW_RU)]),
    'rules: limits at N and at the group below it': ruled_store(
        apart(PF, ROW_R, ROW_OFF) + apart(BPF, ROW_U, ROW_OFF)),
    'rules: a version that grants beside a rule taken off, at the root': ruled_store(
        [ruled('rule:%s/pf' % ROOT, ROW_OFF, nonce=NONCE2)]),
})
for name, d in sorted(rule_stores.items()):
    r = R.read(d, history=True)
    out['meanings']['meanings'][name] = dict(
        files(d), **answer(r), rules=rules(r, [ROOT, N, BELOW]),
        tree={'groups': {g: p for g, p in sorted(TREE.items())}, 'root': ROOT})
out['meanings']['about'] += (
    ' The stores whose names begin with rules: are about a profile\'s rule at a node, read by '
    'read.py with the store\'s rows switched on as decided and the identifier of a row\'s '
    'version in the form split of store.py, the objects and texts those of the records file. '
    'Each holds the first authority of the records file, a version of each of the rows of pf '
    'and pg at the root granting RU, the versions its name gives, and a summary entry of height '
    '1. The summary entry restates every version present that no deletion record and no '
    'version names, but in the store of a deletion record of a version the summary entry '
    'restates, where it restates the root\'s two versions and the version the deletion record '
    'names. Its tree, as tree holds it by identifiers, is the root, aa repeated 16 times, the '
    'group N, bb repeated 16 times, directly under the root, and a group, cc repeated 16 times, '
    'directly under N; no node holds anything of the plugin. Every store answers for all three, '
    'so a limit at N shows in what the group under N is given. A store with a switch that '
    'refuses holds a version of N\'s switch refusing; every other switch has no version, which '
    'inherits. Versions made apart differ in their nonce. Beside the answer of the other stores, '
    'rules holds what read.py answers about '
    'rules: gives, what each node gives pf and pg as letters in the order of CRUSDLAP, read.py\'s '
    'words with the model\'s read and write written R and U; standing, the texts of the versions '
    'of each row and switch that stand; lost, each row or switch a deletion record shows lost; '
    'and values, read.py\'s value of each row and switch: whether it refuses, and for a row the '
    'letters all granting versions hold, null where no version grants, and whether a version '
    'takes the rule off. A deletion record tells of a row\'s version only where the name of its '
    'profile is known to the file, from an addition, a removal or a row\'s object: px is known '
    'in a single store, by an addition that puts it on the list. The model holds a switch under '
    'an object of its own name, sw: and the node, which store.py writes as switch: and the '
    'node\'s identifier, and its acts compose a version at the write, with no buffer between a '
    'press and the write; these stores are composed here and by no act.')


# 7. ADDED (vectors for the writes): the file after each act of a fixed sequence the model plays,
# written out by store.py's Encoder as the family's runs write it, and read by read.py.
def described(enc, eid):
    """A record of the model as the Encoder writes it: identifier, kind, subject and number."""
    u, title, s, prot = enc.entry(eid)
    kind, d = s['KPG.Kind'], json.loads(s['KPG.Data'])
    made = {'id': u, 'kind': kind, 'subject': S.subject_of(kind, d),
            'number': int(d['height']) if kind == 'head' else 0}
    if kind == 'version':   # ADDED (rules per profile): its object, and what its identifier holds
        node, _, profile = d['object'].partition('/')
        made['object'] = d['object']
        if profile:
            made['subject'] = node
            made['number'] = int(S.profile_tag(enc.key(enc.W.EL[eid]), profile), 16)
    return made


def by_record(x):
    return (x['kind'], x['subject'], x['number'], x['id'])


def state(W, enc, i):
    """File i of the model as the Encoder writes it, described, and what read.py answers for
    it."""
    rep = W.reps[i]
    eids = sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid and ok)
    written = [enc.entry(eid) for eid in eids]
    d = dump([(u, s, []) for (u, title, s, prot) in written], [enc.u(x) for x in rep.dels])
    d['groups'] = {N: {'parent': ROOT}}     # ADDED (rules per profile): the model's node N
    r = R.read(d, history=True)
    heads = []
    for (u, title, s, prot) in written:
        if s['KPG.Kind'] == 'head':
            h = json.loads(s['KPG.Data'])
            heads.append({'id': u, 'height': int(h['height']), 'after': len(h['after'].split()),
                          'items': [json.loads(s[k]) for k in sorted(s)
                                    if k.startswith('KPG.S.')]})
    return {'copy': i, 'keys': sorted(set(S.keybytes(W.EL[eid].key).hex() for eid in eids)),
            'present': sorted((described(enc, eid) for eid in eids), key=by_record),
            'deletions': sorted((described(enc, x) for x in rep.dels), key=by_record),
            'heads': sorted(heads, key=lambda h: (h['height'], h['id'])),
            'answer': dict(answer(r), rules=rules(r, [ROOT, N])),    # ADDED (rules per profile)
            'file': files(d)}


def play(nrep, steps):
    """The model's own store, then each (command, act) of steps played by the model, each with
    the file of the copy it wrote and the deletion records it added there."""
    W = M.World(nrep, B.FLAGS)
    W.maxclients = 4
    enc = S.Encoder(W, {'root': ROOT, 'N': N})     # ADDED (rules per profile): N
    made = {'start': state(W, enc, 0), 'steps': []}
    for command, op in steps:
        i = op[2] if op[0] == 'merge' else op[1]
        before = set(W.reps[i].dels)
        x = dict(command=command, act=list(op), played=W.step(op), **state(W, enc, i))
        x['deleted'] = sorted((described(enc, u) for u in set(W.reps[i].dels) - before),
                              key=by_record)
        made['steps'].append(x)
    return made


WRITES_ABOUT = (
    'The file after each act of fixed sequences that the model plays, World.step and '
    'World.act of model/crdt_model.py of spikes/storage/probes/store-as-a-whole on '
    'spikes/storage-survival under the FLAGS of probe/bridge.py, the store as decided, written '
    'out as entries by store.py\'s Encoder as the family\'s runs write them, and read by read.py '
    'with its history rule on; made by vectors.py beside the probe and never from this side. '
    'Each sequence starts from the store the model\'s World starts with, given as start: the '
    'authority a0, the profile name pf, the admission of the client c0 under a0 with the '
    'profile pf alone, a version of the row of each of the profiles pf and pg at the root '
    'granting read, and a summary entry of height 1, each under the key label K; the two copies '
    'of the second and of the third sequence start as the same file. A pairing admits its '
    'client with the profiles pf and pg, and a renewal with the profiles of the admission '
    'renewed. The first sequence raises the '
    'model\'s maxclients to 4, as growth.py raises it, so that three pairings fit beside c0. '
    'The first Create of the sequence asked for, on a '
    'database with no store, is not played. On a file with no store the model\'s Create writes '
    'its authority under a new key label, K and a number, while its pairing writes every '
    'admission under the label K whatever key the file holds; played from emptied files, every '
    'admission after that Create was under a key the file does not hold, passed over by '
    'read.py, and the revocation did not apply. The Create after Delete is played: it takes the '
    'key the retirement holds. Each step names the command, the model\'s act, the copy whose '
    'file follows, whether the act applied, the store keys of the records present, every record '
    'present and every deletion record in the file by identifier, kind, subject and number, the '
    'deletion records the step added, each summary entry present with its identifier, its '
    'height, the count of its after and its items, read.py\'s answer as the meanings file gives '
    'it, and the file itself. Synchronize is the model\'s kp_sync merge of a copy into the other, '
    'which writes the copy merged into alone: copy 1 into copy 0 in the second sequence, and in '
    'the third copy 1 into copy 0 and then copy 0 into copy 1. The third sequence revokes the '
    'client c0 in copy 0 and renews its certificate in copy 1, the model\'s renewcert, which '
    'admits the certificate c0.1 under the client key c0 beside the admission of c0, before the '
    'copies are synchronized. The records hold v, the model\'s short names (a0, c1), nonces '
    'derived from the model\'s elements and store keys derived from its labels, so the product '
    'holds them by shape and by answer. What the model writes otherwise than STORE.md fixes: no '
    'version record at Create and no item format in any summary entry (650, 611); an authority '
    'holding id and storeKey alone (614); an admission without der, subject and admitted (615), '
    'and a renewal\'s without from as well (648); a revocation that also holds certificate (615); '
    'and a retirement for keeping an authority that does not hold the store key (614).')
WRITES_ABOUT += (       # ADDED (profile names)
    ' The fourth sequence plays the model\'s padd and prem, the commands Add and Remove of a '
    'profile name, on two copies that start as the same file: pf removed in copy 0 and added '
    'again there, naming that removal; pf removed in copy 1; pg added in each copy; a '
    'Synchronize of copy 1 into copy 0 and then of copy 0 into copy 1; pf added again in copy '
    '0, naming both removals; and the two Synchronize again in the same order. Once the copies '
    'meet, pf is off the list in both and pg on it by two additions, and once pf is added again '
    'and synchronized, it is on the list in both, and both copies hold the same file. The '
    'model\'s pairing writes no addition for a '
    'name it approves, which STORE.md 627 has a pairing write: no pairing here puts pg on the '
    'list.')

out['writes'] = {'about': WRITES_ABOUT, 'sequences': {
    'one copy': play(1, [('Pair', ('pair', 0)), ('Pair', ('pair', 0)),
                         ('Revoke', ('revoke', 0, 'c1')), ('Recreate', ('recreate', 0)),
                         ('Pair', ('pair', 0)), ('Delete', ('delauth', 0)),
                         ('Create', ('create', 0))]),
    'two copies': play(2, [('Recreate', ('recreate', 0)), ('Recreate', ('recreate', 1)),
                           ('Synchronize', ('merge', 'kp_sync', 0, 1)), ('Keep', ('keep', 0))]),
    # ADDED (a revocation beside a renewal): c0, paired in the store both copies start with
    'two copies, revoked in the first and renewed in the second': play(2, [
        ('Revoke', ('revoke', 0, 'c0')), ('Renew', ('renewcert', 1, 'c0')),
        ('Synchronize', ('merge', 'kp_sync', 0, 1)), ('Synchronize', ('merge', 'kp_sync', 1, 0))]),
    # ADDED (profile names): pf removed in each copy and added again in the first, pg added in each
    'two copies, a name removed in each and added again in the first, another added in each':
        play(2, [
            ('Remove', ('prem', 0, 'pf')), ('Add', ('padd', 0, 'pf')),
            ('Remove', ('prem', 1, 'pf')),
            ('Add', ('padd', 0, 'pg')), ('Add', ('padd', 1, 'pg')),
            ('Synchronize', ('merge', 'kp_sync', 0, 1)),
            ('Synchronize', ('merge', 'kp_sync', 1, 0)),
            ('Add', ('padd', 0, 'pf')),
            ('Synchronize', ('merge', 'kp_sync', 0, 1)),
            ('Synchronize', ('merge', 'kp_sync', 1, 0))]),
}}


# ADDED (rules per profile): the sequences of measured-rules-per-profile 4, 5 and 7 over two
# copies, by the model's acts on a single row, on a node's switch and on a single version of a row,
# and its Synchronize; a base in copy 0 synchronized into copy 1, each copy's acts, then both ways.
# The model's nodes by their identifiers, and the tree they are in.
out['writes'].update(nodes={'N': N, 'root': ROOT}, tree={'groups': {N: ROOT}, 'root': ROOT})
INTO_1, INTO_0 = ('merge', 'kp_sync', 1, 0), ('merge', 'kp_sync', 0, 1)


def row_act(gate, node, profile, model_letters):
    return ('row', gate, node, profile, model_letters)


def switch_act(gate, value):
    return ('switch', gate, 'N', value)


def drop_act(gate, which):
    return ('drop', gate, 'N', 'pf', which, 'again')


def command(op):
    if op[0] == 'merge':
        return 'Synchronize'
    if op[0] == 'row':
        return 'Remove row' if op[4] == 'off' else 'Set row'
    return {'switch': 'Set switch', 'drop': 'Remove version'}[op[0]]


ROOT_RW = [row_act(0, 'root', 'pf', 'rw'), row_act(0, 'root', 'pg', 'rw')]
BOTH = ROOT_RW + [row_act(0, 'N', 'pf', ''), INTO_1, row_act(0, 'N', 'pf', 'r'),
                  row_act(1, 'N', 'pf', 'rw'), INTO_0, INTO_1]
RULE_SEQUENCES = [
    ('A1', 'pf narrowed in the first copy and pg in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'rw'), row_act(0, 'N', 'pg', 'rw'), INTO_1,
                row_act(0, 'N', 'pf', 'r'), row_act(1, 'N', 'pg', 'r')]),
    ('A2', 'pf widened in the first copy and pg in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'), INTO_1,
                row_act(0, 'N', 'pf', 'rw'), row_act(1, 'N', 'pg', 'rw')]),
    ('A3', 'pf widened in the first copy, pg narrowed in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'rw'), INTO_1,
                row_act(0, 'N', 'pf', 'rw'), row_act(1, 'N', 'pg', 'r')]),
    ('A4', 'pf removed in the first copy, pg widened in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'), INTO_1,
                row_act(0, 'N', 'pf', 'off'), row_act(1, 'N', 'pg', 'rw')]),
    ('A5', 'pf emptied in the first copy, pg removed in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'), INTO_1,
                row_act(0, 'N', 'pf', ''), row_act(1, 'N', 'pg', 'off')]),
    ('A6', 'pf widened in the first copy, a row for pg made in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), INTO_1,
                row_act(0, 'N', 'pf', 'rw'), row_act(1, 'N', 'pg', 'w')]),
    ('A7', 'under a switch that refuses, pf widened in the first copy, pg removed in the second',
     ROOT_RW + [switch_act(0, 'deny'), row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'),
                INTO_1, row_act(0, 'N', 'pf', 'rw'), row_act(1, 'N', 'pg', 'off')]),
    ('5', 'pf set to R in the first copy and to RU in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', ''), INTO_1,
                row_act(0, 'N', 'pf', 'r'), row_act(1, 'N', 'pf', 'rw')]),
    ('D1', 'both copies hold R and RU for pf, the first removes R', BOTH + [drop_act(0, 0)]),
    ('D2', 'both copies hold R and RU for pf, the first removes R, the second sets U',
     BOTH + [drop_act(0, 0), row_act(1, 'N', 'pf', 'w')]),
    ('D3', 'both copies hold an empty rule and U for pf, the first removes U, the second the '
           'empty rule',
     [row_act(0, 'N', 'pf', 'r'), INTO_1, row_act(0, 'N', 'pf', ''), row_act(1, 'N', 'pf', 'w'),
      INTO_0, INTO_1, drop_act(0, 1), drop_act(1, 0)]),
    ('W1', 'the switch turned on in the first copy, on and then off in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), INTO_1,
                switch_act(0, 'deny'), switch_act(1, 'deny'), switch_act(1, 'inherit')]),
    ('W2', 'the switch turned on in the first copy, pf widened in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'), INTO_1,
                switch_act(0, 'deny'), row_act(1, 'N', 'pf', 'rw')]),
    ('W3', 'under a switch that refuses, pf removed in the first copy, the switch turned off in '
           'the second',
     ROOT_RW + [switch_act(0, 'deny'), row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pg', 'r'),
                INTO_1, row_act(0, 'N', 'pf', 'off'), switch_act(1, 'inherit')]),
    ('W4', 'the switch turned on in the first copy, a row for pg made in the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), INTO_1,
                switch_act(0, 'deny'), row_act(1, 'N', 'pg', 'r')]),
    ('W5', 'under a switch that inherits, pf removed in the first copy, the switch turned on in '
           'the second',
     ROOT_RW + [row_act(0, 'N', 'pf', 'r'), INTO_1,
                row_act(0, 'N', 'pf', 'off'), switch_act(1, 'deny')]),
    ('W6', 'under a switch that refuses, pf removed and synchronized, removed again in the first '
           'copy, the switch turned off in the second',
     ROOT_RW + [switch_act(0, 'deny'), row_act(0, 'N', 'pf', 'r'), row_act(0, 'N', 'pf', 'off'),
                INTO_1, row_act(0, 'N', 'pf', 'off'), switch_act(1, 'inherit')]),
    ('W7', 'under a switch that refuses, pf removed in the first copy, the switch turned off and '
           'pf removed in the second',
     ROOT_RW + [switch_act(0, 'deny'), row_act(0, 'N', 'pf', 'r'), INTO_1,
                row_act(0, 'N', 'pf', 'off'), switch_act(1, 'inherit'),
                row_act(1, 'N', 'pf', 'off')]),
    ('W8', 'the empty rule of pf at N removed in the first copy, the root widened for pf in the '
           'second',
     [row_act(0, 'N', 'pf', ''), INTO_1,
      row_act(0, 'N', 'pf', 'off'), row_act(1, 'root', 'pf', 'rw')]),
]
for label, what, ops in RULE_SEQUENCES:
    out['writes']['sequences']['rules, %s: %s' % (label, what)] = play(
        2, [(command(op), op) for op in ops + [INTO_0, INTO_1]])
out['writes']['about'] += (
    ' Every sequence plays with the store\'s rows switched on as decided, as '
    'bridge.rows(\'empty\') switches them, the identifier of a row\'s version in the form split '
    'of store.py, and the texts and objects of the records file: the start holds a version of the '
    'row of pf and of pg at the root, row:R. The model\'s node N is bb repeated 16 times, a group '
    'directly under the root, aa repeated 16 times, as nodes and tree give them; the root and N '
    'are the only nodes. Every answer adds rules, read.py\'s answer about rules at the root and '
    'at N as the meanings file gives it. A version present or named by a deletion record adds its '
    'object, and for a row its subject and its number are those its identifier holds, as the '
    'records file gives them. The sequences whose names begin with rules and a comma are those of '
    'measured-rules-per-profile sections 4, 5 and 7 that run over two copies, each named by its '
    'label there, 5 for the two copies of section 5: A1 to A7, 5, D1 to D3, and W1 to W8. Each '
    'plays a base in copy 0, a Synchronize into copy 1, the acts of each copy apart, and then a '
    'Synchronize of copy 1 into copy 0 and of copy 0 into copy 1. Where the report sets several '
    'rows in a single act, each row is an act of its own, in the order of the profiles\' names; '
    'an act of the report that changes no row is not played, as the root grants read from the '
    'start; and D1 to D3 remove a version in the form again, which STORE.md 617 fixes. The '
    'commands: Set row is the model\'s row act with letters, an empty rule where it holds none; '
    'Remove row its row act with off, which writes an empty rule where the copy reads the node\'s '
    'switch as refusing, writes nothing where the row is an empty rule there already, and takes '
    'the rule off where the switch inherits; Set switch its switch act with deny or inherit; '
    'Remove version its drop act, which writes again each version left, naming the version '
    'removed, the version left and everything each of them names. An act\'s letters are the '
    'model\'s r and w, which the texts write R and U, and its node is the model\'s name. A step '
    'whose act writes nothing has played false and the copy\'s file as it stood: the second '
    'removal in W6. At the end of W8, N gives pf R and U, which neither copy gave before the '
    'copies met, as measured. Left out are the sequences of section 5 over three copies, D4 and '
    'D5, and W9, which writes a node\'s rule whole. The model holds a switch under an object of '
    'its own name, sw: and the node, which store.py writes as switch: and the node\'s identifier; '
    'and its acts compose a version at the write, with no buffer: an act reads the copy\'s file '
    'as it stands when the act is played and writes at once, where STORE.md 629 has a rule set in '
    'a node\'s dialog written at the dialog\'s OK.')



def product(readings):
    """The file the product keeps: each store's entries and deletion records, and the answer."""
    made = {}
    for name in sorted(readings):
        r = readings[name]
        made[name] = {
            'state': STATES[r['status']],
            'damage': r['counts']['damage'],
            'passed': r['counts']['passed'],
            'byHistory': r['counts']['byhistory'],
            'deletions': sorted(r['dump']['dels']),
            'entries': {u: {'strings': dict(sorted(e['strings'].items())),
                            'history': [dict(sorted(h['strings'].items())) for h in e['history']]}
                        for u, e in sorted(r['dump']['entries'].items())}}
    return {'about': ABOUT, 'key': ONE.hex(), 'readings': made}


sys.stdout.reconfigure(newline='\n')
if sys.argv[1:] == ['--readings']:
    json.dump(product(out['readings']), sys.stdout, indent=2)
    sys.stdout.write('\n')
elif sys.argv[1:] in (['--records'], ['--meanings'], ['--writes']):    # ADDED (for the writes)
    json.dump(out[sys.argv[1][2:]], sys.stdout, indent=2, sort_keys=True)
    sys.stdout.write('\n')
else:
    json.dump(out, sys.stdout, indent=1, sort_keys=True)
