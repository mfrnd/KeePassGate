#!/bin/sh
# Log Keepass2Android's notifications at a named point: for each notification the package has
# posted, its channel, flags, title, text and actions, read from dumpsys notification.
#   sh notif.sh <label>
. "$(dirname "$0")/env.sh"
adb shell dumpsys notification --noredact 2>/dev/null | tr -d '\r' | awk -v L="$1" '
  /NotificationRecord\(/ { on = ($0 ~ /pkg=keepass2android/); if (on) { n++; sub(/^ */, ""); print "notif " L " #" n ": " $0 } next }
  on && /(android\.title|android\.text|android\.subText|android\.bigText|actions=|channel=|flags=|^ *\[[0-9]+\] "|mChannel|ongoing|fgs|importance=)/ { sub(/^ */, ""); print "notif " L " #" n ":   " $0 }
  END { if (!n) print "notif " L ": none from keepass2android" }' | grep -v 'android.title=null\|android.text=null' | head -60
