# Findings

What was established by building things, kept after the proofs themselves were deleted. Everything
here was measured or executed rather than reasoned about, and each entry says how, so that a doubtful
reader can redo it rather than trust it.

[`ANALYSIS.md`](ANALYSIS.md) is the argument. This is the evidence. [`DESIGN.md`](DESIGN.md) is what
was built from it.

---

## 1. The platform

| Fact | How it was established |
| --- | --- |
| `.plgx` packaging forces **C# 5**. KeePass compiles it with the in-box CodeDOM provider, which rejects C# 6 string interpolation with "unexpected character '$'" | compiled both through `CSharpCodeProvider` |
| **DLL distribution lifts that**, allowing modern C# | the packaging format is the only thing imposing it |
| KeePass 2.61.1 is the newest release, and there is **no separate SDK**: `KeePassLib` lives inside `KeePass.exe` | keepass.info, and the portable archive's contents |
| KeePass is a **.NET Framework** application: `supportedRuntime v4.0` and `v2.0.50727`, CLR 4.0.30319 | read `KeePass.exe.config`, queried the runtime |
| Therefore **modern .NET is unreachable in process**. A process has one runtime; `net8.0` needs CoreCLR and `netstandard2.1` excludes .NET Framework | NetCoreServer has never shipped a Framework-consumable target: 5.0.0 is `net5.0` through 8.0.7 at `net8.0` |
| **`AesGcm` is absent from .NET Framework 4.8**, so no version upgrade unlocks an AEAD | tested on the 4.8.1 runtime |
| `CertificateRequest.Create(issuer, …)` and `SubjectAlternativeNameBuilder.AddUri` exist, so **the certificate authority needs no third-party library** | enumerated the API, then issued a real chain with it |

## 2. Transport

| Fact | How it was established |
| --- | --- |
| **mTLS works in process, and TLS 1.3 negotiates** on .NET Framework 4.8 | `SslStream` over `TcpListener`, server saw `CN=agent-fictitious` |
| **TLS 1.3 is Windows 11 and Server 2022 only.** Every Windows 10 build is unsupported | Microsoft's Schannel protocol table |
| Requiring it therefore excludes only Windows versions that are themselves out of support | Windows 10 ended support in October 2025 |
| **`HttpListener` cannot serve HTTPS** without an administrator certificate binding: `Start()` succeeds and the first request dies | ran it unelevated |
| Plain HTTP on loopback **does** work unelevated; only the wildcard prefix is refused | `http://localhost:port/` and `http://127.0.0.1:port/` both round-tripped |
| **`HttpListener` exposes no TLS control at all**, no `SslProtocols`, no certificate. Version selection is machine-wide Schannel policy | enumerated its members |

That last one is why http.sys was rejected: a design that promises to offer only TLS 1.3 cannot
delegate that promise to the user's registry.

## 3. Vendoring, measured

Three HTTP implementations, each vendored, built on net48, driven with TLS 1.3 and mutual TLS, and
asked to parse the same request.

| | files | bytes | **changed lines** | stub files | glue we write | verdict |
| --- | --- | --- | --- | --- | --- | --- |
| `dotnet/runtime` managed `HttpListener` | 27 | 152 KB | **116** | 7 | pipeline written | most expensive |
| uHttpSharp | 62 | 128 KB | **0** | 0 | **0** | smallest to own |
| **websocket-sharp** | 76 | 922 KB | **10** | 0 | 40 | measured, **not taken** |

Notes that cost time to learn:

- The managed `HttpListener` is **runtime-internal code**. It wants `SR`, `NetEventSource`,
  `LazyAsyncResult` and four shared helpers, and its cookie handling cannot be ported at all because
  the BCL keeps `CookieParser` and three `Cookie` members internal.
- Its **TLS path is dead code** by upstream's own comment, so injecting our stream is the only
  option there rather than a preference.
- **uHttpSharp already targets `v4.5`**, needs no port, and its `IHttpListener` returns an `IClient`
  whose first member is a `Stream`, which is the seam this design wants.
- **websocket-sharp's `GetClientCertificate()` throws `NotSupportedException`**, which looked fatal
  and is not: `HttpConnection` publishes the authenticated stream, so the peer certificate is one
  cast away. Five lines of code fix it.
- **Size predicted nothing.** The largest candidate needed ten lines and the smallest needed 116,
  because what mattered was whether the code was written to stand alone.

