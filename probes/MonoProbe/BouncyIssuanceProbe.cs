using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MonoProbe
{
    /// <summary>
    /// Whether the runtime can at least read what it may not be able to make: a P-256 certificate
    /// and its PKCS#12 made by Bouncy Castle, handed to `X509Certificate2` the way the plugin hands
    /// it what it stored. If `issuance` fails and this passes, issuing through Bouncy Castle is a
    /// way out; if this fails too, the runtime's certificate type is out of the plugin's reach.
    /// </summary>
    internal static class BouncyIssuanceProbe
    {
        internal static void Run()
        {
            var authority = BouncyCertificates.Authority("CN=Mono probe authority", DateTime.UtcNow.AddYears(100));
            var host = BouncyCertificates.Leaf(authority, "CN=Mono probe host", true, DateTime.UtcNow.AddYears(100));
            Report.Line("bouncy.made", "authority " + authority.Der.Length + " bytes, host " + host.Der.Length + " bytes");

            var read = Report.Measure("bouncy.x509-from-der", () => new X509Certificate2(host.Der),
                made => "raw " + made.RawData.Length + ", key algorithm " + made.PublicKey.Oid.Value);
            if (read != null)
            {
                Report.Measure("bouncy.subject", () => read.Subject);
                Report.Measure("bouncy.public-key-object", () => read.PublicKey.Key,
                    key => key.GetType().FullName);
                Report.Measure("bouncy.get-ecdsa-public-key", () => read.GetECDsaPublicKey(),
                    key => key.GetType().FullName);
            }

            var pfx = BouncyCertificates.Pkcs12(host);
            Report.Line("bouncy.pkcs12-made", pfx.Length + " bytes");
            var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;
            var restored = Report.Measure("bouncy.load-pkcs12-ephemeral",
                () => new X509Certificate2(pfx, (string)null, flags), made => "key " + made.HasPrivateKey);
            Report.Measure("bouncy.load-pkcs12-exportable-only",
                () => new X509Certificate2(pfx, (string)null, X509KeyStorageFlags.Exportable),
                made => "key " + made.HasPrivateKey);
            if (restored != null)
            {
                Report.Measure("bouncy.restored-sign-verify", () =>
                {
                    var data = Encoding.UTF8.GetBytes("probe");
                    using (var signer = restored.GetECDsaPrivateKey())
                    {
                        var signature = signer.SignData(data, HashAlgorithmName.SHA256);
                        var check = SignerUtilities.GetSigner("SHA256WITHPLAIN-ECDSA");
                        check.Init(false, (ECPublicKeyParameters)host.Key.Public);
                        check.BlockUpdate(data, 0, data.Length);
                        return check.VerifySignature(signature);
                    }
                });
            }

            // What the TLS server does with the stored PKCS#12: Bouncy Castle reads it back itself.
            Report.Measure("bouncy.pkcs12-read-by-bouncy", () =>
            {
                var store = new Org.BouncyCastle.Pkcs.Pkcs12StoreBuilder().Build();
                store.Load(new System.IO.MemoryStream(pfx), Array.Empty<char>());
                foreach (var alias in store.Aliases)
                {
                    if (store.IsKeyEntry(alias))
                    {
                        AsymmetricKeyParameter key = store.GetKey(alias).Key;
                        return key.GetType().Name;
                    }
                }

                return "no key entry";
            });
        }
    }
}
