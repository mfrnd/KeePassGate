"""Compares a fresh console run of this family with the record: written on the branch.

What the emulator left cannot be made again here, so four things are compared, each line endings
ignored:

1. **The bases.** Every `work\\base\\<name>.kdbx.dump.txt` that `StoreProbe make` and `dumpdir`
   write is compared with `recorded\\base\\`, the first line masked: it names the file's SHA-256,
   which the random salts and IVs of every save change. Its size is reported, not compared.
2. **The recorded diffs are the diffs of the recorded dumps.** Each `*.diff.txt` under
   `recorded\\runs\\` and `recorded\\sync\\` is read as the lines it removes and adds and compared
   with the difference of the two dumps it was made from, the base named on its first line (for a
   run) or the dumps `sync.sh` names (for step 8); and each run's diff is made again against the
   fresh base, which must change nothing but the base's own first line.
3. **Step 8, where it was run again.** If `work\\sync\\` exists, each dump in it is compared with
   `recorded\\sync\\`, the first line masked, and in KeePassDroid's labels the `M settingsChanged`
   line too: KeePassDroid writes no `SettingsChanged`, so every read of its file answers the moment
   of reading. The diffs are compared as in 2, with the same lines set aside.
4. **The MD5 pair** of ground-android.md 5.1: `md5pair\\out\\md5pair.log` with the running time on
   its first line masked, and both bodies byte for byte.

And for the two later measurements, which share the later probe in `probe\\overwrite\\`:

5. **Their bases.** `work\\base\\` OLD, NEW-same, NEW-diff and NEW-ident against
   `recorded\\overwrite\\base\\`, and `sync\\base\\` SOLD and SNEW-fixed against
   `recorded\\k2a-synchronize\\base\\`, the first line masked and the size reported.
6. **Their recorded diffs are the diffs of their recorded dumps**: under `recorded\\overwrite\\runs\\`
   each `vs-NEW` against NEW-diff's dump and each `vs-OLD` against OLD's; under
   `recorded\\k2a-synchronize\\runs\\` each `vs-SOLD` against SOLD's and each `vs-SNEW` against the
   run's own SNEW where its directory holds a SNEW of its own, SNEW-fixed where it does not.
7. **KeePass's own Synchronize, where it was run again.** If `work\\keepass-sync\\` exists, each
   `<run>\\keepass-sync\\*.dump.txt` in it against the record's, the first line masked.

Exits 0 when nothing differs.

    uv run --no-project python -I -X utf8 spikes\\storage\\android\\compare.py
"""
import os
import re
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
REC = os.path.join(HERE, 'recorded')
WORK = os.path.join(HERE, 'work')


def lines(path):
    with open(path, encoding='utf-8') as fh:
        return [x.rstrip('\r\n') for x in fh]


def body(path):
    got = lines(path)
    return got[1:] if got and got[0].startswith('# file') else got


def diff_sides(path):
    gone, new = Counter(), Counter()
    for x in lines(path):
        if x.startswith('< '):
            gone[x[2:]] += 1
        elif x.startswith('> '):
            new[x[2:]] += 1
    return gone, new


def dump_delta(a, b):
    ca, cb = Counter(lines(a)), Counter(lines(b))
    return ca - cb, cb - ca


def drop(c, rx):
    return Counter({k: v for k, v in c.items() if not re.search(rx, k)})


FAILED = []


def report(label, ok, detail=''):
    print('%-62s %s%s' % (label, 'the same' if ok else 'DIFFERS', (': ' + detail) if detail and not ok else ''))
    if not ok:
        FAILED.append(label)


def bases():
    fresh = os.path.join(WORK, 'base')
    if not os.path.isdir(fresh):
        print('work\\base is absent: run StoreProbe make and dumpdir first (RUN.md)')
        FAILED.append('bases')
        return
    for name in sorted(os.listdir(os.path.join(REC, 'base'))):
        r, f = os.path.join(REC, 'base', name), os.path.join(fresh, name)
        if not os.path.exists(f):
            report('base ' + name, False, 'no fresh dump')
            continue
        size_r = re.search(r'size=(\d+)', lines(r)[0]).group(1)
        size_f = re.search(r'size=(\d+)', lines(f)[0]).group(1)
        ok = body(r) == body(f)
        report('base %s, %d lines (size %s, recorded %s)' % (name, len(body(r)), size_f, size_r), ok)


def base_of(diff):
    first = [x for x in lines(diff) if x.startswith('< # file ')]
    return first[0].split()[3] if first else None