## 4. Spec-first binding

**Both candidates support it**, and the difference is 40 lines.

A Connexion-style router was built twice, about 200 lines each time: the OpenAPI document is the
routing table, handlers bind to `operationId` rather than to paths, a declared operation with no
handler is a startup error, path templates capture parameters, and the body is validated against the
declared schema before a handler runs. Both passed the same five checks over TLS 1.3 with a client
certificate.

- uHttpSharp supplies the pipeline and per-request state, so the glue is nothing.
- websocket-sharp's `HttpServer` is a facade with `OnGet` and `OnPost` events, but
  `WebSocketSharp.Net.HttpListener` exposes `Prefixes`, `SslConfiguration` and `GetContext`, so the
  accept loop and pipeline are ours: 40 lines. **Superseded**: the accept loop, the TLS and the
  request parser are all ours now, and no HTTP dependency is taken.

The tooling exists on .NET Framework, which was not obvious: **`Microsoft.OpenApi` 1.6.22** and its
readers target netstandard2.0.

**Nothing in .NET does this off the shelf.** Swashbuckle and NSwag are code-first or build-time
generators; runtime spec-driven routing has no mainstream .NET equivalent, so it is custom work on
any foundation.

## 5. Things that turned out to be wrong

Recorded because a reader who only sees conclusions will re-make the same mistakes.

| Claimed | Actually |
| --- | --- |
| TLS 1.3 was a caveat to design around | It is a choice. Requiring it excludes only unsupported Windows, and it is what makes certificate contents safe |
| Certificates should not name databases | With TLS 1.3 required the confidentiality objection falls. What remains is that **KeePass has no database UUID**: `PwDatabase` has only `DataCipherUuid` and `RecycleBinUuid`, so the identifier would have to be the root group's |
| ...and then they should not, after all | The question dissolved rather than being answered. Once each database carries **its own authority**, a certificate is bound to one database by whoever signed it, so naming it again in the claim is a second place to get it wrong. Claims became `kpg://profile/<name>` and the `kpg://any` wildcard went with them, which is what closed the escalation it had introduced |
| websocket-sharp was the right dependency | It was, while it terminated TLS for us. Selecting a host certificate by SNI made that impossible, and a library parsing only a header block does not earn a submodule. `uHttpSharp` fits the new seam natively with zero changes and is ruled out on **license**, LGPL-2.1, not on fit. See `DESIGN.md` §107 |
| Vendored code should be subset hard, deleting what is unwanted | Deleting is a dangerous edit in the most dangerous place, and it makes upstream fixes hard to port. Minimum divergence, refuse from outside |
| The HTTP server would have to be hand-written | Every objection was about packaging, none applies to source |
| websocket-sharp was disqualified | Ten lines |
| **An `EphemeralKeySet` certificate can serve TLS** | It cannot, in either direction. The probe that said so imported the same PKCS#12 with a persisted container first and an ephemeral one second, and the second borrowed the key the first had left. Measuring the ephemeral case on its own gives `SEC_E_NO_CREDENTIALS` for a server and a dropped handshake for a client. The probe now runs that case first, alone |
| uHttpSharp supported spec-first binding "decisively" | Both do. It judged websocket-sharp by one facade |

## 6. Providing the KeePass everything is built against

Established while replacing a KeePass copied there by hand with a pinned download.

| Fact | How it was established |
| --- | --- |
| **The obvious SourceForge URL returns a web page, not the file.** `downloads.sourceforge.net/project/keepass/...` answers 200 with 137 KB of HTML to a browser-shaped request, and the plausible size is what makes it dangerous | downloaded it and looked at the first four bytes |
| The canonical `sourceforge.net/projects/keepass/files/.../download` form answers with the archive when the user agent is not a browser: 2,898,806 bytes for 2.61.1, starting `PK` | downloaded it and hashed it |
| **`KeePass.exe` is Authenticode signed**, valid, by `CN="Open Source Developer, Dominik Reichl"`. So an archive can be checked for being KeePass rather than only for being the bytes somebody pinned | `Get-AuthenticodeSignature` on the extracted file and on an independently installed copy |
| The portable archive extracts flat, with `KeePass.exe` at the top, which is the shape `KeePassDevDir` already wanted | extracted it |
| **Scoop's manifest would not have helped.** `extras/keepass` fetches the **installer** from the same SourceForge host, verifies a **SHA-1**, and carries only the current version, so pinning an old one means the versions bucket or manifest history | read the manifest |
| KeePass publishes a version feed at `keepass.info/update/version2x.txt`, on its own domain, signed, and it names the current release as `KeePass:<version>` | fetched it |

