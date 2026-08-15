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
        public void One_letter_names_one_verb_and_it_is_the_first_that_wanted_it()
        {
            // Nothing declared today collides. The rule matters for what is declared tomorrow: a
            // verb added later that starts with a letter already spoken for gets no key, rather
            // than taking one away from a verb operators have been pressing for years.
            var keys = VerbKeys.All();

            foreach (var pair in keys)
            {
                Assert.That(VerbKeys.PositionOf(pair.Key), Is.GreaterThanOrEqualTo(0));
                Assert.That(AclVerbs.Grantable[VerbKeys.PositionOf(pair.Key)], Is.EqualTo(pair.Value));
            }

            Assert.That(keys.Count, Is.EqualTo(AclVerbs.Grantable.Count),
                "two verbs share a first letter, so one of them can no longer be typed and the "
                + "mask has two positions spelled the same");
        }
    }
}
