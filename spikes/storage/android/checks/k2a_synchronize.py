"""Holds every table of `papers\\ground\\measured-k2a-synchronize.md` to its record, on the branch.

The emulator runs cannot be repeated here, so what is checked is what they left under
`recorded\\k2a-synchronize\\`: the dumps of the two given copies, of the server's file, the cache, the
local backup, the phone's copy before a synchronize, the run's SNEW and KeePass's own Synchronize of
the same two, the diffs, the verdict lines, the step logs and the server's log. Each claim below is
one cell of a table, or one number of the prose around it, named by its section and case; the check
states it over the lines that decide it, prints `holds` or `DOES NOT HOLD` with those lines, and
exits 0 when exactly the claims listed in `KNOWN` do not hold.

Every item of the synchronize pair is read off a dump the way `sverdict.sh` reads it: an entry is
there when its `parent` line is, a password is the current `S Password` line, and "in history" is
an `H<time> S Password` line holding the other side's value. "Server" is `server.kdbx`, "cache" is
`cache.kdbx`, both pulled at the end of the run. Two readings are this checker's, not the paper's:
"SOLD with ..." sets aside the access times and usage counts every open changes, and "none" of the
phone's own changes sets aside the per-key time every Keepass2Android save drops.

    uv run --no-project python -I -X utf8 spikes\\storage\\android\\checks\\k2a_synchronize.py [-v] [--root <dir>]
"""
import os
import re
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
RECORDED = os.path.join(HERE, '..', 'recorded', 'k2a-synchronize')
PINS = os.path.join(HERE, '..', '..', '..', '..', 'android.sha256')

# Claims the record does not bear out, by identifier, each named in RUN.md.
KNOWN = {'4-9-sequence'}

MERGE_TITLE = 'Merge changes?'
MERGE_TEXT = ('The database file was modified externally. Do you want to load and merge the changes before '
              'saving? Select No if you want to overwrite the external changes.')
PHONE = {'P2': 'p2-old-phone', 'Q1': 'q1-old-phone', 'Q2': 'q2-old-phone', 'Q3': 'q3-old-phone'}
DESK = {'P1': 'p1-desktop', 'Q1': 'q1-desktop', 'Q2': 'q2-desktop', 'Q3': 'q3-desktop'}
B_PREFS = {'UseOfflineCache': 'true', 'SyncOfflineCacheInBackground_key': 'true', 'CheckForFileChangesOnSave': 'true',
           'UseFileTransactions': 'true', 'AlwaysMergeOnConflict': 'false'}
# What every open of a file changes, an entry's or a group's access time and usage count, which the
# paper's "SOLD with ..." does not count as a change and its "the usage count" names once.
READS = r'(E|G) \S+ (H\S+ )?(t\.accessed|usage) '


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

    @property
    def sha(self):
        m = re.search(r'sha256=(\w+)', self.head)
        return m.group(1) if m else None

    def pw(self, alias):
        cur = [re.search(r'"(.*)"$', y).group(1) for y in self.get(r'^E %s S Password ' % alias)]
        hist = [re.search(r'"(.*)"$', y).group(1) for y in self.get(r'^E %s H\S+ S Password ' % alias)]
        return (cur[0] if cur else None), hist

    def modified(self, alias):
        got = self.get(r'^E %s t\.modified ' % alias)
        return got[0].split()[-1] if got else None


def item(d, name):
    """What a dump holds of one item of the pair, as the table's rows name them."""
    if name == 'KG3':
        return d.has(r'^E KG3 parent KG ')
    if name == 'NewKey':
        return d.has(r'^M CD KPG\.NewKey ')
    if name == 'DZ':
        return d.has(r'^D DZ ')
    if name == 'X1':
        return not d.has(r'^E X1 parent ') and d.has(r'^D X1 ')
    if name == 'NP':
        return d.has(r'^E \S+ S Title p=. "phone-new"$')
    raise KeyError(name)


class Log:
    def __init__(self, path):
        self.path = path
        self.lines = read(path)

    def find(self, rx, start=0):
        r = re.compile(rx)
        for i in range(start, len(self.lines)):
            if r.search(self.lines[i]):
                return i
        return -1

    def all(self, rx):
        r = re.compile(rx)
        return [i for i, x in enumerate(self.lines) if r.search(x)]

    def step(self, rx, start=0):
        return self.find(r'^\[[^]]+\] \S+\.flow:\d+ ' + rx, start)

    def rc(self):
        got = [x for x in self.lines if x.startswith('### flow rc=')]
        return got[-1] if got else ''

    def failed(self):
        return [x for x in self.lines if 'FAILED' in x]

    def merge_dialogs(self):
        return self.all(r"dialog alertTitle: 'Merge changes\?'")

    def dialog_text(self, i):
        out = {}
        for x in self.lines[i:i + 8]:
            m = re.search(r"dialog (alertTitle|message|button\d): '(.*)'$", x)
            if m:
                out[m.group(1)] = m.group(2)
        return out

    def stat(self, label):
        """The server's file at a named point: (size, mtime, sha)."""
        i = self.find(r'server \S+ at %s: ' % re.escape(label))
        if i < 0:
            return None
        m = re.search(r': (\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+)', self.lines[i])
        return {'at': i, 'size': int(m.group(1)), 'mtime': m.group(2), 'sha': m.group(3)}

    def put(self, start=0):
        """A file put on the server: the `after:` line of srv-put.sh, and what it came from."""
        i = self.find(r'server \S+ after:  ', start)
        if i < 0:
            return None
        m = re.search(r'after:  (\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+) \(from (\S+)\)', self.lines[i])
        return {'at': i, 'sha': m.group(3), 'from': m.group(4)}

    def events(self, label):
        return [x.split(label + ': ', 1)[1] for x in self.lines if re.search(r'events %s: ' % re.escape(label), x)]

    def prefs(self, label):
        i = self.find(r'prefs %s:' % re.escape(label))
        if i < 0:
            return None
        return dict(re.findall(r'name="(\w+)" value="(\w+)"', self.lines[i]))

    def check_states(self):
        return [x.rsplit('checked=', 1)[1] for x in self.lines if ' checked=' in x and "'" in x]

    def listed(self, start, end=None):
        """Every text a `list` printed between two lines."""
        return [m.group(1) for m in (re.search(r"text='([^']*)'", y) for y in self.lines[start:end]) if m]

    def version_files(self):
        return dict((m.group(1), m.group(2)) for m in
                    (re.search(r'^cache file \S+\.(cache\.(?:base)?version): (\w+)$', y) for y in self.lines) if m)


class Ctx:
    def __init__(self, root):
        self.root = root
        self.cache = {}
        self.verdicts = {}
        current = None
        for x in read(os.path.join(root, 'logs', 'sverdicts.txt')):
            m = re.match(r'^(\S+)/(\S+): (.*)$', x)
            if not m:
                continue
            if m.group(1) != 'keepass-sync':
                current = m.group(1)
                self.verdicts[(current, m.group(2))] = m.group(3)
            else:
                self.verdicts[(current, 'keepass-sync/' + m.group(2))] = m.group(3)

    def dump(self, run, name):
        p = os.path.join(self.root, 'runs', run, name + '.kdbx.dump.txt')
        if p not in self.cache:
            self.cache[p] = Dump(p)
        return self.cache[p]

    def base(self, name):
        p = os.path.join(self.root, 'base', name + '.kdbx.dump.txt')
        if p not in self.cache:
            self.cache[p] = Dump(p)
        return self.cache[p]

    def log(self, run):
        p = os.path.join(self.root, 'logs', 'sync', run + '.log')
        if p not in self.cache:
            self.cache[p] = Log(p)
        return self.cache[p]

    def nc_log(self, name):
        return Log(os.path.join(self.root, 'logs', 'sync-not-counted', name + '.log'))

    def snew(self, run):
        p = os.path.join(self.root, 'runs', run, 'SNEW.kdbx.dump.txt')
        return self.dump(run, 'SNEW') if os.path.exists(p) else self.base('SNEW-fixed')


CLAIMS = []


def claim(cid, where, text):
    def wrap(fn):
        CLAIMS.append((cid, where, text, fn))
        return fn
    return wrap


def all_of(*checks):
    notes = []
    for ok, note in checks:
        notes.append(note)
        if not ok:
            return False, note
    return True, '; '.join(n for n in notes if n)[:300]


def check(cond, note_ok, note_bad=None):
    return (True, note_ok) if cond else (False, note_bad or ('not: ' + note_ok))


