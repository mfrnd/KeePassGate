# Results

**KeePassGate does not run in KeePass on Mono, and two of the reasons are in the runtime rather than
in the plugin.** Mono 6.8 has no `CertificateRequest` that a .NET Framework 4.8 build can reach and no
ECDSA at all, so no authority can be made; and its `X509Certificate2` cannot read an EC public key,
so no certificate the plugin issued, wherever it was issued, can be checked. Below those, four
WinForms differences and six `user32` calls stop the plugin's own windows, one of them badly enough
that opening KeePass's database settings with the plugin installed took KeePass's whole interface
down. The ruling that the plugin is Windows only (R9) stands on measurement now rather than on the
absence of one.

**Measured again at step 35**, which moved every certificate operation to Bouncy Castle: no test
fails on the plugin's certificate code any more, and both tiers pass far more, but the plugin no
longer loads in KeePass on Mono, because step 33 added a `user32` call it makes at startup. "Again,
at step 35" below has it.

What does work is most of what does not touch a certificate or a window: the plugin loads, listens,
watches KeePass's dialogs, writes and saves through KeePass, reads KeePass's internals the way it
does on Windows, follows a lock, and the TLS itself, Bouncy Castle's, completes a mutual TLS 1.3
handshake on Mono exactly as it does on .NET Framework.

## What was measured, and on what

| | |
| --- | --- |
| Windows | .NET Framework 4.8 on Windows 11, the baseline |
| Linux | Ubuntu 24.04.5 LTS in WSL 2, kernel 6.18.54.1 |
| Mono | 6.8.0.105, Ubuntu's own `mono-complete` 6.8.0.105+dfsg-3.6ubuntu2, libgdiplus 6.1 |
| Display | Xvfb 21.1.12 with no window manager; WSLg off |
| KeePass | 2.61.1, the pinned release |
| The plugin | `master` at `8fb11871`, built with `build.ps1`, Release |

`results/setup.txt` is the whole installation and the versions it ended with. The Linux user is
`probe`, made by `wsl/setup.sh`, and its culture is the invariant one: `LANG` is `C.UTF-8`.

## The probes

`results/compare.md` is every measurement side by side, written by `windows/Compare-Runs.ps1` from
`results/net48.txt` and `results/mono.txt`.

**Issuance is impossible.** `CertificateRequest` is in `System.Core` on .NET Framework 4.8 and in
`System` on Mono, with no forwarder between them, so a method that names it fails to compile on Mono
before it runs a line: `issuance` ends in a `TypeLoadException` and reports nothing else. `ecdsa`
asks the same things without naming the type, and finds it in `System`, where a build against the
.NET Framework reference assemblies cannot reach it. It would not help if it could: `ECDsa.Create()`
and `ECDsa.Create(nistP256)` both throw `NotImplementedException`, so there is no key to give it.

**An EC certificate can be read and not used.** A P-256 certificate and its PKCS#12, made by Bouncy
Castle, load into `X509Certificate2` on Mono: the raw bytes, the subject and the key algorithm read
back, and the PKCS#12 reports a private key under both of the plugin's load flags. Everything past
that fails. `PublicKey.Key` throws `NotSupportedException` for the EC algorithm, as it does on .NET
Framework, but `GetECDsaPublicKey`, which works there, throws `NotImplementedException`, and so does
signing with the restored private key.

**`X509Chain` throws for every EC certificate**, the plugin's own as well as a foreign one:
`NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'`, out of
`Build`. None of the questions `AnchorPin` exists for get as far as an answer. The `caIssuers`
listener was contacted once on .NET Framework and never on Mono, which shows only that Mono threw
before it would have looked.

**Bouncy Castle's TLS is unaffected.** `tls` runs `GateTlsServer`'s shape, a mutual TLS 1.3
handshake over P-256 on a loopback socket, and both runtimes answer the same: TLS 1.3, client
certificate offered, the echo returned.

**The audit file opens the same way.** A stream asking for `FileSystemRights.AppendData` alone opens,
appends and appends again on Mono, and the roll by move works with a reader open. `GetAccessControl`
throws `PlatformNotSupportedException`, which costs the plugin nothing: it never calls it.

