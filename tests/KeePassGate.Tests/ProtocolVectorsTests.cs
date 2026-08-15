using System;
using System.Linq;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The eight values `tests/pykeepassgate/test_protocol.py` holds the client to, held to the
    /// plugin here over the same inputs.
    /// </summary>
    /// <remarks>
    /// The client's copy pins its own code against output this plugin produced once, which catches
    /// the client drifting and nothing else. A change to how the plugin writes a token, builds a
    /// name or computes a proof passes every property test beside this one and would first fail at
    /// a real pairing, where it would look like a fault in the client. These are the other half.
    /// A value that has to change changes in both files, and is taken from the plugin.
    ///
    /// The inputs are the Python file's and deliberately boring: 0 to 31 for a token, 100 to 115
    /// for a nonce, 200 to 231 for a challenge, 1 to 32 for a binding, and 1 to 10 for a
    /// certificate to bind to.
    /// </remarks>
    [TestFixture]
    public sealed class ProtocolVectorsTests
    {
        private static readonly byte[] Token = Run(0, 32);

        private static readonly byte[] Nonce = Run(100, 16);

        private static readonly byte[] Challenge = Run(200, 32);

        private static readonly byte[] Binding = Run(1, 32);

        [Test]
        public void A_token_is_written_the_way_the_client_reads_it()
        {
            Assert.That(PairingToken.Encode(Token), Is.EqualTo("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"));
        }

        [Test]
        public void The_name_is_the_one_the_client_builds()
        {
            Assert.That(
                SniName.For(Token, Nonce),
                Is.EqualTo("6465666768696a6b6c6d6e6f70717273.82b166a6ea164ce7bde4cb0bbec5b7e5.kpg"));
        }

        [Test]
        public void The_tag_is_the_one_the_client_computes()
        {
            Assert.That(SniName.Tag(Token, Nonce), Is.EqualTo(Convert.FromBase64String("grFmpuoWTOe95MsLvsW35Q==")));
        }

        [Test]
        public void The_binding_is_the_one_the_client_computes()
        {
            Assert.That(
                PairingProof.Binding(Run(1, 10)),
                Is.EqualTo(Convert.FromBase64String("yEjhAT+fBKnWP6Q85/1K8DUVLHxmmkpAS2cQfO5fLk4=")));
        }

        [Test]
        public void The_client_proof_is_the_one_the_client_sends()
        {
            Assert.That(
                PairingProof.Client(Token, Binding, Nonce),
                Is.EqualTo(Convert.FromBase64String("OhAiWUjmxuLkeL50QLEoOk12eI9H9ZDV4/ljctZr1b0=")));
        }

        [Test]
        public void The_server_proof_is_the_one_the_client_expects()
        {
            Assert.That(
                PairingProof.Server(Token, Binding, Nonce, Challenge),
                Is.EqualTo(Convert.FromBase64String("bJ23EO4jD3y4swTo2uwdsO1Equ0o3VKTmQqVT0pamME=")));
        }

        [Test]
        public void What_a_request_commits_to_is_what_the_client_commits_to()
        {
            Assert.That(
                PairingProof.Asked("agent-invented", new[] { "reader", "writer" }),
                Is.EqualTo(Convert.FromBase64String("YWdlbnQtaW52ZW50ZWQKcmVhZGVyLHdyaXRlcg==")));
        }

        [Test]
        public void The_request_proof_is_the_one_the_client_sends()
        {
            var asked = PairingProof.Asked("agent-invented", new[] { "reader", "writer" });

            Assert.That(
                PairingProof.Request(Token, Binding, Nonce, Challenge, asked),
                Is.EqualTo(Convert.FromBase64String("ImkXm/MSrGCEpJO2/JJ5whKI8/NQIq8vyFdum6EVNvo=")));
        }

        private static byte[] Run(int first, int count)
        {
            return Enumerable.Range(first, count).Select(i => (byte)i).ToArray();
        }
    }
}
