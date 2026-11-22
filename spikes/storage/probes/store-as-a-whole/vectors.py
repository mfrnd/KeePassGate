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

ADDED (form by what arrived): the rows in the form `seen` of `S34-DESIGN.md`, with a removed row
taken off for real, the model configured for each part as below the imports. `--records` adds a
rule taken off with the letters that arrived, with none and while the switch refused, the switch
inheriting with letters for each profile and with none, renewals by a write above, and `texts`,
the texts the script's reader takes or refuses. `--meanings` holds its earlier stores in the form
and stores of the form itself, with capabilities beside letters. `--writes` adds O6, O7, L8, L9
and L10 as `probe/owed.py` holds them, H1, H3, H4, H9 and H10 as `probe/s34.py` holds them, R1a,
and a sequence of writes above, with every merge held to both copies just before it.
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
import owed as O      # noqa: E402  ADDED (form by what arrived): O6, O7, L8, L9 and L10
import s34 as H34     # noqa: E402  ADDED (form by what arrived): the hand histories

# ADDED (rules per profile): the probe's rows switched on. The identifier of a row's version in the
# form `split` of store.py, and the texts of store.py's `doc` with the model's letters written as
# the product writes them. A text of any other form is the model's own and passes as it is; a text
# of a row or a switch that the product's form does not have is refused, so that no vector holds
# such a text. Nothing of store.py changes but these two names, which its Encoder and read.py
# look up.
# ADDED (form by what arrived): the form `seen` of S34-DESIGN.md, as bridge.dep('seen', 'off') sets
# it, with a removed row taken off for real: a version that takes a rule off records the letters
# that arrived for its profile, or `deny` where the node's own switch refused, and a version of
# the switch that inherits records them for each profile. The model writes its letters in lower
# case and in alphabetical order; the product writes them in capitals in the order of CRUSDLAP.
# With the model's two letters, read and write are written R and U; with the full mask, each
# letter is its capital. `configure` sets the model, store.py and read.py to a set of profiles, a
# set of letters and two or three levels, as bridge.owed does, and the letters with them.
S.ID_FORM = 'split'
TWO_LETTERS = {'r': 'R', 'w': 'U'}
MASK_LETTERS = {x: x.upper() for x in 'crusdlap'}
LETTERS = dict(TWO_LETTERS)
ORDER = 'CRUSDLAP'
MODEL_DOC, MODEL_UNDOC = S.doc, S.undoc
NAME_CHARS = set('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_')


def configure(profs, model_letters, levels):
    """ADDED (form by what arrived): the model's profiles, letters and levels, as bridge.owed sets
    them, and the product's letters for them."""
    M.PARENT = {}
    B.owed(profs, model_letters, levels)
    LETTERS.clear()
    LETTERS.update(TWO_LETTERS if model_letters == 'rw' else MASK_LETTERS)


def letters(model):
    """The model's letters as the product's, in the order of CRUSDLAP."""
    return ''.join(sorted((LETTERS[x] for x in model), key=ORDER.index))


def product_doc(f, uid=None):
    text = MODEL_DOC(f, uid)
    if f[0] not in ('rows', 'sws'):
        return text
    kind, _, rest = text.partition(':')
    if kind == 'row':
        return 'row:' + letters(rest)
    if kind == 'off' and '|' not in rest:       # ADDED (form by what arrived): the letters seen
        return text if rest == 'deny' else 'off:' + letters(rest)
    if text == 'sw:deny':
        return text
    if kind == 'sw' and rest.split(' ')[0] == 'inherit' and '|' not in rest:     # ADDED (form by what arrived)
        items = [x.split('=') for x in rest.split(' ')[1:]]
        return 'sw:inherit' + ''.join(' %s=%s' % (p, letters(w)) for p, w in items)
    raise ValueError('a text the product does not write: ' + text)


def product_letters(text):
    """ADDED (form by what arrived): the model's letters of a product's mask: capitals of the
    letters the model is configured with, in the order of CRUSDLAP, each at most once."""
    back = {v: k for k, v in LETTERS.items()}
    if any(x not in back for x in text) or list(text) != sorted(set(text), key=ORDER.index):
        raise ValueError('a mask the product does not write: ' + text)
    return ''.join(sorted(back[x] for x in text))


def model_undoc(text):
    """The product's text of a row or a switch read strictly and handed to store.py's undoc as the
    model's text; any other text handed on as it is. ADDED (form by what arrived): the texts of
    the form, and a switch's items: a name the issuer could issue, in ascending ordinal order, so
    each once, with a mask of at least a letter."""
    kind, sep, rest = text.partition(':')
    if kind == 'row':
        return MODEL_UNDOC('row:' + product_letters(rest))
    if kind == 'off':
        return MODEL_UNDOC(text if rest == 'deny' else 'off:' + product_letters(rest))
    if text == 'sw:deny':
        return MODEL_UNDOC(text)
    if kind == 'sw':
        head, items = rest.split(' ')[0], rest.split(' ')[1:]
        if head != 'inherit':
            raise ValueError('a text the product does not write: ' + text)
        names, parts = [], []
        for x in items:
            p, eq, w = x.partition('=')
            if not eq or not w or not 1 <= len(p) <= 64 or not set(p) <= NAME_CHARS or p == 'default':
                raise ValueError('a text the product does not write: ' + text)
            names.append(p)
            parts.append(p + '=' + product_letters(w))
        if names != sorted(set(names)):
            raise ValueError('a text the product does not write: ' + text)
        return MODEL_UNDOC(' '.join(['sw:inherit'] + parts))
    if kind in ('cut', 'void'):
        raise ValueError('a text the product does not write: ' + text)
    return MODEL_UNDOC(text)


S.doc, S.undoc = product_doc, model_undoc
R.ROWS = R.PP = True    # as bridge.rows sets them: a node's rule read from its rows and its switch
M.ROWS, M.PP = 'off', True      # ADDED (form by what arrived): a removed row taken off for real
M.DEP = S.DEP = 'seen'          # ADDED (form by what arrived): bridge.dep('seen', 'off')
# ADDED (lost settled): `--judge J` plays and reads every vector with crdt_model.JUDGE and
# read.JUDGE set to J (union, object or lost), which prints every vector of the files above as it
# is where no lost version is settled; `--lost` is `--judge lost` with the records, the stores and
# the sequences of the refinement added (`measured-lost-settled`).
ARGS = sys.argv[1:]
# ADDED (renewal answers): `--answers` is `--lost` with the standing answers to a renewal added: the
# records, the stores and the sequences of measured-renewal-answers, each made with crdt_model.ANSWERS
# and read.ANSWERS on while it is made, and off for everything else, so that every other vector is as
# `--lost` prints it.
ANSWERING = '--answers' in ARGS
if ANSWERING:
    ARGS.remove('--answers')
    if '--lost' not in ARGS:
        ARGS.append('--lost')
REFINED = '--lost' in ARGS
if REFINED:
    ARGS.remove('--lost')
    ARGS[:0] = ['--judge', 'lost']
if '--judge' in ARGS:
    at = ARGS.index('--judge')
    M.JUDGE = R.JUDGE = ARGS[at + 1]
    del ARGS[at:at + 2]

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
# ADDED (form by what arrived): the records and the stores of rules are composed with the model
# configured for the full mask and the profiles pf and pg, so that a mask shows the order of
# CRUSDLAP; read and write are then the model's r and u, written R and U as before.
configure(('pf', 'pg'), 'crusdlap', 2)
ROW_RU, ROW_R, ROW_U, ROW_EMPTY = (S.doc(M.row(x)) for x in ('ru', 'r', 'u', ''))
SW_DENY = S.doc(('sws', frozenset(['deny'])))


def off(model_letters):
    """ADDED (form by what arrived): the text of a version that takes the rule off, recording the
    model's letters that arrived."""
    return S.doc(('rows', None, True, frozenset([('s', frozenset(model_letters))])))


def inherit(table):
    """ADDED (form by what arrived): the text of a version of the switch that inherits, recording
    for each profile of `table` the model's letters that arrived."""
    seen = frozenset(p + '.' + w for p, ws in table.items() for w in ws)
    return S.doc(('sws', frozenset(['inherit']), frozenset([('s', seen)])))


OFF_DENY = S.doc(('rows', None, True, frozenset([('deny',)])))
OFF_RU, OFF_R, OFF_NOTHING = off('ru'), off('r'), off('')