def only_changes(a, b, allowed):
    ca, cb = Counter(a.lines), Counter(b.lines)
    gone, new = list((ca - cb).elements()), list((cb - ca).elements())
    rx = re.compile(allowed) if allowed else None
    bad = [x for x in gone + new if not (rx and rx.search(x))]
    return check(not bad, '%d lines differ, all of them %s' % (len(gone) + len(new), allowed or 'none'),
                 'also differs: ' + ' | '.join(x[:100] for x in bad[:4]))


# ------------------------------------------------------------------ the runs

S1 = ['s1-nothing-r1']
S2 = ['s2-server-r1', 's2-server-r2']
S3A = ['s3a-phone-reachable-r1']
S3B = ['s3b-phone-serverstopped-r1']
S3C = ['s3c-phone-cacheonly-r1', 's3c-phone-cacheonly-r2']
S4M = ['s4-both-cacheonly-merge-r1', 's4-both-cacheonly-merge-r2']
S4O = ['s4-both-cacheonly-overwrite-r1']
S4B = ['s4b-both-serverstopped-merge-r1']
S4P = ['s4B-both-cacheonly-bgsync-merge-r1']
S5M = ['s5-save-merge-r1', 's5-save-merge-r2']
S5O = ['s5-save-overwrite-r1']
S5P = ['s5B-save-bgsync-merge-r1']
S6 = ['s6-alwaysmerge-r1']
S7 = ['s7-nocheck-r1']
S8 = ['s8-reopen-r1', 's8-reopen-r2']
S9 = ['s9-quickunlock-merge-r1']
SECTION2 = S1 + S2 + S3A + S3B + S3C + S4M + S4O + S4B + S4P
SECTION3 = S5M + S5O + S5P + S6 + S7
SECTION4 = S8 + S9
BOTH = S4M + S4O + S4B + S4P


def counted(x, tags):
    out = [check(x.log(t).rc().startswith('### flow rc=0') and not x.log(t).failed() and
                 os.path.exists(os.path.join(x.root, 'runs', t, 'server.kdbx.dump.txt')),
                 '', '%s: %s, %d failures' % (t, x.log(t).rc(), len(x.log(t).failed()))) for t in tags]
    out.append(check(True, '%d runs: rc=0, no failure, the server read back' % len(tags)))
    return all_of(*out)


def no_question(x, tags):
    return all_of(*[check(not x.log(t).merge_dialogs(), '', '%s: "Merge changes?" came' % t) for t in tags] +
                  [check(True, 'no "Merge changes?" in %d runs' % len(tags))])


def the_question(x, tags, after_rx, answer):
    out = []
    for t in tags:
        lg = x.log(t)
        a = lg.step(after_rx)
        md = lg.merge_dialogs()
        d = lg.dialog_text(md[0]) if md else {}
        ans = lg.step(r'tap \^%s' % answer, md[0]) if md else -1
        out.append(check(a >= 0 and md and md[0] > a and ans > md[0] and
                         d == {'alertTitle': MERGE_TITLE, 'message': MERGE_TEXT, 'button3': 'Cancel',
                               'button2': 'No, overwrite', 'button1': 'Yes, merge'},
                         '', '%s: step %d, dialogs %s, answer %d, %s' % (t, a, md[:2], ans, d)))
    out.append(check(True, '"Merge changes?" with its text and buttons after %s, answered %s, in %d runs'
                     % (after_rx, answer, len(tags))))
    return all_of(*out)


def server_is(x, tags, base):
    out = []
    for t in tags:
        s = x.dump(t, 'server')
        b = x.base(base)
        out.append(check(s.sha == b.sha, '', '%s: the server %s, not %s' % (t, s.sha[:12], base)))
    out.append(check(True, 'the server is %s byte for byte in %d runs' % (base, len(tags))))
    return all_of(*out)


def cache_is_server(x, tags):
    return all_of(*[check(x.dump(t, 'cache').sha == x.dump(t, 'server').sha, '',
                          '%s: cache %s, server %s' % (t, x.dump(t, 'cache').sha[:12], x.dump(t, 'server').sha[:12]))
                    for t in tags] + [check(True, 'the cache is the server\'s file in %d runs' % len(tags))])


def merged(d, label, q1, q2, q3, phone_new):
    """The desktop's additions there, P1 the desktop's, P2 the phone's, and Q1 to Q3 as given."""
    out = [check(all(item(d, n) for n in ('KG3', 'NewKey', 'DZ', 'X1')), '%s: KG3, NewKey, DZ, X1 deleted' % label,
                 '%s: %s' % (label, [n for n in ('KG3', 'NewKey', 'DZ', 'X1') if not item(d, n)])),
           check(d.pw('P1')[0] == 'p1-desktop' and d.pw('P2')[0] == 'p2-old-phone', 'P1 the desktop\'s, P2 the phone\'s',
                 '%s: P1 %s, P2 %s' % (label, d.pw('P1')[0], d.pw('P2')[0])),
           check(item(d, 'NP') == phone_new, 'phone-new %s' % ('there' if phone_new else 'not made'))]
    for alias, want in (('Q1', q1), ('Q2', q2), ('Q3', q3)):
        cur, hist = d.pw(alias)
        if want == 'desk':
            ok = cur == DESK[alias]
        elif want == 'desk/hist':
            ok = cur == DESK[alias] and PHONE[alias] in hist
        else:
            ok = cur == PHONE[alias] and DESK[alias] in hist
        out.append(check(ok, '%s %s' % (alias, want), '%s: %s current %s, history %s' % (label, alias, cur, hist)))
    return all_of(*out)


def phone_copy(d, label):
    """SOLD with the phone's own changes and nothing of the desktop's, current or in history."""
    out = [check(not any(item(d, n) for n in ('KG3', 'NewKey', 'DZ')) and d.has(r'^E X1 parent ') and
                 not d.has(r'^D X1 '), '%s: KG3, NewKey, DZ gone, X1 back without a record' % label)]
    for alias, v in DESK.items():
        cur, hist = d.pw(alias)
        out.append(check(cur != v and v not in hist, '', '%s: %s %s in %s %s' % (label, alias, v, cur, hist)))
    out.append(check(True, 'none of the desktop\'s passwords, current or in history'))
    return all_of(*out)


def per_key_dropped(d):
    return check(d.has(r'^F version 4\.0$') and all(y.endswith('@none') for y in d.get(r'^M CD ')),
                 'KDBX 4.0, every per-key time dropped')


# ------------------------------------------------------------------ 1.1 program, emulator, server

@claim('1.1-rclone', '1.1', 'rclone v1.68.2 linux-amd64, held to the published SHA256SUMS: 0e6fa180...a13c, matched; '
       'the server answering on port 8080')
def _(x):
    sums = read(os.path.join(x.root, 'server', 'SHA256SUMS'))
    want = '0e6fa18051e67fc600d803a2dcb10ddedb092247fc6eee61be97f64ec080a13c'
    line = [y for y in sums if y.endswith(' rclone-v1.68.2-linux-amd64.zip')]
    pins = [y.split()[0] for y in read(PINS) if 'rclone-v1.68.2-linux-amd64.zip' in y and not y.startswith('#')]
    log = read(os.path.join(x.root, 'server', 'rclone.log'))
    started = [y for y in log if 'WebDav Server started on [http://[::]:8080/]' in y]
    return all_of(check(line and line[0].split()[0] == want, 'SHA256SUMS: %s' % (line[0][:20] if line else '')),
                  check(pins == [want], 'android.sha256 pins the same hash'),
                  check(len(started) >= 1, 'rclone.log: the server started on :8080, %d times' % len(started)))


@claim('1.1-open', '1.1, how the file was opened', 'every run: Open file..., HTTP (WebDav), "Enter WebDav login data:", '
       '10.0.2.2:8080/, the user kpg; the file pressed in the program\'s browser; the cache, its version files and '
       'the local backup copy pulled')
def _(x):
    out = []
    for t in SECTION2 + SECTION3 + SECTION4:
        lg = x.log(t)
        ok = (lg.step(r'tap \^HTTP\.\.WebDav\.\$') >= 0 and lg.find(r"dialog alertTitle: 'Enter WebDav login data:'") >= 0
              and lg.step(r'type http_url 10\.0\.2\.2:8080/$') >= 0 and lg.step(r'type http_user kpg$') >= 0
              and lg.step(r'tap \^kpg-%s\.kdbx\$' % re.escape(t)) >= 0
              and lg.find(r'^pulled /data/data/keepass2android\.keepass2android/no_backup/OfflineCache/[0-9A-F]{64}\.cache -> cache\.kdbx') >= 0
              and set(lg.version_files()) == {'cache.version', 'cache.baseversion'}
              and lg.find(r'^pulled local backup http10\.0\.2\.28080kpg-%s\.kdbx -> ' % re.escape(t)) >= 0)
        out.append(check(ok, '', '%s: a step of the opening or a pulled file missing' % t))
    out.append(check(True, '%d runs' % (len(out))))
    return all_of(*out)


