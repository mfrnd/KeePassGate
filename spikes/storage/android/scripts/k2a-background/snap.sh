#!/bin/sh
# Keep the server's file and Keepass2Android's cache at a named point of a run: the served file
# copied to <run dir>/server-<label>.kdbx, the cache pulled as cache-<label>.kdbx (pull-cache.sh).
#   sh snap.sh <run dir> <served name> <label>
. "$(dirname "$0")/env.sh"
cp "$D/server/root/$2" "$1/server-$3.kdbx" && echo "kept server-$3.kdbx sha256 $(sha256sum "$1/server-$3.kdbx" | cut -d' ' -f1)"
sh "$P/pull-cache.sh" "$1" "cache-$3" | grep -v 'local backup'
rm -f "$1/cache-$3-localbackup.kdbx"
