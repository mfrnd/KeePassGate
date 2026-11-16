"""A reader and writer of the KDBX format that interprets no more than decrypting needs.

Read: both headers field by field in file order, the bytes after the last block, and the XML
document as it was written, with every protected value decrypted beside it. Nothing a program
could have dropped on reading is lost here, so "the reader dropped it" and "the writer dropped it"
can be told apart.

Write (KDBX 4.x only): the same file again, with header fields, inner header fields, the XML text
and trailing bytes changed as the caller likes. The master seed, the KDF parameters and the inner
stream key are kept, so every protected value in the XML stays valid as long as the caller does not
add, remove or reorder a protected value.

Needs `cryptography`, `argon2-cffi` and `lxml`.
"""
from __future__ import annotations

import gzip
import hashlib
import hmac
import struct
from dataclasses import dataclass, field

import argon2.low_level as a2
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

SIG1, SIG2 = 0x9AA2D903, 0xB54BFB67
CIPHER_AES = bytes.fromhex("31c1f2e6bf714350be5805216afc5aff")
CIPHER_CHACHA = bytes.fromhex("d6038a2b8b6f4cb5a524339a31dbb59a")
KDF_AES = bytes.fromhex("c9d9f39a628a4460bf740d08c18a4fea")
KDF_ARGON2D = bytes.fromhex("ef636ddf8c29444b91f7a9a403e30a0c")
KDF_ARGON2ID = bytes.fromhex("9e298b1956db4773b23dfc3ec6f0a1e6")
SALSA_NONCE = bytes.fromhex("e830094b97205d2a")

VD_TYPES = {0x04: "UInt32", 0x05: "UInt64", 0x08: "Bool", 0x0C: "Int32", 0x0D: "Int64",
            0x18: "String", 0x42: "Bytes"}


class KdbxError(Exception):
    pass


# --- the variant dictionary -------------------------------------------------------------------

@dataclass
class VarDict:
    version: int = 0x0100
    items: list = field(default_factory=list)  # [type, name(str), value(bytes)], file order

    @staticmethod
    def parse(b: bytes) -> "VarDict":
        version = struct.unpack_from("<H", b, 0)[0]
        pos, items = 2, []
        while True:
            t = b[pos]
            pos += 1
            if t == 0:
                break
            (nl,) = struct.unpack_from("<i", b, pos)
            pos += 4
            name = b[pos:pos + nl].decode("utf-8")
            pos += nl
            (vl,) = struct.unpack_from("<i", b, pos)
            pos += 4
            items.append([t, name, b[pos:pos + vl]])
            pos += vl
        return VarDict(version, items)

    def build(self) -> bytes:
        out = bytearray(struct.pack("<H", self.version))
        for t, name, value in self.items:
            nb = name.encode("utf-8")
            out += bytes([t]) + struct.pack("<i", len(nb)) + nb + struct.pack("<i", len(value)) + value
        out.append(0)
        return bytes(out)

    def get(self, name: str):
        for t, n, v in self.items:
            if n == name:
                return t, v
        return None

    def set(self, t: int, name: str, value: bytes) -> None:
        for item in self.items:
            if item[1] == name:
                item[0], item[2] = t, value
                return
        self.items.append([t, name, value])

    @staticmethod
    def show(t: int, v: bytes) -> str:
        kind = VD_TYPES.get(t, f"type0x{t:02x}")
        if t in (0x04, 0x0C) and len(v) == 4:
            return f"{kind}:{struct.unpack('<I' if t == 4 else '<i', v)[0]}"
        if t in (0x05, 0x0D) and len(v) == 8:
            return f"{kind}:{struct.unpack('<Q' if t == 5 else '<q', v)[0]}"
        if t == 0x08 and len(v) == 1:
            return f"{kind}:{bool(v[0])}"
        if t == 0x18:
            return f"{kind}:{short(v.decode('utf-8', 'replace'))}"
        return f"{kind}:{blob(v)}"


def short(s: str, limit: int = 120) -> str:
    """A value as a dump line holds it: whole when short, else its length and a digest."""
    if len(s) <= limit:
        return s.replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t")
    d = hashlib.sha256(s.encode("utf-8")).hexdigest()[:16]
    return f"<{len(s)} chars sha256:{d} starts {short(s[:24])}>"


def blob(b: bytes) -> str:
    return f"<{len(b)} bytes sha256:{hashlib.sha256(b).hexdigest()[:16]}>"


# --- key derivation and stream ciphers --------------------------------------------------------

def composite_key(password: str) -> bytes:
    return hashlib.sha256(hashlib.sha256(password.encode("utf-8")).digest()).digest()


def aes_kdf(key: bytes, seed: bytes, rounds: int) -> bytes:
    enc = Cipher(algorithms.AES(seed), modes.ECB()).encryptor()
    data = key
    for _ in range(rounds):
        data = enc.update(data)
    return hashlib.sha256(data).digest()


