# Sourced by every script here: where things are. A copy of the spike's env.sh, changed to this
# measurement's own directories.
#   P                  this scripts directory (on the Windows drive, read through /mnt/c)
#   PINS               the copy of android.sha256 beside these scripts
#   D                  the work directory: databases in work, logs and read-backs in out
#   DL                 the APKs, a directory of this measurement's own inside WSL
P=$(cd "$(dirname "$0")" && pwd)
PINS=$P/android.sha256
D=<work>
KPG_ANDROID_WORK=$D; export KPG_ANDROID_WORK
DL=$HOME/storage-measure/dl
ANDROID_HOME=${ANDROID_HOME:-$HOME/android-sdk}; export ANDROID_HOME
ANDROID_SDK_ROOT=$ANDROID_HOME; export ANDROID_SDK_ROOT
ANDROID_AVD_HOME=${ANDROID_AVD_HOME:-$HOME/.android/avd}; export ANDROID_AVD_HOME
PATH=$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH; export PATH
BT=$(ls -d "$ANDROID_HOME"/build-tools/* 2>/dev/null | sort -V | tail -1)
[ -n "$BT" ] && { PATH=$BT:$PATH; export PATH; }
export ANDROID_SERIAL=${ANDROID_SERIAL:-emulator-5554}
mkdir -p "$D/work" "$D/out"
ts() { date -u +%Y-%m-%dT%H:%M:%S.%3NZ; }
pins() { awk '!/^#/ && NF >= 4 { split($2, p, "/"); if (p[1] != "sdk") print p[1] }' "$PINS"; }
pin() {
  awk -v n="$1" -v c="$2" '!/^#/ && NF >= 4 { split($2, p, "/"); if (p[1] == n) print (c == 2 ? p[2] : $c) }' "$PINS"
}
