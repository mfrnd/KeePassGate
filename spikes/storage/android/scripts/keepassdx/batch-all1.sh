#!/bin/sh
# Items 2 (repeats), 4, 3, 1 (saves), then 5 and 6, one after another.
. "$(dirname "$0")/env.sh"
sh "$P/batch-big.sh"
sh "$P/batch-item56.sh"
echo "### batch all1 done $(ts)" >> "$D/out/batch-item2b.txt"
