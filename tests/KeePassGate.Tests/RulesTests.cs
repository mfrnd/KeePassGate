using System.Linq;
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
            Assert.That(Rules.Letters(letters).SameAs(Rules.Letters(places)), Is.True);
            Assert.That(VerbMask.Of(Rules.Letters(letters)), Is.EqualTo(places));
        }

        [Test]
        public void A_node_s_rules_are_a_version_granting_each_profile_and_a_switch_where_it_refuses()
        {
            var rule = Rules.Node(false, ("agent", "RL"), ("editor", "RU"));

            Assert.That(rule.Profiles, Is.EqualTo(new[] { "agent", "editor" }));
            Assert.That(rule.Row("agent").Versions.Single().Kind, Is.EqualTo(RowVersionKind.Grants));
            Assert.That(VerbMask.Of(rule.Row("agent").Versions.Single().Letters), Is.EqualTo(".R...L.."));
            Assert.That(rule.Row("editor").Answers, Is.True);
            Assert.That(rule.Switch.Versions, Is.Empty, "a switch never set");

            var sealing = Rules.Node(true);
            Assert.That(sealing.Profiles, Is.Empty);
            Assert.That(sealing.Switch.Versions.Single().Kind, Is.EqualTo(SwitchVersionKind.Refuses));
        }

        [Test]
        public void Stored_versions_are_held_as_written_with_identifiers_stable_for_the_same_arguments()
        {
            var rule = Rules.Stored(("agent", "off:R"), ("agent", "row:r"), (null, "sw:inherit agent=R"), ("agent", null));
            var again = Rules.Stored(("agent", "off:R"), ("agent", "row:r"), (null, "sw:inherit agent=R"), ("agent", null));

            Assert.That(rule.Row("agent").Versions.Select(version => version.Kind),
                Is.EquivalentTo(new[] { RowVersionKind.TakenOff, RowVersionKind.OutOfForm }));
            Assert.That(rule.Row("agent").Lost, Has.Count.EqualTo(1));
            Assert.That(rule.Switch.Versions.Single().Kind, Is.EqualTo(SwitchVersionKind.Inherits));
            Assert.That(rule.Row("agent").Versions.Select(version => version.Id), Is.EqualTo(again.Row("agent").Versions.Select(version => version.Id)));
            Assert.That(rule.Row("agent").Lost, Is.EqualTo(again.Row("agent").Lost));
            Assert.That(rule.Row("agent").Versions.All(version => version.Id.Length == 32 && version.Id.All(c => "0123456789abcdef".IndexOf(c) >= 0)), Is.True);

            var twice = Rules.Stored(("agent", "row:R"), ("agent", "row:R"));
            Assert.That(twice.Row("agent").Versions, Has.Count.EqualTo(2), "two versions given alike are two versions");
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
            Assert.That(() => Rules.Letters(mask), Throws.ArgumentException);
            Assert.That(() => Rules.Node(false, ("agent", mask)), Throws.ArgumentException);
        }

        [Test]
        public void A_rule_naming_no_profile_or_one_named_already_is_refused()
        {
            Assert.That(() => Rules.Node(false, (null, "R")), Throws.ArgumentException);
            Assert.That(() => Rules.Node(true, ("agent", "R"), (null, "L")), Throws.ArgumentException);
            Assert.That(() => Rules.Node(false, ("agent", "R"), ("agent", "L")), Throws.ArgumentException);
        }

        [TestCase("", TestName = "an empty profile name")]
        [TestCase("  ", TestName = "a profile name of spaces")]
        [TestCase(" reader ", TestName = "a profile name with spaces round it")]
        [TestCase("read only", TestName = "a profile name with a space in it")]
        [TestCase("agent\t", TestName = "a profile name holding a control character")]
        [TestCase("Default", TestName = "the reserved word in another case")]
        public void A_profile_name_the_editor_refuses_is_refused_as_the_test_sets_up(string profile)
        {
            Assert.That(() => Rules.Node(false, (profile, "R")), Throws.ArgumentException);
        }

        [Test]
        public void No_rules_is_no_rule_unless_the_switch_refuses()
        {
            // A node with no row and a switch never set holds nothing in the store, so a test
            // meaning such a node puts nothing on it.
            Assert.That(() => Rules.Node(false), Throws.ArgumentException);
            Assert.That(Rules.Node(true).Switch.Refuses, Is.True, "a switch that refuses is a rule on its own");
        }
    }
}
