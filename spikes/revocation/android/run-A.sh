#!/bin/sh
# Scenario A for one program: per database, a fresh app state (pm clear), the base file pushed
# alone into /sdcard/Download as <prog>-<db>.kdbx, the program's flow run (open it, unlock, add one
# entry at the root, save), and the file pulled back to work/A/<prog>/<db>.kdbx. Every step logged
# in out/A/<prog>/<db>.log.
#   sh run-A.sh <prog> <flow file in flows> <package> "<db> ..."
. "$(dirname "$0")/env.sh"
PROG=$1; FLOW=$2; PKG=$3; DBS=$4
W="$D/work/A/$PROG"; L="$D/out/A/$PROG"
mkdir -p "$W" "$L"
for db in $DBS; do
  name="$PROG-$db"
  {
    echo "### $(ts) A $PROG $db ($(adb shell dumpsys package $PKG | grep -m1 versionName | tr -d ' \r'))"
    adb shell am force-stop $PKG
    adb shell pm clear $PKG
    adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow 2>&1
    adb shell 'rm -f /sdcard/Download/*.kdbx /sdcard/Download/*.kdbx.*'
    adb push "$D/work/base/$db.kdbx" "/sdcard/Download/$name.kdbx"
    echo "pushed sha256 $(adb shell sha256sum /sdcard/Download/$name.kdbx | cut -d' ' -f1)"
    python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$name" ENTRY="droid-added" PKG="$PKG"
    rc=$?
    sleep 2
    adb shell ls -l --full-time /sdcard/Download/
    echo "device sha256 $(adb shell sha256sum /sdcard/Download/$name.kdbx | cut -d' ' -f1)"
    rm -f "$W/$db.kdbx"
    adb pull "/sdcard/Download/$name.kdbx" "$W/$db.kdbx"
    adb shell am force-stop $PKG
    echo "### flow rc=$rc $(ts)"
  } > "$L/$db.log" 2>&1
  echo "$PROG $db: $(grep -c . "$L/$db.log") lines, $(grep '### flow rc=' "$L/$db.log"), $(grep -c FAILED "$L/$db.log") failures"
done
