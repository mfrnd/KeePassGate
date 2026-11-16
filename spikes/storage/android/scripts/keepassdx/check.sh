#!/bin/sh
# The APKs held to their pins again, what the device has installed, the hash of the APK each
# installed package runs from, and the device's facts this measurement depends on.
#   sh check.sh
. "$(dirname "$0")/env.sh"
sh "$P/verify-apks.sh" > "$D/out/verify.txt" 2>&1; echo "verify-apks rc=$?"
grep -E '^===|MATCH|^Verifies|^package' "$D/out/verify.txt"
for p in com.kunzisoft.keepass.libre keepass2android.keepass2android com.android.keepass com.kunzisoft.keepass.free; do
  a=$(adb shell pm path $p | sed -n 's/^package://p' | tr -d '\r' | head -1)
  v=$(adb shell dumpsys package $p | grep -E '^ +versionName=|versionCode=' | tr -d '\r' | tr -s ' ' | head -2 | tr '\n' ' ')
  if [ -n "$a" ]; then echo "$p: $v installed $a sha256 $(adb shell sha256sum "$a" | cut -d' ' -f1)"; else echo "$p: not installed"; fi
done
echo "screen lock disabled: $(adb shell locksettings get-disabled | tr -d '\r')"
echo "time zone: $(adb shell getprop persist.sys.timezone | tr -d '\r'), auto time: $(adb shell settings get global auto_time | tr -d '\r')"
echo "device clock: $(adb shell date -u | tr -d '\r'), here: $(date -u)"