**None of the six `user32` calls exists.** Each throws `EntryPointNotFoundException`, with null
windows as with real ones.

**One culture comparison differs.** A soft hyphen, U+00AD, is ignored by .NET Framework's culture
comparison and counted by Mono's, in en-US, tr-TR and de-DE alike. Everything else `culture` asks
agrees: the Turkish lowering that keeps two of KeePass's paths apart, the dotless i and the long s
under `OrdinalIgnoreCase`, and the ordering of the `KPG` keys.

**WinForms substitutes and scales differently.** Consolas and Microsoft Sans Serif become DejaVu Sans
Mono and DejaVu Sans, and the auto scale dimensions the KPG pages are laid out against are 7 by 14
rather than 6 by 13.

## The plugin's tests

`product/NUnitOnMono` ran both tiers in process, everything outside the `Live` and `Screenshots`
categories, with a test timeout of two minutes. `tools/classify-failures.py` puts every failure down
to one cause and wrote `results/causes.md`; nothing was left over.

| | ran | passed | failed | other |
| --- | ---: | ---: | ---: | --- |
| unit | 2381 | 1164 | 1216 | 1 inconclusive |
| host | 515 | 306 | 205 | 3 inconclusive, 1 explicit |

**The host tier needed help to load at all.** Its assembly is shared with the end to end tier, which
drives KeePass through Windows UI Automation, and NUnit reads every method signature while it builds
fixtures. `product/AutomationStubs` gives Mono an empty `UIAutomationClient` and `UIAutomationTypes`
carrying the real identities, and the runner leaves out the 39 types whose signatures still name
something those lack, every one of them in `Live`. Of the 610 cases that then load, 95 are in the
excluded categories.

### Why the failures failed

| cause | unit | host |
| --- | ---: | ---: |
| no `CertificateRequest` | 1005 | 181 |
| `DataGridView` column auto sizing | 69 | 16 |
| `user32` called by the plugin | 60 | |
| no `Control.LogicalToDeviceUnits` | 21 | |
| `ToolTip` ignores right to left | 5 | |
| a static field read across assemblies | 2 | |
| `Control.Scale` leaves `Padding` alone | 1 | |
| an open file can be moved | 1 | |
| libgdiplus GIF encoding | | 2 |
| a soft hyphen is not ignorable | | 1 |
| no EC public key in `X509Certificate2` | | 1 |
| *the tests' own:* `user32` | 21 | |
| *the tests' own:* `WindowsIdentity` | 20 | |
| *the tests' own:* Windows paths | 11 | |
| *the tests' own:* UI Automation | | 2 |
| *the tests' own:* run outside a checkout | | 2 |

**`CertificateRequest` is most of it.** Every test that makes a certificate makes it through
`AuthorityIssuer`, so pairing, renewal, admission, the locked gate, the responder and every handler
test behind an admitted client stop there, before anything Mono-specific about them is reached. One
host test got past it with certificates written down in advance: `SeededAuthorityTests` reached
`AnchorPin.Chains`, line 122, and met the `NotSupportedException` the `chain` probe met. That
exception is not among those `Expected.OfCryptography` names, so it would leave `AnchorPin` rather
than be answered as a chain that does not build.

**`DataGridView` throws while the grant table is built.** `GrantPanel.BuildGrid` adds its columns one
at a time, and the second, with `AutoSizeMode` set to `AllCells`, makes Mono's grid re-measure every
column, including a cell its rows do not hold yet: `ArgumentOutOfRangeException` out of
`DataGridViewCellCollection`. Every grant editor test fails there, and so does every attempt to put
the KPG tab on a group or an entry dialog.

**The `user32` calls are the plugin's own in 60 of them**: `NativeMethods.GetWindow` in 57, from
`ApprovalDialog.OnShown`, so every pairing and renewal approval dialog, and `IsWindowEnabled` in
`WindowEnabling`'s three. Read off the code rather than measured live: `WindowEnabling.Preserve`
wraps KeePass's save in every write the gate makes, so on Mono every write would end in
`EntryPointNotFoundException` before KeePass saved anything.

