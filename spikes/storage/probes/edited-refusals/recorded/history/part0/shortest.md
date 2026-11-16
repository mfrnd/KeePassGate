# The shortest sequences that end wider

Steps: B's edit (1); A's own title edit, where the time condition has one (1); a merge (1). The
copy that was edited, read as it is, takes one step. Every sequence that ends wider is listed by
its length and by the copy it ends in.

## R1

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | one character of the payload | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string deleted | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | every string emptied | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string renamed | B alone | g:bin not hidden |
| B, as edited | removal of legacy | one character of the payload | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string deleted | B alone | legacy on the list |
| B, as edited | removal of legacy | every string emptied | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string renamed | B alone | legacy on the list |
| B, as edited | revocation of c3 | one character of the payload | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted without a record | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted with a deletion record | B alone | c3 admitted |
| B, as edited | revocation of c3 | payload string deleted | B alone | c3 admitted |
| B, as edited | revocation of c3 | every string emptied | B alone | c3 admitted |
| B, as edited | revocation of c3 | payload string renamed | B alone | c3 admitted |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | removal of legacy | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | revocation of c3 | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | deleted with a deletion record | Sync into A | c3 admitted |
| A | revocation of c3 | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| B | recycle bin marker | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | revocation of c3 | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | removal of legacy | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | revocation of c3 | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | one character of the payload, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| B | recycle bin marker | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | revocation of c3 | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | one character of the payload, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |

## R2

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | revocation of c3 | deleted without a record | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted with a deletion record | B alone | c3 admitted |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |

## R3

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | one character of the payload | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string deleted | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | every string emptied | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string renamed | B alone | g:bin not hidden |
| B, as edited | removal of legacy | one character of the payload | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string deleted | B alone | legacy on the list |
| B, as edited | removal of legacy | every string emptied | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string renamed | B alone | legacy on the list |
| B, as edited | revocation of c3 | one character of the payload | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted without a record | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted with a deletion record | B alone | c3 admitted |
| B, as edited | revocation of c3 | payload string deleted | B alone | c3 admitted |
| B, as edited | revocation of c3 | every string emptied | B alone | c3 admitted |
| B, as edited | revocation of c3 | payload string renamed | B alone | c3 admitted |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | removal of legacy | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | revocation of c3 | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | deleted with a deletion record | Sync into A | c3 admitted |
| A | revocation of c3 | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| B | recycle bin marker | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | revocation of c3 | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | removal of legacy | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | revocation of c3 | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | one character of the payload, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit later | OE into A | c3 admitted |
| A | revocation of c3 | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | c3 admitted |
| B | recycle bin marker | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | revocation of c3 | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | one character of the payload, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit later | KE into B | c3 admitted |
| B | revocation of c3 | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | c3 admitted |

## R4

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | revocation of c3 | deleted without a record | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted with a deletion record | B alone | c3 admitted |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |

## R5

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | one character of the payload | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string deleted | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | every string emptied | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | payload string renamed | B alone | g:bin not hidden |
| B, as edited | removal of legacy | one character of the payload | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string deleted | B alone | legacy on the list |
| B, as edited | removal of legacy | every string emptied | B alone | legacy on the list |
| B, as edited | removal of legacy | payload string renamed | B alone | legacy on the list |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | removal of legacy | one character of the payload | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| B | recycle bin marker | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit later | OE into A | g:bin not hidden |
| A | recycle bin marker | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | g:bin not hidden |
| A | removal of legacy | one character of the payload, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | one character of the payload, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string deleted, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | every string emptied, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit earlier | Sync into A, OIN into A, OE into A, xc 2.4.2 into A, xc 2.6.6 into A, xc 2.7.12 into A, xc 2.8.0 into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit later | OE into A | legacy on the list |
| A | removal of legacy | payload string renamed, A's title edit in the same second | Sync into A, OIN into A, OE into A | legacy on the list |
| B | recycle bin marker | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit later | KE into B | g:bin not hidden |
| B | recycle bin marker | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | g:bin not hidden |
| B | removal of legacy | one character of the payload, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | one character of the payload, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string deleted, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | every string emptied, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit earlier | Sync into B, OIN into B, KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit later | KE into B | legacy on the list |
| B | removal of legacy | payload string renamed, A's title edit in the same second | KE into B, xc 2.4.2 into B, xc 2.6.6 into B, xc 2.7.12 into B, xc 2.8.0 into B | legacy on the list |

## R6

### 1 step

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| B, as edited | recycle bin marker | deleted without a record | B alone | g:bin not hidden |
| B, as edited | recycle bin marker | deleted with a deletion record | B alone | g:bin not hidden |
| B, as edited | removal of legacy | deleted without a record | B alone | legacy on the list |
| B, as edited | removal of legacy | deleted with a deletion record | B alone | legacy on the list |
| B, as edited | revocation of c3 | deleted without a record | B alone | c3 admitted |
| B, as edited | revocation of c3 | deleted with a deletion record | B alone | c3 admitted |

### 2 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |

### 3 steps

| ends in | record | edit | merges | what is wider |
| --- | --- | --- | --- | --- |
| A | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into A | g:bin not hidden |
| A | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into A | legacy on the list |
| A | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into A | c3 admitted |
| B | recycle bin marker | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | g:bin not hidden |
| B | removal of legacy | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | legacy on the list |
| B | revocation of c3 | deleted with a deletion record, A's title edit earlier | Sync into B, OIN into B, OE into B, KE into B | c3 admitted |
