#!/bin/sh
# Windows side, Git Bash: read back every .kdbx under the directories given (a dump beside each,
# by the KeePassLib 2.62 probe), diff each dump against SOLD's and SNEW's (the run's own SNEW when
# the run directory holds one, else SNEW-fixed), and print one line a file naming, for every item
# of the synchronize pair, what the file holds:
#   KG3 NewKey DZ X1   the desktop's additions: a write-once entry, a database custom data key, a
#                      deletion record naming no entry, a deletion of an entry the phone left alone
#   P1 P2 Q1 Q2 Q3     the current password, and the other sides' values found in history
#   NP                 the phone's new entry
#   sh sverdict.sh <dir> [<dir> ...]
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
B="$M/sync/base"
for R in "$@"; do
  "$X" dumpdir "$(cygpath -w "$R")" >/dev/null 2>&1
  new="$B/SNEW-fixed.kdbx.dump.txt"; [ -f "$R/SNEW.kdbx.dump.txt" ] && new="$R/SNEW.kdbx.dump.txt"
  for d in "$R"/*.kdbx.dump.txt; do
    [ -f "$d" ] || continue
    n=$(basename "${d%.kdbx.dump.txt}")
    if grep -q '^# READ FAILED' "$d"; then echo "$(basename "$R")/$n: READ FAILED"; continue; fi
    sha=$(sed -n '1s/.*sha256=//p' "$d")
    has() { grep -q "$1" "$d" && echo "$2" || echo "$3"; }
    pw() {
      cur=$(sed -n "s/^E $1 S Password p=. \"\\(.*\\)\"\$/\\1/p" "$d")
      [ -n "$cur" ] || { grep -q "^E $1 parent" "$d" && cur='(empty)' || cur='(no entry)'; }
      hist=$(sed -n "s/^E $1 H[^ ]* S Password p=. \"\\(.*\\)\"\$/\\1/p" "$d" | grep -v -- "-old\$" | grep -v -x -- "$cur" | sort -u | tr '\n' '+' | sed 's/+$//')
      [ -n "$hist" ] && echo "$1=$cur[h:$hist]" || echo "$1=$cur"
    }
    kg="KG1$(has '^E KG1 parent' '' '-MISSING') KG2$(has '^E KG2 parent' '' '-MISSING') KG3:$(has '^E KG3 parent' yes NO)"
    nk="NewKey:$(has '^M CD KPG.NewKey' yes NO)"
    dz="DZ:$(has '^D DZ ' yes NO)"
    x1="X1:$(has '^E X1 parent' there gone)/$(has '^D X1 ' rec norec)"
    np="NP:$(grep -q 'S Title p=. "phone-new' "$d" && echo yes || echo NO)"
    same=""
    for b in "$B/SOLD.kdbx.dump.txt" "$new"; do
      [ "$sha" = "$(sed -n '1s/.*sha256=//p' "$b")" ] && same=" =$(basename "${b%.kdbx.dump.txt}")"
    done
    [ -f "$R/SNEW.kdbx.dump.txt" ] && [ "$sha" = "$(sed -n '1s/.*sha256=//p' "$R/SNEW.kdbx.dump.txt")" ] && same=" =SNEW"
    diff "$B/SOLD.kdbx.dump.txt" "$d" > "${d%.dump.txt}.vs-SOLD.diff.txt"
    diff "$new" "$d" > "${d%.dump.txt}.vs-SNEW.diff.txt"
    echo "$(basename "$R")/$n: $kg $nk $dz $x1 | $(pw P1) $(pw P2) $(pw Q1) $(pw Q2) $(pw Q3) $(pw K) | $np | ${sha%"${sha#????????????}"}$same"
  done
done
