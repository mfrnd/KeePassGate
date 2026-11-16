#!/bin/sh
# The WebDAV server the emulator reaches as http://10.0.2.2:8080/: rclone v1.68.2 (fetched and held
# to its published SHA256SUMS by server/fetch-rclone.sh) serving the directory server/root, basic
# authentication with an invented user and password, its log in out/rclone.log. Directory
# listings are not cached (--dir-cache-time 0s), because the runs change the files on disk.
#   sh server.sh start | stop | status
. "$(dirname "$0")/env.sh"
R="$D/server/rclone-dl/rclone"; ROOT="$D/server/root"; PID="$D/out/rclone.pid"
case "$1" in
  start)
    if [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null; then echo "server already running ($(cat "$PID"))"; exit 0; fi
    mkdir -p "$ROOT"
    nohup "$R" serve webdav "$ROOT" --addr 0.0.0.0:8080 --user kpg --pass <invented> \
      --dir-cache-time 0s --poll-interval 0 --log-file "$D/out/rclone.log" -v >/dev/null 2>&1 &
    echo $! > "$PID"
    sleep 2
    echo "server started ($(cat "$PID")) $(curl -s -o /dev/null -w 'PROPFIND %{http_code}' -u kpg:<invented> -X PROPFIND -H 'Depth: 1' http://127.0.0.1:8080/)"
    ;;
  stop)
    [ -f "$PID" ] && kill "$(cat "$PID")" 2>/dev/null; rm -f "$PID"; sleep 1
    echo "server stopped $(curl -s -o /dev/null -w '%{http_code}' --max-time 2 http://127.0.0.1:8080/ 2>&1)"
    ;;
  status)
    [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null && echo "running $(cat "$PID")" || echo "not running"
    ;;
esac