@claim('1.1-prefs', '1.1, cleartext HTTP', 'A: only permit_cleartext_traffic stored; B: that and UseOfflineCache, '
       'SyncOfflineCacheInBackground_key, CheckForFileChangesOnSave, UseFileTransactions true, AlwaysMergeOnConflict false')
def _(x):
    out = []
    for t in SECTION2 + SECTION3 + SECTION4:
        lg = x.log(t)
        mode = re.search(r'PREFS=(\w)', lg.lines[0]).group(1)
        got = lg.prefs('set ' + mode)
        want = {'permit_cleartext_traffic': 'true'}
        if mode == 'B':
            want.update(B_PREFS)
        out.append(check(got == want, '', '%s: %s %s' % (t, mode, got)))
    b = [t for t in SECTION2 + SECTION3 + SECTION4 if 'PREFS=B' in x.log(t).lines[0]]
    out.append(check(sorted(b) == sorted(S4P + S5P), 'A everywhere but %s, which ran B' % ', '.join(b)))
    return all_of(*out)


@claim('1.3-counts', '1.3', 'counted: 21 runs, 12 in section 2, 6 in section 3, 3 in section 4')
def _(x):
    logs = sorted(f[:-4] for f in os.listdir(os.path.join(x.root, 'logs', 'sync')) if f.endswith('.log'))
    return all_of(check((len(SECTION2), len(SECTION3), len(SECTION4)) == (12, 6, 3), '12, 6 and 3'),
                  check(logs == sorted(SECTION2 + SECTION3 + SECTION4), '21 step logs, each a case\'s run',
                        'logs %s' % logs), counted(x, SECTION2 + SECTION3 + SECTION4))


@claim('1.3-not-counted', '1.3', 'not counted: the first case 1 (a stale listing); four runs stopped by hand (cases 2, '
       '3c, 3c and 4); two attempts at 3c (a checkbox, the wrong menu item); the first case 4 (server SNEW, cache the '
       'phone\'s copy); the first case 5 in B; the exploration x-webdav')
def _(x):
    nc = sorted(f[:-4] for f in os.listdir(os.path.join(x.root, 'logs', 'sync-not-counted')) if f.endswith('.log'))
    stale = x.nc_log('s1-nothing-r1-attempt1')
    a1, a2 = x.nc_log('s3c-phone-cacheonly-r1-attempt1'), x.nc_log('s3c-phone-cacheonly-r1-attempt2')
    s5b = x.nc_log('s5B-save-bgsync-r1-attempt1')
    stopped = ['s2-server-r1-attempt1', 's3c-phone-cacheonly-r2-attempt1', 's3c-phone-cacheonly-r2-attempt2',
               's4-both-cacheonly-r2-attempt1']
    v = dict((k, val) for k, val in
             (re.match(r'^(\S+): (.*)$', y).groups() for y in read(os.path.join(x.root, 'logs', 'sverdicts-not-counted.txt'))
              if y))
    return all_of(
        check(len(nc) == 10 and 'x-webdav-attempt1' in nc, '10 logs, the exploration among them', 'logs %s' % nc),
        check(any("text='kpg-x-webdav.kdbx'" in y for y in stale.lines) and
              any('no node matches ^kpg-s1-nothing-r1.kdbx$' in y for y in stale.failed()),
              'the first case 1 listed the file before it'),
        check(all(not x.nc_log(n).rc() and not x.nc_log(n).failed() for n in stopped),
              'four logs end part way, with no result and no failure'),
        check(any('no checkbox after ^Work.on.internal.cache.only$' in y for y in a1.failed()) and
              any('no node matches ^Work.on.internal.cache.only$' in y for y in a2.failed()),
              'the two 3c attempts: a checkbox, the item not in the menu'),
        check('=SNEW' in v['s4-both-cacheonly-r1-attempt1/server'] and
              'P2=p2-old-phone' in v['s4-both-cacheonly-r1-attempt1/cache'] and
              'KG3:NO' in v['s4-both-cacheonly-r1-attempt1/cache'], 'the first case 4: server SNEW, cache the phone\'s'),
        check(s5b.merge_dialogs() and any('this-case-expected-no-question' in y for y in s5b.failed()),
              'the first case 5 in B met the question'))


# ------------------------------------------------------------------ 1.2 the two copies

def row12(name, sold, snew, phone):
    @claim('1.2-%s-sold' % name, '1.2, %s, SOLD' % name, sold[0])
    def _(x):
        return sold[1](x, x.base('SOLD'))

    @claim('1.2-%s-snew' % name, '1.2, %s, SNEW' % name, snew[0])
    def _(x):
        return snew[1](x, x.base('SNEW-fixed'))

    @claim('1.2-%s-phone' % name, "1.2, %s, the phone's own changes" % name, phone[0])
    def _(x):
        return all_of(*[phone[1](x, t, x.dump(t, 'phone-before-sync'), x.base('SOLD')) for t in BOTH])


def same_as_sold(rx):
    """The lines as in SOLD, a per-key time aside: every Keepass2Android save drops it."""
    def fn(x, t, ph, sold):
        a = [re.sub(r' @\S+$', '', y) for y in sold.get(rx)]
        b = [re.sub(r' @\S+$', '', y) for y in ph.get(rx)]
        return check(a == b, '', '%s: %s' % (t, set(a) ^ set(b)))
    return fn


def pw_row(alias, old, new):
    def s(x, d):
        return check(d.pw(alias)[0] == old, '%s %s' % (alias, old), '%s %s' % (alias, d.pw(alias)[0]))

    def n(x, d):
        return check(d.pw(alias)[0] == new, '%s %s' % (alias, new), '%s %s' % (alias, d.pw(alias)[0]))
    return s, n


def kg_sold(x, d):
    return all_of(check(d.has(r'^G KG flags .* search=0 autotype=0 ') and d.has(r'^G KG name "KeePassGate"$'),
                        'KeePassGate, searching and auto-type off'),
                  check(d.has(r'^E KG1 S Title p=0 "KPG grant 1"$') and d.has(r'^E KG1 S KPG\.Grant p=0 '),
                        'KG1 "KPG grant 1" with KPG.Grant'),
                  check(d.has(r'^E KG2 S Title p=0 "KPG admission 1"$') and d.has(r'^E KG2 S KPG\.Admission p=0 ') and
                        d.has(r'^E KG2 S KPG\.Secret p=1 '), 'KG2 "KPG admission 1", KPG.Admission, KPG.Secret protected'),
                  check(not d.has(r'^E KG3 '), 'no KG3'))


def kg_snew(x, d):
    s = x.base('SOLD')
    return all_of(check(d.get(r'^E KG[12] ') == s.get(r'^E KG[12] '), 'KG1 and KG2 as in SOLD'),
                  check(d.has(r'^E KG3 parent KG ') and d.has(r'^E KG3 S Title p=0 "KPG revocation"$') and
                        d.has(r'^E KG3 S KPG\.Revoked\.c2 '), 'KG3 "KPG revocation" with KPG.Revoked.c2'))


row12('KeePassGate', ('KG1 "KPG grant 1" (KPG.Grant), KG2 "KPG admission 1" (KPG.Admission, protected KPG.Secret)', kg_sold),
      ('the same, and KG3 "KPG revocation" (KPG.Revoked.c2)', kg_snew), ('none', same_as_sold(r'^(E KG\d |G KG )')))
row12('custom-data',
      ('KPG.Profiles', lambda x, d: check([y.split()[2] for y in d.get(r'^M CD ')] == ['KPG.Profiles'], 'KPG.Profiles only')),
      ('and KPG.NewKey, a key SOLD lacks', lambda x, d: check([y.split()[2] for y in d.get(r'^M CD ')] ==
                                                              ['KPG.NewKey', 'KPG.Profiles'], 'KPG.NewKey and KPG.Profiles')),
      ('none', same_as_sold(r'^M CD ')))
