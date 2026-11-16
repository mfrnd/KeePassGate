#!/bin/sh
# Copy what carries the background-synchronization results into an evidence directory:
# the dumps of the two copies, every run's dumps (the server's file and the cache at each named
# point, the desktop's turns, KeePass's own Synchronize of the same two) and their diffs, the
# read-back lines, the step logs (counted and not counted), the servers' logs, the flows, the
# scripts and the probe's source. No database is copied. Machine paths are written <work>, the WSL
# user's home <wsl-home>. Git Bash.
#   sh evidence-bg.sh
M=<work>
E=$M/out/evidence/k2a-background
mkdir -p "$E/base" "$E/runs" "$E/logs" "$E/flows" "$E/scripts" "$E/probe" "$E/server"
cp "$M"/sync/base/*.dump.txt "$E/base/"
for top in sync sync-not-counted; do
  [ -d "$M/work/$top" ] || continue
  for d in $(cd "$M/work/$top" && find . -name '*.txt'); do
    t="$E/runs/$top/$d"; [ "$top" = sync ] && t="$E/runs/$d"
    mkdir -p "$(dirname "$t")"; cp "$M/work/$top/$d" "$t"
  done
done
for d in $(cd "$M/out" && find sync sync-not-counted -name '*.log' 2>/dev/null); do
  mkdir -p "$E/logs/$(dirname "$d")"; cp "$M/out/$d" "$E/logs/$d"
done
cp "$M"/out/batch-sy-*.txt "$M"/out/sverdicts*.txt "$M"/out/expect*.txt "$E/logs/" 2>/dev/null
cp "$M"/out/emu-*.log "$E/logs/" 2>/dev/null
cp "$M/out/rclone.log" "$M/out/rclone-sftp.log" "$E/server/" 2>/dev/null
cp "$M/server/fetch-rclone.sh" "$M/server/rclone-dl/SHA256SUMS" "$E/server/" 2>/dev/null
cp "$M"/scripts/flows/k2a-bg-*.flow "$M"/scripts/flows/k2a-cancel-*.flow "$M"/scripts/flows/k2a-adv-*.flow \
   "$M"/scripts/flows/k2a-sftp-*.flow "$M"/scripts/flows/k2a-sy-*.flow "$M"/scripts/flows/k2a-setting.flow \
   "$M"/scripts/flows/k2a-ans-*.flow "$M"/scripts/flows/k2a-ow-edit*.flow "$M"/scripts/flows/k2a-reopen.flow \
   "$M"/scripts/flows/k2a-tap-recent.flow "$E/flows/" 2>/dev/null
cp "$M"/scripts/*.sh "$M/scripts/k.py" "$E/scripts/"
cp -r "$M"/scripts/batches-ow "$E/scripts/"
cp "$M/probe/Store.cs" "$M/probe/Probe.csproj" "$E/probe/"
find "$E" -type f | while read -r f; do
  sed -i -e 's#<scratch-wsl>/android-k2a-background#<work>#g' \
         -e 's#<scratch-bash>/android-k2a-background#<work>#g' \
         -e 's#<scratch-win>.android-k2a-background#<work>#g' \
         -e 's# <user> <user> # <user> <user> #g' \
         -e 's#<work>#<work>#g' \
         -e 's#/home/[a-z_][a-z0-9_-]*#<wsl-home>#g' -e 's#/tmp/android-[a-z_][a-z0-9_-]*#/tmp/android-<user>#g' "$f"
done
find "$E" -type f | wc -l
