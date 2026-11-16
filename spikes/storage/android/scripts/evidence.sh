#!/bin/sh
# Copy what carries the results into the review's evidence directory: the base dumps, every counted
# run's dumps and diffs, the step-8 dumps and diffs, the step logs with the device-side paths kept
# (they name the emulator's directories only), the flows and the scripts, the probe's source.
# Databases are not copied: the dumps carry everything the report uses, and a .kdbx is never put
# where it could be committed. Git Bash.
#   sh evidence.sh
M=$(cd "$(dirname "$0")/.." && pwd)
E=$M/out/evidence
mkdir -p "$E/base" "$E/runs" "$E/sync" "$E/logs" "$E/flows" "$E/scripts" "$E/probe"
cp "$M"/work/base/*.dump.txt "$E/base/"
for d in $(cd "$M/work/runs" && find . -name '*.dump.txt' -o -name '*.diff.txt'); do
  mkdir -p "$E/runs/$(dirname "$d")"; cp "$M/work/runs/$d" "$E/runs/$d"
done
for d in $(cd "$M/work/sync" && find . -name '*.txt'); do
  mkdir -p "$E/sync/$(dirname "$d")"; cp "$M/work/sync/$d" "$E/sync/$d"
done
for d in $(cd "$M/out" && find runs -name '*.log'); do
  mkdir -p "$E/logs/$(dirname "$d")"; cp "$M/out/$d" "$E/logs/$d"
done
cp "$M"/out/batch-*.txt "$M/out/apksigner.txt" "$E/logs/"
cp "$M"/scripts/flows/*.flow "$E/flows/"
cp "$M"/scripts/*.sh "$M/scripts/k.py" "$M/scripts/android.sha256" "$E/scripts/"
cp -r "$M"/scripts/batches "$E/scripts/"
cp "$M/probe/Store.cs" "$M/probe/Probe.csproj" "$E/probe/"
cp "$M/logs/keepasslib-memprot-reset.txt" "$E/"
# The logs and the scripts name this machine's directories; they are written as <work> and <dl>.
find "$E/logs" "$E/scripts" -type f | while read -r f; do
  sed -i -e "s#/mnt$M#<work>#g" \
         -e "s#$M#<work>#g" \
         -e 's#/home/[^/]*/storage-measure/dl#<dl>#g' "$f"
done
find "$E" -type f | wc -l
