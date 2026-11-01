using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MonoProbe
{
    /// <summary>
    /// The plugin's issuance, call for call: a P-256 key from `ECDsa.Create`, a `CertificateRequest`
    /// with the authority's extensions, a host issued under it, and the trip through PKCS#12 that
    /// `CertificateBlob` makes, with the flags it loads with. Then the two facts `CLAUDE.md` records
    /// about .NET Framework that a different runtime could make false: the byte constructor taking a
    /// PKCS#12, and `NotAfter` for a certificate that never expires.
    /// </summary>
    internal static class IssuanceProbe
    {
        private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

        internal static void Run()
        {
            var now = DateTimeOffset.UtcNow;
            var key = Report.Measure("issuance.ecdsa-create-p256", () => ECDsa.Create(ECCurve.NamedCurves.nistP256),
                created => created.GetType().FullName);
            Report.Measure("issuance.ecdsa-create-default", () => ECDsa.Create(), created => created.GetType().FullName);
            if (key == null)
            {
                return;
            }

            var request = Report.Measure("issuance.certificate-request", () =>
            {
                var made = new CertificateRequest(
                    new X500DistinguishedName("CN=Mono probe authority"), key, HashAlgorithmName.SHA256);
                made.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                made.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                made.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(made.PublicKey, false));
                return made;
            }, made => "made");
            if (request == null)
            {
                return;
            }

            var lifetimeEnd = now.AddYears(100);
            var authority = Report.Measure("issuance.authority-self-signed",
                () => request.CreateSelfSigned(now.AddMinutes(-5), lifetimeEnd),
                made => "raw " + made.RawData.Length + " bytes, key " + made.HasPrivateKey
                    + ", signature " + made.SignatureAlgorithm.Value);
            if (authority == null)
            {
                return;
            }

            Report.Measure("issuance.authority-not-after-utc", () => authority.NotAfter.ToUniversalTime());
            Report.Measure("issuance.authority-not-after-exact", () =>
                Math.Abs((authority.NotAfter.ToUniversalTime() - lifetimeEnd.UtcDateTime).TotalSeconds) < 1);

            Report.Measure("issuance.never-expiring-not-after", () =>
            {
                using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    var forever = new CertificateRequest(
                        new X500DistinguishedName("CN=Mono probe forever"), other, HashAlgorithmName.SHA256);
                    var made = forever.CreateSelfSigned(
                        now.AddMinutes(-5), new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero));
                    return made.NotAfter.ToString("o") + " / as UTC " + made.NotAfter.ToUniversalTime().ToString("o");
                }
            });

            var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var host = Report.Measure("issuance.host-issued", () =>
            {
                var hostRequest = new CertificateRequest(
                    new X500DistinguishedName("CN=Mono probe host"), hostKey, HashAlgorithmName.SHA256);
                hostRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                hostRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));
                hostRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ServerAuthOid) }, false));
                var names = new SubjectAlternativeNameBuilder();
                names.AddUri(new Uri("kpg://host/responder"));
                hostRequest.CertificateExtensions.Add(names.Build());
                var serial = new byte[16];
                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(serial);
                }

                serial[0] &= 0x7f;
                return hostRequest.Create(authority, now.AddMinutes(-5), lifetimeEnd, serial);
            }, made => "raw " + made.RawData.Length + " bytes, key " + made.HasPrivateKey);
            if (host == null)
            {
                return;
            }

            var withKey = Report.Measure("issuance.copy-with-private-key", () => host.CopyWithPrivateKey(hostKey),
                made => "key " + made.HasPrivateKey);
            if (withKey == null)
            {
                return;
            }

            var pfx = Report.Measure("issuance.export-pkcs12", () => withKey.Export(X509ContentType.Pkcs12),
                bytes => bytes.Length + " bytes");
            if (pfx == null)
            {
                return;
            }

            // `CertificateBlob.SigningFlags`.
            var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;
            var restored = Report.Measure("issuance.load-pkcs12-ephemeral",
                () => new X509Certificate2(pfx, (string)null, flags),
                made => "key " + made.HasPrivateKey + ", same bytes " + Same(made.RawData, host.RawData));
            Report.Measure("issuance.load-pkcs12-exportable-only",
                () => new X509Certificate2(pfx, (string)null, X509KeyStorageFlags.Exportable),
                made => "key " + made.HasPrivateKey + ", same bytes " + Same(made.RawData, host.RawData));

            if (restored != null)
            {
                Report.Measure("issuance.restored-sign-verify", () =>
                {
                    var data = Encoding.UTF8.GetBytes("probe");
                    using (var signer = restored.GetECDsaPrivateKey())
                    using (var verifier = host.GetECDsaPublicKey())
                    {
                        var signature = signer.SignData(data, HashAlgorithmName.SHA256);
                        return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
                    }
                });
            }

            // The byte constructor sniffs a PKCS#12 on .NET Framework, which is why admission takes
            // the digest of `RawData` after parsing rather than of the bytes it stored.
            Report.Measure("issuance.byte-constructor-takes-pkcs12", () => new X509Certificate2(pfx),
                made => "key " + made.HasPrivateKey + ", raw " + made.RawData.Length + " of " + pfx.Length);

            Report.Measure("issuance.sha256-of-raw", () =>
            {
                using (var sha = SHA256.Create())
                {
                    return Report.Hex(sha.ComputeHash(host.RawData), 8) + "...";
                }
            });
            Report.Measure("issuance.thumbprint-length", () => host.Thumbprint.Length);
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