## 7. Loading a plugin, and testing one

Established while building step 4 of the build order. The first entry is the one that costs an
evening, because it fails without a message.

| Fact | How it was established |
| --- | --- |
| **KeePass screens plugin files on their `ProductName` and it must be exactly `KeePass Plugin`.** A DLL that says anything else is skipped in silence: no error, no message box, no entry in the plugin list, and the assembly is never even opened | disassembled `PluginManager.LoadPlugins`, which calls `FileVersionInfo.GetVersionInfo` and compares against `AppDefs.PluginProductName` before anything else. Confirmed by watching the loaded modules of a running KeePass before and after setting `<Product>` |
| **The entry point is found by name as `<file>.<file>Ext`**, so the namespace has to match the file as well as the class does. `KeePassGate.dll` must expose `KeePassGate.KeePassGateExt` | called `PluginManager.CreatePluginInstance` with several spellings and read the `TypeLoadException`, which names the type it looked for |
| KeePass scans the application directory, then its `Plugins` subdirectory recursively | the same disassembly |
| **`DatabaseSettingsForm` can be constructed outside a running KeePass**, and it builds its five tabs, so a test can drive the real dialog without starting the program | `Activator.CreateInstance` on the type, then read `m_tabMain` |
| `GlobalWindowManager.AddWindow` and `RemoveWindow` are public and static, so a test can raise the real window notifications a plugin subscribes to | enumerated the type, then drove the plugin end to end with them |
| **KeePass hands its command line to an already running instance and exits**, because `Integration.LimitToSingleInstance` defaults to true. A test that starts KeePass while any other copy runs measures nothing | the started process exited at once and owned no window; disabling the setting in a local configuration fixed it |
| A first start asks **"Enable automatic update check?"** in a modal dialog, before any plugin is loaded | the automated run stalled on it; answering it in the configuration with `CheckForUpdateConfigured` removes it |

### Driving KeePass through the desktop

Six traps, all of which look like the plugin failing rather than the harness.

| Fact | How it was established |
| --- | --- |
| **Choosing a menu item through UI automation does not return until the dialog it opens is closed**, and while that call is pending every other automation call against the process times out, including the one waiting for the dialog | `InvokePattern.Invoke` on "Database Settings...", then a `FindAll` from another thread, which failed with a COM timeout |
| **Reading the accessibility tree while a menu is open dismisses the menu**, so a keystroke sent afterwards goes to the main window and does nothing | the menu item was found, the keystroke was delivered, and no dialog appeared |
| **`SendKeys` is inert from a test runner.** On .NET Framework it delivers through a journal hook, which needs a message loop the runner does not have | the same keystrokes worked from PowerShell and not from the test host; synthesizing input with `keybd_event` works in both |
| `SetForegroundWindow` is refused to a process that does not already hold the foreground, which a test runner does not. Attaching to the foreground thread's input queue for the duration lifts the refusal | measured by comparing `GetForegroundWindow` before and after |
| Access keys are readable from the accessibility tree, `Alt+f` for the File menu and `d` for Database Settings, so the keyboard route needs no hard-coded English | read `AutomationElement.Current.AccessKey` |
| An accessible name set on a label **replaces** the text a reader gets back, so identifying controls that way hides the very text a test wants to assert | the end to end test read `KpgVersionLabel` where `Version 0.1.0` was expected |

So the shape that works: discover the keys through the accessibility tree with the menu opened and
closed through it, then drive the menu purely from synthesized keystrokes, then read the result back
through the tree once nothing is modal.

## 8. Storing an authority inside a database

Established while building step 5. The first two decide where the private key lives; the rest cost
time to find.