def rule_record(obj, text, after=(), nonce=NONCE, lost=()):     # ADDED (lost settled): `lost`
    """A version of a row or of a switch as `record` composes it. For a row, its subject and its
    number are those its identifier holds in the form `split`: the subject hash over the object
    before the slash, and the profile's tag read as a number. ADDED (lost settled): `lost`, the
    member of that name, absent where empty."""
    d = {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': nonce, 'object': obj}
    if lost:    # ADDED (lost settled)
        d['lost'] = ' '.join(sorted(lost))
    made = record('version', d)     # ADDED (lost settled): d
    node, _, profile = obj.partition('/')
    if profile:
        made['subject'], made['number'] = node, int(S.profile_tag(ONE, profile), 16)
    assert made['id'] == S.ident(ONE, 'version', made['subject'], made['number'], made['check'])
    return made


def restating_version(v):
    """The item that restates a version: its identifier as `id`, its object, after and text.
    ADDED (lost settled): and its `lost`, where the version holds that member."""
    m = v['members']
    item = {'after': m['after'], 'doc': m['doc'], 'id': v['id'], 'kind': 'version',
            'object': m['object']}
    if 'lost' in m:     # ADDED (lost settled)
        item['lost'] = m['lost']
    return item


# ADDED (form by what arrived): the versions of the form. A switch's version names five profiles,
# so that the order of its items is seen to be ordinal: Zeta before p-b before pa.
SWITCH_SEEN = {'Zeta': 'c', 'p-b': 'u', 'pa': 'rl', 'pf': 'rla', 'pg': 'r'}
granting = rule_record(PF, ROW_RU)
emptied = rule_record(PG, ROW_EMPTY)
taken_off = rule_record(PF, off('rla'))
taken_off_nothing = rule_record(PF, OFF_NOTHING)
taken_off_deny = rule_record(PF, OFF_DENY)
refusing = rule_record(SW, SW_DENY)
inheriting = rule_record(SW, inherit(SWITCH_SEEN))
inheriting_nothing = rule_record(SW, inherit({}))
renewed = rule_record(PF, off('crudla'), after=[taken_off['id']])
renewed_switch = rule_record(SW, inherit(dict(SWITCH_SEEN, pf='crudla')), after=[inheriting['id']])
settled = rule_record(PF, ROW_R, after=[granting['id'], taken_off['id']])
RULE_RECORDS = {
    'version, a row that grants': granting,
    'version, an empty rule': emptied,
    'version, a rule taken off, with the mask that arrived': taken_off,
    'version, a rule taken off, nothing having arrived': taken_off_nothing,
    'version, a rule taken off while the switch refused': taken_off_deny,
    'version, the switch refusing': refusing,
    'version, the switch inheriting, with a mask for each profile': inheriting,
    'version, the switch inheriting, nothing having arrived': inheriting_nothing,
    'version, a rule taken off renewed by a write above': renewed,
    'version, the switch renewed by a write above': renewed_switch,
    'version, a row naming the versions it replaces': settled,
}
head1v = record('head', {'after': '', 'height': '1', 'nonce': NONCE},
                [FORMAT] + [restating_version(x) for x in RULE_RECORDS.values()], 1)


def model_text(text):
    """ADDED (form by what arrived): the model's own text of a product's text, or None where the
    script's reader refuses it."""
    try:
        return MODEL_DOC(model_undoc(text))
    except ValueError:
        return None


# ADDED (form by what arrived): texts of a row's version and of a switch's version, each with the
# model's own text where the script's reader takes it, and null where it refuses it.
TEXTS = ['row:', 'row:RU', 'row:CRUSDLAP', 'row:UR', 'row:RR', 'row:r', 'row:W', 'row: ',
         'off:', 'off:RLA', 'off:A', 'off:CRUSDLAP', 'off:deny', 'off:ALR', 'off:RLLA', 'off:Deny',
         'off:deny ', 'off:-', 'off:R|U', 'cut:R', 'void:',
         'sw:deny', 'sw:inherit', 'sw:inherit pf=R', 'sw:inherit pf=RLA pg=U',
         'sw:inherit Zeta=C p-b=U pa=RL pf=RLA pg=R', 'sw:inherit ' + 'q' * 64 + '=R',
         'sw:', 'sw:allow', 'sw:deny pf=R', 'sw:inherit ', 'sw:inherit  pf=R', 'sw:inherit pf=',
         'sw:inherit pf', 'sw:inherit pg=R pf=R', 'sw:inherit pf=R pf=U', 'sw:inherit pa=R Zeta=C',
         'sw:inherit pa=R p-b=U', 'sw:inherit default=R', 'sw:inherit p.f=R', 'sw:inherit pf=UR',
         'sw:inherit pf=RR', 'sw:inherit pf=r', 'sw:inherit ' + 'q' * 65 + '=R', 'sw:inherit pf=R ',
         'sw:inherit/deny', 'sw:Inherit']
RECORDS_ABOUT += (
    ' A version of a profile\'s row at a node and a version of a node\'s switch are records of '
    'kind version, with the store\'s rows switched on and the identifier in the form split of '
    'store.py, in the form by what arrived (the form seen of S34-DESIGN.md) with a removed row '
    'taken off for real. The object of a row is rule:, the node\'s identifier in 32 lowercase '
    'hexadecimal characters, a slash and the profile\'s name; the object of a switch is switch: '
    'and the node\'s identifier. Each text is store.py\'s doc over the model\'s value, configured '
    'for the full mask, with the model\'s letters written as the product writes them: row: and '
    'the letters granted, row: alone for an empty rule; off: and the letters that arrived for the '
    'profile when the rule was taken off, off: alone where nothing arrived; off:deny for a rule '
    'taken off while the node\'s own switch refused; sw:deny; and sw:inherit followed, for each '
    'profile at least a letter arrived for, by a space, the name, = and its letters, sw:inherit '
    'alone where nothing arrived for any profile. The model writes every mask in lower case and '
    'in alphabetical order (off:alr, sw:inherit pf=alr); the texts here write it in capitals in '
    'the order of CRUSDLAP (off:RLA, sw:inherit pf=RLA), each letter at most once. Both write a '
    'switch\'s items with the names in ascending ordinal order, which puts Zeta before p-b and p-b '
    'before pa. The identifier of a row\'s version holds the subject hash over the object before '
    'the slash and, in bytes 9 and 10, the profile\'s tag, the first two bytes of HMAC-SHA256 '
    'under the store key over name, a line feed and the profile\'s name; its subject and its '
    'number are those, the number the tag read as a big-endian number, and profileTags holds '
    'each tag. The label name is that of no kind and of nothing else keyed under a store key: '
    'the check is keyed under the kind\'s name, a kind\'s tag under tag, a subject\'s hash under '
    'subject and a key\'s name under key. The identifier of a switch\'s version holds the subject '
    'hash over its object and zero. The node is bb repeated 16 times and the rows are of the '
    'model\'s pf and pg. The versions of a row and of the switch here are composed apart and name '
    'nothing, but for three: the rule taken off renewed by a write above names the version taken '
    'off with RLA and records CRUDLA, what arrives after that write; the switch renewed by a write '
    'above names the switch inheriting with a mask for each profile and changes the letters of pf '
    'alone; and a version of pf that grants R names in after the version that grants RU and the '
    'version taken off with RLA, which it replaces. The summary entry of height 1 that restates '
    'rules holds the item format and an item version for each of those versions, each with the '
    'version\'s identifier as id and its object, after and text. texts holds texts of a row and '
    'of a switch, each with the model\'s own text where the script\'s reader takes it and null '
    'where it refuses it: a mask in capitals of the letters of CRUSDLAP in that order, each at '
    'most once; off:deny exactly; sw:deny exactly; sw:inherit and items, each a single space, a '
    'name of 1 to 64 ASCII letters, digits, - and _ other than default, = and a mask of at least '
    'a letter, the names in ascending ordinal order and so each once, and nothing after the last '
    'item. That reader is the script\'s own, before store.py\'s undoc, which takes some of the '
    'texts refused here; read.py raises on every text the reader refuses, so no store of these '
    'vectors holds such a text and no reading answers for it. The model holds a switch under an '
    'object of its own name, sw: and the node, which store.py writes as switch: and the node\'s '
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
    'head of height 1, restating rules': head1v,    # ADDED (rules per profile)
}, 'profileTags': {p: S.profile_tag(ONE, p) for p in ('pf', 'pg', 'px')},  # ADDED (rules per profile)
    'texts': {t: model_text(t) for t in TEXTS}}     # ADDED (form by what arrived)
out['records']['records'].update(RULE_RECORDS)      # ADDED (rules per profile)

# ADDED (lost settled): versions that tell apart the versions they replace, in `after`, from the
# lost versions at their place they settle for their own object, in `lost`; and the versions whose
# identifiers those name and whose content no store of these vectors holds.
LOST_NONCES = [invented('nonce/lost/%d' % k, 16).hex() for k in range(4)]
if REFINED:
    nowhere = rule_record(PF, ROW_U, nonce=LOST_NONCES[0])
    nowhere2 = rule_record(PF, off('r'), nonce=LOST_NONCES[1])
    nowhere_sw = rule_record(SW, inherit({'pf': 'r'}), nonce=LOST_NONCES[2])
    settles = rule_record(PF, ROW_R, after=[granting['id']], lost=[nowhere['id']])
    settles_two = rule_record(PF, ROW_RU, lost=[nowhere['id'], nowhere2['id']])
    carries = rule_record(PF, ROW_RU, after=[granting['id'], settles['id']], lost=[nowhere['id']])
    settles_sw = rule_record(SW, SW_DENY, after=[refusing['id']], lost=[nowhere_sw['id']])
    LOST_RECORDS = {
        'version, a row whose content no store holds, lost': nowhere,
        'version, a rule taken off whose content no store holds, lost': nowhere2,
        'version, the switch inheriting whose content no store holds, lost': nowhere_sw,
        'version, a row replacing a version and settling a lost version': settles,
        'version, a row settling two lost versions and replacing none': settles_two,
        'version, a row replacing a version that settled a lost version, carrying it on': carries,
        'version, the switch replacing a version and settling a lost version': settles_sw,
    }
    out['records']['records'].update(LOST_RECORDS)
    out['records']['records']['head of height 1, restating versions that settle lost versions'] = record(
        'head', {'after': '', 'height': '1', 'nonce': NONCE},
        [FORMAT] + [restating_version(x) for x in (settles_two, carries, settles_sw)], 1)
    out['records']['about'] += (
        ' The records whose names hold settle, lost or carrying it on are in the form of the '
        'refinement of measured-lost-settled: a version of a row or of a switch holds, beside '
        'after, a member lost, the identifiers of the lost versions at its place that the write '
        'settles for its own object and every identifier the versions it replaces hold in lost, '
        'sorted and separated by a single space as after is, and absent where there is none, so '
        'every other record of this file is as it was. after names only the versions of its own '
        'object it replaces and what those name in after. The three versions named lost are '
        'composed here so that their identifiers are of the place of the row of pf at N, of the '
        'same row, and of N\'s switch; no store of the meanings or the writes holds their '
        'content, which is what makes them lost. The summary entry that restates versions that '
        'settle lost versions holds an item for each, the item holding lost as the version does, '
        'and no lost where the version holds none.')

