"""Order of a head's custom strings in the XML as KeePassLib writes it and as KeePassXC writes it.
Needs pykeepass (uv run --no-project --with pykeepass)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kp  # noqa: E402
from pykeepass import PyKeePass  # noqa: E402

W = os.path.join(kp.SURVIVAL, 'work', 'order')
os.makedirs(W, exist_ok=True)
for x in os.listdir(W):
    os.remove(os.path.join(W, x))
h = kp.Host()
try:
    a, b = os.path.join(W, 'a.kdbx'), os.path.join(W, 'b.kdbx')
    h.run('new', a, 'fedcba9876543210')
    kp.copy(a, b)
    spec = os.path.join(W, 'spec.txt')
    extra = ';'.join('s%02d' % i for i in range(1, 13))
    with open(spec, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\t'.join(['A', kp.uid('Hx'), '50', 'head', 'Hx', '{"v":"2"}', extra]) + '\n')
    h.run('patch', a, spec)
finally:
    h.close()
res = []
for label, path in (('KeePassLib 2.62 wrote it', a),):
    kpx = PyKeePass(path, password='test')
    e = [x for x in kpx.entries if x.title == 'KPG head Hx'][0]
    res.append(label + ': ' + ', '.join(s.find('Key').text for s in e._element.findall('String')))
for v in sorted(kp.XC):
    t = os.path.join(W, 'b-%s.kdbx' % v)
    kp.copy(b, t)
    rc, txt = kp.xc_merge(v, t, a)
    kpx = PyKeePass(t, password='test')
    e = [x for x in kpx.entries if x.title == 'KPG head Hx'][0]
    res.append('keepassxc-cli %s wrote it (exit %d): ' % (v, rc) + ', '.join(s.find('Key').text for s in e._element.findall('String')))
print('\n'.join(res))
with open(os.path.join(kp.SURVIVAL, 'out', 'order.txt'), 'w', encoding='utf-8') as fh:
    fh.write('\n'.join(res) + '\n')
