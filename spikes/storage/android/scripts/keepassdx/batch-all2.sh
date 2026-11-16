#!/bin/sh
# The overwrite runs that showed the dialog, repeated with the dialog handled in the editor; the
# write-protected runs; the save of the base with unknown elements; then batch-rest.
. "$(dirname "$0")/env.sh"
sh "$P/batch-ow.sh" "$P/batches/item3-redo.txt"
sh "$P/batch-rest.sh"
echo "### batch all2 done $(ts)" >> "$D/out/batch-rest.txt"