# ADDED (renewal answers): the standing answers to a renewal, versions of the object renew: and the
# client key, in the three texts, with the moments and the clock of crdt_model; and the texts the
# probe's reader takes and refuses, under a member of their own so that the texts of rules stay as
# they are.
RENEW = 'renew:' + K1
MOMENT1, MOMENT2 = M.MOMENTS
ANSWER_NONCES = [invented('nonce/answer/%d' % k, 16).hex() for k in range(8)]


def answer_record(text, after=(), nonce=NONCE, lost=(), obj=RENEW):
    """ADDED (renewal answers): a version of an answer as `record` composes it: its subject the object,
    its number 0."""
    d = {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': nonce, 'object': obj}
    if lost:
        d['lost'] = ' '.join(sorted(lost))
    made = record('version', d)
    assert made['id'] == S.ident(ONE, 'version', obj, 0, made['check'])
    return made


def answer_reading(text):
    """ADDED (renewal answers): what read.py's reader makes of a text: the answer and its moment, or
    None where the text is out of form."""
    a = R.answer_text(text)
    if a is None:
        return None
    return {'answer': a[0], 'moment': a[1]} if a[0] == 'until' else {'answer': a[0]}


ANSWER_TEXTS = [
    'refuse:forever', 'ask', 'refuse:until:' + MOMENT1, 'refuse:until:' + MOMENT2,
    'refuse:until:2026-12-31T23:00:00Z', 'refuse:until:9999-12-31T23:59:59Z',
    'refuse:until:2026-12-14T23:00:00.000Z', 'refuse:until:2026-12-14T23:00:00.5Z',
    'refuse:until:2026-12-15T00:00:00+01:00', 'refuse:until:2026-12-14T23:00:00+00:00',
    'refuse:until:2026-12-14t23:00:00z', 'refuse:until:2026-12-14 23:00:00Z',
    'refuse:until:2026-12-14T23:00Z', 'refuse:until:2026-12-15', 'refuse:until:2026-02-30T00:00:00Z',
    'refuse:until:2026-12-14T24:00:00Z', 'refuse:until:2026-1-5T23:00:00Z',
    'refuse:until:+2026-12-14T23:00:00Z', 'refuse:until:2026-12-14T23:00:00Zx',
    'refuse:until:2026-12-14T23:00:00Z ', 'refuse:until:', 'refuse:until', 'refuse:', 'refuse:forever ',
    ' refuse:forever', 'Refuse:forever', 'refuse:Forever', 'REFUSE:FOREVER', 'refuse:forever:',
    'ask ', 'Ask', 'ASK', 'allowed', 'const:', 'const:rw', '']
if ANSWERING:
    forever_r = answer_record('refuse:forever')
    until_r = answer_record('refuse:until:' + MOMENT1)
    until2_r = answer_record('refuse:until:' + MOMENT2, nonce=NONCE2)
    lift_r = answer_record('ask', after=[forever_r['id']])
    ghost_r = answer_record('refuse:forever', nonce=ANSWER_NONCES[0])
    lift_lost_r = answer_record('ask', lost=[ghost_r['id']])
    lift_two_r = answer_record('ask', after=[forever_r['id'], until2_r['id']], nonce=NONCE2)
    refusal_after_lift_r = answer_record('refuse:until:' + MOMENT2, after=[lift_r['id'], forever_r['id']])
    lift_carrying_r = answer_record('ask', after=[until2_r['id']], lost=[ghost_r['id']], nonce=ANSWER_NONCES[1])
    out_of_form_r = answer_record('refuse:until:2026-12-15')
    ANSWER_RECORDS = {
        'version, an answer refusing renewals forever': forever_r,
        'version, an answer refusing renewals until a moment': until_r,
        'version, an answer refusing renewals until the later moment, made apart': until2_r,
        'version, a lift naming the refusal it replaces': lift_r,
        'version, an answer refusing forever whose content no store holds, lost': ghost_r,
        'version, a lift settling a lost answer': lift_lost_r,
        'version, a lift naming two refusals made apart': lift_two_r,
        'version, a refusal naming the lift it replaces and what the lift replaced': refusal_after_lift_r,
        'version, a lift replacing a refusal and settling a lost answer': lift_carrying_r,
        'version, an answer whose text is out of form': out_of_form_r,
    }
    out['records']['records'].update(ANSWER_RECORDS)
    out['records']['records']['head of height 1, restating answers'] = record(
        'head', {'after': '', 'height': '1', 'nonce': NONCE},
        [FORMAT] + [restating_version(x) for x in (until_r, until2_r, lift_lost_r, refusal_after_lift_r)], 1)
    out['records']['renewalTexts'] = {t: answer_reading(t) for t in ANSWER_TEXTS}
    out['records']['about'] += (
        ' The records whose names hold an answer, a lift or a refusal are the standing answers to a '
        'renewal of measured-renewal-answers: a version of the object renew: and the client key of '
        'this file\'s admissions, the key of the client\'s first certificate in 64 lowercase '
        'hexadecimal characters, its subject that object and its number 0. Its doc is refuse:forever, '
        'refuse:until: and a moment, RFC 3339 in UTC to the second in 20 characters, or ask, the lift. '
        'The moments are %s and %s, the first midnight of two days where the operator is, written in '
        'UTC; the clock the meanings and the writes read them at is %s, before both, %s, between them, '
        'and %s, after both. A refusal names nothing or the versions it replaces; a lift names in after '
        'the versions it replaces and in lost the lost versions it settles, and carries on what the '
        'versions it replaces name in lost. The version refusing forever whose content no store holds '
        'is composed so that its identifier is lost wherever a store holds its deletion record and '
        'nothing names it. The answer whose text is out of form holds a day with no time. The summary '
        'entry restating answers holds an item for four of them, the item holding lost as the version '
        'does. renewalTexts holds texts of an answer, each with what read.py\'s reader makes of it, the '
        'answer and, for until, the moment, and null for a text out of form: a text is in form only '
        'where composing what was read gives the same bytes, so a fraction of a second, an offset, '
        'lower case, a space, a day with no time, a moment that is no date and anything after the text '
        'are out of form. The texts of rules are under texts, as before.'
        % (MOMENT1, MOMENT2, M.CLOCK[0], M.CLOCK[1], M.CLOCK[2]))


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


def ruled_store(versions, dels=(), restated=None, more=(), root=None):
    """The authority, the root's rows of pf and pg granting RU, the versions given, and a summary
    entry restating every version present that nothing names, or the versions given. ADDED (form
    by what arrived): `root`, the root's versions in the place of those two."""
    present = list(root or [ROOT_PF, ROOT_PG]) + list(versions)
    if restated is None:
        named = set(dels)
        for v in present:
            named |= set(json.loads(v[1]['KPG.Data'])['after'].split())
        restated = [v for v in present if v[0] not in named]
    d = dump([AUTH1] + present + list(more) + [summary(*(restates(v) for v in restated))], dels)
    d['groups'] = {g: {'parent': p} for g, p in TREE.items()}
    return d


def recorded(f):
    """ADDED (form by what arrived): what the versions of a value record, as read.py's deps_of
    holds it: for a row, each record's letters or deny; for a switch, each record's letters for
    each profile it names."""
    out = []
    for rec in R.deps_of(f):
        if rec[0] == 'deny':
            out.append('deny')
        elif f[0] == 'rows':
            out.append(letters(rec[1]))
        else:
            per = {}
            for x in rec[1]:
                per.setdefault(x.split('.')[0], []).append(x.split('.')[1])
            out.append({p: letters(w) for p, w in sorted(per.items())})
    return sorted(out, key=lambda x: json.dumps(x, sort_keys=True))


def value(obj, f):
    """read.py's value of a row or a switch: whether it refuses, the letters all granting versions
    hold, and whether a version takes the rule off. ADDED (form by what arrived): and what its
    versions record."""
    if obj.startswith('switch:'):
        return {'records': recorded(f), 'refuses': R.switch_refuses(f)}
    if f[0] == 'const':
        return {'grants': None, 'records': [], 'refuses': True, 'takenOff': False}
    return {'grants': None if f[1] is None else letters(f[1]), 'records': recorded(f),
            'refuses': False, 'takenOff': f[2]}


def capabilities(words, p):
    """ADDED (form by what arrived): crdt_model.caps of what a node gives profile p, each a verb,
    or a verb, + and a switch, in the order of CRUSDLAP."""
    return cap_texts(M.caps(x for x in words if x.split('.')[0] == p), p)


def cap_texts(caps, p):
    """ADDED (form by what arrived): profile p's capabilities of `caps` as text, in the order of
    CRUSDLAP."""
    def place(c):
        return (ORDER.index(LETTERS[c[1]]), ORDER.index(LETTERS[c[2]]) if c[2] else -1)
    return [LETTERS[v] + ('+' + LETTERS[s] if s else '')
            for _, v, s in sorted((c for c in caps if c[0] == p), key=place)]


def rules(r, nodes):
    """read.py's answer about rules: what each node gives each profile, the standing versions of
    each row and switch, the objects a deletion record shows lost, and the value of each. ADDED
    (form by what arrived): and what each node gives each profile as capabilities."""
    return {'gives': {n: {p: letters(x.split('.')[1] for x in r['words_raw'].get(n, ())
                                     if x.startswith(p + '.')) for p in M.PROFS} for n in nodes},
            'capabilities': {n: {p: capabilities(r['words_raw'].get(n, ()), p) for p in M.PROFS}
                             for n in nodes},
            'lost': sorted(r['lost']),
            'standing': dict(sorted(r['standing'].items())),
            'values': {o: value(o, f) for o, f in sorted(r['row_values'].items())}}


DENY = [ruled(SW, SW_DENY)]
LOST = ruled(PF, ROW_R)         # a version of pf that a deletion record names
# ADDED (form by what arrived): the stores of the earlier file in the form. A rule taken off holds
# what arrived when it was taken off: RU from the root at N, R from N at the group below it while
# N's limit gives R, and nothing at the root; under N's switch refusing it is off:deny, as a rule
# taken off while the switch refuses is written. OFF stands for that text in each store.
OFF = object()
rule_meanings = {
    'a row that grants': [ruled(PF, ROW_R)],
    'an empty rule': [ruled(PF, ROW_EMPTY)],
    'a rule taken off': [(PF, OFF)],
    'a row never made': [ruled(PG, ROW_R)],
    'two versions that grant': apart(PF, ROW_R, ROW_RU),
    'a version that grants beside a rule taken off': [(PF, ROW_R), (PF, OFF)],
    'two versions that grant beside a rule taken off': [(PF, ROW_R), (PF, ROW_RU), (PF, OFF)],
    'three versions that grant, with no letter all of them hold': apart(PF, ROW_RU, ROW_R, ROW_U),
    'two versions that take the rule off': [(PF, OFF), (PF, OFF)],
    'an empty rule beside a version that grants': apart(PF, ROW_EMPTY, ROW_R),
}


def written(versions, text):
    """The versions of a store: entries as they are, or pairs of an object and a text, each made
    apart with a nonce of its own and OFF written as `text`."""
    if all(len(v) == 3 for v in versions):
        return list(versions)
    return [ruled(o, text if t is OFF else t, nonce=n)
            for (o, t), n in zip(versions, (NONCE, NONCE2, NONCE3))]


SEEN_RU = inherit({'pf': 'ru', 'pg': 'ru'})
rule_stores = {}
for name, versions in rule_meanings.items():
    rule_stores['rules: ' + name] = ruled_store(written(versions, OFF_RU))
    rule_stores['rules: %s, under a switch that refuses' % name] = ruled_store(
        DENY + written(versions, OFF_DENY))
rule_stores.update({
    'rules: versions of the switch that differ': ruled_store(apart(SW, SW_DENY, SEEN_RU)
                                                             + [ruled(PF, ROW_R)]),
    'rules: a row lost by a deletion record': ruled_store([ruled(PG, ROW_R)], [LOST[0]]),
    'rules: a switch lost by a deletion record': ruled_store([ruled(PF, ROW_R)],
                                                             [ruled(SW, SEEN_RU)[0]]),
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
        apart(PF, ROW_R, OFF_RU) + [ruled(BPF, ROW_RU)]),
    'rules: limits at N and at the group below it': ruled_store(
        apart(PF, ROW_R, OFF_RU) + apart(BPF, ROW_U, OFF_R)),
    'rules: a version that grants beside a rule taken off, at the root': ruled_store(
        [ruled('rule:%s/pf' % ROOT, OFF_NOTHING, nonce=NONCE2)]),
})
# ADDED (form by what arrived): stores of the form itself, each read with the root's rows of pf and
# pg granting RU but where a store's root is given.
BSW = 'switch:' + BELOW
DENIED, OFF_DENIED = ruled(SW, SW_DENY), ruled(PF, OFF_DENY)
ROOT_RA = ruled('rule:%s/pf' % ROOT, S.doc(M.row('ra')))
rule_stores.update({
    'rules: a rule taken off that cuts what arrives now': ruled_store([ruled(PF, OFF_R)]),
    'rules: a rule taken off below a version made apart at the node above, which it did not see':
        ruled_store([ruled(PF, ROW_RU), ruled(BPF, OFF_R)]),
    'rules: a rule taken off that holds more than arrives': ruled_store([ruled(PF, off('crudla'))]),
    'rules: two rules taken off apart, cutting to what both hold': ruled_store(apart(PF, OFF_RU, OFF_R)),
    'rules: a rule taken off while the switch refused, under a switch with no version':
        ruled_store([OFF_DENIED]),
    'rules: a rule taken off, then the switch turned to refuse': ruled_store(DENY + [ruled(PF, OFF_RU)]),
    'rules: a rule taken off while the switch refused, beside the switch turned to inherit apart':
        ruled_store([ruled(SW, SEEN_RU), OFF_DENIED]),
    'rules: a rule taken off while the switch refused, renewed by the switch turned to inherit':
        ruled_store([ruled(SW, SEEN_RU, after=[DENIED[0]]), ruled(PF, OFF_RU, after=[OFF_DENIED[0]])],
                    [DENIED[0], OFF_DENIED[0]]),
    'rules: the switch inheriting with masks beside a widening above':
        ruled_store([ruled(SW, inherit({'pf': 'r', 'pg': 'r'}))]),
    'rules: a profile the switch inheriting does not name': ruled_store([ruled(SW, inherit({'pf': 'r'}))]),
    'rules: a rule taken off and the switch inheriting, each with a mask': ruled_store(
        [ruled(PF, OFF_RU), ruled(SW, inherit({'pf': 'r', 'pg': 'ru'}))]),
    'rules: a rule taken off that leaves a switch and no verb': ruled_store(
        [ruled(PF, off('ua'))], root=[ROOT_RA, ROOT_PG]),
    'rules: the switch inheriting leaving a switch and no verb': ruled_store(
        [ruled(SW, inherit({'pf': 'ua', 'pg': 'ru'}))], root=[ROOT_RA, ROOT_PG]),
    'rules: cuts at N and at the group below it along a single walk': ruled_store(
        [ruled(PF, OFF_R), ruled(BPF, OFF_RU)]),
    'rules: a cut at N and the switch of the group below it along a single walk': ruled_store(
        [ruled(PF, OFF_R), ruled(BSW, inherit({'pf': 'u', 'pg': 'ru'}))]),
    'rules: a rule taken off lost by a deletion record': ruled_store([], [ruled(PF, OFF_R)[0]]),
    'rules: the switch inheriting with masks lost by a deletion record': ruled_store(
        [], [ruled(SW, inherit({'pf': 'r', 'pg': 'r'}))[0]]),
})
for name, d in sorted(rule_stores.items()):
    r = R.read(d, history=True)
    out['meanings']['meanings'][name] = dict(
        files(d), **answer(r), rules=rules(r, [ROOT, N, BELOW]),
        tree={'groups': {g: p for g, p in sorted(TREE.items())}, 'root': ROOT})
