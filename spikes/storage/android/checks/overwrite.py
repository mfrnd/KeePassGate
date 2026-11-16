"""Holds every table of `papers\\ground\\measured-overwrite.md` to its record: written on the branch.

The emulator runs cannot be repeated here, so what is checked is what they left under
`recorded\\overwrite\\`: the dumps of the four given files, the read-back of every file a phone left,
its diffs, the verdict lines and the step logs. Each claim below is one cell of a table, or one
number of the prose around it, named by its section and case; the check states it over the lines
that decide it, prints `holds` or `DOES NOT HOLD` with those lines, and exits 0 when exactly the
claims listed in `KNOWN` do not hold.

Three readings are this checker's, not the paper's, and are applied everywhere:

- "the phone's entry" is the entry with an identifier no base fixes, printed `~...`, and the lines
  that count it (`F groups ... entries`, `G Root children`) are set aside with it;
- "the per-key time dropped" is `M CD KPG.Profiles` read back `@none` where the base has a time;
- a case's runs are the run directories whose names the batch files give that case.

    uv run --no-project python -I -X utf8 spikes\\storage\\android\\checks\\overwrite.py [-v] [--root <dir>]
"""
import os
import re
import sys
from collections import Counter
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
RECORDED = os.path.join(HERE, '..', 'recorded', 'overwrite')
FIRST_BASE = os.path.join(HERE, '..', 'recorded', 'base')
PINS = os.path.join(HERE, '..', 'scripts', 'android.sha256')

# Claims the record does not bear out, by identifier, each named in RUN.md.
KNOWN = {'1.1-installed', '2.2-3b-sequence'}

MERGE_TITLE = 'Merge changes?'
MERGE_TEXT = ('The database file was modified externally. Do you want to load and merge the changes before '
              'saving? Select No if you want to overwrite the external changes.')
PER_KEY = r'^M CD KPG\.Profiles '
COUNTS = r'^(F groups |G Root children )'
STAMP = '%Y-%m-%dT%H:%M:%S'


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

    @property
    def size(self):
        m = re.search(r'size=(\d+)', self.head)
        return int(m.group(1)) if m else None


def local_time(text):
    """A device time as the logs print it, `2026-11-15 22:08:52.988000000 +0100`, in UTC."""
    m = re.match(r'(\d{4}-\d\d-\d\d) (\d\d:\d\d:\d\d)\.(\d+) ([+-])(\d\d)(\d\d)', text)
    t = datetime.strptime(m.group(1) + 'T' + m.group(2), STAMP)
    frac = int((m.group(3) + '000000000')[:9])
    off = timedelta(hours=int(m.group(5)), minutes=int(m.group(6)))
    t = t - off if m.group(4) == '+' else t + off
    return t, frac


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
        """The index of a flow step whose command matches rx."""
        return self.find(r'^\[[^]]+\] \S+\.flow:\d+ ' + rx, start)

    def rc(self):
        got = [x for x in self.lines if x.startswith('### flow rc=')]
        return got[-1] if got else ''

    def failed(self):
        return [x for x in self.lines if 'FAILED' in x]

    def merge_dialogs(self):
        return self.all(r"dialog alertTitle: 'Merge changes\?'")

    def any_dialog_titles(self):
        return [x for x in self.lines if 'dialog alertTitle:' in x]

    def dialog_text(self, i):
        """Title, message and buttons logged with the dialog whose title is on line i."""
        out = {}
        for x in self.lines[i:i + 8]:
            m = re.search(r"dialog (alertTitle|message|button\d): '(.*)'$", x)
            if m:
                out[m.group(1)] = m.group(2)
        return out

    def replace(self):
        b = self.find(r'\| replace: before ')
        a = self.find(r'\| replace: after ')
        if b < 0 or a < 0:
            return None
        rb = re.search(r'before (\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+)', self.lines[b])
        ra = re.search(r'after  (\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+) \(mode (\w+), from (\S+)\.kdbx\)', self.lines[a])
        return {'at': a, 'before_size': int(rb.group(1)), 'before_mtime': rb.group(2), 'before_sha': rb.group(3),
                'size': int(ra.group(1)), 'mtime': ra.group(2), 'sha': ra.group(3), 'mode': ra.group(4),
                'from': ra.group(5)}

    def given(self):
        i = self.find(r'^given ')
        m = re.search(r'given (\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+)', self.lines[i]) if i >= 0 else None
        return None if not m else {'size': int(m.group(1)), 'mtime': m.group(2), 'sha': m.group(3)}

    def start(self):
        m = re.match(r'^### (\S+)Z ', self.lines[0])
        return datetime.strptime(m.group(1)[:19], STAMP)

    def pulled(self):
        got = [x for x in self.lines if x.startswith('device ')]
        if not got:
            return None
        m = re.search(r'sha256 (\w+) (\d+) bytes, mtime (\S+ \S+ \S+)', got[-1])
        return {'sha': m.group(1), 'size': int(m.group(2)), 'mtime': m.group(3)}

    def devstat(self, label):
        i = self.find(r'\| devstat ' + label + ':')
        if i < 0:
            return None
        m = re.search(r'(\d+) bytes, mtime (\S+ \S+ \S+) sha256 (\w+)', self.lines[i])
        return {'size': int(m.group(1)), 'mtime': m.group(2), 'sha': m.group(3)}

    def groups(self, start):
        """The group counts of the first `list` after line start: {'G': 9, 'KPG': 4, ...}."""
        i = self.step(r'list$', start)
        if i < 0:
            return {}
        out, name = {}, None
        for x in self.lines[i + 1:]:
            if x.startswith('['):
                break
            m = re.search(r"id=group_text\s+text='([^']*)'", x)
            if m:
                name = m.group(1)
            m = re.search(r"id=group_label\s+text='Group - (\d+) entries'", x)
            if m and name:
                out[name] = int(m.group(1))
                name = None
        return out

    def kpg_entries(self):
        """What the KPG group listed, after `entries listed in KPG follow`."""
        i = self.find(r'entries listed in KPG follow')
        if i < 0:
            return None
        j = self.step(r'list$', i)
        out = []
        for x in self.lines[j + 1:]:
            if x.startswith('['):
                break
            m = re.search(r"id=entry_text\s+text='([^']*)'", x)
            if m:
                out.append(m.group(1))
        return out

    def prefs(self, label):
        i = self.find(r'\| prefs ' + label + ':')
        if i < 0:
            return None
        return dict(re.findall(r'name="(\w+)" value="(\w+)"', self.lines[i]))

    def check_states(self):
        return [x.rsplit('checked=', 1)[1] for x in self.lines if ' checked=' in x and "'" in x]


def all_of(*checks):
    notes = []
    for ok, note in checks:
        notes.append(note)
        if not ok:
            return False, note
    return True, '; '.join(n for n in notes if n)[:300]