**`LogicalToDeviceUnits` is .NET Framework 4.7 and Mono 6.8 lacks it.** `GateStatusForm`, the window
under Tools, calls it in its constructor.

**Two failures are not explained.** `GateOptionsTests` and `EmbeddedDocumentTests` read
`DatabaseWriter.LockTimeout`, a `public static readonly TimeSpan`, and Mono answers
`MissingFieldException`. The writer itself passes all 32 of `DatabaseWriterTests` and all 9 of
`VaultWritesUnderDialogsTests` on Mono, so the field is there when the plugin uses it.

**What passed in full** is what does not reach a certificate or a window of the plugin's own:
the ACL and its resolver, the routing table and `BodyCheck`, the HTTP reader and writer, JSON bodies,
invitations, pairing tokens and proofs against the protocol vectors, SNI names, the lock watch, the
writer and its saves, `GroupDialogs` watching all six of KeePass's dialogs, the 37
`KeePassContractTests` that read KeePass's internals, the custom data merge rules, and the plugin's
lifecycle.

### KeePass's own compiler

`ReferenceVectorsTests` look for a checkout above their own directory, so `wsl/run-vectors.sh` ran
them again in one laid out for them: `results/vectors.tsv`. They hold KeePass's `SprEngine`, running
on Mono, to the 101 cases it compiled on .NET Framework. Three disagree:

- `{ENV_DIRSEP}` is `/`, which is the platform rather than a difference.
- A password stored under `Pass­word` is not `{PASSWORD}` on Mono, directly or through
  `{REF:P@I:…}`, which is the soft hyphen again, now inside KeePass's own dictionary.
- One entry of the 36 keeps its strings in another order: `ᾀ`, `}ü{S:`, `Title` where .NET Framework
  keeps `}ü{S:`, `Title`, `ᾀ`.

Both of the last two are about `pykeepassgate.refs`, which follows KeePass's order and its idea of
which names are one name, and does so as .NET Framework does. The host tier's
`KdbxVaultTests.An_entry_route_refuses_a_whole_body_that_carries_such_a_name` is the plugin's side
of the same fact: on Mono `Pass­word` is another field, so there is no aliasing for the guard to
refuse, and the three tests that assume there is were inconclusive rather than failing.

## KeePass on Mono, with the plugin installed

`wsl/keepass.sh` started the pinned KeePass on the throwaway database, with the plugin under
`plugins/KeePassGate`, and drove it a step at a time. The pictures are in `results/`, and each run's
console and plugin log in `results/keepass-run-1` and `results/keepass-run-2`.

**It loads and listens.** KeePass found the plugin in the lowercase `plugins` directory, the log
reads `initializing version 0.1.0`, then `listening on 127.0.0.1:51776` and both dialog watches
started, and the Tools menu carries `KeePassGate (KPG)` with its icon and its `K`
(`step-tools-menu.png`). A TLS client offering an SNI nobody issued is dropped, `nomatch`, as on
Windows.

**Opening the database settings took KeePass's interface down.** The KPG tab attached, and the first
paint of the dialog's tab strip threw out of Mono's `ImageList`: `ArgumentException` from
`Image.get_Width`, in the message loop. `TabPageInjector.Decorate` disposes the icon straight after
`ImageList.Images.Add`, because on .NET Framework that call copies the picture into the native list;
Mono's `ImageList` keeps the `Image` and reads it the first time it draws, by which time it is gone.
Afterwards no window of KeePass's existed, the main window included, and the process sat in its
message loop with nothing to show, which a thread dump confirmed (`keepass-run-1/keepass.console`).
What a desktop with a window manager would have shown in between was not observed.

**The window under Tools did not open**, and was handled as the design intends: the menu item's
catch-all logged `UNEXPECTED the status window could not be shown`, with the
`MissingMethodException` for `LogicalToDeviceUnits`, and KeePass carried on.

**A group dialog opened without the KPG tab**: the attach failed in `DataGridView` as the tests did,
was logged as an error, and the dialog itself worked (`step-group-dialog.png`).