out['meanings']['about'] += (
    ' The stores whose names begin with rules: are about a profile\'s rule at a node, read by '
    'read.py with the store\'s rows switched on and the identifier of a row\'s version in the '
    'form split of store.py, in the form by what arrived (the form seen of S34-DESIGN.md), the '
    'objects and texts those of the records file and the model configured for the full mask. '
    'Each holds the first authority of the records file, a version of each of the rows of pf '
    'and pg at the root granting RU, or the root\'s versions its name gives, the versions its '
    'name gives, and a summary entry of height 1. The summary entry restates every version '
    'present that no deletion record and no version names, but in the store of a deletion record '
    'of a version the summary entry restates, where it restates the root\'s two versions and the '
    'version the deletion record names. Its tree, as tree holds it by identifiers, is the root, '
    'aa repeated 16 times, the group N, bb repeated 16 times, directly under the root, and a '
    'group, cc repeated 16 times, directly under N; no node holds anything of the plugin. Every '
    'store answers for all three, so a limit at N shows in what the group under N is given. A '
    'store with a switch that refuses holds a version of N\'s switch refusing; a switch with no '
    'version inherits and cuts nothing. Versions made apart differ in their nonce. The stores of '
    'the earlier file are here in the form: where they held a rule taken off, it holds off: and '
    'what arrived when it was taken off, RU from the root at N, R from N at the group under N '
    'where N gives R, and nothing at the root; under N\'s switch refusing it holds off:deny, as '
    'a rule taken off while the switch refuses is written; and the switch inheriting holds '
    'sw:inherit pf=RU pg=RU. Beside them are stores of the form itself: a rule taken off that '
    'cuts what arrives now; a rule taken off at the group under N below a version at N made '
    'apart, which the taking off did not see; a rule taken off that holds more than arrives; two '
    'rules taken off apart; off:deny under a switch with no version, beside a version of the '
    'switch turned to inherit made apart, and renewed, with the switch\'s version, by the switch '
    'turned to inherit, each renewal naming the version it replaces and that version under a '
    'deletion record; a rule taken off before the switch was turned to refuse; the switch '
    'inheriting with masks beside a widening above, and naming a single profile; a rule taken '
    'off and the switch inheriting together; a rule taken off and the switch inheriting that '
    'leave the switch A and no verb, under the root granting pf RA; cuts at N and at the group '
    'under N along a single walk, by a rule and by the group\'s switch; and a rule taken off and '
    'the switch inheriting, each lost by a deletion record. Beside the answer of the other '
    'stores, rules holds what read.py answers about rules: gives, what each node gives pf and pg '
    'as letters in the order of CRUSDLAP, read.py\'s words with the model\'s letters in capitals; '
    'capabilities, the same as crdt_model.caps has them, each a verb alone or a verb, + and a '
    'switch of the same rule, a switch with no verb granting nothing; standing, the texts of the '
    'versions of each row and switch that stand; lost, each row or switch a deletion record '
    'shows lost; and values, read.py\'s value of each row and switch: whether it refuses, for a '
    'row the letters all granting versions hold, null where no version grants, and whether a '
    'version takes the rule off, and for each what its standing versions record, for a row the '
    'letters or deny of each, for a switch the letters of each profile each names. A deletion '
    'record tells of a row\'s version only where the name of its profile is known to the file, '
    'from an addition, a removal or a row\'s object: px is known in a single store, by an '
    'addition that puts it on the list. The model holds a switch under an object of its own '
    'name, sw: and the node, which store.py writes as switch: and the node\'s identifier, and its '
    'acts compose a version at the write, with no buffer between a press and the write; these '
    'stores are composed here and by no act.')


