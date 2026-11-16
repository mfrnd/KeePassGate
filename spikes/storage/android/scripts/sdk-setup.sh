#!/bin/sh
# The Android SDK under ANDROID_HOME, from Google's command-line tools, checked against the SHA-256
# `android.sha256` pins, which is the value published on https://developer.android.com/studio.
# Idempotent.
#   sh sdk-setup.sh
. "$(dirname "$0")/env.sh"
ZIP=$(pin sdk 2)
WANT=$(pin sdk 1)
URL=$(pin sdk 4)
IMG="system-images;android-30;google_apis;x86_64"
echo "### $(ts) sdk-setup"
[ -n "$WANT" ] || { echo "android.sha256 pins no command-line tools, stopping"; exit 1; }
mkdir -p ~/sdk-dl "$ANDROID_HOME"
if [ ! -x "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" ]; then
  [ -f ~/sdk-dl/$ZIP ] || curl -sSL -o ~/sdk-dl/$ZIP "$URL"
  GOT=$(sha256sum ~/sdk-dl/$ZIP | cut -d' ' -f1)
  echo "sha256 $ZIP: $GOT"
  echo "pinned:      $WANT"
  [ "$GOT" = "$WANT" ] || { echo "MISMATCH, stopping"; exit 1; }
  rm -rf ~/sdk-dl/x; mkdir -p ~/sdk-dl/x; (cd ~/sdk-dl/x && unzip -q ../$ZIP)
  mkdir -p "$ANDROID_HOME/cmdline-tools"; rm -rf "$ANDROID_HOME/cmdline-tools/latest"
  mv ~/sdk-dl/x/cmdline-tools "$ANDROID_HOME/cmdline-tools/latest"
fi
sdkmanager --version
yes | sdkmanager --licenses > /dev/null 2>&1
sdkmanager --list 2>/dev/null | grep -E '^ *(build-tools;[0-9.]+ |emulator |platform-tools )' | tail -4
BTV=$(sdkmanager --list 2>/dev/null | grep -o -E 'build-tools;[0-9]+\.[0-9]+\.[0-9]+ ' | sort -t';' -k2 -V | tail -1 | tr -d ' ')
echo "installing platform-tools emulator $BTV $IMG"
yes | sdkmanager --install platform-tools emulator "$BTV" "$IMG" 2>&1 | grep -v -E '^\[=*' | tail -5
sdkmanager --list_installed 2>/dev/null
echo "### $(ts) done"
