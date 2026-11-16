# The shortest sequences that end wider, by part and rule

Steps: each edit in B or A, a script's change, a maintenance run, and the merge.

## Part 1: history limits

| rule | steps | edit | records: merges | what is wider |
| --- | --- | --- | --- | --- |
| R1 | 1 | limits-byte-1 | marker: B alone | g:bin not hidden |
| R1 | 1 | limits-default-1 | marker: B alone | g:bin not hidden |
| R1 | 1 | limits-none-1 | marker: B alone | g:bin not hidden |
| R1 | 1 | limits-one-1 | marker: B alone | g:bin not hidden |
| R1 | 1 | limits-unlimited-1 | marker: B alone | g:bin not hidden |
| R1 | 1 | limits-byte-1 | removal: B alone | legacy on the list |
| R1 | 1 | limits-default-1 | removal: B alone | legacy on the list |
| R1 | 1 | limits-none-1 | removal: B alone | legacy on the list |
| R1 | 1 | limits-one-1 | removal: B alone | legacy on the list |
| R1 | 1 | limits-unlimited-1 | removal: B alone | legacy on the list |
| R1 | 1 | limits-byte-1 | revocation: B alone | c3 admitted |
| R1 | 1 | limits-default-1 | revocation: B alone | c3 admitted |
| R1 | 1 | limits-none-1 | revocation: B alone | c3 admitted |
| R1 | 1 | limits-one-1 | revocation: B alone | c3 admitted |
| R1 | 1 | limits-unlimited-1 | revocation: B alone | c3 admitted |
| R2 | 1 | limits-byte-1 | marker: B alone | g:bin not hidden |
| R2 | 1 | limits-none-1 | marker: B alone | g:bin not hidden |
| R2 | 1 | limits-byte-1 | removal: B alone | legacy on the list |
| R2 | 1 | limits-none-1 | removal: B alone | legacy on the list |
| R2 | 1 | limits-byte-1 | revocation: B alone | c3 admitted |
| R2 | 1 | limits-none-1 | revocation: B alone | c3 admitted |
| R3 | 1 | limits-byte-1 | marker: B alone | g:bin not hidden |
| R3 | 1 | limits-default-1 | marker: B alone | g:bin not hidden |
| R3 | 1 | limits-none-1 | marker: B alone | g:bin not hidden |
| R3 | 1 | limits-one-1 | marker: B alone | g:bin not hidden |
| R3 | 1 | limits-unlimited-1 | marker: B alone | g:bin not hidden |
| R3 | 1 | limits-byte-1 | removal: B alone | legacy on the list |
| R3 | 1 | limits-default-1 | removal: B alone | legacy on the list |
| R3 | 1 | limits-none-1 | removal: B alone | legacy on the list |
| R3 | 1 | limits-one-1 | removal: B alone | legacy on the list |
| R3 | 1 | limits-unlimited-1 | removal: B alone | legacy on the list |
| R3 | 1 | limits-byte-1 | revocation: B alone | c3 admitted |
| R3 | 1 | limits-default-1 | revocation: B alone | c3 admitted |
| R3 | 1 | limits-none-1 | revocation: B alone | c3 admitted |
| R3 | 1 | limits-one-1 | revocation: B alone | c3 admitted |
| R3 | 1 | limits-unlimited-1 | revocation: B alone | c3 admitted |
| R4 | none | - | - | - |
| R5 | 1 | limits-byte-1 | marker: B alone | g:bin not hidden |
| R5 | 1 | limits-default-1 | marker: B alone | g:bin not hidden |
| R5 | 1 | limits-none-1 | marker: B alone | g:bin not hidden |
| R5 | 1 | limits-one-1 | marker: B alone | g:bin not hidden |
| R5 | 1 | limits-unlimited-1 | marker: B alone | g:bin not hidden |
| R5 | 1 | limits-byte-1 | removal: B alone | legacy on the list |
| R5 | 1 | limits-default-1 | removal: B alone | legacy on the list |
| R5 | 1 | limits-none-1 | removal: B alone | legacy on the list |
| R5 | 1 | limits-one-1 | removal: B alone | legacy on the list |
| R5 | 1 | limits-unlimited-1 | removal: B alone | legacy on the list |
| R6 | none | - | - | - |

