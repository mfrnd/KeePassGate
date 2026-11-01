#!/usr/bin/env bash
# One of the plugin's test tiers under Mono, through `NUnitOnMono`, on a virtual display.
#
#     run-tests.sh <test build directory> <test assembly name> <NUnitOnMono build directory> <results.tsv> [name regex]
#
# The build is copied off /mnt/c and the runner put beside it, so the tests resolve their own NUnit.
# The results file gets one line per test as it finishes; `<results.tsv>.console` gets the name of
# every test as it starts, so a run that ends early names the test it ended in.
#
# With `STUBS` naming a directory, every DLL in it is put beside the tests as well: the host tier
# needs `product/AutomationStubs` for NUnit to load its assembly at all.
set -uo pipefail
tests="$1"
assembly="$2"
runner="$3"
out="$4"
filter="${5:-}"

work="$(mktemp -d)"
cp -r "$tests"/. "$work"/
cp "$runner"/NUnitOnMono.exe* "$work"/
if [ -n "${STUBS:-}" ]; then
    cp "$STUBS"/*.dll "$work"/
fi
cd "$work" || exit 1

# The gallery tests clear whatever directory this names, which is nothing of ours here.
unset KPG_GALLERY_DIR

xvfb-run -a -s "-screen 0 1920x1080x24" \
    timeout 7200 mono --debug NUnitOnMono.exe "$assembly" "$out" ${filter:+"$filter"} > "$out.console" 2>&1
echo "exit $?" >> "$out.console"
cd / && rm -rf "$work"
