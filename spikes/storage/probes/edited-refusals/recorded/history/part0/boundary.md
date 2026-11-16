# Cells that depend on the second boundary

For every record, edit, merge and rule: the verdict when A's title edit falls in the same second
as B's edit (`same`), beside the verdicts when it falls 1800 seconds later (`older`, B's edit the
older) or earlier (`newer`). Listed where the three are not all alike; the last column names the
condition whose verdict `same` shares.

| rule | record | edit | merge | older | same | newer | same as |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R1 | retirement of a0 | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | retirement of a0 | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | retirement of a0 | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R2 | retirement of a0 | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R6 | retirement of a0 | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R2 | retirement of a0 | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R6 | retirement of a0 | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R2 | retirement of a0 | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R6 | retirement of a0 | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R2 | retirement of a0 | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R6 | retirement of a0 | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R1 | retirement of a0 | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R2 | retirement of a0 | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R3 | retirement of a0 | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R6 | retirement of a0 | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | Sync into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | duplicated, original edited | OIN into A | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | duplicated, original edited | Sync into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | OIN into B | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | retirement of a0 | duplicated, original edited | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | duplicated, original edited | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | duplicated, original edited | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | retirement of a0 | duplicated, original edited | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | revocation of c3 | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | revocation of c3 | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | revocation of c3 | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | revocation of c3 | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R2 | revocation of c3 | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R6 | revocation of c3 | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R1 | revocation of c3 | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R2 | revocation of c3 | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R6 | revocation of c3 | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R1 | revocation of c3 | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R2 | revocation of c3 | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R6 | revocation of c3 | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R1 | revocation of c3 | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R2 | revocation of c3 | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R6 | revocation of c3 | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R1 | revocation of c3 | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R2 | revocation of c3 | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R3 | revocation of c3 | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R6 | revocation of c3 | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | revocation of c3 | duplicated, original edited | Sync into A | unchanged | refusing | refusing | newer |
| R4 | revocation of c3 | duplicated, original edited | OIN into A | unchanged | refusing | refusing | newer |
| R4 | revocation of c3 | duplicated, original edited | Sync into B | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | OIN into B | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | revocation of c3 | duplicated, original edited | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | revocation of c3 | duplicated, original edited | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | revocation of c3 | duplicated, original edited | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | revocation of c3 | duplicated, original edited | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | removal of legacy | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | removal of legacy | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | removal of legacy | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | removal of legacy | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | removal of legacy | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R2 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R3 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R4 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R5 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R6 | removal of legacy | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R1 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R2 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R5 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R6 | removal of legacy | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R2 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R5 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R6 | removal of legacy | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R2 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R5 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R6 | removal of legacy | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R1 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R2 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R3 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R5 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R6 | removal of legacy | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | removal of legacy | duplicated, original edited | Sync into A | unchanged | refusing | refusing | newer |
| R4 | removal of legacy | duplicated, original edited | OIN into A | unchanged | refusing | refusing | newer |
| R4 | removal of legacy | duplicated, original edited | Sync into B | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | OIN into B | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | removal of legacy | duplicated, original edited | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | removal of legacy | duplicated, original edited | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | removal of legacy | duplicated, original edited | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | removal of legacy | duplicated, original edited | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | recycle bin marker | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | Sync into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | OIN into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | Sync into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | OIN into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | one character of the payload | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | Sync into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | OIN into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | Sync into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | OIN into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string renamed | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | Sync into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | OIN into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | Sync into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | OIN into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | payload string deleted | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | Sync into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | OIN into A | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | Sync into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | OIN into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | xc 2.4.2 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | xc 2.6.6 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | xc 2.7.12 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | recycle bin marker | every string emptied | xc 2.8.0 into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | xc 2.4.2 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | xc 2.6.6 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | xc 2.7.12 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R3 | recycle bin marker | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R4 | recycle bin marker | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | recycle bin marker | every string emptied | xc 2.8.0 into B | unchanged | wider | wider | newer |
| R1 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R2 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R5 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R6 | recycle bin marker | deleted with a deletion record | Sync into A | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R2 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R5 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R6 | recycle bin marker | deleted with a deletion record | Sync into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R2 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R5 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R6 | recycle bin marker | deleted with a deletion record | OIN into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R2 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R5 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R6 | recycle bin marker | deleted with a deletion record | OE into B | unchanged | unchanged | wider | older |
| R1 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R2 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R3 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R5 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R6 | recycle bin marker | deleted with a deletion record | KE into B | unchanged | unchanged | wider | older |
| R4 | recycle bin marker | duplicated, original edited | Sync into A | unchanged | refusing | refusing | newer |
| R4 | recycle bin marker | duplicated, original edited | OIN into A | unchanged | refusing | refusing | newer |
| R4 | recycle bin marker | duplicated, original edited | Sync into B | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | OIN into B | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | recycle bin marker | duplicated, original edited | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | recycle bin marker | duplicated, original edited | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | recycle bin marker | duplicated, original edited | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | recycle bin marker | duplicated, original edited | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | Sync into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | OIN into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | Sync into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | OIN into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | one character of the payload | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | Sync into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | OIN into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | Sync into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | OIN into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string renamed | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | Sync into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | OIN into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | Sync into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | OIN into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | payload string deleted | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | Sync into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | OIN into A | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | Sync into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | OIN into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | xc 2.4.2 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | xc 2.6.6 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | xc 2.7.12 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | every string emptied | xc 2.8.0 into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | xc 2.4.2 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | xc 2.6.6 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | xc 2.7.12 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R2 | admission record of c1 (control) | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R3 | admission record of c1 (control) | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R4 | admission record of c1 (control) | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R5 | admission record of c1 (control) | every string emptied | xc 2.8.0 into B | unchanged | refusing | refusing | newer |
| R1 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R6 | admission record of c1 (control) | deleted with a deletion record | Sync into A | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R6 | admission record of c1 (control) | deleted with a deletion record | Sync into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R6 | admission record of c1 (control) | deleted with a deletion record | OIN into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R6 | admission record of c1 (control) | deleted with a deletion record | OE into B | unchanged | unchanged | refusing | older |
| R1 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R2 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R3 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R4 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R5 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |
| R6 | admission record of c1 (control) | deleted with a deletion record | KE into B | unchanged | unchanged | refusing | older |

1100 rows.
