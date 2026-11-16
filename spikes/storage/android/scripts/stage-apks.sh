#!/bin/sh
# Copy the three pinned APKs this measurement runs out of the earlier measurement's download cache
# into this measurement's own directory inside WSL, each into a directory of its own. Nothing is
# fetched; verify-apks.sh then holds every file to android.sha256 and to its signer.
#   sh stage-apks.sh <the earlier measurement's dl directory>
. "$(dirname "$0")/env.sh"
SRC=${1:?usage: sh stage-apks.sh <the earlier measurement's dl directory>}
for n in k2a-1.15-r6 kpd-2.7 kpdx-4.5.8; do
  f=$(pin "$n" 2)
  mkdir -p "$DL/$n"
  [ -f "$DL/$n/$f" ] || cp "$SRC/$n/$f" "$DL/$n/$f"
  ls -l "$DL/$n/$f"
done
