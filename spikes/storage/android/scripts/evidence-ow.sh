#!/bin/sh
# Copy what carries the overwrite results into the review's evidence directory: the dumps of the
# four given files, every run's dump and its diffs against NEW-diff and OLD, the verdict lines, the
# step logs (counted and not counted), the flows, the scripts and the probe's source. No database is
# copied. Machine paths are written <work> and <dl>. Git Bash.
#   sh evidence-ow.sh
M=$(cd "$(dirname "$0")/.." && pwd)
E=$M/out/evidence/overwrite
Mw=$(cygpath -w "$M" | sed 's#\\#\\\\#g')
mkdir -p "$E/base" "$E/runs" "$E/logs" "$E/flows" "$E/scripts" "$E/probe"
cp "$M"/work/base/*.dump.txt "$E/base/"
for d in $(cd "$M/work/runs" && find . -name '*.txt'); do
  mkdir -p "$E/runs/$(dirname "$d")"; cp "$M/work/runs/$d" "$E/runs/$d"
done
if [ -d "$M/work/not-counted" ]; then
  for d in $(cd "$M/work/not-counted" && find . -name '*.txt'); do
    mkdir -p "$E/runs/not-counted/$(dirname "$d")"; cp "$M/work/not-counted/$d" "$E/runs/not-counted/$d"
  done
fi
for d in $(cd "$M/out" && find runs not-counted explore -name '*.log' 2>/dev/null); do
  mkdir -p "$E/logs/$(dirname "$d")"; cp "$M/out/$d" "$E/logs/$d"
done
cp "$M"/out/batch-*.txt "$M/out/verify.txt" "$M"/out/verdicts*.txt "$E/logs/" 2>/dev/null; rm -f "$E"/logs/batch-sy-*.txt
cp "$M"/scripts/flows/*.flow "$E/flows/"
cp "$M"/scripts/*.sh "$M/scripts/overwrite/k.py" "$M/scripts/android.sha256" "$E/scripts/"
cp -r "$M"/scripts/batches-ow "$E/scripts/"
cp "$M/probe/overwrite/Store.cs" "$M/probe/overwrite/Probe.csproj" "$E/probe/"
find "$E/logs" "$E/scripts" "$E/runs" -type f | while read -r f; do
  sed -i -e "s#/mnt$M#<work>#g" \
         -e "s#$M#<work>#g" \
         -e "s#$Mw#<work>#g" \
         -e 's#/home/[^/]*/storage-measure/dl#<dl>#g' "$f"
done
find "$E" -type f | wc -l
