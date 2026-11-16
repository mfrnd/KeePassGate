#!/bin/sh
# Capture the accessibility events (toasts and dialogs among them, with their text) for N seconds and
# log the ones that carry text. Toasts are not in a uiautomator dump; they are in these events.
#   sh events.sh <seconds> <label>
. "$(dirname "$0")/env.sh"
adb shell "timeout $1 uiautomator events" 2>/dev/null | tr -d '\r' \
  | grep -E 'TYPE_NOTIFICATION_STATE_CHANGED|TYPE_WINDOW_STATE_CHANGED|TYPE_WINDOW_CONTENT_CHANGED' \
  | grep -o 'EventType: [A-Z_]*\|PackageName: [^;]*\|Text: \[[^]]*\]' | paste -sd' ' - \
  | sed 's/EventType: /\n/g' | grep 'Text: \[[^]]' | sort -u | sed "s/^/events $2: /"
