#!/bin/sh
# Added on the branch: step 8 for every label the record holds, each through sync.sh unchanged.
# The labels, the phone file each used (run 1 of a merge cell) and the KeePass copy are read off
# the first line of each recorded dump under recorded/sync/<label>/; the measurement ran them one
# by one. Windows side, Git Bash. The phone files are where run-op.sh pulls them and the KeePass
# copies are the bases, unless two directories laid out the same way are given.
#   sh sync-all.sh [<runs dir, as work/runs> [<base dir, as work/base>]]
M=$(cd "$(dirname "$0")/.." && pwd)
RUNS=${1:-$M/work/runs}; BASES=${2:-$M/work/base}
while read -r label phone kp base; do
  sh "$M/scripts/sync.sh" "$label" "$RUNS/$phone.kdbx" "$BASES/$kp.kdbx" "$base"
done <<'EOF'
k2a-save-argon k2a/save-argon/k2a-save-argon S-argon S-argon
k2a-edit-e1 k2a/edit-e1/k2a-edit-e1 S-argon S-argon
k2a-edit-carriers k2a/edit-carriers/k2a-edit-carriers S-argon S-argon
k2a-merge-CP-with-C k2a/merge-CP-r1/k2a-merge-CP-r1 C C
k2a-merge-CP-with-P k2a/merge-CP-r1/k2a-merge-CP-r1 P P
k2a-merge-PC-with-C k2a/merge-PC-r1/k2a-merge-PC-r1 C C
k2a-merge-PC-with-P k2a/merge-PC-r1/k2a-merge-PC-r1 P P
kpd-save-argon kpd/save-argon/kpd-save-argon S-argon S-argon
kpd-edit-e1 kpd/edit-e1/kpd-edit-e1 S-argon S-argon
kpd-edit-carriers kpd/edit-carriers/kpd-edit-carriers S-argon S-argon
kpd-delete kpd/delete/kpd-delete S-argon S-argon
kpdx-save-argon kpdx/save-argon/kpdx-save-argon S-argon S-argon
kpdx-edit-e1 kpdx/edit-e1/kpdx-edit-e1 S-argon S-argon
kpdx-edit-carriers kpdx/edit-carriers/kpdx-edit-carriers S-argon S-argon
kpdx-rename-g kpdx/rename-g/kpdx-rename-g S-argon S-argon
kpdx-delete kpdx/delete/kpdx-delete S-argon S-argon
kpdx-merge-fromCP-with-C kpdx/merge-fromCP-r1/kpdx-merge-fromCP-r1 C C
kpdx-merge-fromCP-with-P kpdx/merge-fromCP-r1/kpdx-merge-fromCP-r1 P P
kpdx-merge-fromPC-with-C kpdx/merge-fromPC-r1/kpdx-merge-fromPC-r1 C C
kpdx-merge-fromPC-with-P kpdx/merge-fromPC-r1/kpdx-merge-fromPC-r1 P P
EOF