# ADDED (lost settled): stores of two names whose tags meet under the key `one`, a name whose tag
# is 0 beside the object of N's rule with no slash, and the switch, read with read.JUDGE 'lost'.
def lost_stores():
    (p, q, t), z, tried = O.meeting_names(ONE)
    configure(('pf', 'pg', p, q, z), 'crusdlap', 2)
    PP, QQ, ZZ = ('rule:%s/%s' % (N, x) for x in (p, q, z))
    PLAIN = 'rule:' + N
    nonce = [invented('nonce/lost/store/%d' % k, 16).hex() for k in range(12)]

    def ghost(obj, k, text=ROW_U):
        """The identifier of a version of obj whose content no store holds."""
        return ruled(obj, text, nonce=nonce[k])[0]

    def lruled(obj, text, after=(), lost=(), n=NONCE):
        d = {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': n, 'object': obj}
        if lost:
            d['lost'] = ' '.join(sorted(lost))
        return entry('version', d)

    def lrestates(v):
        item = restates(v)
        d = json.loads(v[1]['KPG.Data'])
        if 'lost' in d:
            item['lost'] = d['lost']
        return item

    def store(versions, dels=(), restated=None, heads=None):
        present = [ROOT_PF, ROOT_PG] + list(versions)
        if restated is None:
            named = set(dels)
            for v in present:
                named |= set(json.loads(v[1]['KPG.Data'])['after'].split())
            restated = [v for v in present if v[0] not in named]
        hs = heads or [summary(*(lrestates(v) for v in restated))]
        d = dump([AUTH1] + present + hs, dels)
        d['groups'] = {g: {'parent': par} for g, par in TREE.items()}
        return d

    def head_of(n, restated):
        return rec(ONE, 'head', {'after': '', 'height': '1', 'nonce': n},
                   numbered((FORMAT,) + tuple(lrestates(v) for v in restated)), 1) + ([],)

    vp, vq = lruled(PP, ROW_R), lruled(QQ, ROW_R, n=NONCE2)
    vq1 = lruled(QQ, ROW_R, n=nonce[0])
    vq2 = lruled(QQ, ROW_RU, after=[vq1[0]])
    g = ghost(PP, 1)
    vp_s = lruled(PP, ROW_RU, lost=[g])
    vq_s = lruled(QQ, ROW_RU, lost=[g], n=NONCE2)
    vq2_s = lruled(QQ, ROW_RU, after=[vq1[0]], lost=[g])
    vp1 = lruled(PP, ROW_R, n=nonce[2])
    vp2_t = lruled(PP, ROW_RU, after=[vp1[0]], lost=[g])
    vq1_t = lruled(QQ, ROW_R, n=nonce[3])
    vq2_t = lruled(QQ, ROW_RU, after=[vq1_t[0]], lost=[g])
    old = head_of(nonce[4], [ROOT_PF, ROOT_PG, vp1, vq1_t])
    new = head_of(nonce[5], [ROOT_PF, ROOT_PG, vp2_t, vq2_t])
    # the next write's summary entry: it names both, which the write deletes with deletion records
    after_both = ' '.join(sorted([old[0], new[0]]))
    following = rec(ONE, 'head', {'after': after_both, 'height': '2', 'nonce': nonce[11]},
                    numbered((FORMAT,) + tuple(lrestates(v) for v in (ROOT_PF, ROOT_PG, vp2_t, vq2_t))),
                    2) + ([],)
    beside = lruled(PP, ROW_U, n=nonce[6])
    vz, vz1 = lruled(ZZ, ROW_R), lruled(ZZ, ROW_R, n=nonce[7])
    vz2 = lruled(ZZ, ROW_RU, after=[vz1[0]])
    plain1 = lruled(PLAIN, ROW_R, n=nonce[8])
    plain2 = lruled(PLAIN, ROW_RU, after=[plain1[0]])
    gz = ghost(ZZ, 9)
    gsw = ghost(SW, 10, SW_DENY)
    stores = {
        'rules, tags that meet: a version of q replacing a version, beside its deletion record':
            store([vp, vq2], [vq1[0]]),
        'rules, tags that meet: a deletion record at their place whose content is nowhere':
            store([vp, vq], [g]),
        'rules, tags that meet: that deletion record settled by p alone': store([vp_s, vq], [g]),
        'rules, tags that meet: that deletion record settled by p and by q': store([vp_s, vq_s], [g]),
        'rules, tags that meet: that deletion record settled by p, and a version of q replacing a version':
            store([vp_s, vq2], [g, vq1[0]]),
        'rules, tags that meet: settled by each, an earlier summary entry still restating what each replaced':
            store([vp2_t, vq2_t], [g, vp1[0], vq1_t[0]], heads=[old, new]),
        'rules, tags that meet: settled by each, after the next write deleted both summary entries':
            store([vp2_t, vq2_t], [g, vp1[0], vq1_t[0], old[0], new[0]], heads=[following]),
        'rules, tags that meet: a version beside its deletion record, named in lost by its own row':
            store([beside, lruled(PP, ROW_RU, lost=[beside[0]], n=NONCE2), vq], [beside[0]]),
        'rules, tags that meet: a version beside its deletion record, named in lost by the other row':
            store([beside, lruled(QQ, ROW_RU, lost=[beside[0]], n=NONCE2)], [beside[0]]),
        'rules, tags that meet: a deletion record at their place named in after by a version at another node':
            store([vp, vq, lruled('rule:%s/%s' % (BELOW, p), ROW_R, after=[g])], [g]),
        'rules, a tag of 0: a version of the object with no slash replacing a version':
            store([vz, plain2], [plain1[0]]),
        'rules, a tag of 0: a version of z replacing a version, beside the object with no slash':
            store([vz2, plain1], [vz1[0]]),
        'rules, a tag of 0: a deletion record at that place settled by z':
            store([lruled(ZZ, ROW_RU, lost=[gz]), plain1], [gz]),
        'rules, the switch: a lost version settled by a version of the switch':
            store([vp, lruled(SW, SW_DENY, lost=[gsw])], [gsw]),
    }
    for name, d in sorted(stores.items()):
        r = R.read(d, history=True)
        out['meanings']['meanings'][name] = dict(
            files(d), **answer(r), rules=rules(r, [ROOT, N, BELOW]),
            tree={'groups': {gg: par for gg, par in sorted(TREE.items())}, 'root': ROOT})
    out['meanings']['about'] += (
        ' The stores whose names begin with rules and a comma are read with the reading of the '
        'refinement of measured-lost-settled (read.JUDGE lost), as every store of this file is '
        'where it is printed so. Their profiles are pf, pg, p, q and z: p is %s and q %s, the first '
        'two names q00000, q00001, ... whose tags meet under the key one, at %s; z is %s, the first '
        'such name whose tag is 0000. A version there may hold lost: the lost versions at its place '
        'it settles for its own object. A deletion record whose version no store holds, and that no '
        'version names, is lost to every object at its place; named in after by a version in force '
        'of an object at its place, it is answered for each of them; named only in lost, for '
        'the namer\'s object alone. A version present beside its deletion record is told of by its '
        'own object, and answered where a version in force of that object names it in after or in '
        'lost. A deletion record under a tag of 0 at N\'s rows tells of the row of z and of the '
        'object rule: and N with no slash where a version of it is present, which is no node\'s '
        'rule. Two of the stores hold two summary entries made apart: an earlier summary entry '
        'restating the versions each row replaced, and a later summary entry restating the '
        'versions that replace them, as '
        'a copy that took a Synchronize holds them before its next write; and the same store after '
        'that write, whose summary entry of height 2 names both and restates what stands, both '
        'deleted with deletion records: each row reads the same in both.'
        % (p, q, t, z))
    configure(('pf', 'pg'), 'crusdlap', 2)


if REFINED:
    lost_stores()


def renewals(r):
    """ADDED (renewal answers): read.py's answer about each client's renewals: the answer, `ask`,
    `until:` and the moment or `forever`; its cause (none, stands, lost or unreadable); the texts of
    the versions that stand and the identifiers of the lost versions; whether it refuses at each
    position of the clock; and the moment a refusal until a moment holds as notBefore."""
    out = {}
    for c, x in sorted(r['renew_detail'].items()):
        tok = x['value']
        out[c] = {'value': tok, 'cause': x['cause'], 'standing': x['standing'], 'lost': x['lost'],
                  'refuses': {clock: M.answer_refuses(tok, clock) for clock in M.CLOCK},
                  'notBefore': tok[6:] if tok.startswith('until:') else None}
    return out


def answer_stores():
    """ADDED (renewal answers): small stores of answers about the client of the admission, and what
    read.py answers for each, read with the reading of answers on."""
    R.ANSWERS = M.ANSWERS = S.HEX = True
    OTHER = invented('client/other', 32).hex()

    def ver(text, after=(), lost=(), n=NONCE, obj=RENEW):
        d = {'after': ' '.join(sorted(after)), 'doc': text, 'nonce': n, 'object': obj}
        if lost:
            d['lost'] = ' '.join(sorted(lost))
        return entry('version', d)

    def item(v):
        d = json.loads(v[1]['KPG.Data'])
        it = {'after': d['after'], 'doc': d['doc'], 'id': v[0], 'kind': 'version', 'object': d['object']}
        if 'lost' in d:
            it['lost'] = d['lost']
        return it

    def store(versions, dels=(), restated=None, more=(), more_items=()):
        present = [AUTH1, PROFILE, ADMITTED] + list(more) + list(versions)
        if restated is None:
            named = set(dels)
            for v in versions:
                named |= set(json.loads(v[1]['KPG.Data'])['after'].split())
            restated = [v for v in versions if v[0] not in named]
        return dump(present + [summary(*([item(v) for v in restated] + list(more_items)))], dels)

    F = ver('refuse:forever')
    F0 = ver('refuse:forever', n=ANSWER_NONCES[2])
    U1 = ver('refuse:until:' + MOMENT1)
    U2 = ver('refuse:until:' + MOMENT2, n=NONCE2)
    G = ver('refuse:forever', n=ANSWER_NONCES[0])      # whose content no store holds
    X = ver('refuse:until:2026-12-15')
    REVOKED_OTHER = entry('revoked', {'client': OTHER, 'nonce': NONCE})
    REVOKING_OTHER = {'client': OTHER, 'kind': 'revoked'}
    stores = {
        'renewals: no answer': store([]),
        'renewals: refused forever': store([F]),
        'renewals: refused until a moment': store([U1]),
        'renewals: refused until the later moment': store([U2]),
        'renewals: a lift whose refusal it replaced is deleted': store([ver('ask', after=[F[0]])], [F[0]]),
        'renewals: forever and until the later moment, made apart': store([F, U2]),
        'renewals: two moments, made apart': store([U1, U2]),
        'renewals: until a moment and a lift made apart, which did not see it': store(
            [U1, ver('ask', after=[F0[0]])], [F0[0]]),
        'renewals: forever and a lift made apart, which did not see it': store(
            [F, ver('ask', after=[F0[0]])], [F0[0]]),
        'renewals: a lift naming the refusal it replaces, both present': store([F, ver('ask', after=[F[0]])]),
        'renewals: a lift naming one of two refusals made apart': store([F, U2, ver('ask', after=[F[0]])]),
        'renewals: a lost answer': store([], [G[0]]),
        'renewals: a lost answer settled by a lift': store([ver('ask', lost=[G[0]])], [G[0]]),
        'renewals: a lost answer settled by a lift, beside a refusal the lift did not see': store(
            [ver('ask', lost=[G[0]]), U2], [G[0]]),
        'renewals: a deletion record of a version that a standing version names': store(
            [ver('refuse:until:' + MOMENT2, after=[F[0]])], [F[0]]),
        'renewals: a version present beside its own deletion record': store([F], [F[0]]),
        'renewals: a version beside its own deletion record, named in after by a lift': store(
            [F, ver('ask', after=[F[0]])], [F[0]]),
        'renewals: a version beside its own deletion record, named in lost by a lift': store(
            [F, ver('ask', lost=[F[0]])], [F[0]]),
        'renewals: a version carried only by an item, beside its deletion record': store([], [F[0]], restated=[F]),
        'renewals: a version carried only by an item': store([], restated=[F]),
        'renewals: a text out of form': store([X]),
        'renewals: a text out of form, beside until a moment': store([X, U1]),
        'renewals: a text out of form, named by a lift': store([X, ver('ask', after=[X[0]])]),
        'renewals: a deletion record of an answer of a client key the file does not know': store(
            [], [ver('refuse:forever', obj='renew:' + OTHER)[0]]),
        'renewals: a deletion record of an answer of a client key a revocation makes known': store(
            [], [ver('refuse:forever', obj='renew:' + OTHER)[0]], more=[REVOKED_OTHER],
            more_items=[REVOKING_OTHER]),
        'renewals: an answer of a client key that only the answer makes known': store(
            [ver('refuse:until:' + MOMENT1, obj='renew:' + OTHER)]),
        'renewals: an answer under an object that holds no client key': store(
            [ver('refuse:forever', obj='renew:' + 'C3' * 32)]),
        'renewals: an answer under an object that holds no client key, beside the client\'s own': store(
            [ver('refuse:forever', obj='renew:' + K1[:63]), U1]),
    }
    for name, d in sorted(stores.items()):
        r = R.read(d, history=True)
        out['meanings']['meanings'][name] = dict(files(d), **answer(r), renewals=renewals(r))
    out['meanings']['about'] += (
        ' The stores whose names begin with renewals: are about the standing answers to a renewal of '
        'measured-renewal-answers, read by read.py with its reading of answers on. Each holds the first '
        'authority, the profile record, the admission of the client K1 of the records file but where its '
        'name gives otherwise, the versions of the object renew: and K1 its name gives, and a summary '
        'entry restating every version present that no deletion record and no version names, or the '
        'versions its name gives. The texts and the moments are those of the records file; versions '
        'made apart differ in their nonce; the version refusing forever whose content no store holds '
        'is that of the records file. renewals holds, for each client key the file knows, the answer, '
        'ask, until: and the moment, or forever; its cause, none where no version stands or is lost, '
        'stands, lost or unreadable; the texts of the versions that stand; the identifiers of the lost '
        'versions; whether it refuses at each moment of the clock %s, %s and %s, a refusal until a '
        'moment refusing while the clock is before the moment; and notBefore, the moment of a refusal '
        'until a moment, null otherwise. The answer is the strictest standing version: forever, then '
        'the later moment, then the earlier, then ask; a lost version and a text out of form refuse '
        'forever, the second whatever else stands. A client key is known from an admission, from a '
        'revocation in a record or a summary item, and from the object of a version present or '
        'restated; the other key of two stores is 32 invented bytes in hexadecimal, and a deletion '
        'record of an answer of a key the file does not know tells of nothing. An object renew: that '
        'holds anything but 64 lowercase hexadecimal characters is the object of nothing, and no '
        'answer is read from it: C3 repeated 32 times, and the client key less its last character.'
        % tuple(M.CLOCK))
    R.ANSWERS = M.ANSWERS = S.HEX = False


if ANSWERING:
    answer_stores()


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


G = 'dd' * 16       # ADDED (form by what arrived): the model's group G, between the root and N


def placed(levels):
    """ADDED (form by what arrived): the model's nodes by identifier, and the groups of the tree
    with their parents: N under the root, or under G with three levels."""
    if levels == 3:
        return {'root': ROOT, 'G': G, 'N': N}, {G: ROOT, N: G}
    return {'root': ROOT, 'N': N}, {N: ROOT}


def read_copy(W, enc, i, tree):
    """File i of the model as the Encoder writes it, as a dump, and what read.py answers for it."""
    rep = W.reps[i]
    eids = sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid and ok)
    written = [enc.entry(eid) for eid in eids]
    d = dump([(u, s, []) for (u, title, s, prot) in written], [enc.u(x) for x in rep.dels])
    d['groups'] = {g: {'parent': p} for g, p in tree.items()}     # ADDED (rules per profile)
    return eids, written, d, R.read(d, history=True)


