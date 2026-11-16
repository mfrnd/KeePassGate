#!/bin/sh
# Log Keepass2Android's stored file-handling preferences at a named point of a flow (as root; the
# file is the program's own SharedPreferences). A key not listed has never been stored, and the
# program then reads its code default.
#   sh prefs.sh <label>
. "$(dirname "$0")/env.sh"
f=/data/data/keepass2android.keepass2android/shared_prefs/keepass2android.keepass2android_preferences.xml
echo "prefs $1: $(adb shell "grep -E 'CheckForFileChangesOnSave|AlwaysMergeOnConflict|SyncOfflineCacheInBackground|UseOfflineCache|UseFileTransactions' $f 2>/dev/null" | tr -d '\r' | tr -s ' ' | tr '\n' ' ')"
