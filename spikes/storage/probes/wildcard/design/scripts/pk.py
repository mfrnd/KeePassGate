# pykeepass round trips over invented databases. Run with:
#   uv run --no-project --with pykeepass python -I -X utf8 pk.py <command> <in> <out>
import sys
from pykeepass import PyKeePass

cmd, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
kp = PyKeePass(src, password="probe-only")
if cmd == "resave":
    pass
elif cmd == "delete-newest":
    # Remove the snapshot entry with the highest sequence number, as a script would: no deletion record.
    best = None
    for e in kp.entries:
        b = e.uuid.bytes
        if b[:4] == b"KPGS":
            seq = int.from_bytes(b[4:8], "big")
            if best is None or seq > best[0]:
                best = (seq, e)
    if best is None:
        raise SystemExit("no snapshot")
    kp.delete_entry(best[1])
else:
    raise SystemExit("unknown command")
kp.save(dst)
print("pykeepass", cmd, "->", dst)
