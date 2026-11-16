"""Holds every table of `papers\\ground\\measured-android.md` to the dumps and logs: written on the branch.

The emulator runs cannot be repeated here, so what is checked is what they left: the read-back of
every file a phone saved, its diff against the file it was given, KeePassLib's Synchronize of it
back into KeePass (step 8), and the step logs. Each claim below is one cell, row or sentence of the
paper, named by its section; the check states it over the dumps, prints `holds` or `DOES NOT HOLD`
with the lines that decide it, and exits 0 when exactly the claims listed in `KNOWN` do not hold.

Two readings are this checker's, not the paper's, and are applied everywhere:

- a `parent` line names the parent by identifier and then by name, so a rename of G changes the
  parent line of everything in G; a carrier is compared with the name in parentheses taken off;
- "the entry the run added" is the entry with an identifier the base does not fix, printed `~...`,
  and the lines that count it (`F groups ... entries`, `children`) are set aside with it.

    uv run --no-project python -I -X utf8 spikes\\storage\\android\\checks\\tables.py
    ... tables.py --bases spikes\\storage\\android\\work\\base    (base claims against a fresh make)
"""
import os
import re
import sys
from collections import Counter
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
RECORDED = os.path.join(HERE, '..', 'recorded')

# Claims the record contradicts, by identifier, each said in RUN.md.
KNOWN = {'5.3-merge-fromPC-with-C-phone', '5.3-merge-fromPC-with-P-phone'}

T0 = '2026-11-01T10:00:00Z'


def read(path):
    with open(path, encoding='utf-8') as fh:
        return [x.rstrip('\r\n') for x in fh]


class Dump:
    def __init__(self, path):
        lines = read(path)
        self.path = path
        self.head = lines[0] if lines and lines[0].startswith('# file') else ''
        self.lines = lines[1:] if self.head else lines

    def get(self, rx):
        r = re.compile(rx)
        return [x for x in self.lines if r.search(x)]

    def has(self, rx):
        return bool(self.get(rx))

    def one(self, prefix):
        got = [x for x in self.lines if x.startswith(prefix)]
        return got[0] if len(got) == 1 else None


def norm(line):
    return re.sub(r' parent (\S+) \(.*\)$', r' parent \1', line)


def matching(d, rx):
    r = re.compile(rx)
    return Counter(norm(x) for x in d.lines if r.search(x))


def kept(b, r, rx):
    """The lines matching rx are the same in b and r, and b has some."""
    a, c = matching(b, rx), matching(r, rx)
    if not a:
        return False, 'the base has no line matching ' + rx
    if a == c:
        return True, '%d lines alike' % sum(a.values())
    return False, delta_text(a, c)


def delta_text(a, c, limit=6):
    gone = list((a - c).elements())
    new = list((c - a).elements())
    out = ['< ' + x for x in gone[:limit]] + ['> ' + x for x in new[:limit]]
    more = len(gone) + len(new) - len(out)
    return ' | '.join(x[:110] for x in out) + (' | ... %d more' % more if more > 0 else '')


def changed(b, r, rx):
    a, c = matching(b, rx), matching(r, rx)
    return list((a - c).elements()), list((c - a).elements())


def value(d, prefix):
    x = d.one(prefix)
    return None if x is None else x[len(prefix):].strip()


def all_of(*checks):
    notes = []
    for ok, note in checks:
        notes.append(note)
        if not ok:
            return False, note
    return True, '; '.join(n for n in notes if n)[:200]


def check(cond, note_ok, note_bad=None):
    return (True, note_ok) if cond else (False, note_bad or ('not: ' + note_ok))


def present(d, rx, note=None):
    got = d.get(rx)
    return check(bool(got), note or ('present: ' + (got[0][:100] if got else rx)),
                 'absent: ' + rx)


def absent(d, rx, note=None):
    got = d.get(rx)
    return check(not got, note or ('absent: ' + rx), 'present: ' + (got[0][:100] if got else ''))


def moved(b, r, node, what):
    x, y = value(b, node + ' ' + what), value(r, node + ' ' + what)
    return check(x is not None and y is not None and x != y, '%s %s %s -> %s' % (node, what, x, y))


def same(b, r, node, what):
    x, y = value(b, node + ' ' + what), value(r, node + ' ' + what)
    return check(x is not None and x == y, '%s %s unchanged (%s)' % (node, what, x),
                 '%s %s %s -> %s' % (node, what, x, y))


def version_added(b, r, alias, h, with_cd):
    """r's history item h of alias holds b's current strings, attachments, tags (and custom data)."""
    kinds = r'(S|A|tags)' + ('|CD' if with_cd else '')
    cur = [x[len('E %s ' % alias):] for x in b.get(r'^E %s (%s) ' % (alias, kinds))
           if not x.startswith('E %s H' % alias)]
    cur = [x for x in cur if re.match(kinds + r'\b', x)]
    hist = set(x[len('E %s %s ' % (alias, h)):] for x in r.get(r'^E %s %s ' % (alias, h)))
    missing = [x for x in cur if x not in hist]
    if not with_cd:
        extra = [x for x in hist if x.startswith('CD ')]
        if extra:
            return False, 'the item holds custom data: ' + extra[0][:80]
    n0, n1 = value(b, 'E %s history.count' % alias), value(r, 'E %s history.count' % alias)
    if missing:
        return False, 'the item %s lacks: %s' % (h, ' | '.join(missing[:3])[:160])
    ok = n0 is not None and n1 is not None and int(n1) == int(n0) + 1
    return check(ok, 'history %s -> %s; item %s holds the %d lines of the version before' %
                 (n0, n1, h, len(cur)))


def outside_kept(b, r, cells):
    """Every line not about a cell (or the run's own entry) is as in b."""
    skip = re.compile(r'^(E ~|F groups |G \S+ children |' + cells + ')')
    a = Counter(norm(x) for x in b.lines if not skip.search(x))
    c = Counter(norm(x) for x in r.lines if not skip.search(x))
    return check(a == c, 'every line outside the cells as in the base', delta_text(a, c))


def alike(dumps, ignore=None):
    def nz(d):
        return [x for x in d.lines if not x.startswith('E ~') and not (ignore and re.search(ignore, x))]
    first = nz(dumps[0])
    bad = [os.path.basename(d.path) for d in dumps[1:] if nz(d) != first]
    return check(not bad, '%d runs read back alike' % len(dumps), 'differ: ' + ', '.join(bad))


def only_changes(b, r, allowed):
    """Lines that differ between b and r, the run's own entry set aside, all match allowed."""
    skip = re.compile(r'^(E ~|F groups |G \S+ children )')
    a = Counter(norm(x) for x in b.lines if not skip.search(x))
    c = Counter(norm(x) for x in r.lines if not skip.search(x))
    gone, new = list((a - c).elements()), list((c - a).elements())
    ok_rx = re.compile(allowed)
    bad = [x for x in gone + new if not ok_rx.search(x)]
    return check(not bad, '%d lines differ, all of them %s' % (len(gone) + len(new), allowed),
                 'also differs: ' + ' | '.join(x[:100] for x in bad[:4]))


class Ctx:
    def __init__(self, root, bases):
        self.root, self.bases = root, bases
        self.cache = {}

    def d(self, *parts):
        p = os.path.join(self.root, *parts)
        if p not in self.cache:
            self.cache[p] = Dump(p)
        return self.cache[p]

    def base(self, name):
        p = os.path.join(self.bases, name + '.kdbx.dump.txt')
        if p not in self.cache:
            self.cache[p] = Dump(p)
        return self.cache[p]

    def run(self, prog, tag, name=None):
        return self.d('runs', prog, tag, (name or '%s-%s' % (prog, tag)) + '.kdbx.dump.txt')

    def sync(self, label, which):
        return self.d('sync', label, which + '.dump.txt')

    def log(self, *parts):
        return read(os.path.join(self.root, 'logs', *parts))


CLAIMS = []


def claim(cid, where, text):
    def wrap(fn):
        CLAIMS.append((cid, where, text, fn))
        return fn
    return wrap


# ------------------------------------------------------------------ 1.3 the databases

