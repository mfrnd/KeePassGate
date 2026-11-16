#!/bin/sh
# The last runs: the background-sync save answered "Yes, merge", then the runs that need a PIN.
. "$(dirname "$0")/env.sh"
sh "$P/batch-sy.sh" "$P/batches-ow/sy-main5.txt"
sh "$P/run-pin.sh"