| Fact | How it was established |
| --- | --- |
| **A PKCS#12 imported on .NET Framework writes a key file into `%APPDATA%\Microsoft\Crypto\Keys`** for as long as the certificate is loaded, and removes it on dispose. `EphemeralKeySet` keeps it off the disk entirely | counted the files either side of the import, `EphemeralKeyProbe` |
| **An `EphemeralKeySet` certificate signs happily and SChannel will not do TLS with it in either direction.** A server gets `SEC_E_NO_CREDENTIALS`, a client never gets its certificate onto the wire. That is what put `Org.BouncyCastle.Tls` under the transport, which does serve with them, so no key this plugin loads reaches the disk | `EphemeralKeyProbe` case A, which is first and alone for the reason below |
| **That is a correction.** An earlier reading of the same probe said ephemeral keys serve TLS. It had imported the same PKCS#12 with a persisted container moments before, and Windows found that key | reordering the cases changed the answer |
| A `CertificateRequest.Create` under an issuer returns a certificate **with no private key**: it has to be married to the generated key with `CopyWithPrivateKey` | `HasPrivateKey` was false until it was |
| An ECDSA P-256 authority is about 384 bytes of DER and 1.3 KB of base64 PKCS#12, so custom data holds it comfortably | measured on the issued certificates |
| **`PwGroup.CustomData` has a setter, and it is not public.** KeePass can replace a group's custom data wholesale from inside its own assembly; a plugin can only set and remove keys one at a time | `PropertyInfo.CanWrite` says true, the compiler says no, and `GetSetMethod(true).IsPublic` says false |
| **Every authority this plugin issues carries the same subject**, so telling a stored pair apart by issuer name accepts a host certificate from a foreign authority. The anchor pin is the only check that works | a test written to prove the name comparison correct failed instead |
| **KeePass's own dialogs cannot be shown outside a running KeePass.** `GroupForm.Show` throws inside the message pump, and WinForms answers with its unhandled-exception dialog, which blocks a test run until something kills it | an automated run hung for five minutes on a window titled "Microsoft .NET Framework" |

That last one bounds what the middle test tier can do. Constructing KeePass's dialogs works and is
worth doing; showing them does not. So the snapshot hazard is measured in two halves: the mechanism
by reflection over the real types, and the behavior by reproducing it with KeePass's own
`CloneDeep` and a real `PwGroup`.

## 9. The gate and the transport

Established while building step 6.

| Fact | How it was established |
| --- | --- |
| A ClientHello arrives in one TLS record from every client tried, and the record header is enough to know how much to read | the transport reads exactly one record and has never needed a second |
| **A connection that is not TLS costs five bytes.** The record header is enough to refuse it, so a stray browser or a port scanner never reaches the parser | fed the listener a plain HTTP request and watched it drop with no response |
| A TLS server accepts a stream that replays already-read bytes, so peeking at the hello costs nothing at the handshake | the whole transport suite runs through `PrependingStream` |
| **`SslPolicyErrors` is unusable as the verdict**, exactly as `CA.md` §214 says: a correct client sees `RemoteCertificateChainErrors` and `RemoteCertificateNameMismatch` on every connection, because the authority is in no store and the name rotates | every test client accepts and records what it was shown |
| A client certificate loaded from a PKCS#12 needs a persisted key too, or `AuthenticateAsClient` never presents it and the server sees nobody | `EphemeralKeyProbe` case A |
| **A managed TLS stack removes the whole problem.** `Org.BouncyCastle.Tls` serves TLS 1.3 with a key parsed out of the PKCS#12 by itself, sees a client certificate, and writes nothing anywhere | `ManagedTlsProbe`, counting key files either side of a handshake |
| **An OpenSSL client reaches it**, which matters more than the .NET one, since that is what agents will use | Python's `ssl` on OpenSSL 3.0.11 negotiated TLS 1.3 with `TLS_AES_256_GCM_SHA384` and was answered |
| A plugin's dependency belongs **beside the plugin**, not beside KeePass: `Assembly.LoadFrom` probes the loaded assembly's own directory | the live gate test runs a real KeePass whose plugins directory holds the plugin and Bouncy Castle, and it serves |
| **And in a directory of its own under `plugins`**, not in `plugins` itself: KeePass finds a plugin in any directory under it, while a flat install shares each dependency by file name with every other plugin installed flat, and with no binding redirect but KeePass's own, one `Newtonsoft.Json.dll` of another major version breaks whichever plugin was there first | a scratch probe replaying KeePass 2.61.1's plugin loading in a fresh process per layout and preparing all 1613 methods: flat beside a foreign Newtonsoft.Json 10.0.2, 21 failed for want of 13.0.0.0; in `plugins\KeePassGate` with the foreign copy in `plugins` or in a sibling plugin's directory, none |
| KeePass's `MainForm.DocumentManager.GetOpenDatabases()` is reachable from a background thread and is what the gate resolves names against | the live gate test answers a request from a separate process |