def state(W, enc, i, tree):
    """File i of the model as the Encoder writes it, described, and what read.py answers for
    it."""
    rep = W.reps[i]
    eids, written, d, r = read_copy(W, enc, i, tree)
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
            'answer': dict(answer(r), rules=rules(r, [ROOT] + sorted(tree, key=depth_of(tree))),
                           **({'renewals': renewals(r)} if M.ANSWERS else {})),     # ADDED (renewal answers)
            'file': files(d)}


def depth_of(tree):
    def depth(n):
        return 0 if n == ROOT else 1 + depth(tree[n])
    return depth


def beyond(after, pre, nodes):
    """ADDED (form by what arrived): what a merged copy gives at each node beyond both copies just
    before the merge, as store_runs.py's part_dep holds it: letters neither copy gave, and
    crdt_model.caps neither copy gave. Only the nodes with something beyond."""
    out = {}
    for n in nodes:
        a, b0, b1 = (x.get(n, frozenset()) for x in (after, pre[0], pre[1]))
        lt, caps = a - b0 - b1, M.caps(a) - M.caps(b0) - M.caps(b1)
        if lt or caps:
            out[n] = {'capabilities': {p: cap_texts(caps, p) for p in sorted(set(c[0] for c in caps))},
                      'letters': {p: letters(x.split('.')[1] for x in lt if x.split('.')[0] == p)
                                  for p in sorted(set(x.split('.')[0] for x in lt))}}
    return out


