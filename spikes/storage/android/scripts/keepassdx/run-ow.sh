#!/bin/sh
# One overwrite run on one program, from cleared data: the program's data cleared (pm clear), OLD
# pushed alone into /sdcard/Download as <prog>-<tag>.kdbx with its file time set to the device's
# clock less 600 s (so that "later" can be the clock itself), the flow run (it opens the file, has
# replace.sh put an arriving copy over it at the time the case names, and does what the case says
# on the phone), and every file left in Download pulled back to work/runs/<prog>/<tag>/. Every step
# is logged in out/runs/<prog>/<tag>.log.
#   sh run-ow.sh <prog> <flow> <package> <base db> <tag> [VAR=value ...]
. "$(dirname "$0")/env.sh"
PROG=$1; FLOW=$2; PKG=$3; DB=$4; TAG=$5; shift 5
W="$D/work/runs/$PROG/$TAG"; L="$D/out/runs/$PROG"
rm -rf "$W"; mkdir -p "$W" "$L"
name="$PROG-$TAG"
{
  echo "### $(ts) ow $PROG $TAG flow=$FLOW base=$DB $* ($(adb shell dumpsys package $PKG | grep -m1 versionName | tr -d ' \r'))"
  adb shell am force-stop $PKG
  adb shell pm clear $PKG
  adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow 2>&1
  adb shell 'rm -f /sdcard/Download/* /data/local/tmp/ow-ref'
  adb push "$D/work/base/$DB.kdbx" "/sdcard/Download/$name.kdbx"
  now=$(adb shell date +%s | tr -d '\r')
  adb shell touch -d "$(date -u -d @$((now - 600)) +%Y-%m-%dT%H:%M:%S)Z" "/sdcard/Download/$name.kdbx"
  echo "given $(adb shell stat -c "'%s bytes, mtime %y'" /sdcard/Download/$name.kdbx | tr -d '\r') sha256 $(adb shell sha256sum /sdcard/Download/$name.kdbx | cut -d' ' -f1)"
  python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$name" PKG="$PKG" SRCDIR="$D/work/base" OUT="$W" P="$P" "$@"
  rc=$?
  sleep 2
  adb shell ls -l --full-time /sdcard/Download/
  for f in $(adb shell 'ls /sdcard/Download/' | tr -d '\r'); do
    echo "device $f sha256 $(adb shell sha256sum /sdcard/Download/$f | cut -d' ' -f1) $(adb shell stat -c "'%s bytes, mtime %y'" /sdcard/Download/$f | tr -d '\r')"
    adb pull "/sdcard/Download/$f" "$W/$f"
  done
  adb shell am force-stop $PKG
  echo "### flow rc=$rc $(ts)"
} > "$L/$TAG.log" 2>&1
echo "$PROG $TAG: $(grep '### flow rc=' "$L/$TAG.log"), $(grep -c FAILED "$L/$TAG.log") failures, $(ls "$W" | tr '\n' ' ')"
