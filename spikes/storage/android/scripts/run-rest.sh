#!/bin/sh
# The check-off reruns, then the QuickUnlock pair with a screen lock PIN set and taken away again.
. "$(dirname "$0")/env.sh"
sh "$P/batch-ow.sh" "$P/batches-ow/k2a-nocheck3.txt"
sh "$P/pin.sh" set
sh "$P/batch-ow.sh" "$P/batches-ow/k2a-qu.txt"
sh "$P/pin.sh" clear
