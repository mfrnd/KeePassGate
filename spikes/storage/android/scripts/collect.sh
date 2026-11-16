#!/bin/sh
# Windows side, Git Bash: read back every .kdbx under one run directory with the KeePassLib 2.62
# probe (a dump beside each file) and diff each dump against the dump of the base it was given,
# writing <file>.diff.txt beside it. The first line of a dump names the file and its hash, so it
# always differs; every other line that differs is a change.
#   sh collect.sh <run dir> <base name, e.g. S-argon>
M=$(cd "$(dirname "$0")/.." && pwd)
X="$M/probe/bin/Release/net48/StoreProbe.exe"
R=$1; BASE=$2
"$X" dumpdir "$(cygpath -w "$R")" 2>/dev/null >/dev/null
for d in "$R"/*.kdbx.dump.txt; do
  [ -f "$d" ] || continue
  diff "$M/work/base/$BASE.kdbx.dump.txt" "$d" > "${d%.dump.txt}.diff.txt"
  echo "== ${d#$M/} : $(grep -c '^[<>]' "${d%.dump.txt}.diff.txt") changed lines"
done
