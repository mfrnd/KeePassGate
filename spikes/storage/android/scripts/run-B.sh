#!/bin/sh
# Scenario B for one program: per run, a fresh app state (pm clear), the target alone in
# /sdcard/Download as <prog>-<tag>.kdbx, then the program's flow, which is handed SRC (the source
# file on this side) and pushes it itself where the flow needs it (over the open file, or beside
# it). Afterwards every .kdbx in Download is pulled to work/B/<prog>/<tag>-r<run>/. Logs in
# out/B/<prog>/<tag>-r<run>.log.
#   sh run-B.sh <prog> <flow> <package> <target db> <source db> <tag> "<runs>"
. "$(dirname "$0")/env.sh"
PROG=$1; FLOW=$2; PKG=$3; TGT=$4; SRC=$5; TAG=$6; RUNS=${7:-1 2 3}
L="$D/out/B/$PROG"; mkdir -p "$L"
for r in $RUNS; do
  name="$PROG-$TAG"; W="$D/work/B/$PROG/$TAG-r$r"
  rm -rf "$W"; mkdir -p "$W"
  {
    echo "### $(ts) B $PROG $TAG r$r target=$TGT source=$SRC ($(adb shell dumpsys package $PKG | grep -m1 versionName | tr -d ' \r'))"
    adb shell am force-stop $PKG
    adb shell pm clear $PKG
    adb shell 'rm -f /sdcard/Download/*.kdbx /sdcard/Download/*.kdbx.*'
    adb push "$D/work/base/$TGT.kdbx" "/sdcard/Download/$name.kdbx"
    echo "target sha256 $(adb shell sha256sum /sdcard/Download/$name.kdbx | cut -d' ' -f1)"
    python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$name" SRC="$D/work/base/$SRC.kdbx" SRCNAME="$PROG-$TAG-src" ENTRY="droid-merge" PKG="$PKG"
    rc=$?
    sleep 2
    adb shell ls -l --full-time /sdcard/Download/
    for f in $(adb shell 'ls /sdcard/Download/' | tr -d '\r'); do
      echo "device $f sha256 $(adb shell sha256sum /sdcard/Download/$f | cut -d' ' -f1)"
      adb pull "/sdcard/Download/$f" "$W/$f"
    done
    adb shell am force-stop $PKG
    echo "### flow rc=$rc $(ts)"
  } > "$L/$TAG-r$r.log" 2>&1
  echo "$PROG $TAG r$r: $(grep '### flow rc=' "$L/$TAG-r$r.log"), $(grep -c FAILED "$L/$TAG-r$r.log") failures, $(ls "$W" | tr '\n' ' ')"
done
