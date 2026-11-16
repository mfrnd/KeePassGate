#!/bin/sh
# Put a file onto the server as the desktop's synchronizer would: written beside the served file
# and renamed over it. Logs the served file's size and hash before and after.
#   sh srv-put.sh <served name> <file on this side>
. "$(dirname "$0")/env.sh"
F="$D/server/root/$1"
[ -f "$F" ] && echo "server $1 before: $(stat -c '%s bytes, mtime %y' "$F") sha256 $(sha256sum "$F" | cut -d' ' -f1)"
cp "$2" "$F.tmp-put" && mv -f "$F.tmp-put" "$F"
echo "server $1 after:  $(stat -c '%s bytes, mtime %y' "$F") sha256 $(sha256sum "$F" | cut -d' ' -f1) (from ${2##*/})"
