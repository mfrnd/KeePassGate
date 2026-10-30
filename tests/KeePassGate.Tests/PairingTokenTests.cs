using System;
using System.Collections.Generic;
using KeePassGate.Pairing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The token, and how it is written down. A token that does not survive being read out and
    /// typed back in is a pairing that fails for a reason nobody can see.
    /// </summary>
    [TestFixture]
    public sealed class PairingTokenTests
    {
        [Test]
        public void A_new_token_is_the_length_a_gate_key_is()
        {
            Assert.That(PairingToken.New().Length, Is.EqualTo(PairingToken.Length));
        }

        [Test]
        public void Two_tokens_are_never_the_same()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 200; i++)
            {
                Assert.That(seen.Add(PairingToken.Encode(PairingToken.New())), Is.True,
                    "a token repeated, which means it is not random");
            }
        }

        [Test]
        public void It_survives_being_written_down_and_read_back()
        {
            for (var i = 0; i < 100; i++)
            {
                var token = PairingToken.New();

                Assert.That(PairingToken.Decode(PairingToken.Encode(token)), Is.EqualTo(token));
            }
        }

        [Test]
        public void The_written_form_is_the_length_the_client_documentation_will_say()
        {
            Assert.That(PairingToken.Encode(PairingToken.New()).Length, Is.EqualTo(PairingToken.TextLength));
        }

        [Test]
        public void It_carries_nothing_that_a_url_or_a_shell_would_quote()
        {
            for (var i = 0; i < 100; i++)
            {
                foreach (var c in PairingToken.Encode(PairingToken.New()))
                {
                    var plain = Ascii.IsLetterOrDigit(c) || c == '-' || c == '_';

                    Assert.That(plain, Is.True, "a token contained " + c);
                }
            }
        }

        [Test]
        public void A_known_token_writes_the_way_base64url_says_it_should()
        {
            // Thirty-two zero bytes, so the expected text can be worked out by hand rather than by
            // running the code being tested.
            Assert.That(PairingToken.Encode(new byte[32]), Is.EqualTo(new string('A', 43)));
        }

        [Test]
        public void The_last_byte_survives_the_encoding_that_does_not_divide_by_three()
        {
            // Thirty-two is not a multiple of three, so the final block is a partial one and is
            // exactly where an encoder loses a byte.
            var token = new byte[32];
            token[31] = 0xFF;

            Assert.That(PairingToken.Decode(PairingToken.Encode(token)), Is.EqualTo(token));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("too short")]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void Anything_that_is_not_the_right_length_is_refused(string text)
        {
            Assert.That(PairingToken.Decode(text), Is.Null);
        }

        [Test]
        public void A_character_outside_the_alphabet_is_refused()
        {
            var written = PairingToken.Encode(PairingToken.New());
            var broken = "+" + written.Substring(1);

            Assert.That(PairingToken.Decode(broken), Is.Null);
        }

        [Test]
        public void Padding_is_refused_because_this_never_writes_any()
        {
            var written = PairingToken.Encode(PairingToken.New());

            Assert.That(PairingToken.Decode(written.Substring(0, 42) + "="), Is.Null);
        }

        [Test]
        public void A_token_spelled_with_its_spare_bits_set_is_refused()
        {
            // Forty-three characters carry 258 bits for a 256 bit token, so the last character has
            // two the token does not use and three other spellings decode to the same bytes. This
            // writes only one of them, and the client's decoder refuses the other three, so the two
            // decoders agree about every text rather than only about every token.
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var written = PairingToken.Encode(PairingToken.New());
            var last = alphabet.IndexOf(written[42]);

            for (var spare = 1; spare < 4; spare++)
            {
                var respelled = written.Substring(0, 42) + alphabet[last | spare];

                Assert.That(PairingToken.Decode(respelled), Is.Null, respelled);
            }
        }

        [Test]
        public void A_token_is_only_ever_encoded_from_something()
        {
            Assert.Throws<ArgumentNullException>(() => PairingToken.Encode(null));
        }
    }
}