def play(nrep, steps, cfg=None):
    """The model's own store, then each (command, act) of steps played by the model, each with
    the file of the copy it wrote and the deletion records it added there. ADDED (form by what
    arrived): `cfg`, the profiles, the model's letters and the levels the model is configured with,
    two profiles, read and write and two levels by default; each merge also holds what the merged
    copy gives beyond both copies just before it."""
    cfg = cfg or (('pf', 'pg'), 'rw', 2)
    configure(*cfg)
    nodes, tree = placed(cfg[2])
    W = M.World(nrep, B.FLAGS)
    W.maxclients = 4
    enc = S.Encoder(W, nodes)     # ADDED (rules per profile): N; (form by what arrived): G
    made = {'nodes': nodes, 'profiles': list(cfg[0]), 'start': state(W, enc, 0, tree), 'steps': [],
            'tree': {'groups': dict(sorted(tree.items())), 'root': ROOT}}
    for command, op in steps:
        i = op[2] if op[0] == 'merge' else op[1]
        before = set(W.reps[i].dels)
        pre = [read_copy(W, enc, k, tree)[3]['words_raw'] for k in (op[2], op[3])] \
            if op[0] == 'merge' else None
        if M.ANSWERS and op[0] == 'merge':     # ADDED (renewal answers)
            pre_renew = [read_copy(W, enc, k, tree)[3]['renew'] for k in (op[2], op[3])]
        x = dict(command=command, act=list(op), played=W.step(op), **state(W, enc, i, tree))
        x['deleted'] = sorted((described(enc, u) for u in set(W.reps[i].dels) - before),
                              key=by_record)
        if pre is not None:
            x['beyond'] = beyond(read_copy(W, enc, i, tree)[3]['words_raw'], pre,
                                 [ROOT] + sorted(tree))
        if M.ANSWERS:   # ADDED (renewal answers): the copy's clock, and answers below both copies
            x['clock'] = M.CLOCK[W.clock[i]]
            if op[0] == 'merge':
                after = read_copy(W, enc, i, tree)[3]['renew']
                x['renewalsBeyond'] = sorted(c for c, tok in after.items() if all(
                    M.answer_rank(tok) < M.answer_rank(p.get(c, M.ASK)) for p in pre_renew))
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
    # ADDED (form by what arrived): a table of rows in a single act, and the press that settles
    return {'switch': 'Set switch', 'drop': 'Remove version', 'rule': 'Set rows',
            'lift': 'Settle limit'}[op[0]]


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


# ADDED (form by what arrived): the sequences of measured-rules-across-levels and S34-DESIGN.md that
# the form closes, leaves or writes for, each under the configuration it was measured in.
def form_sequence(label, what, nrep, ops, cfg=None):
    out['writes']['sequences']['form, %s: %s' % (label, what)] = play(
        nrep, [(command(op), op) for op in ops], cfg)


MASK2, MASK3, LEVELS3 = (O.FOUR, O.MASK, 2), (O.FOUR, O.MASK, 3), (('pf', 'pg'), 'rw', 3)
configure(*MASK2)
for hid, what, base, c0, c1, nrep in O.merges4_histories():
    if hid in ('O6', 'O7'):
        form_sequence(hid, what, nrep, base + c0 + c1 + [INTO_0, INTO_1], MASK2)
configure(*MASK3)
for hid, what, base, c0, c1, nrep in O.levels_histories():
    if hid in ('L8', 'L9', 'L10'):
        form_sequence(hid, what, nrep, base + c0 + c1 + [INTO_0, INTO_1], MASK3)
HAND = {hid: (what, ops, last) for hid, what, ops, last in H34.hand_histories()}
for hid in ('H1', 'H3', 'H4', 'H9', 'H10'):
    what, ops, last = HAND[hid]
    if hid == 'H1':     # then the press that settles in the copy that took the merge, and back
        what += '; then the press that settles in copy 1, synchronized into copy 0'
        ops = ops + [last, ('lift', 1, 'N', 'pf'), INTO_0]
    elif hid == 'H10':  # as S34-DESIGN.md has it: nothing arrives for pf
        what = what.replace('removed for real', 'nothing arriving for pf, removed for real')
        ops = [('row', 0, 'root', 'pf', '')] + ops + [last]
    elif last is not None:
        ops = ops + [last]
    form_sequence(hid, what, 2, ops)
form_sequence('R1a', 'two rules taken off apart at two levels, and a widening after the '
              'merge: G grants pf R and N holds an empty rule; copy 0 takes the rule off N, copy 1 '
              'takes it off G; both ways; the root widened for pf in copy 0',
              2, [('row', 0, 'G', 'pf', 'r'), ('row', 0, 'N', 'pf', ''), INTO_1,
                  ('row', 0, 'N', 'pf', 'off'), ('row', 1, 'G', 'pf', 'off'), INTO_1, INTO_0,
                  ('row', 0, 'root', 'pf', 'rw')], LEVELS3)
form_sequence('above', 'the write above: G and N take pf\'s rule off seeing R; copy 1 widens the '
              'root, renewing G; both ways, so N cuts to R under G; copy 0 narrows the root to R, '
              'renewing G and leaving N, which was cutting; copy 0 widens the root again, renewing '
              'G and N',
              2, [('row', 0, 'G', 'pf', ''), ('row', 0, 'G', 'pf', 'off'), ('row', 0, 'N', 'pf', ''),
                  INTO_1, ('row', 0, 'N', 'pf', 'off'), ('row', 1, 'root', 'pf', 'rw'), INTO_0, INTO_1,
                  ('row', 0, 'root', 'pf', 'r'), ('row', 0, 'root', 'pf', 'rw'), INTO_1], LEVELS3)
configure(('pf', 'pg'), 'rw', 2)
out['writes']['about'] += (
    ' Every sequence plays with the store\'s rows switched on, the identifier of a row\'s version '
    'in the form split of store.py, in the form by what arrived (the form seen of S34-DESIGN.md) '
    'with a removed row taken off for real, as bridge.dep(\'seen\', \'off\') sets them, and the '
    'texts and objects of the records file: the start holds a version of the row of each profile '
    'at the root, row:R. Each sequence holds the profiles it plays with as profiles, the model\'s '
    'nodes by identifier as nodes and the tree as tree. The model\'s node N is bb repeated 16 '
    'times, directly under the root, aa repeated 16 times; where a sequence has three levels, the '
    'model\'s group G, dd repeated 16 times, is directly under the root and N under G. A sequence '
    'plays with the profiles pf and pg and the model\'s letters read and write, written R and U, '
    'but O6, O7, L8, L9 and L10, which play with the profiles pf, pg, ph and pi and the full '
    'mask, as they were measured; the model\'s letters are then those of CRUSDLAP in lower case, '
    'written in capitals. Every answer adds rules, read.py\'s answer about rules at each node as '
    'the meanings file gives it, capabilities among it. Every Synchronize adds beyond: what the '
    'copy merged into gives at each node beyond both copies just before the merge, letters '
    'neither copy gave and capabilities neither copy gave, as store_runs.py\'s part_dep holds a '
    'merge to both copies; only a node with something beyond is there. A version present or named '
    'by a deletion record adds its object, and for a row its subject and its number are those its '
    'identifier holds, as the records file gives them. The sequences whose names begin with rules '
    'and a comma are those of '
    'measured-rules-per-profile sections 4, 5 and 7 that run over two copies, each named by its '
    'label there, 5 for the two copies of section 5: A1 to A7, 5, D1 to D3, and W1 to W8. Each '
    'plays a base in copy 0, a Synchronize into copy 1, the acts of each copy apart, and then a '
    'Synchronize of copy 1 into copy 0 and of copy 0 into copy 1. Where the report sets several '
    'rows in a single act, each row is an act of its own, in the order of the profiles\' names; '
    'an act of the report that changes no row is not played, as the root grants read from the '
    'start; and D1 to D3 remove a version in the form again, which STORE.md 617 fixes. The '
    'commands: Set row is the model\'s row act with letters, an empty rule where it holds none; '
    'Remove row its row act with off, which writes nothing where no row of the profile stands '
    'but versions that take the rule off, writes off:deny where the copy reads the node\'s switch '
    'as refusing, and otherwise writes anew each version of the switch that holds other letters '
    'for the profile than arrive, then takes the rule off with the letters that arrive; Set '
    'switch its switch act with deny, or with inherit, which records for each profile the '
    'letters that arrive, writes nothing where no version of the switch stands or a single '
    'version inherits already, '
    'and writes anew each version at the node that takes a rule off and holds other letters than '
    'arrive for its profile, off:deny among them; Set rows its rule act with a table, a version '
    'for each row the table names at the node, in a single write; Settle limit its lift act, the '
    'press that settles: each version at the node that lets the profile through and holds other '
    'letters than arrive written anew, a version of the switch with that profile\'s letters '
    'changed and every other profile\'s kept; Remove version its drop act, which writes again '
    'each version left, naming the version removed, the version left and everything each of them '
    'names. After every act at a node with nodes below, the write above: each node below, the '
    'nearer first, whose switch does not refuse and whose row does not answer by itself, where '
    'what arrives differs before and after the act and every version there that lets the profile '
    'through held everything that arrived before it, has those versions written anew, each by '
    'itself, with what arrives after, naming the version it renews and everything that version '
    'names; a version that was cutting already, or off:deny, leaves every version of its node for '
    'that profile as it is. An act\'s letters are the model\'s, and its node is the model\'s name. '
    'A step whose act writes nothing has played false and the copy\'s file as it stood: the '
    'second removal in W6. No Synchronize of A1 to W8 gives anything beyond both copies. At the '
    'end of W8 N gives pf R, what the first copy gave, where the store as decided gave R and U; '
    'W3 ends with nothing for pf; and W6 ends with pf given R and U, wider than the store as '
    'decided by design: the switch turned off in the second copy renews off:deny with what '
    'arrives (H9), as measured-rules-across-levels section 4 has it. Left out are the sequences '
    'of section 5 over three copies, D4 and D5, and W9, which writes a node\'s rule whole. The '
    'sequences whose names begin with form and a comma: O6 and O7 of measured-rules-owed-forms '
    '(merges4) and L8, L9 and L10 of its levels, each a base in copy 0, a Synchronize into copy '
    '1, the acts of each copy apart, and a Synchronize of copy 1 into copy 0 and of copy 0 into '
    'copy 1, as measured-rules-across-levels section 4 played them; no Synchronize of them gives '
    'anything beyond both copies, O6 and O7 end with pf given A alone at N, a switch with no verb '
    'and so no capability, L8 with RL at G and at N, L9 with RL at N, and L10 with nothing for pf '
    'at N. H1, H3, H4, H9 and H10 of S34-DESIGN.md section 5, each as probe/s34.py plays it under '
    'the form: H1 then the press that settles in the copy that took the merge, and a Synchronize '
    'back; H10 with the root\'s row of pf first set to no letter, so that nothing arrives for pf, '
    'as the design states it, and pf ends with nothing at N. H4 is the residual history of the '
    'form: its last Synchronize gives pf R at N beyond both copies, within what the version at N '
    'recorded when its rule was taken off. R1a is the second residual history: G grants pf R and '
    'N holds an empty rule, the first copy takes the rule off N and the second takes it off G, '
    'both ways, and the root widened in the first copy reaches N, which ends with R and U, though '
    'no Synchronize gives anything beyond both copies. The sequence the write above names: a '
    'widening of the root in copy 1 renews G, where N still holds its empty rule; once both ways, '
    'N\'s version, taken off in copy 0 seeing R, cuts what G lets through to R; a narrowing of the '
    'root to R in copy 0 renews G and leaves N, which was cutting; and a widening again renews G '
    'and N, so that N, cut no longer by the time of that widening, follows it. What the model '
    'cannot express: it holds a switch under an object of its own name, sw: and the node, which '
    'store.py writes as switch: and the node\'s identifier; and its acts compose a version at the '
    'write, with no buffer: an act reads the copy\'s file as it stands when the act is played and '
    'writes at once, where STORE.md 629 has a rule set in a node\'s dialog written at the dialog\'s '
    'OK, naming what the tab showed at the press. So no vector holds a write at a dialog\'s OK that '
    'names only the versions the tab showed, nor a version that reached the database under an open '
    'dialog and is left by the write above; and the only write of several rows is the rule act, '
    'which sets the rows of a table at a single node, so no write here sets a row and the switch '
    'together.')



