using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Builds the certificates an authority is made of. Every extension is set here, and nothing
    /// is ever accepted from outside: the issuer generates the key pair as well, because a
    /// certificate request can carry extensions and an issuer that copies them lets the requester
    /// choose its own claims.
    /// </summary>
    /// <remarks>
    /// The shapes come from `CA.md` §207 and are asserted by the tests rather than trusted to
    /// this comment. ECDSA on P-256 with SHA-256 throughout.
    ///
    /// **Bouncy Castle makes every one, and signs with a key it read itself**, so no key passes
    /// through Windows: the authority signs from the PKCS#12 it is stored as, and what is issued is
    /// written as one by the same library. .NET Framework could do all of this on Windows, through
    /// CNG, and Mono can do none of it, `spikes/mono-compatibility`.
    ///
    /// **Names are written as UTF8String**, which RFC 5280 section 4.1.2.4 asks of every
    /// certificate issued since 2004. Windows wrote PrintableString, and BMPString for a name
    /// holding an underscore, which PrintableString cannot. A leaf's issuer is its authority's
    /// subject copied as it is stored, never written again, because RFC 5280 section 7.1 compares
    /// names after normalizing them and a chain engine that compares bytes would not, so an
    /// authority written before keeps every certificate it issues afterwards in its chain.
    ///
    /// Everything below is RFC 5280: basic constraints in section 4.2.1.9, key usage in 4.2.1.3,
    /// extended key usage in 4.2.1.12, and the two key identifiers in 4.2.1.1 and 4.2.1.2.
    /// https://www.rfc-editor.org/rfc/rfc5280#section-4.2.1
    /// The curve is NIST P-256, also called secp256r1 and prime256v1.
    /// </remarks>
    public sealed class AuthorityIssuer
    {
        public const string AuthoritySubject = "CN=" + AuthorityName;
        public const string HostSubject = "CN=" + HostName;
        public const string ResponderSubject = "CN=" + ResponderName;

        private const string AuthorityName = "KeePassGate authority";
        private const string HostName = "KeePassGate host";
        private const string ResponderName = "KeePassGate responder";

        /// <summary>The signature every certificate carries: ECDSA over SHA-256.</summary>
        // cspell:ignore WITHECDSA
        private const string SignatureAlgorithm = "SHA256WITHECDSA";

        /// <summary>
        /// What marks a responder certificate, so a client can tell one without reading a subject
        /// name. `CA.md` §207 requires a marker in an extension rather than a convention,
        /// because a client has to be able to refuse to send real requests over this identity.
        /// </summary>
        /// <remarks>
        /// A URI in the subject alternative name rather than a private extension, and that is a
        /// measurement rather than a preference. A custom OID needs an arc: the UUID arc `2.25`
        /// encodes on .NET Framework but does not survive being read back, and anything under
        /// `1.3.6.1.4.1` would be squatting on somebody else's enterprise number. The `kpg://`
        /// namespace is already this design's own and needs nothing from anybody.
        ///
        /// It is not a claim and grants nothing. Claims are read from admission records, and only
        /// client certificates have them at all.
        ///
        /// The shape is deliberate and was measured: `kpg://responder` is a URI with an authority
        /// and no path, and `System.Uri` normalizes that to `kpg://responder/` on the way into the
        /// certificate, so what a client reads back is not what was written. A form with a path,
        /// like the profile claims, survives unchanged. The first label after the scheme says what
        /// kind of thing this is, which is the same convention <c>kpg://profile/{name}</c> uses.
        /// </remarks>
        public const string ResponderClaim = "kpg://host/responder";

        /// <summary>`id-kp-serverAuth`, RFC 5280 section 4.2.1.12.</summary>
        public const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

        /// <summary>
        /// `id-kp-clientAuth`, from the same list. This and the one above are what separate a host
        /// certificate from a client certificate under an authority that signs both, and hostname
        /// verification is deliberately abandoned here, so they carry more weight than usual.
        /// </summary>
        public const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

        /// <summary>
        /// A hundred years for the authority, and so for the host certificate issued with it.
        /// </summary>
        /// <remarks>
        /// In days, as every lifetime here is counted, and from any date this century these land
        /// on the same date a hundred years on: 2100 is no leap year.
        ///
        /// **The fallback the operator named, because the end of year 9999 cannot be read back.**
        /// What was asked for was no expiry at all, RFC 5280's `99991231235959Z`. The builder
        /// writes that as the RFC asks, and `X509Certificate2.NotAfter` answers an earlier moment
        /// for it in any time zone ahead of UTC without saying so; a hundred years is read exactly
        /// by everything on the path. `LongLivedCertificateTests` is the measurement.
        ///
        /// Nothing warns as the end approaches and nothing refuses ahead of it, which is the rest
        /// of the same decision, `CA.md` §207: a shorter life protects nothing here, since the
        /// host key cannot leak without the authority's beside it, and a leaked authority is
        /// answered by recreating it whatever its dates say.
        /// </remarks>
        public static readonly TimeSpan AuthorityLifetime = TimeSpan.FromDays(36524);

        /// <summary>
        /// Long enough to cover a lock, which is the only thing it has to cover.
        /// </summary>
        /// <remarks>
        /// It was seven days, on the reasoning that a responder is re-minted at every unlock and
        /// only has to outlive one session. What it has to outlive is the lock, and a lock is
        /// unbounded: a machine left locked over a holiday presented an expired certificate, a
        /// client that reads `PAIRING.md` §304 refused it, and the operator got the bare handshake
        /// failure this certificate exists to replace. Failing at exactly the case it was built for
        /// is worse than the window being wider.
        ///
        /// What a short window buys is a bound on how long a responder key that leaked is still
        /// accepted, and that key can do one thing: claim a database is locked. `CA.md` §208 is
        /// where the marker stops a client acting on anything else it says, and the marker is what
        /// carries that argument, not the dates. Ninety days is longer than any lock this should
        /// fail on.
        /// </remarks>
        public static readonly TimeSpan ResponderLifetime = TimeSpan.FromDays(90);

        /// <summary>The claim a profile travels as. The database is not named: the issuer is.</summary>
        public const string ProfileClaimPrefix = "kpg://profile/";

        /// <summary>
        /// What a client may be called. Deliberately narrow: the name arrives from outside at
        /// pairing, it ends up in a distinguished name and in an audit log, and neither is a place
        /// for arbitrary text.
        /// </summary>
        public const int MaxSubjectLength = 64;

        /// <summary>
        /// A certificate is backdated slightly, so that a clock a few seconds behind ours does not
        /// see something issued in its future.
        /// </summary>
        public static readonly TimeSpan Backdate = TimeSpan.FromMinutes(5);

        private readonly Func<DateTimeOffset> _clock;

        public AuthorityIssuer()
            : this(null)
        {
        }

        public AuthorityIssuer(Func<DateTimeOffset> clock)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// A self-signed authority: `CA:TRUE` with `pathLen 0` so it can never mint a subordinate,
        /// and no extended key usage, because one there would constrain everything beneath it.
        /// </summary>
        public CertifiedKey CreateAuthority()
        {
            var random = new SecureRandom();
            var key = NewKeyPair(random);
            var subject = Name(AuthorityName);
            var now = _clock();

            var generator = Generator(subject, subject, key.Public, now - Backdate, now + AuthorityLifetime);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));
            generator.AddExtension(
                X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
            generator.AddExtension(
                X509Extensions.SubjectKeyIdentifier,
                false,
                X509ExtensionUtilities.CreateSubjectKeyIdentifier(key.Public));

            return Roundtrip(generator, key.Private, random, key.Private);
        }

        /// <summary>
        /// A host certificate under that authority: `serverAuth`, and ending exactly when the
        /// authority that vouches for it ends.
        /// </summary>
        /// <remarks>
        /// **Issued together, so it lives as long.** It had a life of its own, half the authority's,
        /// and every client went dark at once when it ran out while the authority still had years
        /// left, with nothing to reissue it: the remedy was recreating the authority, which revokes
        /// every client. A host life shorter than the authority's protects nothing either, because
        /// the host key is kept in the same document as the authority's and cannot leak without it.
        /// </remarks>
        public CertifiedKey IssueHost(CertifiedKey authority)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            var random = new SecureRandom();
            var key = NewKeyPair(random);
            var now = _clock();
            var notBefore = now - Backdate;
            var notAfter = EndOf(authority);

            // Against `now`, not `notBefore`. Comparing with the backdated start let an authority
            // that expired within the last five minutes past this guard and mint a host that was
            // already expired, which `Read` then accepted because it ignores expiry.
            if (notAfter <= now)
            {
                throw new InvalidOperationException(
                    "The authority has expired, so it can issue nothing that is valid.");
            }

            var generator = Leaf(authority, Name(HostName), key.Public, notBefore, notAfter);
            generator.AddExtension(
                X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.KeyAgreement));
            generator.AddExtension(
                X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(KeyPurposeID.id_kp_serverAuth));
            Identify(generator, authority, key.Public);

            return Roundtrip(generator, authority.PrivateKey, random, key.Private);
        }

        /// <summary>
        /// The second host certificate: what a locked KeePass answers with.
        /// </summary>
        /// <remarks>
        /// `CA.md` §207 and `PAIRING.md` §308. Locking closes the database, and with it
        /// the authority, the records and the normal host key. Without this there is nothing left to
        /// complete a handshake with, and a client gets a bare connection failure indistinguishable
        /// from a wrong port, a stopped KeePass, or an attack.
        ///
        /// It is minted while the database is open and kept only in memory, and it can do nothing
        /// but answer "locked": the authority it would need to validate a client certificate is
        /// exactly what has been dropped. The marker is what lets a client tell it apart and refuse
        /// to send real requests over it, because an impostor holding this key reads nothing but
        /// could answer with a fabricated secret, and an agent acting on a forged answer is worse
        /// than an agent that fails.
        ///
        /// No `keyAgreement`, unlike the normal host certificate. It signs a handshake and does
        /// nothing else.
        /// </remarks>
        public CertifiedKey IssueResponder(CertifiedKey authority)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            var random = new SecureRandom();
            var key = NewKeyPair(random);
            var now = _clock();
            var notBefore = now - Backdate;
            var notAfter = Clamp(now + ResponderLifetime, authority);

            // Against `now`, as in `IssueHost`. This method was written from that one before its
            // guard was corrected, and comparing with the backdated start let an authority that
            // expired within the last five minutes mint a responder that was already expired.
            if (notAfter <= now)
            {
                throw new InvalidOperationException(
                    "The authority has expired, so it can issue nothing that is valid.");
            }

            var generator = Leaf(authority, Name(ResponderName), key.Public, notBefore, notAfter);
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
            generator.AddExtension(
                X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(KeyPurposeID.id_kp_serverAuth));
            Identify(generator, authority, key.Public);
            generator.AddExtension(
                X509Extensions.SubjectAlternativeName,
                false,
                new GeneralNames(new GeneralName(GeneralName.UniformResourceIdentifier, ResponderClaim)));

            return Roundtrip(generator, authority.PrivateKey, random, key.Private);
        }

        /// <summary>
        /// A client certificate: `clientAuth`, for the lifetime the operator picked, and the
        /// approved profiles as claims.
        /// </summary>
        /// <remarks>
        /// The claims are a convenience for a human reading a certificate and nothing more. What a
        /// client may reach is read from its admission record in the database, so a certificate
        /// whose record is missing grants nothing at all.
        ///
        /// A year when nobody picked one, which is what the approval dialog offers a client that
        /// asked for nothing. Anything outside `Lifetime`'s bounds is refused here as well as on
        /// the wire, so the rule `CA.md` §207 states has one last door, and the result is clamped
        /// to the authority like every other leaf.
        /// </remarks>
        public CertifiedKey IssueClient(
            CertifiedKey authority, string subjectName, IEnumerable<string> profiles, TimeSpan? lifetime = null)
        {
            var lasts = lifetime ?? Lifetime.Unasked;
            if (!Lifetime.IsAllowed(lasts))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lifetime),
                    "A client certificate lasts whole days from " + Lifetime.ShortestDays + " to " +
                    Lifetime.LongestDays + ".");
            }

            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (!IsUsableSubjectName(subjectName))
            {
                throw new ArgumentException(
                    "A client name is 1 to " + MaxSubjectLength +
                    " characters of letters, digits, space, dot, dash or underscore.", nameof(subjectName));
            }

            var claims = ProfileClaims(profiles);

            var random = new SecureRandom();
            var key = NewKeyPair(random);
            var now = _clock();
            var notBefore = now - Backdate;
            var notAfter = Clamp(now + lasts, authority);

            // Against `now`, not `notBefore`, for the reason `IssueHost` records: comparing
            // with the backdated start lets an authority that expired within the last five
            // minutes mint a client that is already expired, which `Read` then accepts because
            // it ignores expiry and every connection afterwards refuses.
            if (notAfter <= now)
            {
                throw new InvalidOperationException(
                    "The authority has expired, so it can issue nothing that is valid.");
            }

            var generator = Leaf(authority, Name(subjectName), key.Public, notBefore, notAfter);
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
            generator.AddExtension(
                X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth));
            Identify(generator, authority, key.Public);

            if (claims != null)
            {
                generator.AddExtension(X509Extensions.SubjectAlternativeName, false, claims);
            }

            return Roundtrip(generator, authority.PrivateKey, random, key.Private);
        }

        /// <summary>
        /// A profile name that can travel as a claim. No empty names, nothing that would need
        /// escaping, and never the ACL's own `default`, which is a rule rather than a grant.
        /// </summary>
        public static bool IsUsableProfileName(string profile)
        {
            if (string.IsNullOrEmpty(profile) || profile.Length > 64)
            {
                return false;
            }

            if (string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return profile.All(c => Ascii.IsLetterOrDigit(c) || c == '-' || c == '_');
        }

        public static bool IsUsableSubjectName(string subjectName)
        {
            if (string.IsNullOrEmpty(subjectName) || subjectName.Length > MaxSubjectLength)
            {
                return false;
            }

            // Measured on `X500DistinguishedName`, which wrote these names before Bouncy Castle
            // did: it strips the space either end, so `CN=agent`, `CN= agent` and `CN=agent `
            // encoded to the same bytes, and a name of nothing but spaces to an empty common name.
            // Two clients would then carry one identity in a log line and in an admission record,
            // which is the one thing a name is for. Anything that trims a name still joins them,
            // so the rule outlives the encoder that showed it was needed.
            if (subjectName[0] == ' ' || subjectName[subjectName.Length - 1] == ' ')
            {
                return false;
            }

            return subjectName.All(c => Ascii.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ' ');
        }

        private static GeneralNames ProfileClaims(IEnumerable<string> profiles)
        {
            if (profiles == null)
            {
                return null;
            }

            var claims = new List<GeneralName>();
            foreach (var profile in profiles)
            {
                if (!IsUsableProfileName(profile))
                {
                    throw new ArgumentException("A profile name is not usable as a claim: " + profile, nameof(profiles));
                }

                claims.Add(new GeneralName(GeneralName.UniformResourceIdentifier, ProfileClaimPrefix + profile));
            }

            return claims.Count > 0 ? new GeneralNames(claims.ToArray()) : null;
        }

        /// <summary>
        /// Sixteen random bytes with the top bit cleared. RFC 5280 section 4.1.2.2 requires a
        /// positive integer of at most twenty octets, and DER encodes a leading one bit as a
        /// negative number, so clearing it is what keeps the serial positive without a seventeenth
        /// octet in front to say so. It does not keep every serial at sixteen: DER writes an
        /// integer in as few octets as hold it, so one whose first byte is zero and whose second is
        /// below 0x80, about one in 256, is written as fifteen octets or fewer, with the same value.
        /// https://www.rfc-editor.org/rfc/rfc5280#section-4.1.2.2
        /// </summary>
        public static byte[] NewSerialNumber()
        {
            var serial = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(serial);
            }

            serial[0] &= 0x7F;
            return serial;
        }

        /// <summary>
        /// A key pair on P-256, named by its curve, so that a certificate carries the curve's
        /// identifier rather than its parameters spelled out, which is how Windows wrote it and what
        /// RFC 5480 section 2.1.1 requires.
        /// </summary>
        private static AsymmetricCipherKeyPair NewKeyPair(SecureRandom random)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(X9ObjectIdentifiers.Prime256v1, random));
            return generator.GenerateKeyPair();
        }

        /// <summary>A name of one common name, which Bouncy Castle writes as UTF8String.</summary>
        private static X509Name Name(string commonName)
        {
            return new X509Name(new[] { X509Name.CN }, new[] { commonName });
        }

        private static X509V3CertificateGenerator Generator(
            X509Name issuer,
            X509Name subject,
            AsymmetricKeyParameter publicKey,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            var generator = new X509V3CertificateGenerator();
            generator.SetSerialNumber(new BigInteger(1, NewSerialNumber()));
            generator.SetIssuerDN(issuer);
            generator.SetSubjectDN(subject);
            generator.SetNotBefore(Seconds(notBefore));
            generator.SetNotAfter(Seconds(notAfter));
            generator.SetPublicKey(publicKey);
            return generator;
        }

        /// <summary>
        /// The start of every leaf: its authority's subject as its issuer, and `CA:FALSE`.
        /// </summary>
        private static X509V3CertificateGenerator Leaf(
            CertifiedKey authority,
            X509Name subject,
            AsymmetricKeyParameter publicKey,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            var generator = Generator(authority.Certificate.SubjectDN, subject, publicKey, notBefore, notAfter);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            return generator;
        }

        /// <summary>
        /// The two key identifiers every leaf carries: its own, and its authority's, which is the
        /// authority's subject key identifier and nothing else.
        /// </summary>
        /// <remarks>
        /// RFC 5280 section 4.2.1.1, where `keyIdentifier` is context tag 0 and the issuer's name
        /// and serial, which are optional, are left out. Both identifiers are the SHA-1 of the
        /// public key, the first method of section 4.2.1.2, as Windows computed them.
        /// </remarks>
        private static void Identify(
            X509V3CertificateGenerator generator, CertifiedKey authority, AsymmetricKeyParameter publicKey)
        {
            generator.AddExtension(
                X509Extensions.SubjectKeyIdentifier,
                false,
                X509ExtensionUtilities.CreateSubjectKeyIdentifier(publicKey));

            var stored = authority.Certificate.GetExtensionValue(X509Extensions.SubjectKeyIdentifier);
            if (stored == null)
            {
                throw new ArgumentException(
                    "The issuer carries no subject key identifier to point at.", nameof(authority));
            }

            var identifier = SubjectKeyIdentifier.GetInstance(X509ExtensionUtilities.FromExtensionValue(stored));
            generator.AddExtension(
                X509Extensions.AuthorityKeyIdentifier,
                false,
                new AuthorityKeyIdentifier(identifier.GetKeyIdentifier()));
        }

        /// <summary>
        /// A moment as a certificate holds one, in whole seconds, which RFC 5280 section 4.1.2.5
        /// asks of both forms a validity date takes.
        /// </summary>
        private static DateTime Seconds(DateTimeOffset moment)
        {
            var ticks = moment.UtcDateTime.Ticks;
            return new DateTime(ticks - ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }

        /// <summary>
        /// The moment the authority stops vouching for anything, which no leaf may outlast.
        /// </summary>
        private static DateTimeOffset EndOf(CertifiedKey authority)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(authority.Certificate.NotAfter, DateTimeKind.Utc));
        }

        private static DateTimeOffset Clamp(DateTimeOffset wanted, CertifiedKey authority)
        {
            var ceiling = EndOf(authority);
            return wanted > ceiling ? ceiling : wanted;
        }

        /// <summary>
        /// Signs, then writes the result as it is stored and reads it back, so that a certificate
        /// which cannot survive being written to the database fails here rather than after somebody
        /// is told it worked.
        /// </summary>
        /// <param name="generator">The certificate to sign, with every field and extension set.</param>
        /// <param name="signer">The key that signs it: the authority's, or its own when self-signed.</param>
        /// <param name="random">What the signature's nonce is drawn from.</param>
        /// <param name="key">The private key the certificate is for.</param>
        private static CertifiedKey Roundtrip(
            X509V3CertificateGenerator generator,
            AsymmetricKeyParameter signer,
            SecureRandom random,
            AsymmetricKeyParameter key)
        {
            var certificate = generator.Generate(new Asn1SignatureFactory(SignatureAlgorithm, signer, random));
            var restored = CertifiedKey.From(new CertifiedKey(certificate, key).ToPkcs12());
            if (restored == null)
            {
                throw new InvalidOperationException(
                    "A certificate was issued that cannot be stored and read back.");
            }

            return restored;
        }
    }
}
