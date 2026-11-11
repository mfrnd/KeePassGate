# KeePassGate: what KeePass and other programs do to a database's custom data

**Measurements, kept for reference and never merged.** KeePassGate keeps its certificate authority,
its admission records and its revocations in `PwDatabase.CustomData`, and a database that is
synchronized between machines is merged by KeePass key by key. Whether a revocation or a recreated
authority survives that merge is therefore a question about KeePass rather than about the plugin,
and it was answered by running KeePass.

The same file can also be opened by another program. What KeePassXC does to it when it saves and
when it merges, and what KeePass 2.47 does when it saves, was answered the same way: by running
six KeePassXC releases from 2.4.1 to 2.8.0, chosen where its behavior changes, and KeePass 2.47
against throwaway databases shaped like the plugin's.

So was what the Android programs that open KDBX 4 files do to it: Keepass2Android, KeePassDX and
KeePassDroid, six releases among them, run in an emulator against the same kind of database, each
saving it and, where the program has a merge, merging two copies.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. What the probe settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside
it under `spikes/revocation/output`.

## Running it

Provide the pinned KeePass first, into a directory that is not committed:

```powershell
. .\tools\KeePass.ps1
Install-KeePass
```

Then build and run the probe. The first argument is a work directory for the throwaway databases it
creates; the second chooses the question.

```powershell
dotnet build spikes\revocation -c Release -o spikes\revocation\out
spikes\revocation\out\Probe.exe work\rules rules     > rules.txt    # the rule, one key at a time
spikes\revocation\out\Probe.exe work\matrix          > matrix.txt   # revocation designs, about 2.5 minutes
spikes\revocation\out\Probe.exe work\layout layout   > layout.txt   # authority layout, about 4 minutes
```

Every file it writes goes under that work directory, which is not committed either. Point `TMP` and
`TEMP` somewhere disposable as well if the build should leave nothing behind in the profile.

## Running KeePassXC and KeePass 2.47 against it

They are provided the same way, each into a directory named after its version that is not
committed. `keepassxc.sha256` pins the KeePassXC releases and `keepass.sha256` the KeePass ones; a
release with no line there is refused, and each program's Authenticode signature is checked every
time it is asked for. `-Archive <zip>` takes an archive already downloaded in place of a download,
checked exactly as a download is.

```powershell
. .\tools\KeePassXC.ps1
Get-KeePassXCPin | ForEach-Object { Install-KeePassXC -Version $_.Version }
Install-KeePass -Version 2.47
```

Then, with the probe built as above, from Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -File spikes\revocation\Measure-KeePassXC.ps1 -Work work\keepassxc
```

That writes `keepassxc-save.txt`, every release saving a copy of P, KeePass 2.47 included, and
`keepassxc-merge.txt`, every release merging C and P both ways, three runs a cell, into
`spikes/revocation/output`. It takes about 50 minutes, nearly all of them the merges with a slow
KDF. `-Scenario save` or `-Scenario merge` runs only that scenario, and `-Release` and `-Runs` run
fewer; a run of fewer than were recorded takes `-Output` somewhere else, since each scenario
replaces its whole file.

The KDF change has a script of its own, because no `keepassxc-cli` among the pinned releases can
make it, so KeePassXC's own window is driven by UI automation. It is optional, it takes the desktop
for a minute or two, and it stops every KeePassXC it started:

```powershell
powershell.exe -NoProfile -File spikes\revocation\Measure-KeePassXCKdf.ps1 -Work work\keepassxc
```

It writes `keepassxc-kdf.txt` beside the others.

## Running the Android programs against it

They run in an emulator, and the emulator under Linux with KVM, which was WSL 2 here. Nothing is
drawn on a desktop: the emulator starts with no window, and `k.py` reads each screen through
`uiautomator` and presses what a flow file names. `android.sha256` pins each APK by its hash and by
the digest of the certificate that signed it, and the Android SDK's command-line tools by their
hash.

The scripts are in `spikes/revocation/android`. The shell scripts are the Linux side and need
`python3`; `kp.ps1` and `collect.ps1` are the Windows side, for Windows PowerShell 5.1, and make
the databases and read them back with the pinned KeePass. Both sides use one work directory,
`work/android` under the repository unless `KPG_ANDROID_WORK` names another, so the checkout has to
be where both reach it, as a Windows drive is from WSL. Nothing in it is committed.

Once, under Linux:

```sh
cd spikes/revocation/android
sudo sh root-setup.sh "$USER" install   # KVM, a JDK and the libraries the emulator needs
sh sdk-setup.sh                         # the SDK, into ~/android-sdk unless ANDROID_HOME is set
sh avd-create.sh                        # the device: Android 11, API 30, x86_64
sh fetch.sh                             # every pinned APK, held to its hash
sh verify-apks.sh                       # and to the certificate that signed it
```

KVM does not stay loaded across a restart of WSL; `sudo sh root-setup.sh "$USER"` loads it again.

Then the databases, from Windows PowerShell 5.1 at the top of the repository:

```powershell
function Kpg { powershell.exe -NoProfile -File spikes\revocation\android\kp.ps1 -KeePass .keepass\2.61.1 -Source Kpg.cs -Type Kpg @args }
$base = 'work\android\work\base'
Kpg make $base fast  argon2d 2 1024 1
Kpg make $base aes   aes 100000
Kpg make $base fastb argon2d 2 1024 1
Kpg make $base aesb  aes 100000
New-Item -ItemType Directory work\android\work\base2 | Out-Null
Kpg settings $base\C-fast.kdbx       work\android\work\base2\C-fast-s.kdbx       2026-11-10T12:00:00Z
Kpg settings $base\P-plain-fast.kdbx work\android\work\base2\P-plain-fast-s.kdbx 2026-11-10T12:00:00Z
```

`make` gives C and P the moment it runs as their `SettingsChanged`, and `settings` makes the copy
whose settings are the newer, so the time on those two lines has to be later than the moment the
bases were made. It was on the day of the run; a re-run gives a later time.

Then the runs, under Linux, with the emulator in the background:

```sh
K2A=keepass2android.keepass2android
DX=com.kunzisoft.keepass.libre
DX3=com.kunzisoft.keepass.free
KPD=com.android.keepass
SETS="P-plain-fast P-moved-fast P-plain-aes P-plain-fastb P-plain-aesb"

sh emu-start.sh &
sh wait-boot.sh

sh install.sh k2a-1.15-r6
sh run-A.sh k2a-1.15-r6 k2a-A.flow $K2A "$SETS"
sh run-B.sh k2a-1.15-r6 k2a-B.flow $K2A P-plain-fast C-fast PC
sh run-B.sh k2a-1.15-r6 k2a-B.flow $K2A C-fast P-plain-fast CP
sh run-B.sh k2a-1.15-r6 k2a-B.flow $K2A P-plain-fast ../base2/C-fast-s PCs
sh run-B.sh k2a-1.15-r6 k2a-B.flow $K2A C-fast ../base2/P-plain-fast-s CPs

sh install.sh kpdx-4.5.8
sh run-A.sh kpdx-4.5.8 kpdx-A.flow $DX "$SETS"
sh run-B.sh kpdx-4.5.8 kpdx-B-from.flow $DX P-plain-fast C-fast fromPC
sh run-B.sh kpdx-4.5.8 kpdx-B-from.flow $DX C-fast P-plain-fast fromCP
sh run-B.sh kpdx-4.5.8 kpdx-B-ext.flow  $DX P-plain-fast C-fast extPC
sh run-B.sh kpdx-4.5.8 kpdx-B-ext.flow  $DX C-fast P-plain-fast extCP
sh run-B.sh kpdx-4.5.8 kpdx-B-keep.flow $DX P-plain-fast C-fast keepPC 1

sh install.sh kpd-2.7
sh run-A.sh kpd-2.7      kpd-A.flow    $KPD "$SETS"
sh run-A.sh kpd-2.7-menu kpd-menu.flow $KPD P-plain-fast

sh install.sh k2a-1.08d-r5 replace
sh run-A.sh k2a-1.08d-r5 k2a-A.flow $K2A "$SETS"
sh run-B.sh k2a-1.08d-r5 k2a-B.flow $K2A P-plain-fast C-fast PC
sh run-B.sh k2a-1.08d-r5 k2a-B.flow $K2A C-fast P-plain-fast CP

sh install.sh kpdx-3.2.0
sh run-A.sh kpdx-3.2.0 kpdx320-A.flow $DX3 "$SETS"
sh run-B.sh kpdx-3.2.0 kpdx320-B-ext.flow $DX3 P-plain-fast C-fast extPC
sh run-B.sh kpdx-3.2.0 kpdx320-B-ext.flow $DX3 C-fast P-plain-fast extCP

sh install.sh kpdx-3.3.0 replace
sh run-A.sh kpdx-3.3.0 kpdx320-A.flow $DX3 "P-plain-fast P-moved-fast"

sh teardown.sh
```

`run-A.sh` is a plain save and `run-B.sh` a merge, three runs unless its last argument names
fewer. That is 27 saves, 37 merges and a walk through KeePassDroid's menus, each a run of its own
from the program's data cleared, and each leaving a step log under the work directory's `out` and
what the program wrote under its `work`. A line printed at the end of each run counts the steps
that failed; a run with a failure is repeated, not read.

Reading back is the Windows side again, a directory at a time:

```powershell
powershell.exe -NoProfile -File spikes\revocation\android\collect.ps1 -Work work\android -Dir work\A\k2a-1.15-r6 -Name A-k2a-1.15-r6 -KeePass .keepass\2.61.1
```

and the same for every directory under `work\A` and `work\B`.

## Reading a re-run

The output names times symbolically rather than printing them, so a re-run against the same KeePass
is line for line the recorded output. A difference is a finding: record it in `RESULTS.md` beside
what it contradicts.

The KeePassXC output names them by where they came from. `C` and `P` are the time that copy was
given, `=` a time both copies share, and `none` no time at all. `save`, `touch`, `dry` and `merge`
are a time inside the span in which another program ran: a save, the save that gives C KeePassXC's
own marker before a merge, a dry run and a merge. Each program starts in a second of its own, so a
time stored to the second names a single program. A value is `C`, `P` or `=` in the same way, `?`
a value neither copy holds and `-` absent. KeePassXC's random marker is `<random>`, and its date
marker `<date language, span>`.

Two kinds of cell depend on where a second boundary falls, by design, and every other cell should
come back exactly:

- **The `fast` merges.** Each merge starts just after a second begins and both copies load in a
  small fraction of it, so they load in the same second and KeePassXC changes no custom data. A
  merge slow enough to reach its second load in the next second gives the `slow` answer instead.
- **The `slow` merges** give their answer because each load takes more than a second, so the two
  always fall in different seconds. A machine fast enough to load both inside one second would turn
  them into `fast` cells.

And one value depends on the machine: the language of KeePassXC's date marker is the system's
before 2.8.0, recorded here as `de-DE`, and English from 2.8.0.

**The Android output is in another notation**, that of `Kpg.cs`, set out at the top of each
`android-*.txt`, and it prints times as they are. A re-run therefore differs from it in every time
a program wrote, in every hash, and in `SettingsChanged`, since `Kpg.cs` makes the databases anew.
What is compared is the first line of each file's block: the format, the value and the time of each
key by the copy it came from, and the rules on G and E1.

Those files are not written by a script. They were put together from the read-backs and the step
logs of the one run, with the work directory cut from the front of every path and the lines that
describe the machine left out. `collect.ps1` writes full paths, so what it writes is not for
committing as it comes back.
