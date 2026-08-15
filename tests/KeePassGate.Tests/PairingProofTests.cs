using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using KeePassGate.Pairing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The three proofs, and the properties that make them worth exchanging.
    /// </summary>
    /// <remarks>
    /// The interesting tests here are the ones that fail if somebody simplifies the construction:
    /// the labels, which stop one proof being replayed as another, and the length prefixes, which
    /// stop the parts being shuffled across the boundaries between them. Neither shows up in a test
    /// that only checks a proof verifies.
    /// </remarks>
    [TestFixture]
    public sealed class PairingProofTests
    {
        private byte[] _token;
        private byte[] _binding;
        private byte[] _nonce;
        private byte[] _challenge;

        [SetUp]
        public void SetUp()
        {
            _token = PairingToken.New();
            _binding = Bytes(32, 1);
            _nonce = Bytes(16, 2);
            _challenge = Bytes(32, 3);
        }

        [Test]
        public void Every_proof_is_a_HMAC_SHA256_and_says_so_by_its_length()
        {
            Assert.That(PairingProof.Client(_token, _binding, _nonce).Length, Is.EqualTo(PairingProof.Length));
            Assert.That(PairingProof.Server(_token, _binding, _nonce, _challenge).Length,
                Is.EqualTo(PairingProof.Length));
            Assert.That(PairingProof.Request(_token, _binding, _nonce, _challenge, new byte[4]).Length,
                Is.EqualTo(PairingProof.Length));
        }

        [Test]
        public void The_same_inputs_always_give_the_same_proof()
        {
            Assert.That(
                PairingProof.Client(_token, _binding, _nonce),
                Is.EqualTo(PairingProof.Client(_token, _binding, _nonce)));
        }

        [Test]
        public void The_client_proof_and_the_server_proof_are_never_the_same_value()
        {
            // They are computed over the same token and the same connection, so only the label
            // separates them. Without one, a client could replay the server's own proof back at it.
            Assert.That(
                PairingProof.Client(_token, _binding, _nonce),
                Is.Not.EqualTo(PairingProof.Server(_token, _binding, _nonce, Array.Empty<byte>())));
        }

        [Test]
        public void A_request_proof_is_not_a_server_proof_over_the_same_bytes()
        {
            Assert.That(
                PairingProof.Server(_token, _binding, _nonce, _challenge),
                Is.Not.EqualTo(PairingProof.Request(_token, _binding, _nonce, _challenge, Array.Empty<byte>())));
        }

        [Test]
        public void Another_token_produces_another_proof()
        {
            Assert.That(
                PairingProof.Client(_token, _binding, _nonce),
                Is.Not.EqualTo(PairingProof.Client(PairingToken.New(), _binding, _nonce)));
        }

        [Test]
        public void Another_certificate_produces_another_proof()
        {
            // This is what stops a machine in the middle: its proof is over its own certificate.
            Assert.That(
                PairingProof.Client(_token, _binding, _nonce),
                Is.Not.EqualTo(PairingProof.Client(_token, Bytes(32, 9), _nonce)));
        }

        [Test]
        public void Another_nonce_produces_another_proof()
        {
            Assert.That(
                PairingProof.Client(_token, _binding, _nonce),
                Is.Not.EqualTo(PairingProof.Client(_token, _binding, Bytes(16, 9))));
        }

        [Test]
        public void Another_challenge_produces_another_proof()
        {
            Assert.That(
                PairingProof.Server(_token, _binding, _nonce, _challenge),
                Is.Not.EqualTo(PairingProof.Server(_token, _binding, _nonce, Bytes(32, 9))));
        }

        [Test]
        public void Parts_cannot_be_shuffled_across_the_boundary_between_them()
        {
            // Without length prefixes these two are the same bytes to sign, and a caller could move
            // a byte from the binding into the nonce and prove something it was never given.
            var left = PairingProof.Client(_token, new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
            var right = PairingProof.Client(_token, new byte[] { 1, 2 }, new byte[] { 3, 4, 5 });

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void The_binding_is_a_SHA256_over_the_certificate_and_nothing_else()
        {
            // RFC 5929 tls-server-end-point, which both sides derive without asking the transport
            // anything. Computed here the long way so the test does not just call the code twice.
            var der = Bytes(200, 7);

            using (var sha256 = SHA256.Create())
            {
                Assert.That(PairingProof.Binding(der), Is.EqualTo(sha256.ComputeHash(der)));
            }
        }

        [Test]
        public void The_binding_changes_completely_when_the_certificate_does()
        {
            var one = Bytes(200, 7);
            var other = (byte[])one.Clone();
            other[100] ^= 0x01;

            Assert.That(PairingProof.Binding(one), Is.Not.EqualTo(PairingProof.Binding(other)));
        }

        [Test]
        public void What_a_request_commits_to_is_the_name_and_the_profiles()
        {
            Assert.That(
                PairingProof.Asked("agent-fictitious", new[] { "ops" }),
                Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("agent-fictitious\nops")));
        }

        [Test]
        public void Asking_for_more_profiles_commits_to_something_else()
        {
            var asked = PairingProof.Asked("agent-fictitious", new[] { "readonly" });
            var more = PairingProof.Asked("agent-fictitious", new[] { "readonly", "admin" });

            Assert.That(
                PairingProof.Request(_token, _binding, _nonce, _challenge, asked),
                Is.Not.EqualTo(PairingProof.Request(_token, _binding, _nonce, _challenge, more)));
        }

        [Test]
        public void Asking_to_be_called_something_else_commits_to_something_else()
        {
            Assert.That(
                PairingProof.Asked("agent-fictitious", Array.Empty<string>()),
                Is.Not.EqualTo(PairingProof.Asked("agent-invented", Array.Empty<string>())));
        }

        [Test]
        public void A_name_and_a_profile_cannot_be_swapped_across_the_separator()
        {
            // The nearest thing to a collision the separator has, and it is not one: the name is
            // followed by a newline whether or not any profiles come after it.
            Assert.That(
                PairingProof.Asked("agent", new[] { "ops" }),
                Is.Not.EqualTo(PairingProof.Asked("agent\nops", Array.Empty<string>())));

            // And that spelling could never arrive anyway, which is the belt to that pair of braces.
            Assert.That(Authority.AuthorityIssuer.IsUsableSubjectName("agent\nops"), Is.False);
        }

        [Test]
        public void Nothing_is_proved_without_a_token()
        {
            Assert.Throws<ArgumentNullException>(() => PairingProof.Client(null, _binding, _nonce));
        }

        [Test]
        public void Nothing_is_proved_over_a_missing_part()
        {
            Assert.Throws<ArgumentException>(() => PairingProof.Client(_token, null, _nonce));
            Assert.Throws<ArgumentException>(() => PairingProof.Client(_token, _binding, null));
        }

        [Test]
        public void There_is_no_binding_without_a_certificate()
        {
            Assert.Throws<ArgumentNullException>(() => PairingProof.Binding(null));
        }

        [Test]
        public void Asking_for_no_profiles_at_all_still_commits_to_the_name()
        {
            Assert.That(
                PairingProof.Asked("agent", Array.Empty<string>()),
                Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("agent\n")));

            Assert.That(
                PairingProof.Asked("agent", Array.Empty<string>()),
                Is.Not.EqualTo(PairingProof.Asked("agent-two", Array.Empty<string>())));
        }

        [Test]
        public void An_empty_profile_list_is_not_the_same_as_two_empty_profiles()
        {
            // One empty profile would collide with none, and cannot arrive: a profile that is not
            // a usable name is refused before anything is proved.
            Assert.That(Authority.AuthorityIssuer.IsUsableProfileName(string.Empty), Is.False);

            Assert.That(
                PairingProof.Asked("agent", Array.Empty<string>()),
                Is.Not.EqualTo(PairingProof.Asked("agent", new List<string> { string.Empty, string.Empty })));
        }

        private static byte[] Bytes(int length, byte seed)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return bytes;
        }
    }
}