@claim('1.3-bases', '1.3, the first table', 'S-argon holds every carrier the table lists')
def _(x):
    b = x.base('S-argon')
    return all_of(
        present(b, r'^F version 4\.1$'),
        present(b, r'^F kdf Argon2d it=2 mem=1024KiB par=1$'),
        present(b, r'^G GKPG flags .* search=0 autotype=0 expanded=0 '),
        present(b, r'^G GKPG notes "The plugin'),
        present(b, r'^E R1 S KPG\.Record p=0 len=2861 '),
        present(b, r'^E R1 S KPG\.Secret p=1 '),
        present(b, r'^E R1 S KPG\.20261115T101500Z\.a1b2 p=0 '),
        present(b, r'^E R1 S KPG\.20261115T101501Z\.c3d4 p=1 '),
        present(b, r'^E R1 A authority\.p12 p=1 len=3000 sha=86f51de7a0078da7$'),
        present(b, r'^E R2 A copy\.p12 p=1 len=3000 sha=86f51de7a0078da7$'),
        present(b, r'^E R1 tags \[kpg;record\]$'),
        present(b, r'^E R1 history\.count 3$'),
        present(b, r'^E R1 H2026-11-01T11:00:00Z S KPG\.Record p=0 "\{\\"kind\\":\\"record\\",\\"version\\":1\}"$'),
        absent(b, r'^E R1 H2026-11-01T11:00:00Z S KPG\.2026'),
        present(b, r'^E R1 H2026-11-01T12:00:00Z S KPG\.20261115T101500Z\.a1b2 '),
        absent(b, r'^E R1 H2026-11-01T12:00:00Z A '),
        present(b, r'^E R1 H2026-11-01T13:00:00Z S KPG\.Secret p=1 .*v0'),
        present(b, r'^E R1 H2026-11-01T13:00:00Z A authority\.p12 '),
        present(b, r'^E R1 t\.modified 2026-11-01T14:00:00Z$'),
        present(b, r'^E R1 CD KPG\.cd = '),
        present(b, r'^E R2 S KPG\.20261115T101502Z\.e5f6 p=0 '),
        present(b, r'^E R2 tags \[kpg\]$'),
        present(b, r'^E M1 S KPG\.base p=0 "\{\\"v\\":0\}"$'),
        present(b, r'^E M2 S KPG\.base p=0 "\{\\"v\\":0\}"$'),
        present(b, r'^G G notes "\{\\"kpg\.rules'),
        present(b, r'^G G tags \[kpg\.reader;team\]$'),
        present(b, r'^G G CD KPG = '),
        present(b, r'^E Gm S KPG\.Marker p=0 '),
        present(b, r'^E E1 S Extra p=0 "extra-value-e1"$'),
        present(b, r'^E E1 A e1\.txt p=0 len=600 '),
        present(b, r'^E E1 tags \[work\]$'),
        present(b, r'^E E1 history\.count 3$'),
        absent(b, r'^E E1 H2026-11-01T11:00:00Z S Extra '),
        present(b, r'^E E1 H2026-11-01T12:00:00Z S Extra p=0 "extra-v1"$'),
        present(b, r'^E E1 H2026-11-01T13:00:00Z S Old p=0 '),
        present(b, r'^E E1 CD KPG = '),
        present(b, r'^E N1 parent N '),
        present(b, r'^G N parent G '),
        present(b, r'^M recycleBin enabled=1 group=Bin '),
        present(b, r'^G Bin children groups=0 entries=0$'),
        present(b, r'^D D1 2026-11-01T10:06:00Z$'),
        present(b, r'^D D2 2026-11-01T10:07:00Z$'),
        present(b, r'^M CD KPG\.Profiles = .* @2026-11-01T10:15:00Z$'),
        present(b, r'^M CD KPG\.Untimed = .* @none$'),
        present(b, r'^M historyMaxItems 8 historyMaxSize 5242880 maintenanceHistoryDays 200$'),
    )


@claim('1.3-aes', '1.3, after the first table', 'the AES-KDF variants: S-aes the same content, '
       'S-aes-nocd no node custom data, S-aes-plain no node custom data, no group tags, no per-key time, KDBX 4.0')
def _(x):
    s, a, n, p = x.base('S-argon'), x.base('S-aes'), x.base('S-aes-nocd'), x.base('S-aes-plain')
    return all_of(
        present(a, r'^F kdf AES-KDF rounds=60000$'), present(n, r'^F kdf AES-KDF rounds=60000$'),
        present(p, r'^F kdf AES-KDF rounds=60000$'),
        only_changes(s, a, r'^F kdf '),
        only_changes(s, n, r'^(F kdf | ?E (E1|R1) CD |G G CD )'),
        absent(n, r' CD (?!KPG\.(Profiles|Untimed) )'),
        present(n, r'^G G tags \[kpg\.reader;team\]$'),
        present(p, r'^F version 4\.0$'), absent(p, r'^(E|G) \S+ CD '),
        present(p, r'^G G tags \[\]$'), present(p, r'^M CD KPG\.Profiles = .* @none$'),
        present(a, r'^F version 4\.1$'), present(n, r'^F version 4\.1$'),
    )


@claim('1.3-pair', '1.3, the merge pair table', 'C and P are S-argon changed cell by cell as the table says')
def _(x):
    s, c, p = x.base('S-argon'), x.base('C'), x.base('P')
    return all_of(
        present(c, r'^E M1 S KPG\.20261115T120000Z\.c p=0 '), present(c, r'^E M1 t\.modified 2026-11-01T15:00:00Z$'),
        present(p, r'^E M1 S KPG\.20261115T140000Z\.p p=0 '), present(p, r'^E M1 t\.modified 2026-11-01T16:00:00Z$'),
        present(c, r'^E M2 S KPG\.20261115T130000Z\.c p=0 '), present(c, r'^E M2 t\.modified 2026-11-01T17:00:00Z$'),
        present(p, r'^E M2 S KPG\.20261115T130000Z\.p p=0 '), present(p, r'^E M2 t\.modified 2026-11-01T17:00:00Z$'),
        present(c, r'^E M1 H2026-11-01T10:50:00Z '), present(p, r'^E M2 H2026-11-01T10:50:00Z '),
        present(c, r'^E NC t\.modified 2026-11-01T15:00:00Z$'), present(p, r'^E NP t\.modified 2026-11-01T16:00:00Z$'),
        present(c, r'^E NC parent GKPG$|^E NC parent GKPG '), present(p, r'^E NP parent GKPG '),
        present(c, r'^E X1 S Title '), present(p, r'^D X1 2026-11-01T16:00:00Z$'), absent(p, r'^E X1 '),
        present(c, r'^D X2 2026-11-01T15:00:00Z$'), absent(c, r'^E X2 '),
        present(p, r'^E X2 S UserName p=0 "changed-in-P"$'), present(p, r'^E X2 t\.modified 2026-11-01T16:00:00Z$'),
        present(c, r'^E Y1 A added-c\.bin p=0 len=1500 '), present(c, r'^E Y1 t\.modified 2026-11-01T15:00:00Z$'),
        absent(p, r'^E Y1 A '),
        present(p, r'^E Y2 tags \[added-p\]$'), present(p, r'^E Y2 t\.modified 2026-11-01T16:00:00Z$'),
        present(c, r'^G G notes .*changedBy\\":\\"C'), present(c, r'^G G t\.modified 2026-11-01T15:00:00Z$'),
        same(s, p, 'G G', 'notes'),
        present(p, r'^E Y3 parent N '), present(p, r'^E Y3 prev G$'), present(p, r'^E Y3 t\.location 2026-11-01T16:00:00Z$'),
        present(c, r'^E Y3 parent G '),
    )


@claim('1.3-memprot', '1.3, the side fact', 'a base written with "protect user name" on came back from '
       "KeePassLib's own open and save with every UserName unprotected (measured: 42 lines)")
def _(x):
    rec = read(os.path.join(x.root, 'keepasslib-memprot-reset.txt'))
    s = Dump(os.path.join(x.root, 'base-memprot', 'S-argon.kdbx.dump.txt'))
    c = Dump(os.path.join(x.root, 'base-memprot', 'C.kdbx.dump.txt'))
    gone, new = changed(s, c, r' S UserName ')
    p1_left = [y for y in c.get(r' S UserName p=1 ')]
    lines = sorted(['< ' + y for y in gone] + ['> ' + y for y in new], key=lambda z: (z[2:], z[0]))
    want = sorted(rec[1:], key=lambda z: (z[2:], z[0]))
    return all_of(
        check(len(rec) == 42, 'the record is 42 lines: one heading and %d diff lines' % (len(rec) - 1)),
        check(lines == want, 'they are the UserName lines of the diff of the two dumps it names'),
        check(not p1_left and all(' p=1 ' in y for y in gone),
              'every UserName in S-argon is p=1, none in C is', 'a protected UserName is left in C'),
    )


