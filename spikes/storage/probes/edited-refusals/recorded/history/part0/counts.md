# Verdicts by reading rule

Merge cells only: 18 a scenario (five KeePassLib methods and four `keepassxc-cli` releases, both
directions). Each cell of a table is wider / unchanged / refusing, over the four time conditions of
that edit (72 cells), or over the 18 cells of the scenario with A's title edit alone (`none`).

## R1

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 486/2232/162 of 2880; control 0/558/162 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| c1 refused | 162 |
| c3 admitted | 162 |
| g:bin not hidden | 162 |
| legacy on the list | 162 |
| undecided: a0 and a1 both serve | 162 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string renamed | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string deleted | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| every string emptied | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | unchanged | unchanged | unchanged | unchanged | unchanged |

## R2

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| payload string renamed | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| payload string deleted | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| every string emptied | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 30/2840/10 of 2880; control 0/558/162 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| c1 refused | 162 |
| c3 admitted | 10 |
| g:bin not hidden | 10 |
| legacy on the list | 10 |
| undecided: a0 and a1 both serve | 10 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | unchanged | unchanged | unchanged | unchanged | refusing: c1 refused |
| payload string renamed | unchanged | unchanged | unchanged | unchanged | refusing: c1 refused |
| payload string deleted | unchanged | unchanged | unchanged | unchanged | refusing: c1 refused |
| every string emptied | unchanged | unchanged | unchanged | unchanged | refusing: c1 refused |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | unchanged | unchanged | unchanged | unchanged | unchanged |

## R3

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 486/2232/162 of 2880; control 0/558/162 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| c1 refused | 162 |
| c3 admitted | 162 |
| g:bin not hidden | 162 |
| legacy on the list | 162 |
| undecided: a0 and a1 both serve | 162 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string renamed | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string deleted | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| every string emptied | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | unchanged | unchanged | unchanged | unchanged | unchanged |

## R4

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/34/38 |
| payload string renamed | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| payload string deleted | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| every string emptied | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 30/1920/930 of 2880; control 0/534/186 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| the store refuses | 1058 |
| c1 refused | 48 |
| c3 admitted | 10 |
| g:bin not hidden | 10 |
| legacy on the list | 10 |
| undecided: a0 and a1 both serve | 10 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: c1 refused |
| payload string renamed | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses |
| payload string deleted | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses |
| every string emptied | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | refusing: the store refuses | unchanged |

## R5

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/72/0 | 0/72/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 324/2556/0 of 2880; control 0/558/162 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| c1 refused | 162 |
| g:bin not hidden | 162 |
| legacy on the list | 162 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string renamed | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| payload string deleted | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| every string emptied | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | unchanged | unchanged | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | unchanged | unchanged | unchanged | unchanged | unchanged |

## R6

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| payload string renamed | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| payload string deleted | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| every string emptied | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

Totals, wider / unchanged / refusing: refusals 30/2840/10 of 2880; control 0/710/10 of 720; no edit 0/108/0 of 108

What the cells that are not unchanged hold:

| detail | cells |
| --- | --- |
| c1 refused | 10 |
| c3 admitted | 10 |
| g:bin not hidden | 10 |
| legacy on the list | 10 |
| undecided: a0 and a1 both serve | 10 |

The edited copy B, read as it is, with no merge:

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| deleted without a record | refusing: undecided: a0 and a1 both serve | wider: c3 admitted | wider: legacy on the list | wider: g:bin not hidden | refusing: c1 refused |
| duplicated, original edited | unchanged | unchanged | unchanged | unchanged | unchanged |

## All rules

| rule | refusal cells (2,880) | control cells (720) | no-edit cells (108) |
| --- | --- | --- | --- |
| R1 | 486/2232/162 | 0/558/162 | 0/108/0 |
| R2 | 30/2840/10 | 0/558/162 | 0/108/0 |
| R3 | 486/2232/162 | 0/558/162 | 0/108/0 |
| R4 | 30/1920/930 | 0/534/186 | 0/108/0 |
| R5 | 324/2556/0 | 0/558/162 | 0/108/0 |
| R6 | 30/2840/10 | 0/710/10 | 0/108/0 |
