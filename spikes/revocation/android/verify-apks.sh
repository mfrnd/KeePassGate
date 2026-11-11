#!/bin/sh
# apksigner verify --print-certs on every pinned APK, plus its package, version and SDK levels, each
# held to `android.sha256`: the file's hash, that the signature verifies, and that the one
# certificate it was signed with is the pinned certificate. Any other answer fails the run.
# Output in out/apksigner.txt.
#   sh verify-apks.sh
. "$(dirname "$0")/env.sh"
verify() {
  bad=0
  echo "### $(ts) apksigner $(apksigner --version) from build-tools ${BT##*/}"
  for name in $(pins); do
    file=$(pin "$name" 2); f="$D/dl/$name/$file"
    echo "=== dl/$name/$file"
    if [ ! -f "$f" ]; then echo "NOT FETCHED"; bad=1; continue; fi
    got=$(sha256sum "$f" | cut -d' ' -f1)
    echo "$got"
    [ "$got" = "$(pin "$name" 1)" ] || { echo "HASH MISMATCH (android.sha256 pins $(pin "$name" 1))"; bad=1; }
    v=$(apksigner verify --verbose --print-certs "$f" 2>&1 | grep -v -E '^WARNING: META-INF/')
    echo "$v"
    aapt2 dump badging "$f" 2>/dev/null | grep -E "^(package|sdkVersion|targetSdkVersion|native-code)"
    signers=$(echo "$v" | sed -n 's/^.*certificate SHA-256 digest: //p' | sort -u)
    if [ "$(echo "$v" | head -1)" = Verifies ] && [ "$signers" = "$(pin "$name" 3)" ]; then
      echo "SIGNER MATCH"
    else
      echo "SIGNER MISMATCH (android.sha256 pins $(pin "$name" 3))"; bad=1
    fi
  done
  return $bad
}
verify > "$D/out/apksigner.txt" 2>&1; rc=$?
grep -E '^===|certificate SHA-256|^Verifie[ds]|DOES NOT|MISMATCH|MATCH|NOT FETCHED|^package|sdkVersion|native-code|Number of signers' "$D/out/apksigner.txt"
exit $rc