# ------------------------------------------------------------------ 1.5 what was counted

def counted(x, prog):
    d = os.path.join(x.root, 'logs', 'runs', prog)
    return sorted(f[:-4] for f in os.listdir(d) if f.endswith('.log'))


@claim('1.5-counted', '1.5, last paragraph', 'counted: Keepass2Android 15 runs (4 saves, 5 operations, '
       '6 merges), KeePassDroid 8 (4 saves, 3 operations, 1 menu listing), KeePassDX 15 (4, 5, 6)')
def _(x):
    k, p, d = counted(x, 'k2a'), counted(x, 'kpd'), counted(x, 'kpdx')
    ok_k = len(k) == 15 and sum(t.startswith('save-') for t in k) == 4 and sum(t.startswith('merge-') for t in k) == 6
    ok_p = len(p) == 8 and sum(t.startswith('save-') for t in p) == 4 and 'menus' in p
    dx = [t for t in d if t != 'edit-g-notes']
    ok_d = len(dx) == 15 and sum(t.startswith('save-') for t in dx) == 4 and sum(t.startswith('merge-') for t in dx) == 6
    rc = all('### flow rc=0' in '\n'.join(x.log('runs', pr, t + '.log'))
             for pr, ts in (('k2a', k), ('kpd', p), ('kpdx', dx)) for t in ts)
    notes = x.log('runs', 'kpdx', 'edit-g-notes.log')
    same_as5 = notes == x.log('runs', 'kpdx', 'not-counted', 'edit-g-notes-attempt5.log')
    return all_of(check(ok_k, 'k2a: %d logs' % len(k)), check(ok_p, 'kpd: %d logs' % len(p)),
                  check(ok_d, 'kpdx: %d logs besides edit-g-notes' % len(dx)),
                  check(rc, 'every counted log ends with flow rc=0'),
                  check(same_as5, 'kpdx edit-g-notes.log is not-counted/edit-g-notes-attempt5.log, '
                        'a failed run, so it is not counted'))


@claim('1.5-k2a-password', '1.5, the second item', 'Keepass2Android steps 4 and 5 stopped at unlock with '
       "34 and 32 of the password's 35 characters in the field")
def _(x):
    def dots(name):
        for line in x.log('runs', 'k2a', 'not-counted', name):
            m = re.search(r"id=password_edit\s+text='([^']*)'", line)
            if m:
                return len(m.group(1))
        return None
    r, dl = dots('rename-g-attempt1.log'), dots('delete-attempt1.log')
    return check(r == 34 and dl == 32 and len('dummy-password-for-a-throwaway-file') == 35,
                 'rename (step 4) %s, delete (step 5) %s of 35' % (r, dl))


@claim('6-attempts', '6, the paragraph after the list', 'KeePassDX, 17 attempts, four of them stopped '
       'by hand; every attempt that reached the pull left its file byte for byte as given, except '
       "KeePassDroid's step 2 attempt; the runs stopped by hand pulled nothing")
def _(x):
    d = os.path.join(x.root, 'logs', 'runs')
    logs = []
    for prog in ('k2a', 'kpd', 'kpdx'):
        nc = os.path.join(d, prog, 'not-counted')
        logs += [(prog, f) for f in sorted(os.listdir(nc)) if f.endswith('.log')]
    dx = [f for p, f in logs if p == 'kpdx']
    stopped = [f for f in dx if '### flow rc=' not in '\n'.join(x.log('runs', 'kpdx', 'not-counted', f))]
    changed_files, pulled_stopped = [], []
    for prog, f in logs:
        text = x.log('runs', prog, 'not-counted', f)
        pushed = [m.group(1) for m in (re.search(r'(?:pushed|target) sha256 ([0-9a-f]{64})', y) for y in text) if m]
        dev = [m.group(1) for m in (re.search(r'^device \S+ sha256 ([0-9a-f]{64})', y) for y in text) if m]
        if f in stopped and dev:
            pulled_stopped.append(f)
        if any(v != pushed[0] for v in dev):
            changed_files.append(prog + '/' + f)
    return all_of(check(len(dx) == 17, '%d KeePassDX attempts' % len(dx)),
                  check(len(stopped) == 4, '%d of them stopped by hand' % len(stopped)),
                  check(not pulled_stopped, 'the stopped runs pulled nothing'),
                  check(changed_files == ['kpd/edit-e1-password-not-typed.log'],
                        'only %s pulled a file unlike the one pushed' % changed_files))


# ------------------------------------------------------------------ 2.1 Keepass2Android, carrier by operation

K2A = (('1', 'save-argon'), ('2', 'edit-e1'), ('3', 'edit-carriers'), ('4', 'rename-g'), ('5', 'delete'), ('6', 'move'))
KPD = (('1', 'save-argon'), ('2', 'edit-e1'), ('3', 'edit-carriers'), ('5', 'delete'))
KDX = (('2', 'edit-e1'), ('3', 'edit-carriers'), ('4', 'rename-g'), ('5', 'delete'), ('6', 'move'))

ROWS = (
    ('kpg-group', 'group KPG: notes, searching off, auto-type off', r'^G GKPG (notes|flags) '),
    ('r1-std', 'R1 standard fields, Notes JSON', r'^E R1 S (Title|UserName|Password|URL|Notes) '),
    ('plain', 'plain custom strings with JSON, the 2861-character value, timestamp names', r'^E R[12] S KPG\.\S+ p=0 '),
    ('protected', 'protected custom strings, their flag', r'^E R1 S KPG\.\S+ p=1 '),
    ('attach', 'attachment authority.p12 (protected) and the same bytes as copy.p12 on R2', r'^E R[12] A '),
    ('tags', 'entry tags (of every entry but E2, which step 5 removes)', r'^E (?!E2 )[A-Za-z]\w* tags '),
    ('r1-hist', "R1's three history items", r'^E R1 H2026-11-01T1[123]:00:00Z '),
    ('marker', "marker entry, KPG.Marker", r'^E Gm S KPG\.Marker '),
    ('g-notes', "G's notes (JSON)", r'^G G notes '),
    ('g-tags', "G's tags", r'^G G tags '),
    ('e1', 'E1: Extra, tag, e1.txt', r'^E E1 (S Extra |tags |A e1\.txt )'),
    ('e1-hist', "E1's three history items", r'^E E1 H2026-11-01T1[123]:00:00Z '),
    ('del', 'deletion records D1, D2', r'^D D[12] '),
)


def carriers(prog, cols, special, base='S-argon'):
    for cid, label, rx in ROWS:
        for col, tag in cols:
            key = (cid, col)
            name = '2.1' if prog == 'k2a' else ('3.2' if prog == 'kpd' else '4.4')
            text = '%s, step %s (%s): "%s" %s' % (prog, col, tag, label,
                                                    'as the cell says' if key in special else 'kept')

            def fn(x, rx=rx, tag=tag, key=key):
                b, r = x.base(base), x.run(prog, tag)
                if key in special:
                    return special[key](x, b, r)
                return kept(b, r, rx)
            CLAIMS.append(('%s-%s-%s' % (name, cid, col), name + ', table', text, fn))


