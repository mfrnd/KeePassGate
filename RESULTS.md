# What each probe settled

<!-- The recorded probe output below smuggles a newline as `%0A`, which runs into the
     `URL=` after it. Quoted verbatim, so the word is ignored here rather than corrected. -->
<!-- cspell:ignore AURL -->

Nine questions the transport design depends on, each with the output that answered it. The output
is recorded as it came back, so a probe re-run that disagrees is a finding rather than a mistake.

[`README.md`](README.md) says how to run one. The build environment is a different subject and is
on `spikes/build-environment`.

| Probe | Question | Answer |
| --- | --- | --- |
| `TlsProbe` | What does .NET Framework give a plugin that wants mutual TLS? | TLS 1.3 negotiates, mutual TLS works, the server sees the client certificate |
| `ChainProbe` | Does the natural chain validation pin the anchor? | **No.** It accepts a foreign CA |
| `SanProbe` | Can a claim be smuggled past a SAN parser? | **Yes**, three ways |
| `OptionalClientCertProbe` | Can one listener serve both certificate holders and strangers? | Yes, via the setting that appears to say otherwise |
| `SniProbe` | Can one listener serve several databases, each with its own CA? | Yes, by reading the ClientHello ourselves |
| `HmacSniProbe` | Can the SNI rotate, so it carries no stable identifier? | Yes, at the cost of hostname verification |
| `BindingProbe` | Can a server prove itself to a client that has no trust anchor? | Yes, RFC 5929 channel binding is available |
| `EphemeralKeyProbe` | Can a key stay out of the user's profile and still be used? | **It depends what for.** It signs; SChannel will not do TLS with it in either direction |
| `ManagedTlsProbe` | Can a TLS 1.3 server run with a key that never reaches the disk? | **Yes**, with Bouncy Castle, and an OpenSSL client reaches it |

## `ManagedTlsProbe.cs`

The answer to what `EphemeralKeyProbe` found. Bouncy Castle parses the PKCS#12 itself and hands
Windows nothing, so the host key is an object on the heap.

```text
A. managed server, .NET client with a certificate
     handshake ok, TLS 1.3, client certificate CN=probe client; client HTTP/1.1 200 OK over Tls13; key files 115 -> 115 -> 115
B. managed server, .NET client with no certificate
     handshake ok, TLS 1.3, client certificate none; client HTTP/1.1 200 OK over Tls13; key files 115 -> 115 -> 115
C. managed server, .NET client that will not speak TLS 1.3
     server FAILED TlsFatalAlert; client FAILED AuthenticationException
```

Case B is the one that also removes a trap: whether a client certificate is asked for is decided in
our own code now, so serving a stranger on the pairing path no longer depends on
`ClientCertificateRequired`, whose plain reading is the opposite of what it does.

It needs Bouncy Castle, and a resolver, because a type compiled by `Add-Type` looks for its
references beside PowerShell rather than beside the probe:

```powershell
. .\tools\BouncyCastle.ps1
$global:BcPath = Install-BouncyCastle
[Reflection.Assembly]::LoadFrom($global:BcPath) | Out-Null
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $e)
    if ($e.Name -like 'BouncyCastle*') { [Reflection.Assembly]::LoadFrom($global:BcPath) } else { $null }
})
Add-Type -TypeDefinition (Get-Content -Raw spikes\ManagedTlsProbe.cs) `
    -ReferencedAssemblies 'System','System.Core','System.Security',$global:BcPath
