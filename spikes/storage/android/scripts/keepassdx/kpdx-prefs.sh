#!/bin/sh
# Write KeePassDX's stored preferences before it starts (as root, after pm clear), or show them.
#   sh kpdx-prefs.sh autosave-off     enable_auto_save_database_key false (the default is true)
#   sh kpdx-prefs.sh show [label]
. "$(dirname "$0")/env.sh"
PKG=com.kunzisoft.keepass.libre
F=/data/data/$PKG/shared_prefs/${PKG}_preferences.xml
case "$1" in
  autosave-off)
    u=$(adb shell stat -c %u /data/data/$PKG | tr -d '\r')
    adb shell "mkdir -p /data/data/$PKG/shared_prefs && printf '%s\n' \"<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\" '<map>' '    <boolean name=\"enable_auto_save_database_key\" value=\"false\" />' '</map>' > $F && chown $u:$u /data/data/$PKG/shared_prefs $F && chmod 660 $F && chmod 771 /data/data/$PKG/shared_prefs && restorecon -R /data/data/$PKG/shared_prefs"
    echo "prefs written: $(adb shell cat $F | tr -d '\r' | tr '\n' ' ')"
    ;;
  show)
    echo "prefs $2: $(adb shell "grep -E 'auto_save|read_only|timeout|device_credential|biometric' $F 2>/dev/null" | tr -d '\r' | tr -s ' ' | tr '\n' ' ')"
    ;;
esac
