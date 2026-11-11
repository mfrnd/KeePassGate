# Sourced by every script here: where things are.
#   KPG_ANDROID_WORK   the work directory: downloads in dl, databases in work, logs and
#                      read-backs in out. `work/android` under the repository unless set, which
#                      git ignores.
#   ANDROID_HOME       the Android SDK, `android-sdk` in the home directory unless set.
#   ANDROID_AVD_HOME   where the emulator's devices live, `.android/avd` in the home directory
#                      unless set.
P=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$P/../../.." && pwd)
PINS=$ROOT/android.sha256
D=${KPG_ANDROID_WORK:-$ROOT/work/android}
KPG_ANDROID_WORK=$D; export KPG_ANDROID_WORK
ANDROID_HOME=${ANDROID_HOME:-$HOME/android-sdk}; export ANDROID_HOME
ANDROID_SDK_ROOT=$ANDROID_HOME; export ANDROID_SDK_ROOT
ANDROID_AVD_HOME=${ANDROID_AVD_HOME:-$HOME/.android/avd}; export ANDROID_AVD_HOME
PATH=$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH; export PATH
BT=$(ls -d "$ANDROID_HOME"/build-tools/* 2>/dev/null | sort -V | tail -1)
[ -n "$BT" ] && { PATH=$BT:$PATH; export PATH; }
# Every adb call goes to ANDROID_SERIAL, the first emulator unless set.
export ANDROID_SERIAL=${ANDROID_SERIAL:-emulator-5554}
mkdir -p "$D/dl" "$D/work" "$D/out"
ts() { date -u +%Y-%m-%dT%H:%M:%S.%3NZ; }
# pins: the name of every APK `android.sha256` pins.
pins() { awk '!/^#/ && NF >= 4 { split($2, p, "/"); if (p[1] != "sdk") print p[1] }' "$PINS"; }
# pin <name> <column>: one column of the line pinning <name>. 1 the file's SHA-256, 2 the file,
# 3 the signing certificate's SHA-256, 4 the URL. Empty when nothing pins that name.
pin() {
  awk -v n="$1" -v c="$2" '!/^#/ && NF >= 4 { split($2, p, "/"); if (p[1] == n) print (c == 2 ? p[2] : $c) }' "$PINS"
}
