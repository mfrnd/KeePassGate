#!/bin/sh
# Windows side, Git Bash: KeePass's own Synchronize, headless (KeePassLib 2.62,
# PwDatabase.MergeIn with PwMergeMethod.Synchronize), between a file a phone program saved and the
# copy KeePass holds, in both directions:
#   into-kp     KeePass's copy open, the phone's file arriving
#   into-phone  the phone's file open, KeePass's copy arriving
# and KeePassLib's own open and save of the phone's file (roundtrip). Each result is saved, read
# back by the probe and diffed against the base, the phone's file and KeePass's copy. Results go to
# work/sync/<label>/.
#   sh sync.sh <label> <phone file> <KeePass-side copy> <base dump>
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
LABEL=$1; PHONE=$2; KP=$3; BASE=$4
O="$M/work/sync/$LABEL"; rm -rf "$O"; mkdir -p "$O"
w() { cygpath -w "$1"; }
"$X" sync "$(w "$KP")" "$(w "$PHONE")" "$(w "$O/into-kp.kdbx")" 2>/dev/null >/dev/null
"$X" sync "$(w "$PHONE")" "$(w "$KP")" "$(w "$O/into-phone.kdbx")" 2>/dev/null >/dev/null
"$X" roundtrip "$(w "$PHONE")" "$(w "$O/roundtrip.kdbx")" 2>/dev/null >/dev/null
"$X" dump "$(w "$PHONE")" "$(w "$O/phone.dump.txt")" 2>/dev/null >/dev/null
"$X" dump "$(w "$KP")" "$(w "$O/keepass.dump.txt")" 2>/dev/null >/dev/null
echo "$LABEL phone=$(basename "$PHONE") keepass=$(basename "$KP") base=$(basename "$BASE")" > "$O/inputs.txt"
# R/X lines (the raw census) describe each writer's own XML and differ by writer, not by content;
# the item diffs below leave them out, and the raw diff keeps them.
norm() { grep -v '^# file' "$1" | grep -v '^[RX] '; }
for r in roundtrip into-kp into-phone; do
  "$X" dump "$(w "$O/$r.kdbx")" "$(w "$O/$r.dump.txt")" 2>/dev/null >/dev/null
  diff <(norm "$BASE") <(norm "$O/$r.dump.txt") > "$O/$r.vs-base.diff.txt"
  diff <(norm "$O/phone.dump.txt") <(norm "$O/$r.dump.txt") > "$O/$r.vs-phone.diff.txt"
  diff <(norm "$O/keepass.dump.txt") <(norm "$O/$r.dump.txt") > "$O/$r.vs-keepass.diff.txt"
  echo "== $LABEL $r: $(grep -c '^[<>]' "$O/$r.vs-base.diff.txt") lines differ from the base, $(grep -c '^[<>]' "$O/$r.vs-phone.diff.txt") from the phone's file, $(grep -c '^[<>]' "$O/$r.vs-keepass.diff.txt") from KeePass's copy"
done
