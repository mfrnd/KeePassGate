#!/bin/sh
M=$(cd "$(dirname "$0")/.." && pwd)
X=$M/probe/overwrite/bin/Release/net48/StoreProbe.exe
"$X" dump "$(wslpath -w "$M/work/base/OLD.kdbx")" | head -3
