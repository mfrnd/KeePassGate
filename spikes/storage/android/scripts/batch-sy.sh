#!/bin/sh
# Several run-sy.sh runs one after another, one a line: <flow> <tag> [VAR=value ...]. Summaries go to
# out/batch-sy-<name>.txt.
#   sh batch-sy.sh <batch file>
. "$(dirname "$0")/env.sh"
B=$1; S="$D/out/batch-sy-$(basename "$B" .txt).txt"
grep -v '^#' "$B" | while read -r line; do
  [ -n "$line" ] || continue
  # shellcheck disable=SC2086
  sh "$P/run-sy.sh" $line </dev/null >> "$S" 2>&1
  tail -1 "$S"
done
echo "### batch $(basename "$B") done $(ts)" >> "$S"