**A lock and an unlock went through.** The plugin logged
`locked, but nothing was being served for it: probe.kdbx`, so it found the locked database's name
through KeePass's document manager on Mono as it does on Windows.

**Not reached:** creating an authority, pairing, serving a client, the responder at a lock. Each
needs `AuthorityIssuer`, and the tests above show where each one stops.

**KeePass persisted an RSA key pair** under `~/.config/.mono/keypairs` about a second before the
plugin began initializing, so it is KeePass's or Mono's own and not the plugin's. Nothing more was
asked about it.

## What running on Mono would take

In the order the failures fall, and none of it is planned:

1. **Every certificate operation through Bouncy Castle**, which is already here for the TLS and the
   PKCS#12 the server reads: issuing the authority, its host and the clients, signing with a stored
   key, and the check `AnchorPin` makes. With no intermediates that check is a signature verified
   with the anchor's public key and the anchor's bytes compared, and doing it there would take
   `X509Chain` out of the plugin on Windows as well, and with it both of the hazards `CLAUDE.md`
   records about it: the anchor it does not pin and the download a peer can make it start.
2. **The six `user32` calls kept to Windows**, with what each one stands for on the others: the
   flash, the capture guard and, the one that matters, the re-disabling around a save.
3. **Four WinForms differences worked around**: the icon kept alive as long as the list that holds
   it, the grant table's columns added without measuring cells that are not there yet,
   `LogicalToDeviceUnits` computed rather than called, and a page's padding scaled by hand.
4. **A Mono tier to keep it so**, since every fact the plugin rests on would then have a second
   runtime to be measured on, and the host tier's assembly would have to stop sharing its signatures
   with the end to end tier.

The first is the large one. The client is unaffected throughout: it runs on CPython, not on Mono.

## Again, at step 35

Build-order step 35 took the first item of the list above: every certificate the plugin makes, every
signature it makes with a stored key and the anchor pin go through Bouncy Castle, `X509Chain` and
`CertificateRequest` left the plugin, and the tab icon lives as long as the image list holding it.
The extended key usage and the dates are still read through `X509Certificate2`. The step ends with
this measurement taken again against it.

| | |
| --- | --- |
| The plugin | step 35 as reviewed: its units 1 to 4 on top of `master` at `f2a36af2`, step 33, the tree of `4cd39164` on its branch, built with `build.ps1 -Task Build -Configuration Release` in a checkout of its own |
| Everything else | as the first run: the same WSL distribution, Mono, Xvfb, KeePass and `NUnitOnMono` |

Everything is in `results/step35/`. The first attempt at each tier is kept beside the one counted,
because how each ended is part of the result.

### The plugin's tests

| | ran | passed | failed | other |
| --- | ---: | ---: | ---: | --- |
| unit, first run | 2381 | 1164 | 1216 | 1 inconclusive |
| unit, step 35 | 2401 | 2089 | 306 | 6 inconclusive |
| host, first run | 515 | 306 | 205 | 3 inconclusive, 1 explicit |
| host, step 35 | 517 | 467 | 43 | 6 inconclusive, 1 explicit |

**No failure is the plugin's certificate code any more.** The two causes that were 1187 of the first
run's failures, no `CertificateRequest` and no EC public key in `X509Certificate2`, are gone: the
issuer, the stored form, the anchor pin, pairing, renewal, the responder and the locked gate all run
under Mono now, as far as a test reaches them without a window of the plugin's own.
`results/step35/causes.md` puts every failure down to one cause and leaves nothing over:

