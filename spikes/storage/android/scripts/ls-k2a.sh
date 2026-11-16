#!/bin/sh
. "$(dirname "$0")/env.sh"
adb shell "find /data/data/keepass2android.keepass2android /data/user_de/0/keepass2android.keepass2android -type f 2>/dev/null | grep -v -E 'code_cache|/lib/|\.so$|oat' | head -50"