def check(cond, note_ok, note_bad=None):
    return (True, note_ok) if cond else (False, note_bad or ('not: ' + note_ok))


def norm(line):
    return re.sub(r' parent (\S+) \(.*\)$', r' parent \1', line)


def only_changes(b, r, allowed):
    """Lines that differ between b and r, the phone's entry set aside, all match allowed."""
    skip = re.compile(r'^(E ~|' + COUNTS[2:-1] + ')')
    a = Counter(norm(x) for x in b.lines if not skip.search(x))
    c = Counter(norm(x) for x in r.lines if not skip.search(x))
    gone, new = list((a - c).elements()), list((c - a).elements())
    ok_rx = re.compile(allowed) if allowed else None
    bad = [x for x in gone + new if not (ok_rx and ok_rx.search(x))]
    return check(not bad, '%d lines differ, all of them %s' % (len(gone) + len(new), allowed or 'none'),
                 'also differs: ' + ' | '.join(x[:100] for x in bad[:4]))


def phone_entry(r, title):
    got = r.get(r'^E ~\S+ S Title p=. "' + re.escape(title) + '"$')
    return check(len(got) == 1, 'the phone\'s entry "%s" is there' % title, 'no entry "%s"' % title)


def no_phone_entry(r):
    return check(not r.has(r'^E ~'), 'no entry the bases do not hold', 'an entry the bases do not hold')


def per_key_dropped(b, r):
    x, y = b.get(PER_KEY), r.get(PER_KEY)
    return check(x and y and not x[0].endswith('@none') and y[0].endswith('@none'),
                 'the per-key time dropped: %s' % (y[0][-30:] if y else ''), 'per-key line: %s' % (y[:1],))


class Ctx:
    def __init__(self, root):
        self.root = root
        self.cache = {}
        self.verdicts = self.read_verdicts('verdicts.txt')
        self.verdicts_nc = self.read_verdicts('verdicts-not-counted.txt')

    def read_verdicts(self, name):
        out = {}
        for x in read(os.path.join(self.root, 'logs', name)):
            m = re.match(r"^(\S+)/(\S+)\.kdbx: NEW's changes (\w+) \| (.*) \| phone: (\S+) \| v(\S+) (\w+)( =(\S+))?$", x)
            if m:
                out[m.group(1)] = {'name': m.group(2), 'new': m.group(3), 'items': m.group(4), 'phone': m.group(5),
                                   'version': m.group(6), 'sha12': m.group(7), 'same': m.group(9), 'line': x}
        return out

    def dump(self, *parts):
        p = os.path.join(self.root, *parts)
        if p not in self.cache:
            self.cache[p] = Dump(p)
        return self.cache[p]

    def base(self, name):
        return self.dump('base', name + '.kdbx.dump.txt')

    def run(self, prog, tag):
        return self.dump('runs', prog, tag, '%s-%s.kdbx.dump.txt' % (prog, tag))

    def log(self, prog, tag):
        p = os.path.join(self.root, 'logs', 'runs', prog, tag + '.log')
        if p not in self.cache:
            self.cache[p] = Log(p)
        return self.cache[p]

    def nc_log(self, name):
        return Log(os.path.join(self.root, 'logs', 'not-counted', name + '.log'))

    def verdict(self, prog, tag):
        v = self.verdicts.get(tag)
        return v if v and v['name'] == '%s-%s' % (prog, tag) else None


CLAIMS = []


def claim(cid, where, text):
    def wrap(fn):
        CLAIMS.append((cid, where, text, fn))
        return fn
    return wrap


def runs(base, n):
    return [base + '-r%d' % i for i in range(1, n + 1)]


# ------------------------------------------------------------------ the cases and their runs

# case, arriving copy, time, on screen or in the background, answer, runs (section 2.1)
K2A_21 = [
    ('1a', 'NEW-diff', 'later', 'fg', 'overwrite', runs('fg-later-diff-overwrite', 3)),
    ('1b', 'NEW-diff', 'later', 'fg', 'merge', runs('fg-later-diff-merge', 3)),
    ('1c', 'NEW-diff', 'later', 'fg', 'cancel', runs('fg-later-diff-cancel', 1)),
    ('2a', 'NEW-same', 'equal', 'fg', 'overwrite', runs('fg-equal-same-overwrite', 3)),
    ('2b', 'NEW-same', 'earlier', 'fg', 'overwrite', runs('fg-earlier-same-overwrite', 1)),
    ('2c', 'NEW-diff', 'earlier', 'fg', 'overwrite', runs('fg-earlier-diff-overwrite', 1)),
    ('2d', 'NEW-diff', 'equal', 'fg', 'overwrite', runs('fg-equal-diff-overwrite', 1)),
    ('2e', 'NEW-same', 'later', 'fg', 'overwrite', runs('fg-later-same-overwrite', 1)),
    ('2f', 'NEW-ident', 'later', 'fg', 'none', runs('fg-later-ident', 3)),
    ('2g', 'NEW-diff', 'later', 'bg', 'overwrite', runs('bg-later-diff-overwrite', 3)),
    ('2h', 'NEW-same', 'equal', 'bg', 'overwrite', runs('bg-equal-same-overwrite', 1)),
]
K2A_OTHER = {
    '3b': runs('openclose-later-diff', 1),
    '3c': runs('close-later-diff', 3),
    '5': runs('look-later-diff', 2),
    '4': runs('sync-later-diff', 1),
    '6a': ['nocheck-later-diff-overwrite-r1'],
    '6b': ['nocheck-reopen-later-diff-r1'],
    '3d': runs('qu-later-diff-overwrite', 2),
}
KPD = {'1': runs('fg-later-diff', 3), '3b': runs('lock-later-diff', 1)}
OVERWRITE_ANSWERED = ([t for c in K2A_21 if c[4] == 'overwrite' for t in c[5]] + K2A_OTHER['6a'] +
                      K2A_OTHER['6b'] + K2A_OTHER['3d'])
NOT_COUNTED = {
    'cancel': ['k2a-fg-later-diff-cancel-r1-attempt1'],
    'missed': ['k2a-nocheck-later-diff-r1-attempt1', 'k2a-nocheck-later-diff-r2-attempt1'],
    'off': ['k2a-nocheck-later-diff-r1-attempt2', 'k2a-nocheck-later-diff-r2-attempt2'],
    'qu': ['k2a-qu-later-diff-overwrite-r1-attempt1', 'k2a-qu-later-diff-overwrite-r2-attempt1'],
}


def counted(x, prog, tags):
    out = []
    for t in tags:
        lg = x.log(prog, t)
        out.append(check(lg.rc().startswith('### flow rc=0') and not lg.failed() and x.verdict(prog, t) is not None,
                         '%s: rc=0, no failure, read back' % t, '%s: %s, %d failures' % (t, lg.rc(), len(lg.failed()))))
    out.append(check(True, '%d runs' % len(tags)))
    return all_of(*out)