## Part 2: Database Maintenance

| rule | steps | edit | records: merges | what is wider |
| --- | --- | --- | --- | --- |
| R1 | 1 | maintenance-after-30 | marker: B alone | g:bin not hidden |
| R1 | 1 | maintenance-after-30 | removal: B alone | legacy on the list |
| R1 | 1 | maintenance-after-30 | revocation: B alone | c3 admitted |
| R2 | 2 | maintenance-before-30 | marker: B alone | g:bin not hidden |
| R2 | 2 | maintenance-before-30 | removal: B alone | legacy on the list |
| R2 | 2 | maintenance-before-30 | revocation: B alone | c3 admitted |
| R3 | 1 | maintenance-after-30 | marker: B alone | g:bin not hidden |
| R3 | 1 | maintenance-after-30 | removal: B alone | legacy on the list |
| R3 | 1 | maintenance-after-30 | revocation: B alone | c3 admitted |
| R4 | none | - | - | - |
| R5 | 1 | maintenance-after-30 | marker: B alone | g:bin not hidden |
| R5 | 1 | maintenance-after-30 | removal: B alone | legacy on the list |
| R6 | none | - | - | - |

## Part 3: an edit that leaves no history

| rule | steps | edit | records: merges | what is wider |
| --- | --- | --- | --- | --- |
| R1 | 1 | library-character | marker: B alone | g:bin not hidden |
| R1 | 1 | library-delete-string | marker: B alone | g:bin not hidden |
| R1 | 1 | library-empty | marker: B alone | g:bin not hidden |
| R1 | 1 | library-rename | marker: B alone | g:bin not hidden |
| R1 | 1 | pykeepass-character | marker: B alone | g:bin not hidden |
| R1 | 1 | pykeepass-delete-string | marker: B alone | g:bin not hidden |
| R1 | 1 | library-character | removal: B alone | legacy on the list |
| R1 | 1 | library-delete-string | removal: B alone | legacy on the list |
| R1 | 1 | library-empty | removal: B alone | legacy on the list |
| R1 | 1 | library-rename | removal: B alone | legacy on the list |
| R1 | 1 | pykeepass-character | removal: B alone | legacy on the list |
| R1 | 1 | pykeepass-delete-string | removal: B alone | legacy on the list |
| R1 | 1 | library-character | revocation: B alone | c3 admitted |
| R1 | 1 | library-delete-string | revocation: B alone | c3 admitted |
| R1 | 1 | library-empty | revocation: B alone | c3 admitted |
| R1 | 1 | library-rename | revocation: B alone | c3 admitted |
| R1 | 1 | pykeepass-character | revocation: B alone | c3 admitted |
| R1 | 1 | pykeepass-delete-string | revocation: B alone | c3 admitted |
| R2 | 1 | library-character | marker: B alone | g:bin not hidden |
| R2 | 1 | library-delete-string | marker: B alone | g:bin not hidden |
| R2 | 1 | library-empty | marker: B alone | g:bin not hidden |
| R2 | 1 | library-rename | marker: B alone | g:bin not hidden |
| R2 | 1 | pykeepass-character | marker: B alone | g:bin not hidden |
| R2 | 1 | pykeepass-delete-string | marker: B alone | g:bin not hidden |
| R2 | 1 | library-character | removal: B alone | legacy on the list |
| R2 | 1 | library-delete-string | removal: B alone | legacy on the list |
| R2 | 1 | library-empty | removal: B alone | legacy on the list |
| R2 | 1 | library-rename | removal: B alone | legacy on the list |
| R2 | 1 | pykeepass-character | removal: B alone | legacy on the list |
| R2 | 1 | pykeepass-delete-string | removal: B alone | legacy on the list |
| R2 | 1 | library-character | revocation: B alone | c3 admitted |
| R2 | 1 | library-delete-string | revocation: B alone | c3 admitted |
| R2 | 1 | library-empty | revocation: B alone | c3 admitted |
| R2 | 1 | library-rename | revocation: B alone | c3 admitted |
| R2 | 1 | pykeepass-character | revocation: B alone | c3 admitted |
| R2 | 1 | pykeepass-delete-string | revocation: B alone | c3 admitted |
| R3 | 1 | library-character | marker: B alone | g:bin not hidden |
| R3 | 1 | library-delete-string | marker: B alone | g:bin not hidden |
| R3 | 1 | library-empty | marker: B alone | g:bin not hidden |
| R3 | 1 | library-rename | marker: B alone | g:bin not hidden |
| R3 | 1 | pykeepass-character | marker: B alone | g:bin not hidden |
| R3 | 1 | pykeepass-delete-string | marker: B alone | g:bin not hidden |
| R3 | 1 | library-character | removal: B alone | legacy on the list |
| R3 | 1 | library-delete-string | removal: B alone | legacy on the list |
| R3 | 1 | library-empty | removal: B alone | legacy on the list |
| R3 | 1 | library-rename | removal: B alone | legacy on the list |
| R3 | 1 | pykeepass-character | removal: B alone | legacy on the list |
| R3 | 1 | pykeepass-delete-string | removal: B alone | legacy on the list |
| R3 | 1 | library-character | revocation: B alone | c3 admitted |
| R3 | 1 | library-delete-string | revocation: B alone | c3 admitted |
| R3 | 1 | library-empty | revocation: B alone | c3 admitted |
| R3 | 1 | library-rename | revocation: B alone | c3 admitted |
| R3 | 1 | pykeepass-character | revocation: B alone | c3 admitted |
| R3 | 1 | pykeepass-delete-string | revocation: B alone | c3 admitted |
| R4 | none | - | - | - |
| R5 | 1 | library-character | marker: B alone | g:bin not hidden |
| R5 | 1 | library-delete-string | marker: B alone | g:bin not hidden |
| R5 | 1 | library-empty | marker: B alone | g:bin not hidden |
| R5 | 1 | library-rename | marker: B alone | g:bin not hidden |
| R5 | 1 | pykeepass-character | marker: B alone | g:bin not hidden |
| R5 | 1 | pykeepass-delete-string | marker: B alone | g:bin not hidden |
| R5 | 1 | library-character | removal: B alone | legacy on the list |
| R5 | 1 | library-delete-string | removal: B alone | legacy on the list |
| R5 | 1 | library-empty | removal: B alone | legacy on the list |
| R5 | 1 | library-rename | removal: B alone | legacy on the list |
| R5 | 1 | pykeepass-character | removal: B alone | legacy on the list |
| R5 | 1 | pykeepass-delete-string | removal: B alone | legacy on the list |
| R6 | none | - | - | - |

