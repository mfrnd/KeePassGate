#!/bin/sh
# Log a device file's size, time and hash at a named point of a flow.
#   sh devstat.sh <device path> <label>
. "$(dirname "$0")/env.sh"
echo "devstat $2: $(adb shell stat -c "'%s bytes, mtime %y'" "$1" | tr -d '\r') sha256 $(adb shell sha256sum "$1" | cut -d' ' -f1)"
