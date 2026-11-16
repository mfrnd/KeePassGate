#!/bin/sh
# Windows side, Git Bash: read back every .kdbx under the run directories given (a dump beside each
# file, by the KeePassLib 2.62 probe), diff each dump against the dump of the file the program was
# given (the base named), with and without the raw census lines (R and X), and print a count.
#   sh collect.sh <base name> <run dir> [<run dir> ...]
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
BASE=$1; shift
B="$M/work/base/$BASE.kdbx.dump.txt"
for R in "$@"; do
  "$X" dumpdir "$(cygpath -w "$R")" >/dev/null 2>&1
  for d in "$R"/*.kdbx.dump.txt; do
    [ -f "$d" ] || continue
    diff <(grep -v '^# file' "$B") <(grep -v '^# file' "$d") > "${d%.dump.txt}.diff.txt"
    echo "== $(basename "$R")/$(basename "${d%.dump.txt}") against $BASE: $(grep -c '^[<>]' "${d%.dump.txt}.diff.txt") lines ($(grep -c '^[<>] [^RX]' "${d%.dump.txt}.diff.txt") outside the raw census)"
  done
done
