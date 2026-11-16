#!/bin/sh
# One k.py command (or "run <flow> VAR=value ...") with the environment env.sh sets, for working
# out a screen by hand before a flow is written.
#   sh k.sh <command> [args]
. "$(dirname "$0")/env.sh"
cd "$P/flows"
exec python3 "$P/k.py" "$@"
