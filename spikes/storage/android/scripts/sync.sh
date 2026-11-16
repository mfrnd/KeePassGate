#!/bin/sh
# Step 8, Windows side, Git Bash: KeePass's own Synchronize, headless (KeePassLib 2.62,
# PwDatabase.MergeIn with PwMergeMethod.Synchronize), between a file a phone program saved and the
# copy KeePass holds, in both directions:
#   into-kp     KeePass's copy open, the phone's file arriving
#   into-phone  the phone's file open, KeePass's copy arriving
# Each result is saved, read back by the probe and diffed against the base and against the phone's
# file. Results go to work/sync/<label>/.
#   sh sync.sh <label> <phone file> <KeePass-side copy> <base name for the diff>
M=$(cd "$(dirname "$0")/.." && pwd)
X="$M/probe/bin/Release/net48/StoreProbe.exe"
LABEL=$1; PHONE=$2; KP=$3; BASE=$4
O="$M/work/sync/$LABEL"; mkdir -p "$O"
w() { cygpath -w "$1"; }
"$X" sync "$(w "$KP")" "$(w "$PHONE")" "$(w "$O/into-kp.kdbx")" 2>/dev/null
"$X" sync "$(w "$PHONE")" "$(w "$KP")" "$(w "$O/into-phone.kdbx")" 2>/dev/null
"$X" dump "$(w "$PHONE")" "$(w "$O/phone.dump.txt")" 2>/dev/null >/dev/null
"$X" dump "$(w "$KP")" "$(w "$O/keepass.dump.txt")" 2>/dev/null >/dev/null
for r in into-kp into-phone; do
  "$X" dump "$(w "$O/$r.kdbx")" "$(w "$O/$r.dump.txt")" 2>/dev/null >/dev/null
  diff "$M/work/base/$BASE.kdbx.dump.txt" "$O/$r.dump.txt" > "$O/$r.vs-base.diff.txt"
  diff "$O/phone.dump.txt" "$O/$r.dump.txt" > "$O/$r.vs-phone.diff.txt"
  diff "$O/keepass.dump.txt" "$O/$r.dump.txt" > "$O/$r.vs-keepass.diff.txt"
  echo "== $LABEL $r: $(grep -c '^[<>]' "$O/$r.vs-base.diff.txt") lines differ from the base, $(grep -c '^[<>]' "$O/$r.vs-phone.diff.txt") from the phone's file, $(grep -c '^[<>]' "$O/$r.vs-keepass.diff.txt") from KeePass's copy"
done
