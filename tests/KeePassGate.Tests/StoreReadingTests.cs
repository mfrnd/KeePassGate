using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The store's reading, `STORE.md` §620 to §624, over candidates the shared composer makes.
    /// </summary>
    /// <remarks>
    /// Each test is a sentence of the document's reading, and the composer is held to the measured
    /// code by `StoreFormatTests`, so a store composed here is a store the measured code wrote.
    /// </remarks>
    [TestFixture]
    public sealed partial class StoreReadingTests
    {
        private static readonly byte[] One = StoreComposer.Key("one");
        private static readonly byte[] Two = StoreComposer.Key("two");

        private StoreVersions _versions;

        [SetUp]
        public void SetUp()
        {
            _versions = new StoreVersions();
        }

        [Test]
        public void An_authority_s_key_is_found_and_tells_every_record_under_it_by_its_tag()
        {
            var authority = Authority(One, 'a');
            var reading = Read(_versions.Candidate(authority));

            Assert.That(reading.Found(One, Two), Is.EqualTo(1));
            Assert.That(reading.Holds(authority.Identifier), Is.True);

            foreach (var kind in StoreKinds.All)
            {
                var tagged = StoreComposer.Tag(One, kind.Name).Concat(new byte[13]).ToArray();
                Assert.That(reading.Holds(tagged), Is.True, kind.Name + " is told by its tag");
            }

            Assert.That(reading.Holds(new byte[16]), Is.False);
        }

        [Test]
        public void A_retirement_that_holds_a_key_gives_it_too()
        {
            var retired = StoreComposer.Record(
                Two, "retired", "authority:" + Hex('b'),
                StoreComposer.Content(("authority", Hex('b')), ("storeKey", StoreComposer.HexOf(Two))));

            Assert.That(Read(_versions.Candidate(retired)).Found(One, Two), Is.EqualTo(1));
        }

        [Test]
        public void Keys_are_compared_by_their_value()
        {
            var retired = StoreComposer.Record(
                One, "retired", "authority:" + Hex('b'),
                StoreComposer.Content(("authority", Hex('b')), ("storeKey", StoreComposer.HexOf(One))));

            Assert.That(Read(_versions.Candidate(Authority(One, 'a')), _versions.Candidate(retired)).Found(One, Two), Is.EqualTo(1));
            Assert.That(Read(_versions.Candidate(Authority(One, 'a')), _versions.Candidate(Authority(Two, 'c'))).Found(One, Two), Is.EqualTo(2));
        }

        [Test]
        public void A_copy_of_the_authority_gives_no_key_and_is_still_held_by_its_check()
        {
            var authority = Authority(One, 'a');
            var copy = Identifier(0x77);

            var alone = Read(_versions.Candidate(copy, authority.Strings));
            Assert.That(alone.Found(One, Two), Is.EqualTo(0), "a copy is no source of the store key, STORE.md §621");

            var beside = Read(_versions.Candidate(authority), _versions.Candidate(copy, authority.Strings));
            Assert.That(beside.Holds(copy), Is.True, "a copy is absent all the same, STORE.md §633");
        }

        [Test]
        public void An_authority_whose_key_is_upper_case_gives_none()
        {
            // `STORE.md` §658: a store key is 64 lowercase hexadecimal characters.
            var authority = StoreComposer.Record(
                One, "authority", "authority:" + Hex('a'),
                StoreComposer.Content(("id", Hex('a')), ("storeKey", StoreComposer.HexOf(One).ToUpperInvariant())));

            Assert.That(Read(_versions.Candidate(authority)).Found(One, Two), Is.EqualTo(0));
        }

        [Test]
        public void An_authority_edited_with_its_written_version_in_the_history_still_gives_its_key()
        {
            var authority = Authority(One, 'a');
            var edited = StoreVersions.With(authority.Strings, StoreComposer.Data, "{\"id\":\"edited\"}");

            Assert.That(Read(_versions.Candidate(authority.Identifier, edited, authority.Strings)).Found(One, Two), Is.EqualTo(1));
        }

        [Test]
        public void A_record_of_a_kind_nobody_knows_is_held_by_its_check()
        {
            var unknown = StoreComposer.Record(One, "future", "about:x", StoreComposer.Content(("about", "x")));
            var reading = Read(_versions.Candidate(Authority(One, 'a')), _versions.Candidate(unknown));

            Assert.That(reading.Holds(unknown.Identifier), Is.True);
        }

        [Test]
        public void A_stranger_s_entry_is_held_by_nothing()
        {
            var stranger = _versions.Candidate(
                Identifier(0x55),
                new Dictionary<string, string> { { "KPG.Kind", "revoked" }, { "KPG.Data", "{}" }, { "KPG.Mac", "00" } });

            Assert.That(Read(_versions.Candidate(Authority(One, 'a')), stranger).Holds(Identifier(0x55)), Is.False);
        }

        [Test]
        public void A_record_composed_without_the_measured_member_v_reads_as_one_with_it()
        {
            // `STORE.md` §607: the measured code wrote `v` and read it nowhere, and a reader
            // passes over a member it does not know, in a kind it knows.
            var without = Authority(One, 'a');
            var with = StoreComposer.Record(
                One, "authority", "authority:" + Hex('a'),
                StoreComposer.Content(("id", Hex('a')), ("storeKey", StoreComposer.HexOf(One)), ("v", "2")));

            Assert.That(Read(_versions.Candidate(without)).Found(One, Two), Is.EqualTo(1));
            Assert.That(Read(_versions.Candidate(with)).Found(One, Two), Is.EqualTo(1));
        }

        [Test]
        public void A_version_at_each_ceiling_reads_and_beyond_it_fails_whole()
        {
            // `STORE.md` §607: a string of up to 1,048,576 characters, and up to 32 members.
            var longest = new string('x', StoreFormat.MaxStringLength - "{\"about\":\"\",\"nonce\":\"00112233445566778899aabbccddeeff\"}".Length);
            var at = StoreComposer.Record(One, "future", "x", StoreComposer.Content(("about", longest), ("nonce", Nonce)));
            var beyond = StoreComposer.Record(One, "future", "x", StoreComposer.Content(("about", longest + "x"), ("nonce", Nonce)));

            Assert.That(at.Strings[StoreComposer.Data].Length, Is.EqualTo(StoreFormat.MaxStringLength));
            Assert.That(Checks(One, at.Strings), Is.True);
            Assert.That(Checks(One, beyond.Strings), Is.False);

            var members = Enumerable.Range(0, KeePassGate.Transport.JsonBody.MaxMembers - 1).Select(index => ("m" + index, "x")).ToArray();
            var full = StoreComposer.Record(One, "future", "x", StoreComposer.Content(members));
            var over = StoreComposer.Record(
                One, "future", "x", StoreComposer.Content(members.Concat(new[] { ("over", "x") }).ToArray()));

            Assert.That(Checks(One, full.Strings), Is.True);
            Assert.That(Checks(One, over.Strings), Is.False);
        }

        [Test]
        public void A_string_beyond_the_ceiling_is_never_read()
        {
            // Its length is asked first, so a giant string of a stranger's entry costs a hold
            // nothing but that question.
            var giant = new Dictionary<string, string>
            {
                { "KPG.Kind", "revoked" },
                { "KPG.Data", new string('x', StoreFormat.MaxStringLength + 1) },
                { "KPG.Mac", "00" },
            };

            Read(_versions.Candidate(Authority(One, 'a')), _versions.Candidate(Identifier(0x44), giant));

            var authorityReads = new StoreVersions();
            Read(authorityReads.Candidate(Authority(One, 'a')));
            Assert.That(_versions.Reads, Is.EqualTo(authorityReads.Reads), "nothing of the giant version was read");
        }

        [Test]
        public void A_line_feed_in_a_part_and_an_item_named_otherwise_each_fail_the_version()
        {
            var head = Head(One, 1, Format());
            Assert.That(Checks(One, head.Strings), Is.True);

            var badName = StoreVersions.With(head.Strings, "KPG.S.01", Format());
            badName = StoreVersions.With(badName, "KPG.Mac", StoreComposer.CheckOf(One, badName));
            Assert.That(Checks(One, badName), Is.False, "STORE.md §608: an item is named with exactly four digits");

            var lineFeed = StoreVersions.With(head.Strings, "KPG.S.0002", "{\"kind\":\"revoked\",\n\"client\":\"x\"}");
            lineFeed = StoreVersions.With(lineFeed, "KPG.Mac", StoreComposer.CheckOf(One, lineFeed));
            Assert.That(Checks(One, lineFeed), Is.False);

            var twice = StoreVersions.With(head.Strings, "KPG.Data", "{\"after\":\"\",\"height\":\"1\",\"height\":\"1\"}");
            twice = StoreVersions.With(twice, "KPG.Mac", StoreComposer.CheckOf(One, twice));
            Assert.That(Checks(One, twice), Is.False, "a member named twice");
        }

        [Test]
        public void Nothing_a_file_can_hold_makes_the_reading_throw()
        {
            var authority = Authority(One, 'a');
            var malformed = new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "KPG.Kind", "head" } },
                new Dictionary<string, string> { { "KPG.Kind", string.Empty }, { "KPG.Data", string.Empty }, { "KPG.Mac", string.Empty } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "[]" }, { "KPG.Mac", "x" } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "{\"after\":7}" }, { "KPG.Mac", new string('f', 64) } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "{\"after\":\"x y\",\"height\":\"0\"}" }, { "KPG.Mac", new string('0', 64) } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "{\"after\":\"\",\"height\":\"65536\"}" }, { "KPG.Mac", new string('0', 63) } },
                new Dictionary<string, string> { { "KPG.Kind", "authority" }, { "KPG.Data", "{\"id\":\"\",\"storeKey\":\"zz\"}" }, { "KPG.Mac", "\u0000" } },
                new Dictionary<string, string> { { "KPG.Kind", "version" }, { "KPG.Data", "{\"object\":\"\",\"after\":\" \",\"doc\":\"\"}" }, { "KPG.Mac", "0" } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "{}" }, { "KPG.Mac", "0" }, { "KPG.S.0001", "{\"kind\":{}}" } },
                new Dictionary<string, string> { { "KPG.Kind", "head" }, { "KPG.Data", "{}" }, { "KPG.Mac", "0" }, { "KPG.S.0001", "not json" } },
                new Dictionary<string, string> { { "KPG.Kind", "kept" }, { "KPG.Data", "{\"key\":\"\",\"after\":\"\"}" }, { "KPG.Mac", "0" }, { "KPG.Other", "x" } },
                new Dictionary<string, string> { { "KPG.Kind", "\uD800" }, { "KPG.Data", "{\"\uD800\":\"\"}" }, { "KPG.Mac", "0" } },
            };

            foreach (var version in malformed)
            {
                var copy = version.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var key in new[] { One, Two })
                {
                    if (copy.ContainsKey("KPG.Kind") && copy.ContainsKey("KPG.Data") && !copy.ContainsKey("KPG.S.0001"))
                    {
                        copy["KPG.Mac"] = StoreComposer.CheckOf(key, copy);
                    }

                    Assert.DoesNotThrow(
                        () => Read(_versions.Candidate(authority), _versions.Candidate(Identifier(0x33), copy)),
                        string.Join(" ", version.Select(pair => pair.Key + "=" + pair.Value)));
                }
            }
        }

        [Test]
        public void A_version_kept_from_the_reading_before_is_not_read_again()
        {
            var authority = Authority(One, 'a');
            var candidate = _versions.Candidate(authority);
            var kept = new StoreKept();

            var first = new StoreKept();
            StoreReading.Of(new[] { candidate }, NoDeletions, kept, first);
            var reads = _versions.Reads;
            var lengths = _versions.Lengths;

            var second = new StoreKept();
            var reading = StoreReading.Of(new[] { candidate }, NoDeletions, first, second);

            Assert.That(reading.Found(One, Two), Is.EqualTo(1), "the kept form still gives its key");
            Assert.That(_versions.Reads, Is.EqualTo(reads), "nothing was read again");
            Assert.That(_versions.Lengths, Is.EqualTo(lengths), "nothing was asked its length again");
        }

        [Test]
        public void Another_object_under_the_same_name_is_read_again_whatever_its_text()
        {
            var authority = Authority(One, 'a');
            var kept = new StoreKept();
            StoreReading.Of(new[] { _versions.Candidate(authority) }, NoDeletions, null, kept);
            var reads = _versions.Reads;

            // The same text, in another object: what KeePass leaves where a value was set again.
            var again = authority.Strings.ToDictionary(pair => pair.Key, pair => new string(pair.Value.ToCharArray()));
            StoreReading.Of(new[] { _versions.Candidate(authority.Identifier, again) }, NoDeletions, kept, new StoreKept());

            Assert.That(_versions.Reads, Is.GreaterThan(reads));
        }

        [Test]
        public void A_kept_form_answers_for_no_other_entry()
        {
            var authority = Authority(One, 'a');
            var kept = new StoreKept();
            StoreReading.Of(new[] { _versions.Candidate(authority) }, NoDeletions, null, kept);

            // The very objects, under another identifier: a copy, which gives no key.
            var reading = StoreReading.Of(
                new[] { _versions.Candidate(Identifier(0x66), authority.Strings) }, NoDeletions, kept, new StoreKept());

            Assert.That(reading.Found(One, Two), Is.EqualTo(0));
        }

        [Test]
        public void A_kept_form_asked_about_a_key_found_since_is_read_again_and_answers()
        {
            // A version checked when the file held one key, and kept, then a second key arrives
            // by a merge: the kept form was never asked about it, so it is read again rather than
            // answered without its text.
            var copyOfTwo = Identifier(0x12);
            var authorityTwo = Authority(Two, 'c');
            var candidate = _versions.Candidate(copyOfTwo, authorityTwo.Strings);

            var kept = new StoreKept();
            StoreReading.Of(new[] { _versions.Candidate(Authority(One, 'a')), candidate }, NoDeletions, null, kept);

            var reading = StoreReading.Of(
                new[] { _versions.Candidate(Authority(One, 'a')), candidate, _versions.Candidate(authorityTwo) },
                NoDeletions,
                kept,
                new StoreKept());

            Assert.That(reading.Found(One, Two), Is.EqualTo(2));
            Assert.That(reading.Holds(copyOfTwo), Is.True, "the copy passes under the key found since");
        }

        [Test]
        public void Keeping_lets_go_of_the_forms_of_versions_no_entry_holds_and_clearing_of_all()
        {
            var authority = Authority(One, 'a');
            var first = new StoreKept();
            StoreReading.Of(new[] { _versions.Candidate(authority) }, NoDeletions, null, first);
            var form = first.Find(authority.Hex, _versions.Version(authority.Strings));
            Assert.That(form, Is.Not.Null);
            var key = form.StoreKey.Bytes;

            var second = new StoreKept();
            StoreReading.Of(Array.Empty<StoreCandidate>(), NoDeletions, first, second);
            first.Leave(second);

            Assert.That(key, Is.All.EqualTo(0), "the key of a form nothing keeps is overwritten");
            Assert.That(second.Find(authority.Hex, _versions.Version(authority.Strings)), Is.Null);

            var third = new StoreKept();
            StoreReading.Of(new[] { _versions.Candidate(authority) }, NoDeletions, null, third);
            var kept = third.Find(authority.Hex, _versions.Version(authority.Strings)).StoreKey.Bytes;
            third.Clear();
            Assert.That(kept, Is.All.EqualTo(0), "clearing overwrites every key held");
        }

        private const string Nonce = "00112233445566778899aabbccddeeff";

        private static readonly IReadOnlyList<byte[]> NoDeletions = Array.Empty<byte[]>();

        private StoreReading Read(params StoreCandidate[] candidates)
        {
            return StoreReading.Of(candidates, NoDeletions, null, new StoreKept());
        }

        private static bool Checks(byte[] key, IReadOnlyDictionary<string, string> strings)
        {
            return StoreVersions.Passes(key, strings);
        }

        private static StoreComposer.Composed Authority(byte[] key, char id)
        {
            return StoreComposer.Record(
                key, "authority", "authority:" + Hex(id),
                StoreComposer.Content(("id", Hex(id)), ("storeKey", StoreComposer.HexOf(key))));
        }

        private static StoreComposer.Composed Head(byte[] key, int height, params string[] items)
        {
            return StoreComposer.Record(
                key, "head", "store",
                StoreComposer.Content(("after", string.Empty), ("height", height.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                height,
                items);
        }

        private static string Format(string number = "1")
        {
            return StoreComposer.Item(("format", number), ("kind", "format"), ("plugin", "0.3.0"));
        }

        private static string Hex(char digit)
        {
            return new string(digit, 64);
        }

        private static byte[] Identifier(byte fill)
        {
            return Enumerable.Repeat(fill, 16).ToArray();
        }
    }
}
