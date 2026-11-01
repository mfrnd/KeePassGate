#!/usr/bin/env bash
# Every probe under Mono, one process each, into one file of lines in the format the Windows run
# writes, so that `windows/Compare-Runs.ps1` can put the two side by side.
#
#     run-probes.sh <MonoProbe build directory> <results file>
#
# The build is copied off /mnt/c first, so that what is measured is a Linux file system. The forms
# probe is the one that needs a display, and it is given a virtual one.
set -uo pipefail
build="$1"
out="$2"

work="$(mktemp -d)"
cp -r "$build"/. "$work"/
: > "$out"
for probe in $(mono "$work/MonoProbe.exe" list | tr -d '\r'); do
    echo "### $probe" >> "$out"
    if [ "$probe" = forms ]; then
        xvfb-run -a -s "-screen 0 1280x800x24" timeout 120 mono --debug "$work/MonoProbe.exe" "$probe" >> "$out" 2>&1
    else
        timeout 120 mono --debug "$work/MonoProbe.exe" "$probe" >> "$out" 2>&1
    fi
    echo "exit $?" >> "$out"
done
rm -rf "$work"
