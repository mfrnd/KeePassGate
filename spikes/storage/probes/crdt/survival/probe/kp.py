"""Driver for the KeePassLib host (host.ps1) and for keepassxc-cli. Invented databases only."""
import hashlib
import os
import shutil
import subprocess

# Changed on the branch spikes/storage-survival, paths only: this probe's directory, and the
# releases tools\KeePassXC.ps1 provides under the branch root (see RUN.md of this family).
SURVIVAL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BRANCH = os.path.normpath(os.path.join(SURVIVAL, '..', '..', '..', '..', '..'))
HOST = os.path.join(SURVIVAL, 'probe', 'host.ps1')
XC = {
    '2.4.2': os.path.join(BRANCH, '.keepassxc', '2.4.2', 'keepassxc-cli.exe'),
    '2.6.6': os.path.join(BRANCH, '.keepassxc', '2.6.6', 'keepassxc-cli.exe'),
    '2.7.12': os.path.join(BRANCH, '.keepassxc', '2.7.12', 'keepassxc-cli.exe'),
    '2.8.0': os.path.join(BRANCH, '.keepassxc', '2.8.0', 'keepassxc-cli.exe'),
}
NOWIN = 0x08000000  # CREATE_NO_WINDOW


def uid(name):
    """A deterministic 16-byte identifier, as hex, for a model element or witness name."""
    return hashlib.sha256(('kpg-crdt/' + name).encode()).hexdigest()[:32]


class Host:
    def __init__(self):
        self.p = subprocess.Popen(
            ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', HOST],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding='utf-8', creationflags=NOWIN)
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
        d = {'entries': {}, 'dels': {}, 'groups': [], 'bin': None}
        for line in self.run('dump', path).split('\n'):
            p = line.split('\t')
            if p[0] == 'R':
                d['bin'] = p[1]
            elif p[0] == 'G':
                d['groups'].append((p[1], p[2], p[3]))
            elif p[0] == 'E':
                d['entries'][p[1]] = dict(mt=int(p[2]), loc=int(p[3]), place=p[4], id=p[5],
                                          intact=p[6] == '1', hist=int(p[7]))
            elif p[0] == 'D':
                d['dels'][p[1]] = int(p[2])
        return d


def xc_merge(version, target, source):
    """keepassxc-cli merge -s <target> <source>: the target is written in place."""
    p = subprocess.run([XC[version], 'merge', '-s', target, source], input='test\ntest\n',
                       capture_output=True, text=True, creationflags=NOWIN,
                       cwd=os.path.dirname(target), timeout=120)
    return p.returncode, (p.stdout + p.stderr).strip()


def xc_rm(version, path, entry_path):
    p = subprocess.run([XC[version], 'rm', path, entry_path], input='test\ntest\n',
                       capture_output=True, text=True, creationflags=NOWIN,
                       cwd=os.path.dirname(path), timeout=120)
    return p.returncode, (p.stdout + p.stderr).strip()


def copy(src, dst):
    shutil.copyfile(src, dst)
