#!/bin/sh
# k.py from the flows directory with env.sh's environment; for working out a screen by hand.
#   sh kk.sh <command> [args]   or   sh kk.sh run <flow> VAR=value ...
. "$(dirname "$0")/env.sh"
cd "$P/flows"
python3 "$P/k.py" "$@"
