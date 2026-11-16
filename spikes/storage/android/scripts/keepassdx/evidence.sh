#!/bin/sh
# Git Bash: copy what carries the KeePassDX results into an evidence directory: the dumps
# of the given files, every pulled file's dump and diffs, the verdicts, the synchronize results'
# dumps and diffs, every step log (counted and not counted), the flows, the scripts, the probe's
# source, the pin file, the release notes excerpt. No database and no APK is copied. Machine paths
# are written <work>, <dl> and <home>. Run again at any point; it copies what exists.
M=<work>
E=$M/out/evidence/keepassdx
mkdir -p "$E"
cd "$M/work" || exit 1
find . -type f \( -name '*.txt' -o -name '*.sha256' \) | while read -r f; do
  mkdir -p "$E/work/$(dirname "$f")"; cp "$f" "$E/work/$f"
done
cd "$M/out" || exit 1
find . -type f \( -name '*.log' -o -name '*.txt' \) | while read -r f; do
  mkdir -p "$E/logs/$(dirname "$f")"; cp "$f" "$E/logs/$f"
done
mkdir -p "$E/flows" "$E/scripts" "$E/probe" "$E/notes"
cp "$M"/scripts/flows/*.flow "$E/flows/"
cp "$M"/scripts/*.sh "$M/scripts/k.py" "$M/scripts/android.sha256" "$E/scripts/"
cp -r "$M/scripts/batches" "$E/scripts/"
cp "$M/probe/Store.cs" "$M/probe/DX.cs" "$M/probe/Probe.csproj" "$E/probe/"
cp "$M"/notes/* "$E/notes/" 2>/dev/null
cp "$M/scripts/android.sha256" "$E/android.sha256"
[ -f "$M/release-notes.txt" ] && cp "$M/release-notes.txt" "$E/release-notes.txt"
find "$E" -type f | while read -r f; do
  sed -i -e 's#<scratch-wsl>/android-keepassdx#<work>#g' \
         -e 's#<scratch-bash>/android-keepassdx#<work>#g' \
         -e 's#<scratch-win>\\android-keepassdx#<work>#g' \
         -e 's#<scratch-wsl>/android-measure#<measure-work>#g' \
         -e 's#<scratch-bash>/android-measure#<measure-work>#g' \
         -e 's#<home>/]*/kpdx-measure/dl#<dl>#g' -e 's#<home>/]*/#<home>/#g' "$f"
done
echo "evidence: $(find "$E" -type f | wc -l) files; kdbx: $(find "$E" -name '*.kdbx' | wc -l); apk: $(find "$E" -name '*.apk' | wc -l)"
