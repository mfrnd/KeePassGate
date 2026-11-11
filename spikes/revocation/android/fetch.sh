#!/bin/sh
# Downloads each pinned APK into a directory of its own, dl/<name>/ under the work directory, and
# checks its SHA-256 against `android.sha256`. A file already there is checked the same way and not
# fetched again. A name with no line there is refused before anything is fetched, and a file whose
# hash differs is named and fails the run: `verify-apks.sh` and `install.sh` refuse it as well.
# Nothing in an APK is run here. Appends to out/fetch.txt.
#   sh fetch.sh [name ...]      every pinned APK unless named; Git Bash or WSL
. "$(dirname "$0")/env.sh"
[ $# -gt 0 ] || set -- $(pins)
fetch() {
  bad=0
  echo "### $(date -u +%Y-%m-%dT%H:%M:%SZ) fetch"
  for name in "$@"; do
    want=$(pin "$name" 1); file=$(pin "$name" 2); url=$(pin "$name" 4)
    if [ -z "$want" ]; then
      echo "$name is not pinned in android.sha256, so there is nothing to fetch and nothing to check it against"
      bad=1; continue
    fi
    d="$D/dl/$name"
    [ -d "$d" ] || mkdir "$d"
    f="$d/$file"
    [ -f "$f" ] || curl -sSL -o "$f" "$url"
    got=$(sha256sum "$f" | cut -d' ' -f1)
    if [ "$got" = "$want" ]; then verdict="MATCH"; else verdict="MISMATCH (android.sha256 pins $want)"; bad=1; fi
    echo "$name $file $(wc -c < "$f") bytes sha256 $got $verdict"
  done
  return $bad
}
fetch "$@" > "$D/out/fetch.new" 2>&1; rc=$?
cat "$D/out/fetch.new"
cat "$D/out/fetch.new" >> "$D/out/fetch.txt"
rm -f "$D/out/fetch.new"
exit $rc
