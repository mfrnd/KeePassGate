#!/bin/sh
# One synchronize run of Keepass2Android, from cleared data: its data cleared, the server's
# directory emptied and SOLD put there as kpg-<tag>.kdbx, the flow run (it opens that file over
# WebDAV and does what the case says), then the server's file copied back and K2A's cache files
# pulled (as root) into work/sync/<tag>/, with every step logged in out/sync/<tag>.log.
#   sh run-sy.sh <flow> <tag> [VAR=value ...]
. "$(dirname "$0")/env.sh"
FLOW=$1; TAG=$2; shift 2
PKG=keepass2android.keepass2android
W="$D/work/sync/$TAG"; L="$D/out/sync"
rm -rf "$W"; mkdir -p "$W" "$L"
name="kpg-$TAG"
{
  echo "### $(ts) sync $TAG flow=$FLOW $* ($(adb shell dumpsys package $PKG | grep -m1 versionName | tr -d ' \r'))"
  adb shell am force-stop $PKG
  adb shell pm clear $PKG
  adb shell input keyevent KEYCODE_WAKEUP; adb shell wm dismiss-keyguard
  sh "$P/server.sh" thaw 2>/dev/null
  sh "$P/server.sh" start
  rm -f "$D"/server/root/*
  sh "$P/srv-put.sh" "$name.kdbx" "$D/sync/base/SOLD.kdbx"
  for a in "$@"; do case "$a" in PREFS=*) sh "$P/setprefs.sh" "${a#PREFS=}" ;; esac; done
  python3 "$P/k.py" run "$P/flows/$FLOW" NAME="$name" SNAME="$name.kdbx" PKG="$PKG" SRCDIR="$D/sync/base" W="$W" P="$P" "$@"
  rc=$?
  sleep 2
  sh "$P/server.sh" thaw 2>/dev/null
  sh "$P/server.sh" start
  sh "$P/srv-stat.sh" "$name.kdbx" end
  cp "$D/server/root/$name.kdbx" "$W/server.kdbx"
  sh "$P/pull-cache.sh" "$W" cache
  ls -la "$D/server/root/"
  adb shell am force-stop $PKG
  echo "### flow rc=$rc $(ts)"
} > "$L/$TAG.log" 2>&1
echo "sync $TAG: $(grep '### flow rc=' "$L/$TAG.log"), $(grep -c FAILED "$L/$TAG.log") failures, $(ls "$W" | tr '\n' ' ')"
