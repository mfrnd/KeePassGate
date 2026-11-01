#!/usr/bin/env bash
# The host tier's `ReferenceVectorsTests` under Mono: KeePass's own `SprEngine`, running on Mono,
# held to the vectors it made on .NET Framework.
#
#     run-vectors.sh <plugin checkout> <NUnitOnMono build directory> <AutomationStubs bin> <results.tsv>
#
# Those tests find the repository by looking for `KeePassGate.sln` above their own directory, and
# `run-tests.sh` runs a copy under /tmp that has none. This lays out the part of a checkout they
# read, the vectors and the marker, around a copy of the host tier's build. They write the vectors
# only when `KPG_WRITE_REFERENCE_VECTORS` is set, which nothing here sets, and the copy is removed.
set -uo pipefail
checkout="$1"
runner="$2"
stubs="$3"
out="$4"

unset KPG_WRITE_REFERENCE_VECTORS
tree="$(mktemp -d)"
build="$tree/tests/KeePassGate.Host.Tests/bin/Release/net48"
mkdir -p "$build" "$tree/tests/Shared"
: > "$tree/KeePassGate.sln"
cp "$checkout/tests/Shared/ReferenceVectors.json" "$tree/tests/Shared"/
cp -r "$checkout/tests/KeePassGate.Host.Tests/bin/Release/net48"/. "$build"/
cp "$runner"/NUnitOnMono.exe* "$build"/
cp "$stubs"/*.dll "$build"/
cd "$build" || exit 1

xvfb-run -a -s "-screen 0 1920x1080x24" \
    timeout 1800 mono --debug NUnitOnMono.exe KeePassGate.Host.Tests.dll "$out" 'ReferenceVectorsTests' > "$out.console" 2>&1
echo "exit $?" >> "$out.console"
cd / && rm -rf "$tree"