def recorded_diffs():
    n = bad = 0
    again = 0
    fresh = os.path.join(WORK, 'base')
    for top, _, files in os.walk(os.path.join(REC, 'runs')):
        for f in sorted(files):
            if not f.endswith('.diff.txt'):
                continue
            d = os.path.join(top, f)
            dump = d[:-len('.diff.txt')] + '.dump.txt'
            base = os.path.join(REC, 'base', base_of(d) + '.dump.txt')
            n += 1
            g1, n1 = diff_sides(d)
            g2, n2 = dump_delta(base, dump)
            if (g1, n1) != (g2, n2):
                bad += 1
                report('recorded diff ' + os.path.relpath(d, REC), False, 'not the diff of its dumps')
            fb = os.path.join(fresh, base_of(d) + '.dump.txt')
            if os.path.exists(fb):
                g3, n3 = dump_delta(fb, dump)
                if drop(g3, r'^# file ') != drop(g2, r'^# file ') or n3 != n2:
                    bad += 1
                    report('run diff against the fresh base ' + os.path.relpath(d, REC), False)
                else:
                    again += 1
    report('%d run diffs are the diffs of their recorded dumps; %d made again against the fresh base' % (n, again),
           bad == 0)
    n = bad = 0
    for label in sorted(os.listdir(os.path.join(REC, 'sync'))):
        s = os.path.join(REC, 'sync', label)
        for r in ('into-kp', 'into-phone'):
            for other, against in (('vs-phone', 'phone'), ('vs-keepass', 'keepass')):
                n += 1
                d = os.path.join(s, '%s.%s.diff.txt' % (r, other))
                if diff_sides(d) != dump_delta(os.path.join(s, against + '.dump.txt'), os.path.join(s, r + '.dump.txt')):
                    bad += 1
                    report('recorded diff sync\\%s\\%s.%s' % (label, r, other), False)
            n += 1
            d = os.path.join(s, '%s.vs-base.diff.txt' % r)
            name = base_of(d)
            if diff_sides(d) != dump_delta(os.path.join(REC, 'base', name + '.dump.txt'), os.path.join(s, r + '.dump.txt')):
                bad += 1
                report('recorded diff sync\\%s\\%s.vs-base' % (label, r), False)
    report('%d step-8 diffs are the diffs of their recorded dumps' % n, bad == 0)


def mask_sync(label, ls):
    out = [re.sub(r'^# file (\S+) size=\d+ sha256=\w+', r'# file \1', x) for x in ls]
    if label.startswith('kpd-'):
        out = [x for x in out if not x.startswith('M settingsChanged ')]
    return out


def sync_again():
    fresh = os.path.join(WORK, 'sync')
    if not os.path.isdir(fresh):
        print('work\\sync is absent: step 8 was not run again here, which needs the files the phones saved')
        return
    same = total = 0
    for label in sorted(os.listdir(os.path.join(REC, 'sync'))):
        for f in sorted(os.listdir(os.path.join(REC, 'sync', label))):
            r, w = os.path.join(REC, 'sync', label, f), os.path.join(fresh, label, f)
            if not os.path.exists(w):
                report('step 8 sync\\%s\\%s' % (label, f), False, 'not made again')
                continue
            total += 1
            if f.endswith('.dump.txt'):
                ok = mask_sync(label, lines(r)) == mask_sync(label, lines(w))
            else:
                rx = r'^(# file |M settingsChanged )' if label.startswith('kpd-') else r'^# file '
                g1, n1 = diff_sides(r)
                g2, n2 = diff_sides(w)
                ok = (drop(g1, rx), drop(n1, rx)) == (drop(g2, rx), drop(n2, rx))
            if ok:
                same += 1
            else:
                report('step 8 sync\\%s\\%s' % (label, f), False)
    report('step 8 made again: %d of %d files' % (same, total), same == total)


def md5pair():
    out = os.path.join(HERE, 'md5pair', 'out')
    rec = os.path.join(HERE, 'md5pair', 'recorded')
    if not os.path.isdir(out):
        print('md5pair\\out is absent: run the MD5 pair probe first (RUN.md)')
        FAILED.append('md5pair')
        return

    def m(ls):
        return [re.sub(r' seconds=[\d.]+ ', ' seconds=<time> ', x) for x in ls]
    report('md5pair.log, the running time masked', m(lines(os.path.join(rec, 'md5pair.log'))) ==
           m(lines(os.path.join(out, 'md5pair.log'))))
    for b in ('a.bin', 'b.bin'):
        with open(os.path.join(rec, 'md5pair', b), 'rb') as fh:
            x = fh.read()
        with open(os.path.join(out, 'md5pair', b), 'rb') as fh:
            y = fh.read()
        report('md5pair\\' + b + ', byte for byte', x == y)


