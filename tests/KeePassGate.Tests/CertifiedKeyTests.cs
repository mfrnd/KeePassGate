using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A stored certificate the reader refuses, in each of the ways it can be built to be refused,
    /// and the one shape it reads.
    /// </summary>
    /// <remarks>
    /// **`CertifiedKey.From` is the only reader of what a database holds**, and a database somebody
    /// crafted can hold any bytes at all, so every shape here is one a `.kdbx` can carry, whatever
    /// any other reader would have made of it. Most are built here bag by bag, under the empty
    /// password and a MAC computed again, so that each differs from a shape that reads in the one
    /// way it is named for: the shape built from these parts reads, which is the first test.
    ///
    /// **Each type the reader names is pinned here**, by a shape that throws it inside: without
    /// the name, the shape's test fails with the exception rather than with a wrong answer.
    /// </remarks>
    [TestFixture]
    public sealed class CertifiedKeyTests
    {
        // cspell:ignore WITHECDSA WITHRSA brainpool sdsi
        private const string Signature = "SHA256WITHECDSA";

        /// <summary>The count this plugin writes, which every part and bag here is built at unless named.</summary>
        private const int Written = 1024;

        private static readonly SecureRandom Random = new SecureRandom();

        private byte[] _stored;
        private CertifiedKey _host;

        [SetUp]
        public void SetUp()
        {
            var issuer = new AuthorityIssuer();
            _host = issuer.IssueHost(issuer.CreateAuthority());
            _stored = CertificateBlob.Bytes(CertificateBlob.Save(_host));
        }

        [Test]
        public void The_host_itself_still_reads_after_all_of_that()
        {
            Assert.That(CertifiedKey.From(_stored), Is.Not.Null);
        }

        [Test]
        public void The_shape_built_here_reads_so_each_refusal_below_is_its_one_change()
        {
            var read = CertifiedKey.From(Stored());

            Assert.That(read, Is.Not.Null);
            Assert.That(read.CertificateDer, Is.EqualTo(_host.CertificateDer));
        }

        [TestCase(CertifiedKey.MostIterations, true)]
        [TestCase(CertifiedKey.MostIterations + 1, false)]
        public void A_MAC_is_computed_at_no_more_than_ten_thousand_iterations(int count, bool reads)
        {
            // Bouncy Castle honors five million, three seconds on KeePass's own thread for the MAC
            // and again for every bag, and refuses a count above that only. Asked of the structure,
            // the count is refused before anything is derived at it.
            var crafted = Pkcs12(count, Plain(KeyBag(_host.PrivateKey)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [TestCase(CertifiedKey.MostIterations, true)]
        [TestCase(CertifiedKey.MostIterations + 1, false)]
        public void A_key_bag_is_held_to_the_same_count(int count, bool reads)
        {
            var crafted = Pkcs12(Written, Plain(KeyBag(_host.PrivateKey, count)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [TestCase(CertifiedKey.MostIterations, true)]
        [TestCase(CertifiedKey.MostIterations + 1, false)]
        public void An_encrypted_part_is_held_to_the_same_count(int count, bool reads)
        {
            var crafted = Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), Sealed(count, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [TestCase(CertifiedKey.MostIterations, true)]
        [TestCase(CertifiedKey.MostIterations + 1, false)]
        public void A_key_bag_inside_an_encrypted_part_is_held_to_the_same_count(int count, bool reads)
        {
            // Bouncy Castle reads a key bag wherever it is, so a file of nothing but encrypted
            // parts can carry as many as its size allows, each at a count nothing outside the
            // part can see. The part is opened, at its own bounded count, to bound what is inside.
            var crafted = Pkcs12(Written, Sealed(Written, KeyBag(_host.PrivateKey, count), CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [Test]
        public void A_key_bag_asking_for_five_million_iterations_is_refused_without_deriving_them()
        {
            // What the counts above are for, measured: Bouncy Castle derived this one for three
            // seconds before finding it would not decrypt, on KeePass's thread at every open.
            var crafted = Pkcs12(Written, Plain(Shrouded(Pkcs3Des(5000000), Garbage())), Sealed(Written, CertificateBag(_host.Certificate)));

            var elapsed = Stopwatch.StartNew();
            Assert.That(CertifiedKey.From(crafted), Is.Null);
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), elapsed.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void A_blob_larger_than_any_this_plugin_writes_is_refused_unread()
        {
            // A bag of a kind Bouncy Castle passes over, so that size is the only thing wrong.
            var padding = new SafeBag(PkcsObjectIdentifiers.SecretBag, new DerOctetString(new byte[CertifiedKey.MostBytes]));
            var crafted = Pkcs12(Written, Plain(KeyBag(_host.PrivateKey), padding), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(crafted.Length, Is.GreaterThan(CertifiedKey.MostBytes));
            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [TestCase(CertifiedKey.MostParts, true)]
        [TestCase(CertifiedKey.MostParts + 1, false)]
        public void No_more_than_four_parts_are_read(int parts, bool reads)
        {
            var empty = Enumerable.Range(0, parts - 2).Select(i => Plain()).ToArray();
            var crafted = Pkcs12(
                Written,
                new[] { Plain(KeyBag(_host.PrivateKey)), Sealed(Written, CertificateBag(_host.Certificate)) }.Concat(empty).ToArray());

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [TestCase(CertifiedKey.MostBags, true)]
        [TestCase(CertifiedKey.MostBags + 1, false)]
        public void No_more_than_four_bags_are_read(int bags, bool reads)
        {
            var passedOver = Enumerable.Range(0, bags - 2)
                .Select(i => new SafeBag(PkcsObjectIdentifiers.SecretBag, new DerOctetString(new byte[] { 1 })))
                .ToArray();
            var crafted = Pkcs12(
                Written,
                Plain(new[] { KeyBag(_host.PrivateKey) }.Concat(passedOver).ToArray()),
                Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted) != null, Is.EqualTo(reads));
        }

        [Test]
        public void A_PKCS12_holding_two_keys_is_nothing_rather_than_the_first_of_them()
        {
            // Bouncy Castle keeps both, and the reader used to take whichever it listed first.
            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();
            var store = new Pkcs12StoreBuilder()
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .SetUseDerEncoding(true)
                .Build();
            store.SetKeyEntry("one", new AsymmetricKeyEntry(_host.PrivateKey), new[] { new X509CertificateEntry(_host.Certificate) });
            store.SetKeyEntry("two", new AsymmetricKeyEntry(authority.PrivateKey), new[] { new X509CertificateEntry(authority.Certificate) });

            Assert.That(CertifiedKey.From(Saved(store, Array.Empty<char>())), Is.Null);
        }

        [Test]
        public void A_key_bag_under_an_algorithm_whose_count_cannot_be_read_is_refused()
        {
            // cspell:ignore PBES
            // PBES2 carries its count inside a key derivation function, where only reading that
            // function would find it, so it is refused as a cost nobody bounded. This one names a
            // cipher with no parameters, which Bouncy Castle answered with an exception of its own
            // from inside the decryption.
            var scheme = new DerSequence(
                new DerSequence(PkcsObjectIdentifiers.IdPbkdf2, new DerSequence(new DerOctetString(new byte[8]), DerInteger.One)),
                new DerSequence(new DerObjectIdentifier("1.2.3.4.10")));
            var crafted = Pkcs12(
                Written,
                Plain(Shrouded(new AlgorithmIdentifier(PkcsObjectIdentifiers.IdPbeS2, scheme), Garbage())),
                Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [Test]
        public void A_MAC_naming_a_cipher_rather_than_a_digest_is_nothing()
        {
            // Bouncy Castle made the cipher and cast it to a MAC, an `InvalidCastException` that no
            // reader names. The structure is asked whether its MAC is one before Bouncy Castle is.
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(_stored));
            var mac = pfx.MacData;
            var cipher = new MacData(
                new DigestInfo(new AlgorithmIdentifier(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc, DerNull.Instance), mac.Mac.Digest.GetOctets()),
                mac.GetSalt(),
                mac.IterationCount.IntValueExact);

            Assert.That(CertifiedKey.From(new Pfx(pfx.AuthSafe, cipher).GetDerEncoded()), Is.Null);
        }

        [Test]
        public void A_MAC_under_an_algorithm_nobody_knows_is_no_host_either()
        {
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(_stored));
            var mac = pfx.MacData;
            var unknown = new MacData(
                new DigestInfo(new AlgorithmIdentifier(new DerObjectIdentifier("1.2.3.4.5")), mac.Mac.Digest.GetOctets()),
                mac.GetSalt(),
                mac.IterationCount.IntValueExact);

            Assert.That(CertifiedKey.From(new Pfx(pfx.AuthSafe, unknown).GetDerEncoded()), Is.Null);
        }

        [Test]
        public void A_PKCS12_under_a_password_comes_back_as_nothing()
        {
            var store = new Pkcs12StoreBuilder()
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .Build();
            store.SetKeyEntry("fictitious", new AsymmetricKeyEntry(_host.PrivateKey), new[] { new X509CertificateEntry(_host.Certificate) });

            Assert.That(CertifiedKey.From(Saved(store, "fictitious".ToCharArray())), Is.Null);
        }

        [Test]
        public void A_key_stored_without_its_certificate_is_nothing()
        {
            // Bouncy Castle reads the key and finds no chain for it, which is the one of the
            // reader's checks on the entry that a stored blob reaches.
            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)))), Is.Null);
        }

        [Test]
        public void A_certificate_stored_beside_a_key_that_is_not_its_own_is_nothing()
        {
            // A PKCS#12 pairs the two by a local identifier anybody can write. An authority read
            // this way would have issued every client a certificate that chains to nothing, and a
            // host would have failed every handshake, both long after the read had found nothing
            // wrong.
            var stranger = new AuthorityIssuer().CreateAuthority();
            var crafted = Pkcs12(Written, Plain(KeyBag(stranger.PrivateKey)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [TestCase("secp112r1")]
        [TestCase("sect163k1")]
        [TestCase("secp256k1")]
        [TestCase("secp384r1")]
        [TestCase("brainpoolP256r1")]
        public void An_authority_on_any_curve_but_P256_is_nothing_this_plugin_wrote(string curve)
        {
            // Bouncy Castle reads every curve it knows, a curve of 112 bits among them, and Windows
            // refused most of them while it read these. P-256 is the only curve keys are made on.
            var keys = KeysOn(ECNamedCurveTable.GetOid(curve));
            var certificate = SelfSigned(keys, Signature);
            var crafted = Pkcs12(Written, Plain(KeyBag(keys.Private)), Sealed(Written, CertificateBag(certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
            Assert.Throws<ArgumentException>(() => new CertifiedKey(certificate, keys.Private));
        }

        [Test]
        public void A_key_on_another_curve_beside_a_P256_certificate_is_nothing()
        {
            var other = KeysOn(ECNamedCurveTable.GetOid("secp384r1"));
            var crafted = Pkcs12(Written, Plain(KeyBag(other.Private)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
            Assert.Throws<ArgumentException>(() => new CertifiedKey(_host.Certificate, other.Private));
        }

        [Test]
        public void An_RSA_key_with_its_own_certificate_is_nothing_this_plugin_wrote()
        {
            var generator = new RsaKeyPairGenerator();
            generator.Init(new KeyGenerationParameters(Random, 2048));
            var keys = generator.GenerateKeyPair();
            var certificate = SelfSigned(keys, "SHA256WITHRSA");
            var crafted = Pkcs12(Written, Plain(KeyBag(keys.Private)), Sealed(Written, CertificateBag(certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
            Assert.Throws<ArgumentException>(() => new CertifiedKey(certificate, keys.Private));
        }

        [Test]
        public void A_P256_key_with_its_curve_spelled_out_rather_than_named_is_nothing_this_plugin_wrote()
        {
            // The same curve, and a key the certificate does certify, written as its parameters
            // rather than its name, which RFC 5480 section 2.1.1 forbids and nothing here writes.
            var named = (ECPrivateKeyParameters)_host.PrivateKey;
            var curve = ECNamedCurveTable.GetByOid(X9ObjectIdentifiers.Prime256v1);
            var spelledOut = new ECPrivateKeyParameters(named.D, new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H));
            var crafted = Pkcs12(Written, Plain(KeyBag(spelledOut)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [Test]
        public void A_certificate_whose_key_is_not_an_elliptic_curve_key_is_nothing()
        {
            var edwards = new Ed25519KeyPairGenerator();
            edwards.Init(new Ed25519KeyGenerationParameters(Random));
            var info = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(edwards.GenerateKeyPair().Public);

            Assert.That(CertifiedKey.From(WithPublicKey(info)), Is.Null);
        }

        [Test]
        public void A_certificate_whose_curve_is_spelled_out_rather_than_named_is_nothing_this_plugin_wrote()
        {
            // The host's own point on P-256, so the key certifies it, with the curve written as
            // its parameters: the decoder would read it, and only its name is asked about.
            var point = ((ECPublicKeyParameters)_host.Certificate.GetPublicKey()).Q;
            var curve = ECNamedCurveTable.GetByOid(X9ObjectIdentifiers.Prime256v1);
            var spelledOut = new X962Parameters(new X9ECParameters(curve.Curve, new X9ECPoint(curve.G, false), curve.N, curve.H));
            var info = new SubjectPublicKeyInfo(
                new AlgorithmIdentifier(X9ObjectIdentifiers.IdECPublicKey, spelledOut), point.GetEncoded(false));

            Assert.That(CertifiedKey.From(WithPublicKey(info)), Is.Null);
        }

        [Test]
        public void A_certificate_whose_curve_is_left_to_its_issuer_is_nothing_rather_than_an_exception()
        {
            // `implicitlyCA`, a NULL where the curve should be, which Bouncy Castle's decoder
            // answers with a `NotImplementedException` nothing names. The curve is asked about
            // before the decoder is, so the decoder only ever decodes a point on P-256.
            var point = ((ECPublicKeyParameters)_host.Certificate.GetPublicKey()).Q;
            var info = new SubjectPublicKeyInfo(
                new AlgorithmIdentifier(X9ObjectIdentifiers.IdECPublicKey, DerNull.Instance), point.GetEncoded(false));

            Assert.That(CertifiedKey.From(WithPublicKey(info)), Is.Null);
        }

        [Test]
        public void A_certificate_whose_point_is_written_compressed_is_nothing_this_plugin_wrote()
        {
            // The host's own point, and so a key it certifies, in the form nothing here writes.
            var point = ((ECPublicKeyParameters)_host.Certificate.GetPublicKey()).Q;

            Assert.That(CertifiedKey.From(WithPublicKey(P256Key(point.GetEncoded(true)))), Is.Null);
        }

        [Test]
        public void A_certificate_whose_point_is_empty_is_nothing_rather_than_an_exception()
        {
            // Decoded, an empty point was an `IndexOutOfRangeException` out of the curve's own
            // decoder, which no reader names. It is asked about as it is written instead.
            Assert.That(CertifiedKey.From(WithPublicKey(P256Key(Array.Empty<byte>()))), Is.Null);
        }

        [Test]
        public void A_certificate_whose_point_is_not_on_the_curve_is_nothing()
        {
            // The one shape here that is a whole uncompressed point, so it reaches the decoder,
            // which refuses it with the `ArgumentException` the reader names.
            var point = new byte[65];
            point[0] = 0x04;
            point[64] = 1;

            Assert.That(CertifiedKey.From(WithPublicKey(P256Key(point))), Is.Null);
        }

        [Test]
        public void Bytes_that_end_before_their_lengths_do_are_nothing()
        {
            // An `IOException`, which is how Bouncy Castle reports ASN.1 that runs out.
            Assert.That(CertifiedKey.From(_stored.Take(_stored.Length / 2).ToArray()), Is.Null);
        }

        [Test]
        public void A_structure_of_the_wrong_kind_is_nothing()
        {
            // An `ArgumentException`, which is what Bouncy Castle makes of an INTEGER where the
            // file's outermost sequence should be.
            Assert.That(CertifiedKey.From(DerInteger.ValueOf(5).GetEncoded()), Is.Null);
        }

        [Test]
        public void An_encrypted_part_tagged_in_a_way_its_type_does_not_allow_is_nothing()
        {
            // An `InvalidOperationException`, which is what Bouncy Castle makes of a tag it did not
            // expect: the encrypted content is context tag 0, and this one is tag 1.
            var sealedPart = Sealed(Written, CertificateBag(_host.Certificate));
            var encrypted = (Asn1Sequence)((Asn1Sequence)sealedPart.Content)[1];
            var content = Asn1OctetString.GetInstance((Asn1TaggedObject)encrypted[2], false);
            var relabeled = new ContentInfo(
                PkcsObjectIdentifiers.EncryptedData,
                new DerSequence(DerInteger.Zero, new DerSequence(encrypted[0], encrypted[1], new DerTaggedObject(false, 1, content))));

            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), relabeled)), Is.Null);
        }

        [Test]
        public void A_key_bag_whose_encryption_will_not_undo_is_nothing()
        {
            // A `CryptoException`: Bouncy Castle answers the padding it cannot undo with
            // `InvalidCipherTextException`.
            var crafted = Pkcs12(Written, Plain(Shrouded(Pkcs3Des(Written, new byte[20]), Garbage())), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [Test]
        public void A_key_of_an_algorithm_nobody_knows_is_nothing()
        {
            // A `SecurityUtilityException`, which derives from nothing but `Exception`.
            var unknown = new PrivateKeyInfo(new AlgorithmIdentifier(new DerObjectIdentifier("1.2.3.4.5")), new DerOctetString(new byte[32]));
            var crafted = Pkcs12(Written, Plain(Shrouded(unknown, Written)), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [Test]
        public void A_certificate_whose_basic_constraints_will_not_parse_is_nothing()
        {
            // A `GeneralSecurityException`: Bouncy Castle's certificate reads its basic
            // constraints as it is made, and throws `CertificateParsingException` for these.
            var unreadable = WithExtension(
                (Asn1Sequence)Asn1Object.FromByteArray(_host.CertificateDer),
                X509Extensions.BasicConstraints,
                new DerUtf8String("not a sequence"));
            var bag = new SafeBag(PkcsObjectIdentifiers.CertBag, new CertBag(PkcsObjectIdentifiers.X509Certificate, new DerOctetString(unreadable)));

            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), Sealed(Written, bag))), Is.Null);
        }

        [Test]
        public void A_certificate_bag_holding_anything_but_an_X509_certificate_is_nothing()
        {
            // `Exception` itself, which Bouncy Castle throws for a certificate type it does not
            // know, here `sdsiCertificate`, and which the reader names by its exact type.
            var bag = new SafeBag(
                PkcsObjectIdentifiers.CertBag,
                new CertBag(new DerObjectIdentifier("1.2.840.113549.1.9.22.2"), new DerOctetString(new byte[] { 1, 2, 3 })));

            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), Sealed(Written, CertificateBag(_host.Certificate), bag))), Is.Null);
        }

        [Test]
        public void A_key_bag_under_an_algorithm_Bouncy_Castle_cannot_decrypt_with_is_nothing()
        {
            // An `ArgumentException` again, this time for a count that reads under an algorithm
            // Bouncy Castle has no cipher for.
            var unknown = new AlgorithmIdentifier(new DerObjectIdentifier("1.2.840.113549.1.12.1.99"), new Pkcs12PbeParams(new byte[8], Written));
            var crafted = Pkcs12(Written, Plain(Shrouded(unknown, Garbage())), Sealed(Written, CertificateBag(_host.Certificate)));

            Assert.That(CertifiedKey.From(crafted), Is.Null);
        }

        [TestCase(true, 1)]
        [TestCase(true, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 0)]
        public void An_attribute_of_fewer_than_two_parts_is_nothing_rather_than_an_exception(bool onTheKey, int parts)
        {
            // Bouncy Castle's store indexes an attribute's values without counting its parts, and
            // threw `IndexOutOfRangeException` for either, past every filter that reads a stored
            // authority.
            var attributes = new DerSet(parts == 1 ? new DerSequence(PkcsObjectIdentifiers.Pkcs9AtFriendlyName) : new DerSequence());
            var key = KeyBag(_host.PrivateKey);
            var certificate = CertificateBag(_host.Certificate);
            if (onTheKey)
            {
                key = With(key, attributes);
            }
            else
            {
                certificate = With(certificate, attributes);
            }

            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(key), Sealed(Written, certificate))), Is.Null);
        }

        [Test]
        public void An_attribute_this_plugin_never_writes_is_left_for_Bouncy_Castle_to_read()
        {
            // Windows named its key storage provider in an attribute of its own, so a stored blob
            // may carry one, and holding attributes to their shape refuses none of those.
            var provider = new DerSet(new DerSequence(
                new DerObjectIdentifier("1.3.6.1.4.1.311.17.1"),
                new DerSet(new DerBmpString("Microsoft Software Key Storage Provider"))));
            var key = With(KeyBag(_host.PrivateKey), provider);

            Assert.That(CertifiedKey.From(Pkcs12(Written, Plain(key), Sealed(Written, CertificateBag(_host.Certificate)))), Is.Not.Null);
        }

        [Test]
        public void The_constructor_refuses_a_missing_certificate_or_key()
        {
            Assert.Throws<ArgumentNullException>(() => new CertifiedKey(null, _host.PrivateKey));
            Assert.Throws<ArgumentNullException>(() => new CertifiedKey(_host.Certificate, null));
        }

        [Test]
        public void One_instance_issues_and_serves_from_many_threads_at_once()
        {
            // What every target does: one authority key issuing for every pairing, renewal and
            // responder, and one host key presented by every connection's TLS server, at once. It
            // holds because nothing in the instance is written after it is made, and a field filled
            // in later would race here. Each issued certificate is checked to the byte, and every
            // handshake to its answer.
            using (var authority = TestAuthority.New())
            {
                var log = new RecordingGateLog();
                var incidents = new GateIncidents(log);
                using (var listener = new GateListener(
                    new GateOptions { Port = 0 },
                    new GateResolver(new FakeGateTargets(authority), log),
                    GateOperations.Router("9.9.9"),
                    incidents,
                    log))
                {
                    Assert.That(listener.Start(), Is.True, "the listener did not start");

                    var client = authority.IssueAdmittedClientKey();
                    var subject = authority.AuthorityKey.Certificate.SubjectDN.GetEncoded();
                    var identifier = X509ExtensionUtilities.CreateSubjectKeyIdentifier(
                        authority.AuthorityKey.Certificate.GetPublicKey()).GetKeyIdentifier();
                    var failures = new ConcurrentQueue<string>();

                    var threads = Enumerable.Range(0, 8).Select(index => new Thread(() =>
                    {
                        for (var round = 0; round < 12; round++)
                        {
                            if (index % 2 == 0)
                            {
                                var reply = BouncyCastleGateClient.Get(
                                    listener.Port, authority.NewName(), client.CertificateDer, client.PrivateKey, "/gate/status");
                                if (reply.Status != 200)
                                {
                                    failures.Enqueue("a handshake: " + reply);
                                }

                                continue;
                            }

                            var issued = new AuthorityIssuer().IssueClient(authority.AuthorityKey, "agent-fictitious", new[] { "ops" });
                            var pointer = X509ExtensionUtilities.GetAuthorityKeyIdentifier(issued.Certificate).KeyIdentifier.GetOctets();
                            using (var leaf = issued.Read())
                            {
                                if (!issued.Certificate.IssuerDN.GetEncoded().SequenceEqual(subject)
                                    || !pointer.SequenceEqual(identifier)
                                    || !AnchorPin.Chains(leaf, authority.Authority))
                                {
                                    failures.Enqueue("an issuance that does not belong to its authority");
                                }
                            }
                        }
                    })).ToList();

                    threads.ForEach(thread => thread.Start());
                    threads.ForEach(thread => thread.Join());

                    Assert.That(failures, Is.Empty);
                    Assert.That(incidents.Count, Is.Zero, string.Join("\n", log.Entries));
                }
            }
        }

        /// <summary>A PKCS#12 of the parts given, under the empty password and a SHA-1 MAC at the count given.</summary>
        private static byte[] Pkcs12(int macCount, params ContentInfo[] parts)
        {
            var authenticated = new AuthenticatedSafe(parts).GetEncoded();
            var salt = new byte[20];
            Random.NextBytes(salt);
            var digest = new DigestInfo(new AlgorithmIdentifier(OiwObjectIdentifiers.IdSha1, DerNull.Instance), Mac(authenticated, salt, macCount));

            return new Pfx(new ContentInfo(PkcsObjectIdentifiers.Data, new DerOctetString(authenticated)), new MacData(digest, salt, macCount))
                .GetDerEncoded();
        }

        /// <summary>The stored shape: the key in a plain part, the certificate in an encrypted one.</summary>
        private byte[] Stored()
        {
            return Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), Sealed(Written, CertificateBag(_host.Certificate)));
        }

        private static ContentInfo Plain(params SafeBag[] bags)
        {
            return new ContentInfo(PkcsObjectIdentifiers.Data, new DerOctetString(new DerSequence(bags).GetEncoded()));
        }

        /// <summary>A part encrypted under three-key triple DES at the count given, as the certificate is stored.</summary>
        private static ContentInfo Sealed(int count, params SafeBag[] bags)
        {
            var algorithm = Pkcs3Des(count);
            var encrypted = Encrypted(algorithm, new DerSequence(bags).GetEncoded());

            return new ContentInfo(
                PkcsObjectIdentifiers.EncryptedData,
                new EncryptedData(PkcsObjectIdentifiers.Data, algorithm, new DerOctetString(encrypted)).ToAsn1Object());
        }

        private static SafeBag KeyBag(AsymmetricKeyParameter key, int count = Written)
        {
            return Shrouded(PrivateKeyInfoFactory.CreatePrivateKeyInfo(key), count);
        }

        private static SafeBag Shrouded(PrivateKeyInfo key, int count)
        {
            var algorithm = Pkcs3Des(count);
            return Shrouded(algorithm, Encrypted(algorithm, key.GetEncoded()));
        }

        /// <summary>Bytes encrypted under the empty password, as everything stored is.</summary>
        private static byte[] Encrypted(AlgorithmIdentifier algorithm, byte[] plain)
        {
            var cipher = (IBufferedCipher)PbeUtilities.CreateEngine(algorithm);
            cipher.Init(true, PbeUtilities.GenerateCipherParameters(algorithm, Array.Empty<char>(), false));
            return cipher.DoFinal(plain);
        }

        /// <summary>A key bag under the algorithm given whose contents are bytes that were never encrypted.</summary>
        private static SafeBag Shrouded(AlgorithmIdentifier algorithm, byte[] contents)
        {
            return new SafeBag(PkcsObjectIdentifiers.Pkcs8ShroudedKeyBag, new EncryptedPrivateKeyInfo(algorithm, contents).ToAsn1Object());
        }

        /// <summary>The bag given, carrying the attributes given.</summary>
        private static SafeBag With(SafeBag bag, Asn1Set attributes)
        {
            return new SafeBag(bag.BagID, bag.BagValue.ToAsn1Object(), attributes);
        }

        private static SafeBag CertificateBag(Org.BouncyCastle.X509.X509Certificate certificate)
        {
            return new SafeBag(
                PkcsObjectIdentifiers.CertBag,
                new CertBag(PkcsObjectIdentifiers.X509Certificate, new DerOctetString(certificate.GetEncoded())));
        }

        private static AlgorithmIdentifier Pkcs3Des(int count, byte[] salt = null)
        {
            if (salt == null)
            {
                salt = new byte[20];
                Random.NextBytes(salt);
            }

            return new AlgorithmIdentifier(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc, new Pkcs12PbeParams(salt, count));
        }

        private static byte[] Garbage()
        {
            return Enumerable.Range(0, 64).Select(i => (byte)(i * 37 + 11)).ToArray();
        }

        private static byte[] Saved(Pkcs12Store store, char[] password)
        {
            using (var bytes = new MemoryStream())
            {
                store.Save(bytes, password, Random);
                return bytes.ToArray();
            }
        }

        private static AsymmetricCipherKeyPair KeysOn(DerObjectIdentifier curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(curve, Random));
            return generator.GenerateKeyPair();
        }

        private static Org.BouncyCastle.X509.X509Certificate SelfSigned(AsymmetricCipherKeyPair keys, string signature)
        {
            var name = new X509Name("CN=KeePassGate authority");
            var generator = Generator(name);
            generator.SetPublicKey(keys.Public);
            return generator.Generate(new Asn1SignatureFactory(signature, keys.Private, Random));
        }

        /// <summary>
        /// The host's key bag beside a certificate the host's key signs whose subject public key is
        /// the one given, so that only the key it claims is wrong.
        /// </summary>
        private byte[] WithPublicKey(SubjectPublicKeyInfo info)
        {
            var generator = Generator(new X509Name("CN=KeePassGate host"));
            generator.SetSubjectPublicKeyInfo(info);
            var certificate = generator.Generate(new Asn1SignatureFactory(Signature, _host.PrivateKey, Random));

            return Pkcs12(Written, Plain(KeyBag(_host.PrivateKey)), Sealed(Written, CertificateBag(certificate)));
        }

        /// <summary>A P-256 public key written as the bytes given, whatever they are.</summary>
        private static SubjectPublicKeyInfo P256Key(byte[] point)
        {
            return new SubjectPublicKeyInfo(
                new AlgorithmIdentifier(X9ObjectIdentifiers.IdECPublicKey, X9ObjectIdentifiers.Prime256v1), point);
        }

        private static X509V3CertificateGenerator Generator(X509Name name)
        {
            var generator = new X509V3CertificateGenerator();
            generator.SetSerialNumber(BigInteger.One);
            generator.SetIssuerDN(name);
            generator.SetSubjectDN(name);
            generator.SetNotBefore(DateTime.UtcNow.AddMinutes(-5));
            generator.SetNotAfter(DateTime.UtcNow.AddDays(30));
            return generator;
        }

        /// <summary>
        /// The certificate with one extension's value replaced, signature and all left as they were,
        /// since nothing that reads the extension first checks the signature.
        /// </summary>
        private static byte[] WithExtension(Asn1Sequence certificate, DerObjectIdentifier oid, Asn1Encodable value)
        {
            var signed = ((Asn1Sequence)certificate[0]).Cast<Asn1Encodable>().ToArray();
            var tagged = (Asn1TaggedObject)signed[signed.Length - 1];
            var extensions = ((Asn1Sequence)tagged.GetExplicitBaseObject()).Cast<Asn1Encodable>()
                .Select(extension => ((DerObjectIdentifier)((Asn1Sequence)extension)[0]).Equals(oid)
                    ? new DerSequence(oid, DerBoolean.True, new DerOctetString(value.GetEncoded()))
                    : extension)
                .ToArray();
            signed[signed.Length - 1] = new DerTaggedObject(true, 3, new DerSequence(extensions));

            return new DerSequence(new DerSequence(signed), certificate[1], certificate[2]).GetEncoded();
        }

        /// <summary>The PKCS#12 MAC under the empty password, which is what the stored form uses.</summary>
        private static byte[] Mac(byte[] data, byte[] salt, int iterations)
        {
            var generator = new Pkcs12ParametersGenerator(new Sha1Digest());
            generator.Init(PbeParametersGenerator.Pkcs12PasswordToBytes(Array.Empty<char>()), salt, iterations);

            var hmac = new HMac(new Sha1Digest());
            hmac.Init((KeyParameter)generator.GenerateDerivedMacParameters(160));
            hmac.BlockUpdate(data, 0, data.Length);

            var output = new byte[hmac.GetMacSize()];
            hmac.DoFinal(output, 0);
            return output;
        }
    }
}