row12('deletions',
      ('D1', lambda x, d: check([y.split()[1] for y in d.get(r'^D (?!count)')] == ['D1'], 'D1 only')),
      ('and DZ, naming no entry, and X1, an entry SOLD holds unchanged',
       lambda x, d: all_of(check([y.split()[1] for y in d.get(r'^D (?!count)')] == ['D1', 'DZ', 'X1'], 'D1, DZ, X1'),
                           check(not d.has(r'^E DZ ') and not x.base('SOLD').has(r'^E DZ '), 'DZ names no entry'),
                           check(x.base('SOLD').has(r'^E X1 parent ') and not d.has(r'^E X1 '), 'X1 in SOLD, gone here'))),
      ('none', same_as_sold(r'^(D |E X1 )')))
for _a, _o, _n in (('P1', 'p1-old', 'p1-desktop'), ('P2', 'p2-old', 'p2-old')):
    _s, _w = pw_row(_a, _o, _n)
    row12(_a, (_o, _s), (_n if _a == 'P1' else 'unchanged', _w),
          ('none', same_as_sold(r'^E P1 S Password ')) if _a == 'P1' else
          ('p2-old-phone', lambda x, t, ph, s: check(ph.pw('P2')[0] == 'p2-old-phone', '', '%s: P2 %s' % (t, ph.pw('P2')[0]))))


def q_snew(alias):
    def fn(x, d):
        out = [check(d.pw(alias)[0] == DESK[alias] and d.pw(alias)[1] == [alias.lower() + '-old'],
                     '%s %s, the old value in history' % (alias, DESK[alias]))]
        if alias == 'Q1':
            out.append(check(d.modified('Q1') == '2026-11-01T18:00:00Z', 'at 2026-11-01T18:00Z'))
        return all_of(*out)
    return fn


def q_phone(alias):
    def fn(x, t, ph, sold):
        out = [check(ph.pw(alias)[0] == PHONE[alias], '', '%s: %s %s' % (t, alias, ph.pw(alias)[0]))]
        sn = x.snew(t)
        tp, ts = ph.modified(alias), sn.modified(alias)
        if alias == 'Q1':
            out.append(check(tp > '2026-11-15' and ts == '2026-11-01T18:00:00Z', '',
                             '%s: Q1 at the phone %s, the desktop %s' % (t, tp, ts)))
        elif alias == 'Q2':
            from datetime import datetime, timedelta
            f = '%Y-%m-%dT%H:%M:%SZ'
            out.append(check(datetime.strptime(ts, f) - datetime.strptime(tp, f) == timedelta(hours=1), '',
                             '%s: Q2 at the phone %s, the desktop %s' % (t, tp, ts)))
        else:
            out.append(check(tp == ts, '', '%s: Q3 at the phone %s, the desktop %s' % (t, tp, ts)))
        return all_of(*out)
    return fn


row12('Q1', ('q1-old', pw_row('Q1', 'q1-old', '')[0]), ('q1-desktop at 2026-11-01T18:00Z', q_snew('Q1')),
      ('q1-old-phone at the phone\'s clock', q_phone('Q1')))
row12('Q2', ('q2-old', pw_row('Q2', 'q2-old', '')[0]), ('q2-desktop one hour after the phone\'s edit', q_snew('Q2')),
      ('q2-old-phone', q_phone('Q2')))
row12('Q3', ('q3-old', pw_row('Q3', 'q3-old', '')[0]), ('q3-desktop at the phone\'s edit\'s time exactly', q_snew('Q3')),
      ('q3-old-phone', q_phone('Q3')))
row12('NP', ('absent', lambda x, d: check(not item(d, 'NP'), 'absent')),
      ('absent', lambda x, d: check(not item(d, 'NP'), 'absent')),
      ('phone-new at the root', lambda x, t, ph, s: check(ph.has(r'^E (~\S+) S Title p=0 "phone-new"$') and
                                                          any(y.endswith('parent Root (Synchronize probe)')
                                                              for y in ph.get(r'^E ~\S+ parent ')), '', t)))
row12('K', ('untouched', lambda x, d: check(d.get(r'^E K ') == x.base('SOLD').get(r'^E K '), 'K as made')),
      ('untouched', lambda x, d: check(d.get(r'^E K ') == x.base('SOLD').get(r'^E K '), 'K as in SOLD')),
      ('untouched', same_as_sold(r'^E K (?!t\.accessed)')))


@claim('1.2-SNEW-fixed', '1.2, after the table', 'SNEW-fixed: Q2 at 2027-01-01, Q3 at 18:00Z; a run that changes '
       'both sides builds its own SNEW, Q2 an hour after the phone\'s edit, Q3 at the same second, renamed over the file')
def _(x):
    f = x.base('SNEW-fixed')
    out = [check(f.modified('Q2') == '2027-01-01T00:00:00Z' and f.modified('Q3') == '2026-11-01T18:00:00Z',
                 'SNEW-fixed: Q2 2027-01-01, Q3 18:00Z')]
    for t in BOTH:
        lg = x.log(t)
        p = lg.put(lg.step(r'host sh \S+/mksnew\.sh '))
        out.append(check(p and p['from'] == 'SNEW.kdbx' and p['sha'] == x.dump(t, 'SNEW').sha, '',
                         '%s: put %s' % (t, p)))
    out.append(check(True, 'each of the %d runs put its own SNEW' % len(BOTH)))
    return all_of(*out)


# ------------------------------------------------------------------ 2 "Synchronize database..."

def sync_row(case, tags, n, seq, showed, server, cache):
    @claim('2-%s-runs' % case, '2, case %s, counted runs' % case, str(n))
    def _(x):
        return all_of(check(len(tags) == n, '%d runs' % n), counted(x, tags))

    @claim('2-%s-sequence' % case, '2, case %s, sequence' % case, seq[0])
    def _(x):
        return seq[1](x, tags)

    @claim('2-%s-showed' % case, '2, case %s, what the program showed' % case, showed[0])
    def _(x):
        return showed[1](x, tags)

    @claim('2-%s-server' % case, '2, case %s, server afterwards' % case, server[0])
    def _(x):
        return server[1](x, tags)

    @claim('2-%s-cache' % case, '2, case %s, cache afterwards' % case, cache[0])
    def _(x):
        return cache[1](x, tags)


def in_order(*rxs):
    def fn(x, tags):
        out = []
        for t in tags:
            lg = x.log(t)
            idx, at = [], 0
            for rx in rxs:
                at = lg.step(rx, at) if not rx.startswith('|') else lg.find(rx[1:], at)
                idx.append(at)
                if at < 0:
                    break
            out.append(check(all(i >= 0 for i in idx), '', '%s: %s not found in that order' % (t, rxs[len(idx) - 1])))
        out.append(check(True, 'in that order in %d runs' % len(tags)))
        return all_of(*out)
    return fn


def events_only_window(label):
    def fn(x, tags):
        out = []
        for t in tags:
            ev = x.log(t).events(label)
            out.append(check(ev and all(e.strip() == 'TYPE_WINDOW_STATE_CHANGED PackageName: keepass2android.keepass2android '
                                        'Text: [Keepass2Android]' for e in ev), '', '%s: %s' % (t, ev)))
        return all_of(*out + [check(True, 'the events after it: the window\'s own only')])
    return fn


def plus(*fns):
    def fn(x, tags):
        return all_of(*[f(x, tags) for f in fns])
    return fn


def stat_same(label_a, label_b):
    def fn(x, tags):
        out = []
        for t in tags:
            a, b = x.log(t).stat(label_a), x.log(t).stat(label_b)
            out.append(check(a and b and a['sha'] == b['sha'] and a['mtime'] == b['mtime'], '',
                             '%s: %s %s, %s %s' % (t, label_a, a, label_b, b)))
        return all_of(*out + [check(True, 'the server\'s file at %s is the file at %s, its time too' % (label_b, label_a))])
    return fn


def stat_is(label, base):
    def fn(x, tags):
        return all_of(*[check(x.log(t).stat(label)['sha'] == x.base(base).sha, '',
                              '%s: %s at %s' % (t, x.log(t).stat(label)['sha'][:12], label)) for t in tags] +
                      [check(True, 'at %s the server held %s' % (label, base))])
    return fn


def kpg_revocation_listed(want):
    def fn(x, tags):
        out = []
        for t in tags:
            lg = x.log(t)
            i = lg.step(r'tap \^KeePassGate\$', lg.step(r'tap \^Synchronize\.database'))
            j = lg.step(r'key BACK', i)
            got = 'KPG revocation' in lg.listed(i, j)
            out.append(check(got == want, '', '%s: KeePassGate %s KPG revocation' % (t, 'listed' if got else 'did not list')))
        return all_of(*out + [check(True, 'KeePassGate %s KPG revocation' % ('listed' if want else 'did not list'))])
    return fn


