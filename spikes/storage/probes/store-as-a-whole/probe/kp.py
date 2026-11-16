"""Driver for the KeePassLib host (host.ps1) and for keepassxc-cli. Invented databases only.

A copy of probes\\crdt\\survival\\probe\\kp.py for the family store-as-a-whole: the paths are this
family's, `dump` reads Store.cs's dump with every KPG. string, and `uid` is gone, since store.py
derives every identifier as the design does.
"""
import base64
import os
import shutil
import subprocess

FAMILY = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BRANCH = os.path.normpath(os.path.join(FAMILY, '..', '..', '..', '..'))
HOST = os.path.join(FAMILY, 'probe', 'host.ps1')
XC = {
    '2.4.2': os.path.join(BRANCH, '.keepassxc', '2.4.2', 'keepassxc-cli.exe'),
    '2.6.6': os.path.join(BRANCH, '.keepassxc', '2.6.6', 'keepassxc-cli.exe'),
    '2.7.12': os.path.join(BRANCH, '.keepassxc', '2.7.12', 'keepassxc-cli.exe'),
    '2.8.0': os.path.join(BRANCH, '.keepassxc', '2.8.0', 'keepassxc-cli.exe'),
}
NOWIN = 0x08000000  # CREATE_NO_WINDOW


def b64(s):
    return base64.b64encode(s.encode('utf-8')).decode('ascii')


def unb64(s):
    return base64.b64decode(s).decode('utf-8')


class Host:
    def __init__(self):
        # Without PSModulePath, so that a Windows PowerShell started under PowerShell 7 loads its own
        # modules rather than failing on PowerShell 7's.
        env = {k: v for k, v in os.environ.items() if k.upper() != 'PSMODULEPATH'}
        self.p = subprocess.Popen(
            ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', HOST],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding='utf-8', creationflags=NOWIN, env=env)
        first = self.p.stdout.readline().strip()
        if first != 'ready':
            raise RuntimeError('host: ' + first + self.p.stdout.read())

    def run(self, *args):
        self.p.stdin.write('\t'.join(str(a) for a in args) + '\n')
        self.p.stdin.flush()
        out = []
        while True:
            line = self.p.stdout.readline()
            if line == '':
                raise RuntimeError('host died: ' + '\n'.join(out))
            line = line.rstrip('\n')
            if line == '<<END>>':
                break
            out.append(line)
        text = '\n'.join(out)
        if text.startswith('ERROR'):
            raise RuntimeError(' '.join(str(a) for a in args) + ': ' + text)
        return text

    def close(self):
        try:
            self.p.stdin.write('quit\n')
            self.p.stdin.flush()
            self.p.wait(10)
        except Exception:
            self.p.kill()

    def dump(self, path):
        """The file as Store.cs dumps it: {bin, root, groups, entries, dels}."""
        d = {'entries': {}, 'dels': {}, 'groups': {}, 'bin': None, 'root': None}
        for line in self.run('dump', path).split('\n'):
            p = line.split('\t')
            if p[0] == 'R':
                d['bin'], d['root'] = (None if p[1] == '-' else p[1]), p[2]
            elif p[0] == 'G':
                d['groups'][p[1]] = dict(parent=p[2], name=unb64(p[3]), store=p[4] == '1')
            elif p[0] == 'E':
                strings = {}
                for kv in p[8:]:
                    k, v = kv.split('=', 1)
                    strings[k] = unb64(v)
                d['entries'][p[1]] = dict(mt=int(p[2]), loc=int(p[3]), place=p[4], parent=p[5],
                                          title=unb64(p[6]), hist=int(p[7]), strings=strings)
            elif p[0] == 'D':
                d['dels'][p[1]] = int(p[2])
        return d


def xc_merge(version, target, source):
    """keepassxc-cli merge -s <target> <source>: the target is written in place."""
    p = subprocess.run([XC[version], 'merge', '-s', target, source], input='test\ntest\n',
                       capture_output=True, text=True, creationflags=NOWIN,
                       cwd=os.path.dirname(target), timeout=600)
    return p.returncode, (p.stdout + p.stderr).strip()


def copy(src, dst):
    shutil.copyfile(src, dst)
