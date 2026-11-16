# What each holding means, kind by kind

For every kind of record and every edit: each distinct holding of `contents.md` among the merge
cells of that edit, the number of merge cells that ended with it, and the verdict under each rule.
A verdict that differs between cells with the same holding is given with the count of each.

## retirement of a0

| edit | holding | cells | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| one character of the payload | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,Oa] | 16 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,E] | 1 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] | 21 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| one character of the payload | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,Oa] | 16 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,E] | 1 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O] | 21 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string renamed | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,Oa] | 16 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,E] | 1 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O] | 21 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| payload string deleted | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,Oa] | 16 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,E] | 1 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| every string emptied | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O] | 21 | refusing: undecided: a0 and a1 both serve | unchanged | refusing: undecided: a0 and a1 both serve | refusing: the store refuses | unchanged |
| every string emptied | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] n:Ob@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Oa] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Ob] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O n:Ob@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] n:O@2g | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa@g[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O n:O@2g | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] n:O@2r | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa@r[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O n:O@2r | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | Oa[O] | 34 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! | 10 | refusing: undecided: a0 and a1 both serve | refusing: undecided: a0 and a1 both serve | refusing: undecided: a0 and a1 both serve | refusing: undecided: a0 and a1 both serve | unchanged |
| deleted with a deletion record | Oa[O]! | 12 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O | 8 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O! | 4 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | Oa[O] | 51 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | O | 17 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] n:E@2[O] n:Oc@2 | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,Oa] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,Oa] c:Oc | 16 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,E] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O] c:Oc | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc | 21 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O n:E@2[O] n:Oc@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O[O] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O n:Oa@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] | 14 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |

## revocation of c3

| edit | holding | cells | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| one character of the payload | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,Oa] | 16 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,E] | 1 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] | 21 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| one character of the payload | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,Oa] | 16 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,E] | 1 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O] | 21 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string renamed | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,Oa] | 16 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,E] | 1 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O] | 21 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| payload string deleted | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,Oa] | 16 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,E] | 1 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| every string emptied | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O] | 21 | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| every string emptied | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] n:Ob@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Oa] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Ob] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O n:Ob@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] n:O@2g | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa@g[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O n:O@2g | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] n:O@2r | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa@r[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O n:O@2r | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | Oa[O] | 34 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! | 10 | wider: c3 admitted | wider: c3 admitted | wider: c3 admitted | wider: c3 admitted | unchanged |
| deleted with a deletion record | Oa[O]! | 12 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O | 8 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O! | 4 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | Oa[O] | 51 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | O | 17 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] n:E@2[O] n:Oc@2 | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,Oa] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,Oa] c:Oc | 16 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,E] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O] c:Oc | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc | 21 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O n:E@2[O] n:Oc@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O[O] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O n:Oa@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] | 14 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |

## removal of legacy

| edit | holding | cells | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| one character of the payload | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,Oa] | 16 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| one character of the payload | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,E] | 1 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| one character of the payload | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] | 21 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| one character of the payload | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,Oa] | 16 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string renamed | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,E] | 1 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string renamed | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O] | 21 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string renamed | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,Oa] | 16 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string deleted | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,E] | 1 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string deleted | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O] | 21 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| payload string deleted | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,Oa] | 16 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| every string emptied | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,E] | 1 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| every string emptied | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O] | 21 | wider: legacy on the list | unchanged | wider: legacy on the list | refusing: the store refuses | wider: legacy on the list |
| every string emptied | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] n:Ob@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Oa] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Ob] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O n:Ob@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] n:O@2g | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa@g[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O n:O@2g | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] n:O@2r | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa@r[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O n:O@2r | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | Oa[O] | 34 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! | 10 | wider: legacy on the list | wider: legacy on the list | wider: legacy on the list | wider: legacy on the list | wider: legacy on the list |
| deleted with a deletion record | Oa[O]! | 12 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O | 8 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O! | 4 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | Oa[O] | 51 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | O | 17 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] n:E@2[O] n:Oc@2 | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,Oa] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,Oa] c:Oc | 16 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,E] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O] c:Oc | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc | 21 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O n:E@2[O] n:Oc@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O[O] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O n:Oa@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] | 14 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |

## recycle bin marker

| edit | holding | cells | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| one character of the payload | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,Oa] | 16 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| one character of the payload | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,E] | 1 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| one character of the payload | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] | 21 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| one character of the payload | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| one character of the payload | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,Oa] | 16 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string renamed | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,E] | 1 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string renamed | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O] | 21 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string renamed | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,Oa] | 16 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string deleted | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,E] | 1 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string deleted | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O] | 21 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| payload string deleted | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,Oa] | 16 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| every string emptied | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,E] | 1 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| every string emptied | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O] | 21 | wider: g:bin not hidden | unchanged | wider: g:bin not hidden | refusing: the store refuses | wider: g:bin not hidden |
| every string emptied | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] n:Ob@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Oa] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Ob] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O n:Ob@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] n:O@2g | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa@g[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O n:O@2g | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] n:O@2r | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa@r[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O n:O@2r | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | Oa[O] | 34 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! | 10 | wider: g:bin not hidden | wider: g:bin not hidden | wider: g:bin not hidden | wider: g:bin not hidden | wider: g:bin not hidden |
| deleted with a deletion record | Oa[O]! | 12 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O | 8 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O! | 4 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | Oa[O] | 51 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | O | 17 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] n:E@2[O] n:Oc@2 | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,Oa] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,Oa] c:Oc | 16 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O,E] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | Oa[O] c:Oc | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc | 21 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O n:E@2[O] n:Oc@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | E[O] c:Oc n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| duplicated, original edited | O[O] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O n:Oa@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] | 14 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |

## admission record of c1 (control)

| edit | holding | cells | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| one character of the payload | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,Oa] | 16 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |
| one character of the payload | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O,E] | 1 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |
| one character of the payload | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] | 21 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |
| one character of the payload | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| one character of the payload | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,Oa] | 16 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string renamed | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O,E] | 1 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string renamed | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | E[O] | 21 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string renamed | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string renamed | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string renamed | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,Oa] | 16 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string deleted | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O,E] | 1 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string deleted | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | E[O] | 21 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| payload string deleted | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| payload string deleted | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| payload string deleted | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | Oa[O] n:E@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,Oa] | 16 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| every string emptied | Oa[O,E] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O,E] | 1 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| every string emptied | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | E[O] | 21 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| every string emptied | O n:E@2[O] | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | E[O] n:O@2 | 1 | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| every string emptied | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| every string emptied | O[O,E] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] n:Ob@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Oa] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O,Ob] | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Oa[O] | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O n:Ob@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | Ob[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| title only | O[O,Ob] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] n:O@2g | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | Oa@g[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O n:O@2g | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to another group | O@g | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] n:O@2r | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa[O] | 6 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O[O,Oa] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | Oa@r[O] | 36 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O n:O@2r | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| moved to the recycle bin | O@r | 13 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | Oa[O] | 34 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! | 10 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |
| deleted with a deletion record | Oa[O]! | 12 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O | 8 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | -! n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted with a deletion record | O! | 4 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | Oa[O] | 51 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | O | 17 | unchanged | unchanged | unchanged | unchanged | unchanged |
| deleted without a record | - n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] n:E@2[O] n:Oc@2 | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc n:Oa@2[O] | 3 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O,Oa] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,Oa] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O,E] c:Oc | 16 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | Oa[O] c:Oc | 7 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc | 21 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O n:E@2[O] n:Oc@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | E[O] c:Oc n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| duplicated, original edited | O[O,E] c:Oc | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] n:O@2 | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O n:Oa@2[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | Oa[O] | 14 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
| no edit | O[O,Oa] | 1 | unchanged | unchanged | unchanged | unchanged | unchanged |
