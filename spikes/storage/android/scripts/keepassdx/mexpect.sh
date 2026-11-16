#!/bin/sh
# Windows side, Git Bash: KeePass 2.62's own Synchronize of two given copies, in both directions
# (KeePassLib's MergeIn with Synchronize, headless), dumped and given the item verdict, for the
# comparison with what KeePassDX made of the same two copies. Results in work/expect/<label>/.
#   sh mexpect.sh <label> <copy A> <copy B>     (A open with B arriving, and B open with A arriving)
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
L=$1; A=$2; B=$3; O="$M/work/expect/$L"; rm -rf "$O"; mkdir -p "$O"
w() { cygpath -w "$1"; }
a=$(basename "$A" .kdbx); b=$(basename "$B" .kdbx)
"$X" sync "$(w "$A")" "$(w "$B")" "$(w "$O/kp-$a-open-$b-arriving.kdbx")" >/dev/null 2>&1
"$X" sync "$(w "$B")" "$(w "$A")" "$(w "$O/kp-$b-open-$a-arriving.kdbx")" >/dev/null 2>&1
"$X" dumpdir "$(w "$O")" >/dev/null 2>&1
"$X" mverdict "$(w "$O/kp-$a-open-$b-arriving.kdbx")" "$(w "$O/kp-$b-open-$a-arriving.kdbx")" 2>/dev/null | tee "$O/verdict.txt"
