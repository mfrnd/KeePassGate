# Findings

What was established by building things, kept after the proofs themselves left the mainline for
branches of their own. Everything here was measured or executed rather than reasoned about, and each
entry states how, so that a doubtful reader can redo it rather than trust it.

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
| `CertificateRequest.Create(issuer, …)` and `SubjectAlternativeNameBuilder.AddUri` exist, so **the certificate authority needs no third-party library**. Still so on Windows, and the plugin issues through Bouncy Castle all the same, because Mono has no ECDSA and no `CertificateRequest` a .NET Framework build can reach, `spikes/mono-compatibility` | enumerated the API, then issued a real chain with it |
| **In KeePass on Mono the plugin does not load at all**, because `ControlUiThread`, the queue of turns on KeePass's thread, asks `user32` for that thread as the plugin starts. Once every certificate went through Bouncy Castle, the tests found no failure in the plugin's certificate code as far as a test reaches it: what fails is put down to Mono's runtime, its WinForms and the plugin's `user32` calls above all, and to the tests' own Windows dependencies, their `SslStream` clients first, which offer no TLS 1.3 on Mono. So the plugin stays Windows only | `spikes/mono-compatibility`, run under Mono 6.8 again after the issuer moved, and recorded beside the first run |

## 2. Transport

| Fact | How it was established |
| --- | --- |
| **mTLS works in process, and TLS 1.3 negotiates** on .NET Framework 4.8 | `SslStream` over `TcpListener`, server saw `CN=agent-fictitious` |
| **TLS 1.3 is Windows 11 and Server 2022 only**, in SChannel. Every Windows 10 build is unsupported | Microsoft's Schannel protocol table |
| Requiring it therefore excluded only Windows versions that are themselves out of support, while SChannel terminated the TLS. Bouncy Castle offers TLS 1.3 whatever the Windows build, so that limit now binds only a client built on SChannel | Windows 10 ended support in October 2025; `DESIGN.md` §133 has why the server's TLS is not the platform's |
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

The tooling exists on .NET Framework, which was not obvious: **`Microsoft.OpenApi` 1.6** and its
readers target netstandard2.0. That was measured on 1.6.22, which NuGet marks deprecated for a
denial of service in its YAML reading. The plugin takes 1.6.31, over `SharpYaml` 2.1.5, which
brings the same three assemblies, and it is that pair which bounds what an alias expands to.
Measured for review 4, with a nest of aliases under an `x-` extension: 1.6.30 over `SharpYaml`
2.1.1 read it with no error, at a cost growing ninefold with each level, and an alias naming
itself ended the process with a stack overflow, where 1.6.31 refuses the first past 5,000 nodes and
names the second a cycle. The plugin reads only the document it embeds, so neither was ever
within a caller's reach.

**Nothing in .NET does this off the shelf.** Swashbuckle and NSwag are code-first or build-time
generators; runtime spec-driven routing has no mainstream .NET equivalent, so it is custom work on
any foundation.

## 5. Things that turned out to be wrong

Recorded because a reader who only sees conclusions will re-make the same mistakes.

| Claimed | Actually |
| --- | --- |
| TLS 1.3 was a caveat to design around | It is a choice. Requiring it excluded only unsupported Windows while SChannel served it, and no longer turns on the Windows build now that Bouncy Castle does, and it is what makes certificate contents safe |
| Certificates should not name databases | With TLS 1.3 required the confidentiality objection falls. What remains is that **KeePass has no database UUID**: of `PwDatabase`'s `PwUuid` members, `DataCipherUuid` names a cipher and `RecycleBinUuid`, `EntryTemplatesGroup`, `LastSelectedGroup` and `LastTopVisibleGroup` name groups, so the identifier would have to be the root group's |
| ...and then they should not, after all | The question dissolved rather than being answered. Once each database carries **its own authority**, a certificate is bound to one database by whoever signed it, so naming it again in the claim is a second place to get it wrong. Claims became `kpg://profile/<name>` and the `kpg://any` wildcard went with them, which is what closed the escalation it had introduced |
| websocket-sharp was the right dependency | It was, while it terminated TLS for us. Selecting a host certificate by SNI made that impossible, and a library parsing only a header block does not earn a submodule. `uHttpSharp` fits the new seam natively with zero changes and is ruled out on **license**, LGPL-2.1, not on fit. See `DESIGN.md` §107 |
| Vendored code should be subset hard, deleting what is unwanted | Deleting is a dangerous edit in the most dangerous place, and it makes upstream fixes hard to port. Minimum divergence, refuse from outside |
| The HTTP server would have to be hand-written | Every objection was about packaging, none applies to source |
| websocket-sharp was disqualified | Ten lines |
| **An `EphemeralKeySet` certificate can serve TLS** | It cannot, in either direction. The probe that said so imported the same PKCS#12 with a persisted container first and an ephemeral one second, and the second borrowed the key the first had left. Measuring the ephemeral case on its own gives `SEC_E_NO_CREDENTIALS` for a server and a dropped handshake for a client. The probe now runs that case first, alone |
| uHttpSharp supported spec-first binding "decisively" | Both do. It judged websocket-sharp by one facade |
| **The connection ceiling keeps KeePass's thread pool out of a stranger's hands** | Only on a machine with more processors than the ceiling has slots. The pool hands out as many threads at once as there are processors and adds about a thread a second past that, and KeePass's managed Argon2 blocks a save on lanes it queues there, so thirty-one writes held on the pool froze the first save for seventeen to twenty-two seconds, and thirty-one silent sockets held KeePass's own pool work back for about ten. Each connection has a thread of the gate's own now. See `DESIGN.md` §104 |
| **The channel binding needs `TransportContext.GetChannelBinding`** | It needs no transport interface at all. `tls-server-end-point` is defined as a hash of the server's certificate, so each side computes it from bytes it already holds: the server from the certificate it chose, the client from the one it was just sent. `BindingProbe` answered a question that mattered while `SslStream` terminated the TLS, and the question went away with `SslStream`. The offset warning it left behind is now unreachable |

## 6. The document as the routing table

Established while building the spec router. The first two are the reason the design's named
library is not the library that shipped.

| Fact | How it was established |
| --- | --- |
| **`Microsoft.OpenApi` costs three assemblies** on net48: itself, its readers, and `SharpYaml`, which the readers depend on whether or not any YAML is read. The document is YAML because of that: JSON would have shipped a YAML reader unused | referenced it in an empty net48 project and listed the output |
| `Microsoft.OpenApi` reads a document on .NET Framework 4.8 and exposes operations, `operationId`, `x-` extensions, parameters and request body schemas with `Required`, `Properties`, `AdditionalPropertiesAllowed` and the length facets | parsed a document with all of them and printed what came back |
| **The reader reports some problems and throws others.** Malformed JSON and an empty document arrive as an entry in `diagnostic.Errors`, and there `Paths` can be null, or the document itself, as for `openapi: [3]`; a document with no recognizable version throws instead | fed it `not a document`, `{`, `{}`, `[1,2]`, three NUL bytes, an empty document and `openapi: [3]`, on 1.6.31 |
| **`OpenApiUnsupportedSpecVersionException` derives straight from `Exception`**, not from the library's own `OpenApiException`, so there is no one base to catch and both have to be named | walked the base types |
| **The transitive closure has to travel, not the direct references.** A plugin copied with only what it references loads until one of its dependencies has one of its own, and then fails with nothing in the log to say why | the end to end tier: 34 of 35 failed with "the plugin never reported a listening gate" until `SharpYaml` was copied too |

## 7. Providing the KeePass everything is built against

Established while replacing a KeePass copied there by hand with a pinned download.

