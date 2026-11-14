using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using KeePassGate.Bridge;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The reader that hands a string's value out a piece at a time from where KeePass keeps
    /// it, against real `ProtectedString` objects: what it hands out, what it leaves behind,
    /// and what it reads through KeePass's own read.
    /// </summary>
    /// <remarks>
    /// A field and an attachment are read alike, the operator's ruling of 2026-11-14, and
    /// differ in nothing but being protected or not. So what is held here is that a value
    /// KeePass keeps as a string is handed out as that string, with nothing copied; that a
    /// value KeePass keeps as bytes is handed out in pieces that join to the string those
    /// bytes decode to whole, wherever a piece ends and whether or not the bytes are UTF-8;
    /// that such a value is never read as a string by KeePass, and never read whole by
    /// KeePass at all where its bytes can be read in place; and that a value whose bytes are
    /// kept any other way is read through KeePass. `KeePassContractTests` pins what the
    /// reader rests on in KeePass.
    ///
    /// Some cases set `ProtectedBinary.ExtCrypt`, a static of KeePass's that another plugin can
    /// set, and each takes it away again whatever happens.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class StoredStringsTests
    {
        private const int Step = StoredStrings.Step;
        private const int Piece = StoredBinaries.Piece;

        /// <summary>
        /// Text of one, two, three and four bytes a character, at lengths around a step of
        /// the decoder and around a piece of the bytes, so that a step and a piece each end
        /// inside a character and between the halves of a pair.
        /// </summary>
        private static IEnumerable<string> Values()
        {
            const string Pair = "\U0001F511";
            yield return "a";
            yield return "not-a-real-password";
            yield return "quoted \" and \\ and \u0001 and \u2028 and a line\r\nafter it";
            yield return new string('a', Step - 1);
            yield return new string('a', Step);
            yield return new string('a', Step + 1);
            yield return new string('\u00E9', Step);
            yield return new string('\u20AC', Step + 5);
            yield return new string('a', Step - 1) + "\u20AC" + new string('b', Step);
            yield return new string('a', Step - 2) + Pair + new string('b', Step);
            yield return new string('a', Piece - 1) + Pair + new string('b', 7);
            yield return new string('a', Piece - 3) + Pair + Pair;
            yield return "a" + string.Concat(Enumerable.Repeat(Pair, Piece / 2));
            yield return new string('\u20AC', (2 * Piece) + 1);
        }

        /// <summary>
        /// Stored bytes that are not UTF-8, which a file another program wrote can hold: a
        /// sequence cut short, a byte that starts nothing, a surrogate encoded, each alone,
        /// at the very end, and across the end of a step and of a piece.
        /// </summary>
        private static IEnumerable<byte[]> Malformed()
        {
            yield return new byte[] { 0x61, 0xFF, 0x62 };
            yield return new byte[] { 0x61, 0xE2, 0x82 };
            yield return new byte[] { 0xC0, 0x80 };
            yield return new byte[] { 0xED, 0xA0, 0x80, 0x61 };
            yield return new byte[] { 0xF4, 0x90, 0x80, 0x80 };
            yield return Enumerable.Repeat((byte)0x61, Step - 1).Concat(new byte[] { 0xE2, 0x82, 0x61, 0x62 }).ToArray();
            yield return Enumerable.Repeat((byte)0x61, Step - 2).Concat(new byte[] { 0xF0, 0x9F, 0x98, 0x41 }).ToArray();
            yield return Enumerable.Repeat((byte)0x61, Piece - 1).Concat(new byte[] { 0xE2, 0x82, 0x61, 0x62 }).ToArray();
            yield return Enumerable.Repeat((byte)0x61, Piece - 1).Concat(new byte[] { 0xF0, 0x9F }).ToArray();
            yield return Enumerable.Repeat((byte)0x61, Piece + 3).Concat(new byte[] { 0xE2, 0x82 }).ToArray();
            yield return Enumerable.Repeat((byte)0xFF, (2 * Step) + 3).ToArray();
        }

        [Test]
        public void A_step_of_the_decoder_stays_under_what_the_runtime_keeps_apart_for_large_objects()
        {
            // The text of a step and the string made of it: two bytes a character, and a
            // character at most for every byte.
            Assert.That(Step, Is.EqualTo(16 * 1024));
            Assert.That(2 * (Encoding.UTF8.GetMaxCharCount(Step) + 16), Is.LessThan(85000));
        }

        [Test]
        public void A_value_KeePass_keeps_as_a_string_is_handed_out_as_that_string()
        {
            // Held in the open, made from a string whatever its flag, or read as a string by
            // KeePass since: KeePass keeps the string, and the reader hands out that object,
            // whole, in a single piece, however long it is. Nothing is copied.
            var text = new string('\u20AC', (3 * Step) + 1);
            var read = new ProtectedString(true, Encoding.UTF8.GetBytes(text));
            read.ReadString();

            var values = new Dictionary<string, ProtectedString>
            {
                { "in the open, from a string", new ProtectedString(false, text) },
                { "in the open, from bytes", new ProtectedString(false, Encoding.UTF8.GetBytes(text)) },
                { "protected, from a string", new ProtectedString(true, text) },
                { "protected, read as a string by KeePass", read },
            };

            foreach (var value in values)
            {
                var kept = value.Value.ReadString();
                var pieces = Pieces(value.Value);

                Assert.That(pieces, Has.Count.EqualTo(1), value.Key);
                Assert.That(pieces[0], Is.SameAs(kept), value.Key + ": a copy was handed out");
                Assert.That(StoredStrings.Read(value.Value), Is.SameAs(kept), value.Key + ": read whole");
            }
        }

        [Test]
        public void Reading_a_value_KeePass_keeps_as_a_string_allocates_next_to_nothing()
        {
            var value = new ProtectedString(false, new string('a', 4 * 1024 * 1024));
            long handed = 0;
            Action<string> counting = piece => handed += piece.Length;

            StoredStrings.Read(new ProtectedString(false, "a"), counting);
            handed = 0;

            var before = GC.GetAllocatedBytesForCurrentThread();
            StoredStrings.Read(value, counting);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(handed, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(allocated, Is.LessThan(4 * 1024), "reading a value held as a string copied something");
        }

        [Test]
        public void A_value_KeePass_keeps_as_bytes_is_handed_out_in_pieces_that_join_to_it(
            [ValueSource(nameof(Values))] string text)
        {
            // A protected value a file was loaded with, and a value written through the gate.
            var value = new ProtectedString(true, Encoding.UTF8.GetBytes(text));
            Assume.That(KeePassMemory.KeepsText(value), Is.False);

            var pieces = Pieces(value);

            Assert.That(string.Concat(pieces) == text, Is.True, "the pieces are other text than was stored");
            Assert.That(pieces, Has.All.Length.GreaterThan(0), "a piece of no characters was handed out");
            Assert.That(pieces, Has.All.Length.LessThanOrEqualTo(Step + 1), "a piece holds more than a step decodes to");
            Assert.That(StoredStrings.Read(value) == text, Is.True, "read whole, the value is other text");

            // No half of a pair ends a piece with its other half in the piece after it: each
            // piece is text on its own, which is what lets each be escaped and encoded alone.
            foreach (var piece in pieces)
            {
                Assert.That(char.IsHighSurrogate(piece[piece.Length - 1]), Is.False, "a piece ends inside a pair");
                Assert.That(char.IsLowSurrogate(piece[0]), Is.False, "a piece starts inside a pair");
            }

            Assert.That(KeePassMemory.KeepsText(value), Is.False, "reading left KeePass the value as a string");
            Assert.That(KeePassMemory.Bytes(value), Is.Not.Null);
        }

        [Test]
        public void Stored_bytes_that_are_not_UTF_8_are_handed_out_as_a_decoding_of_the_whole_replaces_them(
            [ValueSource(nameof(Malformed))] byte[] stored)
        {
            var value = new ProtectedString(true, stored);

            var expected = Encoding.UTF8.GetString(stored);
            Assert.That(string.Concat(Pieces(value)) == expected, Is.True, BitConverter.ToString(stored.Take(8).ToArray()));
            Assert.That(StoredStrings.Read(value) == expected, Is.True);
        }

        [Test]
        public void A_value_of_no_characters_and_a_name_an_entry_does_not_hold_hand_out_nothing()
        {
            var empties = new[]
            {
                null,
                ProtectedString.Empty,
                ProtectedString.EmptyEx,
                new ProtectedString(false, string.Empty),
                new ProtectedString(true, string.Empty),
                new ProtectedString(true, Array.Empty<byte>()),
                new ProtectedString(false, Array.Empty<byte>()),
            };

            foreach (var value in empties)
            {
                Assert.That(Pieces(value), Is.Empty);
                Assert.That(StoredStrings.Read(value), Is.Empty);
            }

            Assert.Throws<ArgumentNullException>(() => StoredStrings.Read(new ProtectedString(false, "a"), null));
        }

        [Test]
        public void Reading_a_protected_value_has_KeePass_read_nothing_and_leaves_its_bytes_encrypted_as_they_were()
        {
            // KeePass's own read of a protected value decrypts its bytes where they stand and
            // encrypts them again through whatever cipher stands by then, so under KeePass's
            // own hook a reader that went through KeePass leaves the bytes kept through the
            // hook, in other bytes than they were. The gate reads a copy of a piece at a time.
            var text = new string('\u20AC', Piece);
            var value = new ProtectedString(true, Encoding.UTF8.GetBytes(text));
            var bytes = KeePassMemory.Bytes(value);
            var held = KeePassMemory.Buffer(bytes);
            var encrypted = (byte[])held.Clone();
            Assume.That(KeePassMemory.KeptAs(bytes), Is.EqualTo("ProtectedMemory"));

            var handed = 0;
            var was = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                Interlocked.Increment(ref handed);
                for (var i = 0; i < data.Length; i++)
                {
                    data[i] ^= 0x5A;
                }
            };

            try
            {
                Assert.That(string.Concat(Pieces(value)) == text, Is.True);
                Assert.That(StoredStrings.Read(value) == text, Is.True);

                Assert.That(Volatile.Read(ref handed), Is.Zero, "KeePass itself read the value");
                Assert.That(KeePassMemory.Bytes(value), Is.SameAs(bytes));
                Assert.That(KeePassMemory.Buffer(bytes), Is.SameAs(held));
                Assert.That(held.SequenceEqual(encrypted), Is.True, "the read left other bytes in KeePass's buffer");
                Assert.That(KeePassMemory.KeptAs(bytes), Is.EqualTo("ProtectedMemory"));
                Assert.That(KeePassMemory.KeepsText(value), Is.False);
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }
        }

        [Test]
        public void A_protected_value_kept_through_another_plugin_s_cipher_is_read_through_KeePass_s_own_read()
        {
            // Made while another plugin's cipher stands, the bytes of a protected value are
            // kept through that cipher, of which the gate knows nothing a piece could be
            // decrypted by. KeePass reads what it keeps that way, so the reader of the bytes
            // has KeePass read them, `StoredBinaries`, and the value is still decoded a step at
            // a time, and still never made a string of by KeePass.
            var decrypted = 0;
            var text = new string('\u20AC', Step + 5);
            var was = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt)
                {
                    Interlocked.Increment(ref decrypted);
                }

                for (var i = 0; i < data.Length; i++)
                {
                    data[i] ^= 0x5A;
                }
            };

            try
            {
                var value = new ProtectedString(true, Encoding.UTF8.GetBytes(text));
                Assume.That(KeePassMemory.KeptAs(KeePassMemory.Bytes(value)), Is.EqualTo("ExtCrypt"));

                var pieces = Pieces(value);

                Assert.That(string.Concat(pieces) == text, Is.True);
                Assert.That(pieces, Has.All.Length.LessThanOrEqualTo(Step + 1));
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "KeePass read the bytes other than a single time");
                Assert.That(KeePassMemory.KeepsText(value), Is.False, "KeePass was asked for the value as a string");
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }
        }

        [Test]
        public void A_value_KeePass_reads_as_a_string_between_two_pieces_is_still_handed_out_whole()
        {
            // KeePass's own `ReadString` moves a value from bytes to a string, on KeePass's
            // thread, while a connection's thread may be part way through the bytes: it keeps
            // the string and lets the bytes go. The bytes the reader found are the value all
            // the same, since KeePass never changes them, so the read goes on over them. And
            // the read after it finds the string.
            var text = new string('\u20AC', Piece);
            var value = new ProtectedString(true, Encoding.UTF8.GetBytes(text));
            var pieces = new List<string>();

            StoredStrings.Read(value, piece =>
            {
                pieces.Add(piece);
                if (pieces.Count == 1)
                {
                    Assert.That(value.ReadString() == text, Is.True);
                    Assert.That(KeePassMemory.Bytes(value), Is.Null, "KeePass kept the bytes of a value it read as a string");
                }
            });

            Assert.That(pieces, Has.Count.GreaterThan(1));
            Assert.That(string.Concat(pieces) == text, Is.True);
            Assert.That(Pieces(value), Is.EqualTo(new[] { value.ReadString() }));
        }

        [Test]
        public void What_takes_the_pieces_throwing_stops_the_read()
        {
            // An answer is written to a peer, and a peer that goes away throws out of the
            // write. The read stops there and what was thrown comes out as it was thrown.
            var text = new string('a', 3 * Step);
            var failure = new IOException("an invented peer that went away");

            foreach (var value in new[] { new ProtectedString(true, Encoding.UTF8.GetBytes(text)), new ProtectedString(false, text) })
            {
                var handed = 0;
                var thrown = Assert.Throws<IOException>(() => StoredStrings.Read(value, piece =>
                {
                    handed++;
                    throw failure;
                }));

                Assert.That(thrown, Is.SameAs(failure));
                Assert.That(handed, Is.EqualTo(1), "a piece was handed out once the read had failed");
                Assert.That(StoredStrings.Read(value) == text, Is.True, "and the read after it hands out the whole");
            }
        }

        [Test]
        public void Where_KeePass_keeps_a_value_is_as_it_was_measured_here()
        {
            // The two fields the reader finds a value in. On a KeePass that keeps a value
            // otherwise, a protected value is read through KeePass's own copy of its bytes,
            // whole, and that is told here before it is met. The plugin asks the same as it
            // loads, and logs the answer.
            Assert.That(StoredStrings.WhyNotInPlace, Is.Null);

            string text;
            ProtectedBinary bytes;
            Assert.That(StoredStrings.Found(new ProtectedString(false, "a"), out text, out bytes), Is.True);
            Assert.That(text, Is.EqualTo("a"));
            Assert.That(bytes, Is.Null);

            var value = new ProtectedString(true, Encoding.UTF8.GetBytes("not-a-real-password"));
            Assert.That(StoredStrings.Found(value, out text, out bytes), Is.True);
            Assert.That(text, Is.Null);
            Assert.That(bytes, Is.SameAs(KeePassMemory.Bytes(value)));
        }

        /// <summary>Every piece the reader hands out of a value, in the order they came.</summary>
        private static List<string> Pieces(ProtectedString value)
        {
            var pieces = new List<string>();
            StoredStrings.Read(value, pieces.Add);
            return pieces;
        }
    }
}
