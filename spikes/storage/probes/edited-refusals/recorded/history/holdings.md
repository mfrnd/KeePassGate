# What each result holds of the record under test

The revocation's entry; the other four records hold the same in every cell (`checks.txt`).
`O` the content as written, `E` the content of B's last edit, `F` the content of A's edit,
`T` anything else (a history item tampered with); history items in brackets, oldest first; `-`
no current version; `!` a deletion record naming the entry; `n:` a copy that Create new IDs made,
`@2` its group. The four keepassxc-cli releases share a column where they agree.

## Part 1: history limits

| edit | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| limits-byte-1 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-byte-10 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-byte-11 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-byte-12 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-byte-2 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-byte-3 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-default-1 | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| limits-default-10 | E[O,E,E,E,E,E,E,E,E,E] | O | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E] | O n:E@2[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | O[E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] n:O@2 | E[O,E,E,E,E,E,E,E,E,E] |
| limits-default-11 | E[E,E,E,E,E,E,E,E,E,E] | O | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | O[E,E,E,E,E,E,E,E,E,E] | O n:E@2[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | O[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] n:O@2 | E[E,E,E,E,E,E,E,E,E,E] |
| limits-default-12 | E[E,E,E,E,E,E,E,E,E,E] | O | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | O[E,E,E,E,E,E,E,E,E,E] | O n:E@2[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | O[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] | E[E,E,E,E,E,E,E,E,E,E] n:O@2 | E[E,E,E,E,E,E,E,E,E,E] |
| limits-default-2 | E[O,E] | O | E[O,E] | E[O,E] | E[O,E] | O[O,E] | O n:E@2[O,E] | E[O,E] | E[O,E] | E[O,E] | O[O,E,E] | E[O,E] | E[O,E] n:O@2 | E[O,E] |
| limits-default-3 | E[O,E,E] | O | E[O,E,E] | E[O,E,E] | E[O,E,E] | O[O,E,E] | O n:E@2[O,E,E] | E[O,E,E] | E[O,E,E] | E[O,E,E] | O[O,E,E,E] | E[O,E,E] | E[O,E,E] n:O@2 | E[O,E,E] |
| limits-none-1 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-none-10 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-none-11 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-none-12 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-none-2 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-none-3 | E | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| limits-one-1 | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[E] | E[O] | E[O] n:O@2 | E[O] |
| limits-one-10 | E[E] | O | E[E] | E[E] | E[E] | O[E] | O n:E@2[E] | E[E] | E[E] | E[E] | O[E] | E[E] | E[E] n:O@2 | E[E] |
| limits-one-11 | E[E] | O | E[E] | E[E] | E[E] | O[E] | O n:E@2[E] | E[E] | E[E] | E[E] | O[E] | E[E] | E[E] n:O@2 | E[E] |
| limits-one-12 | E[E] | O | E[E] | E[E] | E[E] | O[E] | O n:E@2[E] | E[E] | E[E] | E[E] | O[E] | E[E] | E[E] n:O@2 | E[E] |
| limits-one-2 | E[E] | O | E[E] | E[E] | E[E] | O[E] | O n:E@2[E] | E[E] | E[E] | E[E] | O[E] | E[E] | E[E] n:O@2 | E[E] |
| limits-one-3 | E[E] | O | E[E] | E[E] | E[E] | O[E] | O n:E@2[E] | E[E] | E[E] | E[E] | O[E] | E[E] | E[E] n:O@2 | E[E] |
| limits-unlimited-1 | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| limits-unlimited-10 | E[O,E,E,E,E,E,E,E,E,E] | O | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E] | O n:E@2[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E] n:O@2 | E[O,E,E,E,E,E,E,E,E,E] |
| limits-unlimited-11 | E[O,E,E,E,E,E,E,E,E,E,E] | O | E[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E,E] | O n:E@2[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E] n:O@2 | E[O,E,E,E,E,E,E,E,E,E,E] |
| limits-unlimited-12 | E[O,E,E,E,E,E,E,E,E,E,E,E] | O | E[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E,E,E] | O n:E@2[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | O[O,E,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] | E[O,E,E,E,E,E,E,E,E,E,E,E] n:O@2 | E[O,E,E,E,E,E,E,E,E,E,E,E] |
| limits-unlimited-2 | E[O,E] | O | E[O,E] | E[O,E] | E[O,E] | O[O,E] | O n:E@2[O,E] | E[O,E] | E[O,E] | E[O,E] | O[O,E,E] | E[O,E] | E[O,E] n:O@2 | E[O,E] |
| limits-unlimited-3 | E[O,E,E] | O | E[O,E,E] | E[O,E,E] | E[O,E,E] | O[O,E,E] | O n:E@2[O,E,E] | E[O,E,E] | E[O,E,E] | E[O,E,E] | O[O,E,E,E] | E[O,E,E] | E[O,E,E] n:O@2 | E[O,E,E] |

## Part 2: Database Maintenance

| edit | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| maintenance-after-30 | E[O] | O | E | E | E | O | O n:E@2 | E | E | E | O | E | E n:O@2 | E |
| maintenance-before-30 | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
| maintenance-before-365 | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |

## Part 3: an edit that leaves no history

| edit | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| library-character | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
| library-delete-string | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
| library-empty | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
| library-rename | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
| pykeepass-character | E | O | E | E | E[O] | O | O n:E@2 | O | O | O | O[E] | E | E n:O@2 | E |
| pykeepass-delete-string | E | O | E | E | E[O] | O | O n:E@2 | O | O | O | O[E] | E | E n:O@2 | E |

## Part 4: an edit in both copies

| edit | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| both-character newer | E[O] | F | E[O,F] | E[O,F] | E[O,F] | F[O,F] | F n:E@2[O] | E[O,F] | E[O,F] | E[O,F] | F[O,E] | E[O,F] | E[O] n:F@2 | E[O,F] |
| both-character older | E[O] | F | F[O,E] | F[O,E] | E[O,F] | F[O,E] | F n:E@2[O] | F[O,E] | F[O,E] | F[O,E] | F[O,E] | E[O,E] | E[O] n:F@2 | F[O,E] |
| both-character same | E[O] | F | E[O] | E[O] | E[O,F] | F[O] | F n:E@2[O] | F[O] | F[O] | F[O] | F[O,E] | E[O] | E[O] n:F@2 | E[O] |

## Part 5: a history item tampered with

| edit | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| tamper-edit | E[T] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[T] | E[T] | E[T] | E[T] | O[T,E] | E[T] | E[T] n:O@2 | E[T] |
| tamper-remove | E | O | E[O] | E[O] | E[O] | O[O] | O n:E@2 | E[O] | E[O] | E[O] | O[E] | E[O] | E n:O@2 | E[O] |