def server_phone_p2(x, tags):
    out = []
    for t in tags:
        s = x.dump(t, 'server')
        out.append(only_changes(x.base('SOLD'), s, r'^(E P2 |F version |M CD |' + READS + ')'))
        out.append(check(s.pw('P2')[0] == 'p2-old-phone', '%s: P2 p2-old-phone' % t))
    return all_of(*out)


def sync_changed_nothing(label_before):
    def fn(x, tags):
        out = []
        for t in tags:
            lg = x.log(t)
            a, b, e = lg.stat(label_before), lg.stat('after-sync'), lg.stat('end')
            out.append(check(a['sha'] == b['sha'] == e['sha'] == x.dump(t, 'server').sha, '',
                             '%s: %s, after-sync, end differ' % (t, label_before)))
        return all_of(*out + [check(True, 'Synchronize changed nothing on the server')])
    return fn


def same_file(x, tags):
    return cache_is_server(x, tags)


sync_row('1', S1, 1, ('open; look; Synchronize database...; look', in_order(r'tap \^Synchronize\.database', r'tap \^KeePassGate\$')),
         ('no dialog, no toast: the 20 s of events after the press only the window\'s own',
          plus(lambda x, t: no_question(x, t), events_only_window('synchronize'),
               lambda x, t: check(x.log(t[0]).step(r'host sh \S+/events\.sh 20 synchronize$') > 0, '20 s of events'))),
         ('SOLD, byte for byte, its time unchanged', plus(lambda x, t: server_is(x, t, 'SOLD'),
                                                          stat_same('before-sync', 'after-sync'), stat_same('before-sync', 'end'))),
         ('SOLD', lambda x, t: all_of(*[check(x.dump(r, 'cache').sha == x.base('SOLD').sha, 'the cache SOLD') for r in t])))


def s2_cache(x, tags):
    out = []
    for t in tags:
        vf = x.log(t).version_files()
        out.append(check(x.dump(t, 'cache').sha == x.base('SOLD').sha and
                         vf.get('cache.version', '').lower() == vf.get('cache.baseversion', '').lower() == x.base('SOLD').sha and
                         x.dump(t, 'cache-localbackup').sha == x.base('SNEW-fixed').sha,
                         '', '%s: cache %s, versions %s, backup %s' % (t, x.dump(t, 'cache').sha[:12], vf,
                                                                       x.dump(t, 'cache-localbackup').sha[:12])))
    return all_of(*out + [check(True, 'the cache SOLD, its versions SOLD\'s hash, the local backup SNEW-fixed')])


sync_row('2', S2, 2, ('open; SNEW-fixed put on the server; Synchronize database...; look',
                      plus(in_order(r'host sh \S+/srv-put\.sh \S+ \S+/SNEW-fixed\.kdbx', r'tap \^Synchronize\.database',
                                    r'tap \^KeePassGate\$'))),
         ('no dialog, no toast; afterwards KeePassGate listed KPG revocation',
          plus(lambda x, t: no_question(x, t), events_only_window('synchronize'), kpg_revocation_listed(True))),
         ('SNEW-fixed byte for byte, unchanged', plus(lambda x, t: server_is(x, t, 'SNEW-fixed'),
                                                       stat_same('before-sync', 'after-sync'))),
         ('SOLD, its .version and .baseversion SOLD\'s hash; the local backup rewritten as SNEW', s2_cache))
sync_row('3a', S3A, 1, ('open; P2 edited and saved; Synchronize database...',
                        in_order(r'appendpw entry_password -phone$', r'tap menu_save$', r'tap \^Synchronize\.database')),
         ('no dialog at the save, none at Synchronize', lambda x, t: no_question(x, t)),
         ('SOLD with P2 p2-old-phone, written by the save itself; Synchronize changed nothing',
          plus(server_phone_p2, sync_changed_nothing('after-save'))),
         ('the same file', same_file))


def s3b_showed(x, tags):
    out = [no_question(x, tags)]
    for t in tags:
        ev = x.log(t).events('save-P2')
        out.append(check(any('TYPE_NOTIFICATION_STATE_CHANGED' in e and 'P2: Entry is available through KP2A Keyboard' in e
                             for e in ev) and all('KP2A Keyboard' in e or 'TYPE_WINDOW_STATE_CHANGED' in e for e in ev),
                         'at the save only the notification "P2: Entry is available through KP2A Keyboard"', '%s: %s' % (t, ev)))
    out.append(events_only_window('synchronize')(x, tags))
    return all_of(*out)


sync_row('3b', S3B, 1, ('open; server stopped; P2 edited and saved; server started; Synchronize database...',
                        in_order(r'host sh \S+/server\.sh stop$', r'tap menu_save$', r'host sh \S+/server\.sh start$',
                                 r'tap \^Synchronize\.database')),
         ('no dialog and no toast at the save, only a notification; none at Synchronize', s3b_showed),
         ('SOLD while stopped; after Synchronize, the phone\'s file (P2 p2-old-phone)',
          plus(stat_is('after-save-server-stopped', 'SOLD'), server_phone_p2,
               lambda x, t: all_of(*[check(x.log(r).stat('after-sync')['sha'] == x.dump(r, 'server').sha, 'after-sync') for r in t]))),
         ('the same', same_file))


def menu_in_cache_mode(x, tags):
    out = []
    for t in tags:
        lg = x.log(t)
        i = lg.step(r'tap More\.options', lg.step(r'tap \^Work\.on\.internal\.cache\.only\$'))
        j = lg.step(r'tap \^Synchronize\.cache\.copy', i)
        items = lg.listed(i, j)
        out.append(check('Synchronize cache copy with source' in items and not any(y.startswith('Synchronize database')
                                                                                for y in items) and
                         'Work on internal cache only' not in items, '', '%s: the menu %s' % (t, items)))
    return all_of(*out + [check(True, 'the menu in that mode: "Synchronize cache copy with source", neither of the others')])


sync_row('3c', S3C, 2, ('open; Work on internal cache only; P2 edited and saved; Synchronize cache copy with source; '
                        'Synchronize database...', plus(in_order(r'tap \^Work\.on\.internal\.cache\.only\$', r'tap menu_save$',
                                                                 r'tap \^Synchronize\.cache\.copy\.with\.source\$',
                                                                 r'tap \^Synchronize\.database'), menu_in_cache_mode)),
         ('no dialog; nothing at the save', lambda x, t: no_question(x, t)),
         ('SOLD after the save (nothing sent); after "Synchronize cache copy with source", the phone\'s file',
          plus(stat_is('after-save-cache-only', 'SOLD'), server_phone_p2,
               stat_same('after-sync-cache-copy', 'after-sync'))),
         ('the same', same_file))


def s4_seq(offline):
    def fn(x, tags):
        steps = ([r'tap \^Work\.on\.internal\.cache\.only\$'] if not offline else [r'host sh \S+/server\.sh stop$'])
        steps += [r'appendpw entry_password -phone$', r'text phone-new$', r'host sh \S+/mksnew\.sh ']
        steps += ([r'tap \^Synchronize\.cache\.copy\.with\.source\$', r'tap \^Synchronize\.database'] if not offline else
                  [r'host sh \S+/server\.sh start$', r'tap \^Synchronize\.database'])
        out = [in_order(*steps)(x, tags)]
        for t in tags:
            lg = x.log(t)
            edits = [lg.step(r'tap \^%s\$' % a) for a in ('P2', 'Q1', 'Q2', 'Q3')]
            saved = lg.all(r'flow:\d+ tap menu_save$')
            first_q = lg.merge_dialogs()[0] if lg.merge_dialogs() else len(lg.lines)
            put = lg.step(r'host sh \S+/mksnew\.sh ')
            out.append(check(all(e >= 0 for e in edits) and len([s for s in saved if s < put]) == 5 and put < first_q,
                             '', '%s: edits %s, saves before the put %d' % (t, edits, len([s for s in saved if s < put]))))
            pre = x.dump(t, 'phone-before-sync')
            out.append(check(x.log(t).put(put)['sha'] == x.dump(t, 'SNEW').sha and pre.sha != x.base('SOLD').sha,
                             '', '%s: the SNEW put, the cache changed' % t))
        out.append(check(True, 'five saves to the cache before the SNEW was put'))
        return all_of(*out)
    return fn


