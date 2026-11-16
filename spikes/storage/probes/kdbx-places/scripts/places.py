"""Every place of a KDBX file, one line each, read from the file itself.

    dump <file.kdbx> [<file.kdbx> ...]          one `path<TAB>value` line per place, after a
                                                `== <name>` line per file
    inject <in.kdbx> <out.kdbx> <variant> [<risky>]
                                                adds the places KeePassLib cannot write; with
                                                <risky> one of `hfield`, `ifield` or `eoh`, only
                                                that place, in a file of its own
    save <in.dump> <out.dump>                   per path: kept, changed (from, to), gone, added
    cell <open.dump> <arriving.dump> <result.dump>
                                                per path: what a merge result holds of the two

Every database here has the password `probe-only`; a file that does not open with it is tried
with `probe-other`, which `Kpl.exe rekey` sets. A dump names a group or an entry by the label of
its fixed identifier (`places`, `bin`, ...) or, for an object the probe did not make, by its name
or title, so a dump of a file another program wrote lines up with the base's. The paths are the
XML's own element names, keyed where an element repeats.

Needs `cryptography`, `argon2-cffi` and `lxml`; `kdbx_raw.py` beside it.
"""
from __future__ import annotations

import base64
import datetime as dt
import hashlib
import struct
import sys
import uuid as uuid_module
from pathlib import Path

from lxml import etree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kdbx_raw  # noqa: E402

PASSWORDS = ("probe-only", "probe-other")
LABELS = {f"4b50472d-0000-4000-8000-{n:012x}": name for n, name in {
    1: "places", 2: "entry", 3: "bin", 4: "bin2", 5: "tpl", 6: "tpl2", 7: "prev", 8: "other", 9: "root",
    0x11: "icon-used", 0x12: "icon-unused", 0x13: "icon-onlyA", 0x14: "icon-onlyB",
    0x21: "del-shared", 0x22: "del-onlyA", 0x23: "del-onlyB"}.items()}
TIME_TAGS = {"CreationTime", "LastModificationTime", "LastAccessTime", "ExpiryTime", "LocationChanged",
             "SettingsChanged", "DatabaseNameChanged", "DatabaseDescriptionChanged",
             "DefaultUserNameChanged", "MasterKeyChanged", "RecycleBinChanged",
             "EntryTemplatesGroupChanged", "DeletionTime"}
UUID_TAGS = {"UUID", "CustomIconUUID", "RecycleBinUUID", "EntryTemplatesGroup", "LastSelectedGroup",
             "LastTopVisibleGroup", "LastTopVisibleEntry", "PreviousParentGroup"}
HEADER_NAMES = {0: "EndOfHeader", 1: "Comment", 2: "CipherID", 3: "Compression", 4: "MasterSeed",
                5: "TransformSeed", 6: "TransformRounds", 7: "EncryptionIV", 8: "InnerRandomStreamKey",
                9: "StreamStartBytes", 10: "InnerRandomStreamID", 11: "KdfParameters",
                12: "PublicCustomData"}
# What is fresh at every save by design, so that `save` calls it rewritten rather than changed.
RANDOM = ("H.MasterSeed", "H.EncryptionIV", "H.kdf[S]", "I.InnerRandomStreamKey",
          "H.TransformSeed", "H.InnerRandomStreamKey", "H.StreamStartBytes")


def open_file(path: str) -> kdbx_raw.Kdbx:
    data = Path(path).read_bytes()
    last = None
    for pw in PASSWORDS:
        try:
            return kdbx_raw.load(data, pw)
        except kdbx_raw.KdbxError as e:
            last = e
            if "key" not in str(e):
                raise
    raise last


