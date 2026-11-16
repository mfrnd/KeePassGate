r"""Lists the PNG chunks of the unused custom icon in each file, and whether the text value of the
first file's icon is in each of the others, compressed or not.

    icon_chunks.py <work\cap\M.icon.data\<n>.kdbx> <work\cap-out\<program>\M.icon.data\<n>.kdbx> ...

Run by hand after `run.ps1`, on the files the `cap` step leaves in `work\`; `RUN.md` gives the
command.
"""
import base64
import struct
import sys
import zlib

from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import places  # noqa: E402
from lxml import etree  # noqa: E402


def icon(path):
    k = places.open_file(path)
    root = etree.fromstring(k.xml)
    for ic in root.iter("Icon"):
        if places.kp_uuid(ic.findtext("UUID")) == "icon-unused":
            return base64.b64decode(ic.findtext("Data"))


def chunks(png):
    pos, out = 8, []
    while pos < len(png):
        n = struct.unpack(">I", png[pos:pos + 4])[0]
        t = png[pos + 4:pos + 8].decode("latin-1")
        body = png[pos + 8:pos + 8 + n]
        out.append((t, body))
        pos += 12 + n
    return out


def texts(png):
    found = []
    for t, b in chunks(png):
        if t == "tEXt":
            found.append(b.split(b"\0", 1)[1])
        elif t == "zTXt":
            found.append(zlib.decompress(b.split(b"\0", 1)[1][1:]))
        elif t == "iTXt":
            k, rest = b.split(b"\0", 1)
            flag = rest[0]
            rest = rest[2:].split(b"\0", 2)[2]
            found.append(zlib.decompress(rest) if flag else rest)
    return found


base = icon(sys.argv[1])
value = texts(base)[0]
print("base", len(base), [t for t, _ in chunks(base)], len(value))
for p in sys.argv[2:]:
    png = icon(p)
    tx = texts(png)
    print(Path(p).parent.parent.name + "\\" + Path(p).name, len(png), [t for t, _ in chunks(png)], "value kept" if value in tx else "value gone")