## Part 4: an edit in both copies

| rule | steps | edit | records: merges | what is wider |
| --- | --- | --- | --- | --- |
| R1 | 1 | both-character newer | marker: A alone | g:bin not hidden |
| R1 | 1 | both-character older | marker: A alone | g:bin not hidden |
| R1 | 1 | both-character same | marker: A alone | g:bin not hidden |
| R1 | 1 | both-character newer | removal: A alone | legacy on the list |
| R1 | 1 | both-character older | removal: A alone | legacy on the list |
| R1 | 1 | both-character same | removal: A alone | legacy on the list |
| R1 | 1 | both-character newer | revocation: A alone | c3 admitted |
| R1 | 1 | both-character older | revocation: A alone | c3 admitted |
| R1 | 1 | both-character same | revocation: A alone | c3 admitted |
| R2 | 1 | both-character newer | marker: A alone | g:bin not hidden |
| R2 | 1 | both-character older | marker: A alone | g:bin not hidden |
| R2 | 1 | both-character same | marker: A alone | g:bin not hidden |
| R2 | 1 | both-character newer | removal: A alone | legacy on the list |
| R2 | 1 | both-character older | removal: A alone | legacy on the list |
| R2 | 1 | both-character same | removal: A alone | legacy on the list |
| R2 | 1 | both-character newer | revocation: A alone | c3 admitted |
| R2 | 1 | both-character older | revocation: A alone | c3 admitted |
| R2 | 1 | both-character same | revocation: A alone | c3 admitted |
| R3 | 1 | both-character newer | marker: A alone | g:bin not hidden |
| R3 | 1 | both-character older | marker: A alone | g:bin not hidden |
| R3 | 1 | both-character same | marker: A alone | g:bin not hidden |
| R3 | 1 | both-character newer | removal: A alone | legacy on the list |
| R3 | 1 | both-character older | removal: A alone | legacy on the list |
| R3 | 1 | both-character same | removal: A alone | legacy on the list |
| R3 | 1 | both-character newer | revocation: A alone | c3 admitted |
| R3 | 1 | both-character older | revocation: A alone | c3 admitted |
| R3 | 1 | both-character same | revocation: A alone | c3 admitted |
| R4 | none | - | - | - |
| R5 | 1 | both-character newer | marker: A alone | g:bin not hidden |
| R5 | 1 | both-character older | marker: A alone | g:bin not hidden |
| R5 | 1 | both-character same | marker: A alone | g:bin not hidden |
| R5 | 1 | both-character newer | removal: A alone | legacy on the list |
| R5 | 1 | both-character older | removal: A alone | legacy on the list |
| R5 | 1 | both-character same | removal: A alone | legacy on the list |
| R6 | none | - | - | - |

