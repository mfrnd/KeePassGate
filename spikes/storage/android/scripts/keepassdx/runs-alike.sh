#!/bin/sh
# Windows side, Git Bash: whether the runs of one cell read back alike. Each run's dump is taken
# without its first line (the file's name and hash) and without the lines of entries the run itself
# added (identifiers the base does not fix, printed as ~...), and is compared with run 1's.
#   sh runs-alike.sh <dump of run 1> <dump of run 2> ...
norm() { grep -v '^# file' "$1" | grep -v '^E ~'; }
first=$1; shift
for d in "$@"; do
  n=$(diff <(norm "$first") <(norm "$d") | grep -c '^[<>]')
  echo "$(basename "$(dirname "$d")")/$(basename "$d") against $(basename "$(dirname "$first")")/$(basename "$first"): $n lines differ"
  [ "$n" -gt 0 ] && diff <(norm "$first") <(norm "$d") | grep '^[<>]' | head -12
done