def k2a_special():
    def s3_r1(x, b, r):
        gone, new = changed(b, r, r'^E R1 S (Title|UserName|Password|URL|Notes) ')
        return check(len(gone) == 1 and new == ['E R1 S Title p=0 "KPG record 1-phone"'],
                     'only the title changed: ' + ' -> '.join(gone + new))

    def hist_added(alias, cd):
        def fn(x, b, r):
            old = r'^E %s H2026-11-01T1[123]:00:00Z ' % alias
            return all_of(kept(b, r, old), version_added(b, r, alias, 'H2026-11-01T14:00:00Z', cd))
        return fn

    def s3_marker(x, b, r):
        return all_of(kept(b, r, r'^E Gm S KPG\.Marker '), present(r, r'^E Gm S Title p=0 "KPG marker-phone"$'),
                      version_added(b, r, 'Gm', 'H2026-11-01T10:35:00Z', True))

    def s2_e1(x, b, r):
        return all_of(kept(b, r, r'^E E1 (S Extra |tags |A e1\.txt )'),
                      present(r, r'^E E1 S Password p=1 "pw-e1-not-real-phone"$'))

    def s6_e1(x, b, r):
        return all_of(kept(b, r, r'^E E1 (S Extra |tags |A e1\.txt )'), present(r, r'^E E1 parent N'))

    def s5_del(x, b, r):
        return all_of(kept(b, r, r'^D D[12] '), present(r, r'^D E2 '), present(r, r'^D count 3$'))

    def s1_attach(x, b, r):
        return all_of(kept(b, r, r'^E R[12] A '),
                      check(len({y.split(' sha=')[1] for y in r.get(r'^E R[12] A ')}) == 1 and
                            len(r.get(r'^E R[12] A ')) == 2, 'two attachments, one body'))
    return {('r1-std', '3'): s3_r1, ('r1-hist', '3'): hist_added('R1', True), ('marker', '3'): s3_marker,
            ('e1', '2'): s2_e1, ('e1-hist', '2'): hist_added('E1', True), ('e1', '6'): s6_e1,
            ('del', '5'): s5_del, ('attach', '1'): s1_attach}


carriers('k2a', K2A, k2a_special())


def per_column(prog, cols, cid, label, cells, table):
    for col, tag in cols:
        if col not in cells:
            continue

        def fn(x, tag=tag, col=col):
            return cells[col](x, x.base('S-argon'), x.run(prog, tag), tag)
        CLAIMS.append(('%s-%s-%s' % (table, cid, col), table + ', table',
                       '%s, step %s (%s): "%s" as the cell says' % (prog, col, tag, label), fn))


def mid(x, prog, tag, name):
    return x.run(prog, tag, name)


def node_times_kept(b, r, but=()):
    rx = r'^(E [A-Za-z]\w*|G \S+) t\.modified '
    a, c = matching(b, rx), matching(r, rx)
    gone = [y for y in (a - c).elements() if y.split(' t.modified')[0] not in but]
    return check(not gone, 'no other node t.modified moved', 'moved: ' + ' | '.join(gone[:4]))


per_column('k2a', K2A, 'n', 'N and N1', {
    '1': lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )'),
    '2': lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )'),
    '3': lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )'),
    '4': lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )'),
    '5': lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )'),
    '6': lambda x, b, r, t: all_of(present(r, r'^G N parent Root '), present(r, r'^E N1 parent N ')),
}, '2.1')

per_column('k2a', K2A, 'times', 'modification times of nodes not edited', {
    '1': lambda x, b, r, t: node_times_kept(b, r),
    '2': lambda x, b, r, t: all_of(moved(b, r, 'E E1', 't.modified'), node_times_kept(b, r, ('E E1',)),
                                   moved(b, r, 'G G', 't.accessed'), moved(b, r, 'G Root', 't.accessed')),
    '3': lambda x, b, r, t: all_of(node_times_kept(b, r, ('E R1', 'E Gm')), moved(b, r, 'G GKPG', 't.accessed'),
                                   moved(b, r, 'G G', 't.accessed'), moved(b, r, 'G Root', 't.accessed')),
    '4': lambda x, b, r, t: all_of(moved(b, r, 'G G', 't.modified'), moved(b, r, 'G Root', 't.modified'),
                                   check(value(r, 'G G t.modified') == value(r, 'G Root t.modified'),
                                         'G and the root at one time, %s' % value(r, 'G G t.modified')),
                                   node_times_kept(b, r, ('G G', 'G Root'))),
    '5': lambda x, b, r, t: all_of(same(b, mid(x, 'k2a', t, 'mid-recycled'), 'E E2', 't.modified'),
                                   moved(b, mid(x, 'k2a', t, 'mid-recycled'), 'E E2', 't.location'),
                                   present(mid(x, 'k2a', t, 'mid-recycled'), r'^E E2 parent Bin ')),
    '6': lambda x, b, r, t: all_of(same(b, r, 'E E1', 't.modified'), same(b, r, 'G N', 't.modified'),
                                   moved(b, r, 'E E1', 't.location'), moved(b, r, 'G N', 't.location')),
}, '2.1')

per_column('k2a', K2A, 'prev', 'previous parent', {
    '5': lambda x, b, r, t: present(mid(x, 'k2a', t, 'mid-recycled'), r'^E E2 prev -$', 'not recorded for the recycled E2'),
    '6': lambda x, b, r, t: all_of(present(r, r'^E E1 prev -$'), present(r, r'^G N prev -$')),
}, '2.1')

ALL6 = dict((c, None) for c, _ in K2A)
per_column('k2a', K2A, 'format', 'format 4.1', dict((c, lambda x, b, r, t: present(r, r'^F version 4\.1$')) for c in ALL6), '2.1')
per_column('k2a', K2A, 'node-cd', 'control: entry custom data (E1, R1), group custom data (G)',
           dict((c, lambda x, b, r, t: kept(b, r, r'^(E (E1|R1)|G G) CD ')) for c in ALL6), '2.1')
per_column('k2a', K2A, 'db-cd', 'control: database custom data: values kept, the per-key time dropped',
           dict((c, lambda x, b, r, t: all_of(present(r, r'^M CD KPG\.Profiles = "\[\\"reader\\",\\"writer\\"\]" @none$'),
                                              kept(b, r, r'^M CD KPG\.Untimed '))) for c in ALL6), '2.1')


@claim('2.1-aes', '2.1, after the table', "S-aes and S-aes-nocd came back 4.1 with the same result as "
       "S-argon (only the per-key time gone); S-aes-plain came back as KDBX 3.1 with no SettingsChanged, "
       "every carrier it held kept")
def _(x):
    out = []
    for v in ('S-aes', 'S-aes-nocd'):
        b, r = x.base(v), x.run('k2a', 'save-' + v[2:])
        out.append(only_changes(b, r, r'^M CD KPG\.Profiles '))
        out.append(present(r, r'^F version 4\.1$'))
    b, r = x.base('S-aes-plain'), x.run('k2a', 'save-aes-plain')
    out.append(present(r, r'^F version 3\.1$'))
    out.append(check(value(r, 'M settingsChanged') != T0, 'settingsChanged %s, not the base\'s' % value(r, 'M settingsChanged')))
    out.append(only_changes(b, r, r'^(F version |M settingsChanged )'))
    return all_of(*out)


@claim('2.1-step5', '2.1, step 5', 'K2A asked "Delete permanently? ... Press No to recycle." (Cancel, No, '
       'Yes); inside the bin the question had no recycle option; Yes deleted E2 and wrote its record')
def _(x):
    log = '\n'.join(x.log('runs', 'k2a', 'delete.log'))
    first = 'Press No to recycle.' in log and log.count("id=button2                      text='No'") == 1
    second = 'Do you want to delete the selected elements permanently? (content://' in log
    r = x.run('k2a', 'delete')
    return all_of(check(first and second and log.count("text='Cancel'") == 2 and log.count("text='Yes'") == 2,
                        'the first question offers Cancel, No, Yes and says Press No to recycle; the second '
                        'offers Cancel and Yes'),
                  present(mid(x, 'k2a', 'delete', 'mid-recycled'), r'^E E2 parent Bin '),
                  absent(r, r'^E E2 '), present(r, r'^D E2 '))


# ------------------------------------------------------------------ 2.2 Keepass2Android, the merge

def merge_cells(table, prog, pre, rules):
    for cell, (cp_fn, pc_fn) in rules.items():
        for d, fn, base in (('CP', cp_fn, 'C'), ('PC', pc_fn, 'P')):
            if fn is None:
                continue

            def f(x, fn=fn, d=d, base=base):
                rs = [x.run(prog, '%s%s-r%d' % (pre, d, i)) for i in (1, 2, 3)]
                return all_of(*[fn(x, x.base(base), r) for r in rs])
            CLAIMS.append(('%s-%s-%s' % (table, cell, d), table + ', table',
                           '%s merge %s, all three runs: cell %s as the table says' % (prog, d, cell), f))


