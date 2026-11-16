#!/bin/sh
# The runs that need a screen lock (QuickUnlock): the PIN set, the overwrite pair and the
# synchronize case 9, the PIN taken away again.
. "$(dirname "$0")/env.sh"
sh "$P/pin.sh" set
sh "$P/batch-ow.sh" "$P/batches-ow/k2a-qu.txt"
sh "$P/batch-sy.sh" "$P/batches-ow/sy-pin.txt"
sh "$P/pin.sh" clear
