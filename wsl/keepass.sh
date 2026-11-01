#!/usr/bin/env bash
# The pinned KeePass under Mono with the plugin installed, on a display of its own, driven by
# hand one step at a time, because what a step should be depends on what the last picture showed.
#
#     keepass.sh start <KeePass directory> <plugin build directory> <MakeDatabase build directory>
#     keepass.sh shot <file.png>         the whole virtual screen
#     keepass.sh key <xdotool keys...>   to the focused window, e.g. `key alt+t`
#     keepass.sh type <text>
#     keepass.sh windows                 every window's id and title
#     keepass.sh listening               the sockets KeePass listens on
#     keepass.sh tls                     what a TLS 1.3 client is answered on the gate's port
#     keepass.sh files <directory>       copy the plugin's own files (log, audit) out
#     keepass.sh stop
#
# Everything lives under /tmp/kpg-mono. The plugin goes where the operator's documentation and
# `build.ps1 -Task Deploy` put it, `plugins/KeePassGate`; KeePass on Unix finds a plugins
# directory whatever its case. The database is a throwaway with the password `dummy`, which is
# why it can be given on the command line.
set -uo pipefail
root=/tmp/kpg-mono
display=:93
export DISPLAY="$display"
unset WAYLAND_DISPLAY

case "${1:-}" in
start)
    keepass="$2"; plugin="$3"; maker="$4"
    rm -rf "$root" && mkdir -p "$root/KeePass" "$root/maker" "$root/home"
    cp -r "$keepass"/. "$root/KeePass"/
    mkdir -p "$root/KeePass/plugins/KeePassGate"
    cp -r "$plugin"/. "$root/KeePass/plugins/KeePassGate"/
    # The end to end tier's configuration: no update question on the first start, Escape does
    # nothing, and a second instance is allowed.
    cat > "$root/KeePass/KeePass.config.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Application>
    <Start>
      <CheckForUpdate>false</CheckForUpdate>
      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>
    </Start>
  </Application>
  <MainWindow>
    <EscAction>None</EscAction>
  </MainWindow>
  <Integration>
    <LimitToSingleInstance>false</LimitToSingleInstance>
  </Integration>
</Configuration>
XML
    cp -r "$maker"/. "$root/maker"/
    cp "$root/KeePass/KeePass.exe" "$root/maker"/
    mono "$root/maker/MakeDatabase.exe" "$root/probe.kdbx" || exit 1
    Xvfb "$display" -screen 0 1280x800x24 -nolisten tcp > "$root/xvfb.log" 2>&1 &
    echo $! > "$root/xvfb.pid"
    sleep 2
    # A home of its own, so that what KeePass and the plugin write under it is all here.
    # `cd` on a line of its own, so that `$!` is Mono rather than a shell around it.
    (cd "$root/KeePass" || exit 1
        HOME="$root/home" XDG_DATA_HOME="$root/home/.local/share" \
            mono --debug KeePass.exe "$root/probe.kdbx" -pw:dummy > "$root/keepass.console" 2>&1 &
        echo $! > "$root/keepass.pid")
    echo "started; console at $root/keepass.console"
    ;;
shot)
    xwd -root -silent | convert xwd:- "$2" && echo "wrote $2"
    ;;
key)
    shift; xdotool key --delay 100 "$@"
    ;;
type)
    shift; xdotool type --delay 50 "$*"
    ;;
windows)
    for id in $(xdotool search --onlyvisible --name '.*' 2>/dev/null); do
        echo "$id $(xdotool getwindowname "$id")"
    done
    ;;
listening)
    ss -ltnp
    ;;
tls)
    timeout 10 openssl s_client -connect 127.0.0.1:51776 -servername probe -tls1_3 < /dev/null 2>&1 | head -40
    ;;
files)
    mkdir -p "$2" && cp -r "$root/home/.local/share/KeePassGate" "$2"/ 2>/dev/null; ls -laR "$2"
    ;;
stop)
    kill "$(cat "$root/keepass.pid")" 2>/dev/null; sleep 2
    kill "$(cat "$root/xvfb.pid")" 2>/dev/null
    echo stopped
    ;;
*)
    sed -n '2,20p' "$0"; exit 2
    ;;
esac
