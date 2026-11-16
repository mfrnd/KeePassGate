#!/bin/sh
# Windows side, Git Bash: read back every .kdbx under the run directories given (a dump beside each
# file, by the KeePassLib 2.62 probe), diff each dump against NEW-diff's and OLD's, and print one
# line a file saying whether NEW's three changes are in it and whether the phone's own change is:
#   RV      the entry KPG revocation in the KPG group (stands for a revocation record)
#   M1      KPG.base: v1 is KeePass's later value, v0 the older one; "v1 in history" if only there
#   X1      deleted with its record (NEW) or still there (OLD)
#   phone   titles of entries the phone added or edited (phone-...)
#   =       which given file the bytes are exactly, if any
#   sh verdict.sh <run dir> [<run dir> ...]
M=$(cd "$(dirname "$0")/.." && pwd)
X="$M/probe/overwrite/bin/Release/net48/StoreProbe.exe"
B="$M/work/base"
for R in "$@"; do
  "$X" dumpdir "$(cygpath -w "$R")" >/dev/null 2>&1
  for d in "$R"/*.kdbx.dump.txt; do
    [ -f "$d" ] || continue
    sha=$(sed -n '1s/.*sha256=//p' "$d")
    same=""
    for b in OLD NEW-same NEW-diff; do
      [ "$sha" = "$(sed -n '1s/.*sha256=//p' "$B/$b.kdbx.dump.txt")" ] && same=" =$b"
    done
    if grep -q '^# READ FAILED' "$d"; then echo "$(basename "$R")/$(basename "${d%.dump.txt}"): READ FAILED"; continue; fi
    rv=$(grep -q '^E RV parent' "$d" && echo present || echo ABSENT)
    m1=$(sed -n 's/^E M1 S KPG.base p=0 //p' "$d" | grep -q '"v\\":1' && echo v1 || echo v0)
    [ "$m1" = v0 ] && grep -q '^E M1 H.* S KPG.base p=0 .*v\\":1' "$d" && m1="v0 (v1 in history)"
    x1=$(grep -q '^E X1 parent' "$d" && echo "X1 there" || echo "X1 gone")
    dx=$(grep -q '^D X1 ' "$d" && echo "record" || echo "no record")
    ph=$(grep -E '^E [^ ]+ S Title p=. "phone-' "$d" | sed 's/.*"\(phone-[^"]*\)"/\1/' | sort -u | tr '\n' ',' | sed 's/,$//')
    v=$(sed -n 's/^F version //p' "$d")
    if [ "$rv" = present ] && [ "$m1" = v1 ] && [ "$x1" = "X1 gone" ] && [ "$dx" = record ]; then k="NEW's changes KEPT"
    elif [ "$rv" = ABSENT ] && [ "$x1" = "X1 there" ] && [ "$dx" = "no record" ]; then k="NEW's changes LOST"
    else k="NEW's changes PARTLY"; fi
    diff "$B/NEW-diff.kdbx.dump.txt" "$d" > "${d%.dump.txt}.vs-NEW.diff.txt"
    diff "$B/OLD.kdbx.dump.txt" "$d" > "${d%.dump.txt}.vs-OLD.diff.txt"
    echo "$(basename "$R")/$(basename "${d%.dump.txt}"): $k | RV $rv, M1 $m1, $x1, X1 $dx | phone: ${ph:-none} | v$v ${sha%"${sha#????????????}"}$same"
  done
done