def m1_whole(x, b, r):
    return all_of(present(r, r'^E M1 S KPG\.20261115T140000Z\.p '), absent(r, r'^E M1 S KPG\.20261115T120000Z\.c '),
                  present(r, r'^E M1 t\.modified 2026-11-01T16:00:00Z$'), present(r, r'^E M1 history\.count 2$'),
                  present(r, r'^E M1 H2026-11-01T10:50:00Z t\.modified '),
                  present(r, r'^E M1 H2026-11-01T15:00:00Z S KPG\.20261115T120000Z\.c '))


def m2_side(win, lose):
    def fn(x, b, r):
        return all_of(present(r, r'^E M2 S KPG\.20261115T130000Z\.%s ' % win),
                      absent(r, r'KPG\.20261115T130000Z\.%s ' % lose, "the other side's field exists nowhere"),
                      present(r, r'^E M2 history\.count 1$'))
    return fn


def both_new(x, b, r):
    return all_of(present(r, r'^E NC parent GKPG'), present(r, r'^E NP parent GKPG'))


def x1_gone(record):
    def fn(x, b, r):
        return all_of(absent(r, r'^E X1 '), present(r, r'^D X1 ') if record else absent(r, r'^D X1 '))
    return fn


def x2_back(record):
    def fn(x, b, r):
        return all_of(present(r, r'^E X2 S UserName p=0 "changed-in-P"$'),
                      present(r, r'^D X2 ') if record else absent(r, r'^D X2 '))
    return fn


def records(*ids):
    def fn(x, b, r):
        got = sorted(y.split()[1] for y in r.get(r'^D (?!count)'))
        return check(got == sorted(ids), 'deletion records ' + ', '.join(got))
    return fn


def y_cells(x, b, r):
    return all_of(present(r, r'^E Y1 A added-c\.bin '), present(r, r'^E Y2 tags \[added-p\]$'))


def g_notes_c(x, b, r):
    return present(r, r'^G G notes .*changedBy\\":\\"C')


def y3_in_n(x, b, r):
    return all_of(present(r, r'^E Y3 parent N '), present(r, r'^E Y3 prev G$'))


merge_cells('2.2', 'k2a', 'merge-', {
    'M1': (m1_whole, m1_whole),
    'M2': (m2_side('p', 'c'), m2_side('c', 'p')),
    'NC-NP': (both_new, both_new),
    'X1': (x1_gone(True), x1_gone(True)),
    'X2': (x2_back(False), x2_back(False)),
    'records': (records('D1', 'D2', 'X1'), records('D1', 'D2', 'X1')),
    'Y1-Y2': (y_cells, y_cells),
    'G-notes': (lambda x, b, r: all_of(g_notes_c(x, b, r), present(r, r'^G G t\.modified 2026-11-01T15:00:00Z$')), g_notes_c),
    'Y3': (y3_in_n, y3_in_n),
    'rest': (lambda x, b, r: all_of(present(r, r'^F version 4\.1$'), present(r, r'^M CD KPG\.Profiles .* @none$'),
                                    outside_kept(b, r, r'E (M1|M2|NC|NP|X1|X2|Y1|Y2|Y3) |D |G G (notes|t\.modified) |M CD KPG\.Profiles ')),) * 2,
})


@claim('2.2-runs', '2.2, the first paragraph', 'within each cell the three runs read back identically '
       '(the entry the run added set aside), and the two directions differ in exactly one line, M2\'s field')
def _(x):
    cp = [x.run('k2a', 'merge-CP-r%d' % i) for i in (1, 2, 3)]
    pc = [x.run('k2a', 'merge-PC-r%d' % i) for i in (1, 2, 3)]
    a = Counter(y for y in cp[0].lines if not y.startswith('E ~'))
    c = Counter(y for y in pc[0].lines if not y.startswith('E ~'))
    gone, new = list((a - c).elements()), list((c - a).elements())
    return all_of(alike(cp), alike(pc),
                  check(len(gone) == 1 and len(new) == 1 and gone[0].startswith('E M2 S ') and new[0].startswith('E M2 S '),
                        'CP and PC differ in one line: %s / %s' % (gone[:1], new[:1])))


# ------------------------------------------------------------------ 3.1 to 3.3 KeePassDroid

@claim('3.1-menus', '3.1', 'a long press on a group offers Open only; on an entry, Open and Delete; the '
       'group view\'s menu holds Donate, Settings, Change master key, DB sort order; nothing was saved')
def _(x):
    log = x.log('runs', 'kpd', 'menus.log')
    lists, cur = [], None
    for y in log:
        if re.search(r'kpd-menus\.flow:\d+ list$', y):
            cur = []
            lists.append(cur)
        elif cur is not None:
            m = re.search(r"id=title\s+text='([^']*)'", y)
            if m:
                cur.append(m.group(1))
            elif y.startswith('['):
                cur = None
    want = [['Open'], ['Open', 'Delete'], ['Donate', 'Settings', 'Change master key', 'DB sort order']]
    pushed = [m.group(1) for m in (re.search(r'pushed sha256 ([0-9a-f]{64})', y) for y in log) if m]
    dev = [m.group(1) for m in (re.search(r'^device \S+ sha256 ([0-9a-f]{64})', y) for y in log) if m]
    return all_of(check(lists[:3] == want, 'menus listed: %s' % lists[:3]),
                  check(dev and all(v == pushed[0] for v in dev), 'the file came back as pushed'))


def kpd_special():
    sp = k2a_special()

    def no_cd_hist(x, b, r):
        return all_of(kept(b, r, r'^E R1 H2026-11-01T1[123]:00:00Z (?!.* CD )'),
                      version_added(b, r, 'R1', 'H2026-11-01T14:00:00Z', False))

    def e1_hist(x, b, r):
        return all_of(kept(b, r, r'^E E1 H2026-11-01T1[123]:00:00Z '), version_added(b, r, 'E1', 'H2026-11-01T14:00:00Z', False))
    sp[('r1-hist', '3')] = no_cd_hist
    sp[('e1-hist', '2')] = e1_hist
    sp[('marker', '3')] = lambda x, b, r: all_of(kept(b, r, r'^E Gm S KPG\.Marker '),
                                                 present(r, r'^E Gm S Title p=0 "KPG marker-phone"$'),
                                                 version_added(b, r, 'Gm', 'H2026-11-01T10:35:00Z', False))
    del sp[('e1', '6')]
    return sp


carriers('kpd', KPD, kpd_special())

per_column('kpd', KPD, 'kpg-time', "group KPG: its modification time", {
    '1': lambda x, b, r, t: same(b, r, 'G GKPG', 't.modified'),
    '2': lambda x, b, r, t: same(b, r, 'G GKPG', 't.modified'),
    '3': lambda x, b, r, t: moved(b, r, 'G GKPG', 't.modified'),
    '5': lambda x, b, r, t: same(b, r, 'G GKPG', 't.modified'),
}, '3.2')
per_column('kpd', KPD, 'times', 'modification times', {
    '1': lambda x, b, r, t: node_times_kept(b, r),
    '2': lambda x, b, r, t: all_of(moved(b, r, 'E E1', 't.modified'), moved(b, r, 'G G', 't.modified'),
                                   moved(b, r, 'G Root', 't.modified'), node_times_kept(b, r, ('E E1', 'G G', 'G Root'))),
    '3': lambda x, b, r, t: all_of(*[moved(b, r, n, 't.modified') for n in ('E R1', 'E Gm', 'G GKPG', 'G G', 'G Root')] +
                                   [node_times_kept(b, r, ('E R1', 'E Gm', 'G GKPG', 'G G', 'G Root'))]),
    '5': lambda x, b, r, t: all_of(same(b, mid(x, 'kpd', t, 'mid-recycled'), 'E E2', 't.modified'),
                                   moved(b, mid(x, 'kpd', t, 'mid-recycled'), 'E E2', 't.location'),
                                   present(mid(x, 'kpd', t, 'mid-recycled'), r'^E E2 prev -$')),
}, '3.2')
ALL4 = ('1', '2', '3', '5')
per_column('kpd', KPD, 'format', 'format 4.1, as given', dict((c, lambda x, b, r, t: present(r, r'^F version 4\.1$')) for c in ALL4), '3.2')
per_column('kpd', KPD, 'settings', 'SettingsChanged not written (reads back as the moment of reading)',
           dict((c, lambda x, b, r, t: check(value(r, 'M settingsChanged') > '2026-11-15',
                                             'settingsChanged %s' % value(r, 'M settingsChanged'))) for c in ALL4), '3.2')
