#!/bin/sh
# One operation on one program, from cleared data: the program's data cleared (pm clear), the base
# database pushed alone into /sdcard/Download as <prog>-<tag>.kdbx, the flow run (it opens the
# file and does the operation by the program's own controls), and every file left in Download
# pulled back to work/runs/<prog>/<tag>/. Every step is logged in out/runs/<prog>/<tag>.log. Extra
# NAME=value arguments are handed to the flow beside NAME, PKG, SRC and SRCNAME.
#   sh run-op.sh <prog> <flow> <package> <base db> <tag> [VAR=value ...]
. "$(dirname "$0")/env.sh"
PROG=$1; FLOW=$2; PKG=$3; DB=$4; TAG=$5; shift 5
W="$D/work/runs/$PROG/$TAG"; L="$D/out/runs/$PROG"
rm -rf "$W"; mkdir -p "$W" "$L"
name="$PROG-$TAG"
{
  echo "### $(ts) op $PROG $TAG flow=$FLOW base=$DB ($(adb shell dumpsys package $PKG | grep -m1 versionName | tr -d ' \r'))"
  adb shell am force-stop $PKG
  adb shell pm clear $PKG
  adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow 2>&1
  adb shell 'rm -f /sdcard/Download/*'
  adb push "$D/work/base/$DB.kdbx" "/sdcard/Download/$name.kdbx"
  echo "pushed sha256 $(adb shell sha256sum /sdcard/Download/$name.kdbx | cut -d' ' -f1)"
  python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$name" PKG="$PKG" SRCDIR="$D/work/base" OUT="$W" "$@"
  rc=$?
  sleep 2
  adb shell ls -l --full-time /sdcard/Download/
  for f in $(adb shell 'ls /sdcard/Download/' | tr -d '\r'); do
    echo "device $f sha256 $(adb shell sha256sum /sdcard/Download/$f | cut -d' ' -f1)"
    adb pull "/sdcard/Download/$f" "$W/$f"
  done
  adb shell am force-stop $PKG
  echo "### flow rc=$rc $(ts)"
} > "$L/$TAG.log" 2>&1
echo "$PROG $TAG: $(grep '### flow rc=' "$L/$TAG.log"), $(grep -c FAILED "$L/$TAG.log") failures, $(ls "$W" | tr '\n' ' ')"