# ADDED (lost settled): the sequences of measured-lost-settled, played by the model with the tags of
# names under the model's key K, so that its acts see what read.py reads: two names whose tags meet
# and a name whose tag is 0, from model/lost_runs.py.
def lost_command(op):
    if op[0] == 'merge':
        return 'Synchronize'
    if op[0] == 'row':
        return 'Remove row' if op[4] == 'off' else 'Set row'
    return {'switch': 'Set switch', 'rule': 'Set rows', 'settle': 'Settle row', 'padd': 'Add name',
            'lose': 'Lose version', 'del': 'Delete by hand', 'plain': 'Set object with no slash'}[op[0]]


if REFINED:
    import lost_runs as LR      # noqa: E402
    M.TAG_OF = LR.tag
    for label, what, profs, levels, root_rows, ops in LR.hand_sequences():
        M.ROOT_ROWS = root_rows
        out['writes']['sequences']['lost, %s: %s' % (label, what)] = play(
            2, [(lost_command(op), op) for op in ops], (profs, 'rw', levels))
    M.ROOT_ROWS, M.TAG_OF = None, None
    configure(('pf', 'pg'), 'rw', 2)
    out['writes']['about'] += (
        ' Every sequence here is played and read with the refinement of measured-lost-settled '
        '(crdt_model.JUDGE and read.JUDGE lost), which plays and reads every other sequence as it '
        'was. The sequences whose names begin with lost and a comma are its sequences, played with '
        'the tag of every name under the key of the model\'s label K as the model\'s acts see it '
        '(crdt_model.TAG_OF), so that what an act names is what read.py reads: p is %s and q %s, '
        'whose tags meet at %s, z is %s, whose tag is 0000, and r is pf. F1 is the sequence the '
        'measured reading is fail-open in, E1 to E3 ordinary edits of a name whose tag meets '
        'another\'s, L1 to L4 a loss at their place and each order of settling it, T1 to T3 the '
        'sequence of the product\'s test of two names whose tags meet, its order of settling '
        'turned and its copies exchanged, Z1 and Z2 a name whose tag is 0 beside a version of the '
        'object rule: and N with no slash, which is no node\'s rule, and W1 a lost version of the '
        'switch. In T1 to T3 the profiles pf and pg alone hold rows at the root, and p and q hold '
        'none. The further commands: Add name is the model\'s padd, a write that touches no rule; '
        'Settle row its settle act, a row that refuses for a lost version set to the letters its '
        'standing versions all grant; Lose version its lose act, a deletion record of a version of '
        'the object whose content no file holds; Delete by hand its del act, an entry of the store '
        'deleted for good with a deletion record or with none; and Set object with no slash its '
        'plain act. A version names in after the versions of its own object it replaces and what '
        'those name in after, and in lost the lost versions its row or switch shows at the write '
        'and what the versions it replaces name in lost. No Synchronize of these gives anything '
        'beyond both copies.' % (LR.P, LR.Q, LR.tag(LR.P), LR.Z))


# ADDED (renewal answers): A1 to A14 of probe/answers.py, played by the model with the answers on,
# every certificate and client key in hexadecimal, then a Synchronize of copy 1 into copy 0 and of
# copy 0 into copy 1, then the acts that follow in copy 0.
def answer_command(op):
    if op[0] == 'merge':
        return 'Synchronize'
    if op[0] == 'renew':
        return {'forever': 'Refuse forever', 'until1': 'Refuse until a date', 'until2': 'Refuse until a date',
                'ask': 'Allow renewal'}[op[3]]
    return {'see': 'Show answer', 'clock': 'Set clock', 'renewcert': 'Renew', 'pair': 'Pair',
            'revoke': 'Revoke', 'del': 'Delete by hand', 'reseal': 'Seal again', 'lose': 'Lose version',
            'unreadable': 'Write text out of form'}[op[0]]


if ANSWERING:
    import answers as AN        # noqa: E402
    M.ANSWERS = R.ANSWERS = S.HEX = True
    for label, what, ops, then in AN.histories():
        sequence_made = play(2, [(answer_command(op), op) for op in ops + [INTO_0, INTO_1] + AN.bound(then, 0)])
        sequence_made['clients'] = {c: S.cert_hex(c) for c in ('c0', 'c1')}
        sequence_made['clock'] = list(M.CLOCK)
        out['writes']['sequences']['answers, %s: %s' % (label, what)] = sequence_made
    M.ANSWERS = R.ANSWERS = S.HEX = False
    out['writes']['about'] += (
        ' The sequences whose names begin with answers and a comma are A1 to A14 of '
        'measured-renewal-answers, the standing answers to a renewal, played by the model with its '
        'answers on (crdt_model.ANSWERS) and read by read.py with its reading of answers on: each '
        'sequence\'s acts, then a Synchronize of copy 1 into copy 0 and of copy 0 into copy 1, then the '
        'acts that follow in copy 0. Every certificate and client key is written as 64 lowercase '
        'hexadecimal characters, the SHA-256 of kpg-store-as-a-whole/certificate/ and the model\'s name, '
        'a client\'s key that of its first certificate, and clients gives the key of c0 and of c1. An '
        'answer is a version of renew: and the client key, its text refuse:forever, refuse:until: and a '
        'moment, or ask, as in the records file; clock gives the three moments of the clock, and every '
        'step gives the clock of the copy whose file follows and its answer as renewals, as the meanings '
        'file gives it, and every Synchronize adds renewalsBeyond, each client whose answer after the '
        'merge is below both copies\' just before it, of which there is none. The further commands: '
        'Refuse forever and Refuse until a date are the model\'s renew act with forever and with a '
        'moment, given only where the copy\'s reading asks at its clock, for a client admitted there; '
        'Allow renewal its renew act with ask, pressed only where the copy\'s reading refuses at its '
        'clock, naming in after the standing versions it was shown that still stand and in lost the '
        'lost versions it was shown, writing nothing where it names nothing; Show answer its see act, '
        'a dialog that shows the answer, whose shown versions the next answer of that copy about that '
        'client names, and no other; Set clock its clock act; Renew its renewcert act, refused where the '
        'copy\'s reading refuses at its clock; Pair and Revoke as before; Delete by hand, Seal again and '
        'Lose version as before; and Write text out of form its unreadable act, somebody holding the store '
        'key writing an answer whose text is refuse:until:2026-12-15 and a summary entry.')


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
if ARGS == ['--readings']:      # ADDED (lost settled): ARGS for sys.argv[1:], here and below
    json.dump(product(out['readings']), sys.stdout, indent=2)
    sys.stdout.write('\n')
elif ARGS in (['--records'], ['--meanings'], ['--writes']):    # ADDED (for the writes)
    json.dump(out[ARGS[0][2:]], sys.stdout, indent=2, sort_keys=True)
    sys.stdout.write('\n')
else:
    json.dump(out, sys.stdout, indent=1, sort_keys=True)
