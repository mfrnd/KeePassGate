#!/bin/sh
# Exploration, not counted: clear one program, push one base as /sdcard/Download/<name>.kdbx, run a
# flow, and log what it showed in out/explore/<tag>.log.
#   sh explore.sh <package> <base> <name> <flow> <tag> [VAR=value ...]
. "$(dirname "$0")/env.sh"
PKG=$1; DB=$2; NAME=$3; FLOW=$4; TAG=$5; shift 5
mkdir -p "$D/out/explore"
{
adb shell am force-stop $PKG
adb shell pm clear $PKG
adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow
adb shell 'rm -f /sdcard/Download/*'
adb push "$D/work/base/$DB.kdbx" "/sdcard/Download/$NAME.kdbx"
cd "$P/flows"
python3 "$P/overwrite/k.py" run "$P/flows/$FLOW" NAME="$NAME" PKG="$PKG" SRCDIR="$D/work/base" P="$P" "$@"
echo "### rc=$?"
} > "$D/out/explore/$TAG.log" 2>&1
tail -60 "$D/out/explore/$TAG.log"
