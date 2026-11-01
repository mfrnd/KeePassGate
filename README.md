# Mono compatibility

**What would it take for KeePassGate to run in KeePass on Mono, and where does it stop today?**
KeePass 2.x runs on Linux under Mono. Every fact the plugin rests on was measured on .NET Framework 4.8
on Windows, and review 4 ruled the plugin Windows only (R9) because nothing had been measured anywhere
else. This branch measures it.

It is a spike: taken from `master`, never merged, and nothing here ships. The mainline keeps what it
settled and a pointer to it.

## The two halves

**The machine.** `probes/MonoProbe` is one .NET Framework 4.8 executable, built once on Windows and
run unchanged on both runtimes. Each probe repeats what the plugin does, call for call, without the
plugin:

| probe | what it repeats |
| --- | --- |
| `runtime` | which runtime, OS, culture and data directory the other lines came from |
| `issuance` | `AuthorityIssuer`: `ECDsa.Create(nistP256)`, `CertificateRequest`, the PKCS#12 trip `CertificateBlob` makes |
| `ecdsa` | what `issuance` asks before `CertificateRequest`, without naming that type, and where the type is |
| `bouncy` | the same certificates made by Bouncy Castle, read by the runtime's `X509Certificate2` |
| `chain` | `AnchorPin`'s `X509Chain`: own leaf, foreign leaf, an offered foreign root, expiry, a `caIssuers` address |
| `tls` | `GateTlsServer`'s shape: Bouncy Castle TLS 1.3, mutual, P-256, over a loopback `TcpListener` |
| `audit` | `JsonLinesAudit`: a stream asking for `FileSystemRights.AppendData`, the roll by move |
| `native` | the six `user32` calls in `NativeMethods`, with null windows |
| `culture` | path lowering, ordering of `KPG` keys, and case folding of the dotless i and the long s |
| `forms` | font fallback and the auto scale factor the KPG pages are laid out against |

**The product.** The plugin built at a named commit of `master`, confirmed separately and cited by
that commit:

- `product/NUnitOnMono` runs the plugin's own unit and host tiers in process through NUnit's framework
  API, with no adapter and no test host, so it runs on Mono as it does on .NET Framework. A type
  whose method signatures name something the runtime lacks is named and left out, rather than
  making NUnit refuse the whole assembly.
- `product/AutomationStubs` is two empty assemblies with the identities of Windows' UI Automation,
  which the host tier's assembly references for the end to end tier that shares it.
- `tools/classify-failures.py` puts every failed test down to one cause.
- `product/MakeDatabase` makes the throwaway database KeePass is opened on, with KeePassLib.
- `wsl/keepass.sh` starts the pinned KeePass under Mono with the plugin installed and drives it a step
  at a time on a virtual display.

## Running it

### On Windows: the baseline

```powershell
# The pinned KeePass, copied from a mainline checkout rather than fetched.
Copy-Item -Recurse ..\KeePassGate\.keepass .\.keepass

dotnet build -c Release probes\MonoProbe
dotnet build -c Release product\NUnitOnMono
dotnet build -c Release product\MakeDatabase
dotnet build -c Release product\AutomationStubs\UIAutomationClient
dotnet build -c Release product\AutomationStubs\UIAutomationTypes
.\windows\Run-Probes.ps1 -Out results\net48.txt
```

### In WSL: Mono

WSL needs the optional features `Microsoft-Windows-Subsystem-Linux` and `VirtualMachinePlatform`,
which take a restart. **WSLg is turned off before the first start**, in `%USERPROFILE%\.wslconfig`:

```ini
[wsl2]
guiApplications=false
```

Without that, a window Mono opens appears on the Windows desktop. Everything here is given Xvfb
instead.

**The `wsl.exe` that ships with Windows may install nothing.** The one here, 10.0.26100.8737, answered
every command, `--install` included, with "not installed". The WSL package itself is the MSI on
[its releases](https://github.com/microsoft/WSL/releases), checked against the digest GitHub lists for
it and against its Microsoft signature, and installed with `msiexec /i … /quiet /norestart`.

```powershell
wsl --install -d Ubuntu-24.04 --no-launch
wsl -d Ubuntu-24.04 -u root -- bash -c 'bash /mnt/c/dev/wt/spike-mono/wsl/setup.sh > /mnt/c/dev/wt/spike-mono/results/setup.txt 2>&1'
wsl --terminate Ubuntu-24.04

$w = '/mnt/c/dev/wt/spike-mono'
wsl -d Ubuntu-24.04 -- bash "$w/wsl/run-probes.sh" "$w/probes/MonoProbe/bin/Release/net48" "$w/results/mono.txt"
.\windows\Compare-Runs.ps1 -Left results\net48.txt -Right results\mono.txt -LeftName '.NET Framework 4.8' -RightName Mono
```

The product, with `$t` the plugin's checkout at the commit being cited, built with `build.ps1 -Task
Build`:

```powershell
$t = '/mnt/c/dev/wt/review4'
$r = "$w/product/NUnitOnMono/bin/Release/net48"
$s = "$w/product/AutomationStubs/bin"
wsl -d Ubuntu-24.04 -- bash "$w/wsl/run-tests.sh" "$t/tests/KeePassGate.Tests/bin/Release/net48" KeePassGate.Tests.dll $r "$w/results/unit.tsv"
wsl -d Ubuntu-24.04 -- env STUBS=$s bash "$w/wsl/run-tests.sh" "$t/tests/KeePassGate.Host.Tests/bin/Release/net48" KeePassGate.Host.Tests.dll $r "$w/results/host.tsv"
wsl -d Ubuntu-24.04 -- bash "$w/wsl/run-vectors.sh" $t $r $s "$w/results/vectors.tsv"
uv run --no-project python -X utf8 -I tools/classify-failures.py results/unit.tsv results/host.tsv results/vectors.tsv
```

KeePass is started from a session that stays open until it exits, because KeePass left running in
the background of a session that ended did not outlive it, though Xvfb did. `--exec` passes the command to `bash` untouched,
where `--` hands it to a shell first that would expand the `$(…)` before KeePass has a process:

```powershell
wsl -d Ubuntu-24.04 --exec bash -c "bash $w/wsl/keepass.sh start $w/.keepass/2.61.1 $t/src/KeePassGate/bin/Release/net48 $w/product/MakeDatabase/bin/Release/net48 && tail --pid=`$(cat /tmp/kpg-mono/keepass.pid) -f /dev/null"
```

and driven from others while it runs:

```powershell
wsl -d Ubuntu-24.04 --exec bash "$w/wsl/keepass.sh" shot "$w/results/keepass-open.png"
```

`keepass.sh` with no arguments lists its other steps. Xvfb runs no window manager, so a key reaches
whatever is under the pointer: move it over the window first, with `xdotool mousemove`.

## What is not measured

macOS, where KeePass also runs on Mono. A Mono other than the distribution's. A `.plgx`, which KeePass
compiles on Mono with Mono's own compiler. Anything the mainline's end to end tier measures, which
drives KeePass through Windows UI Automation and has no counterpart here.
