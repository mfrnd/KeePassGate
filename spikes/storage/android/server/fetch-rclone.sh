#!/bin/sh
# Fetch rclone v1.68.2 for linux-amd64 into a directory of its own beside this script, hold it to
# the SHA256SUMS published beside it and to the hash the branch's android.sha256 pins, and unpack
# only the binary. Nothing is installed.
#   sh fetch-rclone.sh
V=v1.68.2
D=$(cd "$(dirname "$0")" && pwd)/rclone-dl
PINS=$(cd "$(dirname "$0")/../../../.." && pwd)/android.sha256
mkdir -p "$D"
cd "$D" || exit 1
curl -sSfLO "https://downloads.rclone.org/$V/rclone-$V-linux-amd64.zip" || exit 1
curl -sSfL -o SHA256SUMS "https://downloads.rclone.org/$V/SHA256SUMS" || exit 1
want=$(grep " rclone-$V-linux-amd64.zip\$" SHA256SUMS | cut -d' ' -f1)
pinned=$(awk -v f="rclone-$V-linux-amd64.zip" '!/^#/ && NF >= 4 { n = split($2, p, "/"); if (p[n] == f) print $1 }' "$PINS")
got=$(sha256sum "rclone-$V-linux-amd64.zip" | cut -d' ' -f1)
echo "published $want"
echo "pinned    $pinned"
echo "arrived   $got"
[ -n "$want" ] && [ "$want" = "$got" ] || { echo "HASH MISMATCH"; exit 1; }
[ -n "$pinned" ] && [ "$pinned" = "$got" ] || { echo "NOT THE FILE android.sha256 PINS"; exit 1; }
unzip -o -j -q "rclone-$V-linux-amd64.zip" "rclone-$V-linux-amd64/rclone" -d "$D"
chmod +x "$D/rclone"
"$D/rclone" version | head -3
