using KeePassGate.Acl;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The set of profiles a database names: what it reads, what it refuses, and what it stores.
    /// </summary>
    /// <remarks>
    /// This list is the only place a profile exists as a thing rather than as a coincidence of
    /// spelling, so what it will not hold matters as much as what it will. A name it accepts that
    /// the authority would not is a profile that can be granted and can never be issued.
    /// </remarks>
    [TestFixture]
    public sealed class ProfileNamesTests
    {
        [Test]
        public void A_list_that_will_not_read_is_told_apart_from_one_naming_nothing()
        {
            // Read answers both as naming nothing, which is safe for every reader that only asks
            // what is named; a renewal writes what it reads, and must not write that nothing.
            var names = ProfileNames.Empty();
            Assume.That(names.Add("reader"), Is.Null);

            Assert.That(ProfileNames.Readable(null), Is.True);
            Assert.That(ProfileNames.Readable(string.Empty), Is.True);
            Assert.That(ProfileNames.Readable(names.Write()), Is.True);
            Assert.That(ProfileNames.Readable("not a list at all"), Is.False);
            Assert.That(ProfileNames.Read("not a list at all").Names, Is.Empty);
        }

        [Test]
        public void A_database_with_no_list_names_no_profiles()
        {
            Assert.That(ProfileNames.Read(null).Names, Is.Empty);
            Assert.That(ProfileNames.Read(string.Empty).Names, Is.Empty);
        }

        [Test]
        public void The_names_are_read_back_in_one_order_whatever_order_they_were_written_in()
        {
            // A list somebody reads twice has to read the same way twice, and the operator did not
            // choose the order they were added in.
            var names = ProfileNames.Read("{\"profiles\":[\"ops\",\"agent\",\"reader\"]}");

            Assert.That(names.Names, Is.EqualTo(new[] { "agent", "ops", "reader" }));
        }

        [Test]
        public void A_document_that_will_not_read_names_nothing_rather_than_refusing()
        {
            // Nothing is granted by this list, so losing it costs an operator their pick list and
            // nobody their access. The rules on the nodes go on meaning what they meant.
            Assert.That(ProfileNames.Read("{not a document").Names, Is.Empty);
            Assert.That(ProfileNames.Read("[\"ops\"]").Names, Is.Empty);
        }

        [Test]
        public void A_name_no_certificate_could_carry_is_refused_here_as_well()
        {
            // Or it would be a profile that can be granted from the editor and never issued to a
            // client, which is a rule that silently means nothing.
            var names = ProfileNames.Empty();

            Assert.That(names.Add("has a space"), Is.Not.Null);
            Assert.That(names.Add(string.Empty), Is.Not.Null);
            Assert.That(names.Add(null), Is.Not.Null);
            Assert.That(names.Names, Is.Empty);
        }

        [Test]
        public void The_word_a_rule_reserves_cannot_be_a_profile()
        {
            // Refused by the one rule this list applies, which is the issuer's: the reservation
            // lives there because a claim carrying it could never mean a profile either. Written
            // here as well it would be a second rule to disagree with the first.
            var names = ProfileNames.Empty();

            Assert.That(names.Add(AclDocument.DefaultKey), Does.Contain(AclDocument.DefaultKey));
            Assert.That(names.Add("DEFAULT"), Is.Not.Null, "in any case, as the claim is");
            Assert.That(names.Names, Is.Empty);
        }

        [Test]
        public void Adding_a_name_twice_is_not_an_error_and_does_not_make_two()
        {
            var names = ProfileNames.Empty();

            Assert.That(names.Add("ops"), Is.Null);
            Assert.That(names.Add("ops"), Is.Null);

            Assert.That(names.Names, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_set_with_nothing_in_it_stores_nothing_at_all()
        {
            // The absent key rather than an empty document, so a database that never named a
            // profile and one that stopped naming any are the same database.
            Assert.That(ProfileNames.Empty().Write(), Is.Null);

            var names = ProfileNames.Read("{\"profiles\":[\"ops\"]}");
            Assert.That(names.Remove("ops"), Is.True);
            Assert.That(names.Write(), Is.Null);
        }

        [Test]
        public void What_is_written_reads_back_as_what_was_written()
        {
            var names = ProfileNames.Empty();
            names.Add("reader");
            names.Add("ops-2");
            names.Add("agent_one");

            var again = ProfileNames.Read(names.Write());

            Assert.That(again.Names, Is.EqualTo(new[] { "agent_one", "ops-2", "reader" }));
            Assert.That(again.Holds("reader"), Is.True);
            Assert.That(again.Holds("Reader"), Is.False, "compared exactly, as a claim is");
        }

        [Test]
        public void Removing_a_name_that_is_not_there_says_so()
        {
            var names = ProfileNames.Read("{\"profiles\":[\"ops\"]}");

            Assert.That(names.Remove("reader"), Is.False);
            Assert.That(names.Remove(null), Is.False);
            Assert.That(names.Names, Is.EqualTo(new[] { "ops" }));
        }
    }
}
