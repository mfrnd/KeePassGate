#!/bin/sh
# Copy the server's file into a run directory under a given name.
#   sh keep.sh <run dir> <served name> <file name>
. "$(dirname "$0")/env.sh"
cp "$D/server/root/$2" "$1/$3" && echo "kept $3 sha256 $(sha256sum "$1/$3" | cut -d' ' -f1)"
