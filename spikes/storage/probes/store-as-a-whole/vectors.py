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
`--records` prints `tests/Shared/StoreRecordVectors.json`: records in the forms STORE.md 614, 615
and 648 fix, and summary entries, composed as `rec` composes them (part 5). `--meanings` prints
`tests/Shared/StoreMeaningVectors.json`: small stores of those records and what read.py answers for
each about authorities, admissions and refusals (part 6). `--writes` prints
`tests/Shared/StoreWriteVectors.json`: the file after each act of three fixed sequences that the
model plays, written out by store.py's `Encoder`, with read.py's answer after each (part 7). Part
7 reads more than store.py and read.py: the model, `model/crdt_model.py`, and `probe/bridge.py`
for the switches the family's runs give it. The script imports both whatever it prints, and
starts no program for them either.
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
}}


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
}
stores = {}
for name, d in sorted(meanings.items()):
    stores[name] = dict(files(d), **answer(R.read(d, history=True)))
out['meanings'] = {'about': MEANINGS_ABOUT, 'key': ONE.hex(), 'meanings': stores}


# 7. ADDED (vectors for the writes): the file after each act of a fixed sequence the model plays,
# written out by store.py's Encoder as the family's runs write it, and read by read.py.
def described(enc, eid):
    """A record of the model as the Encoder writes it: identifier, kind, subject and number."""
    u, title, s, prot = enc.entry(eid)
    kind, d = s['KPG.Kind'], json.loads(s['KPG.Data'])
    return {'id': u, 'kind': kind, 'subject': S.subject_of(kind, d),
            'number': int(d['height']) if kind == 'head' else 0}


def by_record(x):
    return (x['kind'], x['subject'], x['number'], x['id'])


def state(W, enc, i):
    """File i of the model as the Encoder writes it, described, and what read.py answers for
    it."""
    rep = W.reps[i]
    eids = sorted(eid for u, (eid, mt, ok) in rep.entries.items() if u == eid and ok)
    written = [enc.entry(eid) for eid in eids]
    d = dump([(u, s, []) for (u, title, s, prot) in written], [enc.u(x) for x in rep.dels])
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
            'answer': answer(R.read(d, history=True)), 'file': files(d)}


def play(nrep, steps):
    """The model's own store, then each (command, act) of steps played by the model, each with
    the file of the copy it wrote and the deletion records it added there."""
    W = M.World(nrep, B.FLAGS)
    W.maxclients = 4
    enc = S.Encoder(W, {'root': ROOT})
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
    'The file after each act of three fixed sequences that the model plays, World.step and '
    'World.act of model/crdt_model.py of spikes/storage/probes/store-as-a-whole on '
    'spikes/storage-survival under the FLAGS of probe/bridge.py, the store as decided, written '
    'out as entries by store.py\'s Encoder as the family\'s runs write them, and read by read.py '
    'with its history rule on; made by vectors.py beside the probe and never from this side. '
    'Each sequence starts from the store the model\'s World starts with, given as start: the '
    'authority a0, the profile name pf, the admission of the client c0 under a0 with the '
    'profile pf alone, a version of the rule of the root granting read, and a summary entry of '
    'height 1, each under the key label K; the two copies of the second and of the third '
    'sequence start as the same file. A pairing admits its client with the profiles pf and pg, '
    'and a renewal with the profiles of the admission renewed. The first sequence raises the '
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
}}



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
