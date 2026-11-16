#!/bin/sh
# Give the emulator a screen lock PIN (so that Keepass2Android offers QuickUnlock) or take it away.
# The screen is kept on (wait-boot.sh), so the keyguard is never shown while the runs go on.
#   sh pin.sh set | clear | show
. "$(dirname "$0")/env.sh"
case "$1" in
  set) adb shell locksettings set-pin 4826 ;;
  clear) adb shell locksettings clear --old 4826 ;;
esac
echo "secure: $(adb shell locksettings get-disabled 2>&1 | tr -d '\r'), $(adb shell dumpsys trust 2>/dev/null | grep -m1 -i 'deviceSecure\|secure=' | tr -d '\r')"
