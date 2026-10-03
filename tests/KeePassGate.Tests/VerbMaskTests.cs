using System.Collections.Generic;
using KeePassGate.Acl;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A grant as eight characters, and everything that is not one.
    /// </summary>
    /// <remarks>
    /// The mask is the only thing an operator reads a rule off, so a mask that says more than the
    /// rule does is the same defect as a document that reads more than it says. These are mostly
    /// refusals for that reason.
    /// </remarks>
    [TestFixture]
    public sealed class VerbMaskTests
    {
        [Test]
        public void The_positions_are_CRUSDLAP()
        {
            Assert.That(VerbMask.Letters, Is.EqualTo("CRUSDLAP"));
            Assert.That(VerbMask.Width, Is.EqualTo(8));
        }

        [Test]
        public void Every_letter_stands_for_exactly_one_verb()
        {
            // The letter is the verb's own first letter, so nothing chooses it separately and
            // nothing can drift. The day two verbs share an initial, this is what says so.
            var seen = new HashSet<char>();

            foreach (var verb in AclVerbs.Grantable)
            {
                var letter = VerbMask.LetterOf(verb);

                Assert.That(seen.Add(letter), Is.True,
                    "two of them want the letter " + letter + ", so the mask needs a table after all");
                Assert.That(letter, Is.EqualTo(char.ToUpperInvariant(AclVerbs.Write(verb)[0])));
            }
        }

        [Test]
        public void A_rule_reads_as_its_letters_and_dots_for_the_rest()
        {
            Assert.That(VerbMask.Of(new[] { AclVerb.Read, AclVerb.List }), Is.EqualTo(".R...L.."));
            Assert.That(VerbMask.Of(new[] { AclVerb.Read, AclVerb.List, AclVerb.Attachments }),
                Is.EqualTo(".R...LA."));
            Assert.That(VerbMask.Of(AclVerbs.Grantable), Is.EqualTo("CRUSDLAP"));
        }

        [Test]
        public void A_rule_that_grants_nothing_is_still_eight_characters()
        {
            // An empty rule is a rule, and a blank cell would read as a row nobody filled in.
            Assert.That(VerbMask.Of(System.Array.Empty<AclVerb>()), Is.EqualTo("........"));
            Assert.That(VerbMask.Of(null), Is.EqualTo("........"));
        }

        [Test]
        public void The_order_a_rule_arrives_in_makes_no_difference()
        {
            Assert.That(
                VerbMask.Of(new[] { AclVerb.Attachments, AclVerb.List, AclVerb.Read }),
                Is.EqualTo(VerbMask.Of(new[] { AclVerb.Read, AclVerb.List, AclVerb.Attachments })));
        }

        [Test]
        public void A_mask_reads_back_as_what_it_shows()
        {
            Assert.That(VerbMask.Read(".R...L.."), Is.EqualTo(new[] { AclVerb.Read, AclVerb.List }));
            Assert.That(VerbMask.Read(".R...LA."),
                Is.EqualTo(new[] { AclVerb.Read, AclVerb.List, AclVerb.Attachments }));
            Assert.That(VerbMask.Read("........"), Is.Empty);
            Assert.That(VerbMask.Read("CRUSDLAP"), Is.EqualTo(AclVerbs.Grantable));
        }

        [Test]
        public void Every_rule_survives_being_shown_and_read_again()
        {
            foreach (var verb in AclVerbs.Grantable)
            {
                var one = new[] { verb };

                Assert.That(VerbMask.Read(VerbMask.Of(one)), Is.EqualTo(one), AclVerbs.Write(verb));
            }
        }

        [Test]
        public void Case_is_not_what_a_person_typing_a_mask_is_choosing_between()
        {
            // There is one rule here, not two spellings of one, and the grid rewrites the cell to
            // the canonical mask the moment it commits.
            Assert.That(VerbMask.Read(".r...la."), Is.EqualTo(VerbMask.Read(".R...LA.")));
        }

        [Test]
        public void Space_around_a_mask_is_not_part_of_it()
        {
            Assert.That(VerbMask.Read("  .R...L.. "), Is.EqualTo(new[] { AclVerb.Read, AclVerb.List }));
        }

        [TestCase(null, TestName = "nothing")]
        [TestCase("", TestName = "empty")]
        [TestCase(".R...L.", TestName = "one short")]
        [TestCase(".R...L...", TestName = "one long")]
        [TestCase("R", TestName = "a letter on its own")]
        [TestCase("R.......", TestName = "a letter in another verb's column")]
        [TestCase(".R...AL.", TestName = "two letters swapped")]
        [TestCase(".R...LX.", TestName = "a letter nobody uses")]
        [TestCase(". R...L.", TestName = "a space inside it")]
        [TestCase("read", TestName = "the word instead")]
        public void Anything_that_is_not_exactly_a_mask_is_refused(string mask)
        {
            // A letter in the wrong column is the one worth refusing loudly. Somebody who typed it
            // meant something, and guessing which verb they meant is how an editor grants one
            // nobody chose.
            Assert.That(VerbMask.Read(mask), Is.Null);
        }

        [Test]
        public void The_last_position_is_the_protected_switch()
        {
            Assert.That(VerbMask.Of(new[] { AclVerb.Read, AclVerb.List, AclVerb.Protected }),
                Is.EqualTo(".R...L.P"));
            Assert.That(VerbMask.Read(".R...L.P"),
                Is.EqualTo(new[] { AclVerb.Read, AclVerb.List, AclVerb.Protected }));
        }

    }
}
