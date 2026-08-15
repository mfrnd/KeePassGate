using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a node has in force before it says anything, which is what the editor shows beside
    /// what is being written.
    /// </summary>
    /// <remarks>
    /// This answers for showing rather than for deciding, so what it must never do is describe a
    /// permission nobody has. The tests that matter are the ones where it stops talking.
    /// </remarks>
    [TestFixture]
    public sealed class AclInheritanceTests
    {
        [Test]
        public void Nothing_above_is_nothing_to_show()
        {
            var inherited = AclResolver.Above(TestNode.Root("root"));

            Assert.That(inherited.Readable, Is.True);
            Assert.That(inherited.Profiles, Is.Empty);
            Assert.That(inherited.For("reader"), Is.Null);
        }

        [Test]
        public void A_rule_above_is_what_a_profile_already_holds()
        {
            var group = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("group");

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Profiles, Is.EqualTo(new[] { "reader" }));
            Assert.That(inherited.For("reader").Holds(AclVerb.Read), Is.True);
        }

        [Test]
        public void The_nearest_rule_about_a_profile_is_the_one_in_force()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.Update);
            var group = root.Child("group").Granting("reader", AclVerb.Read);

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.For("reader").Holds(AclVerb.Read), Is.True);
            Assert.That(inherited.For("reader").Holds(AclVerb.Update), Is.False,
                "the nearer rule is what an entry in this group actually inherits");
        }

        [Test]
        public void Rules_about_different_profiles_are_collected_from_the_whole_chain()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("group").Granting("editor", AclVerb.Update);

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Profiles.OrderBy(name => name), Is.EqualTo(new[] { "editor", "reader" }));
        }

        [Test]
        public void A_default_of_deny_above_stops_the_walk()
        {
            // What is above a seal reaches nothing under it, so listing it as inherited would be
            // listing a permission nobody has. Nothing more is said about the seal: a profile with
            // no row here is refused with it or without it, and a rule written here is nearer.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("sealed").Sealing("editor", AclVerb.Update);

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Profiles, Is.EqualTo(new[] { "editor" }));
            Assert.That(inherited.For("reader"), Is.Null, "the rule above the seal is out of reach");
        }

        [Test]
        public void An_empty_rule_above_is_a_rule_and_shows_as_one()
        {
            var group = TestNode.Root("root").Granting("reader").Child("group");

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.For("reader"), Is.Not.Null);
            Assert.That(inherited.For("reader").Count, Is.Zero);
        }

        [Test]
        public void The_scope_comes_through_as_part_of_the_rule()
        {
            var group = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Attachments).Child("group");

            Assert.That(AclResolver.Above(group).For("reader").CoversAttachments, Is.True);
        }

        [Test]
        public void A_rule_above_that_will_not_read_makes_the_whole_answer_unknown()
        {
            // Nothing beyond an unreadable rule arrives, so listing what the groups further up say
            // would describe permissions nobody has.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("broken").Holding("{\"reader\":[\"purge\"]}");

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Readable, Is.False);
            Assert.That(inherited.Profiles, Is.Empty);
            Assert.That(AclResolver.Permits(group.Child("an entry"), new[] { "reader" }, AclVerb.Read), Is.False,
                "and every profile is refused there");
        }

        [Test]
        public void A_readable_rule_under_an_unreadable_one_is_still_in_force()
        {
            // The half that is easy to get wrong. Walking upwards collects the nearest rules first,
            // so by the time an unreadable document is met there may already be something in hand,
            // and it is in force: nearest wins, so `Decide` stops at it and never reads the broken
            // one. What that one refuses is every profile nothing nearer names.
            var root = TestNode.Root("root").Holding("{\"reader\":[\"purge\"]}");
            var middle = root.Child("middle").Granting("reader", AclVerb.Read);
            var leaf = middle.Child("leaf");

            var inherited = AclResolver.Above(leaf);

            Assert.That(inherited.Readable, Is.False);
            Assert.That(inherited.For("reader"), Is.Not.Null);

            // And the resolver, which is what the display has to agree with.
            Assert.That(AclResolver.Permits(leaf, new[] { "reader" }, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(leaf, new[] { "editor" }, AclVerb.Read), Is.False);
        }

        [Test]
        public void A_chain_that_loops_ends_as_unreadable_rather_than_running()
        {
            var inherited = AclResolver.Above(TestNode.Root("root").InACycle());

            Assert.That(inherited.Readable, Is.False);
        }

        [Test]
        public void The_walk_gives_up_at_the_same_group_the_resolver_does()
        {
            // Counted from the node being edited, which is one level below the node asked about.
            // A grant exactly one level beyond the resolver's reach is the case that disagreed.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var node = root;
            for (var level = 1; level < AclResolver.MaxDepth + 1; level++)
            {
                node = node.Child("level " + level);
            }

            Assert.That(AclResolver.Permits(node, new[] { "reader" }, AclVerb.Read), Is.False);

            var inherited = AclResolver.Above(node.Parent);

            Assert.That(inherited.For("reader"), Is.Null);
            Assert.That(inherited.Readable, Is.False);

            // And one level shallower both of them reach it.
            Assert.That(AclResolver.Permits(node.Parent, new[] { "reader" }, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Above(node.Parent.Parent).For("reader"), Is.Not.Null);
        }

        [Test]
        public void What_a_node_says_itself_is_included_when_it_is_asked_about_itself()
        {
            // The walk starts where it is told to start. The editor asks about the node above, so
            // it gets what that node holds and everything over it, which is exactly what its own
            // rules will be layered onto.
            var group = TestNode.Root("root").Granting("reader", AclVerb.Read);

            Assert.That(AclResolver.Above(group).For("reader"), Is.Not.Null);
        }
    }
}
