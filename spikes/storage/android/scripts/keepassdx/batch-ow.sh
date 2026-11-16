#!/bin/sh
# Several run-ow.sh runs one after another, each given on a line of a batch file as
#   <prog> <flow> <package> <base db> <tag> [VAR=value ...]
# Lines starting with # are skipped. A summary line per run goes to out/batch-<name>.txt.
#   sh batch-ow.sh <batch file>
. "$(dirname "$0")/env.sh"
B=$1; S="$D/out/batch-$(basename "$B" .txt).txt"
grep -v '^#' "$B" | while read -r line; do
  [ -n "$line" ] || continue
  # shellcheck disable=SC2086
  sh "$P/run-ow.sh" $line </dev/null >> "$S" 2>&1
  tail -1 "$S"
done
echo "### batch $(basename "$B") done $(ts)" >> "$S"