def transform(kdf: VarDict, composite: bytes) -> bytes:
    uuid = kdf.get("$UUID")[1]
    if uuid == KDF_AES:
        rounds = struct.unpack("<Q", kdf.get("R")[1])[0]
        return aes_kdf(composite, kdf.get("S")[1], rounds)
    if uuid in (KDF_ARGON2D, KDF_ARGON2ID):
        def u(name, fmt, default=None):
            item = kdf.get(name)
            return struct.unpack(fmt, item[1])[0] if item else default
        secret = kdf.get("K")
        assoc = kdf.get("A")
        if secret or assoc:
            raise KdbxError("Argon2 secret or associated data is not supported here")
        return a2.hash_secret_raw(
            composite, kdf.get("S")[1], time_cost=u("I", "<Q"), memory_cost=u("M", "<Q") // 1024,
            parallelism=u("P", "<I"), hash_len=32,
            type=a2.Type.D if uuid == KDF_ARGON2D else a2.Type.ID, version=u("V", "<I", 0x13))
    raise KdbxError(f"unknown KDF {uuid.hex()}")


def _salsa20_block(key: bytes, nonce: bytes, counter: int) -> bytes:
    def rotl(v, c):
        return ((v << c) & 0xFFFFFFFF) | (v >> (32 - c))
    c = b"expand 32-byte k"
    k = struct.unpack("<8I", key)
    n = struct.unpack("<2I", nonce)
    cs = struct.unpack("<4I", c)
    s = [cs[0], k[0], k[1], k[2], k[3], cs[1], n[0], n[1],
         counter & 0xFFFFFFFF, counter >> 32, cs[2], k[4], k[5], k[6], k[7], cs[3]]
    x = list(s)

    def qr(a, b, cc, d):
        x[b] ^= rotl((x[a] + x[d]) & 0xFFFFFFFF, 7)
        x[cc] ^= rotl((x[b] + x[a]) & 0xFFFFFFFF, 9)
        x[d] ^= rotl((x[cc] + x[b]) & 0xFFFFFFFF, 13)
        x[a] ^= rotl((x[d] + x[cc]) & 0xFFFFFFFF, 18)
    for _ in range(10):
        qr(0, 4, 8, 12); qr(5, 9, 13, 1); qr(10, 14, 2, 6); qr(15, 3, 7, 11)  # noqa: E702
        qr(0, 1, 2, 3); qr(5, 6, 7, 4); qr(10, 11, 8, 9); qr(15, 12, 13, 14)  # noqa: E702
    return struct.pack("<16I", *[(x[i] + s[i]) & 0xFFFFFFFF for i in range(16)])


def key_stream(stream_id: int, key: bytes, length: int) -> bytes:
    if stream_id == 3:
        h = hashlib.sha512(key).digest()
        nonce16 = b"\x00\x00\x00\x00" + h[32:44]
        return Cipher(algorithms.ChaCha20(h[:32], nonce16), None).encryptor().update(b"\x00" * length)
    if stream_id == 2:
        k = hashlib.sha256(key).digest()
        out = bytearray()
        counter = 0
        while len(out) < length:
            out += _salsa20_block(k, SALSA_NONCE, counter)
            counter += 1
        return bytes(out[:length])
    raise KdbxError(f"inner stream {stream_id} is not supported here")


# --- the file ---------------------------------------------------------------------------------

@dataclass
class Kdbx:
    major: int = 4
    minor: int = 1
    fields: list = field(default_factory=list)        # [id, bytes] in file order
    inner: list = field(default_factory=list)         # [id, bytes] in file order (4.x)
    xml: bytes = b""
    trailing: bytes = b""
    transformed: bytes = b""

    def field(self, fid: int):
        for f, v in self.fields:
            if f == fid:
                return v
        return None

    def inner_field(self, fid: int):
        for f, v in self.inner:
            if f == fid:
                return v
        return None

    @property
    def kdf(self) -> VarDict | None:
        v = self.field(11)
        return VarDict.parse(v) if v is not None else None

    def stream(self):
        """The inner stream's algorithm and key, wherever this version keeps them."""
        if self.major >= 4:
            return struct.unpack("<I", self.inner_field(1))[0], self.inner_field(2)
        return struct.unpack("<I", self.field(10))[0], self.field(8)


def _cipher(cipher_id: bytes, key: bytes, iv: bytes, decrypt: bool, data: bytes) -> bytes:
    if cipher_id == CIPHER_AES:
        c = Cipher(algorithms.AES(key), modes.CBC(iv))
        if decrypt:
            dec = c.decryptor()
            out = dec.update(data) + dec.finalize()
            pad = out[-1]
            if pad < 1 or pad > 16 or out[-pad:] != bytes([pad]) * pad:
                raise KdbxError("bad padding")
            return out[:-pad]
        pad = 16 - len(data) % 16
        enc = c.encryptor()
        return enc.update(data + bytes([pad]) * pad) + enc.finalize()
    if cipher_id == CIPHER_CHACHA:
        c = Cipher(algorithms.ChaCha20(key, b"\x00\x00\x00\x00" + iv), None)
        return c.encryptor().update(data)
    raise KdbxError(f"unknown cipher {cipher_id.hex()}")