def s4_saves_quiet(x, tags):
    """Each of the five saves with no question, the server still SOLD before the put."""
    out = []
    for t in tags:
        lg = x.log(t)
        put = lg.step(r'host sh \S+/mksnew\.sh ')
        out.append(check(not [i for i in lg.merge_dialogs() if i < put], '', '%s: a question before the put' % t))
        b = lg.find(r'server \S+ before: ', put)
        out.append(check(b > 0 and x.base('SOLD').sha in lg.lines[b], '', '%s: the server was not SOLD before the put' % t))
    return all_of(*out + [check(True, 'no question at the five saves; the server SOLD until the put')])


S4_MERGE_CELL = ('a merge: KG3, NewKey, DZ, X1\'s deletion; P1 the desktop\'s, P2 the phone\'s, phone-new; Q1 the '
                 'phone\'s with the desktop\'s in history; Q2 the desktop\'s with the phone\'s in history; Q3 the phone\'s '
                 'with the desktop\'s in history; KDBX 4.0, per-key times dropped')


def s4_merged(x, tags):
    out = []
    for t in tags:
        s = x.dump(t, 'server')
        out.append(merged(s, t, 'phone/hist', 'desk/hist', 'phone/hist', True))
        out.append(per_key_dropped(s))
    return all_of(*out)


sync_row('4-merge', S4M, 2, ('open; Work on internal cache only; P2, Q1, Q2, Q3 edited and saved, phone-new added, each '
                             'with no question, the server still SOLD; SNEW put; Synchronize cache copy with source; '
                             'Synchronize database...; look', plus(s4_seq(False), s4_saves_quiet)),
         ('at "Synchronize cache copy with source" the "Merge changes?" dialog; Yes, merge; nothing at Synchronize database... after it',
          plus(lambda x, t: the_question(x, t, r'tap \^Synchronize\.cache\.copy\.with\.source\$', r'Yes'),
               lambda x, t: all_of(*[check(len(x.log(r).merge_dialogs()) == 1, '', r) for r in t]))),
         (S4_MERGE_CELL, s4_merged), ('the same file', same_file))


def s4_overwrite_server(x, tags):
    out = []
    for t in tags:
        s, pre = x.dump(t, 'server'), x.dump(t, 'phone-before-sync')
        out.append(only_changes(pre, s, r' t\.accessed '))
        out.append(phone_copy(s, t))
    return all_of(*out)


sync_row('4-overwrite', S4O, 1, ('as above, the dialog answered No, overwrite', plus(s4_seq(False), s4_saves_quiet)),
         ('the same dialog at "Synchronize cache copy with source"; nothing at Synchronize database... after it',
          plus(lambda x, t: the_question(x, t, r'tap \^Synchronize\.cache\.copy\.with\.source\$', r'No'),
               lambda x, t: all_of(*[check(len(x.log(r).merge_dialogs()) == 1, '', r) for r in t]))),
         ('the phone\'s copy whole: no line differs from the phone\'s copy before the synchronize but access times; '
          'every item of the desktop\'s gone, from the entries and every history', s4_overwrite_server),
         ('the same file', same_file))


def s4b_showed(x, tags):
    out = [the_question(x, tags, r'tap \^Synchronize\.database', r'Yes')]
    for t in tags:
        lg = x.log(t)
        for a in ('P2', 'Q1', 'Q2', 'Q3'):
            ev = lg.events('save-' + a)
            out.append(check(any('%s: Entry is available through KP2A Keyboard' % a in e for e in ev) and
                             all('KP2A Keyboard' in e or 'TYPE_WINDOW_STATE_CHANGED' in e for e in ev), '',
                             '%s: save-%s %s' % (t, a, ev)))
    return all_of(*out + [check(True, 'at each save only the notification "<entry>: Entry is available through KP2A Keyboard"')])


sync_row('4b', S4B, 1, ('open; server stopped; the four password edits and phone-new, each saved with no question and '
                        'no warning, to the cache only; SNEW put; server started; Synchronize database...',
                        plus(s4_seq(True), s4_saves_quiet)),
         ('the "Merge changes?" dialog at Synchronize database...; Yes, merge', s4b_showed),
         ('the same merge as case 4', s4_merged), ('the same file', same_file))
sync_row('4-B', S4P, 1, ('as case 4, preferences B', plus(s4_seq(False), s4_saves_quiet)),
         ('the same dialog at "Synchronize cache copy with source"',
          lambda x, t: the_question(x, t, r'tap \^Synchronize\.cache\.copy\.with\.source\$', r'Yes')),
         ('the same merge as case 4', s4_merged), ('the same file', same_file))


@claim('2-cache-is-server', '2, after the table', 'in every case 4 and 4b run, the cache read back as the same file as the server')
def _(x):
    return cache_is_server(x, BOTH)


@claim('2-real-merge', '2, "Is it a real merge?"', 'KeePass 2.62\'s own Synchronize of the same two, in both directions: '
       'Keepass2Android\'s result held everything KeePass\'s held and differed in three ways only: Q3 (KeePass keeps the '
       'arriving copy\'s whole, the other nowhere; Keepass2Android the phone\'s with the desktop\'s in history), KDBX 4.0 '
       'with the per-key times dropped, and the usage count')
def _(x):
    out = []
    for t in S4M + S4B + S4P:
        s = x.dump(t, 'server')
        for k, q3 in (('kp-phone-open', 'q3-desktop'), ('kp-snew-open', 'q3-old-phone')):
            kp = x.dump(t, 'keepass-sync/' + k)
            cur, hist = kp.pw('Q3')
            out.append(check(cur == q3 and not [h for h in hist if h in ('q3-desktop', 'q3-old-phone')], '',
                             '%s %s: Q3 %s, history %s' % (t, k, cur, hist)))
            out.append(check(all(item(kp, n) for n in ('KG3', 'NewKey', 'DZ', 'X1', 'NP')) and kp.has(r'^D D1 ') and
                             kp.pw('P1')[0] == 'p1-desktop' and kp.pw('P2')[0] == 'p2-old-phone' and
                             kp.pw('Q1')[0] == 'q1-old-phone' and 'q1-desktop' in kp.pw('Q1')[1] and
                             kp.pw('Q2')[0] == 'q2-desktop' and 'q2-old-phone' in kp.pw('Q2')[1], '',
                             '%s %s: not everything' % (t, k)))
            out.append(only_changes(kp, s, r'^(E Q3 |F version |M CD |' + READS + ')'))
    out.append(check(True, '%d runs, both directions' % len(S4M + S4B + S4P)))
    return all_of(*out)


# ---------------------------------------------- 3 a plain save while the server's file changed

def save_seq(x, tags):
    return in_order(r'host sh \S+/srv-put\.sh \S+ \S+/SNEW-fixed\.kdbx', r'appendpw entry_password -phone$',
                    r'tap menu_save$')(x, tags)


def s5_merged(x, tags):
    return all_of(*[merged(x.dump(t, 'server'), t, 'desk', 'desk', 'desk', False) for t in tags])


def s5_overwritten(x, tags):
    return all_of(*[all_of(phone_copy(x.dump(t, 'server'), t), server_phone_p2(x, [t])) for t in tags])


def save_row(case, tags, n, prefs, showed, server):
    @claim('3-%s-runs' % case, '3, case %s, counted runs' % case, str(n))
    def _(x):
        return all_of(check(len(tags) == n, '%d runs' % n), counted(x, tags), save_seq(x, tags))

    @claim('3-%s-prefs' % case, '3, case %s, prefs' % case, prefs[0])
    def _(x):
        return prefs[1](x, tags)

    @claim('3-%s-showed' % case, '3, case %s, what the program showed' % case, showed[0])
    def _(x):
        return showed[1](x, tags)

    @claim('3-%s-server' % case, '3, case %s, server afterwards' % case, server[0])
    def _(x):
        return server[1](x, tags)

    @claim('3-%s-cache' % case, '3, case %s, cache afterwards' % case, 'the same file')
    def _(x):
        return same_file(x, tags)


def prefs_mode(mode):
    def fn(x, tags):
        return all_of(*[check(('PREFS=%s' % mode) in x.log(t).lines[0], '', t) for t in tags] +
                      [check(True, 'preferences %s' % mode)])
    return fn


def screen_then(setting, before, after, stored):
    def fn(x, tags):
        out = [prefs_mode('A')(x, tags)]
        for t in tags:
            lg = x.log(t)
            p = lg.prefs('after-tap') or {}
            out.append(check(lg.prefs('before-settings') == {} and lg.prefs('on-file-handling-screen') == B_PREFS and
                             lg.check_states() == [before, after] and p.get(setting) == stored, '',
                             '%s: %s, %s, %s' % (t, lg.prefs('on-file-handling-screen'), lg.check_states(), p)))
        return all_of(*out + [check(True, 'A, then what the screen stores (B); checkbox %s, pressed, %s; stored %s %s'
                                    % (before, after, setting, stored))])
    return fn


