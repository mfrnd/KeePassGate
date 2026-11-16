// kdbxweb 2.1.1 for `kdbx-places`, with Node's own Argon2 handed to it, since kdbxweb brings none.
//   node kw.js <kdbxweb module dir> resave <in> <out>
//   node kw.js <kdbxweb module dir> edit <in> <out>      an unrelated entry added at the root
//   node kw.js <kdbxweb module dir> merge <open> <arriving> <out>
'use strict';
const fs = require('fs');
const crypto = require('crypto');
const [, , modDir, action, ...rest] = process.argv;
const kdbxweb = require(modDir);

// kdbxweb's Argon2Type: 0 is Argon2d, 2 is Argon2id. Node implements version 0x13 only.
kdbxweb.CryptoEngine.setArgon2Impl((password, salt, memory, iterations, length, parallelism, type, version) => {
    if (version !== 0x13) return Promise.reject(new Error('Argon2 version ' + version + ' is not 0x13'));
    const algorithm = type === 2 ? 'argon2id' : type === 1 ? 'argon2i' : 'argon2d';
    const out = crypto.argon2Sync(algorithm, {
        message: Buffer.from(password), nonce: Buffer.from(salt), parallelism,
        tagLength: length, memory, passes: iterations,
    });
    return Promise.resolve(out.buffer.slice(out.byteOffset, out.byteOffset + out.byteLength));
});

async function load(path) {
    const data = fs.readFileSync(path);
    const ab = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
    const cred = new kdbxweb.Credentials(kdbxweb.ProtectedValue.fromString('probe-only'));
    return kdbxweb.Kdbx.load(ab, cred);
}

async function save(db, path) {
    fs.writeFileSync(path, Buffer.from(await db.save()));
}

(async () => {
    if (action === 'resave') {
        const db = await load(rest[0]);
        await save(db, rest[1]);
    } else if (action === 'edit') {
        const db = await load(rest[0]);
        const e = db.createEntry(db.getDefaultGroup());
        e.fields.set('Title', 'kw-added');
        await save(db, rest[1]);
    } else if (action === 'merge') {
        const open = await load(rest[0]);
        const arriving = await load(rest[1]);
        open.merge(arriving);
        await save(open, rest[2]);
    } else {
        throw new Error('unknown action ' + action);
    }
    console.log(action + ' ok');
})().catch((e) => { console.log('ERROR ' + (e && (e.code || e.name)) + ': ' + (e && e.message)); process.exit(1); });
