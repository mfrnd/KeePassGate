// kdbxweb probe over invented databases: node kw.js <kdbxweb module dir> merge <local> <remote> <out>
'use strict';
const fs = require('fs');
const [, , modDir, action, ...rest] = process.argv;
const kdbxweb = require(modDir);

async function load(path) {
    const data = fs.readFileSync(path);
    const ab = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
    const cred = new kdbxweb.Credentials(kdbxweb.ProtectedValue.fromString('probe-only'));
    return kdbxweb.Kdbx.load(ab, cred);
}

(async () => {
    if (action === 'merge') {
        const [local, remote, out] = rest;
        const l = await load(local);
        const r = await load(remote);
        l.merge(r);
        fs.writeFileSync(out, Buffer.from(await l.save()));
        console.log('merged', local, '<-', remote, '->', out);
    }
})().catch((e) => { console.error('ERROR', e && e.stack || e); process.exit(1); });