SAVE_Q = r'tap menu_save$'
save_row('5-merge', S5M, 2, ('A', prefs_mode('A')),
         ('at Save: "Merge changes?", the same text and buttons', lambda x, t: the_question(x, t, SAVE_Q, r'Yes')),
         ('a merge: KG3, NewKey, DZ, X1\'s deletion, P1, Q1, Q2, Q3 the desktop\'s, P2 the phone\'s', s5_merged))
save_row('5-overwrite', S5O, 1, ('A', prefs_mode('A')),
         ('the same dialog', lambda x, t: the_question(x, t, SAVE_Q, r'No')),
         ('SOLD with the phone\'s P2 and nothing of the desktop\'s', s5_overwritten))


def s5b_showed(x, tags):
    out = [the_question(x, tags, SAVE_Q, r'Yes')]
    for t in tags:
        lg = x.log(t)
        s = lg.step(SAVE_Q)
        found = lg.find(r"'Merge\.changes' is there", s)
        gap = (lg.lines[found][1:24], lg.lines[s][1:24])
        out.append(check(found > s and gap[0] > gap[1], 'the dialog found %s, Save pressed %s' % gap))
    nc = x.nc_log('s5B-save-bgsync-r1-attempt1')
    v = dict(re.match(r'^(\S+): (.*)$', y).groups() for y in read(os.path.join(x.root, 'logs', 'sverdicts-not-counted.txt')) if y)
    out.append(check(nc.merge_dialogs() and 'P2=p2-old-phone' in v['s5B-save-bgsync-r1-attempt1/cache'] and
                     '=SNEW-fixed' in v['s5B-save-bgsync-r1-attempt1/server'],
                     'the first attempt: the dialog, the cache the phone\'s P2, the server SNEW-fixed'))
    return all_of(*out)


save_row('5-B', S5P, 1, ('B', prefs_mode('B')), ('the same dialog, raised a few seconds after the save', s5b_showed),
         ('a merge, as above', s5_merged))
save_row('6', S6, 1, ('A, and then what showing the screen stores (B); Always merge on conflict false, pressed, true; stored true',
                      screen_then('AlwaysMergeOnConflict', 'false', 'true', 'true')),
         ('no dialog at Save', lambda x, t: no_question(x, t)),
         ('a merge, the same as the "Yes, merge" rows', s5_merged))
save_row('7', S7, 1, ('A, then B by the screen; Check for modifications true, pressed, false; stored false',
                      screen_then('CheckForFileChangesOnSave', 'true', 'false', 'false')),
         ('the "Merge changes?" dialog all the same', lambda x, t: the_question(x, t, SAVE_Q, r'No')),
         ('by the answer: SOLD with the phone\'s P2, the desktop\'s items gone', s5_overwritten))


# ------------------------------------------------------------------ 4 opening again

@claim('4-8-runs', '4, case 8, counted runs', '2')
def _(x):
    return counted(x, S8)


@claim('4-8-sequence', '4, case 8, sequence', 'open; lock; Close database; SNEW-fixed put; started again, unlocked; '
       'look; Synchronize database...; look')
def _(x):
    return in_order(r'tap menu_lock$', r'tap QuickUnlock_buttonLock$', r'host sh \S+/srv-put\.sh \S+ \S+/SNEW-fixed\.kdbx',
                    r'include k2a-reopen\.flow', r'tap \^Synchronize\.database')(x, S8)


@claim('4-8-showed', '4, case 8, what the program showed', 'no dialog at the unlock or at Synchronize')
def _(x):
    return no_question(x, S8)


@claim('4-8-loaded', '4, case 8, what it loaded', "the server's file: KeePassGate listed KPG revocation; the cache right "
       "after the unlock SNEW-fixed byte for byte, its version and base version the hash of SNEW-fixed")
def _(x):
    out = []
    for t in S8:
        lg = x.log(t)
        r = lg.step(r'include k2a-reopen\.flow')
        i = lg.step(r'tap \^KeePassGate\$', r)
        j = lg.step(r'key BACK', i)
        pull = lg.find(r'-> cache-after-reopen\.kdbx ', r)
        vers = [y.rsplit(': ', 1)[1].lower() for y in lg.lines[pull:pull + 3] if 'cache file' in y]
        out.append(check('KPG revocation' in lg.listed(i, j) and
                         x.dump(t, 'cache-after-reopen').sha == x.base('SNEW-fixed').sha and
                         vers == [x.base('SNEW-fixed').sha] * 2, '', '%s: %s %s' % (t, lg.listed(i, j)[:6], vers)))
    return all_of(*out + [check(True, 'KPG revocation listed; the cache after the unlock SNEW-fixed, both versions its hash')])


@claim('4-8-server', '4, case 8, server afterwards', 'SNEW-fixed, unchanged')
def _(x):
    return server_is(x, S8, 'SNEW-fixed')


@claim('4-9-runs', '4, case 9, counted runs', '1')
def _(x):
    return counted(x, S9)


@claim('4-9-sequence', '4, case 9, sequence', 'open; the lock button; the QuickUnlock screen ("Enter last 3 characters '
       'of your password:"); SNEW-fixed put; the last three characters, QuickUnlock!; look; P2 edited and saved')
def _(x):
    out = [in_order(r'tap menu_lock$', r'host sh \S+/srv-put\.sh \S+ \S+/SNEW-fixed\.kdbx',
                    r'typepw QuickUnlock_password ile$', r'tap QuickUnlock_button', r'tap menu_save$')(x, S9)]
    lg = x.log(S9[0])
    field = lg.step(r'waitfor QuickUnlock_password ') >= 0 and lg.find(r"tapped \(\d+, \d+\) 'QuickUnlock!'") >= 0
    out.append(check(any("text='Enter last 3 characters of your password:'" in y for y in lg.lines), 'the QuickUnlock screen',
                     'the label is not in this run\'s log, which shows the screen by its field QuickUnlock_password and '
                     'its button QuickUnlock! (%s) and lists nothing of it' % ('both there' if field else 'not even those')))
    return all_of(*out)


@claim('4-9-showed', '4, case 9, what the program showed', 'the home screen after QuickUnlock, the launcher; the copy in '
       'memory: KeePassGate without KPG revocation, X1 still listed; at Save "Merge changes?", Yes, merge')
def _(x):
    t = S9[0]
    lg = x.log(t)
    q = lg.step(r'tap QuickUnlock_button')
    back = lg.step(r'sh monkey ', q)
    i = lg.step(r'tap \^KeePassGate\$', q)
    j = lg.step(r'key BACK', i)
    k = lg.step(r'tap \^Passwords\$', q)
    return all_of(check(q < lg.find(r'id=launcher', q) < back, 'the launcher, then the program brought back'),
                  check('KPG revocation' not in lg.listed(i, j) and 'KPG grant 1' in lg.listed(i, j),
                        'KeePassGate without KPG revocation'),
                  check('X1' in lg.listed(k, lg.step(r'key BACK', k)), 'X1 listed'),
                  the_question(x, S9, SAVE_Q, r'Yes'))


@claim('4-9-loaded', '4, case 9, what it loaded', 'the copy in memory, not the server\'s file; nothing read at the unlock')
def _(x):
    t = S9[0]
    lg = x.log(t)
    i = lg.step(r'tap \^KeePassGate\$', lg.step(r'tap QuickUnlock_button'))
    return check('KPG revocation' not in lg.listed(i, lg.step(r'key BACK', i)), 'the KeePassGate group SOLD\'s')


@claim('4-9-server', '4, case 9, server afterwards', 'a merge, as in case 5')
def _(x):
    return s5_merged(x, S9)


# ------------------------------------------------------------------ 5 item by item

COLUMNS = [
    ('4-sync-merge', S4M + S4B + S4P, 'history', 'yes', 'history', 'yes'),
    ('4-sync-overwrite', S4O, 'no', 'no', 'no', 'yes'),
    ('5-save-merge', S5M + S5P, 'yes', 'yes', 'yes', 'not made'),
    ('5-save-overwrite', S5O, 'no', 'no', 'no', 'not made'),
    ('6-always-merge', S6, 'yes', 'yes', 'yes', 'not made'),
    ('7-check-off', S7, 'no', 'no', 'no', 'not made'),
    ('8-reopened', S8, 'yes', 'yes', 'yes', 'not made'),
    ('9-quickunlock', S9, 'yes', 'yes', 'yes', 'not made'),
]