[ManagedTlsProbe]::Run()
```

### Reaching it from something that is not .NET

`[ManagedTlsProbe]::Serve(52998, $pwd)` writes `ca.pem`, `client.pem` and `client.key` into the
working directory and answers one connection. Anything can then be pointed at it, which is how the interop was
measured rather than assumed:

```python
import socket, ssl
context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
context.minimum_version = ssl.TLSVersion.TLSv1_3
context.load_cert_chain("client.pem", "client.key")
context.load_verify_locations("ca.pem")
context.check_hostname = False          # the name rotates and can never be in a SAN
with socket.create_connection(("127.0.0.1", 52998)) as raw:
    with context.wrap_socket(raw, server_hostname="abc.def.kpg") as tls:
        print(tls.version(), tls.cipher()[0])
        tls.sendall(b"GET /gate/status HTTP/1.1

")
        print(tls.recv(200).decode().splitlines()[0])
```

```text
client: openssl OpenSSL 3.0.11
client: negotiated TLSv1.3 TLS_AES_256_GCM_SHA384
client: got HTTP/1.1 200 OK
server: handshake ok, TLS 1.3, client certificate CN=probe client
server: key files before 114, after 114
```

## `EphemeralKeyProbe.cs`

**The probe that was wrong, and is kept because of it.** The design says the authority lives inside
the database and nowhere else, which is a claim about the disk: importing a PKCS#12 on .NET
Framework writes a key file into `%APPDATA%\Microsoft\Crypto\Keys` unless `EphemeralKeySet` is
asked for.

```text
A. ephemeral only, nothing imported before it
     server: Win32Exception: no credentials are available in the security package
     client: handshake failed (IOException)
     signs:  yes
B. persisted only
     server: ok, Tls13
     client: client certificate seen
C. persisted first, then ephemeral, same PKCS#12
     persisted -> ok, Tls13, then ephemeral -> ok, Tls13  (the second borrows what the first left behind)
D. what each import costs on disk
     before 114, ephemeral 114, persisted 115, after dispose 114
```

**An ephemeral key signs and cannot do TLS**, as a server or as a client. The first version of this
probe ran case C only, in that order, and concluded the opposite: the persisted import had left a
key that Windows then found for the ephemeral certificate, so the probe was measuring its own side
effect. Case A is first now, and stands alone, for that reason.

**This is what took the transport off SChannel.** Rather than write a host key to the disk for as
long as a database is open, the plugin terminates TLS in managed code, where an ephemeral key is
all that is ever needed. `ManagedTlsProbe` is the measurement that made that a decision rather than
a hope, and `FINDINGS.md` records the reversal this probe's first version caused.

## `TlsProbe.cs`

Takes a protocol and whether to require a client certificate:

```powershell
Add-Type -TypeDefinition (Get-Content -Raw spikes\TlsProbe.cs) -ReferencedAssemblies 'System','System.Core'
foreach ($p in 'Tls12','Tls13','None') {
    $proto = [System.Security.Authentication.SslProtocols]::$p
    '{0,-6} {1}' -f $p, [TlsProbe]::Run($proto, $true)
}
```

On .NET Framework 4.8 and Windows 11, TLS 1.3 negotiates and the server sees the client
certificate. `SslProtocols.None`, meaning "let the platform choose", also reached TLS 1.3.

## `ChainProbe.cs`

**The finding that would have shipped.** Builds two independent authorities and a client
certificate under each, then validates both against the first.

```text
naive  + our client     : True    (must be True)
naive  + FOREIGN client : True    (must be False)   <-- the finding
pinned + our client     : True    (must be True)
pinned + FOREIGN client : False   (must be False)
```

`ExtraStore` supplies candidate certificates without constraining the anchor, and
`AllowUnknownCertificateAuthority` tolerates an untrusted root without saying which one, so the
build succeeds against the machine's own root stores. `X509ChainTrustMode.CustomRootTrust` would
fix it and is .NET 5 and later. The pin has to be a byte comparison of the chain's last element.

It also prints the length of `X509Certificate2.Thumbprint`: 40 hex characters, so **SHA-1**, which
is why admission keys on an explicit SHA-256 of `RawData` instead.

## `SanProbe.cs`

Builds a subject alternative name holding one honest URI claim and three hostile entries, then
reads it back the two easy ways.

```text
URL=kpg://any/readonly                    <- the only real claim
DNS-Name=URL=kpg://any/admin              <- a dNSName whose TEXT is a URI claim
RFC822-Name=URL=kpg://any/super           <- an rfc822Name doing the same
URL=kpg://any/ops%0AURL=kpg://any/admin   <- a newline smuggled in as a percent escape

GetNameInfo(UrlName, false): [kpg://any/ops%0AURL=kpg://any/admin]
```

`AddDnsName` and `AddEmailAddress` accept text that renders as a URI claim, the labels are
localized (the recorded run is `de-DE`), and `GetNameInfo` returns one entry, here the attacker's
rather than the honest one. Read the ASN.1 tags instead: `0x86` URI, `0x82` dNSName, `0x81`
rfc822Name.

## `OptionalClientCertProbe.cs`

Pairing needs strangers served on the same listener as certificate holders. The result inverts the
obvious reading of the setting:

```text
Tls13:
  required=True  clientOffers=True  -> server sees CN=agent-fictitious
  required=True  clientOffers=False -> server sees (none)          <- a stranger still connects
  required=False clientOffers=True  -> server sees (none)          <- THE TRAP
Tls12:
  required=False clientOffers=True  -> server sees CN=agent-fictitious
```

**`ClientCertificateRequired = false` is what breaks it.** Under TLS 1.3 the server then never
sends a `CertificateRequest`, so a client holding a good certificate never offers one. Under TLS
1.2 it does, so anyone testing on 1.2 and shipping on 1.3 gets a silent authentication bypass.

And `= true` requires nothing: a certificate-less client completes the handshake. Enforcement is
entirely in the validation callback and the route table.

## `SniProbe.cs`

Several databases, each with its own CA and server certificate, behind one listener. TLS forces the
question: the server sends its certificate before it sees the client's, so only SNI is available in
time. .NET Framework has no server-side SNI hook, so this reads the ClientHello off the socket,
extracts `server_name`, picks the certificate, and replays the buffered bytes through a prepending
stream.

```text
SslServerAuthenticationOptions present: False

SNI=Alpha, client from Alpha -> picked Alpha (correct); server ACCEPTS (correct)
SNI=Alpha, client from BETA  -> picked Alpha (correct); server refuses (correct)
```

The quiet result matters most: the client connected to `127.0.0.1` by address but passed a
CA-derived name as the target host, and `SslPolicyErrors` came back without
`RemoteCertificateNameMismatch`. A name derived from the CA satisfies hostname verification while
being independent of where the database is served.

## `HmacSniProbe.cs`

Rotates the SNI so it carries no stable identifier: `<nonce>.<HMAC(key, nonce)>.kpg`, two labels
because 64 hex characters exceed the 63-octet DNS label limit.

```text
afc5cfab62da686514c1d5dc2a7ad61d.e1664bf5c3c46d8970edccf56723d988.kpg
85f74d4d84d9157175ba5f3a4c3d7bb0.909c3fcd2f67dbdcaacb13784e5c1571.kpg   <- same database

valid SNI Alpha, client Alpha -> picked Alpha; ACCEPTS; client saw [NameMismatch, ChainErrors]
FORGED SNI (bad tag)          -> did not resolve, connection dropped pre-TLS
```

Two consequences. A forged name is dropped **before any TLS work**, which is the real prize: an
attacker who cannot compute a tag never reaches SChannel, certificate selection, or the pairing
dialog. And the name can no longer appear in the SAN, so hostname verification must be abandoned
and replaced by the anchor pin plus an explicit EKU check, which this probe performs.

## `BindingProbe.cs`

An unpaired client has no trust anchor, so it accepts whatever certificate arrives. The server has
to prove it holds the shared token, bound to the certificate it just presented.

```text
RFC 5705 exporter (SslStream.ExportKeyingMaterial): False
TransportContext: present

Tls13 -> Endpoint server=85 bytes, client=85 bytes, agree: True
```

No keying-material exporter on .NET Framework, but `ChannelBindingKind.Endpoint` works on both
sides under TLS 1.3 and both derive the same value. The 85 bytes are a 32-byte header, then
`tls-server-end-point:`, then SHA-256 of the server certificate, which is RFC 5929. Production code
must take the application-data portion using the documented offsets rather than the whole blob,
since the header carries address fields that are only zero today.

`ChannelBindingKind.Unique` also returns a value under TLS 1.3, where tls-unique is no longer
defined. Ignore it.
