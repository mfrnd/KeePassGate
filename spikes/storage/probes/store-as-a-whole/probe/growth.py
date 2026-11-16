"""B.7 of the family store-as-a-whole: the head and the store as the plugin writes, and the read.

Run:  uv run --no-project python -I -X utf8 growth.py

One gate writes 1,000 times through the model's write path (the store as decided: no limit of 64,
a revocation never pruned), over a database with a realistic population: 10 clients admitted, 5
profile names, 20 ruled nodes. The writes are drawn with a fixed seed: a rule changed at a ruled
node (60 in 100), a renewal answer (15), a client revoked and another paired in its place (10), a
name added or removed (10), a name added back (5). After 10, 100 and 1,000 writes the store is
written into a real file through KeePassLib (store.py's entries, the deletion records), dumped, and
read by read.py; the sizes are of the strings the plugin writes. The read is timed in this Python
process, by its median of seven runs: not on KeePass's thread, and not the C# a plugin would run.
"""
import os
import random
import statistics
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'model'))
import bridge as B  # noqa: E402
import kp  # noqa: E402
import read as RD  # noqa: E402
import store as S  # noqa: E402
import crdt_model as M  # noqa: E402

NODES = ['n%02d' % k for k in range(20)]
NAMES = ['pf', 'pg', 'p3', 'p4', 'p5']


def setup():
    W = M.World(1, B.FLAGS)
    W.maxclients = 1000
    for n in NAMES[1:]:
        assert W.step(('padd', 0, n))
    while len(W.fold(W.reps[0])['adm']) < 10:
        assert W.step(('pair', 0))
    for n in NODES:
        assert W.step(('rule', 0, n, 'r'))
    return W


def one_write(W, rng):
    v = W.fold(W.reps[0])
    x = rng.random()
    if x < 0.60:
        return W.step(('rule', 0, rng.choice(NODES), rng.choice(['rw', 'r', 'none', 'inherit'])))
    if x < 0.75:
        c = rng.choice(sorted(M.ckey(c) for c in v['adm']))
        return W.step(('renew', 0, c, rng.choice(['forever', 'allowed'])))
    if x < 0.85:
        c = rng.choice(sorted(M.ckey(c) for c in v['adm']))
        return W.step(('revoke', 0, c)) and W.step(('pair', 0))
    if x < 0.95:
        n = rng.choice(NAMES[1:])
        return W.step(('prem', 0, n)) if n in v['names'] else W.step(('padd', 0, n))
    n = rng.choice(NAMES[1:])
    return W.step(('padd', 0, n))


def measure(host, W, label, path):
    nodes = {'root': B.node_ids()['root'], 'N': B.node_ids()['N']}
    for k, n in enumerate(NODES):
        nodes[n] = 'ee%030x' % k
    enc = S.Encoder(W, nodes)
    rep = W.reps[0]
    lines = []
    for u, (eid, mt, ok) in rep.entries.items():
        uu, title, strings, prot = enc.entry(eid)
        parts = ['A', uu, str(mt), kp.b64(title)]
        for k in sorted(strings):
            parts.append(('!' if k in prot else '') + k + '=' + kp.b64(strings[k]))
        lines.append('\t'.join(parts))
    for u, t in rep.dels.items():
        lines.append('\t'.join(['D', enc.u(u), str(t)]))
    host.run('new', path, '5a17c0de00000002', 'fast', B.TREE)
    spec = path + '.spec'
    with open(spec, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')
    host.run('patch', path, spec)
    os.remove(spec)
    dump = host.dump(path, history=B.DECIDED)     # ADDED (decided store): history items when decided
    times = []
    for _ in range(7):
        t = time.perf_counter()
        r = RD.read(dump, history=B.DECIDED)
        times.append(time.perf_counter() - t)
    ents = [e for e in dump['entries'].values() if 'KPG.Kind' in e['strings']]
    heads = [e for e in ents if e['strings']['KPG.Kind'] == 'head']
    h = max(heads, key=lambda e: int(RD.strict(e['strings']['KPG.Data'])['height']))
    hs = h['strings']
    hbytes = sum(len(k.encode()) + len(v.encode()) for k, v in hs.items())
    hd = RD.strict(hs['KPG.Data'])
    sbytes = sum(sum(len(k.encode()) + len(v.encode()) for k, v in e['strings'].items()) for e in ents)
    longest = max(len(v) for e in ents for v in e['strings'].values())
    vers = [RD.strict(e['strings']['KPG.Data']) for e in ents if e['strings']['KPG.Kind'] == 'version']
    maxafter = max([len(d['after'].split()) for d in vers] + [0])
    return ('%-12s head: %6d bytes in %3d KPG. strings, its after list %4d identifiers, %3d restated items; '
            'store: %3d entries, %7d bytes of KPG. strings, longest string %6d characters, longest version after list %3d; '
            'deletion records %5d; file %7d bytes; read %s, %.1f ms (median of 7, Python)'
            % (label, hbytes, len(hs), len(hd['after'].split()), len([k for k in hs if k.startswith('KPG.S.')]),
               len(ents), sbytes, longest, maxafter, len(dump['dels']), os.path.getsize(path),
               r['status'], 1000 * statistics.median(times)))


def main():
    # ADDED (decided store): --decided reads every file under the history rule (bridge.decide); the
    # writes are the same, since this gate never synchronizes and so never ran the repair
    if len(sys.argv) > 1 and sys.argv[1] == '--decided':
        B.decide()
    sys.stdout.reconfigure(line_buffering=True)
    rng = random.Random(20261116)
    t0 = time.time()
    W = setup()
    host = kp.Host()
    work = os.path.join(B.WORK, ('d-' if B.DECIDED else '') + 'growth')
    os.makedirs(work, exist_ok=True)
    print('== B.7: one gate, 10 clients, 5 names, 20 ruled nodes; writes drawn with seed 20261116')
    print(measure(host, W, 'start', os.path.join(work, 'g0.kdbx')))
    done = 0
    try:
        for target in (10, 100, 1000):
            while done < target:
                if one_write(W, rng):
                    done += 1
            print(measure(host, W, '%d writes' % target, os.path.join(work, 'g%d.kdbx' % target)))
    finally:
        host.close()
    print('== %.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
