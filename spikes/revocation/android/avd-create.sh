#!/bin/sh
# An emulated device: API 30, google_apis, x86_64, which has adb root, and a hardware keyboard so
# that no on-screen keyboard covers the screen. Idempotent with --force.
#   sh avd-create.sh [name, default kpg30]
. "$(dirname "$0")/env.sh"
N=${1:-kpg30}
echo "### $(ts) avd-create $N"
echo no | avdmanager create avd -n "$N" -k "system-images;android-30;google_apis;x86_64" -d pixel_5 --force 2>&1 | grep -v -E '^\[=*|Loading local'
CFG=$ANDROID_AVD_HOME/$N.avd/config.ini
sed -i -e '/^hw.keyboard=/d' -e '/^hw.ramSize=/d' -e '/^disk.dataPartition.size=/d' -e '/^hw.cpu.ncore=/d' "$CFG"
printf 'hw.keyboard=yes\nhw.ramSize=3072\ndisk.dataPartition.size=6G\nhw.cpu.ncore=4\n' >> "$CFG"
grep -E '^(hw.keyboard|hw.ramSize|disk.dataPartition.size|hw.cpu.ncore|image.sysdir.1|hw.lcd)' "$CFG"
emulator -version 2>&1 | head -1
emulator -accel-check 2>&1 | tail -3
