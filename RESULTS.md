# What the probe settled

Why an application domain that has driven KeePass 2.61.1 through `System.Windows.Automation` cannot
be unloaded, and what ends the thing that holds it. Every row was measured on .NET Framework 4.8 on
the development machine, against a KeePass started with no database; the output of the run recorded
on this branch is under [`spikes/unload/output`](spikes/unload/output) as it came back, and an
earlier run of the same questions, before they were gathered into one program, said the same in
every row. [`README.md`](README.md) says how to run it.

| Question | Answer |
| --- | --- |
| Which call leaves the domain unable to unload? | **The first walk into the window's controls.** One `FindAll(TreeScope.Descendants, ...)` over the main window, and `AppDomain.Unload` throws `CannotUnloadAppDomainException` after about 15.3 seconds, every time. Finding the window by its handle and reading its `Name` or `AutomationId` unloads in milliseconds |
| What does that call leave behind? | `UIAutomationClientsideProviders`, loaded into the domain that asked, whose `WinEventTracker.GetCallbackQueue` starts a `QueueProcessor` on a background thread, held in the static `_callbackQueue` |
| Does registering no client-side providers prevent it? | **No.** `ClientSettings.RegisterClientSideProviders` with an empty array, before any other call, loads the same assembly, starts the same queue and fails the same unload, and the window's tree reads the same with it as without it |
| Can the queue be told to stop? | **No.** Its thread waits in `MsgWaitForMultipleObjects` with no timeout and leaves its loop only when that wait fails or times out; a `WM_QUIT` only drains the queue |
| What ends it? | **One work item through the queue's own way in** whose body records the thread and aborts it, with `_callbackQueue` cleared under `_queueLock`. The unload then succeeds in single milliseconds, and a later `FindAll` starts a fresh queue that ends the same way |
| Which end to end fixtures are affected? | The seven `Live` fixtures that read a dialog, each run on its own; the five that only find the main window are not |
| Verdict | **KeePassGate ends the queue thread at the end of the run, through `ClientSideProviderQueue`, from a set up fixture over the `Live` namespace.** Empty providers measured not to work, and `RunConfiguration.DisableAppDomain=true` not adopted |

## 1. What the desktop tests do to a menu, `output/menu.txt`

Each scenario finds the File menu among the main window's descendants, then goes further: takes its
expand pattern, opens and closes it, reads every menu item on the desktop while it is open, and
finally calls `Automation.RemoveAllEventHandlers` before the unload.

| Scenario | Unload |
| --- | --- |
| `find` | `CannotUnloadAppDomainException` after 15269 ms |
| `pattern` | after 15288 ms |
| `expand` | after 15312 ms |
| `expand-read`, 37 menu items seen | after 15274 ms |
| `expand-read-remove` | after 15287 ms |

Every one already fails at the first step, and taking every event handler away changes nothing,
since the probe registered none. Among the static fields of the automation assemblies that hold a
thread or a queue, only `WinEventTracker._callbackQueue` is set; `ClientEventManager._callbackQueue`,
the client's own event queue, stays null.

## 2. The smallest call, `output/minimal.txt`

Three runs of each scenario, each in a process of its own. The `empty-` scenarios register an empty
array of client-side providers before anything else.

| Scenario | Unload, three runs | Loaded | Queue |
| --- | --- | --- | --- |
| `nothing` | ok, 0 ms | `UIAutomationClient`, `UIAutomationTypes` | assembly not loaded |
| `empty-nothing` | ok, 0 to 1 ms | the providers and `UIAutomationProvider` as well | null |
| `handle`, `AutomationElement.FromHandle` alone | ok, 1 to 2 ms | the same four | null |
| `name`, and the window's `Name` read | ok, 1 ms | the same | null |
| `id`, and its `AutomationId` read | ok, 1 to 2 ms | the same | null |
| `findall`, one `FindAll` over the descendants | **15275, 15277 and 15288 ms** | the same | **set** |
| `empty-handle`, `empty-name`, `empty-id` | ok, 1 to 2 ms | the same | null |
| `empty-findall` | **15268, 15267 and 15270 ms** | the same | **set** |

So the providers' assembly arriving is harmless, and the queue is what is not. Registering the empty
set loads the providers itself and keeps none of what follows from happening.

`output/tree-default.txt` and `output/tree-empty.txt` are the main window's raw tree, six levels
deep, read without and with the empty registration. They differ only in the unload time on their
last line, 15274 ms and 15265 ms, so the managed defaults were not what answered for KeePass's
controls in the first place and taking them away buys nothing.

## 3. The queue, `output/members.txt`, `output/items.txt` and `output/il.txt`

Read off `UIAutomationClientsideProviders` 4.0.0.0 as the framework carries it.

- **`WinEventTracker.GetCallbackQueue`** takes `_queueLock`, and when `_callbackQueue` is null makes
  a `QueueProcessor`, stores it there and calls `StartOnThread`, which starts `WaitForWork` on a new
  thread marked as a background thread. The static constructor sets the field to null once; neither
  of the two methods read here ever sets it back.