def replaced(x, prog, tag, arriving, mode, where):
    lg = x.log(prog, tag)
    rp = lg.replace()
    if rp is None:
        return False, '%s: no replacement logged' % tag
    tb, _ = local_time(rp['before_mtime'])
    ta, _ = local_time(rp['mtime'])
    want = x.base(arriving)
    out = [check(rp['from'] == arriving and rp['sha'] == want.sha and rp['size'] == want.size,
                 '%s: %s arrived, %d bytes' % (tag, rp['from'], rp['size']), '%s: from %s' % (tag, rp['from'])),
           check(rp['mode'] == mode, 'mode ' + mode)]
    if mode == 'later':
        out.append(check((ta - tb).total_seconds() >= 600, 'the time moved on %d s' % (ta - tb).total_seconds(),
                         '%s: moved %s s only' % (tag, (ta - tb).total_seconds())))
    elif mode == 'equal':
        out.append(check(rp['mtime'] == rp['before_mtime'], 'the time to the nanosecond: ' + rp['mtime'],
                         '%s: %s against %s' % (tag, rp['mtime'], rp['before_mtime'])))
    else:
        out.append(check((tb - ta).total_seconds() == 3600, 'an hour earlier: ' + rp['mtime'],
                         '%s: %s against %s' % (tag, rp['mtime'], rp['before_mtime'])))
    home = lg.step(r'key HOME$')
    if where == 'bg':
        back = lg.step(r'sh monkey ', rp['at'])
        out.append(check(0 <= home < rp['at'] < back, 'HOME before it, the launcher after it',
                         '%s: HOME %d, replacement %d, launcher %d' % (tag, home, rp['at'], back)))
    else:
        out.append(check(home < 0, 'the program in front', '%s: HOME pressed' % tag))
    return all_of(*out)


def dialog_at_save(lg, tag):
    save = lg.step(r'tap menu_save$')
    md = lg.merge_dialogs()
    return check(save >= 0 and md and md[0] > save, '%s: the dialog after Save' % tag,
                 '%s: Save at %d, dialogs at %s' % (tag, save, md[:3]))


def no_merge_dialog(lg, tag):
    return check(not lg.merge_dialogs(), '%s: no "Merge changes?"' % tag, '%s: the dialog came' % tag)


def file_is(x, prog, tag, base, answer, entry, extra_allowed=''):
    r = x.run(prog, tag)
    v = x.verdict(prog, tag)
    if answer == 'cancel':
        return all_of(check(v['same'] == 'NEW-diff' and r.sha == x.base('NEW-diff').sha,
                            '%s: NEW-diff byte for byte' % tag, '%s: %s' % (tag, v['line'][-40:])),
                      check(v['phone'] == 'none', 'phone: none'), no_phone_entry(r))
    allowed = PER_KEY + extra_allowed
    out = [phone_entry(r, entry), only_changes(x.base(base), r, allowed)]
    if prog == 'k2a':
        out.append(per_key_dropped(x.base(base), r))
    return all_of(*out)


def verdict_is(x, prog, tag, want):
    v = x.verdict(prog, tag)
    return check(v is not None and v['new'] == want, '%s: NEW\'s changes %s (%s)' % (tag, want, v['items'] if v else ''),
                 '%s: %s' % (tag, v['line'] if v else 'no verdict'))


def entry_of(tag):
    return 'phone-' + tag


# ------------------------------------------------------------------ 1.1 and 1.4

@claim('1.1-apks', '1.1', 'verify-apks.sh passed for all three: the hash, a verifying signature, the one pinned signer')
def _(x):
    v = read(os.path.join(x.root, 'logs', 'verify.txt'))
    a = read(os.path.join(x.root, 'logs', 'apksigner.txt'))
    pins = {}
    for line in read(PINS):
        f = line.split()
        if line and not line.startswith('#') and len(f) >= 4:
            pins[f[1]] = f[0]
    out = [check(v.count('SIGNER MATCH') == 3 and v.count('Verifies') == 3 and v.count('Number of signers: 1') == 3,
                 'verify.txt: 3 Verifies, 3 one signer, 3 SIGNER MATCH')]
    for name, short in (('k2a-1.15-r6', '5c446a4f'), ('kpd-2.7', '513b2555'), ('kpdx-4.5.8', '8aaac6a0')):
        f = [k for k in pins if k.startswith(name + '/')][0]
        i = a.index('=== dl/' + f)
        out.append(check(a[i + 1] == pins[f] and pins[f].startswith(short), '%s %s...' % (name, a[i + 1][:8]),
                         '%s: %s against %s' % (name, a[i + 1][:16], pins[f][:16])))
    return all_of(*out)


@claim('1.1-installed', '1.1', 'the APK each installed package runs from hashed to the pinned value')
def _(x):
    hits = []
    for top, _, files in os.walk(x.root):
        for f in files:
            for line in read(os.path.join(top, f)):
                if re.search(r'installed \S+ sha256 ', line):
                    hits.append(line)
    return check(bool(hits), '%d lines' % len(hits),
                 'no line of the record holds an installed file\'s hash: check.sh printed them to its console')


@claim('1.1-timezone', '1.1', 'the logs print file times at +0100; every run cleared the program\'s data first')
def _(x):
    out = []
    for prog, tags in (('k2a', [t for c in K2A_21 for t in c[5]] + [t for v in K2A_OTHER.values() for t in v]),
                       ('kpd', [t for v in KPD.values() for t in v])):
        for t in tags:
            lg = x.log(prog, t)
            rp = lg.replace()
            out.append(check(rp and rp['mtime'].endswith('+0100') and lg.lines[1] == 'Success',
                             '', '%s: %s, %s' % (t, rp and rp['mtime'], lg.lines[1])))
    out.append(check(True, '%d runs: pm clear answered Success, every time +0100' % (len(out))))
    return all_of(*out)


@claim('1.3-given', '1.3', 'the opened file given the device clock less 600 s; each replacement and the pull logged')
def _(x):
    out = []
    for prog, tags in (('k2a', [t for c in K2A_21 for t in c[5]] + [t for v in K2A_OTHER.values() for t in v]),
                       ('kpd', [t for v in KPD.values() for t in v])):
        for t in tags:
            lg = x.log(prog, t)
            g, rp, p = lg.given(), lg.replace(), lg.pulled()
            gt, _ = local_time(g['mtime'])
            lag = (lg.start() - gt).total_seconds()
            out.append(check(590 <= lag <= 610 and rp and p and rp['before_sha'] == g['sha'],
                             '', '%s: given %s, run started %s' % (t, g['mtime'], lg.start())))
    out.append(check(True, '%d runs: given 600 s before the run began, before, after and pull logged' % len(out)))
    return all_of(*out)


