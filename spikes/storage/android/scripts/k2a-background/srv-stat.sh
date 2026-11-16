#!/bin/sh
# Log the served file's size, time and hash, and K2A's cache files (as root), at a named point.
#   sh srv-stat.sh <served name> <label>
. "$(dirname "$0")/env.sh"
F="$D/server/root/$1"
echo "server $1 at $2: $(stat -c '%s bytes, mtime %y' "$F") sha256 $(sha256sum "$F" | cut -d' ' -f1)"
for c in /data/data/keepass2android.keepass2android/no_backup/OfflineCache /data/data/keepass2android.keepass2android/files/OfflineCache /data/data/keepass2android.keepass2android/cache/OfflineCache; do
  adb shell "ls $c 2>/dev/null" | tr -d '\r' | while read -r f; do
    [ -n "$f" ] || continue
    case "$f" in
      *.cache) echo "cache $f at $2: $(adb shell stat -c "'%s bytes'" "$c/$f" | tr -d '\r') sha256 $(adb shell sha256sum "$c/$f" | cut -d' ' -f1)" ;;
      *) echo "cache $f at $2: $(adb shell cat "$c/$f" | tr -d '\r')" ;;
    esac
  done
done