per_column('kpd', KPD, 'entry-cd', 'control: entry custom data (E1, R1) deleted',
           dict((c, lambda x, b, r, t: all_of(present(b, r'^E (E1|R1) CD '), absent(r, r'^E (E1|R1) CD '))) for c in ALL4), '3.2')
per_column('kpd', KPD, 'group-cd', 'control: group custom data (G) deleted',
           dict((c, lambda x, b, r, t: all_of(present(b, r'^G G CD '), absent(r, r'^G G CD '))) for c in ALL4), '3.2')
per_column('kpd', KPD, 'db-cd', 'control: database custom data kept, per-key time kept',
           dict((c, lambda x, b, r, t: kept(b, r, r'^M CD ')) for c in ALL4), '3.2')
per_column('kpd', KPD, 'n', 'N and N1 (not in the table; kept in every run)',
           dict((c, lambda x, b, r, t: kept(b, r, r'^(G N |E N1 )')) for c in ALL4), '3.2')


@claim('3.2-aes', '3.2, after the table', 'all three AES variants came back at the version given (4.1, '
       '4.1, 4.0): carriers kept, node custom data deleted where there was any, no SettingsChanged')
def _(x):
    out = []
    for v, ver in (('S-aes', '4.1'), ('S-aes-nocd', '4.1'), ('S-aes-plain', '4.0')):
        b, r = x.base(v), x.run('kpd', 'save-' + v[2:])
        out += [present(r, r'^F version %s$' % re.escape(ver)), absent(r, r'^(E|G) \S+ CD '),
                only_changes(b, r, r'^(M settingsChanged |E (E1|R1) CD |G G CD )')]
    return all_of(*out)


@claim('3.2-step5', '3.2, step 5', 'Delete in G moved E2 into the database\'s own Recycle Bin, its '
       'modification time unchanged; Delete inside the bin deleted it and wrote its record; Delete asks nothing')
def _(x):
    m, r = mid(x, 'kpd', 'delete', 'mid-recycled'), x.run('kpd', 'delete')
    log = x.log('runs', 'kpd', 'delete.log')
    dialogs = [y for y in log if 'id=alertTitle' in y or 'id=button1' in y]
    return all_of(present(m, r'^E E2 parent Bin '), same(x.base('S-argon'), m, 'E E2', 't.modified'),
                  absent(r, r'^E E2 '), present(r, r'^D E2 '), check(not dialogs, 'no dialog listed'))


@claim('3.3', '3.3', 'an editor save with nothing typed: E1\'s modification time moved, one history item '
       'added (the unchanged version), G\'s and the root\'s modification times moved, every field as it was')
def _(x):
    b = x.base('S-argon')
    r = x.d('runs', 'kpd', 'not-counted', 'edit-e1-password-not-typed', 'kpd-edit-e1.kdbx.dump.txt')
    return all_of(moved(b, r, 'E E1', 't.modified'), version_added(b, r, 'E1', 'H2026-11-01T14:00:00Z', False),
                  moved(b, r, 'G G', 't.modified'), moved(b, r, 'G Root', 't.modified'),
                  kept(b, r, r'^E E1 (S|A|tags) '))


# ------------------------------------------------------------------ 4.1 to 4.4 KeePassDX

@claim('4.1', '4.1', 'all four bases came back with every line as given but the root group\'s '
       'modification and access times: every control, SettingsChanged and the format (4.1, 4.0)')
def _(x):
    out = []
    for v, ver in (('S-argon', '4.1'), ('S-aes', '4.1'), ('S-aes-nocd', '4.1'), ('S-aes-plain', '4.0')):
        b, r = x.base(v), x.run('kpdx', 'save-' + v[2:])
        out += [only_changes(b, r, r'^G Root t\.(modified|accessed) '), present(r, r'^F version %s$' % re.escape(ver)),
                moved(b, r, 'G Root', 't.modified')]
    return all_of(*out)


def m2_open(win, lose):
    return m2_side(win, lose)


merge_cells('4.3', 'kpdx', 'merge-from', {
    'M1': (m1_whole, m1_whole),
    'M2': (m2_open('c', 'p'), m2_open('p', 'c')),
    'NC-NP': (both_new, both_new),
    'X1': (x1_gone(False), x1_gone(True)),
    'X2': (x2_back(True), x2_back(False)),
    'records': (records('D1', 'D2', 'X2'), records('D1', 'D2', 'X1')),
    'Y1-Y2': (y_cells, y_cells),
    'G-notes': (g_notes_c, g_notes_c),
    'Y3': (lambda x, b, r: all_of(present(r, r'^E Y3 parent N '), present(r, r'^E Y3 prev -$'),
                                  present(r, r'^E Y3 t\.location 2026-11-01T10:55:00Z$')),
           lambda x, b, r: all_of(present(r, r'^E Y3 parent G '), present(r, r'^E Y3 prev G$'),
                                  present(r, r'^E Y3 t\.location 2026-11-01T16:00:00Z$'))),
    'rest': (lambda x, b, r: all_of(present(r, r'^F version 4\.1$'),
                                    outside_kept(b, r, r'E (M1|M2|NC|NP|X1|X2|Y1|Y2|Y3) |D |G G (notes|t\.modified) |G Root t\.accessed ')),) * 2,
})


@claim('4.3-runs', '4.3, the first paragraph', 'within each cell the three runs read back identically but '
       'for the root group\'s access time; the arriving copy came back byte for byte as given in every run')
def _(x):
    out = []
    for d, src in (('CP', 'P'), ('PC', 'C')):
        rs = [x.run('kpdx', 'merge-from%s-r%d' % (d, i)) for i in (1, 2, 3)]
        out.append(alike(rs, r'^G Root t\.accessed '))
        sha = x.base(src).head.split('sha256=')[1]
        for i in (1, 2, 3):
            s = x.run('kpdx', 'merge-from%s-r%d' % (d, i), 'kpdx-merge-from%s-r%d-src' % (d, i))
            out.append(check(s.head.endswith('sha256=' + sha), 'the arriving %s of %s r%d has %s\'s hash' % (src, d, i, src)))
    return all_of(*out)


def kdx_special():
    sp = k2a_special()
    sp[('r1-hist', '3')] = lambda x, b, r: all_of(kept(b, r, r'^E R1 H2026-11-01T1[123]:00:00Z '),
                                                  version_added(b, r, 'R1', 'H2026-11-01T14:00:00Z', True))
    sp[('e1-hist', '2')] = lambda x, b, r: all_of(kept(b, r, r'^E E1 H2026-11-01T1[123]:00:00Z '),
                                                  version_added(b, r, 'E1', 'H2026-11-01T14:00:00Z', True))
    sp[('e1', '2')] = lambda x, b, r: all_of(kept(b, r, r'^E E1 (S Extra |tags |A e1\.txt |CD )'),
                                             present(r, r'^E E1 S Title p=0 "E1-phone"$'))
    sp[('del', '5')] = sp[('del', '5')]
    del sp[('attach', '1')]
    return sp


carriers('kpdx', KDX, kdx_special())