def kp_time(text: str) -> str:
    text = (text or "").strip()
    try:
        b = base64.b64decode(text, validate=True)
        if len(b) == 8:
            secs = struct.unpack("<q", b)[0]
            return (dt.datetime(1, 1, 1) + dt.timedelta(seconds=secs)).strftime("%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        pass
    return text


def kp_uuid(text: str) -> str:
    try:
        b = base64.b64decode((text or "").strip(), validate=True)
        if len(b) == 16:
            # The probe makes its identifiers from a .NET Guid's ToByteArray(), which is little
            # endian in its first three parts.
            u = str(uuid_module.UUID(bytes_le=b))
            return LABELS.get(u, u)
    except ValueError:
        pass
    return text or ""


def label_group(g) -> str:
    u = kp_uuid(g.findtext("UUID"))
    if u in LABELS.values():
        return u
    return "name=" + (g.findtext("Name") or "")


def label_entry(e) -> str:
    u = kp_uuid(e.findtext("UUID"))
    if u in LABELS.values():
        return u
    for s in e.findall("String"):
        if s.findtext("Key") == "Title":
            return "title=" + (s.findtext("Value") or "")
    return "uuid=" + u


class Dumper:
    def __init__(self, k: kdbx_raw.Kdbx):
        self.k = k
        self.lines: list[tuple[str, str]] = []
        self.seen: dict[str, int] = {}
        sid, skey = k.stream()
        self.root = etree.fromstring(k.xml, etree.XMLParser(remove_blank_text=False, resolve_entities=False))
        prot = [el for el in self.root.iter() if isinstance(el.tag, str) and el.get("Protected") == "True"]
        raw = [base64.b64decode((el.text or "").strip()) for el in prot]
        ks = kdbx_raw.key_stream(sid, skey, sum(len(r) for r in raw)) if raw else b""
        self.plain = {}
        pos = 0
        for el, r in zip(prot, raw):
            self.plain[el] = bytes(a ^ b for a, b in zip(r, ks[pos:pos + len(r)])).decode("utf-8", "replace")
            pos += len(r)

    def add(self, path: str, value: str) -> None:
        n = self.seen.get(path, 0)
        self.seen[path] = n + 1
        if n:
            path = f"{path}#{n + 1}"
        self.lines.append((path, value))

    def run(self) -> list[tuple[str, str]]:
        k = self.k
        self.add("H.version", f"{k.major}.{k.minor}")
        self.add("H.order", ",".join(str(f) for f, _ in k.fields))
        for fid, v in k.fields:
            name = HEADER_NAMES.get(fid, f"field{fid}")
            if fid in (11, 12):
                d = kdbx_raw.VarDict.parse(v)
                pre = "H.kdf" if fid == 11 else "H.pcd"
                self.add(f"{pre}.version", f"0x{d.version:04x}")
                for t, n, val in d.items:
                    shown = kdbx_raw.VarDict.show(t, val)
                    if t == 0x42 and n != "$UUID" and n != "S":
                        try:
                            shown += " text=" + kdbx_raw.short(val.decode("utf-8"))
                        except UnicodeDecodeError:
                            pass
                    if n == "$UUID":
                        shown = val.hex()
                    self.add(f"{pre}[{n}]", shown)
            elif fid in (2, 3, 10):
                self.add(f"H.{name}", v.hex())
            elif fid == 0:
                self.add("H.EndOfHeader", v.hex())
            elif fid in (4, 7, 8, 9, 5, 6):
                self.add(f"H.{name}", kdbx_raw.blob(v) if fid != 6 else str(struct.unpack('<Q', v)[0]))
            else:
                self.add(f"H.{name}", text_or_blob(v))
        refs: dict[int, int] = {}
        for b in self.root.iter("Binary"):
            v = b.find("Value")
            if v is not None and v.get("Ref") is not None:
                refs[int(v.get("Ref"))] = refs.get(int(v.get("Ref")), 0) + 1
        index = 0
        for fid, v in k.inner:
            if fid == 1:
                self.add("I.InnerRandomStreamID", v.hex())
            elif fid == 2:
                self.add("I.InnerRandomStreamKey", kdbx_raw.blob(v))
            elif fid == 3:
                # Named by the place its marker names, so that a pool a program reorders lines up.
                text = text_or_blob(v[1:])
                key = text.split(":")[1] if text.startswith("kpg:") and text.count(":") >= 2 else str(index)
                self.add(f"I.Binary[{key}]", f"{text} flags={v[0]} refs={refs.get(index, 0)}")
                index += 1
            elif fid == 0:
                self.add("I.EndOfHeader", v.hex() or "(empty)")
            else:
                self.add(f"I.field{fid}", text_or_blob(v))
        if k.trailing:
            self.add("T.trailing", text_or_blob(k.trailing))
        self.walk(self.root, "X")
        return self.lines

    def walk(self, el, path: str) -> None:
        for name, value in sorted(el.attrib.items()):
            if name not in ("Protected", "ProtectInMemory", "Ref", "ID", "Compressed"):
                self.add(f"{path}@{name}", value)
            elif name == "Protected" and el.tag == "Value":
                self.add(f"{path}@{name}", value)
            elif name == "ProtectInMemory":
                self.add(f"{path}@{name}", value)
        kids = [c for c in el if isinstance(c.tag, str)]
        for c in el:
            if not isinstance(c.tag, str):
                self.add(f"{path}/comment()", kdbx_raw.short(str(c.text)))
        if not kids:
            if el in self.plain:
                self.add(path, kdbx_raw.short(self.plain[el]))
            elif el.tag in TIME_TAGS:
                self.add(path, kp_time(el.text))
            elif el.tag in UUID_TAGS:
                self.add(path, kp_uuid(el.text))
            elif el.tag == "Data":
                b = base64.b64decode((el.text or "").strip() or b"")
                self.add(path, kdbx_raw.blob(b))
            elif el.tag == "Value" and el.getparent().tag == "Binary" and el.get("Ref") is not None:
                self.add(path, binary_name(self.k, int(el.get("Ref"))))
            else:
                self.add(path, kdbx_raw.short(el.text or ""))
            return
        for c in kids:
            self.walk(c, path + "/" + self.key(c))

    def key(self, c) -> str:
        tag = c.tag
        if tag == "Group":
            return f"Group[{label_group(c)}]"
        if tag == "Entry":
            if c.getparent().tag == "History":
                return f"Entry[{list(c.getparent()).index(c)}]"
            return f"Entry[{label_entry(c)}]"
        if tag in ("String", "Binary", "Item") and c.find("Key") is not None:
            return f"{tag}[{c.findtext('Key')}]"
        if tag == "Icon":
            return f"Icon[{kp_uuid(c.findtext('UUID'))}]"
        if tag == "DeletedObject":
            return f"DeletedObject[{kp_uuid(c.findtext('UUID'))}]"
        if tag == "Association":
            return f"Association[{[x for x in c.getparent() if x.tag == 'Association'].index(c)}]"
        return tag


def binary_name(k: kdbx_raw.Kdbx, ref: int) -> str:
    bins = [v for f, v in k.inner if f == 3]
    if ref < len(bins):
        return f"ref {ref} = {text_or_blob(bins[ref][1:])}"
    return f"ref {ref} (missing)"


def text_or_blob(v: bytes) -> str:
    try:
        s = v.decode("utf-8")
        if s.isprintable():
            return kdbx_raw.short(s)
    except UnicodeDecodeError:
        pass
    return kdbx_raw.blob(v)


def dump(paths: list[str]) -> None:
    for p in paths:
        print(f"== {Path(p).name}")
        try:
            for path, value in Dumper(open_file(p)).run():
                print(f"{path}\t{value}")
        except Exception as e:  # noqa: BLE001 - a file nothing can read is a result here
            print(f"UNREADABLE\t{type(e).__name__}: {e}")


# --- inject -----------------------------------------------------------------------------------

def b64time(t: dt.datetime) -> str:
    secs = int((t - dt.datetime(1, 1, 1)).total_seconds())
    return base64.b64encode(struct.pack("<q", secs)).decode()


TIMES = {"base": dt.datetime(2026, 1, 1), "A": dt.datetime(2026, 2, 1), "B": dt.datetime(2026, 3, 1)}


# The raw places, in groups that can be added one at a time; `inject` with no parts adds them all.
PARTS = ("comment", "unreferenced", "trailing", "generator", "xml-meta", "xml-other", "xml-empty", "attrs",
         "xml-comment", "node-cd-time")
META_X = ("meta", "memprot", "icon", "meta.cd-item")


def inject(src: str, dst: str, variant: str, risky: str | None = None, tie: bool = False,
           parts: tuple[str, ...] = ("comment", "unreferenced", "trailing", "generator", "xml-meta", "xml-other",
                                     "attrs", "xml-comment", "node-cd-time")) -> None:
    k = open_file(src)
    v = variant

    def val(place: str) -> str:
        return f"kpg:{place}:{v}"

    if risky == "hfield":
        k.fields.insert(len(k.fields) - 1, [13, val("H.field13").encode()])
    elif risky == "ifield":
        k.inner.insert(len(k.inner) - 1, [4, val("I.field4").encode()])
    elif risky == "eoh":
        k.fields[-1][1] = b"\r\n\r\n" + val("H.eoh").encode()
    elif risky:
        raise SystemExit(f"no risky place {risky}")
    else:
        if "comment" in parts:
            # The outer header's comment, right after the cipher, where KeePass 2.x once wrote it.
            k.fields.insert(1, [1, val("H.comment").encode()])
        if "unreferenced" in parts:
            # A binary no entry refers to, at the end of the pool so no reference moves.
            k.inner.insert(len(k.inner) - 1, [3, b"\x00" + val("I.unreferenced").encode()])
        if "trailing" in parts:
            k.trailing = val("T.trailing").encode()
        root = etree.fromstring(k.xml)
        t = TIMES["A"] if tie and v == "B" else TIMES[v]
        meta = root.find("Meta")
        root_element = root.find("Root")
        g = next(e for e in root.iter("Group") if kp_uuid(e.findtext("UUID")) == "places")
        e = next(e for e in g.findall("Entry") if kp_uuid(e.findtext("UUID")) == "entry")
        plain = next(s for s in e.findall("String") if s.findtext("Key") == "kpg-plain")
        spots = {
            "file": root, "meta": meta, "root": root_element, "memprot": meta.find("MemoryProtection"),
            "icon": meta.find("CustomIcons/Icon"), "meta.cd-item": meta.find("CustomData/Item"),
            "group": g, "group.times": g.find("Times"), "group.cd-item": g.find("CustomData/Item"),
            "entry": e, "entry.times": e.find("Times"), "entry.string": plain,
            "entry.binary": e.find("Binary"), "entry.autotype": e.find("AutoType"),
            "entry.association": e.find("AutoType/Association"), "entry.cd-item": e.find("CustomData/Item"),
            "entry.history": e.find("History/Entry"), "deleted": root_element.find("DeletedObjects/DeletedObject"),
        }
        for where, parent in spots.items():
            if parent is None:
                raise SystemExit(f"no element for {where}")
            if (where in META_X and "xml-meta" in parts) or (where not in META_X and "xml-other" in parts):
                etree.SubElement(parent, "KpgX").text = val("X." + where)
            elif "xml-empty" in parts:
                # An unknown element with nothing in it, which a reader may skip where it refuses text.
                etree.SubElement(parent, "KpgX")
        if "generator" in parts:
            meta.find("Generator").text = val("M.generator")
        if "attrs" in parts:
            # Attributes the format does not define, on elements it does.
            meta.set("kpgAttribute", val("XA.meta"))
            g.set("kpgAttribute", val("XA.group"))
            e.set("kpgAttribute", val("XA.entry"))
            plain.find("Value").set("kpgAttribute", val("XA.value"))
        if "xml-comment" in parts:
            # A comment, which is XML and no element.
            meta.append(etree.Comment(val("XC.meta")))
        if "node-cd-time" in parts:
            # A time on a node's custom data item, which the format defines for the database's only.
            for item in (g.find("CustomData/Item"), e.find("CustomData/Item")):
                etree.SubElement(item, "LastModificationTime").text = b64time(t)
        k.xml = etree.tostring(root, xml_declaration=True, encoding="utf-8", standalone=True)
    Path(dst).write_bytes(kdbx_raw.save(k))

# --- comparisons ------------------------------------------------------------------------------

def read_dump(path: str) -> dict[str, dict[str, str]]:
    files: dict[str, dict[str, str]] = {}
    cur = None
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if line.startswith("== "):
            cur = files.setdefault(line[3:], {})
        elif cur is not None and "\t" in line:
            p, v = line.split("\t", 1)
            cur[p] = v
    return files


def one(path: str) -> dict[str, str]:
    files = read_dump(path)
    if len(files) != 1:
        raise SystemExit(f"{path} holds {len(files)} files")
    return next(iter(files.values()))


def is_random(p: str) -> bool:
    return any(p == r or p.startswith(r + "#") for r in RANDOM)


def save_cmp(before: dict[str, str], after: dict[str, str]) -> list[str]:
    out = []
    if "UNREADABLE" in after:
        return [f"UNREADABLE\t{after['UNREADABLE']}"]
    for p in sorted(set(before) | set(after)):
        b, a = before.get(p), after.get(p)
        if b == a:
            out.append(f"{p}\tkept")
        elif a is None:
            out.append(f"{p}\tgone (was {b})")
        elif b is None:
            out.append(f"{p}\tadded ({a})")
        elif is_random(p):
            out.append(f"{p}\trewritten (fresh random)")
        else:
            out.append(f"{p}\tchanged ({b} -> {a})")
    return out


def cell_cmp(o: dict[str, str], a: dict[str, str], r: dict[str, str]) -> list[str]:
    out = []
    if "UNREADABLE" in r:
        return [f"UNREADABLE\t{r['UNREADABLE']}"]
    for p in sorted(set(o) | set(a) | set(r)):
        ov, av, rv = o.get(p), a.get(p), r.get(p)
        if is_random(p):
            continue
        if ov is not None and av is not None:
            if ov == av:
                res = "same" if rv == ov else ("dropped" if rv is None else f"rewritten ({rv})")
            elif rv == ov:
                res = "open"
            elif rv == av:
                res = "arriving"
            elif rv is None:
                res = "dropped"
            else:
                res = f"other ({rv})"
        elif ov is not None:
            res = "only-open kept" if rv == ov else ("only-open dropped" if rv is None else f"only-open rewritten ({rv})")
        elif av is not None:
            res = "only-arriving taken" if rv == av else ("only-arriving not taken" if rv is None else f"only-arriving rewritten ({rv})")
        else:
            res = f"new ({rv})"
        out.append(f"{p}\t{res}")
    return out


def main(argv: list[str]) -> None:
    cmd = argv[1]
    if cmd == "dump":
        dump(argv[2:])
    elif cmd == "inject":
        tie = "--tie" in argv
        args = [a for a in argv[2:] if a != "--tie"]
        parts = tuple(a[8:].split(",") for a in argv if a.startswith("--parts="))
        args = [a for a in args if not a.startswith("--parts=")]
        kw = {"parts": parts[0]} if parts else {}
        inject(args[0], args[1], args[2], args[3] if len(args) > 3 else None, tie, **kw)
    elif cmd == "save":
        print("\n".join(save_cmp(one(argv[2]), one(argv[3]))))
    elif cmd == "cell":
        print("\n".join(cell_cmp(one(argv[2]), one(argv[3]), one(argv[4]))))
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    main(sys.argv)