@claim('1.4-counts', '1.4', 'counted: Keepass2Android 32 runs (21, 7, 2 and 2), KeePassDroid 4 runs')
def _(x):
    k21 = sum(len(c[5]) for c in K2A_21)
    k22 = sum(len(K2A_OTHER[k]) for k in ('3b', '3c', '5', '4'))
    k25 = len(K2A_OTHER['6a']) + len(K2A_OTHER['6b'])
    k26 = len(K2A_OTHER['3d'])
    k = sorted(t for t in x.verdicts if x.verdicts[t]['name'].startswith('k2a-'))
    d = sorted(t for t in x.verdicts if x.verdicts[t]['name'].startswith('kpd-'))
    logs_k = len(os.listdir(os.path.join(x.root, 'logs', 'runs', 'k2a')))
    logs_d = len(os.listdir(os.path.join(x.root, 'logs', 'runs', 'kpd')))
    named = set(t for c in K2A_21 for t in c[5]) | set(t for v in K2A_OTHER.values() for t in v)
    return all_of(check((k21, k22, k25, k26) == (21, 7, 2, 2), 'the cases name 21, 7, 2 and 2 runs',
                        'the cases name %d, %d, %d, %d' % (k21, k22, k25, k26)),
                  check(len(k) == 32 and set(k) == named and logs_k == 32, '32 Keepass2Android read-backs and logs',
                        '%d read-backs, %d logs' % (len(k), logs_k)),
                  check(len(d) == 4 and logs_d == 4, '4 KeePassDroid read-backs and logs'))


@claim('1.4-not-counted', '1.4', 'seven attempts not counted, each repeated, each attempt leaving NEW-diff byte for '
       'byte: the first Cancel run, two presses that landed on no line, two that turned the setting off and '
       'stopped at the dialog, two QuickUnlock runs stopped by the home screen')
def _(x):
    out = [check(len(x.verdicts_nc) == 7 and all(v['same'] == 'NEW-diff' for v in x.verdicts_nc.values()),
                 '7 read-backs, all =NEW-diff')]
    lg = x.nc_log(NOT_COUNTED['cancel'][0])
    out.append(check(lg.step(r'tap \^Cancel\$') >= 0 and any('waitfor: timed out on fabAddNew' in f for f in lg.failed()),
                     'the Cancel attempt waited for the group view'))
    for n in NOT_COUNTED['missed']:
        lg = x.nc_log(n)
        out.append(check(lg.check_states() == ['true', 'true'] and lg.merge_dialogs(),
                         '%s: checked stayed true, the dialog came' % n, '%s: %s' % (n, lg.check_states())))
    for n in NOT_COUNTED['off']:
        lg = x.nc_log(n)
        out.append(check(lg.check_states() == ['true', 'false'] and lg.merge_dialogs() and
                         any('this-case-expected-no-question' in f for f in lg.failed()),
                         '%s: turned off, stopped at the dialog' % n, '%s: %s' % (n, lg.check_states())))
    for n in NOT_COUNTED['qu']:
        lg = x.nc_log(n)
        q = lg.step(r'tap QuickUnlock_button')
        out.append(check(q >= 0 and lg.find(r'id=launcher', q) > q and
                         any('timed out on fabAddNew' in f for f in lg.failed()),
                         '%s: the home screen after QuickUnlock' % n, '%s: no launcher seen' % n))
    return all_of(*out)


# ------------------------------------------------------------------ 1.2 the databases

@claim('1.2-OLD', '1.2, OLD', "the main base S-argon (Argon2d, KDBX 4.1), X1's notes 1500 random characters; 9301 bytes")
def _(x):
    b, s = x.base('OLD'), Dump(os.path.join(FIRST_BASE, 'S-argon.kdbx.dump.txt'))
    diff = Counter(s.lines) - Counter(b.lines), Counter(b.lines) - Counter(s.lines)
    return all_of(check(b.size == 9301, '9301 bytes', '%s bytes' % b.size),
                  check(b.has(r'^F kdf Argon2d ') and b.has(r'^F version 4\.1$'), 'Argon2d, KDBX 4.1'),
                  check(list(diff[0]) == ['E X1 S Notes p=0 ""'] and len(diff[1]) == 1 and
                        re.match(r'^E X1 S Notes p=0 len=1500 ', list(diff[1])[0]),
                        "S-argon's lines but X1's notes, 1500 characters", 'differs from S-argon: %s' % (diff,)))


def new_changes(b, n, notes):
    return all_of(
        only_changes(b, n, r'^(E (RV|M1|X1) |D (X1 |count )|G (G|GKPG) children )'),
        check(n.has(r'^E RV parent GKPG \(KPG\)$') and n.has(r'^E RV S Title p=0 "KPG revocation"$') and
              n.has(r'^E RV S KPG\.Revoked\.c2 ') and n.has(r'^E RV t\.created 2026-11-01T18:00:00Z$'),
              'RV "KPG revocation" in KPG with KPG.Revoked.c2, at 18:00Z'),
        check(n.has(r'^E RV S Notes p=0 len=%d ' % notes), "RV's notes %d characters" % notes),
        check(n.has(r'^E M1 S KPG\.base p=0 "\{\\"v\\":1,\\"by\\":\\"KeePass\\"\}"$') and
              n.has(r'^E M1 H\S+ S KPG\.base p=0 "\{\\"v\\":0\}"$') and n.has(r'^E M1 history\.count 1$') and
              n.has(r'^E M1 t\.modified 2026-11-01T18:00:00Z$'),
              'M1 v0 -> v1 at 18:00Z, the old version in history'),
        check(not n.has(r'^E X1 ') and n.has(r'^D X1 2026-11-01T18:00:00Z$'), 'X1 deleted with its record'))


@claim('1.2-NEW-diff', '1.2, NEW-diff', "OLD with RV, M1's v1 and X1 deleted, all at 18:00Z; RV's notes 4000 characters; 11621 bytes")
def _(x):
    n = x.base('NEW-diff')
    return all_of(check(n.size == 11621, '11621 bytes', '%s bytes' % n.size), new_changes(x.base('OLD'), n, 4000))


@claim('1.2-NEW-same', '1.2, NEW-same', "the same changes, RV's notes 1309 characters, exactly OLD's length; one line from NEW-diff's")
def _(x):
    s, d = x.base('NEW-same'), x.base('NEW-diff')
    diff = Counter(s.lines) - Counter(d.lines)
    return all_of(check(s.size == x.base('OLD').size == 9301, '9301 bytes, OLD\'s length'),
                  new_changes(x.base('OLD'), s, 1309),
                  check(len(diff) == 1 and list(diff)[0].startswith('E RV S Notes '), 'one line from NEW-diff\'s'))


@claim('1.2-NEW-ident', '1.2, NEW-ident', "OLD's bytes exactly; 9301 bytes")
def _(x):
    i, o = x.base('NEW-ident'), x.base('OLD')
    return check(i.sha == o.sha and i.size == 9301 and i.lines == o.lines, "OLD's SHA-256, 9301 bytes")