| Fact | How it was established |
| --- | --- |
| **The obvious SourceForge URL returns a web page, not the file.** `downloads.sourceforge.net/project/keepass/...` answers 200 with 137 KB of HTML to a browser-shaped request, and the plausible size is what makes it dangerous | downloaded it and looked at the first four bytes |
| The canonical `sourceforge.net/projects/keepass/files/.../download` form answers with the archive when the user agent is not a browser: 2,898,806 bytes for 2.61.1, starting `PK` | downloaded it and hashed it |
| **`KeePass.exe` is Authenticode signed**, valid, by `CN="Open Source Developer, Dominik Reichl"`. So an archive can be checked for being KeePass rather than only for being the bytes somebody pinned | `Get-AuthenticodeSignature` on the extracted file and on an independently installed copy |
| The portable archive extracts flat, with `KeePass.exe` at the top, which is the shape `KeePassDevDir` already wanted | extracted it |
| **Scoop's manifest would not have helped.** `extras/keepass` fetches the **installer** from the same SourceForge host, verifies a **SHA-1**, and carries only the current version, so pinning an old one means the versions bucket or manifest history | read the manifest |
| KeePass publishes a version feed at `keepass.info/update/version2x.txt`, on its own domain, signed, and it names the current release as `KeePass:<version>` | fetched it |

## 8. Loading a plugin, and testing one

Established while making the plugin load into KeePass. The first entry costs an evening, because it
fails without a message.

| Fact | How it was established |
| --- | --- |
| **KeePass screens plugin files on their `ProductName` and it must be exactly `KeePass Plugin`.** A DLL that says anything else is skipped in silence: no error, no message box, no entry in the plugin list, and the assembly is never even opened | disassembled `PluginManager.LoadPlugins`, which calls `FileVersionInfo.GetVersionInfo` and compares against `AppDefs.PluginProductName` before anything else. Confirmed by watching the loaded modules of a running KeePass before and after setting `<Product>` |
| **The entry point is found by name as `<file>.<file>Ext`**, so the namespace has to match the file as well as the class does. `KeePassGate.dll` must expose `KeePassGate.KeePassGateExt` | called `PluginManager.CreatePluginInstance` with several spellings and read the `TypeLoadException`, which names the type it looked for |
| KeePass scans the application directory, then its `Plugins` subdirectory recursively | the same disassembly |
| **`DatabaseSettingsForm` can be constructed outside a running KeePass**, and it builds its five tabs, so a test can drive the real dialog without starting the program | `Activator.CreateInstance` on the type, then read `m_tabMain` |
| `GlobalWindowManager.AddWindow` and `RemoveWindow` are public and static, so a test can raise the real window notifications a plugin subscribes to | enumerated the type, then drove the plugin end to end with them |
| **KeePass hands its command line to an already running instance and exits**, because `Integration.LimitToSingleInstance` defaults to true. A test that starts KeePass while any other copy runs measures nothing | the started process exited at once and owned no window; disabling the setting in a local configuration fixed it |
| A first start asks **"Enable automatic update check?"** in a modal dialog, before any plugin is loaded | the automated run stalled on it; answering it in the configuration with `CheckForUpdateConfigured` removes it |
| **The gate is listening before KeePass has opened the database named on its command line**, because the listener starts inside the plugin's `Initialize` and the file is opened once the plugins are initialized. A connection in between resolves no name and is dropped with no TLS answer, which the client reports as the remote party closing the stream. So a live fixture is ready when the log says `serving a database with authority`, never when it says `listening on` | read off the plugin log of a started KeePass, where `listening on` follows `initializing` by 12 ms and the serving line comes later still; a fixture that sent on the first line had its first request dropped on a cold machine, and every assertion after it failed on the entry that was never created |

### Driving KeePass through the desktop

Eight traps, all of which look like the plugin failing rather than the harness.

| Fact | How it was established |
| --- | --- |
| **Choosing a menu item through UI automation does not return until the dialog it opens is closed**, and while that call is pending every other automation call against the process times out, including the one waiting for the dialog | `InvokePattern.Invoke` on "Database Settings...", then a `FindAll` from another thread, which failed with a COM timeout |
| **Reading the accessibility tree while a menu is open dismisses the menu**, so a keystroke sent afterwards goes to the main window and does nothing | the menu item was found, the keystroke was delivered, and no dialog appeared |
| **`SendKeys` is inert from a test runner.** On .NET Framework it delivers through a journal hook, which needs a message loop the runner does not have | the same keystrokes worked from PowerShell and not from the test host; synthesizing input with `keybd_event` works in both |
| `SetForegroundWindow` is refused to a process that does not already hold the foreground, which a test runner does not. Attaching to the foreground thread's input queue for the duration lifts the refusal | measured by comparing `GetForegroundWindow` before and after |
| Access keys are readable from the accessibility tree, `Alt+f` for the File menu and `d` for Database Settings, so the keyboard route needs no hard-coded English | read `AutomationElement.Current.AccessKey` |
| An accessible name set on a label **replaces** the text a reader gets back, so identifying controls that way hides the very text a test wants to assert | the end to end test read `KpgVersionLabel` where `Version 0.1.0` was expected |
| **A `ListView` in details view is a `DataGrid` to UI automation, not a `List`**, with `Selection`, `Grid` and `Table` patterns, and its rows are `DataItem`s named by their first column. Looked for as a `List` it is not there, and a description of the window's lists says it has none | the authority chooser's list was not found in a live run; the same dialog, shown off screen and read by a .NET Framework client as the test host reads it, reported a `DataGrid` |
| **The first walk into a window's controls leaves a thread no AppDomain can unload.** `System.Windows.Automation` loads its client-side providers into the domain that asks, and their `WinEventTracker` starts a queue on a background thread waiting in `MsgWaitForMultipleObjects` with no timeout and no way to stop, so `AppDomain.Unload` gives up on it after fifteen seconds with `CannotUnloadAppDomainException`. NUnit unloads the test assembly's domain at the end of a run, which is the "Exception encountered unloading application domain" every end to end run ended with. Registering an empty set of client-side providers does not stop it. Ending that thread at the end of the run, with one work item through the queue's own way in, does: `ClientSideProviderQueue` | a probe ran one automation call in a child domain against the pinned KeePass and unloaded it, three times each: finding the window by its handle and reading its name unloaded in milliseconds, one `FindAll` over its descendants failed after 15.3 seconds every time, and the same with the queue ended unloaded in 6 to 9 milliseconds, and in 4 to 8 on a second run. Run one at a time, the seven end to end fixtures that read a dialog each ended this way, and the five that only find the main window did not. `spikes/automation-unload` holds the probe, the per fixture runner and the output |

So the shape that works: discover the keys through the accessibility tree with the menu opened and
closed through it, then drive the menu purely from synthesized keystrokes, then read the result back
through the tree once nothing is modal.

## 9. Storing an authority inside a database

Established while storing the authority in the database. The first two decide where the private
key lives; the rest cost time to find.

| Fact | How it was established |
| --- | --- |
| **A PKCS#12 imported on .NET Framework writes a key file into `%APPDATA%\Microsoft\Crypto\Keys`** for as long as the certificate is loaded, and removes it on dispose. `EphemeralKeySet` keeps it off the disk entirely | counted the files either side of the import, `EphemeralKeyProbe` |
| **An `EphemeralKeySet` certificate signs happily and SChannel will not do TLS with it in either direction.** A server gets `SEC_E_NO_CREDENTIALS`, a client never gets its certificate onto the wire. That is what put `Org.BouncyCastle.Tls` under the transport, which does serve with them, so no key this plugin loads reaches the disk | `EphemeralKeyProbe` case A, which is first and alone for the reason below |
| **That is a correction.** An earlier reading of the same probe said ephemeral keys serve TLS. It had imported the same PKCS#12 with a persisted container moments before, and Windows found that key | reordering the cases changed the answer |
| A `CertificateRequest.Create` under an issuer returns a certificate **with no private key**: it has to be married to the generated key with `CopyWithPrivateKey`. That was the issuer until every certificate went through Bouncy Castle, which marries nothing | `HasPrivateKey` was false until it was |
| An ECDSA P-256 authority is about 384 bytes of DER and 1.3 KB of base64 PKCS#12 as Windows issued it, and about 393 bytes and 1.4 KB as Bouncy Castle issues it, with a serial twice as long, so custom data holds it comfortably | measured on the issued certificates, and again on Bouncy Castle's: 392 to 394 bytes over three hundred, and 1372 to 1384 characters of base64 |
| **`PwGroup.CustomData` has a setter, and it is not public.** KeePass can replace a group's custom data wholesale from inside its own assembly; a plugin can only set and remove keys one at a time | `PropertyInfo.CanWrite` says true, the compiler says no, and `GetSetMethod(true).IsPublic` says false |
| **Every authority this plugin issues carries the same subject**, so telling a stored pair apart by issuer name accepts a host certificate from a foreign authority. The anchor pin is the only check that works | a test written to prove the name comparison correct failed instead |
| **KeePass's own dialogs cannot be shown outside a running KeePass.** `GroupForm.Show` throws inside the message pump, and WinForms answers with its unhandled-exception dialog, which blocks a test run until something kills it | an automated run hung for five minutes on a window titled "Microsoft .NET Framework" |

