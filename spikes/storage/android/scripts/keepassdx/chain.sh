#!/bin/sh
# Windows side, Git Bash: read back a chain run (files sNN-<operation>.kdbx pulled after each
# operation) and diff each against the file before it (the first against the base), so that every
# operation reads as an operation on its own. Writes sNN-<operation>.step.diff.txt beside each and
# prints the changed item lines, then the raw lines that changed (R lines, and X element paths that
# appear or disappear; X counts that only change are left out of the print, not of the diff file).
#   sh chain.sh <base name> <run dir>
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
BASE=$1; R=$2
"$X" dumpdir "$(cygpath -w "$R")" >/dev/null 2>&1
prev="$M/work/base/$BASE.kdbx.dump.txt"
for d in $(ls "$R"/s[0-9][0-9]-*.kdbx.dump.txt 2>/dev/null | sort); do
  out="${d%.kdbx.dump.txt}.step.diff.txt"
  diff <(grep -v '^# file' "$prev") <(grep -v '^# file' "$d") > "$out"
  echo "== $(basename "${d%.kdbx.dump.txt}"): $(grep -c '^[<>] [^RX]' "$out") item lines"
  grep '^[<>] [^RX]' "$out" | head -${LINES:-40}
  grep '^[<>] R' "$out"
  diff <(sed -n 's/^X \(.*\) [0-9]*$/\1/p' "$prev") <(sed -n 's/^X \(.*\) [0-9]*$/\1/p' "$d") | grep '^[<>]' | sed 's/^/  path /'
  prev="$d"
done