def desk_item(d, name):
    """yes, history or no for one of the desktop's items in a dump."""
    if name in ('KG3', 'NewKey', 'DZ', 'X1'):
        return 'yes' if item(d, name) else 'no'
    cur, hist = d.pw(name)
    if cur == DESK[name]:
        return 'yes'
    return 'history' if DESK[name] in hist else 'no'


def table5(col, tags, q1, q2, q3, np_):
    want = {'KG3': 'yes', 'NewKey': 'yes', 'DZ': 'yes', 'X1': 'yes', 'P1': 'yes', 'Q1': q1, 'Q2': q2, 'Q3': q3}
    if col in ('4-sync-overwrite', '5-save-overwrite', '7-check-off'):
        want.update({'KG3': 'no', 'NewKey': 'no', 'DZ': 'no', 'X1': 'no', 'P1': 'no'})
    for name in ('KG3', 'NewKey', 'DZ', 'X1', 'P1', 'Q1', 'Q2', 'Q3'):
        def mk(name):
            @claim('5-%s-%s' % (name, col), '5, %s, %s' % (name, col), want[name])
            def _(x):
                got = [(t, desk_item(x.dump(t, 'server'), name)) for t in tags]
                extra = []
                if col == '4-sync-merge' and name == 'NewKey':
                    extra = [check(all(x.dump(t, 'server').get(r'^M CD KPG\.NewKey ')[0].endswith('@none') for t in tags),
                                   'its per-key time dropped')]
                if col == '4-sync-overwrite' and name == 'X1':
                    extra = [check(all(x.dump(t, 'server').has(r'^E X1 parent ') and not x.dump(t, 'server').has(r'^D X1 ')
                                       for t in tags), 'X1 back, no record')]
                return all_of(check(all(g == want[name] for _, g in got), '%s in %d runs' % (want[name], len(tags)),
                                    'got %s' % got), *extra)
        mk(name)

    @claim('5-P2-%s' % col, '5, P2, %s' % col, '(not changed)' if col == '8-reopened' else 'yes')
    def _(x):
        want_p2 = 'p2-old' if col == '8-reopened' else 'p2-old-phone'
        got = [(t, x.dump(t, 'server').pw('P2')[0]) for t in tags]
        extra = [check(all(x.log(t).step(r'appendpw entry_password -phone$') < 0 for t in tags), 'no edit by the phone')] \
            if col == '8-reopened' else []
        return all_of(check(all(g == want_p2 for _, g in got), 'P2 %s' % want_p2, 'got %s' % got), *extra)

    @claim('5-phone-new-%s' % col, '5, phone-new, %s' % col, np_ if np_ == 'yes' else '(not made)')
    def _(x):
        made = [x.log(t).step(r'text phone-new$') >= 0 for t in tags]
        there = [item(x.dump(t, 'server'), 'NP') for t in tags]
        if np_ == 'yes':
            return check(all(made) and all(there), 'made and there in %d runs' % len(tags))
        return check(not any(made) and not any(there), 'not made in %d runs' % len(tags))


for _c in COLUMNS:
    table5(*_c)


@claim('5-columns', '5, the column heads', 'the run counts: 4 (4 x2, 4b, 4 B), 1, 2 and 1 in B, 1, 1, 1, 2, 1')
def _(x):
    return check([len(c[1]) for c in COLUMNS] == [4, 1, 3, 1, 1, 1, 2, 1], '4, 1, 3, 1, 1, 1, 2, 1')


@claim('5-KG1-KG2', '5, after the table', 'KG1 and KG2 were in every file of every run')
def _(x):
    out = []
    for top, _, files in os.walk(os.path.join(x.root, 'runs')):
        for f in files:
            if f.endswith('.kdbx.dump.txt'):
                d = Dump(os.path.join(top, f))
                out.append(check(d.has(r'^E KG1 parent KG ') and d.has(r'^E KG2 parent KG '), '', os.path.join(top, f)))
    return all_of(*out + [check(True, '%d read-backs' % len(out))])


@claim('2-verdicts', '1.2, the read-back', 'sverdicts.txt is what the dumps hold: every line\'s KG3, NewKey, DZ, X1, '
       'passwords and NP agree with the dump it names')
def _(x):
    out = []
    for (run, name), line in x.verdicts.items():
        d = x.dump(run, name)
        got = 'KG3:%s NewKey:%s DZ:%s X1:%s' % ('yes' if item(d, 'KG3') else 'NO', 'yes' if item(d, 'NewKey') else 'NO',
                                                 'yes' if item(d, 'DZ') else 'NO',
                                                 ('there' if d.has(r'^E X1 parent ') else 'gone') + '/' +
                                                 ('rec' if d.has(r'^D X1 ') else 'norec'))
        out.append(check(got in line and ('NP:yes' in line) == item(d, 'NP') and
                         ('P2=%s' % d.pw('P2')[0]) in line, '', '%s/%s: %s against %s' % (run, name, got, line[:80])))
    return all_of(*out + [check(True, '%d lines' % len(out))])


# ---------------------------------------------- 7 what a phone that synchronizes keeps and loses

@claim('7-loss', '7, the one sequence that loses', 'with "No, overwrite": 3 runs of 3, at a save, at "Synchronize cache '
       'copy with source", and with "Check for modifications" off; the phone\'s own changes never lost')
def _(x):
    lost = [t for t in SECTION2 + SECTION3 + SECTION4 if not item(x.dump(t, 'server'), 'KG3') and
            x.log(t).step(r'host sh \S+/(srv-put|mksnew)\.sh ') >= 0]
    phone_kept = all(x.dump(t, 'server').pw('P2')[0] == 'p2-old-phone' for t in SECTION2 + SECTION3 + SECTION4
                     if x.log(t).step(r'appendpw entry_password -phone$') >= 0 and
                     x.log(t).step(r'tap \^P2\$') >= 0)
    return all_of(check(sorted(lost) == sorted(S4O + S5O + S7), '3 runs lost the desktop\'s items: %s' % ', '.join(lost),
                        'lost: %s' % lost), check(phone_kept, 'P2 the phone\'s wherever the phone changed it'))


@claim('7-kdbx-4.0', '7, not a loss', 'every Keepass2Android write drops the database custom data\'s per-key times and '
       'writes KDBX 4.0')
def _(x):
    out = []
    for t in SECTION2 + SECTION3 + SECTION4:
        for name in ('server', 'cache'):
            d = x.dump(t, name)
            if d.sha in (x.base('SOLD').sha, x.base('SNEW-fixed').sha) or d.sha == x.snew(t).sha:
                continue
            out.append(check(d.has(r'^F version 4\.0$') and all(y.endswith('@none') for y in d.get(r'^M CD ')), '',
                             '%s/%s: %s' % (t, name, d.get(r'^F version '))))
    return all_of(*out + [check(True, '%d files Keepass2Android wrote, all KDBX 4.0 without per-key times' % len(out))])


@claim('7-cache-older', '7, not a loss', 'after a synchronize that only loaded the server\'s changes, the cache kept the '
       'older file, 2 of 2')
def _(x):
    return all_of(*[check(x.dump(t, 'cache').sha == x.base('SOLD').sha and x.dump(t, 'server').sha == x.base('SNEW-fixed').sha,
                          '', t) for t in S2] + [check(True, 'case 2: the cache SOLD, the server SNEW-fixed, 2 of 2')])


def main(argv):
    root = RECORDED
    if '--root' in argv:
        root = os.path.abspath(argv[argv.index('--root') + 1])
    x = Ctx(root)
    bad = []
    for cid, where, text, fn in CLAIMS:
        try:
            ok, note = fn(x)
        except (OSError, IndexError, ValueError, AttributeError, TypeError, KeyError) as e:
            ok, note = False, 'cannot check: %s: %s' % (type(e).__name__, e)
        if not ok:
            bad.append(cid)
        print('%-15s %-28s %s' % ('holds' if ok else 'DOES NOT HOLD', cid, text[:150]))
        if not ok or '-v' in argv:
            print('%15s %s' % ('', note[:400]))
    unexpected = set(bad) - KNOWN
    missing = KNOWN - set(bad)
    print('== %d claims, %d hold, %d do not: %s' % (len(CLAIMS), len(CLAIMS) - len(bad), len(bad), ', '.join(bad) or 'none'))
    if unexpected or missing:
        print('== UNEXPECTED: %s; listed in KNOWN but held: %s' % (sorted(unexpected), sorted(missing)))
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
