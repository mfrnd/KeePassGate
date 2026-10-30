#!/usr/bin/env bash
# Runs the range measurement's steps on the probe's pull request #10 and prints every answer.
# R1 ran when the pull request opened; R2 posts the range past its hunk, R3 the second review.
set -u
REPO=mfrnd/codeql-suppression-probe
CHECKOUT=${1:?the probe checkout}
cd "$CHECKOUT" || exit 2

wait_and_read() {
    local sha=$1 id=""
    for _ in $(seq 1 60); do
        id=$(gh run list -R $REPO -w measure.yml -b measure/review-ranges -L 20 --json databaseId,headSha \
             --jq ".[] | select(.headSha==\"$sha\") | .databaseId" | head -1)
        [ -n "$id" ] && break
        sleep 5
    done
    [ -z "$id" ] && { echo "no run for $sha"; return 1; }
    gh run watch "$id" -R $REPO --interval 10 > /dev/null 2>&1
    for _ in $(seq 1 12); do
        out=$(gh run view "$id" -R $REPO --log 2>/dev/null | grep -o 'RESULT {.*' )
        [ -n "$out" ] && { echo "run $id"; echo "$out"; return 0; }
        sleep 10
    done
    echo "run $id printed no result"
}

step() {
    local name=$1 set=$2 size=$3
    printf '{"step": "%s", "set": "%s", "offset": 0, "batches": [%s], "pause": 1, "retry": false}\n' \
        "$name" "$set" "$size" > measure/plan.json
    git -c user.name=mfrnd -c user.email=mfrnd@users.noreply.github.com commit -q \
        -m "Plan step $name: $set" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>" \
        measure/plan.json
    git push -q origin measure/review-ranges
    echo "== $name"
    wait_and_read "$(git rev-parse HEAD)"
}

echo "== R1"
wait_and_read "$(git rev-parse HEAD)"
sleep 180
step R2 outside 1
sleep 180
step R3 "second review" 4