That last one bounds what the middle test tier can do. Constructing KeePass's dialogs works and is
worth doing; showing them does not. So the snapshot hazard is measured in two halves: the mechanism
by reflection over the real types, and the behavior by reproducing it with KeePass's own
`CloneDeep` and a real `PwGroup`.

## 10. The gate and the transport

Established while building the gate and the transport.

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

## 11. Pairing

Established while building pairing. The first two cost an hour each, and both fail by producing
something plausible rather than by failing.

| Fact | How it was established |
| --- | --- |
| **A control with an `AccessibleName` reports that name to UI automation instead of its text.** A label set up for one kind of test then reads as `KpgApprovalDetailLabel` to the other | the end to end test found the approval dialog, read every text on it, and got the control names back. Set `Name` for the in-process tests, and leave `AccessibleName` off anything whose text is read from the desktop |
| **`Button.PerformClick()` does nothing on a form that was never shown.** `CanSelect` walks the parents, and an unshown form is not visible, so the click is dropped in silence and the test asserts against a dialog nobody pressed | the dialog answered `TimedOut` after being "clicked". What the button does is public on the form now, exactly as the tab's buttons are, so a test can reach the decision without a window |
| **`MainForm.SaveDatabase(PwDatabase, object)` is public and saves the database it is given.** `UIFileSave` saves whichever document is active, which is not necessarily the one a connection resolved to | enumerated `MainForm`, then disassembled it: it takes the active database only when handed null, and falls through to Save As when the database has no path |
| **KeePass's save reports nothing, and `PwDatabase.Modified` going false is half the signal.** Every write reads it back, and since KeePass clears it from what it did in memory, every write also hashes the file against `PwDatabase.HashOfLastIO` and answers `500 not verified` when the two disagree | measured against a real KDBX4 in the host tier, and asserted there, so a KeePass that stops clearing it fails a test rather than a pairing |
| **A `GroupForm` can be given its group without being shown**, with `InitEx(group, false, null, database)`, which is what sets the field the watcher reads | the group dialog watcher is tested against the real dialog and the real window notifications |
| **A modal dialog shown from inside a marshaled call still pumps messages**, so UI automation can press its buttons while the connection thread that raised it waits on the answer | the end to end test approves and refuses real pairing requests this way |
| `Control.BeginInvoke` plus a wait on the returned handle is a synchronous marshal, and its bound is on the wait for the work to start and nothing after it: work that has started is waited out, or a caller would let go of a lock while its own write was still running. What the work throws does not wait for `EndInvoke`, as the row below has it, so the work catches its own and the waiting thread throws it again | every write and the approval dialog are waited for with that bound, through a turn of their own on KeePass's thread since section 21; the case that cannot be arranged with a real user interface is covered by a stand-in that never runs the work |
| **`BeginInvoke` cannot be taken back, so a timeout on it means "not yet" unless something else is added.** The work stays in the queue and runs whenever the user interface next drains it, which for pairing is a record saved after the client was refused, or an approval dialog for a client that was turned away minutes ago | `UiThreadTests` posts work onto a real form that is not pumping, lets the caller give up, and only then pumps. Without the latch that refuses to start work already called off, the count goes up after the caller was told it never ran |
| **In a custom format string a bare `-` and `:` are the current culture's date and time separators**, not literals, so `"yyyy-MM-dd HH:mm:ss"` writes `2026.08.26 11.04.07` on a machine set to a culture that uses dots. It is correct only for as long as every caller remembers `InvariantCulture` | one of four copies of two timestamp formats had them unquoted. `Moments` is now the only place either format is written down, and both quote their separators |
| **An exception thrown by work posted with `BeginInvoke` reaches `Application.ThreadException` whether or not anything calls `EndInvoke`**, whose default handler is a modal dialog. In a test host that pumps messages, that is a hang rather than a failure | a test that asserted `EndInvoke` rethrows never finished; it was deleted, because it was pinning behavior that predates the fix it was written beside. A form on one thread and a throwing `BeginInvoke` from another then showed a registered handler raised on the form's thread before `EndInvoke` was ever called |
| **A `TextBox` has no caption, so its `AccessibleName` is all a screen reader has to say for it.** The rule above leaves `AccessibleName` off anything the desktop reads, and a box is the case it does not cover: what goes there has to be the phrase a screen reader should say. While the desktop tests found a box by that name, the string had two jobs and only one right value; they find it by its `AutomationId` now, which comes from `Name`, so the accessible name has the one job, `CONVENTIONS.md` | setting it to the lookup name instead announced `KpgInvitationBox` to a screen reader; setting it to the words broke `LivePairingTests` until the driver was pointed at the same constant. Keying the lookup on the name was then what made the two collide, and the driver asks for the identifier instead, as the in-process tests always matched on `Name` |

## 12. Grants in a real tree

Established while building the rules. The rules themselves need no password manager; these are the
three things about KeePass that the rules depend on and could not be reasoned out.

| Fact | How it was established |
| --- | --- |
| **A `PwEntry` carries custom data of its own**, so a grant can be written on a single entry rather than only on the group holding it. It is a KDBX4 feature and there would be nowhere to put one in a KDBX3 file | wrote a document on an entry in a real KDBX4, saved, closed, opened the file again and resolved against it |
| **An absent key reads back as null, not as an empty string**, on both a group and an entry, and both are the same `StringDictionaryEx` | asserted in the host tier, which is where a KeePass that changes it will say so. The resolver treats an empty string as absence anyway, because a key left blank rather than removed must not become a rule that will not read |
| **`ParentGroup` is set by `AddEntry` and `AddGroup` with `takeOwnership` true**, on entries and groups alike, which is the whole of what inheritance walks | resolved a grant written on the root group from an entry three groups down |

Two things this section deliberately does not claim. Nothing here was measured about **where** a rule
is asked, because these were measured before the resolver was wired to the router, and section 14 is
where the two answers are combined. And the `GroupForm` snapshot hazard in section 9 applies to
grants exactly as it applies to everything else in custom data, which is why the editor has a
section of its own, 13.

## 13. Editing a grant on somebody else's dialog

Established while building the grant editor.

| Fact | How it was established |
| --- | --- |
| **The group and entry dialogs both keep their own copy of a node's custom data**, in `m_sdCustomData`, and write it back over the node when they are accepted. Writing there is what makes their OK keep a grant and their Cancel discard it | the host tier writes through a real `GroupForm` and finds the live group untouched; the end to end tier presses OK and finds the rule in the `.kdbx` afterwards |
| **That copy does not exist while the tab is being built.** KeePass makes it as the dialog loads and announces the window during the same load, so a plugin that captured the field at attach time would capture null and write nowhere | asserted directly: after `InitEx` and before the dialog loads, the field is null. The adapter therefore resolves it on every access and reads from the node itself |
| `PwEntryForm.InitEx` **refuses a null image list**, which a test constructing one has to supply | `ArgumentNullException` on `ilIcons`, from the first attempt |
| A `DataGridView` cell reaches UI automation as an `Edit` whose name is its column header plus its row, and **a read only one exposes no pattern that operates it**: the value pattern refuses, and there is no invoke, because opening a picker is a decision the panel makes about a click rather than a property of the cell | the end to end test asked, was told the value is read only, and now synthesizes a real pointer and real keystrokes instead |
| The main menu path to a group's own dialog is **Group, Edit Group**, and both report access keys | dumped the real menu tree through UI automation rather than guessing at it |

## 14. Combining the two answers

Established while combining the admission record with the rules, and the first entry changed the
design rather than confirming it.

