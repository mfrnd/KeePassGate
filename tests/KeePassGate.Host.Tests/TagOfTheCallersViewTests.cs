using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// An entry's tag is the tag of what its caller may read, the same on every route and in
    /// every change, and handed to every caller holding `read` and to nobody else.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-11-10, `API.md` §410. A tag covering a value its holder may
    /// not read is a way to test guesses at the value, so a tag covers what its holder reads:
    /// two callers that see different things of an entry hold different tags for it, two that
    /// see the same hold the same, and a change is asked against the tag its own caller was
    /// handed. Every case here is the real router over the real vault and the rules as an
    /// operator writes them, on an entry changed the way KeePass or another plugin changes it,
    /// with nothing touched, so what moves a tag is what the view holds and never a time.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class TagOfTheCallersViewTests
    {
        private const string Reads = "R";
        private const string ReadsProtected = "RP";
        private const string ReadsAttachments = "RA";
        private const string ReadsWhole = "RAP";
        private const string ReadsAndLists = "RL";
        private const string ReadsBesideListingAttachments = "R+LA";

        /// <summary>
        /// The rules each case is held to, every case with the `update` its write needs.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, (string Profile, string Mask)[]> Cases =
            new Dictionary<string, (string Profile, string Mask)[]>
            {
                { Reads, new[] { ("agent", "RU") } },
                { ReadsProtected, new[] { ("agent", "RUP") } },
                { ReadsAttachments, new[] { ("agent", "RUA") } },
                { ReadsWhole, new[] { ("agent", "RUAP") } },
                { ReadsAndLists, new[] { ("agent", "RUL") } },
                { ReadsBesideListingAttachments, new[] { ("agent", "RU"), ("lister", "LA") } },
            };

        /// <summary>
        /// A name the store cannot tell from `Title`: a soft hyphen, to which the collation
        /// KeePass's dictionaries compare names by gives no weight.
        /// </summary>
        private const string TitleAlias = "Ti\u00ADtle";

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
        private SpecRouter _router;
        private int _saves;
        private string _clientKey;
        private string _clientName;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(_group, true);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "an invented name"));
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-password"));
            _entry.Binaries.Set("note.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            _group.AddEntry(_entry, true);

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
            _saves = 0;
            _clientKey = "d1e2f3";
            _clientName = "CN=agent-fictitious";
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [TestCase(Reads, "entry,field")]
        [TestCase(ReadsProtected, "entry,field")]
        [TestCase(ReadsAttachments, "entry,field,attachment")]
        [TestCase(ReadsWhole, "entry,field,attachment")]
        [TestCase(ReadsAndLists, "entry,fields,field")]
        [TestCase(ReadsBesideListingAttachments, "entry,fields,field,attachments")]
        public void Every_route_hands_a_caller_the_same_tag_and_its_write_is_asked_against_that_tag(
            string rules, string reached)
        {
            Granting(rules);
            var tag = Tag(rules);
            Assert.That(tag, Does.Match("^\"[0-9a-f]{32}\"$"));

            // Every route about the entry that this caller reaches, and the head beside each. A
            // route its rules do not open answers as an entry that is not there, and hands no
            // tag.
            var served = new List<string>();
            foreach (var route in Routes())
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    var answer = Send(method, route.Value, null, null, rules);

                    if (answer.Status == 404)
                    {
                        Assert.That(Handed(answer), Is.Null, method + " " + route.Key);
                        continue;
                    }

                    Assert.That(answer.Status, Is.EqualTo(200), method + " " + route.Key);
                    Assert.That(Handed(answer), Is.EqualTo(tag), method + " " + route.Key);

                    if (method == "GET")
                    {
                        served.Add(route.Key);
                    }
                }
            }

            Assert.That(served, Is.EquivalentTo(reached.Split(',')), "the routes these rules reach");

            // A write under that tag goes ahead, and answers with the tag every route then
            // hands the same caller.
            var written = Send("PUT", Entry() + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", "renamed")), tag, rules);
            Assert.That(written.Status, Is.EqualTo(200), written.Document());
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("renamed"));

            var left = Written(written);
            Assert.That(left, Is.Not.Null.And.Not.EqualTo(tag));
            Assert.That(Tag(rules), Is.EqualTo(left));

            // And the tag the entry has moved on from is refused, on a write and on a read.
            var stale = Send("PUT", Entry() + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", "lost")), tag, rules);
            Assert.That(stale.Status, Is.EqualTo(412));
            Assert.That(Send("GET", Entry(), null, tag, rules).Status, Is.EqualTo(412));
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("renamed"));
        }

        [Test]
        public void Callers_that_see_different_things_hold_different_tags_and_callers_that_see_the_same_hold_the_same()
        {
            var tags = Tags();

            // `list` without `attachments` opens nothing `read` does not, so the two views are
            // the same view, and nothing about the rule or the client is in a tag.
            Assert.That(tags[ReadsAndLists], Is.EqualTo(tags[Reads]));

            var views = new[] { Reads, ReadsProtected, ReadsAttachments, ReadsWhole, ReadsBesideListingAttachments };
            Assert.That(views.Select(view => tags[view]), Is.Unique);

            // The tag of another caller's view meets the condition of no caller that sees
            // something else, and changes nothing.
            foreach (var mine in views)
            {
                foreach (var theirs in views.Where(view => view != mine))
                {
                    Granting(mine);

                    var refused = Send(
                        "PUT", Entry() + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", "lost")), tags[theirs], mine);

                    Assert.That(refused.Status, Is.EqualTo(412), mine + " under the tag of " + theirs);
                }
            }

            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("an invented name"));
        }

        [Test]
        public void A_tag_moves_with_what_its_caller_may_read_and_with_nothing_else()
        {
            var before = Tags();

            // A password set with nothing touched, as another plugin sets a value: in the view of
            // a rule holding `read` and `protected` together, and of no other.
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "another-invented-password"));
            Moved(before, "a protected value changed", ReadsProtected, ReadsWhole);

            // An attachment's bytes at the same size: read by `read` and `attachments` together.
            before = Tags();
            _entry.Binaries.Set("note.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("INVENTED")));
            Moved(before, "an attachment's bytes changed", ReadsAttachments, ReadsWhole);

            // An attachment added: its name and size are also in the view of a caller that may
            // list attachments.
            before = Tags();
            _entry.Binaries.Set("more.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            Moved(before, "an attachment added", ReadsAttachments, ReadsWhole, ReadsBesideListingAttachments);

            // An attachment renamed.
            before = Tags();
            var renamed = _entry.Binaries.Get("more.txt");
            _entry.Binaries.Remove("more.txt");
            _entry.Binaries.Set("other.txt", renamed);
            Moved(before, "an attachment renamed", ReadsAttachments, ReadsWhole, ReadsBesideListingAttachments);

            // And a string nothing protects is in every view.
            before = Tags();
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "renamed"));
            Moved(before, "an open value changed", Cases.Keys.ToArray());
        }

        [Test]
        public void A_rule_widened_over_an_entry_where_nothing_new_comes_into_sight_moves_no_tag()
        {
            // Nothing about the rule is in a tag, only what the caller is shown. An entry
            // holding no attachment shows a rule with `attachments` what it shows a rule
            // without, so widening the rule leaves the tag, and a write made under the tag from
            // before the widening goes ahead: a `412` there would be for nothing anybody could
            // see.
            _entry.Binaries.Clear();

            var tags = Tags();
            Assert.That(tags[ReadsAttachments], Is.EqualTo(tags[Reads]));
            Assert.That(tags[ReadsBesideListingAttachments], Is.EqualTo(tags[Reads]));
            Assert.That(tags[ReadsWhole], Is.EqualTo(tags[ReadsProtected]));
            Assert.That(tags[ReadsProtected], Is.Not.EqualTo(tags[Reads]), "while the password is still another view");

            Granting(Reads);
            var narrow = Tag(Reads);

            Granting(ReadsAttachments);
            Assert.That(Tag(ReadsAttachments), Is.EqualTo(narrow));

            var written = Send(
                "PUT", Entry() + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", "renamed")), narrow, ReadsAttachments);
            Assert.That(written.Status, Is.EqualTo(200), written.Document());
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("renamed"));

            // And an attachment arriving is something the wider rule sees and the narrower
            // does not.
            var left = Written(written);
            _entry.Binaries.Set("note.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            Assert.That(Tag(ReadsAttachments), Is.Not.EqualTo(left));
            Granting(Reads);
            Assert.That(Tag(Reads), Is.EqualTo(left));
        }

        [Test]
        public void A_caller_that_may_set_a_protected_value_and_not_read_the_entry_is_handed_no_tag()
        {
            // `update` and `protected` with no `read`: a tag would be a tag of nothing this
            // caller reads, so there is none, and a condition it sends fails and is not ignored.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "ULAP")));
            var profiles = new[] { "agent" };
            var password = Entry() + "/fields/" + PwDefs.PasswordField;

            foreach (var listing in new[] { Entry() + "/fields", Entry() + "/attachments" })
            {
                var listed = Send("GET", listing, null, null, profiles);

                Assert.That(listed.Status, Is.EqualTo(200), listing);
                Assert.That(
                    listed.Document(),
                    Does.Contain(
                        listing.EndsWith("/fields", StringComparison.Ordinal) ? PwDefs.PasswordField : "note.txt"));
                Assert.That(listed.Document(), Does.Not.Contain("not-a-real-password").And.Not.Contain("an invented"));
                Assert.That(Handed(listed), Is.Null, listing);
                Assert.That(Send("GET", listing, null, "*", profiles).Status, Is.EqualTo(412), listing);
            }

            var written = Send("PUT", password, Bodies.Of(("value", "a-guess")), null, profiles);
            Assert.That(written.Status, Is.EqualTo(200), written.Document());
            Assert.That(Written(written), Is.Null);

            Assert.That(Send("PUT", password, Bodies.Of(("value", "overwritten")), "*", profiles).Status, Is.EqualTo(412));
            Assert.That(_entry.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo("a-guess"));
        }

        [Test]
        public void A_caller_reading_through_one_rule_and_setting_protected_values_through_another_holds_a_tag_the_password_is_not_in()
        {
            // `read` without `protected` beside `update` with `protected`: the pair the guessing
            // attack needs. The caller is handed a tag, and the tag is the tag of a caller that
            // only reads, so the password changing under it moves nothing it could compare.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "R"), ("writer", "UP")));
            var profiles = new[] { "reader", "writer" };

            var tag = Handed(Send("GET", Entry(), null, null, profiles));
            Assert.That(tag, Is.Not.Null);

            foreach (var guess in new[] { "a-wrong-guess", "not-a-real-password" })
            {
                _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, guess));

                Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(tag), guess);
            }

            Granting(Reads);
            Assert.That(Tag(Reads), Is.EqualTo(tag), "the tag of the view `read` alone has");

            // A right guess and a wrong guess, each written under the caller's tag at the same
            // stored value. Both are written like any change, a save and a revision each, so
            // neither the time a listing serves nor the tag tells them apart: the tag in each
            // answer is the tag the caller's next read hands out, moved by `modified` alike and
            // by nothing of the value.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "R"), ("writer", "UP")));
            var password = Entry() + "/fields/" + PwDefs.PasswordField;
            var shapes = new List<string>();

            foreach (var guess in new[] { "not-a-real-password", "a-wrong-guess" })
            {
                _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-password"));
                var held = Handed(Send("GET", Entry(), null, null, profiles));
                var saves = _saves;
                var revisions = _entry.History.UCount;

                var written = Send("PUT", password, Bodies.Of(("value", guess)), held, profiles);

                Assert.That(written.Status, Is.EqualTo(200), guess + ": " + written.Document());
                Assert.That(_saves, Is.EqualTo(saves + 1), guess + ": saves");
                Assert.That(_entry.History.UCount, Is.EqualTo(revisions + 1), guess + ": revisions");
                Assert.That(_entry.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(guess));
                Assert.That(Written(written), Is.Not.Null.And.EqualTo(Handed(Send("GET", Entry(), null, null, profiles))), guess);

                shapes.Add(string.Join(
                    ",", JObject.Parse(written.Document()).Properties().Select(member => member.Name)));
            }

            Assert.That(shapes[1], Is.EqualTo(shapes[0]), "the answer to a wrong guess and to a right guess");
        }

        [Test]
        public void A_change_that_adds_a_protected_value_answers_with_the_tag_of_what_its_caller_still_reads()
        {
            // `read` without `protected` beside `create` with `protected`: the caller adds a
            // string it may not read back. The tag in the answer is of the view after the
            // change, in which the new string is a name and a mark, so it is the tag the caller's
            // next read hands out, and it holds nothing of the value written.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "R"), ("maker", "CP")));
            var profiles = new[] { "reader", "maker" };
            var before = Handed(Send("GET", Entry(), null, null, profiles));

            var made = Send(
                "POST",
                Entry() + "/fields",
                Bodies.Of(("name", "PIN"), ("value", "not-a-real-pin"), ("protected", "true")),
                before,
                profiles);

            Assert.That(made.Status, Is.EqualTo(201), made.Document());
            Assert.That(_entry.Strings.Get("PIN").IsProtected, Is.True);

            var left = Written(made);
            Assert.That(left, Is.Not.Null.And.Not.EqualTo(before), "a name added is in every view");
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(left));
            Assert.That(Send("GET", Entry(), null, null, profiles).Document(), Does.Not.Contain("not-a-real-pin"));

            // Another value under the same name, set with nothing touched, is the same view.
            _entry.Strings.Set("PIN", new ProtectedString(true, "another-invented-pin, and longer"));
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(left));

            // While a caller that reads protected values holds another tag, which moves with it.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RP")));
            var whole = Handed(Send("GET", Entry(), null, null, new[] { "reader" }));
            Assert.That(whole, Is.Not.EqualTo(left));
            _entry.Strings.Set("PIN", new ProtectedString(true, "not-a-real-pin"));
            Assert.That(Handed(Send("GET", Entry(), null, null, new[] { "reader" })), Is.Not.EqualTo(whole));
        }

        [Test]
        public void Nothing_of_the_client_is_in_a_tag()
        {
            // Two clients with different certificates and different keys, under the same
            // rules: the same view, so the same tag, and a write by one under the tag the other
            // was handed goes ahead.
            Granting(Reads);
            var first = Tag(Reads);

            _clientKey = "a1b2c3";
            _clientName = "CN=another-fictitious-agent";
            Assert.That(Tag(Reads), Is.EqualTo(first));

            var written = Send(
                "PUT", Entry() + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", "renamed")), first, Reads);
            Assert.That(written.Status, Is.EqualTo(200), written.Document());
        }

        [TestCase(true, TestName = "A title the database protects, shown by a listing, is served by a read and covered by the tag")]
        [TestCase(false, TestName = "A title flagged protected, shown by a listing, is served by a read and covered by the tag")]
        public void A_protected_title_a_listing_shows_its_caller_is_in_that_caller_s_view(bool byDatabase)
        {
            // A caller's view of an entry holds everything any route shows that caller of the
            // entry. `list` with `protected` shows a protected title in a listing, so beside
            // `read` the title is in the view: the read serves it, every route hands the same
            // tag, and the tag moves with it. The password is no string a listing shows, and
            // stays out.
            Title("an invented service", byDatabase);
            var profiles = new[] { "reader", "lister" };
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RU"), ("lister", "LP")));

            Assert.That(Listed(profiles), Is.EqualTo("an invented service"));

            var read = Send("GET", Entry(), null, null, profiles);
            Assert.That(read.Status, Is.EqualTo(200), read.Document());
            Assert.That(Value(read, PwDefs.TitleField), Is.EqualTo("an invented service"));
            Assert.That(Value(read, PwDefs.PasswordField), Is.Null, "the password is still withheld");
            Assert.That(read.Document(), Does.Not.Contain("not-a-real-password"));

            var field = Send("GET", Entry() + "/fields/" + PwDefs.TitleField, null, null, profiles);
            Assert.That((string)JObject.Parse(field.Document())["value"], Is.EqualTo("an invented service"));
            var secret = Send("GET", Entry() + "/fields/" + PwDefs.PasswordField, null, null, profiles);
            Assert.That(JObject.Parse(secret.Document())["value"].Type, Is.EqualTo(JTokenType.Null));

            var tag = Handed(read);
            Assert.That(tag, Is.Not.Null);
            Assert.That(Handed(field), Is.EqualTo(tag));
            Assert.That(Handed(secret), Is.EqualTo(tag));
            Assert.That(Handed(Send("GET", Entry() + "/fields", null, null, profiles)), Is.EqualTo(tag));

            // The password changed with nothing touched: not in this view, so not in its tag.
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "another-invented-password"));
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(tag));

            // The title changed with nothing touched: in this view, so the tag moves, and a
            // write under the tag from before is refused.
            Title("renamed by the operator", byDatabase);
            var moved = Handed(Send("GET", Entry(), null, null, profiles));
            Assert.That(moved, Is.Not.EqualTo(tag));

            var user = Entry() + "/fields/" + PwDefs.UserNameField;
            Assert.That(Send("PUT", user, Bodies.Of(("value", "lost")), tag, profiles).Status, Is.EqualTo(412));
            Assert.That(Send("PUT", user, Bodies.Of(("value", "kept")), moved, profiles).Status, Is.EqualTo(200));
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("kept"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_protected_title_no_listing_shows_its_caller_stays_out_of_that_caller_s_view(bool byDatabase)
        {
            Title("an invented service", byDatabase);

            // `read`, and `list` without `protected`: neither route shows the title, and the
            // tag does not move with it.
            var profiles = new[] { "reader", "lister" };
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RU"), ("lister", "L")));

            Assert.That(Listed(profiles), Is.Null, "withheld in the listing");
            var read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, PwDefs.TitleField), Is.Null, "and by the read");

            var tag = Handed(read);
            Assert.That(tag, Is.Not.Null);
            Title("renamed by the operator", byDatabase);
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(tag));

            // `list` with `protected` and no `read`: the listing shows the title, and there is
            // no value in its view of the entry, no read and no tag.
            profiles = new[] { "lister" };
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("lister", "LP")));

            Assert.That(Listed(profiles), Is.EqualTo("renamed by the operator"));
            Assert.That(Send("GET", Entry(), null, null, profiles).Status, Is.EqualTo(404));
            Assert.That(Send("GET", Entry() + "/fields/" + PwDefs.TitleField, null, null, profiles).Status, Is.EqualTo(404));

            var names = Send("GET", Entry() + "/fields", null, null, profiles);
            Assert.That(names.Status, Is.EqualTo(200), names.Document());
            Assert.That(Handed(names), Is.Null);
            Assert.That(names.Document(), Does.Not.Contain("renamed by the operator"));

            // `read` with `protected` in one rule, as it always was: the read serves the title
            // and the password, and the tag moves with either.
            profiles = new[] { "reader" };
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RUP")));

            read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, PwDefs.TitleField), Is.EqualTo("renamed by the operator"));
            Assert.That(Value(read, PwDefs.PasswordField), Is.EqualTo("not-a-real-password"));

            tag = Handed(read);
            Title("renamed again", byDatabase);
            var afterTitle = Handed(Send("GET", Entry(), null, null, profiles));
            Assert.That(afterTitle, Is.Not.EqualTo(tag));
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "another-invented-password"));
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.Not.EqualTo(afterTitle));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_listing_shows_a_caller_the_protected_title_a_read_of_the_entry_serves_it(bool byDatabase)
        {
            // The other direction of the same rule: `read` with `protected` beside `list`
            // without. The read serves the title, so the listings show it, the group's own and
            // the sweep of the whole database alike, and the tag covers it. The condition is in
            // one place for the listing and the view, so the two answer alike.
            Title("an invented service", byDatabase);
            var profiles = new[] { "reader", "lister" };
            var root = _database.RootGroup;
            root.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RUP"), ("lister", "L")));

            Assert.That(Listed(profiles), Is.EqualTo("an invented service"));
            Assert.That(Listed("/entries", profiles), Is.EqualTo("an invented service"));

            var read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, PwDefs.TitleField), Is.EqualTo("an invented service"));

            var tag = Handed(read);
            Title("renamed by the operator", byDatabase);
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.Not.EqualTo(tag));
            Assert.That(Listed("/entries", profiles), Is.EqualTo("renamed by the operator"));

            // `read` beside `list`, neither with `protected`: neither route shows it.
            root.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RU"), ("lister", "L")));
            Assert.That(Listed(profiles), Is.Null);
            Assert.That(Listed("/entries", profiles), Is.Null);
            Assert.That(Value(Send("GET", Entry(), null, null, profiles), PwDefs.TitleField), Is.Null);

            // `list` with `protected` alone: the listings show it, and there is no read.
            profiles = new[] { "lister" };
            root.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("lister", "LP")));
            Assert.That(Listed(profiles), Is.EqualTo("renamed by the operator"));
            Assert.That(Listed("/entries", profiles), Is.EqualTo("renamed by the operator"));
            Assert.That(Send("GET", Entry(), null, null, profiles).Status, Is.EqualTo(404));

            // And a profile the client does not hold shows it nothing.
            root.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RUP"), ("lister", "L")));
            Assert.That(Listed("/entries", profiles), Is.Null);
        }

        [TestCase(true, TestName = "An address the database protects, shown by a listing, is served by a read, tagged and skipped")]
        [TestCase(false, TestName = "An address flagged protected, shown by a listing, is served by a read, tagged and skipped")]
        public void A_protected_address_a_listing_shows_its_caller_is_in_that_caller_s_view(bool byDatabase)
        {
            // The URL is the other string a listing shows, held to the same rule against the
            // real vault: under `list` with `protected` beside `read` the listing, the read of
            // the entry and the read of the field all serve it, the tag moves with it, and an
            // update naming it as it stands is skipped, since its caller reads it.
            const string address = "https://invented.example/";
            Address(address, byDatabase);
            var profiles = new[] { "reader", "lister", "writer" };
            var listing = "/groups/" + _group.Uuid.ToHexString() + "/entries";
            var field = Entry() + "/fields/" + PwDefs.UrlField;
            _group.CustomData.Set(
                AclDocument.CustomDataKey, Rules.Granting(("reader", "R"), ("lister", "LP"), ("writer", "UP")));

            Assert.That(Listed(listing, profiles, "url"), Is.EqualTo(address));

            var read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, PwDefs.UrlField), Is.EqualTo(address));
            Assert.That(Value(read, PwDefs.PasswordField), Is.Null, "the password is still withheld");
            Assert.That(
                (string)JObject.Parse(Send("GET", field, null, null, profiles).Document())["value"],
                Is.EqualTo(address));

            var tag = Handed(read);
            Assert.That(Handed(Send("GET", field, null, null, profiles)), Is.EqualTo(tag));

            var saves = _saves;
            Assert.That(Send("PUT", field, Bodies.Of(("value", address)), tag, profiles).Status, Is.EqualTo(200));
            Assert.That(_saves, Is.EqualTo(saves), "an address its caller reads, written again as it stands, is skipped");

            Address("https://renamed.example/", byDatabase);
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.Not.EqualTo(tag));
            Assert.That(Listed(listing, profiles, "url"), Is.EqualTo("https://renamed.example/"));

            // Without the switch beside `list`, neither route shows it, the tag does not move
            // with it, and the same update is written, since its caller may not read it.
            _group.CustomData.Set(
                AclDocument.CustomDataKey, Rules.Granting(("reader", "R"), ("lister", "L"), ("writer", "UP")));

            Assert.That(Listed(listing, profiles, "url"), Is.Null);
            read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, PwDefs.UrlField), Is.Null);

            tag = Handed(read);
            Address("https://invented.example/again", byDatabase);
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.EqualTo(tag));

            saves = _saves;
            var blind = Send("PUT", field, Bodies.Of(("value", "https://invented.example/again")), null, profiles);
            Assert.That(blind.Status, Is.EqualTo(200), blind.Document());
            Assert.That(_saves, Is.EqualTo(saves + 1));
        }

        [Test]
        public void A_title_stored_under_a_name_the_store_cannot_tell_from_Title_is_shown_alike_by_a_listing_and_a_read()
        {
            // A file can arrive whose title is keyed with a spelling the store's collation
            // cannot tell from `Title`. The listing finds the title through the store's own
            // lookup, so it shows this string, and the view decides which strings a listing
            // shows the same way: compared byte for byte, the listing showed a caller holding
            // `read` beside `list` with `protected` a title the read of the same entry withheld,
            // under a tag the title was not in.
            _entry.Strings.Remove(PwDefs.TitleField);
            _entry.Strings.Set(TitleAlias, new ProtectedString(true, "a look-alike title"));
            Assume.That(_entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("a look-alike title"),
                "the store finds the string under the name a listing asks for");

            var profiles = new[] { "reader", "lister" };
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RU"), ("lister", "LP")));

            Assert.That(Listed(profiles), Is.EqualTo("a look-alike title"));

            var read = Send("GET", Entry(), null, null, profiles);
            Assert.That(Value(read, TitleAlias), Is.EqualTo("a look-alike title"));

            var tag = Handed(read);
            _entry.Strings.Set(TitleAlias, new ProtectedString(true, "renamed by the operator"));
            Assert.That(Handed(Send("GET", Entry(), null, null, profiles)), Is.Not.EqualTo(tag));

            var user = Entry() + "/fields/" + PwDefs.UserNameField;
            Assert.That(Send("PUT", user, Bodies.Of(("value", "lost")), tag, profiles).Status, Is.EqualTo(412));

            // And without the switch neither route shows it.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("reader", "RU"), ("lister", "L")));
            Assert.That(Listed(profiles), Is.Null);
            Assert.That(Value(Send("GET", Entry(), null, null, profiles), TitleAlias), Is.Null);
        }

        /// <summary>
        /// Sets the entry's URL with nothing touched, protected by the database's setting for
        /// a URL or by the string's own flag.
        /// </summary>
        private void Address(string address, bool byDatabase)
        {
            _database.Database.MemoryProtection.ProtectUrl = byDatabase;
            _entry.Strings.Set(PwDefs.UrlField, new ProtectedString(!byDatabase, address));
        }

        /// <summary>
        /// Sets the entry's title with nothing touched, protected by the database's setting
        /// for a title or by the string's own flag.
        /// </summary>
        private void Title(string title, bool byDatabase)
        {
            _database.Database.MemoryProtection.ProtectTitle = byDatabase;
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(!byDatabase, title));
        }

        /// <summary>The title a listing of the group's entries shows this caller, or null.</summary>
        private string Listed(string[] profiles)
        {
            return Listed("/groups/" + _group.Uuid.ToHexString() + "/entries", profiles);
        }

        /// <summary>The title this listing shows this caller for the entry, or null.</summary>
        private string Listed(string path, string[] profiles)
        {
            return Listed(path, profiles, "title");
        }

        /// <summary>What this listing shows this caller under this member of the entry, or null.</summary>
        private string Listed(string path, string[] profiles, string member)
        {
            var listing = Send("GET", path, null, null, profiles);

            Assert.That(listing.Status, Is.EqualTo(200), listing.Document());
            var shown = JObject.Parse(listing.Document())["entries"]
                .Single(entry => (string)entry["uuid"] == _entry.Uuid.ToHexString());

            return (string)shown[member];
        }

        /// <summary>The value a read of the entry served for this string, or null.</summary>
        private static string Value(GateContext read, string name)
        {
            return (string)JObject.Parse(read.Document())["fields"][name];
        }

        /// <summary>The routes that answer about the entry, by a short name each.</summary>
        private IReadOnlyDictionary<string, string> Routes()
        {
            return new Dictionary<string, string>
            {
                { "entry", Entry() },
                { "fields", Entry() + "/fields" },
                { "field", Entry() + "/fields/" + PwDefs.TitleField },
                { "attachments", Entry() + "/attachments" },
                { "attachment", Entry() + "/attachments/note.txt" },
            };
        }

        private string Entry()
        {
            return "/entries/" + _entry.Uuid.ToHexString();
        }

        private void Granting(string rules)
        {
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(Cases[rules]));
        }

        /// <summary>The tag a read of the entry hands the caller these rules describe.</summary>
        private string Tag(string rules)
        {
            var read = Send("GET", Entry(), null, null, rules);

            Assert.That(read.Status, Is.EqualTo(200), rules + ": " + read.Document());
            return Handed(read);
        }

        /// <summary>The tag each case's caller is handed for the entry as the entry stands.</summary>
        private Dictionary<string, string> Tags()
        {
            var tags = new Dictionary<string, string>();

            foreach (var rules in Cases.Keys)
            {
                Granting(rules);
                tags[rules] = Tag(rules);
                Assert.That(tags[rules], Is.Not.Null, rules);
            }

            return tags;
        }

        /// <summary>
        /// Holds the tags as they are now against the tags before a change: moved for the
        /// cases named, and where they were for every other case.
        /// </summary>
        private void Moved(IReadOnlyDictionary<string, string> before, string change, params string[] moved)
        {
            var after = Tags();

            foreach (var rules in Cases.Keys)
            {
                if (moved.Contains(rules))
                {
                    Assert.That(after[rules], Is.Not.EqualTo(before[rules]), change + ", and the tag stayed for " + rules);
                }
                else
                {
                    Assert.That(after[rules], Is.EqualTo(before[rules]), change + ", and the tag moved for " + rules);
                }
            }
        }

        /// <summary>The `ETag` an answer carried, quotes and all, or null.</summary>
        private static string Handed(GateContext answer)
        {
            return answer.Headers == null ? null : answer.Headers["ETag"];
        }

        /// <summary>The tag a change answered with in its body, quotes and all, or null.</summary>
        private static string Written(GateContext answer)
        {
            var etag = JObject.Parse(answer.Document())["etag"];
            return etag == null ? null : (string)etag;
        }

        private GateContext Send(string method, string path, string body, string ifMatch, string rules)
        {
            return Send(method, path, body, ifMatch, Cases[rules].Select(rule => rule.Profile).ToArray());
        }

        private GateContext Send(string method, string path, string body, string ifMatch, string[] profiles)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(
                    new Immediate(),
                    database =>
                    {
                        _saves++;
                        database.Save(null);
                    },
                    null,
                    null,
                    null),
                TestTags.Fixed,
                null);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ifMatch != null)
            {
                headers["If-Match"] = ifMatch;
            }

            using (var certificate = Minted.Named(_clientName).SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead(method, path, new Dictionary<string, string>(), headers, Bodies.LengthOf(body)),
                    body ?? string.Empty,
                    ClientIdentity.Paired(_clientKey, certificate, profiles));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);

                // An answer written as it is sent is written here, as the transport writes it, so
                // a read of an entry or of a field driven from here reads its values.
                context.Document();
                return context;
            }
        }

        /// <summary>KeePass's thread with nothing ahead of the write.</summary>
        private sealed class Immediate : IUiThread
        {
            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                work();
                return true;
            }
        }
    }
}
