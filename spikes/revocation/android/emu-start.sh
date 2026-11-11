#!/bin/sh
# One emulator, headless, with nothing drawn on any desktop. It stays in the foreground, so the
# caller runs it in the background. KVM must be loaded first (root-setup.sh). Its log is
# out/emu-<avd>-<utc>.log, which names this machine's paths and is not for committing.
#   sh emu-start.sh [avd, default kpg30] [console port, default 5554]
. "$(dirname "$0")/env.sh"
AVD=${1:-kpg30}; PORT=${2:-5554}
L="$D/out/emu-$AVD-$(date -u +%Y%m%dT%H%M%SZ).log"
echo "### $(ts) emulator $AVD on $PORT, log out/${L##*/}"
ls -l /dev/kvm
exec emulator -avd "$AVD" -port "$PORT" -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect -accel on -netdelay none -netspeed full > "$L" 2>&1
