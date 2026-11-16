#!/bin/sh
# Wait until the emulator reports that it has booted, then root adb, keep the screen on, turn the
# animations off and print the device's facts.
#   sh wait-boot.sh [timeout seconds]
. "$(dirname "$0")/env.sh"
T=${1:-600}
echo "### $(ts) waiting for boot (up to $T s)"
start=$(date +%s)
adb wait-for-device
while [ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" != 1 ]; do
  [ $(( $(date +%s) - start )) -gt "$T" ] && { echo "### $(ts) NOT BOOTED after $T s"; exit 1; }
  sleep 3
done
echo "### $(ts) booted after $(( $(date +%s) - start )) s"
adb root; sleep 3; adb wait-for-device
adb shell svc power stayon true
adb shell settings put global window_animation_scale 0
adb shell settings put global transition_animation_scale 0
adb shell settings put global animator_duration_scale 0
adb shell settings put system screen_off_timeout 1800000
adb shell input keyevent KEYCODE_WAKEUP
adb shell wm dismiss-keyguard 2>/dev/null
echo "android $(adb shell getprop ro.build.version.release | tr -d '\r') api $(adb shell getprop ro.build.version.sdk | tr -d '\r') abi $(adb shell getprop ro.product.cpu.abilist | tr -d '\r')"
adb shell date -u
date -u
