#!/bin/sh
# Write Keepass2Android's preferences file (as root, after pm clear, before the program starts), so
# that a run starts from a stated set of preferences:
#   A   only "Allow cleartext HTTP" (permit_cleartext_traffic) on, which the HTTP server needs; every
#       other preference unstored, so the program reads its code defaults (Synchronize in background
#       false, Check for modifications true, Always merge on conflict false, Database caching true)
#   B   what showing the File handling screen stores, measured (measured-overwrite.md 2.5), with
#       cleartext HTTP on: UseOfflineCache, SyncOfflineCacheInBackground_key, CheckForFileChangesOnSave
#       and UseFileTransactions true, AlwaysMergeOnConflict false
#   sh setprefs.sh A|B
. "$(dirname "$0")/env.sh"
PK=keepass2android.keepass2android
DIR=/data/data/$PK/shared_prefs; F=$DIR/${PK}_preferences.xml
case "$1" in
  A) body='    <boolean name="permit_cleartext_traffic" value="true" />' ;;
  B) body='    <boolean name="permit_cleartext_traffic" value="true" />
    <boolean name="UseOfflineCache" value="true" />
    <boolean name="SyncOfflineCacheInBackground_key" value="true" />
    <boolean name="CheckForFileChangesOnSave" value="true" />
    <boolean name="UseFileTransactions" value="true" />
    <boolean name="AlwaysMergeOnConflict" value="false" />' ;;
  *) echo "setprefs: A or B"; exit 1 ;;
esac
T=$(mktemp)
printf "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n<map>\n%s\n</map>\n" "$body" > "$T"
uid=$(adb shell stat -c %u /data/data/$PK | tr -d '\r')
adb shell mkdir -p $DIR
adb push "$T" $F >/dev/null 2>&1
adb shell chown $uid:$uid $DIR $F
adb shell chmod 771 $DIR
adb shell chmod 660 $F
adb shell restorecon -R $DIR
rm -f "$T"
echo "prefs set $1: $(adb shell cat $F | tr -d '\r' | grep boolean | tr -s ' ' | tr '\n' ' ')"
