#!/bin/sh
# Pull Keepass2Android's cached copies (as root) into a directory, each .cache as <prefix>.kdbx
# (a second one as <prefix>-2.kdbx), and log the version files beside them.
#   sh pull-cache.sh <dir> <prefix>
. "$(dirname "$0")/env.sh"
n=0
for c in /data/data/keepass2android.keepass2android/no_backup/OfflineCache /data/data/keepass2android.keepass2android/files/OfflineCache /data/data/keepass2android.keepass2android/cache/OfflineCache; do
  for f in $(adb shell "ls $c 2>/dev/null" | tr -d '\r'); do
    case "$f" in
      *.cache) n=$((n + 1)); out="$1/$2.kdbx"; [ $n -gt 1 ] && out="$1/$2-$n.kdbx"
               adb pull "$c/$f" "$out" >/dev/null 2>&1
               echo "pulled $c/$f -> ${out##*/} sha256 $(sha256sum "$out" | cut -d' ' -f1)" ;;
      *) echo "cache file $c/$f: $(adb shell cat "$c/$f" | tr -d '\r')" ;;
    esac
  done
done
[ $n -eq 0 ] && echo "no cache file"
# The local backup copy the program keeps after a successful load (Settings, File handling, "Local
# backups", on by default), beside the cache.
for b in $(adb shell "ls /data/data/keepass2android.keepass2android/no_backup/ 2>/dev/null" | tr -d '\r' | grep -v OfflineCache); do
  adb pull "/data/data/keepass2android.keepass2android/no_backup/$b" "$1/$2-localbackup.kdbx" >/dev/null 2>&1
  echo "pulled local backup $b -> $2-localbackup.kdbx sha256 $(sha256sum "$1/$2-localbackup.kdbx" | cut -d' ' -f1)"
done