| Fact | How it was established |
| --- | --- |
| **The intersection is not a stage of its own.** Handing the resolver the profiles the admission record carries makes `record INTERSECT acl` structural: there is nowhere the two are compared, and so nowhere they can be compared wrongly | tried to write the comparison and found there was nothing to write. The tests that remain assert the two failure directions instead: a rule about a profile no record carries, and a record naming profiles no rule mentions |
| **The plugin's own JSON reader refuses a boolean**, so an audit line saying `"permitted":true` could not be read back by the thing that wrote it | asserted it, and changed the format: the decision is a word, `"decision":"permitted"`, which the design named that way anyway. The format later took a bare `null` for a member that does not apply and an array for the profiles a refusal names as removed, and the reader refuses the null, deliberately: nothing in the plugin parses a line back, `AuditEntry` has why |
| A refusal and a node that is not there have to answer the same thing, or a client that may reach nothing can map the database one identifier at a time | which is what `list` exists to be able to withhold, so the router answers 404 to both and the audit records which it was |

## 15. Reading and writing JSON

Measured on the `spikes/json-reading` branch, taken from `master` and never merged, where every count
can be reproduced by building. What the mainline keeps is the decision, and the two things about
Json.NET that anybody reading `JsonBody` has to know.

| Fact | How it was established |
| --- | --- |
| **One assembly against nine decides the reader.** `Newtonsoft.Json` arrives alone; `System.Text.Json` brings eight more on this framework, one of them `System.Runtime.CompilerServices.Unsafe`, which wants a binding redirect a plugin cannot supply because the only `app.config` in play is KeePass's | built a throwaway `net48` library against each and counted the output directory |
| **No schema library is taken**, at fourteen assemblies, twenty, or a license an MIT plugin cannot hold. What checks a body instead is a checker over the subset this wire carries, with a startup refusal of any schema it does not implement, so a subset cannot become a gap | the same counts, and `LICENSE.md` read out of the package |
| **Json.NET accepts a trailing comma, a comment, a string or a name in single quotes, a name in no quotes, `\'` inside a string, whitespace beyond JSON's four between the tokens, such as U+00A0, U+2028 or U+3000, and a raw control character in a string.** None carries a member and none changes what the object holds, so all but the last are accepted rather than fought: refusing them is exactly what owning a grammar would have cost. The control character is refused, by `JsonBody` rather than by the grammar, whether it arrives raw or escaped, unless it is a tab, a line feed or a carriage return, because a `.kdbx` drops the rest, section 18 | ran the body fixture against it. The control character matters in the other direction too, because an entry's notes hold paragraphs, so a line break has to survive and what keeps a line break out of a log is escaping on the way out |
| **Json.NET reports a NUL after the object as the end of its input**, so the check that refuses anything after the closing brace, which asks the reader for one more token, passed a body ending in one, though text after the NUL was refused. So a body holding a NUL is refused before the parser sees it | read the object and one more token from the built plugin, with a NUL alone, a NUL and text, and a NUL and a second object after it |
| **Json.NET turns any date shaped string into a `DateTime` unless told not to**, so a value would come back reformatted rather than as it was sent | `DateParseHandling.None`, pinned by a test, because this is the sort of default discovered by a client reporting a timestamp it never sent |

## 16. Serving the API

Established while building the routes, and the first two changed the design rather than confirming
it.

| Fact | How it was established |
| --- | --- |
| **A path has to be split before it is decoded**, which is what RFC 3986 section 2.2 says and what this did the other way round. Decoding first turns `%2F` into a separator, so an attachment named `a/b` arrives as one segment too many and matches no route: not refused, not mis-served, impossible to address at all | tried to write the attachment routes and found the name unreachable. Splitting first also stops `+` meaning a space in a path, where that rule never belonged, and lets `notes..txt` be a name rather than a traversal |
| **`JsonBody`'s 16 KB string ceiling would have made attachment writes a fiction**, capping an upload near 12 KB | counted: base64 of a megabyte body against a reader that refused a string over 16 KB. It is now the same megabyte the transport allows a body, and each route's declared `maxLength` is what actually limits a string |
| **A `PwEntry` KeePass makes has the five standard strings and one `new PwEntry` has none**, so an entry created through the gate looked wrong in the user interface and made an update of `Password` a create | two host tests failed on a created entry having no `Password` at all |
| The response side was already wider than the request side before any of this: the status route answers `"ok":true`, and `JsonBody` refuses booleans | asserting an entry read back through the request reader failed, correctly, because a read carries a nested `fields` object |

## 17. Reconstruction

The proofs left the mainline once they had answered their questions, and are kept on branches of
their own: the transport, vendoring and spec-first proofs on `spikes/http-lib-measurements`, and
the TLS and X.509 probes on `spikes/tls-and-certificates`. To rebuild any of them without either:

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

## 18. What KeePass does with a change, measured

**A `.kdbx` silently drops 2,079 of the 65,536 code points in the basic multilingual plane.** KeePass
writes every unprotected string through `StrUtil.SafeXmlString`, which removes what XML 1.0 cannot
represent and reports nothing: the 29 control characters other than tab, carriage return and line
feed, the 2,048 surrogates when they arrive without a pair, and `U+FFFE` and `U+FFFF`. A surrogate
pair survives, including the pairs whose halves look like those last two, so `U+1FFFE` is stored as
it was sent. Measured by calling that method on KeePass 2.61.1 once per code point and comparing
what came back, which is also what `JsonBody.IsUsable` is now written against: the two agree on
every one of the 65,536 inside a string. Alone, four differ, since `IsUsable` also refuses a string
that is whitespace alone, which `SafeXmlString` keeps, so a lone tab, line feed, carriage return or
space is refused for the reader's reason rather than KeePass's. `JsonBodyAgainstKeePassTests`
holds the two to both halves of that against the pinned KeePass.

**A lone surrogate reaches a body reader only as `U+FFFD`.** Json.NET substitutes the replacement
character for an escaped half while it is reading, so `{"a":"one\uD800two"}` comes out of
`JObject.Load` carrying `U+FFFD` and nothing downstream can tell it from a caller that sent one. A
surrogate written raw in the text is passed through untouched, which is why the refusal is still
worth having: `JsonBody` also reads documents out of a `.kdbx` this plugin did not write. Measured
against the Json.NET this builds against, by printing the code points that came back.

**`PwEntry.CreateBackup(PwDatabase)` applies the operator's limits itself.** It clones the entry,
clears the clone's own history, adds it, and then calls `MaintainBackups`, which enforces
`HistoryMaxItems` and `HistoryMaxSize`. So a plugin that takes a backup does not also have to decide
how many to keep, and must not: the operator answered that in Database Settings, for KeePass's own
edits and for anything else that writes. Read off KeePass 2.61.1 rather than from documentation.

**`DatabaseSettingsForm.OnBtnOK` can be driven outside a running KeePass.** It touches only its own
controls and the `PwDatabase`, and calls nothing that puts a window on the desktop, so the host tier
can press it on a form that was never shown and watch what it writes back. That is how the recycle
bin snapshot was measured rather than modeled.

## 19. The gate, and where it can run

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
monitor, which is enough for the places the tier moves a real pointer.
`spikes/hosted-runner-desktop` holds the probe and the numbers.

**Containers were never the alternative.** Linux is the wrong operating system for UI Automation,
`keybd_event` and WinForms on .NET Framework; Windows containers have no interactive desktop; and
GitHub does not support container jobs on Windows runners at all.

**Required status checks were available and not configured** when this was measured, so the
workflow reported and blocked nothing, which was a property of the repository's settings rather
than of the gate. It is configured now: a ruleset on `master` requires `gate passed` before a
merge, and another requires it of a release tag, as `CLAUDE.md` has under "There is CI".

## 20. What a version of a node can be made of

**KeePass keeps no version of a group or an entry, and its modification time is not one.** The
obvious tag for `If-Match` was the modification time, perhaps beside the time a node last moved,
and all of what follows was measured against KeePass 2.61.1 before choosing:

