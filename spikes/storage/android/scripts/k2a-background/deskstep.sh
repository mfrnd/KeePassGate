#!/bin/sh
# One of the desktop's turns (probe deskstep a, b or c) on the server's file as it is now: the file
# copied, the step applied by KeePassLib 2.62, and the result put back on the server written beside
# the served file and renamed over it (srv-put.sh). Step c reads the phone's edit times from its
# cached copy, pulled first. The server's process need not run: the file is changed on its disk.
#   sh deskstep.sh <run dir> <served name> a|b|c
. "$(dirname "$0")/env.sh"
W=$1; S=$2; T=$3
X="$D/probe/bin/Release/net48/StoreProbe.exe"
cp "$D/server/root/$S" "$W/desk-$T-in.kdbx"
ph=
if [ "$T" = c ]; then sh "$P/pull-cache.sh" "$W" "phone-before-c" | grep -v 'local backup'; rm -f "$W/phone-before-c-localbackup.kdbx"; ph=$(wslpath -w "$W/phone-before-c.kdbx"); fi
"$X" deskstep "$(wslpath -w "$W/desk-$T-in.kdbx")" "$(wslpath -w "$W/desk-$T.kdbx")" "$T" $ph 2>&1 | grep -v '^KeePassLib'
sh "$P/srv-put.sh" "$S" "$W/desk-$T.kdbx"
