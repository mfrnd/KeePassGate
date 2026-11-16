// kdbxweb probe: node kw.js <kdbxweb module dir> merge <local> <remote> <out>
//                node kw.js <kdbxweb module dir> resave <in> <out>
'use strict';
const fs = require('fs');
const [, , modDir, action, ...rest] = process.argv;
const kdbxweb = require(modDir);

async function load(path) {
    const data = fs.readFileSync(path);
    const ab = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
    const cred = new kdbxweb.Credentials(kdbxweb.ProtectedValue.fromString('test'));
    return kdbxweb.Kdbx.load(ab, cred);
}

async function save(db, path) {
    const ab = await db.save();
    fs.writeFileSync(path, Buffer.from(ab));
}

(async () => {
    if (action === 'merge') {
        const [local, remote, out] = rest;
        const l = await load(local);
        const r = await load(remote);
        l.merge(r);
        await save(l, out);
        console.log('merged', local, '<-', remote, '->', out);
    } else if (action === 'rawxml') {
        // the XML document as the binary save built it, before encryption
        const db = await load(rest[0]);
        await db.save();
        console.log(kdbxweb.ByteUtils ? new (require(rest[1]).XMLSerializer)().serializeToString(db.xml) : '');
    } else if (action === 'xml') {
        const db = await load(rest[0]);
        console.log(await db.saveXml(true));
    } else if (action === 'resave') {
        const [input, out] = rest;
        const db = await load(input);
        await save(db, out);
        console.log('resaved', input, '->', out, 'version', db.header.versionMajor + '.' + db.header.versionMinor);
    }
})().catch((e) => { console.error('ERROR', e && e.stack || e); process.exit(1); });
