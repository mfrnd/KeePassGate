using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The helper the tests write their rules with, held to the two forms of a mask it takes.
    /// </summary>
    /// <remarks>
    /// A helper that read a mask generously would set up a rule its test never meant, which is the
    /// failure it exists to take away, so what it refuses is pinned as closely as what it reads.
    /// </remarks>
    [TestFixture]
    public sealed class RulesTests
    {
        [TestCase("R", ".R......")]
        [TestCase("RL", ".R...L..")]
        [TestCase("CRUSDLA", "CRUSDLA.")]
        [TestCase("AP", "......AP")]
        [TestCase("", "........")]
        public void Only_the_letters_say_what_all_eight_places_do(string letters, string places)
        {
            Assert.That(Rules.Granting(("agent", letters)), Is.EqualTo(Rules.Granting(("agent", places))));
        }

        [Test]
        public void A_rule_is_written_as_the_editor_writes_it()
        {
            var grants = new Dictionary<string, IEnumerable<AclVerb>>
            {
                { "agent", new[] { AclVerb.Read, AclVerb.List } },
                { "editor", new[] { AclVerb.Read, AclVerb.Update } },
            };

            Assert.That(Rules.Granting(("agent", "RL"), ("editor", "RU")),
                Is.EqualTo(AclDocument.Of(AclDefault.Inherit, grants).Write()));
            Assert.That(Rules.Sealing(), Is.EqualTo(AclDocument.Of(AclDefault.Deny, null).Write()));
        }

        [TestCase("LR", TestName = "letters out of order")]
        [TestCase(".L...R..", TestName = "a letter in another verb's place")]
        [TestCase("RR", TestName = "a letter twice")]
        [TestCase(".RL", TestName = "a short form with a dot in it")]
        [TestCase(".R.....", TestName = "seven places")]
        [TestCase("rl", TestName = "lower case")]
        [TestCase("X", TestName = "a letter that names no verb")]
        [TestCase(" R", TestName = "a space")]
        [TestCase(null, TestName = "no mask at all")]
        public void Anything_else_is_refused_as_the_test_sets_up(string mask)
        {
            Assert.That(() => Rules.Granting(("agent", mask)), Throws.ArgumentException);
        }

        [Test]
        public void A_rule_naming_no_profile_or_one_named_already_is_refused()
        {
            Assert.That(() => Rules.Granting((null, "R")), Throws.ArgumentException);
            Assert.That(() => Rules.Sealing(("agent", "R"), (null, "L")), Throws.ArgumentException);
            Assert.That(() => Rules.Granting(("agent", "R"), ("agent", "L")), Throws.ArgumentException);
        }

        [TestCase("", TestName = "an empty profile name")]
        [TestCase("  ", TestName = "a profile name of spaces")]
        [TestCase(" reader ", TestName = "a profile name with spaces round it")]
        [TestCase("read only", TestName = "a profile name with a space in it")]
        [TestCase("agent\t", TestName = "a profile name holding a control character")]
        [TestCase("Default", TestName = "the reserved word in another case")]
        public void A_profile_name_the_editor_refuses_is_refused_as_the_test_sets_up(string profile)
        {
            // Some of these make a document the reader refuses, which refuses wherever it is found,
            // and the rest a document it reads and no operator could have written.
            Assert.That(() => Rules.Granting((profile, "R")), Throws.ArgumentException);
        }

        [Test]
        public void A_document_the_reader_would_refuse_is_refused_as_the_test_sets_up()
        {
            Assert.That(AclDocument.Read(Rules.Numbered(AclDocument.MostProfiles, "R")), Is.Not.Null,
                "as many profiles as the reader takes");
            Assert.That(() => Rules.Numbered(AclDocument.MostProfiles + 1, "R"), Throws.ArgumentException);
        }

        [Test]
        public void No_rules_is_no_document_unless_the_node_is_sealed()
        {
            // The editor takes the key away when the last rule goes, so a document asking the group
            // above for everything is a document no node holds.
            Assert.That(() => Rules.Granting(), Throws.ArgumentException);
            Assert.That(() => Rules.Numbered(0, "R"), Throws.ArgumentException);
            Assert.That(AclDocument.Read(Rules.Sealing()), Is.Not.Null, "a seal is a rule on its own");
        }
    }
}
