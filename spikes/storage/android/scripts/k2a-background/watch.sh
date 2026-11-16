#!/bin/sh
# Watch for N seconds, every 2 s, the server's file (its SHA-256), the cache's version files and the
# title and text of Keepass2Android's notifications, and log each change with its time: when a
# synchronization wrote the server, the cache, or asked through a notification.
#   sh watch.sh <served name> <seconds> <label>
. "$(dirname "$0")/env.sh"
F="$D/server/root/$1"; C=/data/data/keepass2android.keepass2android/no_backup/OfflineCache
end=$(( $(date +%s) + $2 )); last=
while :; do
  s=$(sha256sum "$F" 2>/dev/null | cut -c1-12)
  v=$(adb shell "cat $C/*.cache.version 2>/dev/null; echo /; cat $C/*.cache.baseversion 2>/dev/null" | tr -d '\r\n' | sed 's/\([0-9a-f]\{12\}\)[0-9a-f]*/\1/g')
  n=$(adb shell dumpsys notification --noredact 2>/dev/null | tr -d '\r' | awk '/NotificationRecord\(/ { on = ($0 ~ /pkg=keepass2android/) } on && /android\.(title|text)=/ { sub(/^ */, ""); printf "%s; ", $0 }')
  cur="server $s cache $v notif [$n]"
  [ "$cur" != "$last" ] && echo "watch $3 $(ts): $cur"
  last=$cur
  [ "$(date +%s)" -ge "$end" ] && break
  sleep 2
done
