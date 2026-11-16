#!/bin/sh
# Kill Keepass2Android's process as the system does when it reclaims memory (SIGKILL, as root), so
# that nothing of it runs on: no lifecycle callback, no service stopped.
. "$(dirname "$0")/env.sh"
p=$(adb shell pidof keepass2android.keepass2android | tr -d '\r')
adb shell kill -9 $p; sleep 2
echo "killed $p at $(ts); now pid '$(adb shell pidof keepass2android.keepass2android | tr -d '\r')'"
