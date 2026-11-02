using System;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using X509Certificate = Org.BouncyCastle.X509.X509Certificate;

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
    ///
    /// **One instance is shared by every thread that reaches its database**: the host's by the TLS
    /// server of every connection at once, the authority's by every pairing, every renewal and
    /// every responder minted. That is safe because nothing here is written after the constructor
    /// returns, and because what it hands out guards its own lazily computed values: measured on
    /// Bouncy Castle 2.7.0, every such value on a certificate, a key and a curve is set under a
    /// lock or an interlocked exchange, or was already set when the certificate was parsed. A field
    /// filled in later, a cached Windows reading or a cached TLS certificate, would race between
    /// connections, and `CertifiedKeyTests` issues and serves from one instance on several threads
    /// at once to hold it to that.
    /// </remarks>
    public sealed class CertifiedKey
    {
        /// <summary>
        /// The name the one entry is stored under, which Windows shows as the certificate's friendly
        /// name if somebody imports the file.
        /// </summary>
        private const string Alias = "KeePassGate";

        /// <summary>
        /// The largest stored PKCS#12 this reads, in bytes. This plugin writes about 1.1 KB for an
        /// authority or a host, and a client's identity at its most, thirty-two profile claims,
        /// is a few kilobytes, so this is sixteen times the largest and no time at all to parse.
        /// </summary>
        internal const int MostBytes = 64 * 1024;

        /// <summary>
        /// The highest iteration count any key derivation in a stored PKCS#12 may ask for. This
        /// plugin writes 1024 and Windows wrote 2000, and a derivation at this count takes a few
        /// milliseconds, where Bouncy Castle's own ceiling of five million took three seconds a
        /// bag.
        /// </summary>
        internal const int MostIterations = 10000;

        /// <summary>
        /// How many parts a stored PKCS#12 may hold. This plugin and Windows both write two, one
        /// for the key and one for the certificate, and twice that keeps what a reading can cost to
        /// a handful of derivations at <see cref="MostIterations"/>.
        /// </summary>
        internal const int MostParts = 4;

        /// <summary>
        /// How many bags a stored PKCS#12 may hold across its parts, for the reason
        /// <see cref="MostParts"/> gives: two are written, one in each part.
        /// </summary>
        internal const int MostBags = 4;

        private readonly X509Certificate _certificate;
        private readonly byte[] _certificateDer;
        private readonly AsymmetricKeyParameter _privateKey;

        /// <summary>A certificate and its key, which have to belong together.</summary>
        public CertifiedKey(X509Certificate certificate, AsymmetricKeyParameter privateKey)
            : this(Paired(certificate, privateKey), privateKey, certificate.GetEncoded())
        {
        }

        /// <summary>A pair known to belong together, which <see cref="From"/> makes.</summary>
        private CertifiedKey(X509Certificate certificate, AsymmetricKeyParameter privateKey, byte[] certificateDer)
        {
            _certificate = certificate;
            _certificateDer = certificateDer;
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
        /// Reads a PKCS#12 of the shape this plugin stores: one key bag, no password, a certificate
        /// on P-256 and a key that is the one it certifies. Returns null for anything else, because
        /// a certificate that cannot be read is a database that cannot be served, which is reported
        /// rather than thrown.
        /// </summary>
        /// <remarks>
        /// **This is the only reader of what a database holds**, and a database somebody crafted
        /// can hold any bytes at all, so the shape is asked about before Bouncy Castle is asked to
        /// do anything the file names. Bouncy Castle derives a key for the MAC, for every encrypted
        /// part and for every key bag at whatever iteration count the file gives, up to five
        /// million, which is three seconds a bag on KeePass's own thread at every open. So the
        /// structure is read first, without deriving anything at a count it has not bounded:
        /// the size, the MAC's count and every part's and every bag's, the number of parts and of
        /// bags, and one key bag at most. An encrypted part is opened only after its own count has
        /// been bounded, under the encoding of the empty password the MAC was computed with, which
        /// is what lets a bag inside one be bounded as well: Bouncy Castle reads key bags wherever
        /// they are.
        ///
        /// **The key is checked against the certificate**, which nothing did while Windows read
        /// these: a PKCS#12 pairs the two by a local identifier anybody can write, so a database
        /// somebody crafted could hold an authority beside a key that is not its own, and that
        /// authority would have issued every client a certificate that chains to nothing. Both are
        /// held to P-256, named, with the point uncompressed, before the certificate's key is
        /// decoded, because that is the only curve this plugin makes keys on and Bouncy Castle
        /// reads every other one it knows, a curve of 112 bits among them.
        /// </remarks>
        public static CertifiedKey From(byte[] pkcs12)
        {
            if (pkcs12 == null || pkcs12.Length == 0 || pkcs12.Length > MostBytes)
            {
                return null;
            }

            try
            {
                if (!IsShapedAsStored(pkcs12))
                {
                    return null;
                }

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

                    // The only one: the shape allows a single key bag, and one bag is one entry.
                    // A key of another kind is no elliptic curve key, and `GetCertificateChain` is
                    // null for a key stored without its certificate; an empty chain is what Bouncy
                    // Castle promises never to answer, and is refused rather than indexed.
                    var key = store.GetKey(alias).Key as ECPrivateKeyParameters;
                    var chain = store.GetCertificateChain(alias);
                    if (key == null || chain == null || chain.Length == 0)
                    {
                        return null;
                    }

                    var certificate = chain[0].Certificate;
                    return Certifies(certificate, key)
                        ? new CertifiedKey(certificate, key, certificate.GetEncoded())
                        : null;
                }

                return null;
            }
            catch (Exception error) when (error is IOException
                                          || error is ArgumentException
                                          || error is InvalidOperationException
                                          || error is CryptoException
                                          || error is SecurityUtilityException
                                          || error is GeneralSecurityException
                                          || error.GetType() == typeof(Exception))
            {
                // A PKCS#12 that will not parse is a database that cannot be served, and the tab
                // is where an operator is told about it.
                //
                // **Bouncy Castle's own types, and every one of them was measured**, by feeding
                // this every crafted shape the reviews found and some thousands of mutations of a
                // stored blob, and each is pinned by a test in `CertifiedKeyTests`. `IOException`
                // is not a mistake: Bouncy Castle reports malformed bytes as an IO failure with the
                // ASN.1 complaint inside it. `ArgumentException` is a structure of the wrong kind,
                // an algorithm Bouncy Castle has no cipher or MAC for, and a point that is not on
                // its curve. `InvalidOperationException` is a tag Bouncy Castle did not expect.
                // `CryptoException` is a key bag whose encryption will not undo, and
                // `SecurityUtilityException` a key of an algorithm Bouncy Castle does not know.
                // `GeneralSecurityException` is a certificate whose extensions will not parse.
                //
                // **And `Exception` itself, by its exact type**, which is not a catch-all: it is
                // what Bouncy Castle throws for a certificate bag holding anything but an X.509
                // certificate, and naming the type exactly lets every type derived from it travel
                // as it should.
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
        /// pairing hands it unless that provider is loaded. **DER because Bouncy Castle's own is
        /// BER**, with lengths left open, which the client's `cryptography` reads only through a
        /// fallback it warns is deprecated and promises to turn into an error, on every pairing and
        /// every renewal. The iteration count is Bouncy Castle's, 1024 where Windows wrote 2000,
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

        /// <summary>The certificate, once it is known to be certified by the key.</summary>
        private static X509Certificate Paired(X509Certificate certificate, AsymmetricKeyParameter privateKey)
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

            return certificate;
        }

        /// <summary>
        /// Whether the key is the private half of the certificate's public key, both on P-256.
        /// That is the only curve this plugin makes keys on, and anything else read from a database
        /// is something this plugin did not write.
        /// </summary>
        /// <remarks>
        /// The certificate's key is asked about as it is written before Bouncy Castle decodes it,
        /// so that the decoder only ever decodes one uncompressed point on P-256, and what it
        /// cannot decode is a refusal here rather than an exception from inside it: an empty point
        /// was an `IndexOutOfRangeException`, a curve left to the issuer a
        /// `NotImplementedException`, and a binary curve whose polynomial is cut short an
        /// `IndexOutOfRangeException` again, none of which any reader names.
        /// </remarks>
        private static bool Certifies(X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            var key = privateKey as ECPrivateKeyParameters;
            if (key == null || !X9ObjectIdentifiers.Prime256v1.Equals(key.PublicKeyParamSet)
                || !IsUncompressedP256(certificate.SubjectPublicKeyInfo))
            {
                return false;
            }

            var claimed = certificate.GetPublicKey() as ECPublicKeyParameters;
            return claimed != null && key.Parameters.G.Multiply(key.D).Normalize().Equals(claimed.Q.Normalize());
        }

        /// <summary>
        /// An elliptic curve key on the named curve P-256, as one uncompressed point: the type
        /// byte and two coordinates of thirty-two bytes, which is how this plugin and Windows both
        /// wrote every key.
        /// </summary>
        private static bool IsUncompressedP256(SubjectPublicKeyInfo info)
        {
            if (info == null || !X9ObjectIdentifiers.IdECPublicKey.Equals(info.Algorithm.Algorithm)
                || !X9ObjectIdentifiers.Prime256v1.Equals(info.Algorithm.Parameters)
                || info.PublicKey.PadBits != 0)
            {
                return false;
            }

            var point = info.PublicKey.GetBytes();
            return point.Length == 65 && point[0] == 0x04;
        }

        /// <summary>
        /// Whether a stored PKCS#12 is built the way this plugin and Windows build one, asked of
        /// its structure before anything in it is derived at a count nobody has bounded.
        /// </summary>
        /// <remarks>
        /// Throws what the ASN.1 it reads throws, and <see cref="From"/> answers that as it answers
        /// Bouncy Castle's own reading.
        /// </remarks>
        private static bool IsShapedAsStored(byte[] pkcs12)
        {
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(pkcs12));
            var safe = pfx.AuthSafe;
            var content = PkcsObjectIdentifiers.Data.Equals(safe.ContentType)
                ? Asn1OctetString.GetInstance(safe.Content)
                : null;
            if (content == null)
            {
                return false;
            }

            var authenticated = content.GetOctets();

            bool wrongZero;
            if (!IsAuthentic(pfx.MacData, authenticated, out wrongZero))
            {
                return false;
            }

            var parts = AuthenticatedSafe.GetInstance(Asn1Object.FromByteArray(authenticated)).GetContentInfo();
            if (parts.Length > MostParts)
            {
                return false;
            }

            var bags = 0;
            var keys = 0;
            foreach (var inside in parts.Select(part => Opened(part, wrongZero)))
            {
                if (inside == null)
                {
                    return false;
                }

                var contents = Asn1Sequence.GetInstance(Asn1Object.FromByteArray(inside));
                foreach (var bag in contents.Select(element => SafeBag.GetInstance(element)))
                {
                    if (++bags > MostBags || !HasAttributesOfTheirShape(bag))
                    {
                        return false;
                    }

                    if (PkcsObjectIdentifiers.KeyBag.Equals(bag.BagID))
                    {
                        keys++;
                    }
                    else if (PkcsObjectIdentifiers.Pkcs8ShroudedKeyBag.Equals(bag.BagID))
                    {
                        keys++;
                        var shrouded = EncryptedPrivateKeyInfo.GetInstance(bag.BagValue);
                        if (!IsBounded(shrouded.EncryptionAlgorithm))
                        {
                            return false;
                        }
                    }
                }
            }

            // One key bag and no more. Bouncy Castle keeps every key it reads, and two under one
            // name keep only the last, so a second key is refused here rather than counted after.
            return keys <= 1;
        }

        /// <summary>
        /// Whether every attribute a bag carries is an identifier and a set of values, which is the
        /// one shape RFC 7292 gives an attribute and the one this plugin and Windows both wrote.
        /// </summary>
        /// <remarks>
        /// **Asked because Bouncy Castle's store indexes an attribute without counting it.** One of
        /// a single element, or of none, threw `IndexOutOfRangeException` from inside `Load`, on a
        /// key bag and a certificate bag alike, and no reader of a stored authority names that
        /// type. Every other shape an attribute was measured in, a value of the wrong type, a set
        /// left empty, an attribute written twice and an identifier this plugin never writes among
        /// them, is answered by Bouncy Castle itself, as a refusal or as a reading.
        /// </remarks>
        private static bool HasAttributesOfTheirShape(SafeBag bag)
        {
            return bag.BagAttributes == null || bag.BagAttributes.All(IsAttribute);
        }

        private static bool IsAttribute(Asn1Encodable element)
        {
            var attribute = element as Asn1Sequence;
            return attribute != null && attribute.Count == 2
                   && attribute[0] is DerObjectIdentifier && attribute[1] is Asn1Set;
        }

        /// <summary>
        /// Whether the MAC, if there is one, is computed at a bounded count and over these bytes,
        /// and which of the two encodings of the empty password it was computed with.
        /// </summary>
        /// <remarks>
        /// Asked the way Bouncy Castle asks it, the encoding RFC 7292 gives first and the one some
        /// writers used second, because that answer decides the key every encrypted part is opened
        /// with. A MAC that matches neither is a file Bouncy Castle refuses too.
        /// </remarks>
        private static bool IsAuthentic(MacData mac, byte[] authenticated, out bool wrongZero)
        {
            wrongZero = false;
            if (mac == null)
            {
                return true;
            }

            if (!IsBounded(mac.IterationCount))
            {
                return false;
            }

            if (MacMatches(mac, authenticated, false))
            {
                return true;
            }

            wrongZero = true;
            return MacMatches(mac, authenticated, true);
        }

        private static bool MacMatches(MacData mac, byte[] authenticated, bool wrongZero)
        {
            var algorithm = mac.Mac.DigestAlgorithm.Algorithm;
            var engine = PbeUtilities.CreateEngine(algorithm) as IMac;
            if (engine == null)
            {
                return false;
            }

            engine.Init(PbeUtilities.GenerateCipherParameters(
                algorithm,
                Array.Empty<char>(),
                wrongZero,
                PbeUtilities.GenerateAlgorithmParameters(algorithm, mac.GetSalt(), mac.IterationCount.IntValue)));

            return Arrays.FixedTimeEquals(MacUtilities.DoFinal(engine, authenticated), mac.Mac.Digest.GetOctets());
        }

        /// <summary>
        /// The bags one part holds, opened if it is encrypted and its count is bounded, or null for
        /// a part that is neither of the two kinds this plugin writes.
        /// </summary>
        private static byte[] Opened(ContentInfo part, bool wrongZero)
        {
            if (PkcsObjectIdentifiers.Data.Equals(part.ContentType))
            {
                var plain = Asn1OctetString.GetInstance(part.Content);
                return plain == null ? null : plain.GetOctets();
            }

            if (!PkcsObjectIdentifiers.EncryptedData.Equals(part.ContentType))
            {
                return null;
            }

            var encrypted = EncryptedData.GetInstance(part.Content);
            if (encrypted == null || encrypted.Content == null || !IsBounded(encrypted.EncryptionAlgorithm))
            {
                return null;
            }

            var cipher = PbeUtilities.CreateEngine(encrypted.EncryptionAlgorithm) as IBufferedCipher;
            if (cipher == null)
            {
                return null;
            }

            cipher.Init(false, PbeUtilities.GenerateCipherParameters(
                encrypted.EncryptionAlgorithm, Array.Empty<char>(), wrongZero));
            return cipher.DoFinal(encrypted.Content.GetOctets());
        }

        /// <summary>
        /// Whether an encryption names its salt and a count no higher than
        /// <see cref="MostIterations"/>, which is the shape of every algorithm this plugin and
        /// Windows wrote. Any other shape is refused, since its count is then a cost nobody
        /// bounded.
        /// </summary>
        private static bool IsBounded(AlgorithmIdentifier algorithm)
        {
            var parameters = algorithm == null ? null : algorithm.Parameters as Asn1Sequence;
            if (parameters == null || parameters.Count != 2 || !(parameters[0] is Asn1OctetString))
            {
                return false;
            }

            var count = parameters[1] as DerInteger;
            return count != null && IsBounded(count.Value);
        }

        private static bool IsBounded(BigInteger count)
        {
            return count.SignValue > 0 && count.CompareTo(BigInteger.ValueOf(MostIterations)) <= 0;
        }
    }
}