## 10. Pairing

Established while building step 8. The first two cost an hour each, and both fail by producing
something plausible rather than by failing.

| Fact | How it was established |
| --- | --- |
| **A control with an `AccessibleName` reports that name to UI automation instead of its text.** A label set up for one kind of test then reads as `KpgApprovalDetailLabel` to the other | the end to end test found the approval dialog, read every text on it, and got the control names back. Set `Name` for the in-process tests, and leave `AccessibleName` off anything whose text is read from the desktop |
| **`Button.PerformClick()` does nothing on a form that was never shown.** `CanSelect` walks the parents, and an unshown form is not visible, so the click is dropped in silence and the test asserts against a dialog nobody pressed | the dialog answered `TimedOut` after being "clicked". What the button does is public on the form now, exactly as the tab's buttons are, so a test can reach the decision without a window |
| **`MainForm.SaveDatabase(PwDatabase, object)` is public and saves the database it is given.** `UIFileSave` saves whichever document is active, which is not necessarily the one a connection resolved to | enumerated `MainForm`, then disassembled it: it takes the active database only when handed null, and falls through to Save As when the database has no path |
| **KeePass's save reports nothing, and `PwDatabase.Modified` going false is the signal.** It is what the pairing write reads back | measured against a real KDBX4 in the host tier, and asserted there, so a KeePass that stops clearing it fails a test rather than a pairing |
| **A `GroupForm` can be given its group without being shown**, with `InitEx(group, false, null, database)`, which is what sets the field the watcher reads | the group dialog watcher is tested against the real dialog and the real window notifications |
| **A modal dialog shown from inside a marshaled call still pumps messages**, so UI automation can press its buttons while the connection thread that raised it waits on the answer | the end to end test approves and refuses real pairing requests this way |
| `Control.BeginInvoke` plus a wait on the returned handle is a synchronous marshal with a bound on it, and `EndInvoke` brings anything the work threw back to the waiting thread | the pairing write and the approval dialog both use it; the case that cannot be arranged with a real user interface is covered by a stand-in that never runs the work |
| **`BeginInvoke` cannot be taken back, so a timeout on it means "not yet" unless something else is added.** The work stays in the queue and runs whenever the user interface next drains it, which for pairing is a record saved after the client was refused, or an approval dialog for a client that was turned away minutes ago | `UiThreadTests` posts work onto a real form that is not pumping, lets the caller give up, and only then pumps. Without the latch that refuses to start work already called off, the count goes up after the caller was told it never ran |
| **In a custom format string a bare `-` and `:` are the current culture's date and time separators**, not literals, so `"yyyy-MM-dd HH:mm:ss"` writes `2026.08.26 11.04.07` on a machine set to a culture that uses dots. It is correct only for as long as every caller remembers `InvariantCulture` | one of four copies of two timestamp formats had them unquoted. `Moments` is now the only place either format is written down, and both quote their separators |
| **An exception thrown by work posted with `BeginInvoke` and left unobserved reaches `Application.ThreadException`**, whose default handler is a modal dialog. In a test host that pumps messages, that is a hang rather than a failure | a test that asserted `EndInvoke` rethrows never finished; it was deleted, because it was pinning behavior that predates the fix it was written beside |
| **The desktop tests find a box by its `AccessibleName`, so that string has two jobs and only one right value.** The rule above says to leave `AccessibleName` off anything the desktop reads, and a `TextBox` is the case it does not cover: it has no caption, so with nothing set there is no name to search by at all. What goes there has to be the phrase a screen reader should say, and the driver has to look the box up by that | setting it to the lookup name instead announced `KpgInvitationBox` to a screen reader; setting it to the words broke `LivePairingTests` until the driver was pointed at the same constant. `Name` stays the lookup key for the in-process tests, which match on it and not on the accessible name |

## 11. Reconstruction

The proofs were deleted once they had answered their questions. To rebuild any of them:

1. **Transport.** `TcpListener` on loopback, `SslStream`, `AuthenticateAsServer(cert, true,
   SslProtocols.Tls13, false)`. Issue a CA, a server certificate and a client certificate with
   `CertificateRequest`, round-tripping each through PKCS#12 so the private key has a usable handle,
   and give the CA a longer validity than what it signs.
