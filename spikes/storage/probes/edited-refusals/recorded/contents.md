# What each result holds of the record under test

The record under test in a cell, in short.

For each entry that holds the record: its current version and then its history items in
brackets. `O` is the content (the `KPG.` strings) as written, `E` the content B's edit gave it,
`?` anything else; a following `a` is A's title, `b` B's title, `c` the copy's title. `@g` is
the group `General`, `@r` the recycle bin; `@2`, `@2g` and `@2r` are the second `KeePassGate`,
`General` and `Recycle Bin` that Create new IDs makes. `-` is no
current version, `!` a deletion record naming the entry. The original entry comes first, then
`c:` the duplicate B made, then `n:` each copy under an identifier Create new IDs drew.

Each edit's rows carry the four time conditions: `untouched`, A as it was made; `older`, A edits
the same entry's title 1800 seconds after B's edit; `newer`, 1800 seconds before it; `same`, in the
same second. `none title-only` is A's title edit with no edit in B. Where the four `keepassxc-cli`
releases agree, their column holds the result once; where they differ, all four in release order.
The verdicts under each rule, cell by cell, are in `verdicts.tsv`.

## retirement of a0

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |

The witness `w:retired-a0` is in every file of this kind: yes.

## revocation of c3

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |

The witness `w:revoked-c3` is in every file of this kind: yes.

## removal of legacy

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |

## recycle bin marker

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |

## admission record of c1 (control)

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |
