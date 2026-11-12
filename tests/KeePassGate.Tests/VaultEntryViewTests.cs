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
        private static readonly VaultSight OpenStringsAndAttachments = new VaultSight(false, true);

        private static readonly VaultSight EveryStringAndNoAttachment = new VaultSight(true, false);

        [Test]
        public void A_protected_string_out_of_sight_keeps_its_name_and_holds_no_value()
        {
            var reads = 0;
            var field = new VaultViewField("Password", true, 19, OpenStringsAndAttachments, () =>
            {
                reads++;
                return "not-a-real-password";
            });

            Assert.That(field.Name, Is.EqualTo("Password"));
            Assert.That(field.IsProtected, Is.True);
            Assert.That(field.IsWithheld, Is.True);
            Assert.That(field.Value, Is.Null);
            Assert.That(reads, Is.Zero, "the reader handed in was dropped, so nothing can read the value through the view");
            Assert.That(field.Characters, Is.EqualTo("Password".Length + 19), "and it still counts toward the entry's size");
        }

        [Test]
        public void A_withheld_string_needs_no_reader_and_a_string_in_sight_refuses_to_be_made_without_one()
        {
            Assert.That(new VaultViewField("Password", true, 3, VaultSight.Least, null).Value, Is.Null);
            Assert.Throws<ArgumentNullException>(() => new VaultViewField("Title", false, 3, VaultSight.Least, null));
            Assert.Throws<ArgumentNullException>(() => new VaultViewField("Password", true, 3, VaultSight.Whole, null));
        }

        [Test]
        public void No_sight_at_all_is_the_least_sight()
        {
            var guarded = new VaultViewField("Password", true, 3, null, () => "abc");
            var open = new VaultViewField("Title", false, 3, null, () => "abc");
            var view = new VaultEntryView(
                Entry(), null, new[] { guarded, open }, new[] { Attachment("notes.txt", 3) }, null);

            Assert.That(guarded.IsWithheld, Is.True);
            Assert.That(open.Value, Is.EqualTo("abc"), "a string nothing protects is in every sight");
            Assert.That(view.Attachments, Is.Null);
            Assert.That(VaultSight.Least.Protected || VaultSight.Least.Attachments, Is.False);
            Assert.That(VaultSight.Whole.Protected && VaultSight.Whole.Attachments, Is.True);
        }

        [Test]
        public void A_value_is_read_when_it_is_first_asked_for_and_once()
        {
            var reads = 0;
            var field = new VaultViewField("Title", false, 7, VaultSight.Least, () =>
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
        public void A_value_that_could_not_be_read_is_read_again_by_the_next_asking()
        {
            var failing = true;
            var field = new VaultViewField("Title", false, 7, VaultSight.Whole, () =>
            {
                if (failing)
                {
                    throw new InvalidOperationException("an invented read that fails");
                }

                return "a title";
            });

            Assert.Throws<InvalidOperationException>(() => { var unused = field.Value; });
            failing = false;

            Assert.That(field.Value, Is.EqualTo("a title"));
        }

        [Test]
        public void An_entry_is_measured_with_every_string_counted_and_none_read()
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
                    new VaultViewField("Title", false, 3, OpenStringsAndAttachments, counted),
                    new VaultViewField("Password", true, 40, OpenStringsAndAttachments, counted),
                    new VaultViewField("PIN", true, 4, OpenStringsAndAttachments, counted),
                },
                Array.Empty<VaultViewAttachment>(),
                null);

            Assert.That(view.Characters, Is.EqualTo("Title".Length + 3 + "Password".Length + 40 + "PIN".Length + 4),
                "what the ceiling is asked about is the whole entry, in sight or not");
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
                VaultSight.Whole,
                new[] { new VaultViewField("Title", false, 3, VaultSight.Whole, () => "abc") },
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
                Entry(), VaultSight.Whole, Array.Empty<VaultViewField>(), Array.Empty<VaultViewAttachment>(), null);

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

            var over = new VaultViewAttachment("big.bin", VaultViewAttachment.MostServed + 1, counted, counted);
            var largest = new VaultViewAttachment("largest.bin", VaultViewAttachment.MostServed, counted, counted);

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
                VaultSight.Whole,
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
                VaultSight.Whole,
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

            Assert.Throws<InvalidOperationException>(() => { var unused = view.Tag; });
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
                return VaultSight.Whole;
            });

            Assert.That(asked, Has.Count.EqualTo(1));
            Assert.That(asked[0].Entry.Uuid, Is.EqualTo(entry));
            Assert.That(asked[0].Guarded, Is.False, "nothing on the entry is protected yet");
            Assert.That(view.Entry.Uuid, Is.EqualTo(entry));

            vault.Plant(entry, "Password", "not-a-real-password");
            vault.View(entry, found =>
            {
                asked.Add(found);
                return VaultSight.Whole;
            });

            Assert.That(asked[1].Guarded, Is.True, "and now a string is");
            Assert.That(vault.View("00000000000000000000000000000000", found => VaultSight.Whole), Is.Null,
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
                Assert.That(view.Field("Title").Value, Is.EqualTo("an invented service"));
                Assert.That(view.Attachments, Is.Null);
            }
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
                null,
                true);
        }

        private static VaultViewAttachment Attachment(string name, int size)
        {
            return new VaultViewAttachment(name, size, () => new byte[size], () => new byte[32]);
        }
    }
}
