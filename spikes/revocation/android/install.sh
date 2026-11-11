#!/bin/sh
# Install one fetched APK, uninstalling whatever holds its package first when asked, and print what
# the device then has. The file is held to `android.sha256` once more before it goes anywhere.
#   sh install.sh <name> [replace]
. "$(dirname "$0")/env.sh"
APK="$D/dl/$1/$(pin "$1" 2)"
[ -n "$(pin "$1" 1)" ] || { echo "$1 is not pinned in android.sha256; nothing installed"; exit 1; }
[ "$(sha256sum "$APK" | cut -d' ' -f1)" = "$(pin "$1" 1)" ] || { echo "$1 is not the file android.sha256 pins; nothing installed"; exit 1; }
PKG=$(aapt2 dump badging "$APK" 2>/dev/null | sed -n "s/^package: name='\([^']*\)'.*/\1/p")
echo "### $(ts) install $1 ($PKG) from ${APK#$D/}"
[ "$2" = replace ] && adb uninstall "$PKG"
adb install -r "$APK"
adb shell dumpsys package "$PKG" | grep -E 'versionName|versionCode|minSdk|targetSdk' | head -3