# ---------------------------------------------------------- 2.1 Keepass2Android, the overwrite

def row21(case, arriving, mode, where, answer, tags):
    label = '2.1-' + case

    @claim(label + '-replacement', '2.1, case %s, replacement' % case,
           '%s, %s, %s' % (arriving, mode, 'in the background' if where == 'bg' else 'on screen'))
    def _(x):
        return all_of(*[replaced(x, 'k2a', t, arriving, mode, where) for t in tags])

    @claim(label + '-runs', '2.1, case %s, counted runs' % case, '%d' % len(tags))
    def _(x):
        return counted(x, 'k2a', tags)

    if answer == 'none':
        showed = 'no dialog; the save went straight through'
    elif where == 'bg':
        showed = "nothing on return (OLD's counts, KPG 4 entries, G 9); the dialog at Save"
    elif answer == 'cancel':
        showed = 'the dialog; the editor stayed open with the entry; its own cancel wrote nothing'
    else:
        showed = 'the dialog at Save'

    @claim(label + '-showed', '2.1, case %s, what the program showed' % case, showed)
    def _(x):
        out = []
        for t in tags:
            lg = x.log('k2a', t)
            if answer == 'none':
                out.append(no_merge_dialog(lg, t))
                continue
            out.append(dialog_at_save(lg, t))
            if where == 'bg':
                back = lg.step(r'sh monkey ', lg.replace()['at'])
                d = lg.step(r'dialog$', back)
                g = lg.groups(back)
                out.append(check(back > lg.replace()['at'] > lg.step(r'key HOME$') >= 0 and
                                 not [i for i in lg.all("dialog alertTitle") if back < i < lg.step(r'tap menu_save$')]
                                 and g.get('KPG') == 4 and g.get('G') == 9 and d > back,
                                 '%s: nothing on return; KPG 4 entries, G 9' % t, '%s: %s' % (t, g)))
            if answer == 'cancel':
                c = lg.step(r'tap \^Cancel\$')
                out.append(check(lg.step(r'assert entry_title@', c) > c and lg.step(r'tap menu_cancel$', c) > c and
                                 not lg.failed() and lg.pulled()['sha'] == x.base('NEW-diff').sha,
                                 '%s: the editor still open, its cancel taken, the file NEW-diff' % t))
        return all_of(*out)

    if answer in ('overwrite', 'none'):
        after = "OLD with the phone's entry; the per-key time dropped, the only other line"
    elif answer == 'merge':
        after = "NEW with the phone's entry; the per-key time dropped"
    else:
        after = "NEW-diff byte for byte; the phone's entry nowhere"

    @claim(label + '-file', '2.1, case %s, the file afterwards' % case, after)
    def _(x):
        base = 'NEW-diff' if answer == 'merge' else 'OLD'
        return all_of(*[file_is(x, 'k2a', t, base, answer, entry_of(t)) for t in tags])

    want = {'overwrite': 'LOST', 'merge': 'KEPT', 'cancel': 'KEPT', 'none': 'LOST'}[answer]

    @claim(label + '-new', "2.1, case %s, NEW's changes" % case,
           {'overwrite': 'lost', 'merge': 'kept', 'cancel': 'kept; the entry not saved',
            'none': 'nothing to lose: the arriving file held no change'}[answer])
    def _(x):
        out = [verdict_is(x, 'k2a', t, want) for t in tags]
        if answer == 'none':
            out.append(check(x.base('NEW-ident').sha == x.base('OLD').sha, "NEW-ident is OLD's bytes"))
        return all_of(*out)


for _row in K2A_21:
    row21(*_row)


@claim('2.1-dialog-18', '2.1, after the table', 'the dialog in all 18 runs of the table that showed it, every run '
       'but the three of 2f, exactly: the title, the message, [Cancel] [No, overwrite] [Yes, merge]')
def _(x):
    shown, without = [], []
    out = []
    for c in K2A_21:
        for t in c[5]:
            lg = x.log('k2a', t)
            md = lg.merge_dialogs()
            (shown if md else without).append(t)
            for i in md:
                d = lg.dialog_text(i)
                out.append(check(d == {'alertTitle': MERGE_TITLE, 'message': MERGE_TEXT, 'button3': 'Cancel',
                                       'button2': 'No, overwrite', 'button1': 'Yes, merge'}, '', '%s: %s' % (t, d)))
    out.insert(0, check(len(shown) == 18 and sorted(without) == sorted(K2A_21[8][5]),
                        '18 runs showed it; the three without are 2f\'s', 'shown %d, without %s' % (len(shown), without)))
    return all_of(*out)


@claim('2.1-only-entry', '2.1, after the table', 'every "No, overwrite" run read back as OLD with the phone\'s entry and '
       'nothing else; every "Yes, merge" run as NEW with the phone\'s entry and nothing else')
def _(x):
    out = [file_is(x, 'k2a', t, 'OLD', 'overwrite', entry_of(t)) for t in OVERWRITE_ANSWERED]
    out += [file_is(x, 'k2a', t, 'NEW-diff', 'merge', entry_of(t)) for t in K2A_21[1][5]]
    out.append(check(len(OVERWRITE_ANSWERED) == 18, '18 "No, overwrite" runs and 3 "Yes, merge" runs'))
    return all_of(*out)


@claim('2.1-storages', '2.1, the first paragraph', 'the list of storage types offers no local file: System file '
       'picker, Dropbox, Google Drive, OneDrive, SFTP, FTP, WebDAV, ownCloud, Nextcloud, Samba, pCloud, MEGA')
def _(x):
    ls = read(os.path.join(x.root, 'logs', 'explore', 'k2a-storages.log'))
    names = [m.group(1) for m in (re.search(r"Button .*text='([^']+)'", y) for y in ls) if m]
    want = ['System file picker', 'Dropbox', 'Google Drive', 'OneDrive', 'SFTP', 'FTP', 'WebDav', 'OwnCloud',
            'Nextcloud', 'Samba', 'PCloud', 'MEGA']
    missing = [w for w in want if not any(n.lower().startswith(w.lower()) or w.lower() in n.lower() for n in names)]
    local = [n for n in names if re.search(r'local|internal|device', n, re.I)]
    return check(not missing and not local, '%d buttons: %s' % (len(names), ', '.join(names)),
                 'missing %s, local %s' % (missing, local))


# ------------------------------------------------------------------ 2.2 to 2.4

@claim('2.2-3a', '2.2, case 3a', 'open and unlocked, as 2.1: the dialog at Save, the file by the answer')
def _(x):
    out = []
    for c in K2A_21:
        if c[4] in ('overwrite', 'merge', 'cancel'):
            for t in c[5]:
                out.append(dialog_at_save(x.log('k2a', t), t))
                out.append(verdict_is(x, 'k2a', t, 'LOST' if c[4] == 'overwrite' else 'KEPT'))
    return all_of(*out)


