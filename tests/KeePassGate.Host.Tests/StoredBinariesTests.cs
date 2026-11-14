using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KeePassGate.Bridge;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The reader that hands an attachment out a piece at a time from where KeePass keeps it,
    /// against real `ProtectedBinary` objects: what it hands out, what it leaves behind, and
    /// what it reads through KeePass's own read.
    /// </summary>
    /// <remarks>
    /// An attachment is read through the gate at any size, with no copy of the whole made
    /// wherever it can be read in place, and through KeePass's own read wherever it cannot,
    /// the operator's ruling of 2026-11-14. So what is held here is that the pieces are the
    /// attachment, byte for byte, at every size around a block and around a piece; that an
    /// attachment held in the open is read out of KeePass's own buffer with nothing copied;
    /// that a protected attachment is never decrypted where it stands by a read in place and
    /// leaves no piece of itself in the clear once the read is over; and that an attachment
    /// kept any other way is read through KeePass, in the same pieces, from a copy that is
    /// overwritten. `KeePassContractTests` pins what the read in place rests on in KeePass
    /// and in Windows.
    ///
    /// Some cases set `ProtectedBinary.ExtCrypt`, a static of KeePass's that another plugin can
    /// set, and each takes it away again whatever happens.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class StoredBinariesTests
    {
        private const int Piece = StoredBinaries.Piece;

        /// <summary>The block Windows' protection of process memory works in.</summary>
        private const int Block = 16;

        /// <summary>
        /// Sizes around a block, around a piece and well past several pieces, and no bytes at
        /// all.
        /// </summary>
        private static readonly int[] Sizes =
        {
            0, 1, 15, 16, 17, 47, 48, Piece - 1, Piece, Piece + 1, (3 * Piece) + 5, 2 * 1024 * 1024,
        };

        [Test]
        public void A_piece_ends_where_a_block_and_a_step_of_base64_do()
        {
            // A piece of a protected attachment is decrypted on its own, so it ends where a
            // block ends, and the base64 of a piece is written on its own, so it ends where
            // three bytes do: `JsonWriter.Base64` pads a piece that does not.
            Assert.That(Piece, Is.EqualTo(48 * 1024));
            Assert.That(Piece % Block, Is.Zero);
            Assert.That(Piece % 3, Is.Zero);
        }

        [Test]
        public void The_pieces_joined_are_what_KeePass_hands_out_whole(
            [Values(false, true)] bool isProtected, [ValueSource(nameof(Sizes))] int size)
        {
            var content = Invented(size);
            var binary = new ProtectedBinary(isProtected, content);

            var counts = new List<int>();
            using (var joined = new MemoryStream())
            {
                StoredBinaries.Read(binary, (bytes, offset, count) =>
                {
                    Assert.That(offset, Is.GreaterThanOrEqualTo(0));
                    Assert.That(
                        offset + count,
                        Is.LessThanOrEqualTo(bytes.Length),
                        "a piece runs past the array it came in");
                    counts.Add(count);
                    joined.Write(bytes, offset, count);
                });

                Assert.That(
                    joined.ToArray().SequenceEqual(binary.ReadData()),
                    Is.True,
                    "the pieces are other bytes than KeePass reads");
                Assert.That(
                    joined.ToArray().SequenceEqual(content),
                    Is.True,
                    "the pieces are other bytes than were stored");
            }

            Assert.That(
                counts,
                Has.Count.EqualTo((size + Piece - 1) / Piece),
                "an attachment of no bytes hands out no piece");
            Assert.That(counts, Has.All.GreaterThan(0), "a piece of no bytes was handed out");
            Assert.That(counts, Has.All.LessThanOrEqualTo(Piece), "a piece holds more than a piece");
            Assert.That(
                counts.Take(Math.Max(0, counts.Count - 1)),
                Has.All.EqualTo(Piece),
                "a piece before the last is short");
            Assert.That(counts.Sum(), Is.EqualTo(size));

            // And read again, by the asking after, to the same bytes: reading left the
            // attachment as it was.
            Assert.That(Joined(binary).SequenceEqual(content), Is.True, "a second read handed out other bytes");
            Assert.That(
                binary.ReadData().SequenceEqual(content),
                Is.True,
                "KeePass reads other bytes once the gate has read");
        }

        [Test]
        public void An_attachment_held_in_the_open_is_handed_out_as_KeePass_s_own_buffer(
            [ValueSource(nameof(Sizes))] int size)
        {
            // Nothing is copied: each piece is the buffer KeePass keeps the attachment in, at
            // the offset the piece starts at.
            var binary = new ProtectedBinary(false, Invented(size));
            var held = KeePassMemory.Buffer(binary);
            var at = 0;

            StoredBinaries.Read(binary, (bytes, offset, count) =>
            {
                Assert.That(bytes, Is.SameAs(held), "a piece of an open attachment came in an array of its own");
                Assert.That(offset, Is.EqualTo(at), "a piece is out of its place in the buffer");
                at += count;
            });

            Assert.That(at, Is.EqualTo(size));
        }

        [Test]
        public void Reading_an_attachment_held_in_the_open_allocates_next_to_nothing()
        {
            // What reading out of KeePass's own buffer buys: an answer serving megabytes keeps
            // none of them alive beside the attachment itself. Measured once the reader has
            // found what it reads of KeePass, which it does a single time, and with what takes
            // the pieces made beforehand.
            const int Size = 8 * 1024 * 1024;
            var binary = new ProtectedBinary(false, Invented(Size));
            long handed = 0;
            Action<byte[], int, int> counting = (bytes, offset, count) => handed += count;

            StoredBinaries.Read(new ProtectedBinary(false, Invented(Block)), counting);
            handed = 0;

            var before = GC.GetAllocatedBytesForCurrentThread();
            StoredBinaries.Read(binary, counting);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(handed, Is.EqualTo(Size));
            Assert.That(allocated, Is.LessThan(4 * 1024), "reading an open attachment copied something");
        }

        [Test]
        public void Reading_a_protected_attachment_keeps_a_piece_alive_and_never_the_whole()
        {
            // A protected attachment is read through a copy of each piece, so a read allocates
            // about the attachment's size over its whole length and never an array as large as
            // the attachment: no array handed out is longer than a piece and the block ahead
            // of it.
            const int Size = 8 * 1024 * 1024;
            var binary = new ProtectedBinary(true, Invented(Size));
            var longest = 0;
            long handed = 0;
            Action<byte[], int, int> counting = (bytes, offset, count) =>
            {
                handed += count;
                longest = Math.Max(longest, bytes.Length);
            };

            StoredBinaries.Read(new ProtectedBinary(true, Invented(Block)), counting);
            handed = 0;
            longest = 0;

            var before = GC.GetAllocatedBytesForCurrentThread();
            StoredBinaries.Read(binary, counting);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(handed, Is.EqualTo(Size));
            Assert.That(longest, Is.LessThanOrEqualTo(Piece + Block));
            Assert.That(allocated, Is.LessThan(Size + (Size / 8)), "a copy of the whole was made beside the pieces");
        }

        [Test]
        public void A_protected_attachment_s_pieces_are_copies_overwritten_once_the_read_is_over(
            [ValueSource(nameof(Sizes))] int size)
        {
            var content = Invented(size);
            var binary = new ProtectedBinary(true, content);
            var held = KeePassMemory.Buffer(binary);
            var encrypted = (byte[])held.Clone();

            Assume.That(size == 0 || KeePassMemory.KeptAs(binary) == "ProtectedMemory", Is.True);
            Assert.That(Shows(encrypted, content), Is.False, "KeePass keeps a protected attachment in the clear");

            var handed = new List<byte[]>();
            StoredBinaries.Read(binary, (bytes, offset, count) =>
            {
                handed.Add(bytes);

                Assert.That(bytes, Is.Not.SameAs(held), "a piece of a protected attachment is KeePass's own buffer");
                Assert.That(
                    held.SequenceEqual(encrypted),
                    Is.True,
                    "KeePass's buffer was changed, or stood decrypted, while a piece was handed out");
            });

            Assert.That(handed, Has.Count.EqualTo((size + Piece - 1) / Piece));
            foreach (var copy in handed)
            {
                Assert.That(copy, Has.All.EqualTo(0), "a piece of a protected attachment was left in the clear");
            }

            Assert.That(held.SequenceEqual(encrypted), Is.True, "KeePass's buffer holds other bytes than it held");
            Assert.That(KeePassMemory.Buffer(binary), Is.SameAs(held), "KeePass's buffer is another array than it was");
            Assert.That(size == 0 || KeePassMemory.KeptAs(binary) == "ProtectedMemory", Is.True);
            Assert.That(
                binary.ReadData().SequenceEqual(content),
                Is.True,
                "KeePass reads other bytes once the gate has read");
        }

        [Test]
        public void The_digest_is_the_SHA_256_of_what_KeePass_hands_out_whole(
            [Values(false, true)] bool isProtected, [ValueSource(nameof(Sizes))] int size)
        {
            var binary = new ProtectedBinary(isProtected, Invented(size));

            byte[] expected;
            using (var sha256 = SHA256.Create())
            {
                expected = sha256.ComputeHash(binary.ReadData());
            }

            Assert.That(StoredBinaries.Digest(binary), Is.EqualTo(expected));
            Assert.That(StoredBinaries.Digest(binary), Is.EqualTo(expected), "and again by the asking after");
        }

        [Test]
        public void What_takes_the_pieces_throwing_stops_the_read_and_leaves_no_piece_in_the_clear(
            [Values(false, true)] bool isProtected)
        {
            // An answer is written to a peer, and a peer that goes away throws out of the
            // write. The read stops there, what was thrown comes out as it was thrown, and the
            // copy a protected piece was handed in is overwritten all the same.
            var content = Invented((3 * Piece) + 5);
            var binary = new ProtectedBinary(isProtected, content);
            var held = KeePassMemory.Buffer(binary);
            var kept = (byte[])held.Clone();
            var failure = new IOException("an invented peer that went away");
            var handed = new List<byte[]>();

            var thrown = Assert.Throws<IOException>(() => StoredBinaries.Read(binary, (bytes, offset, count) =>
            {
                handed.Add(bytes);
                if (handed.Count == 2)
                {
                    throw failure;
                }
            }));

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(handed, Has.Count.EqualTo(2), "a piece was handed out once the read had failed");
            Assert.That(held.SequenceEqual(kept), Is.True, "KeePass's buffer holds other bytes than it held");

            if (isProtected)
            {
                foreach (var copy in handed)
                {
                    Assert.That(copy, Has.All.EqualTo(0), "a piece was left in the clear by a read that failed");
                }
            }

            Assert.That(Joined(binary).SequenceEqual(content), Is.True, "and the read after it hands out the whole");
        }

        [Test]
        public void A_read_is_right_while_KeePass_reads_the_same_attachment_on_other_threads()
        {
            // `ProtectedBinary.ReadData` decrypts the buffer where it stands, copies it and
            // encrypts it again, under the object's own lock. A piece copied beside that could
            // be a copy of the buffer standing decrypted, which decrypted again is noise, or of
            // half of each. The reader copies under the same lock, which is what this holds it
            // to: KeePass's own thread and other connections read the attachment whole, over
            // and over, while it is read a piece at a time. A race proves nothing by passing,
            // and this fails, sooner or later, on a reader that copies outside the lock.
            const int Readers = 3;
            var content = Invented((5 * Piece) + 7);
            var binary = new ProtectedBinary(true, content);
            byte[] expected;
            using (var sha256 = SHA256.Create())
            {
                expected = sha256.ComputeHash(content);
            }

            var stop = 0;
            var whole = 0;
            var wrong = new List<string>();
            var readers = new List<Task>();

            using (var together = new Barrier(Readers + 1))
            {
                for (var reader = 0; reader < Readers; reader++)
                {
                    readers.Add(Task.Factory.StartNew(
                        () =>
                        {
                            together.SignalAndWait();
                            while (Volatile.Read(ref stop) == 0)
                            {
                                if (!binary.ReadData().SequenceEqual(content))
                                {
                                    lock (wrong)
                                    {
                                        wrong.Add("KeePass read other bytes beside a read a piece at a time");
                                    }
                                }

                                Interlocked.Increment(ref whole);
                            }
                        },
                        TaskCreationOptions.LongRunning));
                }

                together.SignalAndWait();
                var rounds = 0;
                var until = DateTime.UtcNow.AddSeconds(1.5);
                try
                {
                    while (DateTime.UtcNow < until)
                    {
                        Assert.That(
                            Joined(binary).SequenceEqual(content),
                            Is.True,
                            "round " + rounds + " handed out other bytes");
                        Assert.That(StoredBinaries.Digest(binary), Is.EqualTo(expected), "round " + rounds);
                        rounds++;
                    }
                }
                finally
                {
                    Volatile.Write(ref stop, 1);
                    Task.WaitAll(readers.ToArray());
                }

                Assert.That(wrong, Is.Empty);
                Assert.That(rounds, Is.GreaterThan(1), "the attachment was read a single time, beside nothing");
                Assert.That(Volatile.Read(ref whole), Is.GreaterThan(Readers), "KeePass's reads did not run beside it");
            }

            Assert.That(binary.ReadData().SequenceEqual(content), Is.True);
        }

        [Test]
        public void A_protected_attachment_kept_through_another_plugin_s_cipher_is_read_through_KeePass_s_own_read()
        {
            // KeePass keeps a protected attachment made while `ProtectedBinary.ExtCrypt` is set
            // through that cipher, of which the gate knows nothing a piece could be decrypted
            // by. KeePass itself reads what it keeps that way, so the gate has KeePass read
            // it: a single copy of the whole attachment, handed out in the pieces a read in
            // place hands out and overwritten once the read is over. An attachment held in
            // the open is not encrypted by anything, and an attachment of no bytes is kept as
            // it is, so both are read as ever, with KeePass reading nothing.
            var decrypted = 0;
            var content = Invented(Piece + 5);
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
                var hooked = new ProtectedBinary(true, content);
                var open = new ProtectedBinary(false, content);
                var empty = new ProtectedBinary(true, Array.Empty<byte>());
                Assume.That(KeePassMemory.KeptAs(hooked), Is.EqualTo("ExtCrypt"));

                var counts = new List<int>();
                var arrays = new List<byte[]>();
                using (var joined = new MemoryStream())
                {
                    StoredBinaries.Read(hooked, (bytes, offset, count) =>
                    {
                        counts.Add(count);
                        arrays.Add(bytes);
                        joined.Write(bytes, offset, count);
                    });

                    Assert.That(
                        joined.ToArray().SequenceEqual(content),
                        Is.True,
                        "the pieces are other bytes than were stored");
                }

                Assert.That(counts, Is.EqualTo(new[] { Piece, 5 }), "the pieces are other pieces than a read in place hands out");
                Assert.That(arrays.Distinct().Count(), Is.EqualTo(1), "more than a single copy was made");
                Assert.That(arrays[0], Has.Length.EqualTo(content.Length), "the copy is not the attachment whole");
                Assert.That(arrays[0], Has.All.EqualTo(0), "the copy was left in the clear");
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "KeePass read the attachment other than a single time");
                Assert.That(KeePassMemory.KeptAs(hooked), Is.EqualTo("ExtCrypt"));

                byte[] expected;
                using (var sha256 = SHA256.Create())
                {
                    expected = sha256.ComputeHash(content);
                }

                Assert.That(StoredBinaries.Digest(hooked), Is.EqualTo(expected));
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(2), "a digest reads the attachment a single time");

                var held = KeePassMemory.Buffer(open);
                StoredBinaries.Read(open, (bytes, offset, count) => Assert.That(bytes, Is.SameAs(held)));
                Assert.That(
                    Joined(open).SequenceEqual(content),
                    Is.True,
                    "an attachment held in the open is read as ever");
                Assert.That(Joined(empty), Is.Empty);
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(2), "KeePass read what is read in place");
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }
        }

        [Test]
        public void An_attachment_is_read_in_place_or_through_KeePass_by_how_KeePass_keeps_it_at_that_moment()
        {
            // KeePass's own read decrypts a protected attachment and encrypts it again through
            // whatever cipher stands by then, and a save reads every attachment. So how an
            // attachment is kept is no property of the object for good: an attachment the gate
            // read in place is read through KeePass once KeePass has read it under another
            // plugin's cipher, and in place again once KeePass has read it with that cipher
            // gone, the gate's own read through KeePass among those reads. Which is why the
            // reader asks how the buffer is kept at every read. That it asks at every piece of
            // a read is held by the test after this.
            var content = Invented(Piece + 5);
            var binary = new ProtectedBinary(true, content);
            Assume.That(KeePassMemory.KeptAs(binary), Is.EqualTo("ProtectedMemory"));
            Assert.That(Longest(binary, content), Is.LessThanOrEqualTo(Piece + Block), "read through a copy of the whole");

            var decrypted = 0;
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
                Assert.That(
                    Longest(binary, content),
                    Is.LessThanOrEqualTo(Piece + Block),
                    "a cipher being set changes nothing KeePass keeps");
                Assert.That(KeePassMemory.KeptAs(binary), Is.EqualTo("ProtectedMemory"));

                Assert.That(binary.ReadData().SequenceEqual(content), Is.True);
                Assert.That(
                    KeePassMemory.KeptAs(binary),
                    Is.EqualTo("ExtCrypt"),
                    "KeePass no longer keeps what it read through the cipher that stands");

                Assert.That(Longest(binary, content), Is.EqualTo(content.Length), "read in place, of another cipher's bytes");
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "KeePass read it other than a single time");
                Assert.That(KeePassMemory.KeptAs(binary), Is.EqualTo("ExtCrypt"));
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }

            // The cipher going away changes nothing KeePass keeps, so the read after it still
            // goes through KeePass, which decrypts through the cipher it encrypted with and
            // keeps the attachment under Windows' protection from then on.
            Assert.That(Longest(binary, content), Is.EqualTo(content.Length));
            Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(2));
            Assert.That(KeePassMemory.KeptAs(binary), Is.EqualTo("ProtectedMemory"));

            Assert.That(
                Longest(binary, content),
                Is.LessThanOrEqualTo(Piece + Block),
                "and it is read in place again once KeePass keeps it as it keeps any");
            Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(2));
        }

        [Test]
        public void An_attachment_KeePass_comes_to_keep_another_way_between_two_pieces_is_read_on_through_KeePass()
        {
            // The reader holds no lock while a piece is handed over, which is a write to a
            // peer, so KeePass can read the attachment between two pieces of a single read, a
            // save among those reads, and keep it from then on through whatever cipher stands.
            // The pieces still to come would be that cipher's bytes run through Windows'
            // protection, which checks nothing and hands back noise. So how the buffer is kept
            // is asked under KeePass's lock for every piece, and a read that finds it changed
            // goes on through KeePass's own read from where it stands: a reader that asked a
            // single time, ahead of the first piece, hands out the whole attachment here, its
            // later pieces noise.
            var content = Invented((3 * Piece) + 5);
            var binary = new ProtectedBinary(true, content);
            Assume.That(KeePassMemory.KeptAs(binary), Is.EqualTo("ProtectedMemory"));

            var counts = new List<int>();
            var arrays = new List<byte[]>();
            byte[] handed;
            var was = ProtectedBinary.ExtCrypt;
            try
            {
                using (var joined = new MemoryStream())
                {
                    StoredBinaries.Read(binary, (bytes, offset, count) =>
                    {
                        counts.Add(count);
                        arrays.Add(bytes);
                        joined.Write(bytes, offset, count);
                        if (counts.Count > 1)
                        {
                            return;
                        }

                        // Inside the first piece: another plugin's cipher comes to stand, and
                        // KeePass reads the attachment whole, as its save does.
                        ProtectedBinary.ExtCrypt = (data, flags, id) =>
                        {
                            for (var i = 0; i < data.Length; i++)
                            {
                                data[i] ^= 0x5A;
                            }
                        };

                        Assert.That(binary.ReadData().SequenceEqual(content), Is.True);
                        Assert.That(
                            KeePassMemory.KeptAs(binary),
                            Is.EqualTo("ExtCrypt"),
                            "KeePass no longer keeps what it read through the cipher that stands");
                    });

                    handed = joined.ToArray();
                }
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }

            Assert.That(counts, Is.EqualTo(new[] { Piece, Piece, Piece, 5 }), "the pieces are other pieces than a read in place hands out");
            Assert.That(handed.SequenceEqual(content), Is.True, "what was handed out is other bytes than the attachment");
            Assert.That(arrays[0], Has.Length.LessThanOrEqualTo(Piece + Block), "the first piece was read in place");
            Assert.That(
                arrays.Skip(1).Distinct().Count(),
                Is.EqualTo(1),
                "the pieces after it came from more than a single copy");
            Assert.That(arrays[1], Has.Length.EqualTo(content.Length));
            foreach (var copy in arrays)
            {
                Assert.That(copy, Has.All.EqualTo(0), "a copy was left in the clear");
            }

            // And KeePass still reads it, and the gate after it.
            Assert.That(binary.ReadData().SequenceEqual(content), Is.True);
            Assert.That(Joined(binary).SequenceEqual(content), Is.True);
        }

        [Test]
        public void Everything_the_read_in_place_rests_on_is_as_it_was_measured_here()
        {
            // The fields it reads of KeePass, and what it rests on of Windows: a piece
            // decrypting on its own once the block ahead is decrypted with it, the start of a
            // buffer decrypting on its own from its first byte, and the same bytes protected to
            // the same bytes, each checked by the reader itself. On a KeePass or a Windows
            // where that is no longer so, every read of an attachment goes through KeePass's
            // own read and copies the attachment whole, and that is told here before it is
            // met. The plugin asks the same as it loads, and logs the answer.
            Assert.That(StoredBinaries.WhyNotInPlace, Is.Null);
        }

        [Test]
        public void A_read_needs_an_attachment_and_something_to_take_its_pieces()
        {
            var binary = new ProtectedBinary(true, Invented(Block));

            Assert.Throws<ArgumentNullException>(() => StoredBinaries.Read(null, (bytes, offset, count) => { }));
            Assert.Throws<ArgumentNullException>(() => StoredBinaries.Read(binary, null));
            Assert.Throws<ArgumentNullException>(() => StoredBinaries.Digest(null));
        }

        /// <summary>
        /// Invented bytes, the same for the same size, and no two blocks of them alike, so that
        /// a piece out of its place shows.
        /// </summary>
        private static byte[] Invented(int size)
        {
            var bytes = new byte[size];
            new Random(size + 17).NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// Every byte the reader hands out of an attachment, joined in the order the pieces
        /// came, each piece copied where it is handed over.
        /// </summary>
        private static byte[] Joined(ProtectedBinary binary)
        {
            using (var joined = new MemoryStream())
            {
                StoredBinaries.Read(binary, joined.Write);
                return joined.ToArray();
            }
        }

        /// <summary>
        /// The longest array a read of an attachment hands its pieces in, the read held to
        /// handing out the content: a piece and the block ahead of it at most for a protected
        /// attachment read in place, and the whole attachment for a read through KeePass.
        /// </summary>
        private static int Longest(ProtectedBinary binary, byte[] content)
        {
            var longest = 0;
            using (var joined = new MemoryStream())
            {
                StoredBinaries.Read(binary, (bytes, offset, count) =>
                {
                    longest = Math.Max(longest, bytes.Length);
                    joined.Write(bytes, offset, count);
                });

                Assert.That(joined.ToArray().SequenceEqual(content), Is.True, "a read handed out other bytes");
            }

            return longest;
        }

        /// <summary>
        /// Whether a buffer holds any whole block of the content where the content has it,
        /// which a buffer kept encrypted does not.
        /// </summary>
        private static bool Shows(byte[] buffer, byte[] content)
        {
            for (var at = 0; at + Block <= content.Length; at += Block)
            {
                var same = true;
                for (var i = 0; i < Block && same; i++)
                {
                    same = buffer[at + i] == content[at + i];
                }

                if (same)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