2. **Vendoring.** Fetch at a pinned commit, rename the namespace only where it collides, and record
   every patch. Measure divergence by diffing against the pinned upstream and discounting the
   namespace lines.
3. **Spec-first.** `Microsoft.OpenApi` to read the document, a path-template matcher over the
   segments, a checker over the subset the wire carries for the body, and a dictionary from
   `operationId` to handler with a startup assertion that the two agree.
4. **Client.** Use a conforming HTTP client. A hand-written request string was accepted by two of the
   three parsers and rejected by the third, which cost an hour of misattribution.

`TlsProbe.cs` on `spikes/tls-and-certificates` is kept because it answers the TLS question in
isolation and is the cheapest way to re-check it on a new machine.

## 12. What KeePass does with a change, measured

**A `.kdbx` silently drops 2,079 of the 65,536 code points in the basic multilingual plane.** KeePass
writes every unprotected string through `StrUtil.SafeXmlString`, which removes what XML 1.0 cannot
represent and reports nothing: the 29 control characters other than tab, carriage return and line
feed, the 2,048 surrogates when they arrive without a pair, and `U+FFFE` and `U+FFFF`. A surrogate
pair survives, including the pairs whose halves look like those last two, so `U+1FFFE` is stored as
it was sent. Measured by calling that method on KeePass 2.61.1 once per code point and comparing
what came back, which is also what `JsonBody.IsUsable` is now written against: the two agree on all
65,536.

**A lone surrogate reaches a body reader only as `U+FFFD`.** Json.NET substitutes the replacement
character for an escaped half while it is reading, so `{"a":"one\uD800two"}` comes out of
`JObject.Load` carrying `U+FFFD` and nothing downstream can tell it from a caller that sent one. A
surrogate written raw in the text is passed through untouched, which is why the refusal is still
worth having: `JsonBody` also reads documents out of a `.kdbx` this plugin did not write. Measured
against the Json.NET this builds against, by printing the code points that came back.

## 13. The gate, and where it can run

**A warning is not a failure, and under `Stop` it becomes one.** A native command writing to stderr
raises a terminating error as soon as anything upstream merges the streams, which is what capturing
a build's output does. `npm ci` warns about any configuration key it does not recognize, and
installing the prose tools is the first thing a fresh clone does, so the gate ended on a warning and
named nothing while the command it ran had succeeded. Every step therefore runs under `Continue` and
is judged by its exit code alone. `BuildStepProbe.ps1` on `spikes/build-environment` reproduces it.

**A PowerShell 7 on the machine can leave this one unable to find its own cmdlets.** `pwsh` leads
`PSModulePath` with its own module directories, a Windows PowerShell started from it inherits them,
and `Microsoft.PowerShell.Utility` then resolves to a copy marked for a runtime this is not. It
refuses to load and takes `Get-FileHash` with it, so the name is not a command at all rather than a
command that errors, which is what makes it look like a broken script rather than a borrowed path.
Same probe.

**A hosted `windows-latest` runner has a real interactive desktop**, and this reverses what was
asserted before it was measured. The claim was that no hosted runner could drive a GUI and that the
end to end tier would need a self-hosted one. It is wrong: the runner reports `WinSta0` and
`Default`, a window from another process is visible, the foreground can be taken, synthesized
keystrokes and clicks arrive, and UI Automation describes another process. The whole end to end tier
passed there, **47 tests, none skipped**, the same count as locally on the day it was read, and the
pinned KeePass is fetched, hash checked and signature checked on the way. That count is a reading of
the runner rather than a standing claim about the tier: the suite grows, so what it does there at any
later commit is what that commit's run says and nothing here. The screen is 1024x768 with one
monitor, which is enough for the one place the tier moves a real pointer. `spikes/hosted-runner-desktop` holds
the probe and the numbers.

**Containers were never the alternative.** Linux is the wrong operating system for UI Automation,
`keybd_event` and WinForms on .NET Framework; Windows containers have no interactive desktop; and
GitHub does not support container jobs on Windows runners at all.

**Required status checks are available and not configured.** The workflow reports and does not
block, which is a property of the repository's settings rather than of the gate. Requiring the
gate before a merge is one setting and costs nothing.
