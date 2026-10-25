# The pykeepass probe

Two measurements, both against the installed library rather than against its documentation.

## What `pykeepass` requires

```powershell
uv run --no-project --with pykeepass --with pytest python -m pytest test_pykeepass.py -q
```

Four claims, each one a thing `RESULTS.md` depends on: that a `PyKeePass` is constructed from a file
and a password and from nothing else, that a database can be synthesized from nothing, that `save`
writes to the file the object was opened from, and that a synthesized database is protected by
whatever password the synthesizer chose.

The third and fourth are the ones that decide the question. Together they say that handing a user a
synthesized `PyKeePass` would give them an object whose edits go to a copy and whose protection is a
secret this side invented.

## What its surface maps onto

```powershell
uv run --no-project --with pykeepass python surface.py
```

`surface.json` classifies every public member of `PyKeePass`, `Entry`, `Group` and `Attachment` as
one of four things: **served** by routes the gate already has, with the meaning pykeepass gives it;
**served-differently**, where the gate has the operation but it means something else, so a facade
must not offer it under that name; **needs-routes** because nothing in the design forbids it and
the gate does not serve it yet; or **refused** because it needs the file, the key, the XML
document, or an operation the design deliberately does not offer.

The probe reads the library's own `dir()` and reports anything the table does not cover, so a
member added or removed upstream fails the run rather than going unnoticed. It exits non-zero when
the table and the library disagree.
