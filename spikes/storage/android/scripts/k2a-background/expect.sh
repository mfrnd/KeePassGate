#!/bin/sh
# Windows side, Git Bash: what KeePass 2.62's own Synchronize makes of the same two copies, for a
# run where both sides changed: the phone's copy before the synchronize (phone-before-sync.kdbx)
# and the desktop's (SNEW.kdbx), in both directions (phone open with SNEW arriving, and SNEW open
# with the phone's arriving), each read back with sverdict and diffed against the server's file.
#   sh expect.sh <run dir>
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
R=$1; E="$R/keepass-sync"; mkdir -p "$E"
w() { cygpath -w "$1"; }
"$X" sync "$(w "$R/phone-before-sync.kdbx")" "$(w "$R/SNEW.kdbx")" "$(w "$E/kp-phone-open.kdbx")" >/dev/null 2>&1
"$X" sync "$(w "$R/SNEW.kdbx")" "$(w "$R/phone-before-sync.kdbx")" "$(w "$E/kp-snew-open.kdbx")" >/dev/null 2>&1
cp "$R/SNEW.kdbx" "$E/SNEW.kdbx"
sh "$M/scripts/sverdict.sh" "$E" | grep -v '/SNEW:'
for k in kp-phone-open kp-snew-open; do
  n=$(diff <(grep -v '^# file\|t.accessed\|^M CD' "$E/$k.kdbx.dump.txt") <(grep -v '^# file\|t.accessed\|^M CD' "$R/server.kdbx.dump.txt") | grep -c '^[<>]')
  echo "$k against the server's file: $n lines differ (access times and database custom data times left out)"
done
