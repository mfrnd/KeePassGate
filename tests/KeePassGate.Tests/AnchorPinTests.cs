using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Security;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the pin refuses, and what it never does: a certificate from another authority, or
    /// naming ours in other bytes, or under an anchor that is no authority, or of any shape this
    /// plugin does not write, is refused, nothing is ever thrown at the caller, and nothing a
    /// certificate names is ever fetched.
    /// </summary>
    /// <remarks>
    /// The two measurements that made the chain engine unfit for this are the ones `AnchorPin`
    /// gives: that it accepted a certificate from a completely foreign authority unless the anchor
    /// was pinned by its bytes, which `spikes/tls-and-certificates` holds as `ChainProbe`, and that
    /// on its way to refusing one it downloaded whatever that certificate named as its issuer,
    /// which a test of this fixture measured while a chain engine answered and which is in the
    /// history under its own name.
    ///
    /// **Every check the pin makes has a test here that fails without it**, and so does every type
    /// its filter names, each fed to it by the input that throws it. A certificate a check refuses
    /// is built so that nothing else would: signed for real wherever a signature is not what is
    /// being asked about, so that the shape alone is what is refused.
    /// </remarks>
    [TestFixture]
    public sealed class AnchorPinTests
    {
        /// <summary>
        /// How long the pin may take over a certificate, whichever way it answers. Generous
        /// against a loaded machine and nowhere near the fifteen seconds an unbounded download cost
        /// while a chain engine answered this: 15.0 s for the leaf that names one, 79 ms once the
        /// retrieval was bounded to a millisecond, and nothing fetched at all now.
        /// </summary>
        private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(3);

        private AuthorityIssuer _issuer;
        private CertifiedKey _oursKey;
        private X509Certificate2 _ours;
        private CertifiedKey _foreignKey;
        private X509Certificate2 _foreign;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _oursKey = _issuer.CreateAuthority();
            _ours = _oursKey.Read();
            _foreignKey = _issuer.CreateAuthority();
            _foreign = _foreignKey.Read();
        }

        [TearDown]
        public void TearDown()
        {
            _ours.Reset();
            _foreign.Reset();
        }

        [Test]
        public void Our_own_host_chains_to_us()
        {
            using (var host = _issuer.IssueHost(_oursKey).Read())
            {
                Assert.That(AnchorPin.Chains(host, _ours), Is.True);
            }
        }

        [Test]
        public void A_foreign_host_does_not()
        {
            // It names its issuer in exactly the bytes ours is named in, so the signature is the
            // whole of what refuses it.
            var host = _issuer.IssueHost(_foreignKey);
            Assume.That(host.Certificate.IssuerDN.GetEncoded(), Is.EqualTo(_oursKey.Certificate.SubjectDN.GetEncoded()));

            using (var read = host.Read())
            {
                Assert.That(AnchorPin.Chains(read, _ours), Is.False);
            }
        }

        [Test]
        public void Two_authorities_share_a_subject_name_and_nothing_else()
        {
            // Which is why the pin compares bytes: names would match here.
            Assert.That(_foreign.Subject, Is.EqualTo(_ours.Subject));
            Assert.That(_foreign.RawData, Is.Not.EqualTo(_ours.RawData));
        }

        [Test]
        public void An_authority_under_another_name_is_refused()
        {
            // Before the pin stopped building chains, the chain engine found no issuer for this,
            // tolerated a chain of one, and only the pin's count of two refused it. The issuer's
            // bytes refuse it now.
            using (var leaf = ClientUnder(SomebodyElse()))
            {
                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_leaf_our_key_signed_that_names_us_in_other_bytes_is_refused()
        {
            // Only the holder of our key could make one, and nothing this plugin issues is one:
            // a leaf's issuer is our subject as stored. Refused all the same, because "the very
            // bytes" is the rule the pin keeps, and the rule is what a reader can check.
            var other = new X509Name(
                new[] { X509Name.CN }, new[] { "KeePassGate authority" }, new PrintableNames());
            Assume.That(other.GetEncoded(), Is.Not.EqualTo(_oursKey.Certificate.SubjectDN.GetEncoded()));

            using (var leaf = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .IssuedBy(_oursKey, other)
                       .Read())
            {
                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_leaf_of_ours_written_in_other_bytes_is_refused()
        {
            // The same certificate with its length in one octet more than DER allows. Windows reads
            // it as the same certificate, and Bouncy Castle checks its signature over what it
            // writes back, which is the bytes we signed, so without asking whether the bytes
            // received are those it was answered as ours: a leaf that names us byte for byte
            // and carries our signature, though neither is true of the bytes that arrived.
            var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
            var longer = Reshaped.LongForm(client);

            using (var leaf = new X509Certificate2(longer))
            {
                Assume.That(leaf.RawData, Is.EqualTo(longer), "Windows kept the bytes it was given");
                Assume.That(new BcCertificate(longer).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True,
                    "and Bouncy Castle would call them signed");

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void An_anchor_written_in_other_bytes_vouches_for_nothing()
        {
            using (var anchor = new X509Certificate2(Reshaped.LongForm(_oursKey.CertificateDer)))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assert.That(AnchorPin.Chains(client, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_that_is_no_authority_vouches_for_nothing()
        {
            // A host can sign with its key like anything else, and nothing about it is an
            // authority's: `CA:FALSE`, and a key usage without `keyCertSign`. Either refuses it,
            // which is why each has a test of its own below.
            var host = _issuer.IssueHost(_oursKey);

            using (var anchor = host.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(host).Read())
            {
                Assert.That(AnchorPin.Chains(leaf, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_marked_CA_FALSE_vouches_for_nothing_whatever_its_key_usage_allows()
        {
            var anchor = Minted.Named(AuthoritySubject)
                .With(X509Extensions.BasicConstraints, true, new BasicConstraints(false))
                .With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign))
                .SelfSigned();

            AssertVouchesForNothing(anchor);
        }

        [Test]
        public void An_anchor_with_no_basic_constraints_vouches_for_nothing()
        {
            // Which the chain engine tolerated as an untrusted root.
            var anchor = Minted.Named(AuthoritySubject)
                .With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign))
                .SelfSigned();

            AssertVouchesForNothing(anchor);
        }

        [Test]
        public void An_anchor_not_allowed_to_sign_certificates_vouches_for_nothing()
        {
            var anchor = Minted.Named(AuthoritySubject)
                .With(X509Extensions.BasicConstraints, true, new BasicConstraints(0))
                .With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature))
                .SelfSigned();

            AssertVouchesForNothing(anchor);
        }

        [Test]
        public void An_anchor_with_no_key_usage_is_restricted_by_none()
        {
            // `keyCertSign` is asked for only when there is a key usage to find it in, as RFC 5280
            // section 4.2.1.3 has it: a certificate with none is restricted by none.
            var anchor = Minted.Named(AuthoritySubject)
                .With(X509Extensions.BasicConstraints, true, new BasicConstraints(0))
                .SelfSigned();

            using (var read = anchor.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(anchor).Read())
            {
                Assert.That(AnchorPin.Chains(leaf, read), Is.True);
            }
        }

        [Test]
        public void A_critical_extension_nobody_here_understands_refuses_an_anchor()
        {
            var anchor = Minted.Named(AuthoritySubject)
                .AsAuthority()
                .With(Unknown, true, DerNull.Instance)
                .SelfSigned();

            AssertVouchesForNothing(anchor);
        }

        [Test]
        public void An_anchor_naming_another_issuer_vouches_for_nothing()
        {
            // Signed by its own key, `CA:TRUE` and allowed to sign, and naming somebody else as
            // the issuer, which the chain engine tolerated as a partial chain.
            var made = Minted.Named(AuthoritySubject).AsAuthority().SelfSigned();
            var renamed = Reshaped.SignedAgain(
                made.CertificateDer, made, tbs => Reshaped.WithIssuer(tbs, new X509Name("CN=Somebody Else Entirely")));

            using (var anchor = new X509Certificate2(renamed))
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(made).Read())
            {
                Assume.That(new BcCertificate(renamed).IsSignatureValid(made.Certificate.GetPublicKey()), Is.True,
                    "its own key signed it");

                Assert.That(AnchorPin.Chains(leaf, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_somebody_else_signed_is_the_subordinate_pathLen_0_forbids()
        {
            // Named as ours and as its own issuer, `CA:TRUE` and allowed to sign, and signed by our
            // authority rather than by itself. The chain engine found no issuer for it among the
            // certificates it was given and tolerated it as a partial chain; its own signature is
            // what refuses it now.
            var subordinate = Minted.Named(AuthoritySubject).AsAuthority().IssuedBy(_oursKey);

            using (var anchor = subordinate.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(subordinate).Read())
            {
                Assert.That(AnchorPin.Chains(leaf, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_whose_key_is_not_on_P_256_vouches_for_nothing()
        {
            // An authority on P-384 that signed itself and a leaf with SHA-256, as the plugin
            // signs: every signature here checks, and only the curve is not ours. The leaf is the
            // bare one the pin takes under our own authority, since a `CertifiedKey` holds nothing
            // off P-256 and so `Minted` cannot sign under this one.
            Org.BouncyCastle.Crypto.AsymmetricKeyParameter key;
            var anchor = AuthorityOn(SecObjectIdentifiers.SecP384r1, out key);

            using (var read = new X509Certificate2(anchor.GetEncoded()))
            using (var leaf = BareLeafUnder(anchor, key))
            {
                Assume.That(new BcCertificate(leaf.RawData).IsSignatureValid(anchor.GetPublicKey()), Is.True);

                Assert.That(AnchorPin.Chains(leaf, read), Is.False);
            }
        }

        [Test]
        public void An_anchor_naming_no_curve_is_an_answer_rather_than_an_exception()
        {
            // X9.62 calls a key with NULL in place of its curve implicitly CA, and Bouncy Castle
            // throws `NotImplementedException` reading one, which no filter here could name.
            var implicitly = Reshaped.SignedAgain(
                _oursKey.CertificateDer,
                _oursKey,
                tbs => Reshaped.WithKeyAlgorithm(tbs, new AlgorithmIdentifier(X9ObjectIdentifiers.IdECPublicKey, DerNull.Instance)));

            using (var anchor = new X509Certificate2(implicitly))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assume.That(() => new BcCertificate(implicitly).GetPublicKey(), Throws.InstanceOf<NotImplementedException>());

                Assert.That(AnchorPin.Chains(client, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_whose_key_is_named_as_another_kind_vouches_for_nothing()
        {
            // Our own key, on P-256 as it should be, named as one for elliptic curve key agreement
            // alone, RFC 5480 section 2.1.2, and signed again by itself: the curve is ours and the
            // kind of key is not.
            var agreement = new AlgorithmIdentifier(SecObjectIdentifiers.ecdh, X9ObjectIdentifiers.Prime256v1);
            var crafted = Reshaped.SignedAgain(
                _oursKey.CertificateDer, _oursKey, tbs => Reshaped.WithKeyAlgorithm(tbs, agreement));

            using (var anchor = new X509Certificate2(crafted))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assert.That(AnchorPin.Chains(client, anchor), Is.False);
            }
        }

        [Test]
        public void A_path_length_past_int_is_an_answer_rather_than_an_exception()
        {
            // `GetBasicConstraints` throws `ArithmeticException` for a path length that does not
            // fit an `int`, which a stored authority is free to carry. The pin asks `IsCA` alone,
            // and since it accepts no leaf that is an authority there is nothing beneath for a path
            // length to constrain, so this is an authority like any other.
            var past = new DerSequence(DerBoolean.True, new DerInteger(Org.BouncyCastle.Math.BigInteger.One.ShiftLeft(40)));
            var crafted = Reshaped.SignedAgain(
                _oursKey.CertificateDer,
                _oursKey,
                tbs => Reshaped.WithExtensionValue(tbs, X509Extensions.BasicConstraints, past.GetEncoded()));

            using (var anchor = new X509Certificate2(crafted))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assume.That(() => new BcCertificate(crafted).GetBasicConstraints(), Throws.InstanceOf<ArithmeticException>());

                Assert.That(AnchorPin.Chains(client, anchor), Is.True);
            }
        }

        [Test]
        public void A_critical_extension_nobody_here_understands_refuses_a_leaf_our_key_signed()
        {
            // RFC 5280 section 4.2, as the chain engine kept it: a reader that does not recognize a
            // critical extension refuses the certificate. The same extension not critical is
            // ignored, as it may be.
            using (var critical = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .With(Unknown, true, DerNull.Instance)
                       .IssuedBy(_oursKey)
                       .Read())
            using (var ignorable = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .With(Unknown, false, DerNull.Instance)
                       .IssuedBy(_oursKey)
                       .Read())
            {
                Assert.That(AnchorPin.Chains(critical, _ours), Is.False);
                Assert.That(AnchorPin.Chains(ignorable, _ours), Is.True);
            }
        }

        [Test]
        public void A_leaf_with_no_extensions_at_all_is_answered_by_the_other_checks()
        {
            // No basic constraints, so it is no authority, and nothing marked critical, so nothing
            // unread. It names us and carries our signature, which is everything the pin asks: what
            // it is for is the gate's question, and the gate refuses it for having no usage.
            using (var bare = BareLeafUnder(_oursKey.Certificate, _oursKey.PrivateKey))
            {
                Assume.That(new BcCertificate(bare.RawData).TbsCertificate.Extensions, Is.Null);

                Assert.That(AnchorPin.Chains(bare, _ours), Is.True);
            }
        }

        [Test]
        public void A_leaf_our_key_signed_under_another_digest_is_refused()
        {
            // Bouncy Castle builds its verifier from whatever the certificate names, and every one
            // of these checks against our key: only the algorithm is not the one this plugin signs
            // with, `CA.md` §203.
            // cspell:ignore WITHECDSA WITHPLAIN
            foreach (var digest in new[] { "SHA224WITHECDSA", "SHA384WITHECDSA", "SHA3-256WITHECDSA", "SHA256WITHPLAIN-ECDSA" })
            {
                var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
                var signed = Reshaped.SignedAgain(client, _oursKey, null, digest);

                using (var leaf = new X509Certificate2(signed))
                {
                    Assume.That(new BcCertificate(signed).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True, digest);

                    Assert.That(AnchorPin.Chains(leaf, _ours), Is.False, digest);
                }
            }
        }

        [Test]
        public void An_anchor_that_signed_itself_under_another_digest_vouches_for_nothing()
        {
            // The same key and the same name, so a client we issued carries a signature it checks;
            // only the anchor's own algorithm is not ours.
            var signed = Reshaped.SignedAgain(_oursKey.CertificateDer, _oursKey, null, "SHA224WITHECDSA");

            using (var anchor = new X509Certificate2(signed))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assume.That(new BcCertificate(signed).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True);

                Assert.That(AnchorPin.Chains(client, anchor), Is.False);
            }
        }

        [Test]
        public void A_leaf_naming_an_algorithm_our_key_cannot_check_is_an_answer_rather_than_an_exception()
        {
            // Ed25519, Ed448, ML-DSA and SLH-DSA: the verifier Bouncy Castle builds for each casts
            // the key it is given to its own kind, and an elliptic curve key threw
            // `InvalidCastException` past every filter on the way to the connection's catch-all.
            // Windows reads every one of these.
            foreach (var oid in new[] { "1.3.101.112", "1.3.101.113", "2.16.840.1.101.3.4.3.17", "2.16.840.1.101.3.4.3.20" })
            {
                var named = new AlgorithmIdentifier(new DerObjectIdentifier(oid));
                var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;

                using (var leaf = new X509Certificate2(Reshaped.Naming(client, named, named, _oursKey)))
                {
                    Assert.That(AnchorPin.Chains(leaf, _ours), Is.False, oid);
                }
            }
        }

        [Test]
        public void An_anchor_naming_an_algorithm_its_key_cannot_check_is_an_answer_rather_than_an_exception()
        {
            var named = new AlgorithmIdentifier(new DerObjectIdentifier("1.3.101.112"));

            using (var anchor = new X509Certificate2(Reshaped.Naming(_oursKey.CertificateDer, named, named, _oursKey)))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assert.That(AnchorPin.Chains(client, anchor), Is.False);
            }
        }

        [Test]
        public void An_explicit_NULL_beside_the_signature_algorithm_is_not_ours()
        {
            // Bouncy Castle takes NULL parameters and absent ones as the same, and checks the
            // signature over the signed part, which names them absent: this verifies. The plugin
            // writes them absent, so the outer name alone is what refuses it.
            var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
            var crafted = Reshaped.Naming(client, WithNull, Absent, _oursKey);

            using (var leaf = new X509Certificate2(crafted))
            {
                Assume.That(new BcCertificate(crafted).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True);

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void An_explicit_NULL_beside_the_algorithm_in_the_signed_part_is_not_ours_either()
        {
            // The other way round, and signed over the NULL: the name inside is what refuses it.
            var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
            var crafted = Reshaped.Naming(client, Absent, WithNull, _oursKey);

            using (var leaf = new X509Certificate2(crafted))
            {
                Assume.That(new BcCertificate(crafted).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True);

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_key_of_another_kind_is_an_answer_rather_than_an_exception()
        {
            // RSA on either side, signed with RSA: Bouncy Castle threw for both while the pin let a
            // certificate choose its own verifier, and the algorithm each names refuses it now
            // before any key is read.
            using (var rsa = RsaAuthority())
            using (var ours = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assert.That(AnchorPin.Chains(rsa, _ours), Is.False);
                Assert.That(AnchorPin.Chains(ours, rsa), Is.False);
            }
        }

        [Test]
        public void An_authority_does_not_chain_to_itself_as_a_leaf()
        {
            // The anchor is an authority, and a leaf that is one is refused before anything else
            // about it is asked.
            Assert.That(AnchorPin.Chains(_ours, _ours), Is.False);
        }

        [Test]
        public void The_anchor_with_its_signature_turned_is_no_leaf_of_its_own_either()
        {
            // `(r, n - s)` verifies wherever `(r, s)` does, so this is other bytes our key signed,
            // naming itself: comparing the two certificates' bytes let it through as a leaf.
            var turned = Reshaped.Turned(_oursKey.CertificateDer);

            using (var leaf = new X509Certificate2(turned))
            {
                Assume.That(turned, Is.Not.EqualTo(_oursKey.CertificateDer));
                Assume.That(new BcCertificate(turned).IsSignatureValid(_oursKey.Certificate.GetPublicKey()), Is.True);

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_certificate_Windows_no_longer_holds_is_an_answer_rather_than_an_exception()
        {
            // An empty certificate, and one whose context was let go, which a connection still
            // holding a database's authority while the database locks can be handed: Windows
            // answers `RawData` for either with `CryptographicException`.
            using (var empty = new X509Certificate2())
            using (var released = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                released.Reset();
                Assume.That(() => empty.RawData, Throws.InstanceOf<CryptographicException>());

                Assert.That(AnchorPin.Chains(empty, _ours), Is.False);
                Assert.That(AnchorPin.Chains(released, _ours), Is.False);
                Assert.That(AnchorPin.Chains(_ours, empty), Is.False);
            }
        }

        [Test]
        public void An_anchor_whose_key_is_no_point_is_an_answer_rather_than_an_exception()
        {
            // Nothing reads the anchor's point until its own signature is checked, and Bouncy Castle
            // refuses one that is not on the curve with `ArgumentException` and one not encoded as
            // a point at all with `FormatException`. Windows reads both certificates.
            var offCurve = Reshaped.WithPoint(_oursKey.CertificateDer, _oursKey, point => point[point.Length - 1] ^= 0x01);
            var noPoint = Reshaped.WithPoint(_oursKey.CertificateDer, _oursKey, point => point[0] = 0xC2);

            using (var first = new X509Certificate2(offCurve))
            using (var second = new X509Certificate2(noPoint))
            using (var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assume.That(() => new BcCertificate(offCurve).GetPublicKey(), Throws.InstanceOf<ArgumentException>());
                Assume.That(() => new BcCertificate(noPoint).GetPublicKey(), Throws.InstanceOf<FormatException>());

                Assert.That(AnchorPin.Chains(client, first), Is.False);
                Assert.That(AnchorPin.Chains(client, second), Is.False);
            }
        }

        [Test]
        public void A_signature_that_does_not_end_on_a_whole_byte_is_an_answer_rather_than_an_exception()
        {
            // Still DER, since the bit it leaves unused is clear, and Bouncy Castle reads it back
            // exactly: it throws `IOException` only once it is asked for the octets to verify.
            var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
            var padded = Reshaped.SignaturePadded(client);

            using (var leaf = new X509Certificate2(padded))
            {
                Assume.That(new BcCertificate(padded).GetEncoded(), Is.EqualTo(padded));

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_basic_constraints_value_Bouncy_Castle_cannot_parse_is_an_answer_rather_than_an_exception()
        {
            // Windows reads an extension's value only when asked for it, and Bouncy Castle parses
            // this one as it reads the certificate, refusing it with a `GeneralSecurityException`.
            var client = _issuer.IssueClient(_oursKey, "agent-fictitious", null).CertificateDer;
            var crafted = Reshaped.SignedAgain(
                client, _oursKey, tbs => Reshaped.WithExtensionValue(tbs, X509Extensions.BasicConstraints, new byte[] { 0x01, 0x02 }));

            using (var leaf = new X509Certificate2(crafted))
            {
                Assume.That(() => new BcCertificate(crafted), Throws.InstanceOf<GeneralSecurityException>());

                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void An_expired_leaf_still_belongs_to_its_authority()
        {
            // A client certificate issued eleven years ago, under an authority made the same day
            // and still current: its one year is long gone, and the authority is still the one that
            // signed it. The dates are `CertificateValidity`'s to judge, and the pin asks nothing of
            // them. A host is no good for this any more, since it ends with its authority, a century
            // on.
            var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(4000);
            var oldAuthorityKey = new AuthorityIssuer(() => longAgo).CreateAuthority();
            using (var oldAuthority = oldAuthorityKey.Read())
            using (var oldClient = new AuthorityIssuer(() => longAgo).IssueClient(oldAuthorityKey, "agent-fictitious", null).Read())
            {
                Assume.That(CertificateValidity.HasExpired(oldClient, DateTimeOffset.UtcNow), Is.True);
                Assert.That(AnchorPin.Chains(oldClient, oldAuthority), Is.True);
            }
        }

        [Test]
        public void Nothing_chains_to_nothing()
        {
            using (var host = _issuer.IssueHost(_oursKey).Read())
            {
                Assert.That(AnchorPin.Chains(null, _ours), Is.False);
                Assert.That(AnchorPin.Chains(host, null), Is.False);
                Assert.That(AnchorPin.Chains(null, null), Is.False);
            }
        }

        /// <summary>
        /// `CA.md` §214: a leaf whose issuer the chain engine did not hold sent it looking for one,
        /// at the address the leaf named, and the certificate arrives from whoever opened the
        /// connection. Nothing is fetched now, from a foreign leaf or from our own.
        /// </summary>
        [Test]
        public void A_leaf_that_names_a_download_is_answered_without_anything_reaching_the_address()
        {
            using (var silent = new SilentHttpAddress())
            using (var foreign = ClientNaming(_foreignKey, silent.CaIssuers))
            using (var ours = ClientNaming(_oursKey, silent.CaIssuers))
            {
                var elapsed = Stopwatch.StartNew();
                var foreignChains = AnchorPin.Chains(foreign, _ours);
                var oursChains = AnchorPin.Chains(ours, _ours);
                elapsed.Stop();

                Assert.That(foreignChains, Is.False, "a leaf from another authority is not ours whatever address it names.");
                Assert.That(oursChains, Is.True, "and one of ours is ours whatever address it names.");
                Assert.That(elapsed.Elapsed, Is.LessThan(Promptly), elapsed.ElapsedMilliseconds + " ms");

                // A request the engine opened arrived while it gave up, so a quiet moment is given
                // for one to land before nothing is believed to have.
                Thread.Sleep(Quiet);
                Assert.That(silent.Arrivals, Is.Zero, "nothing reaches " + silent.CaIssuers);
            }
        }

        /// <summary>How long a request is given to arrive before none is believed to have been made.</summary>
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// A certificate the pin should accept is accepted at once, because the anchor is handed in
        /// rather than looked up.
        /// </summary>
        [Test]
        public void Our_own_client_still_chains_and_does_so_at_once()
        {
            using (var client = _issuer.IssueClient(_oursKey, "measured", new[] { "reader" }).Read())
            {
                var elapsed = Stopwatch.StartNew();
                var chains = AnchorPin.Chains(client, _ours);
                elapsed.Stop();

                Assert.That(chains, Is.True);
                Assert.That(elapsed.Elapsed, Is.LessThan(Promptly), elapsed.ElapsedMilliseconds + " ms");
            }
        }

        /// <summary>The subject every authority this plugin makes carries, as `X509Name` parses it.</summary>
        private const string AuthoritySubject = "CN=KeePassGate authority";

        /// <summary>An extension nobody here understands.</summary>
        private static readonly DerObjectIdentifier Unknown = new DerObjectIdentifier("2.25.1");

        /// <summary>ecdsa-with-SHA256 with its parameters absent, as the plugin names it.</summary>
        private static readonly AlgorithmIdentifier Absent = new AlgorithmIdentifier(X9ObjectIdentifiers.ECDsaWithSha256);

        /// <summary>The same with an explicit NULL for its parameters.</summary>
        private static readonly AlgorithmIdentifier WithNull =
            new AlgorithmIdentifier(X9ObjectIdentifiers.ECDsaWithSha256, DerNull.Instance);

        /// <summary>
        /// Asserts that a leaf <paramref name="anchor"/> signed, naming it in its own bytes and
        /// asking for nothing it would refuse, does not chain to it.
        /// </summary>
        private static void AssertVouchesForNothing(CertifiedKey anchor)
        {
            using (var read = anchor.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(anchor).Read())
            {
                Assume.That(new BcCertificate(leaf.RawData).IsSignatureValid(anchor.Certificate.GetPublicKey()), Is.True,
                    "the anchor's key signed the leaf");
                Assume.That(new BcCertificate(read.RawData).IsSignatureValid(anchor.Certificate.GetPublicKey()), Is.True,
                    "and the anchor");

                Assert.That(AnchorPin.Chains(leaf, read), Is.False);
            }
        }

        /// <summary>A self-signed authority under a name that is not ours.</summary>
        private static CertifiedKey SomebodyElse()
        {
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=Somebody Else Entirely").AsAuthority().Lasting(now.AddMinutes(-5), now.AddYears(1)).SelfSigned();
        }

        /// <summary>A certificate for client authentication, signed by the authority given.</summary>
        private static X509Certificate2 ClientUnder(CertifiedKey authority)
        {
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=stranger")
                .ForUsage(AuthorityIssuer.ClientAuthOid)
                .Lasting(now.AddMinutes(-5), now.AddMonths(1))
                .IssuedBy(authority)
                .Read();
        }

        /// <summary>
        /// An authority shaped as ours in every way but its curve, signed by itself with SHA-256 as
        /// the plugin signs, and the key it signs with.
        /// </summary>
        private static BcCertificate AuthorityOn(
            DerObjectIdentifier curve, out Org.BouncyCastle.Crypto.AsymmetricKeyParameter key)
        {
            var keys = new Org.BouncyCastle.Crypto.Generators.ECKeyPairGenerator();
            keys.Init(new Org.BouncyCastle.Crypto.Parameters.ECKeyGenerationParameters(curve, new SecureRandom()));
            var pair = keys.GenerateKeyPair();

            var name = new X509Name(AuthoritySubject);
            var generator = Generator(name, name, pair.Public);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign));

            key = pair.Private;
            return generator.Generate(
                new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory(Reshaped.Ours, pair.Private, new SecureRandom()));
        }

        /// <summary>
        /// A leaf under the authority given, signed with its key, that carries no extensions at
        /// all, which nothing this plugin or `Minted` makes.
        /// </summary>
        private static X509Certificate2 BareLeafUnder(
            BcCertificate authority, Org.BouncyCastle.Crypto.AsymmetricKeyParameter signer)
        {
            var keys = new Org.BouncyCastle.Crypto.Generators.ECKeyPairGenerator();
            keys.Init(new Org.BouncyCastle.Crypto.Parameters.ECKeyGenerationParameters(
                X9ObjectIdentifiers.Prime256v1, new SecureRandom()));

            var generator = Generator(authority.SubjectDN, new X509Name("CN=stranger"), keys.GenerateKeyPair().Public);
            var certificate = generator.Generate(
                new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory(Reshaped.Ours, signer, new SecureRandom()));
            return new X509Certificate2(certificate.GetEncoded());
        }

        private static Org.BouncyCastle.X509.X509V3CertificateGenerator Generator(
            X509Name issuer, X509Name subject, Org.BouncyCastle.Crypto.AsymmetricKeyParameter key)
        {
            var now = DateTimeOffset.UtcNow;
            var generator = new Org.BouncyCastle.X509.X509V3CertificateGenerator();
            generator.SetSerialNumber(new Org.BouncyCastle.Math.BigInteger(1, AuthorityIssuer.NewSerialNumber()));
            generator.SetIssuerDN(issuer);
            generator.SetSubjectDN(subject);
            generator.SetNotBefore((now - AuthorityIssuer.Backdate).UtcDateTime);
            generator.SetNotAfter(now.AddDays(30).UtcDateTime);
            generator.SetPublicKey(key);
            return generator;
        }

        /// <summary>A self-signed authority on an RSA key, which this plugin never makes.</summary>
        private static X509Certificate2 RsaAuthority()
        {
            var keys = new Org.BouncyCastle.Crypto.Generators.RsaKeyPairGenerator();
            keys.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 2048));
            var pair = keys.GenerateKeyPair();

            var name = new X509Name(AuthoritySubject);
            var generator = Generator(name, name, pair.Public);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));

            // cspell:ignore WITHRSA
            var certificate = generator.Generate(
                new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory("SHA256WITHRSA", pair.Private));
            return new X509Certificate2(certificate.GetEncoded());
        }

        /// <summary>Every value in a name as a PrintableString, which is how Windows wrote this plugin's names.</summary>
        private sealed class PrintableNames : X509NameEntryConverter
        {
            public override Asn1Object GetConvertedValue(DerObjectIdentifier oid, string value)
            {
                return new DerPrintableString(value);
            }
        }

        /// <summary>
        /// A client certificate under <paramref name="authority"/> carrying an authority
        /// information access extension naming one `caIssuers` location, which `AuthorityIssuer`
        /// never writes and a stranger is free to.
        /// </summary>
        /// <remarks>
        /// RFC 5280 section 4.2.2.1. https://www.rfc-editor.org/rfc/rfc5280#section-4.2.2.1
        /// </remarks>
        private static X509Certificate2 ClientNaming(CertifiedKey authority, string caIssuers)
        {
            // cspell:ignore ADCA
            var location = new GeneralName(GeneralName.UniformResourceIdentifier, caIssuers);
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=stranger")
                .ForUsage(AuthorityIssuer.ClientAuthOid)
                .With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature))
                .With(
                    X509Extensions.AuthorityInfoAccess,
                    false,
                    new AuthorityInformationAccess(new AccessDescription(AccessDescription.IdADCAIssuers, location)))
                .Lasting(now - AuthorityIssuer.Backdate, now + Lifetime.Unasked)
                .IssuedBy(authority)
                .Read();
        }

        /// <summary>
        /// A loopback address that accepts a connection and then answers nothing, which is the
        /// shape of a download a stranger names: reachable enough to hold whoever asked, and never
        /// finished. Loopback and a port the operating system picks, so the test needs no network
        /// and no name resolution, and a fresh path each time, because Windows caches what it has
        /// already failed to fetch.
        /// </summary>
        private sealed class SilentHttpAddress : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _accepting;
            private readonly List<TcpClient> _held = new List<TcpClient>();
            private int _arrivals;

            public SilentHttpAddress()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();

                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                CaIssuers = "http://127.0.0.1:" + port + "/" + Guid.NewGuid().ToString("N") + ".cer";

                _accepting = new Thread(Accept) { IsBackground = true, Name = "SilentHttpAddress" };
                _accepting.Start();
            }

            /// <summary>The URL a certificate can point at.</summary>
            public string CaIssuers { get; private set; }

            /// <summary>How many connections arrived, which the failure message reports.</summary>
            public int Arrivals
            {
                get { return Interlocked.CompareExchange(ref _arrivals, 0, 0); }
            }

            public void Dispose()
            {
                _listener.Stop();

                lock (_held)
                {
                    foreach (var client in _held)
                    {
                        client.Close();
                    }

                    _held.Clear();
                }

                _accepting.Join(TimeSpan.FromSeconds(5));
            }

            private void Accept()
            {
                try
                {
                    while (true)
                    {
                        var client = _listener.AcceptTcpClient();
                        Interlocked.Increment(ref _arrivals);

                        // Held rather than answered, and held rather than closed: a refusal is an
                        // answer of a kind, and the point is an address that gives none.
                        lock (_held)
                        {
                            _held.Add(client);
                        }
                    }
                }
                catch (SocketException)
                {
                    // `Stop` on the listener, which is how this thread is asked to end.
                }
                catch (ObjectDisposedException)
                {
                    // The same request to end, met by a socket `Stop` had already closed.
                }
                catch (InvalidOperationException)
                {
                    // And again, when `Stop` came between two accepts and the next one found the
                    // listener stopped.
                }
            }
        }
    }
}
