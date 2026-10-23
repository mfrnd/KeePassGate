using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>Reading an `If-Match` the way RFC 9110 section 13.1.1 has it, and failing closed.</summary>
    [TestFixture]
    public sealed class VaultPreconditionTests
    {
        private const string Tag = "3f9c0e7a1b2d4c5e6f708192a3b4c5d6";

        [Test]
        public void No_field_asks_for_nothing()
        {
            Assert.That(VaultPrecondition.Read(null), Is.Null);
        }

        [TestCase("\"" + Tag + "\"")]
        [TestCase("  \"" + Tag + "\"  ")]
        [TestCase("\"0000\", \"" + Tag + "\"")]
        [TestCase("\"0000\",\"" + Tag + "\",")]
        [TestCase(", ,\"" + Tag + "\"")]
        public void A_tag_in_the_list_is_met(string field)
        {
            Assert.That(VaultPrecondition.Read(field).Admits(Tag), Is.True, field);
        }

        [Test]
        public void A_tag_not_in_the_list_is_not()
        {
            Assert.That(VaultPrecondition.Read("\"0000\", \"1111\"").Admits(Tag), Is.False);
        }

        [Test]
        public void A_weak_tag_never_matches()
        {
            // Strong comparison, which `If-Match` asks for: this gate hands out no weak tags, so
            // one is a tag from somewhere else.
            Assert.That(VaultPrecondition.Read("W/\"" + Tag + "\"").Admits(Tag), Is.False);
            Assert.That(VaultPrecondition.Read("W/\"" + Tag + "\", \"" + Tag + "\"").Admits(Tag), Is.True,
                "and does not spoil a strong one beside it");
        }

        [Test]
        public void A_star_is_met_by_anything_that_is_there()
        {
            // With nothing around it, as the request reader hands every value on: it trims the
            // spaces and tabs around a field before anything here sees it.
            var any = VaultPrecondition.Read("*");

            Assert.That(any.Admits(Tag), Is.True);
            Assert.That(any.Admits(null), Is.False, "and not by a node that is not there");
        }

        [Test]
        public void No_precondition_refuses_nothing_and_one_refuses_what_it_does_not_admit()
        {
            Assert.That(VaultPrecondition.Fails(null, Tag), Is.False);
            Assert.That(VaultPrecondition.Fails(null, null), Is.False, "not even a node with no tag");
            Assert.That(VaultPrecondition.Fails(VaultPrecondition.Read("\"" + Tag + "\""), Tag), Is.False);
            Assert.That(VaultPrecondition.Fails(VaultPrecondition.Read("\"0000\""), Tag), Is.True);
            Assert.That(VaultPrecondition.Fails(VaultPrecondition.Read("*"), null), Is.True);
        }

        [Test]
        public void Nothing_is_met_by_no_tag_at_all()
        {
            // What a client that may not be handed a tag has its condition replaced with.
            Assert.That(VaultPrecondition.Nothing().Admits(Tag), Is.False);
            Assert.That(VaultPrecondition.Nothing().Admits(null), Is.False);
        }

        [TestCase("")]
        [TestCase(",")]
        [TestCase(Tag)]
        [TestCase("\"" + Tag)]
        [TestCase("\"" + Tag + "\" junk")]
        [TestCase("\"a b\"")]
        [TestCase("\"" + Tag + "\"\"" + Tag + "\"")]
        [TestCase("*, \"" + Tag + "\"")]
        [TestCase("w/\"" + Tag + "\"")]
        public void A_field_that_will_not_read_is_met_by_nothing(string field)
        {
            // Every way of ignoring a condition ends with the write going ahead, which is the one
            // thing a client that sent one did not ask for.
            var read = VaultPrecondition.Read(field);

            Assert.That(read, Is.Not.Null, field);
            Assert.That(read.Admits(Tag), Is.False, field);
        }
    }
}