## Part 5: a history item tampered with

| rule | steps | edit | records: merges | what is wider |
| --- | --- | --- | --- | --- |
| R1 | 2 | tamper-edit | marker: B alone | g:bin not hidden |
| R1 | 2 | tamper-remove | marker: B alone | g:bin not hidden |
| R1 | 2 | tamper-edit | removal: B alone | legacy on the list |
| R1 | 2 | tamper-remove | removal: B alone | legacy on the list |
| R1 | 2 | tamper-edit | revocation: B alone | c3 admitted |
| R1 | 2 | tamper-remove | revocation: B alone | c3 admitted |
| R2 | 2 | tamper-edit | marker: B alone | g:bin not hidden |
| R2 | 2 | tamper-remove | marker: B alone | g:bin not hidden |
| R2 | 2 | tamper-edit | removal: B alone | legacy on the list |
| R2 | 2 | tamper-remove | removal: B alone | legacy on the list |
| R2 | 2 | tamper-edit | revocation: B alone | c3 admitted |
| R2 | 2 | tamper-remove | revocation: B alone | c3 admitted |
| R3 | 2 | tamper-edit | marker: B alone | g:bin not hidden |
| R3 | 2 | tamper-remove | marker: B alone | g:bin not hidden |
| R3 | 2 | tamper-edit | removal: B alone | legacy on the list |
| R3 | 2 | tamper-remove | removal: B alone | legacy on the list |
| R3 | 2 | tamper-edit | revocation: B alone | c3 admitted |
| R3 | 2 | tamper-remove | revocation: B alone | c3 admitted |
| R4 | none | - | - | - |
| R5 | 2 | tamper-edit | marker: B alone | g:bin not hidden |
| R5 | 2 | tamper-remove | marker: B alone | g:bin not hidden |
| R5 | 2 | tamper-edit | removal: B alone | legacy on the list |
| R5 | 2 | tamper-remove | removal: B alone | legacy on the list |
| R6 | none | - | - | - |
