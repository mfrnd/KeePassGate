using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The seal's dialog, `STORE.md` §625: every string it shows, from what the stopped store
    /// lost; its buttons and Escape; and the room it takes at every scaling, nothing scrolling
    /// sideways.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SealFormTests
    {
        private const int SmallestScreenWidth = 1024;

        /// <summary>
        /// How tall the working area of the smallest screen the dialog is held to is, at 100%: a
        /// screen 768 pixels tall less a taskbar of 48.
        /// </summary>
        private const int SmallestWorkingHeight = 720;

        private static readonly string Node = new string('1', 32);

        [Test]
        public void Each_kind_of_loss_is_a_row_of_its_kind_what_it_is_about_and_what_the_seal_makes_of_it()
        {
            var store = Stopped(out var revokedApart, out var damaged);
            using (var reading = store.Read())
            {
                var losses = StoreLosses.Of(reading);
                var rows = SealRows.Of(
                    losses,
                    Array.Empty<string>(),
                    node => node == Node ? "Banking" : null,
                    client => client == Hex('e') ? "agent-7" : null);

                Assert.That(
                    rows.Select(row => row.Kind + " | " + row.Of + " | " + row.After),
                    Is.EquivalentTo(new[]
                    {
                        "Summary entry | height 9 | named as replaced",
                        "Rule of a profile | Banking, ops | an empty rule",
                        "Switch | Banking | the switch refusing",
                        "Answer to renewals | agent-7 (eeeeeeee) | refused forever",
                        "Revocation | " + revokedApart.Substring(0, 8) + " | restated, stays in force",
                        "Damaged entry | " + damaged.Substring(0, 8) + " | deleted with a record",
                    }));
                Assert.That(rows.Single(row => row.Kind == SealRows.Damaged).Tip, Does.StartWith(damaged).And.Contain("deletion record"));

                using (var form = new SealForm(losses.Reason, rows, "Kept: 1 authority."))
                {
                    Assert.That(form.Text, Is.EqualTo("Seal the store again"));
                    Assert.That(Text(form, "KpgSealReason"), Is.EqualTo(SealForm.StoppedStart + losses.Reason));
                    Assert.That(Text(form, "KpgSealOver"), Is.EqualTo("Deletion records show these as deleted:"));
                    Assert.That(Text(form, "KpgSealConsequence"), Is.EqualTo(
                        "What was deleted with no deletion record cannot be shown. Sealing makes the state shown the state, "
                        + "in this copy and in every copy that takes the file in."));
                    Assert.That(Text(form, GateTab.SealKeptLabelName), Is.EqualTo("Kept: 1 authority."));
                    var list = ControlTree.FindByName<ListView>(form, GateTab.SealListName);
                    Assert.That(
                        list.Items.Cast<ListViewItem>().Select(item => item.Text + " | " + item.SubItems[1].Text + " | " + item.SubItems[2].Text),
                        Is.EqualTo(rows.Select(row => row.Kind + " | " + row.Of + " | " + row.After)));
                    Assert.That(list.Columns.Cast<ColumnHeader>().Select(column => column.Text), Is.EqualTo(new[] { "Deleted", "Of", "After sealing" }));
                }
            }
        }

        [Test]
        public void A_loss_the_seal_leaves_out_and_an_object_a_damaged_version_leaves_refusing_refuse_until_set_again()
        {
            var losses = StoreLosses.Of(Stopped(out _, out _).Read());
            var row = StoreComposer.RowObject(Node, "ops");
            var rows = SealRows.Of(losses, new[] { row }, node => null, client => null);

            Assert.That(rows.Single(listed => listed.Kind == SealRows.RuleOfAProfile).Of, Is.EqualTo("a node this database does not hold, ops"));
            Assert.That(rows.Single(listed => listed.Kind == SealRows.RuleOfAProfile).After, Is.EqualTo("refuses until set again"));
        }

        [Test]
        public void A_row_s_tooltip_carries_every_deletion_record_that_tells_of_its_object()
        {
            var store = Stopped(out _, out _);
            store.Delete(StoreComposer.IdentifierOf(One, "version", "rule:" + Node, StoreComposer.ProfileTag(One, "ops"), new string('8', 64)));
            var losses = StoreLosses.Of(store.Read());
            var lost = losses.Lost.Single(loss => loss.Object == StoreComposer.RowObject(Node, "ops")).Lost;
            Assume.That(lost, Has.Count.EqualTo(2), "two deletion records tell of the row");

            var row = SealRows.Of(losses, Array.Empty<string>(), node => "Banking", client => null).Single(listed => listed.Kind == SealRows.RuleOfAProfile);

            foreach (var identifier in lost)
            {
                Assert.That(row.Tip, Does.Contain(identifier));
            }
        }

        [Test]
        public void A_revocation_s_and_a_retirement_s_tooltips_carry_the_whole_key_and_every_deletion_record_that_tells_of_them()
        {
            var store = Stopped(out var revoked, out _);
            var retirement = StoreComposer.IdentifierOf(One, "retired", "authority:" + Hex('a'), 0, new string('5', 64));
            store.Delete(retirement);
            var losses = StoreLosses.Of(store.Read());

            var rows = SealRows.Of(losses, Array.Empty<string>(), node => null, client => null);

            var revocation = rows.Single(row => row.Kind == SealRows.Revocation);
            Assert.That(revocation.Tip, Does.Contain(revoked));
            Assert.That(revocation.Tip, Does.Contain(StoreComposer.HexOf(StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('e'), 0, new string('7', 64)))));
            var retired = rows.Single(row => row.Kind == SealRows.Retirement);
            Assert.That(retired.Tip, Does.Contain(Hex('a')));
            Assert.That(retired.Tip, Does.Contain(StoreComposer.HexOf(retirement)));
        }

        [Test]
        public void A_decision_whose_subject_the_database_names_nowhere_is_a_row_of_its_kind_with_every_deletion_record_in_its_tooltip()
        {
            // `STORE.md` §611 and §625: a revocation told by two deletion records, a retirement
            // and a removal, each of a subject no record or item names, which the seal restates
            // with no subject; and a revocation an item with no subject already carries, which no
            // deletion record alone tells of.
            var store = new ComposedStore(One);
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 9, new string('5', 64)));
            var revocation = StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('c'), 0, new string('5', 64));
            var again = StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('c'), 0, new string('6', 64));
            var retirement = StoreComposer.IdentifierOf(One, "retired", "authority:" + Hex('9'), 0, new string('5', 64));
            var removal = StoreComposer.IdentifierOf(One, "removed", "profile:reader", 0, new string('5', 64));
            var carried = StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('d'), 0, new string('5', 64));
            foreach (var deleted in new[] { revocation, again, retirement, removal, carried })
            {
                store.Deleted.Add(deleted);
            }

            store.Restate(StoreComposer.Item(("hash", StoreComposer.HexOf(carried).Substring(0, 16)), ("kind", "revoked")));
            using (var reading = store.Read())
            {
                var rows = SealRows.Of(StoreLosses.Of(reading), Array.Empty<string>(), node => null, client => null);

                Assert.That(
                    rows.Select(row => row.Kind + " | " + row.Of + " | " + row.After),
                    Is.EqualTo(new[]
                    {
                        "Summary entry | height 9 | named as replaced",
                        "Revocation | a client named nowhere | restated, stays in force",
                        "Removal of a name | a name held nowhere | restated, stays in force",
                        "Retirement | an authority named nowhere | restated, stays in force",
                    }));
                var revoked = rows.Single(row => row.Kind == SealRows.Revocation);
                Assert.That(revoked.Tip, Does.Contain(StoreComposer.HexOf(revocation)).And.Contain(StoreComposer.HexOf(again)).And.Contain("restates it"));
                Assert.That(revoked.Tip, Does.Not.Contain(StoreComposer.HexOf(carried)), "the revocation an item carries is no row");
                Assert.That(rows.Single(row => row.Kind == SealRows.Retirement).Tip, Does.StartWith(StoreComposer.HexOf(retirement)));
                Assert.That(rows.Single(row => row.Kind == SealRows.Removal).Tip, Does.StartWith(StoreComposer.HexOf(removal)));
            }
        }

        [Test]
        public void With_no_deletion_record_showing_a_loss_the_dialog_says_so_in_the_list_s_place()
        {
            using (var form = new SealForm("no summary entry of the store is in force", Array.Empty<SealRow>(), "Kept: 1 authority."))
            {
                Assert.That(Text(form, "KpgSealNoneShown"), Is.EqualTo("No deletion record shows a loss."));
                Assert.That(ControlTree.FindByName<Control>(form, "KpgSealOver"), Is.Null);
                Assert.That(ControlTree.FindByName<ListView>(form, GateTab.SealListName), Is.Null, "no list");
            }
        }

        [Test]
        public void Seal_again_is_the_default_and_Escape_cancels()
        {
            using (var form = new SealForm("reason", Array.Empty<SealRow>(), "Kept: nothing."))
            {
                Assert.That(((Button)form.AcceptButton).Name, Is.EqualTo(GateTab.SealConfirmButtonName));
                Assert.That(((Button)form.AcceptButton).Text, Is.EqualTo("Seal again"));
                Assert.That(((Button)form.CancelButton).Name, Is.EqualTo(GateTab.SealCancelButtonName), "Escape presses Cancel");
                form.Cancel();
                Assert.That(form.Sealed, Is.False);
            }

            using (var form = new SealForm("reason", Array.Empty<SealRow>(), "Kept: nothing."))
            {
                form.Seal();
                Assert.That(form.Sealed, Is.True);
            }
        }

        [Test]
        public void What_the_seal_keeps_is_counted_for_any_number()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            using (var reading = store.Read())
            {
                Assert.That(SealRows.Kept(reading), Is.EqualTo("Kept: 1 authority, 0 clients, 0 profile names, 0 ruled nodes, 0 standing answers."));
            }

            var none = new ComposedStore(One);
            none.Drop(none.Authority);
            none.Add(StoreComposer.Record(One, "retired", "authority:" + Hex('a'), StoreComposer.Content(("authority", Hex('a')), ("storeKey", StoreComposer.HexOf(One)))));
            Assert.That(SealRows.Kept(none.Read()), Does.StartWith("Kept: no authority, "));
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void A_hundred_rows_of_the_longest_texts_fit_at_any_scaling_and_nothing_scrolls_sideways(float scaling)
        {
            var rows = Enumerable.Range(0, 100)
                .Select(at => new SealRow(
                    SealRows.Withdrawn,
                    new string('W', 32) + ", " + new string('w', 64),
                    SealRows.Restated,
                    new string('5', 32)))
                .ToList();
            // A reason in words, as a reading composes a reason, longer than the dialog shows of it,
            // so that it wraps onto every line the clipped text takes.
            var reason = string.Join(" ", Enumerable.Repeat("a summary entry was deleted that nothing in force replaces, and entries have no version that passes;", 8));
            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling, Control.DefaultFont.Style))
            using (var form = new SealForm(reason, rows, "Kept: 9,999 authorities, 9,999 clients, 9,999 profile names, 9,999 ruled nodes, 9,999 standing answers.", larger))
            {
                form.CreateControl();
                form.PerformLayout();

                var room = form.PreferredSize - (form.Size - form.ClientSize);
                Assert.That(form.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling), "wider than the smallest screen at " + scaling);
                Assert.That(form.PreferredSize.Height, Is.LessThanOrEqualTo(SmallestWorkingHeight * scaling), "taller than the smallest screen's working area at " + scaling);
                var shownReason = ControlTree.FindByName<Label>(form, "KpgSealReason");
                Assert.That(shownReason.Height, Is.GreaterThan(shownReason.Font.Height), "the reason wraps as a reading's does");
                var list = ControlTree.FindByName<ListView>(form, GateTab.SealListName);
                Assert.That(
                    list.Columns.Cast<ColumnHeader>().Sum(column => column.Width),
                    Is.LessThanOrEqualTo(list.Width - SystemInformation.VerticalScrollBarWidth),
                    "the columns share the list's width less a scroll bar's");
                foreach (var name in new[] { GateTab.SealConfirmButtonName, GateTab.SealCancelButtonName, GateTab.SealListName, GateTab.SealKeptLabelName, "KpgSealConsequence" })
                {
                    var control = ControlTree.FindByName<Control>(form, name);
                    var inside = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    Assert.That(inside.Right, Is.LessThanOrEqualTo(room.Width), name + " is off the side at " + scaling);
                    Assert.That(inside.Bottom, Is.LessThanOrEqualTo(room.Height), name + " is off the bottom at " + scaling);
                }

                Assert.That(form.AutoScroll, Is.False);
            }
        }

        /// <summary>
        /// A store stopped by a summary entry deleted that nothing replaces, beside a lost row, a
        /// lost switch, a lost answer, a revocation known by its deletion record alone and an
        /// entry that is damage.
        /// </summary>
        private static MemoryStore Stopped(out string revokedApart, out string damaged)
        {
            var store = new MemoryStore();
            store.Put(StoreRecords.Authority(One, 'a'));
            var standing = StoreComposer.RuleVersion(One, StoreComposer.RowObject(Node, "ops"), "row:R");
            store.Put(standing);
            store.Put(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("certificate", Hex('e')), ("client", Hex('e')))));
            store.Put(Head(One, 1, string.Empty, StoreComposer.RuleVersionItem(standing, StoreComposer.RowObject(Node, "ops"), "row:R")));
            store.Delete(StoreComposer.IdentifierOf(One, "head", "store", 9, new string('5', 64)));
            store.Delete(StoreComposer.IdentifierOf(One, "version", "rule:" + Node, StoreComposer.ProfileTag(One, "ops"), new string('6', 64)));
            store.Delete(StoreComposer.IdentifierOf(One, "version", StoreComposer.SwitchObject(Node), 0, new string('6', 64)));
            store.Delete(StoreComposer.IdentifierOf(One, "version", RenewalText.ObjectOf(Hex('e')), 0, new string('6', 64)));
            var revocation = StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('e'), 0, new string('7', 64));
            store.Delete(revocation);
            revokedApart = Hex('e');
            var edited = StoreComposer.Record(One, "retired", "authority:" + Hex('9'), StoreComposer.Content(("authority", Hex('9'))));
            store.Put(edited.Identifier, StoreVersions.With(edited.Strings, StoreComposer.Data, "{\"edited\":\"\"}"));
            damaged = edited.Hex;
            return store;
        }

        private static string Text(Form form, string name)
        {
            return ControlTree.FindByName<Control>(form, name).Text;
        }
    }
}