def lock_screen_close(lg, tag):
    lock = lg.step(r'tap menu_lock$')
    close = lg.find(r"tapped \(\d+, \d+\) 'Close database'", lock)
    return lock, close


@claim('2.2-3b-runs', '2.2, case 3b, counted runs', '1')
def _(x):
    return counted(x, 'k2a', K2A_OTHER['3b'])


@claim('2.2-3b-sequence', '2.2, case 3b, sequence', 'replacement while open; the lock; the lock screen shows '
       '"Password-based QuickUnlock not available" and offers Close database, pressed; started again; unlocked')
def _(x):
    t = K2A_OTHER['3b'][0]
    lg = x.log('k2a', t)
    rp = lg.replace()
    lock, close = lock_screen_close(lg, t)
    text_at = lg.find(r'Password-based QuickUnlock not available|blocked because screen lock is not activated')
    return all_of(check(rp and rp['at'] < lock < close, 'replacement, lock, Close database pressed'),
                  check(lg.step(r'waitfor password_edit', close) > close, 'the password screen again, unlocked'),
                  check(text_at >= 0, 'the lock screen\'s text', 'the record holds no such text on the lock screen: '
                        'it logged the buttons only, %s' % ', '.join(
                            y.split('dialog ')[1] for y in lg.lines[lock:close] if ' dialog QuickUnlock' in y)))


def kept_after_reopen(x, tags):
    out = []
    for t in tags:
        lg = x.log('k2a', t)
        k = lg.kpg_entries() or []
        out.append(check('KPG revocation' in k, '%s: KPG listed KPG revocation' % t, '%s: KPG listed %s' % (t, k)))
        out.append(no_merge_dialog(lg, t))
    return all_of(*out)


@claim('2.2-3b-showed', '2.2, case 3b, what the program showed', 'after unlocking, KPG listed KPG revocation; no dialog at Save')
def _(x):
    return kept_after_reopen(x, K2A_OTHER['3b'])


@claim('2.2-3b-file', '2.2, case 3b, the file afterwards', "NEW with the phone's entry")
def _(x):
    t = K2A_OTHER['3b'][0]
    return file_is(x, 'k2a', t, 'NEW-diff', 'merge', entry_of(t))


@claim('2.2-3b-new', "2.2, case 3b, NEW's changes", 'kept')
def _(x):
    return verdict_is(x, 'k2a', K2A_OTHER['3b'][0], 'KEPT')


@claim('2.2-3c-runs', '2.2, case 3c, counted runs', '3')
def _(x):
    return counted(x, 'k2a', K2A_OTHER['3c'])


@claim('2.2-3c-sequence', '2.2, case 3c, sequence', 'open; lock; Close database; replacement while closed; '
       'started again, unlocked, entry added and saved')
def _(x):
    out = []
    for t in K2A_OTHER['3c']:
        lg = x.log('k2a', t)
        lock = lg.step(r'tap menu_lock$')
        close = lg.step(r'tap QuickUnlock_buttonLock$', lock)
        rp = lg.replace()
        out.append(check(0 <= lock < close < rp['at'] < lg.step(r'waitfor password_edit', rp['at']) <
                         lg.step(r'tap menu_save$'), '%s: lock, close, replace, unlock, save' % t))
    return all_of(*out)


@claim('2.2-3c-showed', '2.2, case 3c, what the program showed', 'KPG listed KPG revocation; no dialog at Save')
def _(x):
    return kept_after_reopen(x, K2A_OTHER['3c'])


@claim('2.2-3c-file', '2.2, case 3c, the file afterwards', "NEW with the phone's entry")
def _(x):
    return all_of(*[file_is(x, 'k2a', t, 'NEW-diff', 'merge', entry_of(t)) for t in K2A_OTHER['3c']])


@claim('2.2-3c-new', "2.2, case 3c, NEW's changes", 'kept, in every run')
def _(x):
    return all_of(*[verdict_is(x, 'k2a', t, 'KEPT') for t in K2A_OTHER['3c']])


@claim('2.3-5-runs', '2.3, case 5, counted runs', '2')
def _(x):
    return counted(x, 'k2a', K2A_OTHER['5'])


@claim('2.3-5-sequence', '2.3, case 5, sequence', 'open; replacement; G, E1 opened, back, back, KPG listed, back; '
       'lock; Close database')
def _(x):
    out = []
    for t in K2A_OTHER['5']:
        lg = x.log('k2a', t)
        r = lg.replace()['at']
        idx = [lg.step(rx, r) for rx in (r'tap \^G\$', r'tap \^E1\$', r'key BACK', r'tap \^KPG\$', r'tap menu_lock$',
                                         r'tap QuickUnlock_buttonLock$')]
        out.append(check(all(i > r for i in idx) and idx == sorted(idx) and lg.step(r'tap menu_save$') < 0,
                         '%s: in that order, nothing saved by the flow' % t, '%s: %s' % (t, idx)))
    return all_of(*out)


@claim('2.3-5-file', '2.3, case 5, the file afterwards', "NEW-diff byte for byte, with the replacement's own time")
def _(x):
    out = []
    for t in K2A_OTHER['5']:
        lg, v = x.log('k2a', t), x.verdict('k2a', t)
        p, rp = lg.pulled(), lg.replace()
        out.append(check(v['same'] == 'NEW-diff' and p['sha'] == rp['sha'] and p['mtime'] == rp['mtime'],
                         '%s: =NEW-diff, mtime %s' % (t, p['mtime']), '%s: %s %s' % (t, p, rp['mtime'])))
    return all_of(*out)


@claim('2.4-4-runs', '2.4, case 4, counted runs', '1')
def _(x):
    return counted(x, 'k2a', K2A_OTHER['4'])


@claim('2.4-4-showed', '2.4, case 4, what the program showed', "no dialog; NEW's counts (KPG 5 entries, G 8); the "
       'file not written by it (the bytes and time of the replacement); then no dialog at Save')
def _(x):
    t = K2A_OTHER['4'][0]
    lg = x.log('k2a', t)
    s = lg.step(r'tap \^Synchronize\.database')
    g = lg.groups(s)
    d, rp = lg.devstat('after-sync'), lg.replace()
    return all_of(check(s > rp['at'], 'Synchronize database... after the replacement'),
                  check(g.get('KPG') == 5 and g.get('G') == 8, 'KPG 5 entries, G 8', 'counts %s' % g),
                  check(d and d['sha'] == rp['sha'] and d['mtime'] == rp['mtime'],
                        'after it the file is the replacement\'s bytes and time', 'after-sync %s' % d),
                  no_merge_dialog(lg, t))


@claim('2.4-4-file', '2.4, case 4, the file afterwards', "NEW with the phone's entry")
def _(x):
    t = K2A_OTHER['4'][0]
    return file_is(x, 'k2a', t, 'NEW-diff', 'merge', entry_of(t))


