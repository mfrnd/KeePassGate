using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Tests.Fakes;
using KeePassGate.Testing;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The whole of an entry as one caller may read it: what a view holds, what it leaves out, and
    /// when it reads anything.
    /// </summary>
    /// <remarks>
    /// The view is what every answer about an entry and the entry's tag are made from, so what is
    /// pinned here is that a part out of sight is not in it to be rendered or digested, and that
    /// nothing is read before it is asked for. Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class VaultEntryViewTests
    {
        private static readonly VaultSight OpenStringsAndAttachments = new VaultSight(true, false, VaultAttachmentSight.Read);

        private static readonly VaultSight EveryStringAndNoAttachment = new VaultSight(true, true, VaultAttachmentSight.None);

        /// <summary>
        /// The sights that do not read: none at all, the least, `read`'s switches handed in
        /// without `read`, and the listing of attachments alone.
        /// </summary>
        private static readonly VaultSight[] NotReading =
        {
            null,
            VaultSight.Least,
            new VaultSight(false, true, VaultAttachmentSight.Read),
            new VaultSight(false, false, VaultAttachmentSight.Listed),
        };

        [Test]
        public void The_stored_bytes_of_a_value_are_handed_out_only_for_a_string_in_sight()
        {
            // What a tag digests a protected value from, with no string made of it. Dropped
            // with the reader for a withheld string, and absent for a value the store keeps in
            // the open, which is read as the string it already is.
            var copies = 0;
            Func<byte[]> stored = () =>
            {
                copies++;
                return new byte[] { 0x30, 0x30 };
            };

            var withheld = new VaultViewField(
                "Password", true, () => 2, OpenStringsAndAttachments, () => "00", false, stored);
            Assert.That(withheld.IsWithheld, Is.True);
            Assert.That(withheld.Stored(), Is.Null);
            Assert.That(copies, Is.Zero);

            var inSight = new VaultViewField(
                "Password", true, () => 2, EveryStringAndNoAttachment, () => "00", false, stored);
            Assert.That(inSight.Stored(), Is.EqualTo(new byte[] { 0x30, 0x30 }));
            Assert.That(inSight.Stored(), Is.Not.SameAs(inSight.Stored()), "a copy each time, for the caller to overwrite");

            var open = new VaultViewField("UserName", false, () => 2, EveryStringAndNoAttachment, () => "00");
            Assert.That(open.Stored(), Is.Null);
        }

        [Test]
        public void A_protected_string_out_of_sight_keeps_its_name_and_holds_no_value()
        {
            var reads = 0;
            var field = new VaultViewField("Password", true, () => 19, OpenStringsAndAttachments, () =>
            {
                reads++;
                return "not-a-real-password";
            });

            Assert.That(field.Name, Is.EqualTo("Password"));
            Assert.That(field.IsProtected, Is.True);
            Assert.That(field.IsWithheld, Is.True);
            Assert.That(field.Value, Is.Null);
            Assert.That(reads, Is.Zero, "the reader handed in was dropped, so nothing can read the value through the view");
            Assert.That(field.Characters, Is.EqualTo("Password".Length),
                "its name is all of it that is served, so all that is counted: the length was dropped with the reader");
        }

        [Test]
        public void A_withheld_string_needs_no_reader_and_a_string_in_sight_refuses_to_be_made_without_one()
        {
            var reader = new VaultSight(true, false, VaultAttachmentSight.None);

            Assert.That(new VaultViewField("Password", true, () => 3, reader, null).Value, Is.Null);
            Assert.That(new VaultViewField("Password", true, () => 3, VaultSight.Least, null).Value, Is.Null);
            Assert.That(new VaultViewField("Title", false, () => 3, VaultSight.Least, null).Value, Is.Null,
                "a string nothing protects is withheld too from a sight that does not read");
            Assert.Throws<ArgumentNullException>(() => new VaultViewField("Title", false, () => 3, reader, null));
            Assert.Throws<ArgumentNullException>(() => new VaultViewField("Password", true, () => 3, Sights.Whole, null));
        }

        [Test]
        public void A_view_whose_sight_does_not_read_holds_no_value_for_any_string()
        {
            // A caller that only lists is served names and flags. Its view holds those and the
            // lengths the ceiling is measured by, and nothing a value could be read with.
            var reads = 0;
            Func<string> counted = () =>
            {
                reads++;
                return "abc";
            };

            var views = NotReading.Select(sight => new VaultEntryView(
                Entry(),
                sight,
                new[]
                {
                    new VaultViewField("Title", false, () => 3, sight, counted),
                    new VaultViewField("Password", true, () => 3, sight, counted),
                },
                Array.Empty<VaultViewAttachment>(),
                null));

            foreach (var view in views)
            {
                Assert.That(view.Fields.Select(field => field.IsWithheld), Is.All.True);
                Assert.That(view.Fields.Select(field => field.Value), Is.All.Null);
                Assert.That(view.Fields.Select(field => field.Name), Is.EqualTo(new[] { "Title", "Password" }));
                Assert.That(view.Protected, Is.EqualTo(new[] { "Password" }), "which strings are protected is still told");
                Assert.That(view.Characters, Is.EqualTo("Title".Length + "Password".Length),
                    "names, which are what such a caller is served, and nothing of a value");
                Assert.That(view.NameCharacters, Is.EqualTo("Title".Length + "Password".Length));
            }

            Assert.That(reads, Is.Zero, "the readers handed in were dropped");
        }

        [Test]
        public void No_sight_at_all_is_the_least_sight()
        {
            var guarded = new VaultViewField("Password", true, () => 3, null, () => "abc");
            var open = new VaultViewField("Title", false, () => 3, null, () => "abc");
            var view = new VaultEntryView(
                Entry(), null, new[] { guarded, open }, new[] { Attachment("notes.txt", 3) }, null);

            Assert.That(guarded.IsWithheld, Is.True);
            Assert.That(open.IsWithheld, Is.True);
            Assert.That(open.Value, Is.Null, "the least sight does not read, so no string's value is in it");
            Assert.That(view.Attachments, Is.Null);
            Assert.That(view.Sight, Is.SameAs(VaultSight.Least));
            Assert.That(VaultSight.Least.Reads || VaultSight.Least.Protected, Is.False);
            Assert.That(VaultSight.Least.Attachments, Is.EqualTo(VaultAttachmentSight.None));
            Assert.That(Sights.Whole.Reads && Sights.Whole.Protected, Is.True);
            Assert.That(Sights.Whole.Attachments, Is.EqualTo(VaultAttachmentSight.Read));
        }

        [Test]
        public void A_sight_that_does_not_read_reaches_nothing_read_opens()
        {
            // Protected values and attachments' bytes are read under `read` alone, so a sight
            // without it is built reaching neither, whatever it was handed. Listing attachments
            // is `list`'s to open, and stays.
            var blind = new VaultSight(false, true, VaultAttachmentSight.Read);
            Assert.That(blind.Reads, Is.False);
            Assert.That(blind.Protected, Is.False);
            Assert.That(blind.Attachments, Is.EqualTo(VaultAttachmentSight.None));

            var lister = new VaultSight(false, true, VaultAttachmentSight.Listed);
            Assert.That(lister.Protected, Is.False);
            Assert.That(lister.Attachments, Is.EqualTo(VaultAttachmentSight.Listed));

            var reader = new VaultSight(true, false, VaultAttachmentSight.Listed);
            Assert.That(reader.Reads, Is.True);
            Assert.That(reader.Protected, Is.False);
            Assert.That(reader.Attachments, Is.EqualTo(VaultAttachmentSight.Listed));

            // The listing's reach into protected strings brings a value into a view, and a view
            // that does not read holds none, so it is dropped with the rest.
            Assert.That(new VaultSight(false, false, VaultAttachmentSight.None, true).ListedInSight, Is.False);
            Assert.That(new VaultSight(true, false, VaultAttachmentSight.None, true).ListedInSight, Is.True);
            Assert.That(reader.ListedInSight, Is.False);
            Assert.That(VaultSight.Least.ListedInSight, Is.False);
        }

        [Test]
        public void A_protected_string_a_listing_shows_is_in_sight_on_the_listing_s_reach_beside_read()
        {
            // A caller's view holds everything any route shows that caller: a listing shows a
            // protected title under `list` with `protected`, so beside `read` the title's value
            // is in the view. A protected string no listing shows needs `read` with `protected`.
            var listing = new VaultSight(true, false, VaultAttachmentSight.None, true);
            var reading = new VaultSight(true, false, VaultAttachmentSight.None);
            var blind = new VaultSight(false, false, VaultAttachmentSight.None, true);

            Func<VaultSight, bool, bool, VaultViewField> made = (sight, guarded, listed) =>
                new VaultViewField("Invented", guarded, () => 3, sight, () => "abc", listed);

            Assert.That(made(listing, true, true).Value, Is.EqualTo("abc"));
            Assert.That(made(listing, true, false).IsWithheld, Is.True, "the listing's reach opens no other string");
            Assert.That(made(reading, true, true).IsWithheld, Is.True, "`read` alone opens no protected string");
            Assert.That(made(blind, true, true).IsWithheld, Is.True, "and without `read` a view holds no value");
            Assert.That(made(blind, false, true).IsWithheld, Is.True);
            Assert.That(made(null, true, true).IsWithheld, Is.True);
            Assert.That(made(reading, false, true).Value, Is.EqualTo("abc"), "a string nothing protects, as ever");
            Assert.That(made(Sights.Whole, true, false).Value, Is.EqualTo("abc"));
        }

        [Test]
        public void A_view_whose_sight_does_not_read_has_no_tag()
        {
            // Who is handed a tag is decided here and not by a route: nothing rendering from the
            // view of a caller without `read` has a tag to hand out.
            var asked = 0;
            Func<VaultEntryView, string> tagger = view =>
            {
                asked++;
                return "0123456789abcdef0123456789abcdef";
            };

            var views = NotReading.Select(sight => new VaultEntryView(
                Entry(), sight, Array.Empty<VaultViewField>(), Array.Empty<VaultViewAttachment>(), tagger));

            Assert.That(views.Select(view => view.Tag), Is.All.Null);

            Assert.That(asked, Is.Zero, "and nothing was digested to find that out");

            var read = new VaultEntryView(
                Entry(),
                new VaultSight(true, false, VaultAttachmentSight.None),
                Array.Empty<VaultViewField>(),
                Array.Empty<VaultViewAttachment>(),
                tagger);
            Assert.That(read.Tag, Is.Not.Null, "`read` alone is enough, with neither switch");
        }

        [Test]
        public void An_attachment_listed_and_not_read_keeps_its_name_and_size_and_holds_no_bytes()
        {
            var read = 0;
            Func<byte[]> counted = () =>
            {
                read++;
                return new byte[] { 1, 2, 3 };
            };

            var listed = new VaultSight(true, true, VaultAttachmentSight.Listed);
            var attachment = new VaultViewAttachment("notes.txt", 3, listed, counted, counted);
            var view = new VaultEntryView(Entry(), listed, Array.Empty<VaultViewField>(), new[] { attachment }, null);

            Assert.That(view.Attachments, Has.Count.EqualTo(1));
            Assert.That(view.Attachment("notes.txt").Name, Is.EqualTo("notes.txt"));
            Assert.That(view.Attachment("notes.txt").Size, Is.EqualTo(3));
            Assert.That(attachment.IsWithheld, Is.True);
            Assert.That(attachment.Content, Is.Null);
            Assert.That(attachment.Digest, Is.Null);
            Assert.That(read, Is.Zero, "the readers handed in were dropped, so nothing can read the bytes through the view");

            Assert.That(new VaultViewAttachment("notes.txt", 3, listed, null, null).Content, Is.Null);
            Assert.That(new VaultViewAttachment("notes.txt", 3, null, counted, counted).IsWithheld, Is.True);
            Assert.Throws<ArgumentNullException>(() => new VaultViewAttachment("notes.txt", 3, Sights.Whole, null, counted));
            Assert.Throws<ArgumentNullException>(() => new VaultViewAttachment("notes.txt", 3, Sights.Whole, counted, null));
        }

        [Test]
        public void A_value_is_read_when_it_is_first_asked_for_and_once()
        {
            var reads = 0;
            var reader = new VaultSight(true, false, VaultAttachmentSight.None);
            var field = new VaultViewField("Title", false, () => 7, reader, () =>
            {
                reads++;
                return "a title";
            });

            Assert.That(field.Characters, Is.EqualTo("Title".Length + 7));
            Assert.That(reads, Is.Zero, "measuring reads nothing");

            Assert.That(field.Value, Is.EqualTo("a title"));
            Assert.That(field.Value, Is.EqualTo("a title"));
            Assert.That(reads, Is.EqualTo(1));
        }

        [Test]
        public void A_value_is_measured_when_its_size_is_first_asked_and_once_and_a_withheld_value_never()
        {
            // KeePass may decrypt a protected value to count its characters, so a view asks a
            // length only when something asks the size, and never of a value it withholds.
            var measures = 0;
            Func<int> measured = () =>
            {
                measures++;
                return 7;
            };

            var reader = new VaultSight(true, false, VaultAttachmentSight.None);
            var open = new VaultViewField("Title", false, measured, reader, () => "a title");
            var withheld = new VaultViewField("Password", true, measured, reader, null);
            var view = new VaultEntryView(
                Entry(), reader, new[] { open, withheld }, Array.Empty<VaultViewAttachment>(), null);

            Assert.That(view.NameCharacters, Is.EqualTo("Title".Length + "Password".Length));
            Assert.That(view.Protected, Is.EqualTo(new[] { "Password" }));
            Assert.That(open.Value, Is.EqualTo("a title"));
            Assert.That(measures, Is.Zero, "building a view, its names and reading a value measure nothing");

            Assert.That(withheld.Characters, Is.EqualTo("Password".Length));
            Assert.That(measures, Is.Zero, "a withheld value is not measured");

            Assert.That(view.Characters, Is.EqualTo("Title".Length + 7 + "Password".Length));
            Assert.That(view.Characters, Is.EqualTo(open.Characters + withheld.Characters));
            Assert.That(measures, Is.EqualTo(1), "the value in sight, once");

            Assert.Throws<ArgumentNullException>(() => new VaultViewField("Title", false, null, reader, () => "a title"));
            Assert.That(new VaultViewField("Password", true, null, reader, null).Characters, Is.EqualTo("Password".Length),
                "a withheld string needs nothing to measure it with");
        }

        [Test]
        public void A_value_that_could_not_be_read_is_read_again_by_the_next_asking()
        {
            var failing = true;
            var field = new VaultViewField("Title", false, () => 7, Sights.Whole, () =>
            {
                if (failing)
                {
                    throw new InvalidOperationException("an invented read that fails");
                }

                return "a title";
            });

            Assert.That(() => field.Value, Throws.InvalidOperationException);
            failing = false;

            Assert.That(field.Value, Is.EqualTo("a title"));
        }

        [Test]
        public void An_entry_is_measured_by_what_its_caller_is_handed_with_no_string_read()
        {
            var reads = 0;
            Func<string> counted = () =>
            {
                reads++;
                return "abc";
            };

            var view = new VaultEntryView(
                Entry(),
                OpenStringsAndAttachments,
                new[]
                {
                    new VaultViewField("Title", false, () => 3, OpenStringsAndAttachments, counted),
                    new VaultViewField("Password", true, () => 40, OpenStringsAndAttachments, counted),
                    new VaultViewField("PIN", true, () => 4, OpenStringsAndAttachments, counted),
                },
                Array.Empty<VaultViewAttachment>(),
                null);

            Assert.That(view.Characters, Is.EqualTo("Title".Length + 3 + "Password".Length + "PIN".Length),
                "what the ceiling is asked about is every name and the values in sight, and no withheld value");
            Assert.That(view.NameCharacters, Is.EqualTo("Title".Length + "Password".Length + "PIN".Length));

            var all = new VaultEntryView(
                Entry(),
                EveryStringAndNoAttachment,
                new[]
                {
                    new VaultViewField("Title", false, () => 3, EveryStringAndNoAttachment, counted),
                    new VaultViewField("Password", true, () => 40, EveryStringAndNoAttachment, counted),
                    new VaultViewField("PIN", true, () => 4, EveryStringAndNoAttachment, counted),
                },
                Array.Empty<VaultViewAttachment>(),
                null);

            Assert.That(all.Characters, Is.EqualTo("Title".Length + 3 + "Password".Length + 40 + "PIN".Length + 4),
                "a caller reading protected values is handed them, so they count for that caller");
            Assert.That(view.Protected, Is.EqualTo(new[] { "Password", "PIN" }));
            Assert.That(view.Fields.Select(field => field.Name), Is.EqualTo(new[] { "Title", "Password", "PIN" }),
                "in the order they were handed in, which is the order KeePass keeps them");
            Assert.That(reads, Is.Zero);
        }

        [Test]
        public void A_string_is_found_by_its_name_byte_for_byte()
        {
            var view = new VaultEntryView(
                Entry(),
                Sights.Whole,
                new[] { new VaultViewField("Title", false, () => 3, Sights.Whole, () => "abc") },
                new[] { Attachment("notes.txt", 3) },
                null);

            Assert.That(view.Field("Title"), Is.Not.Null);
            Assert.That(view.Field("title"), Is.Null);
            Assert.That(view.Field("Title­"), Is.Null, "a name the store's collation cannot tell apart is another name");
            Assert.That(view.Field(null), Is.Null);
            Assert.That(view.Attachment("notes.txt"), Is.Not.Null);
            Assert.That(view.Attachment("notes­.txt"), Is.Null);
            Assert.That(view.Attachment(null), Is.Null);
        }

        [Test]
        public void Attachments_out_of_sight_are_no_list_at_all_and_never_an_empty_one()
        {
            var read = 0;
            var attachment = new VaultViewAttachment(
                "notes.txt",
                3,
                Sights.Whole,
                () =>
                {
                    read++;
                    return new byte[] { 1, 2, 3 };
                },
                () =>
                {
                    read++;
                    return new byte[32];
                });

            var hidden = new VaultEntryView(
                Entry(), EveryStringAndNoAttachment, Array.Empty<VaultViewField>(), new[] { attachment }, null);
            var none = new VaultEntryView(
                Entry(), Sights.Whole, Array.Empty<VaultViewField>(), Array.Empty<VaultViewAttachment>(), null);

            Assert.That(hidden.Attachments, Is.Null);
            Assert.That(hidden.Attachment("notes.txt"), Is.Null, "so nothing rendered from the view can reach it");
            Assert.That(none.Attachments, Is.Empty, "an entry holding none, to a caller that may see them, is an empty list");
            Assert.That(read, Is.Zero);
        }

        [Test]
        public void An_attachment_larger_than_the_gate_serves_is_measured_and_neither_read_nor_digested()
        {
            var read = 0;
            Func<byte[]> counted = () =>
            {
                read++;
                return new byte[] { 1 };
            };

            var over = new VaultViewAttachment("big.bin", VaultViewAttachment.MostServed + 1, Sights.Whole, counted, counted);
            var largest = new VaultViewAttachment("largest.bin", VaultViewAttachment.MostServed, Sights.Whole, counted, counted);

            Assert.That(over.Size, Is.EqualTo(VaultViewAttachment.MostServed + 1));
            Assert.That(over.Content, Is.Null);
            Assert.That(over.Digest, Is.Null);
            Assert.That(read, Is.Zero);

            Assert.That(largest.Content, Is.Not.Null);
            Assert.That(largest.Content, Is.Not.Null);
            Assert.That(read, Is.EqualTo(1), "an attachment the gate serves is read when asked for, and once");
            Assert.That(largest.Digest, Is.Not.Null);
        }

        [Test]
        public void A_tag_is_taken_of_the_view_when_it_is_first_asked_for_and_once()
        {
            var taken = new List<VaultEntryView>();
            var view = new VaultEntryView(
                Entry(),
                Sights.Whole,
                Array.Empty<VaultViewField>(),
                Array.Empty<VaultViewAttachment>(),
                asked =>
                {
                    taken.Add(asked);
                    return "0123456789abcdef0123456789abcdef";
                });

            Assert.That(taken, Is.Empty, "a view nobody asks a tag of digests nothing");

            Assert.That(view.Tag, Is.EqualTo("0123456789abcdef0123456789abcdef"));
            Assert.That(view.Tag, Is.EqualTo("0123456789abcdef0123456789abcdef"));
            Assert.That(taken, Is.EqualTo(new[] { view }), "taken once, and of this view");
        }

        [Test]
        public void A_tag_that_could_not_be_taken_is_taken_again_by_the_next_asking()
        {
            var failing = true;
            var view = new VaultEntryView(
                Entry(),
                Sights.Whole,
                Array.Empty<VaultViewField>(),
                Array.Empty<VaultViewAttachment>(),
                asked =>
                {
                    if (failing)
                    {
                        throw new InvalidOperationException("an invented tag that fails");
                    }

                    return "0123456789abcdef0123456789abcdef";
                });

            Assert.That(() => view.Tag, Throws.InvalidOperationException);
            failing = false;

            Assert.That(view.Tag, Is.EqualTo("0123456789abcdef0123456789abcdef"));
        }

        [Test]
        public void The_vault_asks_the_sight_once_of_the_entry_it_found()
        {
            var vault = new FakeVault();
            var entry = vault.AddEntry(vault.RootUuid, "an invented service");
            var asked = new List<VaultFound>();

            var view = vault.View(entry, found =>
            {
                asked.Add(found);
                return Sights.Whole;
            });

            Assert.That(asked, Has.Count.EqualTo(1));
            Assert.That(asked[0].Entry.Uuid, Is.EqualTo(entry));
            Assert.That(asked[0].Guarded, Is.False);
            Assert.That(view.Entry.Uuid, Is.EqualTo(entry));
            Assert.That(view.Entry.Title, Is.Null, "a title is in a view as a field or not at all");
            Assert.That(view.Entry.Url, Is.Null);

            vault.Plant(entry, "Password", "not-a-real-password");
            vault.View(entry, found =>
            {
                asked.Add(found);
                return Sights.Whole;
            });

            Assert.That(asked[1].Guarded, Is.False,
                "a view names no string, so how far its caller reads is asked whatever the entry holds");
            Assert.That(vault.View("00000000000000000000000000000000", found => Sights.Whole), Is.Null,
                "an entry that is not there has no view");
        }

        [Test]
        public void A_view_the_vault_is_asked_for_with_no_sight_withholds()
        {
            var vault = new FakeVault();
            var entry = vault.AddEntry(vault.RootUuid, "an invented service");
            vault.Plant(entry, "Password", "not-a-real-password");
            Assert.That(vault.CreateAttachment(entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            foreach (var view in new[] { vault.View(entry, null), vault.View(entry, found => null) })
            {
                Assert.That(view.Field("Password").IsWithheld, Is.True);
                Assert.That(view.Field("Password").Value, Is.Null);
                Assert.That(view.Field("Title").IsWithheld, Is.True);
                Assert.That(view.Field("Title").Value, Is.Null, "a string nothing protects among them");
                Assert.That(view.Attachments, Is.Null);
                Assert.That(view.Tag, Is.Null, "and is handed no tag");
            }

            Assert.That(vault.StringReads, Is.Zero);
        }

        [Test]
        public void An_entry_holding_no_attachment_has_the_same_tag_however_far_the_sight_reaches_into_attachments()
        {
            // Nothing about the rule is in a tag, so a rule widened over an entry where nothing
            // new comes into sight moves no tag: the stand-in vault, held to the real digest.
            var vault = new FakeVault();
            var entry = vault.AddEntry(vault.RootUuid, "an invented service");

            var none = new VaultSight(true, true, VaultAttachmentSight.None);
            var listed = new VaultSight(true, true, VaultAttachmentSight.Listed);

            var tag = vault.View(entry, found => none).Tag;
            Assert.That(tag, Is.Not.Null);
            Assert.That(vault.View(entry, found => listed).Tag, Is.EqualTo(tag));
            Assert.That(vault.Whole(entry).Tag, Is.EqualTo(tag));

            Assert.That(vault.CreateAttachment(entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(vault.View(entry, found => none).Tag, Is.EqualTo(tag), "still nothing in this view");
            Assert.That(
                new[] { tag, vault.View(entry, found => listed).Tag, vault.Whole(entry).Tag },
                Is.Unique,
                "and an attachment is in each view as far as the view holds it");
        }

        [Test]
        public void A_withheld_value_and_attachments_out_of_sight_never_move_the_tag_of_their_view()
        {
            // The stand-in vault tags a view from the view alone, as the real vault does, which is
            // what lets the handlers' tests rest on it. The real digest is pinned in the host
            // tier, over real KeePass objects.
            var vault = new FakeVault();
            var entry = vault.AddEntry(vault.RootUuid, "an invented service");
            vault.Plant(entry, "Password", "not-a-real-password");
            Assert.That(vault.CreateAttachment(entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            var blind = vault.View(entry, found => OpenStringsAndAttachments).Tag;
            var bare = vault.View(entry, found => EveryStringAndNoAttachment).Tag;
            var whole = vault.Whole(entry).Tag;
            Assert.That(new[] { blind, bare, whole }, Is.Unique, "a view's tag is of what that view holds");

            vault.Plant(entry, "Password", "another, and longer");
            Assert.That(vault.View(entry, found => OpenStringsAndAttachments).Tag, Is.EqualTo(blind));
            Assert.That(vault.View(entry, found => EveryStringAndNoAttachment).Tag, Is.Not.EqualTo(bare));
            Assert.That(vault.Whole(entry).Tag, Is.Not.EqualTo(whole));

            bare = vault.View(entry, found => EveryStringAndNoAttachment).Tag;
            blind = vault.View(entry, found => OpenStringsAndAttachments).Tag;
            Assert.That(vault.UpdateAttachment(entry, "notes.txt", new byte[] { 2, 3 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(vault.View(entry, found => EveryStringAndNoAttachment).Tag, Is.EqualTo(bare));
            Assert.That(vault.View(entry, found => OpenStringsAndAttachments).Tag, Is.Not.EqualTo(blind));
        }

        [Test]
        public void Attachments_listed_and_not_read_move_the_tag_of_their_view_by_name_and_size_alone()
        {
            // The stand-in vault again, held to the real vault's three levels.
            var vault = new FakeVault();
            var entry = vault.AddEntry(vault.RootUuid, "an invented service");
            Assert.That(vault.CreateAttachment(entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            var listing = new VaultSight(true, true, VaultAttachmentSight.Listed);
            var listed = vault.View(entry, found => listing);
            Assert.That(listed.Attachment("notes.txt").Size, Is.EqualTo(1));
            Assert.That(listed.Attachment("notes.txt").Content, Is.Null);
            Assert.That(
                new[] { listed.Tag, vault.View(entry, found => EveryStringAndNoAttachment).Tag, vault.Whole(entry).Tag },
                Is.Unique);

            var whole = vault.Whole(entry).Tag;
            Assert.That(vault.UpdateAttachment(entry, "notes.txt", new byte[] { 2 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(vault.View(entry, found => listing).Tag, Is.EqualTo(listed.Tag), "other bytes of the same size");
            Assert.That(vault.Whole(entry).Tag, Is.Not.EqualTo(whole));

            Assert.That(vault.UpdateAttachment(entry, "notes.txt", new byte[] { 2, 3 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(vault.View(entry, found => listing).Tag, Is.Not.EqualTo(listed.Tag), "another size");

            var resized = vault.View(entry, found => listing).Tag;
            Assert.That(vault.CreateAttachment(entry, "more.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(vault.View(entry, found => listing).Tag, Is.Not.EqualTo(resized), "another name");
        }

        private static VaultEntry Entry()
        {
            return new VaultEntry(
                "0123456789ABCDEF0123456789ABCDEF",
                "FEDCBA9876543210FEDCBA9876543210",
                "an invented service",
                string.Empty,
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                null,
                false,
                false,
                null);
        }

        private static VaultViewAttachment Attachment(string name, int size)
        {
            return new VaultViewAttachment(name, size, Sights.Whole, () => new byte[size], () => new byte[32]);
        }
    }
}
