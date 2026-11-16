"""Find two distinct attachment bodies of equal length whose MD5 digests share the first four bytes.

KeePassDX keys attachment deduplication on that 32-bit value (BinaryData.kt, BinaryPool.kt). This
measures what such a pair costs to find, and writes the pair for an emulator run to use as two
attachments in a base database. Invented content only.
"""
import hashlib
import json
import os
import sys
import time

out_dir = sys.argv[1]
seen = {}
start = time.perf_counter()
tries = 0
while True:
    tries += 1
    body = json.dumps({"kpg": "record", "nonce": tries}, separators=(",", ":")).encode()
    # Pad to a fixed length so both members of the pair have the same size, as BinaryData.equals needs.
    body = body.ljust(48, b" ")
    key = hashlib.md5(body).digest()[:4]
    other = seen.get(key)
    if other is not None and other != body:
        elapsed = time.perf_counter() - start
        break
    seen[key] = body
os.makedirs(out_dir, exist_ok=True)
with open(os.path.join(out_dir, "a.bin"), "wb") as f:
    f.write(other)
with open(os.path.join(out_dir, "b.bin"), "wb") as f:
    f.write(body)
print(f"tries={tries} seconds={elapsed:.2f} length={len(body)}")
print("a", other.decode().rstrip(), hashlib.md5(other).hexdigest())
print("b", body.decode().rstrip(), hashlib.md5(body).hexdigest())
