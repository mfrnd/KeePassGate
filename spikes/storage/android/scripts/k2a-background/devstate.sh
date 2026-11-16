#!/bin/sh
# Log the device's screen state, the program's process and its foreground service at a named point.
#   sh devstate.sh <label>
. "$(dirname "$0")/env.sh"
echo "device $1: $(adb shell dumpsys power | tr -d '\r' | grep -m1 -o 'mWakefulness=[A-Za-z]*'), pid $(adb shell pidof keepass2android.keepass2android | tr -d '\r'), top $(adb shell dumpsys activity activities | tr -d '\r' | grep -m1 -o 'mResumedActivity: [^ ]* [^ ]* [^ ]*'), service $(adb shell dumpsys activity services keepass2android.keepass2android | tr -d '\r' | grep -o 'BackgroundSyncService[^ ]*' | head -1)"
