#!/bin/sh
# For working out a screen by hand: clear the program's data, push one base alone as
# /sdcard/Download/<prog>-explore.kdbx, and run an open flow, leaving the program open.
#   sh prep.sh <prog> <package> <base db> <open flow>
. "$(dirname "$0")/env.sh"
PROG=$1; PKG=$2; DB=$3; FLOW=$4
adb shell am force-stop $PKG
adb shell pm clear $PKG
adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow
adb shell 'rm -f /sdcard/Download/*'
adb push "$D/work/base/$DB.kdbx" "/sdcard/Download/$PROG-explore.kdbx"
cd "$P/flows"
python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$PROG-explore" PKG="$PKG" 2>&1 | grep -E '^\[' | tail -5
