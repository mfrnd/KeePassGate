| measurement | .NET Framework 4.8 | Mono | |
| --- | --- | --- | --- |
| runtime.mono | `false` | `true` | differs |
| runtime.clr-version | `4.0.30319.42000` | `4.0.30319.42000` |  |
| runtime.os | `Microsoft Windows NT 6.2.9200.0` | `Unix 6.18.54.1` | differs |
| runtime.platform | `Win32NT` | `Unix` | differs |
| runtime.pointer-size | `8` | `8` |  |
| runtime.culture | `de-DE` | `` | differs |
| runtime.ui-culture | `en-US` | `` | differs |
| runtime.directory-separator | `\` | `/` | differs |
| runtime.local-application-data | `C:\Users\kpg\AppData\Local` | `/home/probe/.local/share` | differs |
| issuance.ecdsa-create-p256 | `System.Security.Cryptography.ECDsaCng` | `` | differs |
| issuance.ecdsa-create-default | `System.Security.Cryptography.ECDsaCng` | `` | differs |
| issuance.certificate-request | `made` | `` | differs |
| issuance.authority-self-signed | `raw 384 bytes, key True, signature 1.2.840.10045.4.3.2` | `` | differs |
| issuance.authority-not-after-utc | `2126-11-01T13:12:46.0000000 kind Utc` | `` | differs |
| issuance.authority-not-after-exact | `true` | `` | differs |
| issuance.never-expiring-not-after | `9999-12-31T23:59:59.9999999+01:00 / as UTC 9999-12-31T22:59:59.9999999Z` | `` | differs |
| issuance.host-issued | `raw 403 bytes, key False` | `` | differs |
| issuance.copy-with-private-key | `key True` | `` | differs |
| issuance.export-pkcs12 | `1019 bytes` | `` | differs |
| issuance.load-pkcs12-ephemeral | `key True, same bytes True` | `` | differs |
| issuance.load-pkcs12-exportable-only | `key True, same bytes True` | `` | differs |
| issuance.restored-sign-verify | `true` | `` | differs |
| issuance.byte-constructor-takes-pkcs12 | `key True, raw 403 of 1019` | `` | differs |
| issuance.sha256-of-raw | `b571616ec4091eb4...` | `` | differs |
| issuance.thumbprint-length | `40` | `` | differs |
| ecdsa.type-certificate-request | `present` | `absent` | differs |
| ecdsa.type-certificate-request-in-system | `absent` | `present` | differs |
| ecdsa.method-copy-with-private-key | `true` | `true` |  |
| ecdsa.create-p256 | `System.Security.Cryptography.ECDsaCng` | `! System.NotImplementedException: The method or operation is not implemented.` | differs |
| ecdsa.create-default | `System.Security.Cryptography.ECDsaCng` | `! System.NotImplementedException: The method or operation is not implemented.` | differs |
| ecdsa.key-size | `256` | `` | differs |
| ecdsa.export-parameters | `curve nistP256, x 32 bytes` | `` | differs |
| ecdsa.sign-verify | `signature 64 bytes, verifies True` | `` | differs |
| bouncy.made | `authority 360 bytes, host 370 bytes` | `authority 360 bytes, host 370 bytes` |  |
| bouncy.x509-from-der | `raw 370, key algorithm 1.2.840.10045.2.1` | `raw 370, key algorithm 1.2.840.10045.2.1` |  |
| bouncy.subject | `CN=Mono probe host` | `CN=Mono probe host` |  |
| bouncy.public-key-object | `! System.NotSupportedException: The certificate key algorithm is not supported.` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| bouncy.get-ecdsa-public-key | `System.Security.Cryptography.ECDsaCng` | `! System.NotImplementedException: The method or operation is not implemented.` | differs |
| bouncy.pkcs12-made | `985 bytes` | `985 bytes` |  |
| bouncy.load-pkcs12-ephemeral | `key True` | `key True` |  |
| bouncy.load-pkcs12-exportable-only | `key True` | `key True` |  |
| bouncy.restored-sign-verify | `true` | `! System.NotImplementedException: The method or operation is not implemented.` | differs |
| bouncy.pkcs12-read-by-bouncy | `ECPrivateKeyParameters` | `ECPrivateKeyParameters` |  |
| chain.own-leaf | `built true, elements 2, last is anchor true, status [UntrustedRoot]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.foreign-leaf | `built true, elements 1, last is anchor false, status [PartialChain]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.foreign-leaf-root-offered | `built true, elements 2, last is anchor false, status [UntrustedRoot]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.expired-leaf | `built false, elements 2, last is anchor true, status [UntrustedRoot,NotTimeValid]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.expired-leaf-ignoring-time | `built true, elements 2, last is anchor true, status [UntrustedRoot,NotTimeValid]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.ca-issuers-leaf | `built true, elements 1, last is anchor false, status [PartialChain]` | `! System.NotSupportedException: Cannot decode public key from unknown OID '1.2.840.10045.2.1'.` | differs |
| chain.ca-issuers-build-ms | `14` | `22` | differs |
| chain.ca-issuers-connections | `1` | `0` | differs |
| tls.client-handshake | `echo probe` | `echo probe` |  |
| tls.server | `version TLS 1.3, client certificate offered` | `version TLS 1.3, client certificate offered` |  |
| audit.append-data-stream | `ok` | `ok` |  |
| audit.length-after-append | `11` | `11` |  |
| audit.append-data-stream-twice | `ok` | `ok` |  |
| audit.length-after-second | `22` | `22` |  |
| audit.roll-with-reader-open | `moved, reader still reads {"line":1}` | `moved, reader still reads {"line":1}` |  |
| audit.get-access-control | `System.Security.AccessControl.FileSecurity` | `! System.PlatformNotSupportedException: Operation is not supported on this platform.` | differs |
| native.is-window-enabled | `false` | `! System.EntryPointNotFoundException: IsWindowEnabled assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| native.enable-window | `false` | `! System.EntryPointNotFoundException: EnableWindow assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| native.get-window | `0` | `! System.EntryPointNotFoundException: GetWindow assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| native.get-ancestor | `0` | `! System.EntryPointNotFoundException: GetAncestor assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| native.flash-window-ex | `false` | `! System.EntryPointNotFoundException: FlashWindowEx assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| native.set-window-display-affinity | `false` | `! System.EntryPointNotFoundException: SetWindowDisplayAffinity assembly:<unknown assembly> type:<unknown type> member:(null)` | differs |
| culture.en-US.lower-I | `U+0069` | `U+0069` |  |
| culture.en-US.upper-i | `U+0049` | `U+0049` |  |
| culture.en-US.path-equal | `true` | `true` |  |
| culture.en-US.compare-KPG-kpg | `1` | `1` |  |
| culture.en-US.compare-dot-dash | `-1` | `-1` |  |
| culture.en-US.compare-profiles-revoked | `-1` | `-1` |  |
| culture.en-US.compare-ignorable | `0` | `1` | differs |
| culture.tr-TR.lower-I | `U+0131` | `U+0131` |  |
| culture.tr-TR.upper-i | `U+0130` | `U+0130` |  |
| culture.tr-TR.path-equal | `false` | `false` |  |
| culture.tr-TR.compare-KPG-kpg | `1` | `1` |  |
| culture.tr-TR.compare-dot-dash | `-1` | `-1` |  |
| culture.tr-TR.compare-profiles-revoked | `-1` | `-1` |  |
| culture.tr-TR.compare-ignorable | `0` | `1` | differs |
| culture.de-DE.lower-I | `U+0069` | `U+0069` |  |
| culture.de-DE.upper-i | `U+0049` | `U+0049` |  |
| culture.de-DE.path-equal | `true` | `true` |  |
| culture.de-DE.compare-KPG-kpg | `1` | `1` |  |
| culture.de-DE.compare-dot-dash | `-1` | `-1` |  |
| culture.de-DE.compare-profiles-revoked | `-1` | `-1` |  |
| culture.de-DE.compare-ignorable | `0` | `1` | differs |
| culture.invariant-upper-dotless-i | `U+0131` | `U+0131` |  |
| culture.invariant-upper-long-s | `U+017F` | `U+017F` |  |
| culture.ordinal-ignore-case-TITLE-dotless | `false` | `false` |  |
| culture.ordinal-ignore-case-u-umlaut | `true` | `true` |  |
| forms.font-consolas | `Consolas` | `DejaVu Sans Mono` | differs |
| forms.font-microsoft-sans-serif | `Microsoft Sans Serif` | `DejaVu Sans` | differs |
| forms.font-generic-monospace | `Courier New` | `DejaVu Sans Mono` | differs |
| forms.default-font | `Microsoft Sans Serif 8.25` | `DejaVu Sans 8.25` | differs |
| forms.message-box-font | `Segoe UI 9` | `DejaVu Sans 11` | differs |
| forms.auto-scale | `current {Width=6, Height=13}, handle True` | `current {Width=7, Height=14}, handle True` | differs |
| forms.measure-text | `{Width=98, Height=13}` | `{Width=122, Height=14}` | differs |
| runtime.mono-version | `` | `6.8.0.105 (Debian 6.8.0.105+dfsg-3.6ubuntu2 Sun Mar 31 02:55:28 UTC 2024)` | differs |
| issuance.probe-defect | `` | `! System.TypeLoadException: Could not load type of field 'MonoProbe.IssuanceProbe+<>c__DisplayClass1_0:request' (1) due to: Could not resolve type with token 0100002e from typeref (expected class 'System.Security.Cryptography.X509Certificates.CertificateRequest' in assembly 'System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089') assembly:System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089 type:System.Security.Cryptography.X509Certificates.CertificateRequest member:(null)` | differs |