def load(data: bytes, password: str) -> Kdbx:
    s1, s2, ver = struct.unpack_from("<III", data, 0)
    if s1 != SIG1 or s2 != SIG2:
        raise KdbxError("not a KDBX file")
    k = Kdbx(major=ver >> 16, minor=ver & 0xFFFF)
    pos = 12
    while True:
        fid = data[pos]
        if k.major >= 4:
            (size,) = struct.unpack_from("<i", data, pos + 1)
            pos += 5
        else:
            (size,) = struct.unpack_from("<H", data, pos + 1)
            pos += 3
        k.fields.append([fid, data[pos:pos + size]])
        pos += size
        if fid == 0:
            break
    header = data[:pos]
    composite = composite_key(password)
    seed = k.field(4)
    if k.major >= 4:
        k.transformed = transform(k.kdf, composite)
        if hashlib.sha256(header).digest() != data[pos:pos + 32]:
            raise KdbxError("header hash mismatch")
        base = hashlib.sha512(seed + k.transformed + b"\x01").digest()
        hkey = hashlib.sha512(b"\xff" * 8 + base).digest()
        if hmac.new(hkey, header, hashlib.sha256).digest() != data[pos + 32:pos + 64]:
            raise KdbxError("wrong key or header HMAC mismatch")
        pos += 64
        blocks, index = bytearray(), 0
        while True:
            mac = data[pos:pos + 32]
            (size,) = struct.unpack_from("<i", data, pos + 32)
            chunk = data[pos + 36:pos + 36 + size]
            block_key = hashlib.sha512(struct.pack("<Q", index) + base).digest()
            if hmac.new(block_key, struct.pack("<Q", index) + struct.pack("<i", size) + chunk,
                        hashlib.sha256).digest() != mac:
                raise KdbxError(f"block {index} HMAC mismatch")
            pos += 36 + size
            index += 1
            if size == 0:
                break
            blocks += chunk
        k.trailing = data[pos:]
        plain = _cipher(k.field(2), hashlib.sha256(seed + k.transformed).digest(), k.field(7), True,
                        bytes(blocks))
        if struct.unpack("<I", k.field(3))[0] == 1:
            plain = gzip.decompress(plain)
        p = 0
        while True:
            fid = plain[p]
            (size,) = struct.unpack_from("<i", plain, p + 1)
            k.inner.append([fid, plain[p + 5:p + 5 + size]])
            p += 5 + size
            if fid == 0:
                break
        k.xml = plain[p:]
    else:
        kdf = VarDict(items=[[0x42, "$UUID", KDF_AES], [0x42, "S", k.field(5)], [0x05, "R", k.field(6)]])
        k.transformed = transform(kdf, composite)
        plain = _cipher(k.field(2), hashlib.sha256(seed + k.transformed).digest(), k.field(7), True,
                        data[pos:])
        if plain[:32] != k.field(9):
            raise KdbxError("wrong key")
        p, body = 32, bytearray()
        while True:
            (size,) = struct.unpack_from("<i", plain, p + 36)
            body += plain[p + 40:p + 40 + size]
            p += 40 + size
            if size == 0:
                break
        k.trailing = plain[p:]
        k.xml = gzip.decompress(bytes(body)) if struct.unpack("<I", k.field(3))[0] == 1 else bytes(body)
    return k


def save(k: Kdbx) -> bytes:
    """KDBX 4.x only: the file again, keeping seed, KDF and inner key, as `k` now holds them."""
    if k.major < 4:
        raise KdbxError("writing is KDBX 4.x only")
    header = bytearray(struct.pack("<III", SIG1, SIG2, (k.major << 16) | k.minor))
    for fid, value in k.fields:
        header += bytes([fid]) + struct.pack("<i", len(value)) + value
    header = bytes(header)
    seed = k.field(4)
    base = hashlib.sha512(seed + k.transformed + b"\x01").digest()
    hkey = hashlib.sha512(b"\xff" * 8 + base).digest()
    out = bytearray(header + hashlib.sha256(header).digest() + hmac.new(hkey, header, hashlib.sha256).digest())
    plain = bytearray()
    for fid, value in k.inner:
        plain += bytes([fid]) + struct.pack("<i", len(value)) + value
    plain += k.xml
    plain = bytes(plain)
    if struct.unpack("<I", k.field(3))[0] == 1:
        plain = gzip.compress(plain)
    enc = _cipher(k.field(2), hashlib.sha256(seed + k.transformed).digest(), k.field(7), False, plain)
    index, pos = 0, 0
    while True:
        chunk = enc[pos:pos + 1048576]
        pos += len(chunk)
        block_key = hashlib.sha512(struct.pack("<Q", index) + base).digest()
        mac = hmac.new(block_key, struct.pack("<Q", index) + struct.pack("<i", len(chunk)) + chunk,
                       hashlib.sha256).digest()
        out += mac + struct.pack("<i", len(chunk)) + chunk
        index += 1
        if not chunk:
            break
    out += k.trailing
    return bytes(out)
