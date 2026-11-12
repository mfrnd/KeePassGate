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
    /// on: the condition asked of the node, whether the answer carries a tag, and whether the
    /// caller reads what the change names. Held here on its own, apart from any vault, so that a
    /// missing answer is seen to fail closed in each of them. Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class VaultAskingTests
    {
        private static readonly VaultSight Reading = new VaultSight(true, false, VaultAttachmentSight.None);

        private static readonly VaultSight ReadingProtected = new VaultSight(true, true, VaultAttachmentSight.None);

        private static readonly VaultSight ReadingAttachments = new VaultSight(true, false, VaultAttachmentSight.Read);

        private static readonly VaultSight ListingAttachments = new VaultSight(true, false, VaultAttachmentSight.Listed);

        private static readonly VaultSight ListingOnly = new VaultSight(false, false, VaultAttachmentSight.Listed);

        [Test]
        public void A_sight_nobody_answers_is_the_least_and_fails_closed_in_every_part()
        {
            var condition = VaultPrecondition.Read("*");
            var askings = new[]
            {
                new VaultAsking(found => true, null, condition, true),
                new VaultAsking(found => true, found => null, condition, true),
                new VaultAsking(found => true, found => VaultSight.Least, condition, true),
                new VaultAsking(found => true, found => ListingOnly, condition, true, true),
            };

            foreach (var tagging in askings.Select(asking => asking.Tagging(Open())))
            {
                Assert.That(tagging.Sight.Reads, Is.False);
                Assert.That(tagging.Answers, Is.False, "no tag in the answer for a caller that does not read");
                Assert.That(tagging.Sees, Is.False);
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
            Assert.That(tagging.Sees, Is.False);
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
        public void A_caller_reads_what_a_change_names_as_far_as_its_sight_reaches()
        {
            // Strings and a group's name and notes: a sight that reads, reaching protected
            // values where a named string is protected.
            Assert.That(Sees(Reading, Open(), false), Is.True);
            Assert.That(Sees(Reading, Guarded(), false), Is.False);
            Assert.That(Sees(ReadingProtected, Guarded(), false), Is.True);
            Assert.That(Sees(ReadingAttachments, Guarded(), false), Is.False, "the other switch opens no string");
            Assert.That(Sees(Reading, new VaultFound(Group()), false), Is.True);
            Assert.That(Sees(VaultSight.Least, new VaultFound(Group()), false), Is.False);

            // An attachment's bytes: a sight reading attachments, and nothing short of it.
            Assert.That(Sees(ReadingAttachments, Open(), true), Is.True);
            Assert.That(Sees(ListingAttachments, Open(), true), Is.False, "listed is names and sizes");
            Assert.That(Sees(ReadingProtected, Open(), true), Is.False);
            Assert.That(Sees(ListingOnly, Open(), true), Is.False);
        }

        private static bool Sees(VaultSight sight, VaultFound found, bool attachment)
        {
            return new VaultAsking(asked => true, asked => sight, null, false, attachment).Tagging(found).Sees;
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

        private static VaultGroup Group()
        {
            return new VaultGroup(
                "FEDCBA9876543210FEDCBA9876543210",
                "Fictitious",
                "invented notes",
                null,
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                null);
        }
    }
}
