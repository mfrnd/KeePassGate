using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a change does about its node's tag and about an update that would change nothing,
    /// read off how far its caller may read.
    /// </summary>
    /// <remarks>
    /// <see cref="VaultAsking.Tagging"/> is where a sight becomes the three things a vault acts
    /// on: the condition asked of the node, whether the answer carries a tag, and the sight whose
    /// view decides whether the caller reads what the change names. Held here on its own, apart
    /// from any vault, so that a
    /// missing answer is seen to fail closed in each of them. Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class VaultAskingTests
    {
        private static readonly VaultSight Reading = new VaultSight(true, false, VaultAttachmentSight.None);

        private static readonly VaultSight ReadingProtected = new VaultSight(true, true, VaultAttachmentSight.None);

        private static readonly VaultSight ReadingAttachments = new VaultSight(true, false, VaultAttachmentSight.Read);

        private static readonly VaultSight ListingAttachments = new VaultSight(true, false, VaultAttachmentSight.Listed);

        private static readonly VaultSight ListingOnly = new VaultSight(false, false, VaultAttachmentSight.Listed, true);

        private static readonly VaultSight ReadingBesideListingProtected =
            new VaultSight(true, false, VaultAttachmentSight.None, true);

        [Test]
        public void A_sight_nobody_answers_is_the_least_and_fails_closed_in_every_part()
        {
            var condition = VaultPrecondition.Read("*");
            var askings = new[]
            {
                new VaultAsking(found => true, null, condition, true),
                new VaultAsking(found => true, found => null, condition, true),
                new VaultAsking(found => true, found => VaultSight.Least, condition, true),
                new VaultAsking(found => true, found => ListingOnly, condition, true),
            };

            foreach (var tagging in askings.Select(asking => asking.Tagging(Open())))
            {
                Assert.That(tagging.Sight.Reads, Is.False);
                Assert.That(tagging.Answers, Is.False, "no tag in the answer for a caller that does not read");
                Assert.That(tagging.Precondition.Refusal, Is.EqualTo(VaultPrecondition.Withheld().Refusal),
                    "and its condition, `*` included, is a condition nothing meets");
                Assert.That(VaultPrecondition.Fails(tagging.Precondition, () => "0123456789abcdef0123456789abcdef"), Is.True);
            }

            var unconditional = new VaultAsking(found => true, null, null, true).Tagging(Open());
            Assert.That(unconditional.Precondition, Is.Null, "a caller that asked no condition is asked none");
            Assert.That(unconditional.Sight, Is.SameAs(VaultSight.Least));
        }

        [Test]
        public void Nothing_found_is_nothing_in_sight_whatever_the_sight_would_answer()
        {
            var asked = 0;
            var asking = new VaultAsking(
                found => true,
                found =>
                {
                    asked++;
                    return ReadingProtected;
                },
                VaultPrecondition.Read("*"),
                true);

            var tagging = asking.Tagging(null);

            Assert.That(asked, Is.Zero);
            Assert.That(tagging.Sight, Is.SameAs(VaultSight.Least));
            Assert.That(tagging.Answers, Is.False);
            Assert.That(tagging.Precondition.Refusal, Is.EqualTo(VaultPrecondition.Withheld().Refusal));
            Assert.That(asking.Allows(null), Is.False);
        }

        [Test]
        public void A_sight_that_reads_keeps_the_condition_asked_and_answers_a_tag_only_where_one_was_asked_for()
        {
            var condition = VaultPrecondition.Read("\"0123456789abcdef0123456789abcdef\"");

            var answering = new VaultAsking(found => true, found => Reading, condition, true).Tagging(Open());
            Assert.That(answering.Precondition, Is.SameAs(condition));
            Assert.That(answering.Answers, Is.True);
            Assert.That(answering.Sight, Is.SameAs(Reading));

            var silent = new VaultAsking(found => true, found => Reading, condition, false).Tagging(Open());
            Assert.That(silent.Precondition, Is.SameAs(condition));
            Assert.That(silent.Answers, Is.False, "a change that answers with no tag answers with none to a reader too");
        }

        [Test]
        public void The_sight_is_asked_once_for_each_asking_of_the_node_it_was_asked_about()
        {
            var asked = new List<VaultFound>();
            var asking = new VaultAsking(
                found => true,
                found =>
                {
                    asked.Add(found);
                    return Reading;
                },
                null,
                true);

            var open = Open();
            var guarded = Guarded();
            asking.Tagging(open);
            asking.Tagging(guarded);

            Assert.That(asked, Is.EqualTo(new[] { open, guarded }));
        }

        [Test]
        public void A_caller_reads_what_a_change_names_as_far_as_its_view_holds()
        {
            // What decides whether an update that would change nothing is skipped, read off the
            // view the change found and not worked out again from the sight: every named string
            // the entry carries has its value in the view.
            var open = new[] { "Account" };
            var secret = new[] { "Password" };
            var title = new[] { "Title" };

            Assert.That(Viewed(Reading).Reads(open), Is.True);
            Assert.That(Viewed(Reading).Reads(secret), Is.False);
            Assert.That(Viewed(Reading).Reads(new[] { "Account", "Password" }), Is.False, "the named values, every one");
            Assert.That(Viewed(ReadingProtected).Reads(secret), Is.True);
            Assert.That(Viewed(ReadingAttachments).Reads(secret), Is.False, "the other switch opens no string");
            Assert.That(Viewed(Reading).Reads(new[] { "Absent" }), Is.True, "a name the entry does not carry is no value to read");
            Assert.That(Viewed(Reading).Reads(null), Is.True, "the entry's own facts, its expiry among them");
            Assert.That(Viewed(Reading).Reads(Array.Empty<string>()), Is.True);

            // A protected title is a value its caller reads where a listing shows it to that
            // caller, and the password beside it is not.
            Assert.That(Viewed(Reading).Reads(title), Is.False);
            Assert.That(Viewed(ReadingBesideListingProtected).Reads(title), Is.True);
            Assert.That(Viewed(ReadingBesideListingProtected).Reads(secret), Is.False);

            // No value at all for a sight that does not read, an open string among them.
            foreach (var view in new[] { null, VaultSight.Least, ListingOnly }.Select(Viewed))
            {
                Assert.That(view.Reads(open), Is.False);
                Assert.That(view.Reads(null), Is.False);
                Assert.That(view.ReadsAttachment("notes.txt"), Is.False);
            }

            // An attachment's bytes: a view holding them, and nothing short of it.
            Assert.That(Viewed(ReadingAttachments).ReadsAttachment("notes.txt"), Is.True);
            Assert.That(Viewed(ReadingAttachments).ReadsAttachment("absent.txt"), Is.False);
            Assert.That(Viewed(ListingAttachments).ReadsAttachment("notes.txt"), Is.False, "listed is names and sizes");
            Assert.That(Viewed(ReadingProtected).ReadsAttachment("notes.txt"), Is.False);
        }

        /// <summary>
        /// An invented entry as this sight reaches into it: a protected title, a protected
        /// password, an open string and an attachment.
        /// </summary>
        private static VaultEntryView Viewed(VaultSight sight)
        {
            return new VaultEntryView(
                Entry(),
                sight,
                new[]
                {
                    new VaultViewField("Title", true, sight, () => "abc", true),
                    new VaultViewField("Password", true, sight, () => "abc"),
                    new VaultViewField("Account", false, sight, () => "abc"),
                },
                new[] { new VaultViewAttachment("notes.txt", 3, sight, () => new byte[3], () => new byte[32]) },
                null);
        }

        /// <summary>An entry found by a change that names no protected string.</summary>
        private static VaultFound Open()
        {
            return new VaultFound(Entry(), false);
        }

        /// <summary>An entry found by a change that names a protected string.</summary>
        private static VaultFound Guarded()
        {
            return new VaultFound(Entry(), true);
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
    }
}
