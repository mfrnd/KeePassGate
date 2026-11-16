#!/bin/sh
# Build this run's SNEW (the desktop's later save) from SOLD and the phone's cached copy as it is now
# (so that Q2 is an hour after the phone's edit of Q2 and Q3 at the phone's edit of Q3 exactly),
# and put it on the server over the run's file.
#   sh mksnew.sh <run dir> <served name>
. "$(dirname "$0")/env.sh"
W=$1
sh "$P/pull-cache.sh" "$W" phone-before-sync
X="$D/probe/bin/Release/net48/StoreProbe.exe"
"$X" newsync "$(wslpath -w "$D/sync/base/SOLD.kdbx")" "$(wslpath -w "$W/SNEW.kdbx")" "$(wslpath -w "$W/phone-before-sync.kdbx")" 2>&1 | grep -v '^KeePassLib'
sh "$P/srv-put.sh" "$2" "$W/SNEW.kdbx"
