#!/bin/sh
# Copy what carries the synchronize results into the review's evidence directory: the dumps of the
# two copies, every run's dumps (server, cache, local backup, the phone's copy before the
# synchronize, the run's SNEW, KeePass's own Synchronize of the same two) and their diffs, the
# verdict lines, the step logs (counted and not counted), the server's log, the flows and the
# scripts. No database is copied. Machine paths are written <work>. Git Bash.
#   sh evidence-sy.sh
M=$(cd "$(dirname "$0")/.." && pwd)
E=$M/out/evidence/k2a-synchronize
Mw=$(cygpath -w "$M" | sed 's#\\#\\\\#g')
mkdir -p "$E/base" "$E/runs" "$E/logs" "$E/flows" "$E/scripts" "$E/probe" "$E/server"
cp "$M"/sync/base/*.dump.txt "$E/base/"
for top in sync sync-not-counted; do
  [ -d "$M/work/$top" ] || continue
  for d in $(cd "$M/work/$top" && find . -name '*.txt'); do
    t="$E/runs/$top/$d"; [ "$top" = sync ] && t="$E/runs/$d"
    mkdir -p "$(dirname "$t")"; cp "$M/work/$top/$d" "$t"
  done
done
for d in $(cd "$M/out" && find sync sync-not-counted -name '*.log' -o -name '*.txt' 2>/dev/null); do
  mkdir -p "$E/logs/$(dirname "$d")"; cp "$M/out/$d" "$E/logs/$d"
done
cp "$M"/out/batch-sy-*.txt "$M"/out/sverdicts*.txt "$E/logs/" 2>/dev/null
cp "$M/out/rclone.log" "$E/server/rclone.log" 2>/dev/null
cp "$M/server/fetch-rclone.sh" "$M/server/rclone-dl/SHA256SUMS" "$E/server/" 2>/dev/null
cp "$M"/scripts/flows/k2a-sy-*.flow "$M"/scripts/flows/k2a-setting.flow "$M"/scripts/flows/k2a-ans-*.flow \
   "$M"/scripts/flows/k2a-ow-edit*.flow "$M"/scripts/flows/k2a-reopen.flow "$M"/scripts/flows/k2a-tap-recent.flow \
   "$M"/scripts/flows/k2a-after-qu.flow "$M"/scripts/flows/x-k2a-webdav.flow "$E/flows/"
cp "$M"/scripts/*.sh "$M/scripts/overwrite/k.py" "$E/scripts/"
cp -r "$M"/scripts/batches-ow "$E/scripts/"
cp "$M/probe/overwrite/Store.cs" "$M/probe/overwrite/Probe.csproj" "$E/probe/"
find "$E" -type f | while read -r f; do
  sed -i -e "s#/mnt$M#<work>#g" \
         -e "s#$M#<work>#g" \
         -e "s#$Mw#<work>#g" \
         -e 's#/home/[^/]*/storage-measure/dl#<dl>#g' "$f"
done
find "$E" -type f | wc -l