| What was measured | What it showed |
| --- | --- |
| `DateTime.UtcNow`, which `Touch` stamps, sampled in a loop | steps of 0.5 to 1.5 milliseconds on the development machine, so two changes inside one step share a time; Windows can step as coarsely as 15.6 |
| a thousand `Touch` calls with a change between each pair | every pair carried the same time |
| a KDBX 4 save read back | the time keeps whole seconds and loses the rest, so every time changes across a reload with nothing changed |
| a synchronize where the other copy's strings changed at the very same time | its strings are taken and the time stays where it was |
| a synchronize where the other copy's change carries an older time | nothing is taken, and the time stays |
| a move, by `Remove` and `AddEntry` as the main window makes one, and one arriving by merge | `LocationChanged` moves and the modification time does not |
| every method of `KeePass.exe` and `KeePassLib` that changes a string, an attachment, an expiry, a name, notes or a parent, read off the IL | every edit KeePass makes from its own windows ends in `Touch` or a set of the time, apart from importers, which make new nodes, and the entry dialog's working copy, which `SaveEntry` stamps; what a plugin does is nobody's promise |

So a tag made of times misses a change in four ways: two inside one step, a merge at an equal
time, a move unless `LocationChanged` is in it, and a plugin that never calls `Touch`. The tag is
a digest of the content instead, and `NodeTagsTests` drives each of those cases, the merge among
them, against real KeePass objects. The two times a route serves are in it too, to the second as
they are served, which the reload above keeps, so a time changed with nothing else is still a
change and a reload is still not one.

**A plain digest of an entry is a way to guess its password**, which is why the tag is an HMAC.
A client with `read` and without `protected` sees every string but the protected ones, so over a
plain hash it could try candidates offline until one reproduced the tag. The key is derived from
the host certificate's stored PKCS#12, which no client holds, so a tag is the same across a
restart; it was drawn at load first, which made every tag change whenever KeePass started.

**A keyed digest is still a way to guess, if it is handed to a client that cannot read what it
covers.** Measured in the review of the change that added it: a client whose rule held `update` and
`protected` and not `read` wrote an empty update to learn the tag, then wrote each candidate PIN
into the password and compared the tag that came back with the first, and had a three digit one in
614 writes and 7.8 seconds; a rule holding `read` beside another profile's `update` and
`protected` recovered an eleven character password of a known shape in four. A key stops a tag
being computed and not being compared, so a tag went only to a rule that may read everything it
covers, `API.md` §410.

**A tag over what its caller may read gives the guess nothing to compare, and gives every reader a
tag.** That rule closed the attack by handing most rules no tag: an entry KeePass makes holds a
protected `Password` from the start, so a rule without `protected` was never handed a tag for it.
The operator ruled on 2026-11-10 to reverse it. The tag is taken of the entry as its caller's
rules open it, with a value the caller may not read entering as a fixed mark beside its name, and
it is handed to every caller holding `read`. Both shapes of the measured attack were run again
through the real router and vault, in `TagOfTheCallersViewTests`: the rule holding `update` and
`protected` without `read` is handed no tag at all, and the rule holding `read` beside another
profile's `update` and `protected` is handed the tag a rule holding `read` alone has, which
stays where it was while the password under it is changed with nothing touched. A guess it
writes is written like any change, a save and a revision for a right guess and a wrong guess
alike, so its tag moves with `modified` the same for both and with nothing of the value.
What moves a narrow tag was measured
case by case in `NodeTagsTests`, on the same entry changed with nothing touched: a withheld value
changed moves only the tag of a view reading protected values, an attachment's bytes changed at
the same size only the tag of a view reading attachments, and an attachment added or renamed
also the tag of a view that lists them. Nothing about how far a rule reaches is in a tag, so an
entry holding no attachment has the same tag whether or not its caller's rule reaches
attachments, and a rule widened over an entry where nothing new comes into sight moves no tag:
`TagOfTheCallersViewTests` widens `read` to `read` with `attachments` over such an entry and
writes under the tag from before. What is left for a caller writing a value blind is the
entry's `modified`, which is in every view: its tag moves when a change moves the served second,
so it misses what the table above has a time missing, and nothing else.

**An update that writes nothing was a way to guess as well, with no tag in it.** A client whose
rule held `update` and not `read` sent a value and was told whether the node already held it: a
match took no revision, moved no time and saved nothing, and anything else did all three, so the
`modified` a listing serves, or the time the answer took, gave the match away. One guess for each
value an operator set, since a wrong guess replaces what was there. Reproduced through the real
router and vault for an open field, a protected one, an entry, an attachment and a group, eleven
cases in `SkippedOnlyForAReaderTests`, each reaching no save where it now reaches one. An update
is skipped only for a caller that may read every value it names.

**A write refused for an entry's size was a way to measure a value, with no guess in it.** An
entry's strings were held to 524288 characters in both directions, and a write that would take
them past it was refused `413` with what they would come to, protected values included. A client
whose rule held `update` and not `read`, or `read` without `protected`, padded the entry with
strings of its own to just under the ceiling and sent a last write: the refusal named the whole
count, and the count less the client's own padding is the length of what it was withheld. With
the count left out, the padding at which the refusals begin tells the same, in a search of about
twenty writes. Reproduced through the real router and vault in `NoSizeToldToAWriterTests`: two
entries alike but for a password ten characters longer in the second, the same padding in both,
and the same write answered `200` for the first and `413` with `524293 characters` for the
second, for a field updated, an entry updated and a field added. The same count went to a single
field read: a protected string past the ceiling was `413` with its length to a client that may
not read it. The operator ruled on 2026-11-10 that no write is refused for an entry's size and
that a read is measured over what its caller is handed: the view a read was
measured by held no length for a value it withholds, both writes are now made and answered
alike, and the withheld field is the `null` every withheld value is. No read is held to a
size since 2026-11-14, `API.md` §412, and a view holds no length of any value.

**An answer built whole is alive several times over while it is written, and an answer written
as it is sent is alive a value at a time.** Built whole, an answer made of an entry's values is
every value read, the document they are written into, the text taken of that document and the
bytes the text encodes to, all alive when the first byte goes out. Written as it is sent,
`HttpResponse.Write` with content, each value is read where it is written and goes out in
chunks. Measured on the development machine in a Release build, over KeePass's own strings, each
form writing its whole answer, head and all, into a stream that keeps a digest of it and
nothing else:

| Strings | Answer | Built whole | Written as it is sent |
| --- | --- | --- | --- |
| 8 protected, of 65536 characters | 0.6 MB | 3.6 to 3.9 ms, 4.5 MB allocated, 4.0 MB alive at once | 2.6 to 3.1 ms, 1.5 MB allocated |
| 200 protected, of 65536 | 14.8 MB | 92 to 96 ms, 112 MB, 99 MB alive | 69 to 74 ms, 38 MB |
| 200 held in the open, of 65536 | 14.8 MB | 74 to 79 ms, 74 MB, 74 MB alive | 53 to 54 ms, nothing |
| 50 protected, of a million | 56.6 MB | 340 to 354 ms, 427 MB, 379 MB alive | 254 to 265 ms, 143 MB |
| 50 held in the open, of a million | 56.6 MB | 277 to 297 ms, 284 MB, 284 MB alive | 200 to 208 ms, nothing |

Alive at once as it is sent, collected and counted after each value, came to under a tenth of a
megabyte in every row: the chunk being gathered and the writers' own buffers. Beside that stands
the value being written, two bytes a character, and for a protected value the bytes it was
decrypted to while they are decoded, and nothing that grows with how many values an entry
holds. A value held in the open is the string KeePass already keeps, so writing it allocates
nothing. A protected value is decrypted and decoded to be written, which is the 1.5, 38 and
143 MB, each of it short-lived. Written as it is sent an answer also takes a quarter to a third
less time, since nothing is copied into a document, out of it again and into bytes.

**Counting the length first, to keep `Content-Length`, was measured and refused.** The same
answer written twice over, into a stream that only counts and then into the connection, keeps
as little alive and took thirty to forty percent longer than the answer built whole, 5.3 ms
against 3.5 in the first row and 473 to 508 against 340 to 430 in the fourth, since every
protected value is decrypted and escaped twice. `DESIGN.md` §143 has what was chosen.