def later_bases():
    for rec, fresh, names in ((os.path.join(REC, 'overwrite', 'base'), os.path.join(WORK, 'base'),
                               ('OLD', 'NEW-same', 'NEW-diff', 'NEW-ident')),
                              (os.path.join(REC, 'k2a-synchronize', 'base'), os.path.join(HERE, 'sync', 'base'),
                               ('SOLD', 'SNEW-fixed'))):
        for name in names:
            r, f = os.path.join(rec, name + '.kdbx.dump.txt'), os.path.join(fresh, name + '.kdbx.dump.txt')
            if not os.path.exists(f):
                report('later base ' + name, False, 'no fresh dump: run.ps1 makes it')
                continue
            size_r = re.search(r'size=(\d+)', lines(r)[0]).group(1)
            size_f = re.search(r'size=(\d+)', lines(f)[0]).group(1)
            a, b = body(r), body(f)
            note = ''
            if name == 'NEW-same':
                # Its padding is searched until the file is OLD's length, which the random IVs move.
                pad = [re.search(r'len=(\d+)', x).group(1) for x in (a + b) if x.startswith('E RV S Notes ')]
                a, b = [x for x in a if not x.startswith('E RV S Notes ')], [x for x in b if not x.startswith('E RV S Notes ')]
                note = ', RV\'s notes %s, recorded %s, masked' % (pad[1], pad[0])
            report('later base %s, %d lines (size %s, recorded %s%s)' % (name, len(body(r)), size_f, size_r, note), a == b)


def later_diffs():
    n = bad = 0
    ow = os.path.join(REC, 'overwrite')
    for top, _, files in os.walk(os.path.join(ow, 'runs')):
        for f in sorted(files):
            m = re.match(r'^(.*)\.vs-(NEW|OLD)\.diff\.txt$', f)
            if not m:
                continue
            n += 1
            base = os.path.join(ow, 'base', ('NEW-diff' if m.group(2) == 'NEW' else 'OLD') + '.kdbx.dump.txt')
            if diff_sides(os.path.join(top, f)) != dump_delta(base, os.path.join(top, m.group(1) + '.dump.txt')):
                bad += 1
                report('recorded diff ' + os.path.relpath(os.path.join(top, f), REC), False, 'not the diff of its dumps')
    sy = os.path.join(REC, 'k2a-synchronize')
    for top, _, files in os.walk(os.path.join(sy, 'runs')):
        for f in sorted(files):
            m = re.match(r'^(.*)\.vs-(SOLD|SNEW)\.diff\.txt$', f)
            if not m:
                continue
            n += 1
            own = os.path.join(top, 'SNEW.kdbx.dump.txt')
            if m.group(2) == 'SOLD':
                base = os.path.join(sy, 'base', 'SOLD.kdbx.dump.txt')
            else:
                base = own if os.path.exists(own) else os.path.join(sy, 'base', 'SNEW-fixed.kdbx.dump.txt')
            if diff_sides(os.path.join(top, f)) != dump_delta(base, os.path.join(top, m.group(1) + '.dump.txt')):
                bad += 1
                report('recorded diff ' + os.path.relpath(os.path.join(top, f), REC), False, 'not the diff of its dumps')
    report('%d diffs of the later measurements are the diffs of their recorded dumps' % n, bad == 0)


def keepass_sync_again():
    fresh = os.path.join(WORK, 'keepass-sync')
    if not os.path.isdir(fresh):
        print('work\\keepass-sync is absent: KeePass\'s own Synchronize was not run again here, which needs the files '
              'the phone left')
        return
    same = total = 0
    for run in sorted(os.listdir(fresh)):
        rec = os.path.join(REC, 'k2a-synchronize', 'runs', run, 'keepass-sync')
        for f in sorted(os.listdir(rec)):
            if not f.endswith('.dump.txt'):
                continue
            w = os.path.join(fresh, run, 'keepass-sync', f)
            if not os.path.exists(w):
                report('keepass-sync %s\\%s' % (run, f), False, 'not made again')
                continue
            total += 1
            if body(os.path.join(rec, f)) == body(w):
                same += 1
            else:
                report('keepass-sync %s\\%s' % (run, f), False)
    report('KeePass\'s own Synchronize made again: %d of %d dumps' % (same, total), same == total and total > 0)


def main():
    bases()
    recorded_diffs()
    sync_again()
    md5pair()
    later_bases()
    later_diffs()
    keepass_sync_again()
    print('== %s' % ('nothing differs' if not FAILED else '%d differ' % len(FAILED)))
    return 1 if FAILED else 0


if __name__ == '__main__':
    sys.exit(main())
