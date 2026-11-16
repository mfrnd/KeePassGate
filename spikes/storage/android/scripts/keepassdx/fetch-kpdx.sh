#!/bin/sh
# Download the KeePassDX release this measurement runs (the libre flavor, as before) from its GitHub
# release into a new, empty directory of this measurement's own inside WSL, and print its SHA-256.
# verify-apks.sh then holds it to android.sha256 and to the pinned signer.
#   sh fetch-kpdx.sh <version>
. "$(dirname "$0")/env.sh"
V=${1:-4.5.8}
O="$DL/kpdx-$V"
rm -rf "$O"; mkdir -p "$O"
echo "### $(ts) fetch KeePassDX $V into <dl>/kpdx-$V"
curl -sSfL -o "$O/KeePassDX-$V-libre.apk" "https://github.com/Kunzisoft/KeePassDX/releases/download/$V/KeePassDX-$V-libre.apk"
ls -l "$O"
sha256sum "$O/KeePassDX-$V-libre.apk"