**A listing handed over whole is alive five times over beside what it is written from, and
written as it is sent it adds nothing.** Measured the same way, over invented entries of the
six members a listing of entries writes for each. Ten thousand entries are an answer of 2.2 MB:
handed over whole, 9 to 11 ms with 11 MB alive at once beyond the entries it is written from,
and as it is sent, 5.6 to 6.3 ms with 0.03 MB. A hundred thousand are 21.8 MB: 124 to 127 ms
with 109 MB alive, against 58 to 59 ms with 0.03 MB. So the listings of groups and of entries
are written as they are sent too, and what a listing keeps alive is the descriptions taken
inside its hold.

**What a tag costs on an entry far past the ceiling a read was held to**, measured while a read
was held to 524288 characters: fifty
strings of a million characters each, on the development machine in a Release build, through the
real router and vault. A read of the whole entry was refused then and took no tag. Every other request
that hands a tag to a client with all fifty values in sight, a listing of the fields or a read of
the title, digests the fifty megabytes. **Taken inside the hold, that was KeePass's thread standing
still for every such request**: 45 to 47 milliseconds, with 48 MB allocated for the UTF-8 form of
each value in turn, and 93 to 104 milliseconds and 191 MB for the first request after the values
were loaded, since KeePass decrypts and decodes each value once when it is read as text and then
keeps the text. So a read takes its view of the entry inside the hold and asks the view for
everything else once the hold has ended, `DESIGN.md` §119. Measured the same way after that, the
hold for those fifty strings is 0.07 to 0.09 milliseconds, and what the request took on its own
connection's thread, while a tag read every value for every request, depended on how KeePass holds
the values. Protected and held as bytes, the way a file's are: about 59 milliseconds and 48 MB,
every time, a copy of each value decrypted, digested and overwritten, a megabyte at a time, since a
protected value is no longer read the way that makes KeePass keep it as text, below. Held in the
open: 41 to 43 milliseconds and 0.2 MB, the tag encoding
a long value a piece at a time. A whole read refused for its size measured the protected values the
first time, 46 to 73 milliseconds and 48 MB, and was answered in a tenth of a millisecond after that,
none of it inside the hold. A client that reads the entry without `protected`, the fifty being
protected, reads none of them: a withheld value is never read. A write takes its tags
inside its own turn on KeePass's thread, the tag it is asked against and the tag it leaves: 58 to 65
milliseconds and 48 MB each over the fifty protected strings, 40 to 43 milliseconds over fifty held
in the open, beside a save of 2.2 seconds for the protected strings and 0.3 seconds for the strings
held in the open, which the write causes whoever sends it. Each of those figures for a tag is the
cost of the first tag over a value now, and no tag after it pays it: a digest of each value is
kept, below.

**The hold grew with how many strings an entry holds, and a listing's with what it wrote, until both
were made after the hold.** With the values read after the hold, taking the view stayed inside, and
walked every string to find which the database protects and which a listing shows, several
comparisons of each name by the culture's collation: 4.0 milliseconds of hold for an entry of ten
thousand strings of one character, and 54 to 59 for a hundred thousand, in Release, whichever of the
entry's reads was asked. No write bounds how many strings an entry holds. A listing of ten thousand
entries held KeePass's thread for 38 to 54 milliseconds where titles and addresses are held in the
open, 42 to 44 where the database protects them and the caller is shown neither, and 91 to 94 where
the caller is shown them, a copy of each title and address decrypted inside the hold for every
listing. So the view's strings and attachments are made after the hold, of a copy of the entry's
lists and of the database's settings taken inside it, and a listing is written after the hold from
the descriptions taken inside it, `DESIGN.md` §119. Measured the same way after that: the hold for
an entry of ten thousand strings is 0.29 to 0.50 milliseconds and for a hundred thousand 2.7 to 4.1,
which is the copy of the list, and the request then takes about 12 and about 150 milliseconds on its
own connection's thread, making the strings and taking the tag over their names. A listing of ten
thousand entries holds KeePass's thread for 22 to 26 milliseconds where titles are held in the open,
and for 30 to 33 where the database protects them, shown to the caller or not, since whether they
are shown is a rule asked of each entry; the 2.3 MB is then written in 15 to 17 milliseconds where
titles are in the open and about 62 where each protected title shown is decrypted, on the
connection's thread.

**A tag read every value in sight for every request until it kept a digest of each.** With the
values read after the hold and a protected value left as KeePass held it, every tagged answer over
the fifty protected strings of a million characters cost about 59 milliseconds and 48 MB on the
connection's thread, and a write twice that inside its turn. A tag now takes a value by its SHA-256,
taken once for as long as KeePass keeps the object the value is held in and kept against that
object, as an attachment's digest is, `DESIGN.md` §136. Measured the same way, in Release: the first
tagged read of that entry takes 62 to 88 milliseconds and 48 MB, and every tagged read after it 0.23
to 0.30 milliseconds and 0.08 MB; fifty such strings held in the open take 36 milliseconds and 4 MB
the first time and 0.13 to 0.21 milliseconds after; an entry of six strings of twenty characters
takes a tenth of a millisecond either way. The digests kept for ten thousand entries of six strings,
a third of them protected, come to 12.0 MB, about two hundred bytes for each value, each living as
long as its value's object. A synchronize needs nothing told: a value a merge changes is held in
another object, which has no digest kept, `NodeTagsTests`. What shows is the first tag over a long
value taking longer than the tags after it, to a client that sees the value, after anything that
makes KeePass put the value in another object: a change to it, a lock and unlock, a synchronize
that takes the entry. The buffers a long value held in the open is encoded through are made for
each value, which is the 4 MB of the first tag over the fifty.

**A view of an entry can be asked after the hold it was taken in, because KeePass replaces the
object a value is held in and never changes it.** Read off KeePass 2.61.1, decompiled, and
measured against it. `ProtectedStringDictionary.Set` and `ProtectedBinaryDictionary.Set` put
another object under the name, and no public member of `ProtectedString` or `ProtectedBinary`
changes an object in place: `Insert`, `Remove`, `Trim` and `WithProtection` each hand back
another string. Both are built for a second reader. `ProtectedBinary.ReadData` decrypts its
bytes, copies them and encrypts them again under a lock of its own, and `ProtectedString`
takes its bytes into a local before it reads them, keeps the string it decoded and only then
lets the bytes go, in that order. Measured: a view kept while its entry had values and
attachments replaced, removed and added, its times moved, the entry moved to another group and
the database locked gave the tag it gave when it was taken; and in forty rounds of a tag taken
on a second thread while the first read the same twelve values of 200,000 characters, no tag
differed and nothing threw. `KeePassContractTests` pins the public surface of both types and
the reading from two threads, and `DecidedAtTheReadTests` holds the real router and vault to an
answer and a tag of the entry as it stood inside the hold.

**`ProtectedString.ReadString` ends KeePass's protection of the value it reads, and the gate
read every value with it.** Read off KeePass 2.61.1, decompiled, and asked of the object's own
field. A protected string made from bytes, which is how a loaded file's are made, holds them
encrypted. `Length` and `ReadUtf8` each decrypt a copy, and `Equals` compares two such copies,
all of them leaving the object as it was. `ReadString` decodes the value, keeps the string in
`m_strPlainText` and lets the encrypted bytes go, for as long as the object lives, and a string
made from text keeps that text whatever its flag. A tag digests every value in its caller's
sight, so a listing of an entry's fields, or a read of its title, left every protected value of
the entry in the open in KeePass's memory, the values nobody asked for with them, and a
protected value written through the gate was in the open from the start. A protected value is
now read with `ReadUtf8`, digested from that copy, or decoded from it where it is served, and the
copy overwritten; and a protected value is handed to KeePass as bytes, `StoredStrings`. What
that cost, while a tag read every value, was the copy for every request, where KeePass keeping
the text made every request after the first cheaper: the 59 milliseconds and 48 MB above,
against 40 to 42 milliseconds and 0.2 MB with the values left in the open after a first request
of 90 milliseconds and 143 MB. With a digest of each value kept, above, it is the copy for the
first tag over a value, and a copy of each protected title and address a listing shows, for
every listing.
What it does not do: a value that is served is a string in the process until the runtime reuses
its memory, and so is the answer it is written into, and neither can be overwritten.

