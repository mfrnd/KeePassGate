using System;
using System.Linq;
using System.Security.Cryptography;
using KeePassGate.Gate;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The name a connection carries. It is the only thing an attacker can send before any TLS
    /// work, so what it refuses matters more than what it accepts.
    /// </summary>
    [TestFixture]
    public sealed class SniNameTests
    {
        private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] OtherKey = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] Nonce = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

        [Test]
        public void A_name_is_two_hex_labels_and_a_suffix()
        {
            var name = SniName.For(Key, Nonce);

            var labels = name.Split('.');
            Assert.That(labels.Length, Is.EqualTo(3));
            Assert.That(labels[0], Does.Match("^[0-9a-f]{32}$"));
            Assert.That(labels[1], Does.Match("^[0-9a-f]{32}$"));
            Assert.That(labels[2], Is.EqualTo(SniName.Suffix));
        }

        [Test]
        public void Every_label_fits_what_a_name_allows()
        {
            // 64 hex characters would not: the limit on one label is 63 octets, which is why there
            // are two of them.
            foreach (var label in SniName.For(Key).Split('.'))
            {
                Assert.That(label.Length, Is.LessThanOrEqualTo(63));
            }
        }

        [Test]
        public void The_same_nonce_and_key_always_give_the_same_name()
        {
            Assert.That(SniName.For(Key, Nonce), Is.EqualTo(SniName.For(Key, Nonce)));
        }

        [Test]
        public void A_different_key_gives_a_different_tag_for_the_same_nonce()
        {
            var ours = SniName.For(Key, Nonce);
            var theirs = SniName.For(OtherKey, Nonce);

            Assert.That(theirs.Split('.')[0], Is.EqualTo(ours.Split('.')[0]), "the nonce is the same");
            Assert.That(theirs.Split('.')[1], Is.Not.EqualTo(ours.Split('.')[1]), "and the tag is not");
        }

        [Test]
        public void Each_connection_gets_a_name_of_its_own()
        {
            var names = Enumerable.Range(0, 20).Select(i => SniName.For(Key)).ToList();

            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count),
                "A name that repeated would be a stable identifier, which is the thing this avoids.");
        }

        [Test]
        public void A_name_we_built_matches_the_key_it_was_built_from()
        {
            Assert.That(SniName.Matches(SniName.For(Key), Key), Is.True);
        }

        [Test]
        public void A_name_built_from_another_key_does_not_match()
        {
            Assert.That(SniName.Matches(SniName.For(OtherKey), Key), Is.False);
        }

        [Test]
        public void A_tag_that_is_one_bit_out_does_not_match()
        {
            var name = SniName.For(Key, Nonce);
            var labels = name.Split('.');
            var tampered = labels[1].ToCharArray();
            tampered[0] = tampered[0] == '0' ? '1' : '0';

            Assert.That(SniName.Matches(labels[0] + "." + new string(tampered) + "." + labels[2], Key), Is.False);
        }

        [Test]
        public void A_nonce_that_is_one_bit_out_does_not_match()
        {
            var labels = SniName.For(Key, Nonce).Split('.');
            var tampered = labels[0].ToCharArray();
            tampered[31] = tampered[31] == 'a' ? 'b' : 'a';

            Assert.That(SniName.Matches(new string(tampered) + "." + labels[1] + "." + labels[2], Key), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("kpg")]
        [TestCase("something.kpg")]
        [TestCase("one.two.three.kpg")]
        [TestCase("localhost")]
        [TestCase("....")]
        public void Anything_not_shaped_like_a_name_is_refused(string name)
        {
            byte[] nonce;
            byte[] tag;

            Assert.That(SniName.TryParse(name, out nonce, out tag), Is.False);
            Assert.That(SniName.Matches(name, Key), Is.False);
        }

        [Test]
        public void A_name_whose_nonce_and_tag_share_one_label_is_refused()
        {
            // What a client that forgot the 63 octet label limit would send: the right nonce and
            // the right tag as one label of 64 characters. Made from a real name, so what refuses
            // it is the shape and not the tag, and a parser that took it would be taking a second
            // spelling of a valid name.
            var name = SniName.For(Key, Nonce);
            var merged = name.Remove(name.IndexOf('.'), 1);

            byte[] nonce;
            byte[] tag;

            Assert.That(SniName.TryParse(merged, out nonce, out tag), Is.False);
            Assert.That(SniName.Matches(merged, Key), Is.False);
        }

        [Test]
        public void A_name_with_the_wrong_suffix_is_refused()
        {
            var labels = SniName.For(Key, Nonce).Split('.');

            Assert.That(SniName.Matches(labels[0] + "." + labels[1] + ".com", Key), Is.False);
        }

        [Test]
        public void Upper_case_hex_is_refused_rather_than_accepted_as_a_second_spelling()
        {
            var name = SniName.For(Key, Nonce);

            Assert.That(SniName.Matches(name.ToUpperInvariant().Replace(".KPG", ".kpg"), Key), Is.False);
        }

        [Test]
        public void Labels_of_the_wrong_length_are_refused()
        {
            var labels = SniName.For(Key, Nonce).Split('.');

            Assert.That(SniName.Matches(labels[0].Substring(1) + "." + labels[1] + ".kpg", Key), Is.False);
            Assert.That(SniName.Matches(labels[0] + "." + labels[1] + "ab.kpg", Key), Is.False);
        }

        [Test]
        public void A_parsed_name_gives_back_the_nonce_it_was_built_from()
        {
            byte[] nonce;
            byte[] tag;

            Assert.That(SniName.TryParse(SniName.For(Key, Nonce), out nonce, out tag), Is.True);
            Assert.That(nonce, Is.EqualTo(Nonce));
            Assert.That(tag, Is.EqualTo(SniName.Tag(Key, Nonce)));
        }

        [Test]
        public void The_tag_is_the_first_half_of_the_hmac()
        {
            using (var hmac = new HMACSHA256(Key))
            {
                var full = hmac.ComputeHash(Nonce);

                Assert.That(SniName.Tag(Key, Nonce), Is.EqualTo(full.Take(SniName.TagLength).ToArray()));
            }
        }

        [Test]
        public void A_missing_key_matches_nothing()
        {
            Assert.That(SniName.Matches(SniName.For(Key), null), Is.False);
        }

        [Test]
        public void Building_without_the_parts_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => SniName.For(null, Nonce));
            Assert.Throws<ArgumentNullException>(() => SniName.For(Key, null));
            Assert.Throws<ArgumentException>(() => SniName.For(Key, new byte[8]));
        }
    }

    [TestFixture]
    public sealed class FixedTimeTests
    {
        [Test]
        public void Equal_arrays_are_equal()
        {
            Assert.That(FixedTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }), Is.True);
        }

        [Test]
        public void Anything_else_is_not()
        {
            Assert.That(FixedTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }), Is.False);
            Assert.That(FixedTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }), Is.False);
            Assert.That(FixedTime.Equals(Array.Empty<byte>(), Array.Empty<byte>()), Is.True);
            Assert.That(FixedTime.Equals((byte[])null, Array.Empty<byte>()), Is.False);
            Assert.That(FixedTime.Equals(Array.Empty<byte>(), (byte[])null), Is.False);
            Assert.That(FixedTime.Equals((byte[])null, (byte[])null), Is.False);
        }

        [Test]
        public void Text_is_compared_the_same_way_and_stays_case_sensitive()
        {
            Assert.That(FixedTime.Equals("abc", "abc"), Is.True);
            Assert.That(FixedTime.Equals("abc", "abd"), Is.False);
            Assert.That(FixedTime.Equals("abc", "ABC"), Is.False);
            Assert.That(FixedTime.Equals("abc", "ab"), Is.False);
            Assert.That(FixedTime.Equals((string)null, "abc"), Is.False);
        }
    }
}