@claim('2.4-4-new', "2.4, case 4, NEW's changes", 'kept')
def _(x):
    return verdict_is(x, 'k2a', K2A_OTHER['4'][0], 'KEPT')


# ------------------------------------------------------------------ 2.5 Check for modifications off

def turned_off(lg, t):
    return check(lg.check_states() == ['true', 'false'], '%s: checkbox true, then false' % t,
                 '%s: %s' % (t, lg.check_states()))


for _case, _tags, _seq in (('6a', K2A_OTHER['6a'], 'the setting pressed; back; replacement; entry saved; "No, overwrite"'),
                           ('6b', K2A_OTHER['6b'], 'as 6a, with lock, Close database, start, unlock before the replacement')):
    def _make(case, tags, seq):
        @claim('2.5-%s-runs' % case, '2.5, case %s, counted runs' % case, '1')
        def _(x):
            return counted(x, 'k2a', tags)

        @claim('2.5-%s-sequence' % case, '2.5, case %s, sequence' % case, seq)
        def _(x):
            t = tags[0]
            lg = x.log('k2a', t)
            press = lg.step(r'tapb ')
            rp = lg.replace()
            out = [turned_off(lg, t), check(0 <= press < rp['at'] < lg.step(r'tap menu_save$'), 'press, replace, save')]
            if case == '6b':
                lock = lg.step(r'tap menu_lock$', press)
                out.append(check(press < lock < lg.step(r'waitfor password_edit', lock) < rp['at'],
                                 'lock, close, unlock between the press and the replacement'))
            return all_of(*out)

        @claim('2.5-%s-pref' % case, '2.5, case %s, stored preference' % case,
               'CheckForFileChangesOnSave false' + (', before and after the reopen' if case == '6b' else ''))
        def _(x):
            lg = x.log('k2a', tags[0])
            labels = ['after-tap'] + (['after-reopen'] if case == '6b' else [])
            return all_of(*[check((lg.prefs(lb) or {}).get('CheckForFileChangesOnSave') == 'false', '%s: false' % lb,
                                  '%s: %s' % (lb, lg.prefs(lb))) for lb in labels])

        @claim('2.5-%s-showed' % case, '2.5, case %s, what the program showed' % case, 'the dialog all the same')
        def _(x):
            return dialog_at_save(x.log('k2a', tags[0]), tags[0])

        @claim('2.5-%s-file' % case, '2.5, case %s, the file afterwards' % case, "OLD with the phone's entry")
        def _(x):
            return file_is(x, 'k2a', tags[0], 'OLD', 'overwrite', entry_of(tags[0]))

        @claim('2.5-%s-new' % case, "2.5, case %s, NEW's changes" % case, 'lost (by the answer)')
        def _(x):
            return verdict_is(x, 'k2a', tags[0], 'LOST')
    _make(_case, _tags, _seq)


@claim('2.5-four-false', '2.5, after the table', 'two earlier attempts of 6a stored false and showed the dialog, the file '
       'left as NEW-diff: four runs that stored it false, all four asked')
def _(x):
    out = []
    for n in NOT_COUNTED['off']:
        lg = x.nc_log(n)
        out.append(check((lg.prefs('after-tap') or {}).get('CheckForFileChangesOnSave') == 'false' and lg.merge_dialogs()
                         and x.verdicts_nc[n]['same'] == 'NEW-diff', '%s: false, the dialog, NEW-diff' % n))
    for t in K2A_OTHER['6a'] + K2A_OTHER['6b']:
        lg = x.log('k2a', t)
        out.append(check((lg.prefs('after-tap') or {}).get('CheckForFileChangesOnSave') == 'false' and lg.merge_dialogs(),
                         '%s: false, the dialog' % t))
    return all_of(*out)


@claim('2.5-side-effect', '2.5, the second paragraph', 'in all six runs that showed the screen: nothing stored before '
       'it; UseOfflineCache, SyncOfflineCacheInBackground_key, CheckForFileChangesOnSave, UseFileTransactions true and '
       'AlwaysMergeOnConflict false once shown, with nothing pressed; the dialog in all six')
def _(x):
    want = {'UseOfflineCache': 'true', 'SyncOfflineCacheInBackground_key': 'true', 'CheckForFileChangesOnSave': 'true',
            'UseFileTransactions': 'true', 'AlwaysMergeOnConflict': 'false'}
    logs = [x.log('k2a', t) for t in K2A_OTHER['6a'] + K2A_OTHER['6b']] + \
           [x.nc_log(n) for n in NOT_COUNTED['missed'] + NOT_COUNTED['off']]
    out = []
    for lg in logs:
        n = os.path.basename(lg.path)
        out.append(check(lg.prefs('before-settings') == {} and lg.prefs('on-file-handling-screen') == want and
                         lg.find(r'on-file-handling-screen') < lg.step(r'checkstate ') and lg.merge_dialogs(),
                         '', '%s: before %s, on the screen %s' % (n, lg.prefs('before-settings'),
                                                                 lg.prefs('on-file-handling-screen'))))
    out.append(check(len(logs) == 6, 'six runs, all alike'))
    return all_of(*out)


# ------------------------------------------------------------------ 2.6 QuickUnlock

@claim('2.6-3d-runs', '2.6, case 3d, counted runs', '2')
def _(x):
    return counted(x, 'k2a', K2A_OTHER['3d'])


@claim('2.6-3d-sequence', '2.6, case 3d, sequence', 'the lock; the QuickUnlock screen ("Enter last 3 characters of your '
       'password:", QuickUnlock!, Close database); replacement while locked; the last three characters, QuickUnlock!')
def _(x):
    out = []
    for t in K2A_OTHER['3d']:
        lg = x.log('k2a', t)
        lock = lg.step(r'tap menu_lock$')
        q = lg.step(r'tap QuickUnlock_button\$?$', lock)
        screen = lg.lines[lock:lg.replace()['at']]
        out.append(check(any("text='Enter last 3 characters of your password:'" in y for y in screen) and
                         any("text='QuickUnlock!'" in y for y in screen) and any("text='Close database'" in y for y in screen)
                         and lock < lg.replace()['at'] < lg.step(r'typepw QuickUnlock_password ile$') < q,
                         '%s: the screen, the replacement while locked, three characters, QuickUnlock!' % t))
    return all_of(*out)


@claim('2.6-3d-showed', '2.6, case 3d, what the program showed', "the home screen after QuickUnlock!, the launcher; "
       "OLD's counts (KPG 4 entries, G 9); KPG without KPG revocation; the dialog at Save")
