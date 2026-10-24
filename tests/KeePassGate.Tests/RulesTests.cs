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
            Assert.That(Rules.Granting("agent", letters), Is.EqualTo(Rules.Granting("agent", places)));
        }

        [Test]
        public void A_rule_is_written_as_the_editor_writes_it()
        {
            var grants = new Dictionary<string, IEnumerable<AclVerb>>
            {
                { "agent", new[] { AclVerb.Read, AclVerb.List } },
                { "editor", new[] { AclVerb.Read, AclVerb.Update } },
            };

            Assert.That(Rules.Granting("agent", "RL", "editor", "RU"),
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
        [TestCase((string)null, TestName = "no mask at all")]
        public void Anything_else_is_refused_as_the_test_sets_up(string mask)
        {
            Assert.That(() => Rules.Granting("agent", mask), Throws.ArgumentException);
        }

        [Test]
        public void A_profile_without_a_mask_or_named_twice_is_refused()
        {
            Assert.That(() => Rules.Granting("agent", "R", "editor"), Throws.ArgumentException);
            Assert.That(() => Rules.Granting("agent", "R", "agent", "L"), Throws.ArgumentException);
        }
    }
}
