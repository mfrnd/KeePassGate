using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Acl;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Reading a node's rules, and refusing anything that is not exactly a set of them.
    /// </summary>
    /// <remarks>
    /// Most of these are refusals, and they are the point. A grant document is the thing that says
    /// what a client may reach, so every way of reading one wrongly is a way of granting something
    /// nobody wrote. There is no repair anywhere in here: a document that is not right is refused,
    /// and the resolver turns that into a denial.
    /// </remarks>
    [TestFixture]
    public sealed class AclDocumentTests
    {
        [Test]
        public void A_document_is_profiles_and_the_verbs_they_hold()
        {
            var document = AclDocument.Read("{\"reader\":[\"read\"],\"editor\":[\"read\",\"update\"]}");

            Assert.That(document.Grant("reader").Holds(AclVerb.Read), Is.True);
            Assert.That(document.Grant("reader").Holds(AclVerb.Update), Is.False);
            Assert.That(document.Grant("editor").Holds(AclVerb.Update), Is.True);
        }

        [Test]
        public void A_profile_it_says_nothing_about_is_silence_rather_than_an_empty_rule()
        {
            // The two answers that look alike and are not. Silence is what the default answers for;
            // an empty rule is a decision this node made.
            var document = AclDocument.Read("{\"reader\":[]}");

            Assert.That(document.Grant("reader"), Is.Not.Null, "an empty rule is a rule");
            Assert.That(document.Grant("reader").Count, Is.Zero);
            Assert.That(document.Grant("nobody"), Is.Null, "and this is silence");
        }

        [Test]
        public void A_document_inherits_unless_it_says_otherwise()
        {
            Assert.That(AclDocument.Read("{\"reader\":[\"read\"]}").Default, Is.EqualTo(AclDefault.Inherit));
            Assert.That(AclDocument.Read("{\"default\":\"inherit\"}").Default, Is.EqualTo(AclDefault.Inherit));
            Assert.That(AclDocument.Read("{\"default\":\"deny\"}").Default, Is.EqualTo(AclDefault.Deny));
        }

        [Test]
        public void The_reserved_member_cannot_be_a_profile()
        {
            // Free, because `default` was already refused as a profile name by the issuer that puts
            // profiles into certificates, long before this format existed.
            var raised = Assert.Throws<ArgumentException>(() => AclDocument.Of(
                AclDefault.Inherit,
                new Dictionary<string, IEnumerable<AclVerb>> { { "default", new[] { AclVerb.Read } } }));

            Assert.That(raised.Message, Does.Contain("reserved"));
        }

        [TestCase("", TestName = "nothing")]
        [TestCase("not a document", TestName = "not JSON")]
        [TestCase("[\"reader\"]", TestName = "not an object")]
        [TestCase("{\"reader\":\"read\"}", TestName = "a rule written as a string")]
        [TestCase("{\"default\":[\"deny\"]}", TestName = "a default written as an array")]
        [TestCase("{\"default\":\"allow\"}", TestName = "a default nobody defined")]
        [TestCase("{\"default\":\"Deny\"}", TestName = "a default in the wrong case")]
        [TestCase("{\"reader\":[\"admin\"]}", TestName = "a verb nobody defined")]
        [TestCase("{\"reader\":[\"Read\"]}", TestName = "a verb in the wrong case")]
        [TestCase("{\"reader\":[\"none\"]}", TestName = "the verb that is not grantable")]
        [TestCase("{\"reader\":{\"read\":true}}", TestName = "nested")]
        public void Anything_that_is_not_exactly_a_document_is_refused(string text)
        {
            // Refused, not repaired and not partially read. A rule this cannot honor has to become
            // a denial, and it only can if reading says no.
            Assert.That(AclDocument.Read(text), Is.Null);
        }

        [Test]
        public void What_is_written_reads_back_as_what_was_written()
        {
            var document = AclDocument.Of(AclDefault.Deny, new Dictionary<string, IEnumerable<AclVerb>>
            {
                { "editor", new[] { AclVerb.Update, AclVerb.Read } },
                { "reader", new[] { AclVerb.Read } },
                { "nobody", Array.Empty<AclVerb>() },
            });

            var again = AclDocument.Read(document.Write());

            Assert.That(again.Default, Is.EqualTo(AclDefault.Deny));
            Assert.That(again.Grant("editor").OrderBy(v => v), Is.EqualTo(new[] { AclVerb.Read, AclVerb.Update }));
            Assert.That(again.Grant("reader").Holds(AclVerb.Read), Is.True);
            Assert.That(again.Grant("nobody").Count, Is.Zero);
        }

        [Test]
        public void What_is_written_is_the_same_text_every_time()
        {
            // A document that reordered itself on every save would make every write a change, and
            // `PwDatabase.Modified` is what a save is believed on.
            var grants = new Dictionary<string, IEnumerable<AclVerb>>
            {
                { "zulu", new[] { AclVerb.Delete, AclVerb.Read } },
                { "alpha", new[] { AclVerb.Read, AclVerb.Delete } },
            };

            var once = AclDocument.Of(AclDefault.Inherit, grants).Write();
            var twice = AclDocument.Of(AclDefault.Inherit, grants).Write();

            Assert.That(once, Is.EqualTo(twice));
            Assert.That(once, Does.StartWith("{\"default\":"), "what it does with what it does not name comes first");
            Assert.That(once.IndexOf("alpha", StringComparison.Ordinal),
                Is.LessThan(once.IndexOf("zulu", StringComparison.Ordinal)));
        }

        [Test]
        public void More_rules_than_the_reader_allows_denies_rather_than_truncating()
        {
            // The reader's ceiling is this format's ceiling, and it is worth a test because
            // reaching it refuses the whole document rather than dropping the last rule. Nobody
            // should be near 32 profiles on one node, and a node that is has rules nobody can read.
            Assert.That(AclDocument.Read(Profiles(31)), Is.Not.Null, "the default and 31 profiles");
            Assert.That(AclDocument.Read(Profiles(32)), Is.Null, "one more than the reader allows");
        }

        [Test]
        public void The_ceiling_holds_for_a_document_that_leaves_the_default_out()
        {
            // Such a document fits 32 profiles into the reader's 32 members, and `Write` puts the
            // default back, so one ordinary edit on the tab would store a document this refuses.
            // Only a document written by hand can get here, since the editor stops at 31.
            Assert.That(AclDocument.Read(Profiles(AclDocument.MostProfiles + 1, false)), Is.Null,
                "32 profiles with no default is still one more than a node carries");

            var most = AclDocument.Read(Profiles(AclDocument.MostProfiles, false));

            Assert.That(most, Is.Not.Null);
            Assert.That(AclDocument.Read(most.Write()), Is.Not.Null,
                "whatever this reads, it can write back and read again");
        }

        private static string Profiles(int count, bool withDefault = true)
        {
            var text = new StringBuilder("{");
            if (withDefault)
            {
                text.Append("\"" + AclDocument.DefaultKey + "\":\"deny\"");
            }

            for (var i = 0; i < count; i++)
            {
                if (withDefault || i > 0)
                {
                    text.Append(',');
                }

                text.Append("\"profile").Append(i).Append("\":[\"read\"]");
            }

            return text.Append('}').ToString();
        }

        [Test]
        public void An_absent_document_is_a_node_with_nothing_to_say()
        {
            Assert.That(AclDocument.Empty.Default, Is.EqualTo(AclDefault.Inherit));
            Assert.That(AclDocument.Empty.Profiles, Is.Empty);
            Assert.That(AclDocument.Empty.Grant("reader"), Is.Null);
        }
    }

    /// <summary>The verbs, which are what a rule is written about.</summary>
    [TestFixture]
    public sealed class AclVerbTests
    {
        [TestCase("create", AclVerb.Create)]
        [TestCase("read", AclVerb.Read)]
        [TestCase("update", AclVerb.Update)]
        [TestCase("strip", AclVerb.Strip)]
        [TestCase("delete", AclVerb.Delete)]
        [TestCase("list", AclVerb.List)]
        [TestCase("attachments", AclVerb.Attachments)]
        [TestCase("protected", AclVerb.Protected)]
        [TestCase("none", AclVerb.None)]
        public void Every_verb_reads_back_as_itself(string text, AclVerb verb)
        {
            Assert.That(AclVerbs.Read(text), Is.EqualTo(verb));
            Assert.That(AclVerbs.Write(verb), Is.EqualTo(text));
        }

        [Test]
        public void What_a_rule_can_hold_is_declared_in_the_order_it_is_shown()
        {
            // CRUSDLAP, and the enum is declared in that order so a stored document lists what it
            // grants the way the editor shows it. One list, so nothing can disagree about what
            // there is.
            Assert.That(AclVerbs.Grantable, Is.EqualTo(new[]
            {
                AclVerb.Create, AclVerb.Read, AclVerb.Update, AclVerb.Strip,
                AclVerb.Delete, AclVerb.List, AclVerb.Attachments, AclVerb.Protected,
            }));

            foreach (var verb in AclVerbs.Grantable)
            {
                Assert.That(AclVerbs.IsGrantable(verb), Is.True, AclVerbs.Write(verb));
            }

            Assert.That(AclVerbs.Grantable, Has.No.Member(AclVerb.None));
        }

        [Test]
        public void That_order_cannot_be_rearranged_by_whoever_reads_it()
        {
            // A position in it is a letter of every mask and a verb's place in every document
            // written, for the life of the process, so one test sorting it would change both for
            // every test that ran after it.
            var list = (IList<AclVerb>)AclVerbs.Grantable;

            Assert.That(AclVerbs.Grantable as AclVerb[], Is.Null, "an array can be sorted in place");
            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => list[0] = AclVerb.Protected);

            Assert.That(AclVerbs.PositionOf(AclVerb.Create), Is.Zero);
            Assert.That(AclVerbs.PositionOf(AclVerb.Protected), Is.EqualTo(AclVerbs.Grantable.Count - 1));
            Assert.That(AclVerbs.PositionOf(AclVerb.None), Is.EqualTo(-1), "no rule holds it");
        }

        [Test]
        public void The_last_of_them_says_how_far_a_rule_reaches_rather_than_what_it_may_do()
        {
            // A rule holds it alongside its verbs, and it is still not one. The distinction is what
            // stops an operation declaring it as the thing it does.
            Assert.That(AclVerbs.IsScope(AclVerb.Attachments), Is.True);
            Assert.That(AclVerbs.IsScope(AclVerb.Protected), Is.True,
                "both of them, or the one left out becomes a verb an operation may declare");

            foreach (var verb in new[]
            {
                AclVerb.None, AclVerb.Create, AclVerb.Read,
                AclVerb.Update, AclVerb.Strip, AclVerb.Delete, AclVerb.List,
            })
            {
                Assert.That(AclVerbs.IsScope(verb), Is.False, AclVerbs.Write(verb));
            }
        }

        [Test]
        public void A_rule_says_separately_how_far_it_reaches()
        {
            var fields = AclDocument.Read("{\"reader\":[\"read\",\"list\"]}").Grant("reader");
            var inside = AclDocument.Read("{\"reader\":[\"read\",\"list\",\"attachments\"]}").Grant("reader");

            Assert.That(fields.Holds(AclVerb.Read), Is.True);
            Assert.That(fields.CoversAttachments, Is.False);

            Assert.That(inside.Holds(AclVerb.Read), Is.True);
            Assert.That(inside.CoversAttachments, Is.True, "read and list now reach what is inside");
        }

        [Test]
        public void A_scope_over_no_verbs_grants_nothing()
        {
            // `......A.` is a rule that reaches attachments for none of the things anybody may do,
            // which is a rule granting nothing. It reads, because it is not malformed.
            var grant = AclDocument.Read("{\"reader\":[\"attachments\"]}").Grant("reader");

            Assert.That(grant, Is.Not.Null);
            Assert.That(grant.CoversAttachments, Is.True);

            foreach (var verb in new[]
            {
                AclVerb.Create, AclVerb.Read, AclVerb.Update,
                AclVerb.Strip, AclVerb.Delete, AclVerb.List,
            })
            {
                Assert.That(grant.Holds(verb), Is.False, AclVerbs.Write(verb));
            }
        }

        [Test]
        public void A_stored_rule_lists_its_verbs_in_that_same_order()
        {
            var document = AclDocument.Of(AclDefault.Inherit, new Dictionary<string, IEnumerable<AclVerb>>
            {
                { "everything", AclVerbs.Grantable },
            });

            Assert.That(document.Write(), Does.Contain(
                "[\"create\",\"read\",\"update\",\"strip\",\"delete\",\"list\",\"attachments\",\"protected\"]"));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("Read")]
        [TestCase("READ")]
        [TestCase("readd")]
        [TestCase("admin")]
        [TestCase("write", TestName = "the spelling this verb used to have")]
        [TestCase("attachment", TestName = "the singular of one that exists")]
        public void Anything_else_is_not_a_verb(string text)
        {
            // Fail closed. A typo in a rule must not become a rule that was silently dropped, which
            // is the way a mistake in a grant widens what a client may do. The same answer for a
            // spelling that used to work: a document written against an older reading of this is
            // refused rather than half honored.
            Assert.That(AclVerbs.Read(text), Is.Null);
        }

        [Test]
        public void The_verb_for_touching_nothing_is_not_grantable()
        {
            // `none` says an operation reaches no data. A grant of it would read as permission to
            // do something rather than as permission to do nothing.
            Assert.That(AclVerbs.IsGrantable(AclVerb.None), Is.False);
        }
        [Test]
        public void A_rule_says_separately_whether_it_reaches_what_the_database_protects()
        {
            var plain = AclDocument.Read("{\"reader\":[\"read\"]}").Grant("reader");
            var reaching = AclDocument.Read("{\"reader\":[\"read\",\"protected\"]}").Grant("reader");

            Assert.That(plain.CoversProtected, Is.False);
            Assert.That(reaching.CoversProtected, Is.True);
            Assert.That(reaching.Holds(AclVerb.Read), Is.True, "the switch does not replace the verb");
        }

    }
}