def _(x):
    out = []
    for t in K2A_OTHER['3d']:
        lg = x.log('k2a', t)
        q = lg.step(r'tap QuickUnlock_button')
        back = lg.step(r'sh monkey ', q)
        g = lg.groups(back)
        k = lg.kpg_entries() or []
        out.append(check(q < lg.find(r'id=launcher', q) < back and g.get('KPG') == 4 and g.get('G') == 9 and
                         'KPG revocation' not in k and k, '%s: launcher, KPG 4, G 9, KPG: %s' % (t, ', '.join(k)),
                         '%s: %s %s' % (t, g, k)))
        out.append(dialog_at_save(lg, t))
    return all_of(*out)


@claim('2.6-3d-file', '2.6, case 3d, the file afterwards', "OLD with the phone's entry")
def _(x):
    return all_of(*[file_is(x, 'k2a', t, 'OLD', 'overwrite', entry_of(t)) for t in K2A_OTHER['3d']])


@claim('2.6-3d-new', "2.6, case 3d, NEW's changes", 'lost (by the answer)')
def _(x):
    return all_of(*[verdict_is(x, 'k2a', t, 'LOST') for t in K2A_OTHER['3d']])


# ------------------------------------------------------------------ 3 KeePassDroid

KPD_ALLOWED = r'|^(E \S+ CD |G \S+ CD |M settingsChanged )'


def kpd_custom_data_gone(x, t):
    r, lg = x.run('kpd', t), x.log('kpd', t)
    s = [y for y in r.lines if y.startswith('M settingsChanged ')][0].split()[-1]
    end = re.search(r'rc=\d+ (\S+)Z', lg.rc()).group(1)[:19]
    return all_of(check(not r.has(r'^(E \S+|G \S+) CD '), '%s: no entry or group custom data' % t),
                  check(s[:19] > end, '%s: SettingsChanged %s, after the run ended %s' % (t, s, end)))


@claim('3-1-replacement', '3, case 1, replacement', 'NEW-diff, later, on screen')
def _(x):
    return all_of(*[replaced(x, 'kpd', t, 'NEW-diff', 'later', 'fg') for t in KPD['1']])


@claim('3-1-runs', '3, case 1, counted runs', '3')
def _(x):
    return counted(x, 'kpd', KPD['1'])


@claim('3-1-showed', '3, case 1, what the program showed', 'nothing: no dialog at the replacement, none at SAVE; '
       'the new entry listed at the root')
def _(x):
    out = []
    for t in KPD['1']:
        lg = x.log('kpd', t)
        last = lg.all(r'flow:\d+ list$')[-1]
        out.append(check(not lg.any_dialog_titles() and any("text='%s'" % entry_of(t) in y for y in lg.lines[last:]),
                         '%s: no dialog, the entry listed' % t))
    return all_of(*out)


@claim('3-1-file', '3, case 1, the file afterwards', "OLD with the phone's entry; entry and group custom data "
       'deleted and SettingsChanged the moment of reading')
def _(x):
    out = []
    for t in KPD['1']:
        out.append(file_is(x, 'kpd', t, 'OLD', 'overwrite', entry_of(t), KPD_ALLOWED))
        out.append(kpd_custom_data_gone(x, t))
    return all_of(*out)


@claim('3-1-new', "3, case 1, NEW's changes", 'lost, all three, in all three runs')
def _(x):
    return all_of(*[verdict_is(x, 'kpd', t, 'LOST') for t in KPD['1']])


@claim('3-3b-runs', '3, case 3b, counted runs', '1')
def _(x):
    return counted(x, 'kpd', KPD['3b'])


@claim('3-3b-sequence', '3, case 3b, replacement', 'NEW-diff, later, while open; then Lock Database; unlocked again')
def _(x):
    t = KPD['3b'][0]
    lg = x.log('kpd', t)
    rp = lg.replace()
    lock = lg.step(r'tap Lock\.Database$')
    return all_of(replaced(x, 'kpd', t, 'NEW-diff', 'later', 'fg'),
                  check(rp['at'] < lock < lg.step(r'typepwck? ', lock), 'replacement, Lock Database, the password'))


@claim('3-3b-showed', '3, case 3b, what the program showed', 'the lock left the password screen for the same file; '
       'KPG listed KPG revocation; nothing at SAVE')
def _(x):
    t = KPD['3b'][0]
    lg = x.log('kpd', t)
    lock = lg.step(r'tap Lock\.Database$')
    nxt = lg.lines[lock:lg.step(r'list$', lock) + 40]
    k = lg.kpg_entries() or []
    return all_of(check(any('password' in y.lower() for y in nxt) and any(entry_of(t)[6:] in y or 'kpd-%s' % t in y
                                                                           for y in nxt),
                        'the password screen for kpd-%s.kdbx' % t, 'after the lock: ' + ' | '.join(nxt[:6])),
                  check('KPG revocation' in k, 'KPG listed KPG revocation', 'KPG listed %s' % k),
                  check(not lg.any_dialog_titles(), 'no dialog'))


@claim('3-3b-file', '3, case 3b, the file afterwards', "NEW with the phone's entry, custom data deleted as above")
def _(x):
    t = KPD['3b'][0]
    return all_of(file_is(x, 'kpd', t, 'NEW-diff', 'merge', entry_of(t), KPD_ALLOWED), kpd_custom_data_gone(x, t))


@claim('3-3b-new', "3, case 3b, NEW's changes", 'kept')
def _(x):
    return verdict_is(x, 'kpd', KPD['3b'][0], 'KEPT')


# ------------------------------------------------------------------ 5 what the operator can rely on

@claim('5-k2a', '5, Keepass2Android', '18 runs of 18 with "No, overwrite" lost NEW\'s changes and no other '
       'sequence lost anything: 3 of 3 merges, 2 of 2 looks, 4 of 4 Close database, 1 of 1 Synchronize')
def _(x):
    k = {t: v for t, v in x.verdicts.items() if v['name'].startswith('k2a-')}
    lost = sorted(t for t, v in k.items() if v['new'] == 'LOST')
    want_lost = sorted(OVERWRITE_ANSWERED + K2A_21[8][5])
    return all_of(check(lost == want_lost, '%d lost: the 18 "No, overwrite" runs and the 3 of 2f, which had nothing '
                        'to lose' % len(lost), 'lost: %s' % lost),
                  check(all(k[t]['new'] == 'KEPT' for t in K2A_21[1][5] + K2A_OTHER['5'] + K2A_OTHER['3b'] +
                            K2A_OTHER['3c'] + K2A_OTHER['4']), '3 + 2 + 4 + 1 kept'))


@claim('5-kpd', '5, KeePassDroid', 'saved over the changed file without any question, 3 of 3; unlocking after its '
       'own lock read the file afresh, 1 run')
def _(x):
    return all_of(*[verdict_is(x, 'kpd', t, 'LOST') for t in KPD['1']] + [verdict_is(x, 'kpd', KPD['3b'][0], 'KEPT')])


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
        print('%-15s %-24s %s' % ('holds' if ok else 'DOES NOT HOLD', cid, text[:150]))
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
