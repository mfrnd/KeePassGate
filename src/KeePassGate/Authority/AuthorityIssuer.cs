using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
    /// this comment. ECDSA on P-256 with SHA-256 throughout, which needs no third-party
    /// cryptography on .NET Framework.
    ///
    /// Everything below is RFC 5280: basic constraints in section 4.2.1.9, key usage in 4.2.1.3,
    /// extended key usage in 4.2.1.12, and the two key identifiers in 4.2.1.1 and 4.2.1.2.
    /// https://www.rfc-editor.org/rfc/rfc5280#section-4.2.1
    /// The curve is NIST P-256, also called secp256r1 and prime256v1.
    /// </remarks>
    public sealed class AuthorityIssuer
    {
        public const string AuthoritySubject = "CN=KeePassGate authority";
        public const string HostSubject = "CN=KeePassGate host";
        public const string ResponderSubject = "CN=KeePassGate responder";

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

        /// <summary>`id-ce-subjectKeyIdentifier`, RFC 5280 section 4.2.1.2.</summary>
        private const string SubjectKeyIdentifierOid = "2.5.29.14";

        /// <summary>`id-ce-authorityKeyIdentifier`, RFC 5280 section 4.2.1.1.</summary>
        private const string AuthorityKeyIdentifierOid = "2.5.29.35";

        /// <summary>Ten years for the authority, five for a host, as the design specifies.</summary>
        public static readonly TimeSpan AuthorityLifetime = TimeSpan.FromDays(3652);

        public static readonly TimeSpan HostLifetime = TimeSpan.FromDays(1826);

        /// <summary>One year for a client, as `CA.md` §207 specifies.</summary>
        public static readonly TimeSpan ClientLifetime = TimeSpan.FromDays(365);

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
        /// fail on and a twentieth of the host certificate it stands in for.
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
        public X509Certificate2 CreateAuthority()
        {
            // Disposed rather than dropped: this is an NCrypt key handle, and one per press adds
            // up inside a password manager that runs for days. `Roundtrip` exports before the scope
            // ends, so what it returns carries its own copy of the key.
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName(AuthoritySubject), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                var now = _clock();
                var certificate = request.CreateSelfSigned(now - Backdate, now + AuthorityLifetime);

                // Through the store and back, so that a certificate which cannot survive being
                // written to the database fails here rather than after the operator is told it
                // worked.
                return Roundtrip(certificate);
            }
        }

        /// <summary>
        /// A host certificate under that authority: `serverAuth`, and never outliving the authority
        /// that vouches for it.
        /// </summary>
        public X509Certificate2 IssueHost(X509Certificate2 authority)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (!authority.HasPrivateKey)
            {
                throw new ArgumentException("The authority cannot sign without its key.", nameof(authority));
            }

            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName(HostSubject), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ServerAuthOid) }, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                request.CertificateExtensions.Add(AuthorityKeyIdentifierOf(authority));

                var now = _clock();
                var notBefore = now - Backdate;
                var notAfter = Clamp(now + HostLifetime, authority.NotAfter);

                // Against `now`, not `notBefore`. Comparing with the backdated start let an authority
                // that expired within the last five minutes past this guard and mint a host that was
                // already expired, which `Read` then accepted because it ignores expiry.
                if (notAfter <= now)
                {
                    throw new InvalidOperationException(
                        "The authority has expired, so it can issue nothing that is valid.");
                }

                // `issued` is a certificate context of its own and is disposed with the rest: it
                // carries no key, and only the copy below is returned.
                using (var issued = request.Create(authority, notBefore, notAfter, NewSerialNumber()))
                using (var withKey = issued.CopyWithPrivateKey(key))
                {
                    return Roundtrip(withKey);
                }
            }
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
        public X509Certificate2 IssueResponder(X509Certificate2 authority)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (!authority.HasPrivateKey)
            {
                throw new ArgumentException("The authority cannot sign without its key.", nameof(authority));
            }

            // Disposed, as in the siblings: an NCrypt handle per unlock adds up in a process that
            // runs for days.
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName(ResponderSubject), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ServerAuthOid) }, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                request.CertificateExtensions.Add(AuthorityKeyIdentifierOf(authority));

                var marker = new SubjectAlternativeNameBuilder();
                marker.AddUri(new Uri(ResponderClaim));
                request.CertificateExtensions.Add(marker.Build(false));

                var now = _clock();
                var notBefore = now - Backdate;
                var notAfter = Clamp(now + ResponderLifetime, authority.NotAfter);

                // Against `now`, as in `IssueHost`. This method was written from that one before its
                // guard was corrected, and comparing with the backdated start let an authority that
                // expired within the last five minutes mint a responder that was already expired.
                if (notAfter <= now)
                {
                    throw new InvalidOperationException(
                        "The authority has expired, so it can issue nothing that is valid.");
                }

                using (var issued = request.Create(authority, notBefore, notAfter, NewSerialNumber()))
                using (var withKey = issued.CopyWithPrivateKey(key))
                {
                    return Roundtrip(withKey);
                }
            }
        }

        /// <summary>
        /// A client certificate: `clientAuth`, one year, and the approved profiles as claims.
        /// </summary>
        /// <remarks>
        /// The claims are a convenience for a human reading a certificate and nothing more. What a
        /// client may reach is read from its admission record in the database, so a certificate
        /// whose record is missing grants nothing at all.
        /// </remarks>
        public X509Certificate2 IssueClient(X509Certificate2 authority, string subjectName, IEnumerable<string> profiles)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (!authority.HasPrivateKey)
            {
                throw new ArgumentException("The authority cannot sign without its key.", nameof(authority));
            }

            if (!IsUsableSubjectName(subjectName))
            {
                throw new ArgumentException(
                    "A client name is 1 to " + MaxSubjectLength +
                    " characters of letters, digits, space, dot, dash or underscore.", nameof(subjectName));
            }

            // Disposed rather than dropped, for the reason `CreateAuthority` gives: this is an
            // NCrypt key handle, and pairing is the path an agent drives repeatedly. The scope has
            // to cover the extension building as well, because an unusable profile name throws
            // after the key exists.
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName("CN=" + subjectName), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ClientAuthOid) }, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                request.CertificateExtensions.Add(AuthorityKeyIdentifierOf(authority));

                var claims = ProfileClaims(profiles);
                if (claims != null)
                {
                    request.CertificateExtensions.Add(claims);
                }

                var now = _clock();
                var notBefore = now - Backdate;
                var notAfter = Clamp(now + ClientLifetime, authority.NotAfter);

                // Against `now`, not `notBefore`, for the reason `IssueHost` records: comparing
                // with the backdated start lets an authority that expired within the last five
                // minutes mint a client that is already expired, which `Read` then accepts because
                // it ignores expiry and every connection afterwards refuses.
                if (notAfter <= now)
                {
                    throw new InvalidOperationException(
                        "The authority has expired, so it can issue nothing that is valid.");
                }

                using (var issued = request.Create(authority, notBefore, notAfter, NewSerialNumber()))
                using (var withKey = issued.CopyWithPrivateKey(key))
                {
                    return Roundtrip(withKey);
                }
            }
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

            foreach (var c in profile)
            {
                var allowed = (c >= 'a' && c <= 'z')
                              || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9')
                              || c == '-' || c == '_';

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsUsableSubjectName(string subjectName)
        {
            if (string.IsNullOrEmpty(subjectName) || subjectName.Length > MaxSubjectLength)
            {
                return false;
            }

            // Measured: `X500DistinguishedName` strips the space either end, so `CN=agent`,
            // `CN= agent` and `CN=agent ` encode to the same bytes, and a name of nothing but
            // spaces encodes to an empty common name. Two clients would then carry one identity in
            // a log line and in an admission record, which is the one thing a name is for.
            if (subjectName[0] == ' ' || subjectName[subjectName.Length - 1] == ' ')
            {
                return false;
            }

            foreach (var c in subjectName)
            {
                var allowed = (c >= 'a' && c <= 'z')
                              || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9')
                              || c == '-' || c == '_' || c == '.' || c == ' ';

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static X509Extension ProfileClaims(IEnumerable<string> profiles)
        {
            if (profiles == null)
            {
                return null;
            }

            var builder = new SubjectAlternativeNameBuilder();
            var any = false;

            foreach (var profile in profiles)
            {
                if (!IsUsableProfileName(profile))
                {
                    throw new ArgumentException("A profile name is not usable as a claim: " + profile, nameof(profiles));
                }

                builder.AddUri(new Uri(ProfileClaimPrefix + profile));
                any = true;
            }

            return any ? builder.Build(false) : null;
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
        /// The authority key identifier, built by hand because .NET Framework has no class for it:
        /// `SEQUENCE { [0] keyIdentifier }` carrying the issuer's subject key identifier.
        /// </summary>
        /// <remarks>
        /// RFC 5280 section 4.2.1.1, where `keyIdentifier` is context tag 0 and the other two
        /// fields are optional and omitted here. The encoding below is DER by hand: `0x30` is
        /// SEQUENCE, `0x80` is that context tag, and each length fits one byte because a key
        /// identifier is a SHA-1 hash. ITU-T X.690 section 8.1 is the length rule.
        /// </remarks>
        public static X509Extension AuthorityKeyIdentifierOf(X509Certificate2 issuer)
        {
            var subjectKeyIdentifier = issuer.Extensions[SubjectKeyIdentifierOid] as X509SubjectKeyIdentifierExtension;
            if (subjectKeyIdentifier == null)
            {
                throw new ArgumentException(
                    "The issuer carries no subject key identifier to point at.", nameof(issuer));
            }

            var identifier = FromHex(subjectKeyIdentifier.SubjectKeyIdentifier);
            // 125, not 127. The identifier is wrapped in four bytes below, and the DER short
            // form runs out at 127 for the whole structure rather than for its contents.
            if (identifier.Length > 125)
            {
                throw new ArgumentException("The issuer's key identifier is implausibly long.", nameof(issuer));
            }

            var encoded = new byte[identifier.Length + 4];
            encoded[0] = 0x30;                              // SEQUENCE
            encoded[1] = (byte)(identifier.Length + 2);
            encoded[2] = 0x80;                              // [0] keyIdentifier, context specific
            encoded[3] = (byte)identifier.Length;
            Buffer.BlockCopy(identifier, 0, encoded, 4, identifier.Length);

            return new X509Extension(AuthorityKeyIdentifierOid, encoded, false);
        }

        private static DateTimeOffset Clamp(DateTimeOffset wanted, DateTime authorityNotAfter)
        {
            var ceiling = new DateTimeOffset(authorityNotAfter.ToUniversalTime(), TimeSpan.Zero);
            return wanted > ceiling ? ceiling : wanted;
        }

        private static X509Certificate2 Roundtrip(X509Certificate2 certificate)
        {
            string stored;
            try
            {
                stored = CertificateBlob.Save(certificate);
            }
            finally
            {
                // In a `finally`, so a certificate that cannot be exported is still released
                // rather than left for the garbage collector.
                certificate.Reset();
            }

            var restored = CertificateBlob.Load(stored);
            if (restored == null)
            {
                throw new InvalidOperationException(
                    "A certificate was issued that cannot be stored and read back.");
            }

            return restored;
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
