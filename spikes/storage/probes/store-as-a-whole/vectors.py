"""Vectors for the product's reading of the store, made by the measured code and by nothing else.

Run offline, from anywhere:

  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py
  uv run --no-project python -I -X utf8 spikes/storage/probes/store-as-a-whole/vectors.py --readings

It starts no program and reads no file but `probe/store.py` and `probe/read.py` beside it. Every
value printed is an output of that code: `hm`, `tag`, `subject_hash`, `mac`, `ident` and `dumps` of
store.py, and `read` of read.py, with its history rule on, over a dump composed here. The inputs are
invented and written out beside each value. The records of the kinds the measured code never wrote
(`withdrawn`, `kept`, `format`) are composed with the same functions, so their vectors hold the
product to the measured check and identifier, and to nothing the measured code ran of those kinds.

Without an argument it prints every vector. With `--readings` it prints the file the product keeps
as `tests/Shared/StoreReadingVectors.json`, byte for byte: the small stores of part 4 and what
read.py answered for each. That file renames and adds nothing but what `STATES` and `ABOUT` hold:
the three words of read.py's `status` are written as the names the product gives those states.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'probe'))
import read as R      # noqa: E402
import store as S     # noqa: E402

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
else:
    json.dump(out, sys.stdout, indent=1, sort_keys=True)
