using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
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
    /// permission nobody has. Each profile it lists is listed with what the walk gives it, every
    /// cut on the way taken, so a table built from it shows what arrives and never what a rule
    /// further up granted before a node below cut it.
    /// </remarks>
    [TestFixture]
    public sealed class AclInheritanceTests
    {
        [Test]
        public void Nothing_above_is_nothing_to_show()
        {
            var inherited = AclResolver.Above(TestNode.Root("root"));

            Assert.That(inherited.TooDeep, Is.False);
            Assert.That(inherited.Profiles, Is.Empty);
            Assert.That(inherited.For("reader"), Is.Null);
            Assert.That(AclResolver.Above(null).Profiles, Is.Empty);
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
        public void A_switch_that_refuses_above_stops_the_walk()
        {
            // What is above a refusing switch reaches nothing under it, so listing it as inherited
            // would be listing a permission nobody has. Nothing more is told of the switch: a
            // profile with no row here is refused with it or without it, and a rule written here
            // is nearer.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("sealed").Sealing("editor", AclVerb.Update);

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Profiles, Is.EqualTo(new[] { "editor" }));
            Assert.That(inherited.For("reader"), Is.Null, "the rule above the switch is out of reach");
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
        public void What_is_listed_is_what_arrives_with_every_cut_on_the_way()
        {
            // A group taking a rule off, or an inheriting switch, cuts what arrives below it, and
            // the editor of a node under that group is shown the cut letters and nothing wider.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "CRUA"), ("editor", "RU")));
            var group = root.Child("group").Holding(Rules.Stored(("reader", "off:RA"), (null, "sw:inherit editor=R reader=RUA")));

            var inherited = AclResolver.Above(group);

            Assert.That(VerbMask.Of(inherited.For("reader")), Is.EqualTo(".R....A."));
            Assert.That(VerbMask.Of(inherited.For("editor")), Is.EqualTo(".R......"));
            Assert.That(inherited.For("reader").SameAs(AclResolver.Arrives(group, "reader")), Is.True);
            Assert.That(inherited.For("editor").SameAs(AclResolver.Arrives(group, "editor")), Is.True);
        }

        [Test]
        public void A_row_above_that_refuses_is_listed_as_granting_nothing()
        {
            // A row stands for the profile there, lost or out of form, so the editor shows the
            // profile's inherited row granting nothing rather than leaving the profile out.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "R"), ("editor", "R")));
            var group = root.Child("group").Holding(Rules.Stored(("reader", null), ("editor", "row:r")));

            var inherited = AclResolver.Above(group);

            Assert.That(inherited.Profiles.OrderBy(name => name), Is.EqualTo(new[] { "editor", "reader" }));
            Assert.That(inherited.For("reader").Count, Is.Zero, "a lost row");
            Assert.That(inherited.For("editor").Count, Is.Zero, "a text out of form");
            Assert.That(AclResolver.Permits(group.Child("an entry"), new[] { "reader" }, AclVerb.Read), Is.False);
        }

        [Test]
        public void A_switch_above_that_is_lost_or_out_of_form_lists_nothing_beyond_it()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var lost = root.Child("lost").Holding(Rules.Stored((null, null), ("editor", "row:U")));
            var unread = root.Child("unread").Holding(Rules.Stored((null, "sw:inherit reader=r")));

            Assert.That(AclResolver.Above(lost).Profiles, Is.EqualTo(new[] { "editor" }));
            Assert.That(AclResolver.Above(unread).Profiles, Is.Empty);
            Assert.That(AclResolver.Permits(unread.Child("an entry"), new[] { "reader" }, AclVerb.Read), Is.False);
        }

        [Test]
        public void A_chain_that_loops_ends_rather_than_running()
        {
            var inherited = AclResolver.Above(TestNode.Root("root").Granting("reader", AclVerb.Read).InACycle());

            Assert.That(inherited.TooDeep, Is.True);
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
            Assert.That(inherited.TooDeep, Is.True, "and it is the depth, not a rule, that stopped it");
            Assert.That(AclResolver.Above(node.Parent.Parent).TooDeep, Is.False);

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
