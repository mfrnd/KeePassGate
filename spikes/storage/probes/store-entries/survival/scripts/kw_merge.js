// kdbxweb merge over invented databases (password "test"):
//   node kw_merge.js <kdbxweb module dir> <local> <remote> <out>
'use strict';
const fs = require('fs');
const [, , modDir, local, remote, out] = process.argv;
const kdbxweb = require(modDir);

async function load(path) {
    const data = fs.readFileSync(path);
    const ab = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
    return kdbxweb.Kdbx.load(ab, new kdbxweb.Credentials(kdbxweb.ProtectedValue.fromString('test')));
}

(async () => {
    const l = await load(local);
    const r = await load(remote);
    l.merge(r);
    const ab = await l.save();
    fs.writeFileSync(out, Buffer.from(ab));
    console.log('kdbxweb', require(modDir + '/package.json').version, 'merged', local.split(/[\\/]/).pop(), '<-', remote.split(/[\\/]/).pop());
})().catch((e) => { console.error('ERROR', e && e.stack || e); process.exit(1); });
