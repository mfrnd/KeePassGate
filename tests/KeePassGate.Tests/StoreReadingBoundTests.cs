using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a reading costs in the number of store keys a file brings, and what it answers past
    /// the ceiling on them, `StoreFormat.MaxKeys`.
    /// </summary>
    /// <remarks>
    /// Anybody can make a file whose entries each pass under a key of their own, since a key is
    /// found by passing under itself, `STORE.md` §621, and an operator can be handed one to
    /// synchronize. Such a file has to cost a reading what any file of its size costs.
    /// </remarks>
    [TestFixture]
    public sealed class StoreReadingBoundTests
    {
        private const string Nonce = "00112233445566778899aabbccddeeff";

        [TestCase(300)]
        [TestCase(1000)]
        public void A_file_that_brings_a_key_for_each_of_its_entries_is_read_in_bounded_time(int count)
        {
            // Entries each an authority under a key of its own. The second reading is what every
            // hold after the first costs: what the checked forms keep answers it. Measured in
            // Release at a millisecond for a thousand, where it was 19 seconds while the keys were
            // each asked of every version, so the bound passes a slow machine and fails that growth.
            var versions = new StoreVersions();
            var candidates = Enumerable.Range(0, count).Select(index => versions.Candidate(Authority(index))).ToList();

            var first = new StoreKept();
            var clock = Stopwatch.StartNew();
            var reading = StoreReading.Of(candidates, Array.Empty<byte[]>(), new StoreKept(), first);
            var firstTime = clock.Elapsed;

            clock.Restart();
            var again = StoreReading.Of(candidates, Array.Empty<byte[]>(), first, new StoreKept());
            var secondTime = clock.Elapsed;

            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "figure: {0} keys, first reading {1:F0} ms, second {2:F0} ms",
                count,
                firstTime.TotalMilliseconds,
                secondTime.TotalMilliseconds));

            Assert.That(reading.State, Is.EqualTo(StoreState.Split));
            Assert.That(again.State, Is.EqualTo(StoreState.Split));
            Assert.That(secondTime, Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test]
        [Explicit("a measurement to read, with a bound set from it")]
        public void Past_the_ceiling_a_second_reading_costs_what_the_same_entries_giving_no_key_cost()
        {
            // Twenty thousand entries, each an authority under a key of its own, against twenty
            // thousand of the same shape that give no key, their identifiers not the ones their
            // contents give: what the second costs is the reading of that many versions and
            // nothing under any key, so the first may cost it a constant more and nothing that
            // grows with the keys found. Run with `dotnet test --filter` and this name, in Release.
            var versions = new StoreVersions();
            var keyed = Enumerable.Range(0, 20000).Select(index => versions.Candidate(Authority(index))).ToList();
            var keyless = Enumerable.Range(0, 20000)
                .Select(index => versions.Candidate(Unplaced(index), Authority(index).Strings))
                .ToList();

            var keyedTime = SecondReading(keyed);
            var keylessTime = SecondReading(keyless);

            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "figure: 20000 keys, second reading {0:F0} ms, the same entries giving no key {1:F0} ms",
                keyedTime.TotalMilliseconds,
                keylessTime.TotalMilliseconds));

            Assert.That(keyedTime, Is.LessThan(TimeSpan.FromTicks((keylessTime.Ticks * 2) + TimeSpan.FromMilliseconds(50).Ticks)));
        }

        [Test]
        public void At_the_ceiling_every_key_is_judged_and_past_it_none_is_and_the_store_is_split()
        {
            var at = Keyed(StoreFormat.MaxKeys);
            var reading = StoreReading.Of(at.Candidates, Array.Empty<byte[]>(), null, new StoreKept());

            Assert.That(reading.State, Is.EqualTo(StoreState.Split));
            Assert.That(reading.Reason, Does.StartWith("records under " + StoreFormat.MaxKeys));
            Assert.That(reading.Holds(at.Stranger), Is.False, "at the ceiling a stranger's entry is passed over and served");

            var past = Keyed(StoreFormat.MaxKeys + 1);
            var beyond = StoreReading.Of(past.Candidates, Array.Empty<byte[]>(), null, new StoreKept());

            Assert.That(beyond.State, Is.EqualTo(StoreState.Split));
            Assert.That(beyond.Judged, Is.False, "nothing of the store is judged, so it counts nothing");
            Assert.That(
                beyond.Reason,
                Does.Contain((StoreFormat.MaxKeys + 1).ToString(CultureInfo.InvariantCulture) + " store keys")
                    .And.Contain("all " + past.Candidates.Count.ToString(CultureInfo.InvariantCulture) + " entries")
                    .And.Contain("have to be deleted before the store is judged again"),
                "the line names the keys, what is hidden, and what the operator does");
            Assert.That(reading.Judged, Is.True);
        }

        [Test]
        public void Past_the_ceiling_the_guard_hides_what_it_hid_and_every_entry_carrying_a_string_of_the_store()
        {
            var past = Keyed(StoreFormat.MaxKeys + 1);
            var beyond = StoreReading.Of(past.Candidates, Array.Empty<byte[]>(), null, new StoreKept());

            foreach (var candidate in past.Candidates)
            {
                Assert.That(beyond.Holds(candidate.Identifier), Is.True, candidate.Hex);
            }

            Assert.That(beyond.Holds(past.Stranger), Is.True, "a stranger's entry carries a string of the store");
            Assert.That(beyond.Holds(past.Stripped), Is.True, "a record stripped of its strings is still told by its tag");
            Assert.That(beyond.Holds(new byte[16]), Is.False, "an entry of the operator's is served");
        }

        private static (List<StoreCandidate> Candidates, byte[] Stranger, byte[] Stripped) Keyed(int keys)
        {
            var versions = new StoreVersions();
            var candidates = Enumerable.Range(0, keys).Select(index => versions.Candidate(Authority(index))).ToList();

            var stranger = Enumerable.Repeat((byte)0x55, 16).ToArray();
            candidates.Add(versions.Candidate(
                stranger,
                new Dictionary<string, string> { { "KPG.Kind", "revoked" }, { "KPG.Data", "{}" }, { "KPG.Mac", "00" } }));

            // A record of the first key, every string of the store taken off it, so no candidate:
            // only its identifier tells it.
            var stripped = StoreComposer.Tag(KeyOf(0), "revoked").Concat(new byte[13]).ToArray();
            return (candidates, stranger, stripped);
        }

        private static TimeSpan SecondReading(IReadOnlyList<StoreCandidate> candidates)
        {
            var kept = new StoreKept();
            StoreReading.Of(candidates, Array.Empty<byte[]>(), new StoreKept(), kept);
            var clock = Stopwatch.StartNew();
            StoreReading.Of(candidates, Array.Empty<byte[]>(), kept, new StoreKept());
            return clock.Elapsed;
        }

        /// <summary>An identifier no content gives, so the version under it gives no key.</summary>
        private static byte[] Unplaced(int index)
        {
            var identifier = new byte[16];
            BitConverter.GetBytes(index).CopyTo(identifier, 0);
            identifier[15] = 0xee;
            return identifier;
        }

        private static StoreComposer.Composed Authority(int index)
        {
            var key = KeyOf(index);
            var id = index.ToString("x64", CultureInfo.InvariantCulture);
            return StoreComposer.Record(
                key, "authority", "authority:" + id,
                StoreComposer.Content(("id", id), ("nonce", Nonce), ("storeKey", StoreComposer.HexOf(key))));
        }

        private static byte[] KeyOf(int index)
        {
            return StoreComposer.Key("bound/" + index.ToString(CultureInfo.InvariantCulture));
        }
    }
}
