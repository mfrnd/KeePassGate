#!/bin/sh
# For working out a screen by hand: clear KeePassDX's data, push one base alone as
# /sdcard/Download/kpdx-explore.kdbx, and run a flow (output to the terminal, list lines kept).
#   sh xrun.sh <base db> <flow> [VAR=value ...]
. "$(dirname "$0")/env.sh"
PKG=com.kunzisoft.keepass.libre
DB=$1; FLOW=$2; shift 2
adb shell am force-stop $PKG
adb shell pm clear $PKG >/dev/null
adb shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow
adb shell 'rm -f /sdcard/Download/*'
adb push "$D/work/base/$DB.kdbx" "/sdcard/Download/kpdx-explore.kdbx" >/dev/null
cd "$P/flows"
python3 "$P/k.py" run "$P/flows/$FLOW" NAME=kpdx-explore PKG=$PKG P="$P" SRCDIR="$D/work/base" OUT="$D/out/explore" "$@" 2>&1