- **`QueueProcessor.WaitForWork`** pumps the thread's messages, runs whatever is in `_q`, then calls
  `MsgWaitForMultipleObjects` on `_ev` with a timeout of `-1`, which is `INFINITE`, and a wake mask
  of 255, `QS_ALLINPUT`. It returns only when the wait answers `-1`, `WAIT_FAILED`, or 258,
  `WAIT_TIMEOUT`, which an infinite wait never does. A `WM_QUIT`, message 18, only breaks out of the
  pump to drain `_q`, and the loop goes round again. An exception that is not critical is swallowed
  and the loop goes round again too.
- **There is no method that stops it.** `QueueProcessor` declares `StartOnThread`,
  `PostSyncWorkItem` and `WaitForWork`, two constructors and the fields `_q` and `_ev`, and nothing
  else.
- **`PostSyncWorkItem`** wraps the item in a `SyncQueueItem`, enqueues it on `_q`, sets `_ev` and
  waits up to 2000 ms for the item to have run. That is the queue's own way in.
- **`QueueItem+MSAAWinEventItem`** is constructed from one delegate, and its `Process` is that
  delegate's `Invoke()` with no arguments, so any method taking none can be put through the queue.
  `WinEventTracker`'s own `StartStopDelegate`, which takes the hook parameters by reference, is a
  different type.

A thread waiting in native code takes an abort only when it returns to managed code, which this one
never does, and that is the fifteen seconds `AppDomain.Unload` spends before it gives up.

## 4. Ending the thread, `output/stop.txt`

`FindAll` over the window, then, for `stop`, `_callbackQueue` read and cleared under `_queueLock`,
and an `MSAAWinEventItem` enqueued on `_q` with `_ev` set, the way `PostSyncWorkItem` enqueues one
without its two second wait. The item's body records the thread it runs on and aborts it, and the
probe joins that thread before the unload. `stop-again` then calls `FindAll` a second time and ends
the second queue the same way.

| Scenario | Unload, three runs | Hook registrations in `_ahp` | Queue afterwards |
| --- | --- | --- | --- |
| `findall` | **16615, 15305 and 15272 ms** | 3 | set |
| `stop` | ok, 4, 5 and 5 ms; the thread ended within the millisecond | 3 | null |
| `stop-again` | ok, 8, 8 and 7 ms; the second `FindAll` set the queue again and it ended the same way | 3 | null |

The earlier run measured the same unloads at 6 to 9 ms. The three hook registrations belong to the
thread's message queue, which goes with the thread, so nothing can call into the domain through them
once it has gone. What the tracker remembers about them is left as it is, which is why ending the
queue is for the end of a run rather than the middle of one.

## 5. The end to end fixtures, an earlier measurement

**Not re-run here**: it needs the desktop for several minutes and a KeePassGate checkout with its
host tests built. `spikes/unload/each-fixture.ps1` is the runner, and this is the summary it wrote
on 2026-10-22, unelevated, against a checkout of the step that brought `ETag`, before the fix below
was in it. Every fixture passed every test; what differs is whether the engine reported
`NUnitEngineUnloadException`, "Exception encountered unloading application domain", at the end.

| Fixture | Tests | Unload failed | Seconds |
| --- | --- | --- | --- |
| `LiveApiTests` | 9 | no | 6 |
| `LiveAuthorityChooserTests` | 13 | **yes** | 27 |
| `LiveAuthorityTests` | 6 | **yes** | 32 |
| `LiveCertificateTests` | 4 | no | 4 |
| `LiveGateTests` | 12 | no | 5 |
| `LiveGrantEditorTests` | 4 | **yes** | 28 |
| `LiveKeePassTests` | 10 | **yes** | 25 |
| `LiveLockTests` | 5 | no | 6 |
| `LiveLockTimerTests` | 2 | no | 16 |
| `LivePairingTests` | 11 | **yes** | 38 |
| `LiveReferenceClientTests` | 15 | **yes** | 38 |
| `LiveRenewalTests` | 7 | **yes** | 33 |

The seven that fail read a dialog, which means walking into a window's controls; the five that do
not only find the main window. That is the line section 2 draws, seen from the test tier.

## 6. The verdict

**KeePassGate ends the queue thread at the end of the run.** A set up fixture over the `Live`
namespace, `LiveRun`, calls `ClientSideProviderQueue.End` from its one-time tear down, which does
what `stop` does here: the field cleared under the framework's lock, and one item through the
queue's own way in that ends the thread it runs on. Every name it reaches is private to .NET
Framework 4.8, the last of its line, and a host test pins them so that a name that has gone fails a
test rather than leaving a run to end the old way unnoticed.

**Registering an empty set of client-side providers was refused on measurement**: section 2 shows it
starting the same queue and failing the same unload.

**`RunConfiguration.DisableAppDomain=true` was not adopted.** It would have NUnit run the tests in
the test host's own domain, which is never unloaded, so the thread would not matter. Over the whole
host test assembly it broke four tests with a `FileLoadException` for `System.Buffers` in the themed
child domain `GrantTabFitsTests` makes. Set for the `Live` run alone, it would load the test code
under `testhost.net48.exe.config`'s binding redirects instead of the test assembly's own, unlike
every other tier, so the end to end tier would run against a different binding than the tiers it is
meant to confirm.