EXPIRY = r'^%s t\.expires 0 2999-11-28T23:59:59Z$'
per_column('kpdx', KDX, 'expiry', 'expiry time of an edited entry or group rewritten to 2999-11-28T23:59:59Z (expiry still off)', {
    '2': lambda x, b, r, t: present(r, EXPIRY % 'E E1'),
    '3': lambda x, b, r, t: all_of(present(r, EXPIRY % 'E R1'), present(r, EXPIRY % 'E Gm')),
    '4': lambda x, b, r, t: present(r, EXPIRY % 'G G'),
}, '4.4')
per_column('kpdx', KDX, 'kpg-time', 'group KPG and its flags (modification time)', {
    '2': lambda x, b, r, t: all_of(kept(b, r, r'^G GKPG (notes|flags) '), same(b, r, 'G GKPG', 't.modified')),
    '3': lambda x, b, r, t: all_of(kept(b, r, r'^G GKPG (notes|flags) '), moved(b, r, 'G GKPG', 't.modified')),
}, '4.4')
per_column('kpdx', KDX, 'times', 'modification times', {
    '2': lambda x, b, r, t: all_of(*[moved(b, r, n, 't.modified') for n in ('E E1', 'G G', 'G Root')] +
                                   [node_times_kept(b, r, ('E E1', 'G G', 'G Root'))]),
    '3': lambda x, b, r, t: all_of(*[moved(b, r, n, 't.modified') for n in ('E R1', 'E Gm', 'G GKPG', 'G G', 'G Root')] +
                                   [node_times_kept(b, r, ('E R1', 'E Gm', 'G GKPG', 'G G', 'G Root'))]),
    '4': lambda x, b, r, t: all_of(moved(b, r, 'G G', 't.modified'), moved(b, r, 'G Root', 't.modified'),
                                   node_times_kept(b, r, ('G G', 'G Root'))),
    '5': lambda x, b, r, t: all_of(moved(b, mid(x, 'kpdx', t, 'mid-recycled'), 'E E2', 't.modified'),
                                   *[moved(b, r, n, 't.modified') for n in ('G G', 'G Bin', 'G Root')]),
    '6': lambda x, b, r, t: all_of(*[moved(b, r, n, 't.modified') for n in ('E E1', 'G N', 'G G', 'G Root')]),
}, '4.4')
per_column('kpdx', KDX, 'prev', 'previous parent recorded (G)', {
    '5': lambda x, b, r, t: present(mid(x, 'kpdx', t, 'mid-recycled'), r'^E E2 prev G$'),
    '6': lambda x, b, r, t: all_of(present(r, r'^E E1 prev G$'), present(r, r'^G N prev G$')),
}, '4.4')
ALL5 = ('2', '3', '4', '5', '6')
per_column('kpdx', KDX, 'controls', 'controls: entry, group and database custom data kept',
           dict((c, lambda x, b, r, t: kept(b, r, r'^(E [A-Za-z]\w*|G \S+|M) CD ')) for c in ALL5), '4.4')
per_column('kpdx', KDX, 'format', 'format 4.1', dict((c, lambda x, b, r, t: present(r, r'^F version 4\.1$')) for c in ALL5), '4.4')
per_column('kpdx', KDX, 'n', 'N and N1 (kept; in step 6 N under the root)', {
    '2': lambda x, b, r, t: kept(b, r, r'^(G N (name|notes|parent)|E N1 )'),
    '3': lambda x, b, r, t: kept(b, r, r'^(G N (name|notes|parent)|E N1 )'),
    '4': lambda x, b, r, t: kept(b, r, r'^(G N (name|notes|parent)|E N1 )'),
    '5': lambda x, b, r, t: kept(b, r, r'^(G N (name|notes|parent)|E N1 )'),
    '6': lambda x, b, r, t: all_of(present(r, r'^G N parent Root '), present(r, r'^E N1 parent N ')),
}, '4.4')


@claim('4.4-steps', '4.4, before the table', 'step 2 changed E1\'s title and not its password')
def _(x):
    b, r = x.base('S-argon'), x.run('kpdx', 'edit-e1')
    return all_of(present(r, r'^E E1 S Title p=0 "E1-phone"$'), same(b, r, 'E E1', 'S Password'))


@claim('6-kpdx-notes', '6, the last item', 'KeePassDX\'s group dialog holds the notes: the dialog listed '
       'group_edit_note holding G\'s JSON')
def _(x):
    log = x.log('runs', 'kpdx', 'rename-g.log')
    i = [k for k, y in enumerate(log) if 'id=group_edit_note ' in y]
    j = [k for k, y in enumerate(log) if 'id=group_edit_note_container' in y]
    return check(i and j and j[0] < i[0] and 'kpg.rules' in log[i[0]],
                 'group_edit_note holds G\'s JSON, listed after group_edit_note_container')


@claim('6-k2a-dialog', '6, the second item', 'Keepass2Android\'s group dialog holds a name and an icon only '
       '(the dialog listed group_name, icon_button, OK, Cancel)')
def _(x):
    log = x.log('runs', 'k2a', 'rename-g.log')
    ids = [m.group(1) for m in (re.search(r' id=(\S+)\s+text=', y) for y in log) if m]
    return check({'group_name', 'icon_button', 'ok', 'cancel'} <= set(ids) and 'group_edit_note' not in ids,
                 'listed: group_name, icon_button, ok, cancel; no notes field')


@claim('1.4-merge-question', '1.4, the table, step 7', 'the save\'s "Merge changes?" answered "Yes, merge"')
def _(x):
    n = 0
    for d in ('CP', 'PC'):
        for i in (1, 2, 3):
            log = '\n'.join(x.log('runs', 'k2a', 'merge-%s-r%d.log' % (d, i)))
            n += "text='Merge changes?'" in log and "text='Yes, merge'" in log
    return check(n == 6, 'in all %d merge logs' % n)


# ------------------------------------------------------------------ 5 back into KeePass

def sync_rows(table, rows):
    for label, which, text, fn in rows:
        def f(x, label=label, which=which, fn=fn):
            return fn(x, x.sync(label, 'phone'), x.sync(label, which), x.sync(label, 'keepass'))
        CLAIMS.append(('%s-%s-%s' % (table, label.split('-', 1)[1], 'kp' if which == 'into-kp' else 'phone'),
                       table + ', table', '%s, %s: %s' % (label, which, text), f))


def exactly(allowed=None):
    def fn(x, ph, r, kp):
        if allowed is None:
            a, c = Counter(ph.lines), Counter(r.lines)
            return check(a == c, "the phone's file exactly", delta_text(a, c))
        return only_changes(ph, r, allowed)
    return fn


PER_KEY = r'^M CD KPG\.Profiles '
GROUP_ACCESS = r'^G \S+ (t\.accessed|usage) '


def per_key_back(x, ph, r, kp):
    return present(r, r'^M CD KPG\.Profiles = .* @2026-11-01T10:15:00Z$', 'per-key time restored')


def plus(*fns):
    def fn(x, ph, r, kp):
        return all_of(*[f(x, ph, r, kp) for f in fns])
    return fn


def m2_is(side):
    return lambda x, ph, r, kp: present(r, r'^E M2 S KPG\.20261115T130000Z\.%s ' % side, 'M2 is %s\'s version' % side.upper())


