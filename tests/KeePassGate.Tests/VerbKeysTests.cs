using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Which verb a letter toggles in the rule picker.
    /// </summary>
    /// <remarks>
    /// The letter is the verb's own first letter, which is also the letter it occupies in a mask,
    /// so the keyboard and the column agree by construction rather than by a table somebody has to
    /// keep in step. What is worth pinning is that it stays derived: a verb added later must get
    /// its key without anybody editing a list, and must not take one away from a verb operators
    /// have been pressing for years.
    /// </remarks>
    [TestFixture]
    public sealed class VerbKeysTests
    {
        [Test]
        public void Every_verb_is_reachable_by_its_own_first_letter()
        {
            for (var i = 0; i < AclVerbs.Grantable.Count; i++)
            {
                var verb = AclVerbs.Grantable[i];
                var letter = VerbMask.LetterOf(verb);

                Assert.That(VerbKeys.PositionOf(letter), Is.EqualTo(i),
                    AclVerbs.Write(verb) + " cannot be typed");
            }
        }

        [Test]
        public void The_letters_are_the_mask_the_column_shows()
        {
            // So a rule spelled on the keyboard reads back in exactly the characters that were
            // pressed, which is the whole reason the mapping is derived rather than written down.
            Assert.That(VerbKeys.PositionOf('C'), Is.EqualTo(0), "create");
            Assert.That(VerbKeys.PositionOf('R'), Is.EqualTo(1), "read");
            Assert.That(VerbKeys.PositionOf('U'), Is.EqualTo(2), "update");
            Assert.That(VerbKeys.PositionOf('S'), Is.EqualTo(3), "strip");
            Assert.That(VerbKeys.PositionOf('D'), Is.EqualTo(4), "delete");
            Assert.That(VerbKeys.PositionOf('L'), Is.EqualTo(5), "list");
            Assert.That(VerbKeys.PositionOf('A'), Is.EqualTo(6), "attachments");
            Assert.That(VerbKeys.PositionOf('P'), Is.EqualTo(7), "protected");
            Assert.That(VerbMask.Letters, Is.EqualTo("CRUSDLAP"));
        }

        [Test]
        public void A_letter_is_taken_in_either_case()
        {
            Assert.That(VerbKeys.PositionOf('r'), Is.EqualTo(VerbKeys.PositionOf('R')));
        }

        [Test]
        public void A_letter_that_names_nothing_toggles_nothing()
        {
            Assert.That(VerbKeys.PositionOf('Z'), Is.EqualTo(-1));
            Assert.That(VerbKeys.PositionOf('7'), Is.EqualTo(-1));
        }

        [Test]
        public void No_two_verbs_share_a_letter_and_each_letter_toggles_its_own_verb()
        {
            // A verb added later that starts with a letter already spoken for would leave one of
            // the two with no key to type and the mask with two positions spelled the same.
            var letters = AclVerbs.Grantable.Select(VerbMask.LetterOf).ToList();

            Assert.That(letters, Is.Unique,
                "two verbs share a first letter, so one of them can no longer be typed and the "
                + "mask has two positions spelled the same");

            for (var i = 0; i < letters.Count; i++)
            {
                Assert.That(VerbKeys.PositionOf(letters[i]), Is.EqualTo(i));
            }
        }
    }
}
