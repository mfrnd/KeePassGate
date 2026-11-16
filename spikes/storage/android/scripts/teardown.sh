#!/bin/sh
# Stop every emulator and the adb server this measurement started, and show that nothing is left.
# WSL itself is stopped from Windows afterwards, with wsl --terminate and the distribution's name.
#   sh teardown.sh
. "$(dirname "$0")/env.sh"
for s in $(adb devices | awk '/^emulator-/{print $1}'); do ANDROID_SERIAL=$s adb emu kill; done
sleep 5
adb kill-server
sleep 1
ps -eo pid,args | grep -E 'qemu|emulator|adb|k\.py|run-[AB]' | grep -v grep || echo "no emulator, adb or driver process left"
