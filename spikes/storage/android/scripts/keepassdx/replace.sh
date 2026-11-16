#!/bin/sh
# Put an arriving copy over the open file, as a synchronizer bringing KeePass's later save would:
# the same path written over (adb push), then the file's modification time set as the case names,
# relative to the time the replaced file had (its seconds and its nanoseconds):
#   equal    exactly the replaced file's time, nanoseconds included (touch -r through a reference)
#   plusN    the replaced file's time plus N seconds, nanoseconds kept (plus0 is equal)
#   earlier  the replaced file's time less 3600 s
#   later    the device's clock at the moment of the replacement
#   keep     whatever time adb push leaves (reported, not set)
# The size is the arriving copy's own: NEW-same is OLD's length to the byte, NEW-diff is not.
#   sh replace.sh <device path> <arriving file on this side> <mode>
. "$(dirname "$0")/env.sh"
DEV=$1; SRC=$2; MODE=$3
echo "replace: before $(adb shell stat -c "'%s bytes, mtime %y'" "$DEV" | tr -d '\r') sha256 $(adb shell sha256sum "$DEV" | cut -d' ' -f1)"
old=$(adb shell stat -c %Y "$DEV" | tr -d '\r')
ns=$(adb shell stat -c %y "$DEV" | tr -d '\r' | sed -n 's/^[^.]*\.\([0-9]*\).*/\1/p')
adb shell touch -r "$DEV" /data/local/tmp/ow-ref
adb push "$SRC" "$DEV" 2>&1 | tail -1
case "$MODE" in
  equal)   adb shell touch -r /data/local/tmp/ow-ref "$DEV" ;;
  plus*)   n=${MODE#plus}; adb shell touch -d "$(date -u -d @$((old + n)) +%Y-%m-%dT%H:%M:%S).${ns}Z" "$DEV" ;;
  earlier) adb shell touch -d "$(date -u -d @$((old - 3600)) +%Y-%m-%dT%H:%M:%S).${ns}Z" "$DEV" ;;
  later)   adb shell touch "$DEV" ;;
  keep)    ;;
  *) echo "replace: unknown mode $MODE"; exit 1 ;;
esac
echo "replace: after  $(adb shell stat -c "'%s bytes, mtime %y'" "$DEV" | tr -d '\r') sha256 $(adb shell sha256sum "$DEV" | cut -d' ' -f1) (mode $MODE, from ${SRC##*/})"