sync_rows('5.1', [
    ('k2a-save-argon', 'into-kp', "the phone's file exactly, the per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-save-argon', 'into-phone', 'the same', plus(exactly(PER_KEY), per_key_back)),
    ('k2a-edit-e1', 'into-kp', "the phone's file exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-edit-e1', 'into-phone', 'the same, group access times from the arriving copy',
     plus(exactly(PER_KEY + '|' + GROUP_ACCESS), per_key_back)),
    ('k2a-edit-carriers', 'into-kp', "the phone's file exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-edit-carriers', 'into-phone', 'the same, group access times from the arriving copy',
     plus(exactly(PER_KEY + '|' + GROUP_ACCESS), per_key_back)),
    ('k2a-merge-CP-with-C', 'into-kp', "the phone's merge result exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-CP-with-C', 'into-phone', "the same, but M2 becomes C's version",
     plus(exactly(PER_KEY + r'|^E M2 S KPG\.20261115T130000Z\.'), per_key_back, m2_is('c'))),
    ('k2a-merge-CP-with-P', 'into-kp', "the phone's merge result exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-CP-with-P', 'into-phone', 'the same', plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-PC-with-C', 'into-kp', "the phone's merge result exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-PC-with-C', 'into-phone', 'the same', plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-PC-with-P', 'into-kp', "the phone's merge result exactly, per-key time restored", plus(exactly(PER_KEY), per_key_back)),
    ('k2a-merge-PC-with-P', 'into-phone', "the same, but M2 becomes P's version",
     plus(exactly(PER_KEY + r'|^E M2 S KPG\.20261115T130000Z\.'), per_key_back, m2_is('p'))),
])

CARRY = r'^(E \S+( H\S+)? (S|A) |E \S+( H\S+)? tags |G \S+ (notes|tags|flags) |D )'


def carriers_as_phone(x, ph, r, kp):
    return kept(ph, r, CARRY)


def cd(rx_present=(), rx_absent=()):
    def fn(x, ph, r, kp):
        return all_of(*[present(r, p) for p in rx_present] + [absent(r, a) for a in rx_absent])
    return fn


def settings_now(x, ph, r, kp):
    """The result's SettingsChanged is the moment the phone's file was read, to within two seconds."""
    a, c = value(ph, 'M settingsChanged'), value(r, 'M settingsChanged')

    def t(s):
        return datetime.strptime(s, '%Y-%m-%dT%H:%M:%SZ')
    near = a is not None and c is not None and abs((t(c) - t(a)).total_seconds()) <= 2
    return check(near and c > '2026-11-15', "SettingsChanged %s, the moment the phone's file was read, %s" % (c, a))


sync_rows('5.2', [
    ('kpd-save-argon', 'into-kp', "carriers kept; E1's, R1's and G's custom data lost; SettingsChanged becomes the phone's now",
     plus(carriers_as_phone, cd((), (r'^E E1 CD ', r'^E R1 CD ', r'^G G CD ')), settings_now)),
    ('kpd-save-argon', 'into-phone', 'carriers kept; all custom data restored from KeePass\'s copy',
     plus(carriers_as_phone, cd((r'^E E1 CD ', r'^E R1 CD ', r'^G G CD ')))),
    ('kpd-edit-e1', 'into-kp', "carriers kept; the new password; G's and R1's custom data lost; E1's current lost, "
     'kept only in its history item at +4 h', plus(carriers_as_phone, cd((r'^E E1 S Password p=1 "pw-e1-not-real-phone"$',
                                                                          r'^E E1 H2026-11-01T14:00:00Z CD KPG '),
                                                                         (r'^G G CD ', r'^E R1 CD ', r'^E E1 CD ')))),
    ('kpd-edit-e1', 'into-phone', "carriers kept; G's and E1's custom data lost, E1's history item at +4 h without it; R1's restored",
     plus(carriers_as_phone, cd((r'^E R1 CD ',), (r'^G G CD ', r'^E E1 CD ', r'^E E1 H2026-11-01T14:00:00Z CD ')))),
    ('kpd-edit-carriers', 'into-kp', "carriers kept; G's and E1's custom data lost; R1's current lost, kept only in its history item",
     plus(carriers_as_phone, cd((r'^E R1 H2026-11-01T14:00:00Z CD KPG\.cd ',), (r'^G G CD ', r'^E E1 CD ', r'^E R1 CD ')))),
    ('kpd-edit-carriers', 'into-phone', "carriers kept; G's and R1's lost; E1's restored",
     plus(carriers_as_phone, cd((r'^E E1 CD ',), (r'^G G CD ', r'^E R1 CD ')))),
    ('kpd-delete', 'into-kp', 'E2 gone, its record kept, D1 and D2 kept; custom data lost as in step 1',
     plus(carriers_as_phone, cd((r'^D E2 ', r'^D D1 ', r'^D D2 '), (r'^E E2 ', r'^E E1 CD ', r'^E R1 CD ', r'^G G CD ')))),
    ('kpd-delete', 'into-phone', 'E2 gone, record kept; custom data restored',
     plus(carriers_as_phone, cd((r'^D E2 ', r'^E E1 CD ', r'^E R1 CD ', r'^G G CD '), (r'^E E2 ',)))),
])

sync_rows('5.3', [(lbl, w, "the phone's file exactly", exactly())
                  for lbl in ('kpdx-save-argon', 'kpdx-edit-e1', 'kpdx-edit-carriers', 'kpdx-rename-g', 'kpdx-delete')
                  for w in ('into-kp', 'into-phone')] + [
    ('kpdx-merge-fromCP-with-C', 'into-kp', "X1 back; C's stale record for X2 dropped; Y3 back in G",
     cd((r'^E X1 S Title ', r'^E Y3 parent G '), (r'^D X2 ',))),
    ('kpdx-merge-fromCP-with-C', 'into-phone', "X1 back; X2's record dropped",
     cd((r'^E X1 S Title ',), (r'^D X2 ',))),
    ('kpdx-merge-fromCP-with-P', 'into-kp', "P's record for X1 restored; X2's record dropped; Y3's previous parent and location time restored",
     cd((r'^D X1 2026-11-01T16:00:00Z$', r'^E Y3 prev G$', r'^E Y3 t\.location 2026-11-01T16:00:00Z$'), (r'^D X2 ',))),
    ('kpdx-merge-fromCP-with-P', 'into-phone', "the same, and M2 becomes P's version",
     plus(cd((r'^D X1 2026-11-01T16:00:00Z$', r'^E Y3 prev G$', r'^E Y3 t\.location 2026-11-01T16:00:00Z$'), (r'^D X2 ',)), m2_is('p'))),
    ('kpdx-merge-fromPC-with-C', 'into-kp', "the phone's merge result exactly", exactly()),
    ('kpdx-merge-fromPC-with-C', 'into-phone', "the same, but M2 becomes C's version",
     plus(exactly(r'^E M2 S KPG\.20261115T130000Z\.'), m2_is('c'))),
    ('kpdx-merge-fromPC-with-P', 'into-kp', 'Y3 back in N (P\'s place)',
     plus(exactly(r'^(E Y3 parent |G (G|N) children )'), cd((r'^E Y3 parent N ',)))),
    ('kpdx-merge-fromPC-with-P', 'into-phone', "the phone's merge result exactly", exactly()),
])


@claim('5-inputs', '5, the first paragraph', 'each phone-saved file synchronized with the copy KeePass would '
       'hold: the base the phone was given, and for the merges run 1 of each cell with C and with P')
def _(x):
    out = []
    for label in sorted(os.listdir(os.path.join(x.root, 'sync'))):
        ph, kp = x.sync(label, 'phone'), x.sync(label, 'keepass')
        name = ph.head.split()[2]
        merge = '-merge-' in label
        want_kp = label.rsplit('-with-', 1)[1] if merge else 'S-argon'
        out.append(check(name.endswith('-r1.kdbx') if merge else name == label + '.kdbx',
                         '%s: the phone file %s' % (label, name)))
        out.append(check(kp.head.split('sha256=')[1] == x.base(want_kp).head.split('sha256=')[1],
                         '%s: KeePass\'s copy is %s as given' % (label, want_kp)))
    return all_of(*out)


@claim('6-history', '6, the eighth item', 'no entry reached HistoryMaxItems (8) in any run')
def _(x):
    top, where = 0, ''
    for sub in ('runs', 'sync'):
        for d, _, files in os.walk(os.path.join(x.root, sub)):
            for f in files:
                if f.endswith('.dump.txt'):
                    for y in Dump(os.path.join(d, f)).get(r'^E \S+ history\.count '):
                        n = int(y.split()[-1])
                        if n > top:
                            top, where = n, '%s in %s' % (y.split()[1], f)
    return check(0 < top < 8, 'the most history items any entry holds is %d (%s)' % (top, where))


def main(argv):
    bases = None
    root = RECORDED
    if '--bases' in argv:
        bases = os.path.abspath(argv[argv.index('--bases') + 1])
    if '--root' in argv:
        root = os.path.abspath(argv[argv.index('--root') + 1])
    x = Ctx(root, bases or os.path.join(root, 'base'))
    only_bases = bases is not None
    bad = []
    n = 0
    for cid, where, text, fn in CLAIMS:
        if only_bases and not cid.startswith('1.3-') or (only_bases and cid == '1.3-memprot'):
            continue
        n += 1
        try:
            ok, note = fn(x)
        except (OSError, IndexError, ValueError, AttributeError, TypeError) as e:
            ok, note = False, 'cannot check: %s: %s' % (type(e).__name__, e)
        if not ok:
            bad.append(cid)
        print('%-15s %-28s %s' % ('holds' if ok else 'DOES NOT HOLD', cid, text[:150]))
        if not ok or '-v' in argv:
            print('%15s %s' % ('', note[:400]))
    unexpected = set(bad) - KNOWN
    missing = set() if only_bases else KNOWN - set(bad)
    print('== %d claims, %d hold, %d do not: %s' % (n, n - len(bad), len(bad), ', '.join(bad) or 'none'))
    if unexpected or missing:
        print('== UNEXPECTED: %s; listed in KNOWN but held: %s' % (sorted(unexpected), sorted(missing)))
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