| cause | whose | unit | host |
| --- | --- | ---: | ---: |
| `user32` called by the plugin | runtime | 69 | 2 |
| `DataGridView` column auto sizing | runtime | 69 | 16 |
| `user32` in the queue of turns (step 33) | runtime | 10 | |
| no `Control.LogicalToDeviceUnits` | runtime | 21 | |
| libgdiplus refuses a drawing parameter | runtime | | 9 |
| `ToolTip` ignores right to left | runtime | 5 | |
| WinForms accessibility and list layout | runtime | 4 | |
| `Control.Scale` leaves `Padding` alone | runtime | 1 | 3 |
| libgdiplus GIF encoding | runtime | | 2 |
| a static field read across assemblies | runtime | 2 | |
| a soft hyphen is not ignorable | runtime | 1 | 1 |
| an open file can be moved | platform | 1 | |
| *the tests' own:* `SslStream` without TLS 1.3 | tests | 54 | 2 |
| *the tests' own:* `user32` | tests | 31 | 4 |
| *the tests' own:* `WindowsIdentity` | tests | 20 | |
| *the tests' own:* Windows paths | tests | 11 | |
| *the tests' own:* Windows loading a stored key | tests | 4 | |
| *the tests' own:* the local time zone moved | tests | 3 | |
| *the tests' own:* UI Automation | tests | | 2 |
| *the tests' own:* run outside a checkout | tests | | 2 |

**What got further met what was behind it.** Tests that used to stop at issuance now reach the
grant table, the plugin's windows and the plugin's own `user32` calls, which is why those causes
grew, and reach two that were never met before:

- **The tests' own `SslStream` offers no TLS 1.3 on Mono**, so every test whose client is
  `GateClient` or `PairingClient` is refused in the handshake by a gate that speaks nothing older,
  and sees no answer at all. The gate's side is Bouncy Castle and is unaffected: the `tls` probe of
  the first run is the same handshake over Bouncy Castle on both sides, and it worked.
- **Step 33's queue of turns asks `user32`.** It reads KeePass's thread with
  `GetWindowThreadProcessId` on its first turn and walks that thread's windows with
  `EnumThreadWindows`, `IsWindowVisible` and `GetClassName` before a write is queued, so on Mono no
  turn starts and its caller hears nothing: ten tests, read off the code.

**Mono crashed twice, in WinForms.** The first attempt at the unit tier ended in a segmentation fault
inside `RenewalApprovalFormTests`, and so did a second at another of its tests; the third ran the
whole tier through, that fixture included, and is the one counted (`unit-3.tsv`; the attempts are
`unit.tsv` and `unit-2.tsv`). The first attempt at the host tier ended when Mono's X11 driver threw
`NullReferenceException` out of its `FrameExtents` on the thread `UiLoop` runs a second
message loop on, in `PluginLifecycleTests`; the second left that one test out and is the one counted
(`host-2.tsv`; the attempt is `host.tsv`).

### KeePass on Mono, with the plugin installed

**The plugin does not load any more.** KeePass shows "The plugin cannot be loaded" with
`GetWindowThreadProcessId` (`step35/keepass/1-start.png`), and the plugin's log has
`UNEXPECTED initialization failed` from `ControlUiThread`'s constructor, which asks that function
for KeePass's thread when the plugin starts. Step 33 added it, after the first run, where the plugin
loaded, listened and put its tab on the dialogs. KeePass itself carries on without the plugin
(`2-after-ok.png`). So nothing after loading could be driven here, the authority this step made
possible on Mono included: in KeePass on Mono, the first wall is now step 33's.

### What running on Mono would take now

1. **Done by step 35**: every certificate operation through Bouncy Castle, and the icon kept as long
   as its list.
2. **The `user32` calls kept to Windows**, which is ten now: the six the first run named, and step
   33's four, the first of which stops the plugin loading.
3. **The other WinForms differences worked around**: the grant table's columns,
   `LogicalToDeviceUnits`, a page's padding scaled by hand, and the three met for the first time
   here, a drawing parameter libgdiplus refuses, a row's accessible description and a list that
   grows where it should scroll.
4. **The tests' own clients moved off `SslStream`**, onto the Bouncy Castle client the tests already
   have, for a test tier to mean anything on Mono.
5. **A Mono tier to keep it so**, as before.

## What is not measured

Mono releases other than Ubuntu's 6.8: a later one may forward `CertificateRequest` or implement
ECDSA, and nothing here measured whether it does. macOS. A desktop with a window manager, where the
settings dialog may fail differently. A `.plgx`. And everything after an authority exists, which on
this Mono nothing can make.