**KeePass's own save reads some values as text.** `KdbxFile.WriteObject` writes each of an
entry's five own strings protected or not by the database's setting for that name, whatever the
string's flag, and reads with `ReadString` what the setting leaves unprotected. Measured: after
a save, a title and an address flagged protected in a database that protects neither were text
in memory, by KeePass's own hand, and a password and a protected custom string were not.
`ProtectedInMemoryTests` holds every read and every write through the real router to leaving
each protected value as KeePass held it, in a database whose settings protect the strings it
flags, and `KeePassContractTests` pins the field and what each of those members does to it.

**Where the work is done, measured before it was moved**, on the same fifty strings in Release, with
the view taken inside the hold in every case and every value read with `ReadString`, so the first
time is KeePass decoding a value and every later time finds the text kept. Values read and digested
inside: the hold is 93 to 104 milliseconds the first time and 45 to 47 after. Values read inside and
digested after: 53 milliseconds of hold the first time and 0.04 after, with 45 to 48 after the hold.
Both after: 0.04 milliseconds of hold every time, with 96 to 115 after it the first time and 44 to
50 later. Eight strings of 65,536 characters, an entry at its ceiling, cost half a millisecond
wherever it is done. A hundred attachments of 512 KiB, the most that is served, cost 60 to 90
milliseconds and 50 MB the first time their digests are taken, wherever that is, and 0.15 after,
since a digest is kept against the object. Encoding each value for the digest in pieces of 16,384
characters through one buffer gave the digest of the value encoded whole with 0.1 MB allocated
against 47.7 MB, in 39 to 42 milliseconds against 49 to 56.

**KeePass counts a protected string's characters by decrypting it, the first time it is asked,
which "measured with nothing decrypted" had wrong.** `ProtectedString.Length` answers from a count
it keeps, `m_nCachedLength`, and fills that count the first time: from the string, where KeePass
holds the value as a string, which is how it holds a value constructed from a string, a protected
one included, and every value once `ReadString` has read it; and by `ProtectedBinary.ReadData`,
`GetCharCount` and zeroing the copy, where it holds the value as bytes, which is how a file's
protected values are held once loaded. Read off the getter's IL in 2.61.1 and measured: a
protected value of a million characters held as bytes took 4.7 milliseconds to measure the first
time and 0.02 the second, and the first request to measure the fifty strings above took 196
milliseconds and 49 MB where every later request took a millisecond, in a Debug build; the
figures above for the same request are a Release build's. An attachment is not so:
`ProtectedBinary.Length` returns a field. Nothing public answers a string's length without that
decryption; the UTF-8 byte count beside the encrypted bytes is private and is a bound on the
characters and not their count. So what holds is narrower than what was written: no value is
handed anywhere until it is asked for, building a view asks no length, a view asks the length
only of a value in its caller's sight and only when the size is asked, so a withheld value is
neither read nor measured, and the first measurement of a value in sight after a file load
decrypts it once, inside KeePass's own type, with a number coming out.
`NodeTagsTests.A_protected_value_held_as_bytes_is_decrypted_once_to_be_measured_and_never_for_a_view_withholding_it`
counts the decryptions. The view asked every string's length as it was built until this was
measured, a withheld string's among them.

**A value a read decodes is not always the bytes the store keeps.** A protected string another
program wrote with bytes that are not UTF-8 is served with each bad sequence replaced, and KeePass
keeps the decoded form once it has been read, so a tag over the stored bytes changed the first time
the value was read as a string. The tag hashes the value as a read decodes it.

**What a digest costs, on the development machine under .NET Framework 4.8:** six microseconds for
a group, and the same for an entry's ten strings with two kilobytes of notes, protected ones
decrypted. Attachment bytes go through SHA-256 at about 2 GB/s, which is 5 milliseconds for
10 MB, against 8 milliseconds to decrypt a protected attachment of that size, and only up to the
largest attachment the gate hands out, 512 KiB, above which the size stands for the bytes: the
review measured a 201 MiB attachment costing 260 milliseconds and 201 MiB of memory to every
request that looked the entry up, the router's among them, and 32 at once holding 1.6 GB. **And
attachments are hashed once per object**, because `ProtectedBinary` offers nothing that changes its
bytes and KeePass keeps the object's own hash code inside it; `KeePassContractTests` pins that, and
two readers asking at once wait on one digest rather than taking two. The probes that measured all
of this are on `spikes/entity-tags`.

## 21. Taking a turn on KeePass's thread

**A call posted to KeePass's thread was believed to wait its turn, and it went ahead of everything.**
Review 4 measured which way of taking a turn on that thread leaves the operator's input and
KeePass's timers alone, and where in KeePass's own work a turn can start. The probes are on
`spikes/keepass-thread`.

| Fact | How it was established |
| --- | --- |
| **A posted turn goes ahead of the operator's input and of every timer.** Thirty-two threads each taking the window's thread for 17 ms at a time through `BeginInvoke` let one of sixty mouse moves through in three seconds. Asking `GetQueueStatus` for input first let input through and still held a 100 ms timer off for 600 ms whenever no input came, because `QS_TIMER` never showed: Windows makes a timer's message only once nothing else is queued | mouse moves sent with `SendInput` to a window on this machine's desktop, and a timer on the same window |
| **Started on `Application.Idle`, turns give way to everything.** Input waited 8 to 11 ms at the median, at most the rest of the turn it arrived during, and the 100 ms timer ticked no more than 125 ms apart, against 112 with nothing queued, at 95% of the thread spent in turns. Inside a WinForms `ShowDialog` the same | the same window, in each loop |
| **`Application.Idle` is never raised inside `Application.DoEvents`**: none of 37 turns started inside a `DoEvents` loop came from it. And KeePass pumps through `DoEvents` in every long operation: `SaveDatabase` through its status bar logger and `UIBlockInteraction`, the merges through their status loggers, `StatusProgressForm`, which XML Replace uses, and `ExportUtil.Export` | the same probe, and the callers of `DoEvents` in KeePass 2.61.1's IL |
| **A message box, a task dialog and a file dialog are each a visible window of class `#32770` on their thread, and a WinForms form is not.** `EnumThreadWindows` from another thread finds one in about 21 µs, under a `MessageBox` and an `OpenFileDialog` and not under a `ShowDialog` | a probe opening each over a form and asking from another thread |

## 22. Reading while KeePass changes the tree

**Reads on a connection thread were believed safe because they raised nothing a retry could not take
again, and that was the wrong test.** Review 4 measured what a read meets while KeePass's own thread
changes a list, and then what each way of keeping the two apart costs. The probes are on
`spikes/keepass-thread`.

