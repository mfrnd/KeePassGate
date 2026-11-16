"""Single transport facts the CRDT model rests on, measured on real files: KeePassLib 2.62 in all
five methods and keepassxc-cli 2.4.2 to 2.8.0. Run with Python 3, no packages."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kp  # noqa: E402

WORK = os.path.join(kp.SURVIVAL, 'work', 'facts')
METHODS = ['Synchronize', 'OverwriteIfNewer', 'OverwriteExisting', 'KeepExisting', 'CreateNewUuids']
SUFFIX = '0123456789abcdef'
out = []


def say(s=''):
    print(s)
    out.append(s)


def f(name):
    return os.path.join(WORK, name + '.kdbx')


def spec(lines):
    p = os.path.join(WORK, 'spec.txt')
    with open(p, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')
    return p


def A(id_, t, kind='ver', extra=''):
    return '\t'.join(['A', kp.uid(id_), str(t), kind, id_, '{"v":"2","id":"%s","nonce":"%s"}' % (id_, kp.uid(id_)), extra])


def D(id_, t):
    return '\t'.join(['D', kp.uid(id_), str(t)])


def R(id_):
    return '\t'.join(['R', kp.uid(id_)])


def show(h, path, names):
    d = h.dump(path)
    rev = {kp.uid(n): n for n in names}
    ents = []
    for u, e in sorted(d['entries'].items(), key=lambda x: x[1]['id']):
        nm = rev.get(u, 'NEWUUID(' + e['id'] + ')')
        ents.append('%s[mt=%d loc=%d %s%s]' % (nm, e['mt'], e['loc'], e['place'], '' if e['intact'] else ' damaged'))
    dels = ['%s@%d' % (rev.get(u, u[:8]), t) for u, t in sorted(d['dels'].items(), key=lambda x: rev.get(x[0], x[0]))]
    return 'entries: ' + (', '.join(ents) or 'none') + ' | records: ' + (', '.join(dels) or 'none')


def main():
    os.makedirs(WORK, exist_ok=True)
    for x in os.listdir(WORK):
        os.remove(os.path.join(WORK, x))
    h = kp.Host()
    try:
        # ---- F1: a deletion record dated in the same second as the entry it names
        say('## F1. A record dated the same second as its entry (entry E at 100, record at 100)')
        h.run('new', f('f1base'), SUFFIX)
        h.run('patch', f('f1base'), spec([A('E', 100)]))
        kp.copy(f('f1base'), f('f1keep'))
        kp.copy(f('f1base'), f('f1del'))
        h.run('patch', f('f1del'), spec([R('E'), D('E', 100)]))
        say('  f1keep: ' + show(h, f('f1keep'), ['E']))
        say('  f1del:  ' + show(h, f('f1del'), ['E']))
        for m in METHODS[:4]:
            h.run('merge', f('f1keep'), f('f1del'), m, f('f1-into-keep-' + m))
            h.run('merge', f('f1del'), f('f1keep'), m, f('f1-into-del-' + m))
            say('  %-17s into the copy holding E: %s' % (m, show(h, f('f1-into-keep-' + m), ['E'])))
            say('  %-17s into the copy holding the record: %s' % (m, show(h, f('f1-into-del-' + m), ['E'])))
        say('  the same with the record one second later (101), Synchronize into the copy holding E:')
        kp.copy(f('f1base'), f('f1del2'))
        h.run('patch', f('f1del2'), spec([R('E'), D('E', 101)]))
        h.run('merge', f('f1keep'), f('f1del2'), 'Synchronize', f('f1-into-keep-101'))
        say('    ' + show(h, f('f1-into-keep-101'), ['E']))

        # ---- F2: KeePassDroid's editor Save (T3) on a pruned element, then a merge with the gate's copy
        say('')
        say('## F2. An element the gate pruned (record at 200), saved unchanged on the phone at 300 (T3)')
        h.run('new', f('f2base'), SUFFIX)
        h.run('patch', f('f2base'), spec([A('V1', 100), A('H1', 100, 'head')]))
        kp.copy(f('f2base'), f('f2gate'))
        kp.copy(f('f2base'), f('f2phone'))
        h.run('patch', f('f2gate'), spec([A('V2', 200), A('H2', 200, 'head'), R('V1'), R('H1'), D('V1', 200), D('H1', 200)]))
        h.run('touch', f('f2phone'), kp.uid('V1'), 300)
        h.run('touch', f('f2phone'), kp.uid('H1'), 300)
        names = ['V1', 'V2', 'H1', 'H2']
        say('  gate:  ' + show(h, f('f2gate'), names))
        say('  phone: ' + show(h, f('f2phone'), names))
        for m in METHODS[:4]:
            h.run('merge', f('f2gate'), f('f2phone'), m, f('f2-into-gate-' + m))
            h.run('merge', f('f2phone'), f('f2gate'), m, f('f2-into-phone-' + m))
            say('  %-17s into gate:  %s' % (m, show(h, f('f2-into-gate-' + m), names)))
            say('  %-17s into phone: %s' % (m, show(h, f('f2-into-phone-' + m), names)))
        for v in kp.XC:
            kp.copy(f('f2gate'), f('f2-xc-gate-' + v))
            r = kp.xc_merge(v, f('f2-xc-gate-' + v), f('f2phone'))
            say('  keepassxc-cli %-6s into gate (exit %d): %s' % (v, r[0], show(h, f('f2-xc-gate-' + v), names)))

        # ---- F3: a store element moved into the recycle bin, then each merge
        say('')
        say('## F3. The head binned in one copy (location time 400, modification time unchanged), each merge')
        h.run('new', f('f3base'), SUFFIX)
        h.run('patch', f('f3base'), spec([A('H2', 200, 'head'), A('V2', 200)]))
        kp.copy(f('f3base'), f('f3binned'))
        kp.copy(f('f3base'), f('f3other'))
        h.run('bin', f('f3binned'), kp.uid('H2'), 400)
        names = ['H2', 'V2']
        say('  binned: ' + show(h, f('f3binned'), names))
        for m in METHODS[:4]:
            h.run('merge', f('f3other'), f('f3binned'), m, f('f3-into-other-' + m))
            h.run('merge', f('f3binned'), f('f3other'), m, f('f3-into-binned-' + m))
            say('  %-17s into the other copy:  %s' % (m, show(h, f('f3-into-other-' + m), names)))
            say('  %-17s into the binned copy: %s' % (m, show(h, f('f3-into-binned-' + m), names)))
        for v in kp.XC:
            kp.copy(f('f3other'), f('f3-xc-other-' + v))
            kp.copy(f('f3binned'), f('f3-xc-binned-' + v))
            r1 = kp.xc_merge(v, f('f3-xc-other-' + v), f('f3binned'))
            r2 = kp.xc_merge(v, f('f3-xc-binned-' + v), f('f3other'))
            say('  keepassxc-cli %-6s into the other copy (exit %d):  %s' % (v, r1[0], show(h, f('f3-xc-other-' + v), names)))
            say('  keepassxc-cli %-6s into the binned copy (exit %d): %s' % (v, r2[0], show(h, f('f3-xc-binned-' + v), names)))

        # ---- F4: a witness (a record naming nothing) through every merge
        say('')
        say('## F4. A witness record naming no object, held by one copy only, through every merge')
        h.run('new', f('f4base'), SUFFIX)
        h.run('patch', f('f4base'), spec([A('H1', 100, 'head')]))
        kp.copy(f('f4base'), f('f4w'))
        kp.copy(f('f4base'), f('f4o'))
        h.run('patch', f('f4w'), spec([D('T|revoked|c0', 150)]))
        names = ['H1', 'T|revoked|c0']
        for m in METHODS:
            h.run('merge', f('f4o'), f('f4w'), m, f('f4-into-o-' + m))
            h.run('merge', f('f4w'), f('f4o'), m, f('f4-into-w-' + m))
            say('  %-17s into the copy without: %s' % (m, show(h, f('f4-into-o-' + m), names)))
            say('  %-17s into the witness copy: %s' % (m, show(h, f('f4-into-w-' + m), names)))
        for v in kp.XC:
            kp.copy(f('f4o'), f('f4-xc-o-' + v))
            kp.copy(f('f4w'), f('f4-xc-w-' + v))
            r1 = kp.xc_merge(v, f('f4-xc-o-' + v), f('f4w'))
            r2 = kp.xc_merge(v, f('f4-xc-w-' + v), f('f4o'))
            say('  keepassxc-cli %-6s into the copy without (exit %d): %s' % (v, r1[0], show(h, f('f4-xc-o-' + v), names)))
            say('  keepassxc-cli %-6s into the witness copy (exit %d): %s' % (v, r2[0], show(h, f('f4-xc-w-' + v), names)))

        # ---- F5: File then Import (Create new IDs) of a copy, then Delete Duplicate Entries, then Synchronize
        say('')
        say('## F5. Import with new identifiers of a stale copy, Delete Duplicate Entries, Synchronize to a third copy')
        h.run('new', f('f5base'), SUFFIX)
        els = ['auth:a0', 'rec:c0', 'padd:pf#1', 'ver:rule:root#2', 'H001.3']
        h.run('patch', f('f5base'), spec([A('auth:a0', 1, 'auth'), A('rec:c0', 1, 'rec'), A('padd:pf#1', 1, 'padd'),
                                          A('ver:rule:root#2', 1), A('H001.3', 10, 'head')]))
        kp.copy(f('f5base'), f('f5gate'))
        kp.copy(f('f5base'), f('f5backup'))
        kp.copy(f('f5base'), f('f5third'))
        h.run('patch', f('f5gate'), spec([A('ref:c0#4', 20, 'ref'), A('H002.5', 20, 'head'), R('H001.3'), D('H001.3', 20),
                                          R('rec:c0'), D('rec:c0', 20), D('T|revoked|c0', 20)]))
        names = els + ['ref:c0#4', 'H002.5', 'T|revoked|c0']
        say('  gate before:   ' + show(h, f('f5gate'), names))
        h.run('merge', f('f5gate'), f('f5backup'), 'CreateNewUuids', f('f5imported'))
        say('  after import:  ' + show(h, f('f5imported'), names))
        kp.copy(f('f5imported'), f('f5dedup'))
        say('  Delete Duplicate Entries: ' + h.run('dedupe', f('f5dedup')))
        say('  after dedupe:  ' + show(h, f('f5dedup'), names))
        h.run('merge', f('f5third'), f('f5dedup'), 'Synchronize', f('f5third-synced'))
        say('  third copy, Synchronize with it: ' + show(h, f('f5third-synced'), names))
    finally:
        h.close()
    with open(os.path.join(kp.SURVIVAL, 'out', 'facts.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(out) + '\n')


if __name__ == '__main__':
    main()
