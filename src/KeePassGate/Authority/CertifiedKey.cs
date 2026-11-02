using System;
using System.IO;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// A certificate and the private key it certifies, as Bouncy Castle holds them: what the issuer
    /// signs with and what it hands out, and what the TLS server presents, without Windows being
    /// involved at any point.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the transport terminates TLS in managed code. SChannel will not
    /// serve with a key that has not been written to the user's profile, which contradicts
    /// `CA.md` §202. Bouncy Castle parses the PKCS#12 itself, so the key is an object on the heap
    /// and nothing else. The issuer signs the same way, so no key of this plugin's is ever handed
    /// to Windows, and `X509Certificate2` is made from <see cref="CertificateDer"/> by whoever
    /// wants to read a certificate, never holding a key.
    ///
    /// Nothing here is disposable, because there is nothing to release: no handle, no container, no
    /// file. The bytes go when the object does.
    ///
    /// That is also why a lock lets this go rather than erasing it. An `AsymmetricKeyParameter` is
    /// an immutable `BigInteger` and offers nothing to overwrite, so the last reference going is
    /// the whole of what happens to it. `CA.md` §208 states the bound that leaves, and it is the
    /// same one conceded for local code execution as the user.
    /// </remarks>
    public sealed class CertifiedKey
    {
        /// <summary>
        /// The name the one entry is stored under, which Windows shows as the certificate's friendly
        /// name if somebody imports the file.
        /// </summary>
        private const string Alias = "KeePassGate";

        private readonly X509Certificate _certificate;
        private readonly byte[] _certificateDer;
        private readonly AsymmetricKeyParameter _privateKey;

        /// <summary>A certificate and its key, which have to belong together.</summary>
        public CertifiedKey(X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (privateKey == null)
            {
                throw new ArgumentNullException(nameof(privateKey));
            }

            if (!Certifies(certificate, privateKey))
            {
                throw new ArgumentException("The key is not the one the certificate certifies.", nameof(privateKey));
            }

            _certificate = certificate;
            _certificateDer = certificate.GetEncoded();
            _privateKey = privateKey;
        }

        public X509Certificate Certificate
        {
            get { return _certificate; }
        }

        /// <summary>The certificate as it goes on the wire, as a copy.</summary>
        public byte[] CertificateDer
        {
            get { return (byte[])_certificateDer.Clone(); }
        }

        public AsymmetricKeyParameter PrivateKey
        {
            get { return _privateKey; }
        }

        /// <summary>
        /// Reads a PKCS#12 of the shape this plugin stores: one key entry, no password, and a key
        /// that is the one its certificate certifies. Returns null for anything else, because a
        /// certificate that cannot be read is a database that cannot be served, which is reported
        /// rather than thrown.
        /// </summary>
        /// <remarks>
        /// **The key is checked against the certificate**, which nothing did while Windows read
        /// these: a PKCS#12 pairs the two by a local identifier anybody can write, so a database
        /// somebody crafted could hold an authority beside a key that is not its own, and that
        /// authority would have issued every client a certificate that chains to nothing.
        /// </remarks>
        public static CertifiedKey From(byte[] pkcs12)
        {
            if (pkcs12 == null || pkcs12.Length == 0)
            {
                return null;
            }

            try
            {
                var store = new Pkcs12StoreBuilder().Build();
                using (var bytes = new MemoryStream(pkcs12, false))
                {
                    store.Load(bytes, Array.Empty<char>());
                }

                foreach (string alias in store.Aliases)
                {
                    if (!store.IsKeyEntry(alias))
                    {
                        continue;
                    }

                    var key = store.GetKey(alias);
                    var chain = store.GetCertificateChain(alias);
                    if (key == null || key.Key == null || !key.Key.IsPrivate || chain == null || chain.Length == 0)
                    {
                        return null;
                    }

                    var certificate = chain[0].Certificate;
                    return Certifies(certificate, key.Key) ? new CertifiedKey(certificate, key.Key) : null;
                }

                return null;
            }
            catch (Exception error) when (Expected.OfCryptography(error)
                                          || error is IOException
                                          || error is InvalidOperationException
                                          || error is CryptoException
                                          || error is SecurityUtilityException)
            {
                // A PKCS#12 that will not parse is a database that cannot be served, and the tab
                // is where an operator is told about it.
                //
                // **Bouncy Castle's own types, and every one of them was measured.** `IOException`
                // is not a mistake: Bouncy Castle reports malformed key material as an IO failure
                // with the ASN.1 complaint inside it, so bytes that are not a PKCS#12 arrive as
                // "BOOLEAN value should have 1 byte in it" wrapped in an `IOException` rather than
                // as a `CryptographicException`. The other three escaped this filter until they
                // were measured, and two of them come from files Windows accepts, which makes them
                // reachable from a database somebody crafted: beside the certificate's own key, a
                // second key bag whose encryption will not undo is an `InvalidCipherTextException`,
                // a `CryptoException`, and one naming a negative iteration count is an
                // `InvalidOperationException`, as is an ASN.1 length that runs out. An algorithm
                // Bouncy Castle does not know is a `SecurityUtilityException`. Each of them broke
                // the promise this makes, that anything it cannot read comes back as null, and
                // traveled up to whoever had asked for the certificate instead.
                return null;
            }
        }

        /// <summary>
        /// The PKCS#12 this is stored as: one key entry, no password, the key and the certificate
        /// each under three-key triple DES, and DER throughout.
        /// </summary>
        /// <remarks>
        /// The shape Windows wrote while it exported these, measured rather than remembered: a
        /// SHA-1 MAC and both bags under `pbeWithSHAAnd3-KeyTripleDES-CBC`, so a database keeps one
        /// kind of PKCS#12 whichever release wrote its authority. **The certificate's algorithm is
        /// set because Bouncy Castle's own is RC2 with a forty-bit key**, which OpenSSL 3 keeps in
        /// its legacy provider only, so the client's `cryptography` would refuse the identity a
        /// pairing hands it. The iteration count is Bouncy Castle's, 1024 where Windows wrote 2000,
        /// and it protects nothing either way, since the password is empty: the database's own
        /// encryption is what protects these, `CA.md` §202.
        /// </remarks>
        public byte[] ToPkcs12()
        {
            var store = new Pkcs12StoreBuilder()
                .SetKeyAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .SetUseDerEncoding(true)
                .Build();

            store.SetKeyEntry(
                Alias, new AsymmetricKeyEntry(_privateKey), new[] { new X509CertificateEntry(_certificate) });

            using (var bytes = new MemoryStream())
            {
                store.Save(bytes, Array.Empty<char>(), new SecureRandom());
                return bytes.ToArray();
            }
        }

        /// <summary>
        /// Whether the key is the private half of the certificate's public key. Elliptic curve keys
        /// only, because those are the only keys this plugin makes, and anything else read from a
        /// database is something this plugin did not write.
        /// </summary>
        private static bool Certifies(X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            var key = privateKey as ECPrivateKeyParameters;
            var claimed = certificate.GetPublicKey() as ECPublicKeyParameters;
            if (key == null || claimed == null || !key.Parameters.Equals(claimed.Parameters))
            {
                return false;
            }

            return key.Parameters.G.Multiply(key.D).Normalize().Equals(claimed.Q.Normalize());
        }
    }
}
