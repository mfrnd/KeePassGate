# Results

**A read on a connection thread meets KeePass's own thread changing the tree, and nothing on the
reading side can tell; holding KeePass's thread still for the read closes it, and taking the hold on
`Application.Idle` keeps the operator's input and KeePass's timers ahead of it.** Measured on .NET
Framework 4.8 on Windows 11, against KeePass 2.61.1's KeePassLib, on 2026-11-01 and 2026-11-02.

## What a read meets

| | |
| --- | --- |
| `FindEntry` with the entry's group last in its list, while groups go in at the front and come off it | 20,762 misses in 2.07 million lookups |
| the same with the group in the middle of its list | none to two in two million |
| `FindEntry`, `FindGroup`, `GetEntries` and `GetGroups` meeting a slot a removal emptied | `NullReferenceException` inside KeePassLib; `GetEntries`' copy keeps the null |
| `PwObjectList.Add`, `Insert`, `AddEntry` and `AddGroup` given a null | refused, so a null in a list comes only from a list in motion |

## What telling it apart costs

| | |
| --- | --- |
| `List<T>._version` read before and after a walk | caught 5 to 27 of about 12,000 misses |
| the same, waiting 100 µs before trusting it | still missed 32, and flagged 7,350 of 7,814 good walks |

The version moves last: `List<T>` changes its size first, so a writer preempted between the two
leaves a list that is wrong with nothing to show it.

## What holding the thread costs

| | |
| --- | --- |
| a reader holding KeePass's thread in a callback for each walk | no misses and nothing thrown, in 71,503 and 46,297 walks |
| a reader beside it, holding nothing | 224 and 128 misses, 91,770 and 58,745 exceptions |
| waiting for the hold to begin | 0.07 ms at the median, under 1 ms at p99, 115 ms at worst, which was the longest handler |

How long one of the plugin's reads holds the thread, against its own `KdbxVault` over 1,000 groups
and 10,000 entries, built from `master` as review 4 found it:

| read | Release, median | Debug, median |
| --- | --- | --- |
| a full sweep | 16.9 ms, about 41 at worst | 19.3 ms |
| a shallow snapshot of every reference | about 10 ms | about 10 ms |
| the structure alone | 0.055 ms | 0.055 ms |
| one lookup | 0.17 ms | 0.17 ms |

**A hold over the structure alone would not be exact**, because KeePass changes some live entries in
place outside its dialogs, read off `Mutators` and `FieldWriters`: `EntryUtil.FillPlaceholders` for a
placeholder that makes a value, `MainForm.ExecuteBinaryOpen` through `OnEntryBinaryClick`,
`MultipleValuesEntryContext.ApplyChanges`, `XmlUtil.PerformXmlReplace`, very likely
`OnEntryOtpGenSettings`, and `PwDatabase.MergeIn` through `MergeInDbProperties`, which writes the
database's own custom data. A `ProtectedString` and a `ProtectedBinary` never change once made, read
off `Immutable`: their only other writes are caches and their encryption, under their own lock.

## Taking the turn

`UiPriority`, 32 readers each holding the window's thread for 17 ms at a time, a mouse move sent to
the window every 40 ms, and a 100 ms timer on the same window.

| mode | moves delivered | input, p50 / p99 | timer, longest gap | holds |
| --- | --- | --- | --- | --- |
| no readers | all | 0.6 to 0.9 / 2.6 ms | 112 ms | none |
| `plain`: a `BeginInvoke` per hold | 1 of 60 within 3 s | | | 59 a second, the whole thread |
| `priority`: each callback asks `GetQueueStatus` for input and steps aside on a timer | all | 23.7 / 57.7 ms | not measured | 59 a second; waits up to 3.0 s, out of order |
| `queued`, the next turn posted after each hold | all | 3.4 / 53.7 ms | 597 to 630 ms whenever no input came | 97% of the thread |
| `queued`, every hold started on `Application.Idle` | all | 8 to 11 / 18 to 33 ms | 125 ms | 95% of the thread; about 555 ms each, in order |
| the same, inside a `MessageBox` | | | 129 ms | 15 a second, 26%, on the 50 ms fallback timer |
| the same, inside a WinForms `ShowDialog` | | | 125 ms | 95%, every one on Idle |
| the same, inside a `DoEvents` loop | | | 131 ms | 37 started inside it, none of them on Idle |

What the rows show:

- **Posted, a hold goes ahead of everything.** Windows hands out a posted message before input and
  makes a timer's message only once nothing else is queued, and WinForms runs every queued
  `BeginInvoke` callback in one go.
- **Asking for input first is not enough.** `GetQueueStatus` never reported the waiting timer, so
  turns posted one after another held it off for as long as no input came.
- **Started on Idle, a hold gives way to all of it.** Input waited at most for the read it arrived
  during, and the timer kept its time.
- **A message box's loop raises no Idle, and neither does `DoEvents`.** So a read needs the fallback
  timer there, and a turn that starts only on Idle never starts inside `DoEvents`. Which is how
  KeePass pumps through every long operation, read off `Mutators` over `Application.DoEvents` and
  `UIUtil.DoEventsByTime`: `SaveDatabase` through its status bar logger and `UIBlockInteraction`,
  the merges through their status loggers, `StatusProgressForm`, and `ExportUtil.Export`.

## Telling a native dialog from a form

`NativeLoop`, asking from another thread whether the window's thread has a visible `#32770` window,
a thousand times each:

| over the form | answer | one check |
| --- | --- | --- |
| nothing | no | 23.6 µs |
| a `MessageBox` | yes | 20.9 µs |
| the box closed | no | 20.7 µs |
| a WinForms `ShowDialog` | no | 30.0 µs |
| an `OpenFileDialog` | yes | 21.6 µs |
| the file dialog closed | no | 20.8 µs |
