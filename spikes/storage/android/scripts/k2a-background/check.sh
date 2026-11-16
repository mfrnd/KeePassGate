#!/bin/sh
# The APKs held to their pins again, and what the device has installed.
#   sh check.sh
. "$(dirname "$0")/env.sh"
sh "$P/verify-apks.sh" > "$D/out/verify.txt" 2>&1; echo "verify-apks rc=$?"
grep -E '^===|MATCH|^Verifies|^package' "$D/out/verify.txt"
for p in keepass2android.keepass2android com.android.keepass com.kunzisoft.keepass.libre; do
  echo "$p: $(adb shell dumpsys package $p | grep -E '^ +versionName=|versionCode=' | tr -d '\r' | tr -s ' ' | head -2 | tr '\n' ' ')"
done
for p in keepass2android.keepass2android com.android.keepass com.kunzisoft.keepass.libre; do
  a=$(adb shell pm path $p | sed -n 's/^package://p' | tr -d '\r' | head -1)
  echo "$p installed $a sha256 $(adb shell sha256sum "$a" | cut -d' ' -f1)"
done