| Fact | How it was established |
| --- | --- |
| **A lookup racing a change on KeePass's thread misses a node that is there, and gives no sign.** With the group sought last in its list, `FindGroup` missed it 20,762 times in 2.07 million while groups were put in and taken out at the front; with it in the middle, none to two in two million. A sweep met a slot a removal had emptied and threw `NullReferenceException` inside KeePassLib, and `GetEntries` handed the empty slot back in its copy | a probe changing a real `PwGroup` on a form's own thread while another thread looked up and swept, counting what it got |
| **`List<T>` moves its version after its size, so a reader that checks the version finds almost nothing wrong.** Checking it either side of a walk caught 5 to 27 of about twelve thousand misses, and waiting 100 µs before trusting it still missed 32 while flagging 7,350 of 7,814 good walks | the same churn, with the version read through reflection |
| **Holding KeePass's thread still for the read closes it.** A `BeginInvoke` callback that waits on KeePass's thread until the reader, on its own thread, is done: no misses and nothing thrown in 71,503 and 46,297 walks, against 224 and 128 misses and 91,770 and 58,745 exceptions for a reader beside it that held nothing. Waiting for the hold took 0.07 ms at the median, under a millisecond at p99, and 115 ms at worst, the longest handler | the same churn |
| **A read holds the thread for its own length, and that is short.** Over a thousand groups and ten thousand entries: a full sweep 16.9 ms at the median in Release, a lookup 0.17 ms, the structure alone 0.055 ms | timed in Release and Debug |
| **A whole request decided and served inside one hold is still short**, measured while every read made its whole answer inside the hold; what is made of an entry's values is made after the hold now, section 20, so the entry and attachment figures here are no longer time on KeePass's thread. Over a thousand groups and ten thousand entries, with an authority and an admitted client, at the median in Release: every entry listed, 2.3 MB of JSON, 55 ms in one run and 43 in another, 69 at worst; every group listed 2.3 ms; a group 0.11 ms; an entry 0.81 ms; an entry of fifty fields of ten thousand characters 2.4 ms; an attachment of 512 KiB, the most that is served, 2.6 ms and 15 at worst. The first read of an entry holding forty such attachments 47 ms, its tag digesting 20 MiB once, and 0.83 ms after that. Reading the caller's record and the profile list again is about 0.03 ms of each | the real router and vault over a database made for it, one timed hold a request and forty-one runs of each, in Release and Debug |
| **KeePass changes some live entries in place outside its dialogs**, so a hold over the structure alone would not be exact: placeholders that generate a value, opening an attachment, editing several entries at once, XML Replace, the OTP generator settings and a merge's database properties | KeePass 2.61.1's IL, every caller of the in-place mutators of an entry's dictionaries |
| **A read needs a turn where Idle is never raised too.** Inside a `MessageBox`, whose loop raises no Idle, a 50 ms fallback timer kept reads going at fifteen a second, and the window's own timer stayed on time | the window section 21 measured, inside a message box |
| **That timer served a read from inside a merge, of a tree the merge had not finished.** A merge pumps through its status logger between one step and the next, and a read asked for at the first step of a merge bringing forty entries was served inside the pump and counted none of them. A read inside a pump had been held harmless, which is true of a save and not of a merge | `PwDatabase.MergeIn` over two real databases on a message loop, with a logger pumping `DoEvents` for 300 ms at its first call and another thread asking for the read there |
| **A thread inside a merge has `PwDatabase.MergeIn` on its stack, whichever way the merge was asked for.** The overload taking a status logger is 435 bytes of IL, which the just-in-time compiler does not compile into its caller, and `ImportUtil.Import`, `XmlUtil.PerformXmlReplace` and the ten-byte overload without a logger are everything in KeePass 2.61.1 that calls it. Asked from the logger under each of the five merge methods, the stack held it at every call, and never before or after. Nothing KeePass announces covers the same ground: `UIIsInteractionBlocked` is raised around a save as well, and a merge a plugin runs raises nothing | every call site of both overloads read off KeePass 2.61.1's IL, and the merge driven with a logger that reads its own thread's stack |
| **A look at the stack costs twenty to fifty microseconds.** 18 µs at fifty frames, 25 µs at ninety and 45 µs at a hundred and eighty, frames read without file or line and every method compared by type and name | twenty thousand looks at each depth on .NET Framework 4.8, the median of five runs |

## 23. WinForms' own error dialog, inside KeePass

**The plugin's remarks held that the dialog's Quit ends KeePass outside its own shutdown, and it does
not; what the dialog does cost lay elsewhere, and was worse.** Measured for review 4 in KeePass 2.61.1
on .NET Framework 4.8, while raising a defect again for this dialog to ask the operator about was
weighed and given up, `DESIGN.md` §141. The dialog's own behavior was measured with a throwaway
plugin throwing once from a timer on KeePass's thread, KeePass registering no
`Application.ThreadException` handler, over a throwaway database: twenty-two runs, a run for each
case.

| Fact | How it was established |
| --- | --- |
| **Quit closes KeePass as closing its window does, first.** `Application.ExitInternal` raises `FormClosing`, with `CloseReason.ApplicationExitCall`, on every open form, so KeePass's main window asks whether to save a changed database in the task dialog its ordinary close shows, then `FormClosed`, where KeePass unloads its plugins and their `Terminate` runs, and only then does the process end, with exit code 0 | the probe's own handlers on the main window, with the stacks from the timer through `OnThreadException` to the save question and to `Terminate`, and the database file read afterwards: changed after Save, untouched after Discard |
| **Cancel on that question ends KeePass anyway**, though the question promises that KeePass will not be closed: nothing is saved, `FormClosed` is never raised and no plugin's `Terminate` runs | the same, answering Cancel: the main window's `FormClosing` reported the close canceled and the process ended 5 ms later |
| **Escape presses Continue, and Enter shows the details.** Continue is both the accept and the cancel button, but the dialog opens with Details focused, and Enter presses the focused button | posted keys and real keystrokes on the dialog, the tab order read off it: Details, Continue, Quit |
| **A letter typed into the dialog presses the button it underlines**, without Alt: `q` is Quit, `c` Continue, `d` Details | `WM_CHAR` and key messages posted to the focused button, and real keystrokes |
| **A second exception while the dialog is up shows nothing more**, and an exception after Continue shows the dialog again | a second timer throwing five times under the dialog, and once after Continue |
| **Continue leaves KeePass running**, with the throwing handler's later work never done, timers ticking and the database open, and a later close of KeePass's own runs `Terminate` as usual | the same probe, closing KeePass afterwards |
| **Work posted to KeePass's thread runs inside the next pump, which can be KeePass's own work.** KeePass pumps inside a save, through `UIBlockInteraction` and its status bar logger while the file is written, inside a lock, as `PrepareLock` closes its windows, and between auto-typed keys. A dialog that comes up there stands inside that work, and with interaction blocked `LockAllDocuments` returns at once and the inactivity timer only postpones, so every lock is refused while reads, which start on `Application.Idle` inside the dialog's own loop, go on | a throw posted with `BeginInvoke` reached WinForms' handler inside a `DoEvents` in the middle of stand-in work; the callers of `DoEvents` and `LockAllDocuments` read off KeePass 2.61.1's IL |
| **Quit asks nothing, either, while KeePass's interaction is blocked or with closing set to minimize** KeePass, since its `FormClosing` cancels the close in both before asking: the process ends unsaved, and neither `Terminate` nor KeePass's own clearing of the clipboard runs | KeePass 2.61.1's `MainForm.OnFormClosing`, read rather than driven |

## 24. Setting a file aside under the backup and restore privileges

**`File.Delete` and `File.Move` let a privileged process past an ACL that the next file cannot get
past, so an audit sealed for append only lost every decision from its first roll on.** Measured
for review 4 on Windows 11 and .NET Framework 4.8, after a test of the sealed directory failed only
when the unit tier was started from Git Bash. A throwaway probe in Windows PowerShell 5.1 tried
each way of setting a file aside in a directory under each ACL, once from Git Bash run elevated,
with both privileges enabled, and once from an elevated PowerShell, holding both disabled, and
`JsonLinesAuditTests` keeps what the audit rests on.

| Fact | How it was established |
| --- | --- |
| **Git Bash run elevated enables the backup and restore privileges in every process it starts**, where an elevated PowerShell holds both disabled and an unelevated token holds neither | `whoami /priv` from each; the sealed-directory test failed twenty runs in twenty started from Git Bash and passed twenty in twenty from PowerShell and unelevated |
| **`File.Delete` and `File.Move` pass any ACL with the privileges enabled**: in a directory granting only listing, traversing, reading attributes and synchronizing, over files granting only appending, both did their work, and both were refused without the privileges. Each opens what it changes for backup, which is what lets the restore privilege grant the right to delete | the probe's sealed directory, in both runs |
| **Making a file passes no ACL that way**: a `FileStream` making the next file in that directory was refused with the privileges enabled, so a roll that went through left nothing to append to | the same |
| **A handle opened for the right to delete and no other, without asking for backup, is given only what the ACLs give**, enabled or not: refused on an append-only file in a directory that refuses removal, given where the directory lets files be removed | every case of the probe, answering alike in both runs |
| **A rename through that handle is held to the directory's ACL too**: `SetFileInformationByHandle` with `FileRenameInfo` was refused where the directory lets files be removed and not added, with the privileges enabled, where `File.Move` went through | the same |
| **A rename that replaces the previous file asks the previous file's ACL as well**: refused when the previous file allows only appending and its directory refuses removal, done when either lets it go | the probe's replacing cases |
| **A reader holding the previous file holds that rename off whatever it shares**, the right to delete included, where `File.Delete` lets the name go on this Windows | the probe, with a reader sharing reading alone and a reader sharing everything, and `File.Delete` and then `File.Move` under the second |
