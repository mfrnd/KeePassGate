using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the authority chooser shows and what its two answers mean, built and pressed without
    /// ever putting a window on the screen.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthorityChooserTests
    {
        /// <summary>How wide the smallest screen the dialog is held to is, at 100%.</summary>
        private const int SmallestScreenWidth = 1024;

        private static readonly DateTimeOffset Admitted = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Requested = new DateTimeOffset(2026, 9, 20, 10, 30, 0, TimeSpan.Zero);

        private InMemoryKeyValueStore _values;
        private AuthorityService _service;
        private string[] _candidates;
        private string _clientKey;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _service = new AuthorityService(_values, null);
            _candidates = TwoAuthorities();
            _clientKey = AdmitUnder(_candidates[0], "agent-fictitious", "ops", "reader");
        }

        [Test]
        public void Each_candidate_is_an_option_with_when_it_was_made_and_the_clients_it_admitted()
        {
            var choice = Choice(null);

            Assert.That(choice.Options.Select(o => o.Fingerprint), Is.EquivalentTo(_candidates));

            var first = choice.Options.Single(o => o.Fingerprint == _candidates[0]);
            Assert.That(first.CanKeep, Is.True);
            Assert.That(first.Created, Is.Not.Null);
            Assert.That(first.Clients.Single().Name, Is.EqualTo("agent-fictitious"), "without the CN= every row would carry");
            Assert.That(first.Clients.Single().Profiles, Is.EqualTo(new[] { "ops", "reader" }));
            Assert.That(first.Clients.Single().Admitted, Is.EqualTo(Admitted));

            var second = choice.Options.Single(o => o.Fingerprint == _candidates[1]);
            Assert.That(second.Clients, Is.Empty, "a client belongs to the authority its certificate chains to");
        }

        [Test]
        public void The_last_request_comes_from_the_audit_and_says_so_when_there_is_none()
        {
            var choice = Choice(keys => new Dictionary<string, DateTimeOffset> { { _clientKey, Requested } });
            var client = choice.Options.Single(o => o.Fingerprint == _candidates[0]).Clients.Single();

            Assert.That(client.LastSeen, Is.EqualTo(Requested));
            Assert.That(AuthorityChooserForm.LastRequest(client, choice.LastSeenKnown), Is.EqualTo(Moments.OnScreen(Requested)));

            var nothing = Choice(keys => new Dictionary<string, DateTimeOffset>());
            var unseen = nothing.Options.Single(o => o.Fingerprint == _candidates[0]).Clients.Single();
            Assert.That(AuthorityChooserForm.LastRequest(unseen, nothing.LastSeenKnown), Is.EqualTo(AuthorityChooserForm.NeverSeen),
                "absent from the audit is not the same as never having connected, and the words say which");
        }

        [Test]
        public void An_audit_that_cannot_be_read_makes_every_last_request_unknown()
        {
            var choice = Choice(keys => null);
            var client = choice.Options.Single(o => o.Fingerprint == _candidates[0]).Clients.Single();

            Assert.That(choice.LastSeenKnown, Is.False);
            Assert.That(AuthorityChooserForm.LastRequest(client, choice.LastSeenKnown), Is.EqualTo(AuthorityChooserForm.Unknown));
        }

        [Test]
        public void A_candidate_that_will_not_read_is_listed_and_cannot_be_kept()
        {
            _values.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");

            var option = Choice(null).Options.Single(o => o.Fingerprint == new string('d', 64));

            Assert.That(option.CanKeep, Is.False);
            Assert.That(option.Problem, Is.Not.Empty);
        }

        [Test]
        public void The_dialog_says_what_happened_and_what_choosing_does()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), "Invented Vault"))
            {
                var text = string.Join(" ", AllText(dialog));

                Assert.That(text, Does.Contain("Invented Vault holds 2 certificate authorities."));
                Assert.That(text, Does.Contain(AuthorityChooserForm.Consequence));
            }
        }

        [Test]
        public void The_selected_authority_shows_its_full_fingerprint_and_its_clients()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                dialog.Select(_candidates[0]);

                Assert.That(Box(dialog, GateTab.ChooserFingerprintBoxName).Text, Is.EqualTo(_candidates[0]));
                Assert.That(Label(dialog, GateTab.ChooserDetailLabelName).Text, Is.Empty,
                    "a readable authority has nothing wrong to say, and no date worth one: CA.md §219");

                var clients = List(dialog, GateTab.ChooserClientListName);
                Assert.That(clients.Items.Count, Is.EqualTo(1));
                Assert.That(clients.Items[0].Text, Is.EqualTo("agent-fictitious"));
                Assert.That(clients.Items[0].SubItems[1].Text, Is.EqualTo("ops, reader"));

                dialog.Select(_candidates[1]);
                Assert.That(clients.Items.Count, Is.Zero);
            }
        }

        [Test]
        public void Every_authority_is_a_row_named_by_the_short_fingerprint_the_tab_shows()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                var rows = List(dialog, GateTab.ChooserAuthorityListName).Items.Cast<ListViewItem>().Select(r => r.Text);

                Assert.That(rows, Is.EquivalentTo(_candidates.Select(CertificateDigest.Shorten)));
            }
        }

        [Test]
        public void A_client_that_renewed_is_one_client_in_the_row_of_its_authority()
        {
            // Listed once per certificate under the selected authority, since each can be taken
            // away on its own, and counted once in the row, since both carry one client key.
            Admit(_candidates[0], "agent-fictitious", _clientKey, "ops", "reader");

            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                var row = List(dialog, GateTab.ChooserAuthorityListName).Items.Cast<ListViewItem>()
                    .Single(item => item.Text == CertificateDigest.Shorten(_candidates[0]));

                Assert.That(row.SubItems[2].Text, Is.EqualTo("1"));
            }
        }

        [Test]
        public void Two_programs_that_paired_under_one_name_are_two_clients_in_the_row()
        {
            // A client chooses its own name, so one name is not one client: counted by name, a
            // second program under it went uncounted.
            AdmitUnder(_candidates[0], "agent-fictitious", "ops", "reader");

            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                var row = List(dialog, GateTab.ChooserAuthorityListName).Items.Cast<ListViewItem>()
                    .Single(item => item.Text == CertificateDigest.Shorten(_candidates[0]));

                Assert.That(row.SubItems[2].Text, Is.EqualTo("2"));
            }
        }

        [Test]
        public void Keeping_keeps_the_selected_one()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                dialog.Select(_candidates[1]);
                dialog.Keep();

                Assert.That(dialog.Chosen, Is.EqualTo(_candidates[1]));
                Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.OK));
            }
        }

        [Test]
        public void Canceling_keeps_nothing()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                dialog.Select(_candidates[0]);
                dialog.Cancel();

                Assert.That(dialog.Chosen, Is.Null);
                Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.Cancel));
            }
        }

        [Test]
        public void One_that_will_not_read_cannot_be_kept_from_the_dialog_either()
        {
            _values.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");

            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                dialog.Select(new string('d', 64));

                Assert.That(Button(dialog, GateTab.KeepButtonName).Enabled, Is.False);
                Assert.That(Label(dialog, GateTab.ChooserDetailLabelName).Text, Does.StartWith("This one cannot be read"));

                dialog.Keep();
                Assert.That(dialog.Chosen, Is.Null);
            }
        }

        [Test]
        public void Escape_cancels_and_Enter_reaches_nothing()
        {
            // Keeping retires the others for good, so it takes a press. A dialog that can be
            // dismissed into a choice is a dialog that will be.
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                Assert.That(dialog.AcceptButton, Is.Null);
                Assert.That(((Button)dialog.CancelButton).Name, Is.EqualTo(GateTab.ChooserCancelButtonName));
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void Everything_can_be_seen_and_pressed_at_any_scaling(float scaling)
        {
            // **Built at the larger font rather than given it afterwards**, because a scaled display
            // hands the dialog its font before it is built, as the approval dialog's test explains.
            //
            // **Held to the room the dialog asks for, not the room it was given.** WinForms holds a
            // window to the screen it is on, so on a screen narrower than the dialog the client area
            // stops growing and this would be measuring the machine running it. Whether the dialog
            // fits the smallest screen at this scaling is asked separately, and of nothing but the
            // dialog: 1024 pixels across at 100%.
            using (var larger = Scaled(Control.DefaultFont, scaling))
            using (var dialog = new AuthorityChooserForm(Choice(null), "Invented Vault", larger))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var room = Asked(dialog);
                Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the dialog is wider than the smallest screen at " + scaling);

                foreach (var name in new[]
                         {
                             GateTab.KeepButtonName, GateTab.ChooserCancelButtonName,
                             GateTab.ChooserAuthorityListName, GateTab.ChooserClientListName,
                             GateTab.ChooserFingerprintBoxName
                         })
                {
                    var control = ControlTree.FindByName<Control>(dialog, name);
                    Assert.That(control, Is.Not.Null, name);

                    var inside = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    Assert.That(inside.Bottom, Is.LessThanOrEqualTo(room.Height), name + " is off the bottom at " + scaling);
                    Assert.That(inside.Right, Is.LessThanOrEqualTo(room.Width), name + " is off the side at " + scaling);
                }

                Assert.That(dialog.AutoScroll, Is.False, "the dialog itself never scrolls");
            }
        }

        [Test]
        public void The_dialog_grows_with_the_font_rather_than_clipping()
        {
            using (var larger = Scaled(Control.DefaultFont, 1.5f))
            using (var ordinary = new AuthorityChooserForm(Choice(null), null))
            using (var enlarged = new AuthorityChooserForm(Choice(null), null, larger))
            {
                ordinary.CreateControl();
                ordinary.PerformLayout();

                enlarged.CreateControl();
                enlarged.PerformLayout();

                Assert.That(Asked(enlarged).Height, Is.GreaterThan(Asked(ordinary).Height));
                Assert.That(Asked(enlarged).Width, Is.GreaterThan(Asked(ordinary).Width));
            }
        }

        /// <summary>The same font at a larger size, which is what a scaled display amounts to here.</summary>
        private static Font Scaled(Font font, float scaling)
        {
            return new Font(font.FontFamily, font.Size * scaling, font.Style);
        }

        /// <summary>
        /// The client area the dialog asks for, which is the one it has until the screen it is on
        /// is too small for it: its preferred size is not held to the screen, its size is.
        /// </summary>
        private static Size Asked(Form dialog)
        {
            return dialog.PreferredSize - (dialog.Size - dialog.ClientSize);
        }

        [Test]
        public void The_two_answers_sit_on_the_same_line()
        {
            using (var dialog = new AuthorityChooserForm(Choice(null), null))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var keep = Button(dialog, GateTab.KeepButtonName);
                var cancel = Button(dialog, GateTab.ChooserCancelButtonName);

                Assert.That(keep.Top, Is.EqualTo(cancel.Top));
                Assert.That(keep.Height, Is.EqualTo(cancel.Height));
            }
        }

        private AuthorityChoice Choice(Func<ICollection<string>, IDictionary<string, DateTimeOffset>> lastSeen)
        {
            using (var reading = _service.Read())
            {
                Assume.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                return AuthorityChoice.From(reading, new AdmissionStore(_values, null).All(), lastSeen, null);
            }
        }

        /// <summary>
        /// Two authorities side by side and neither retired, which is what two copies each
        /// recreating and then being merged leave behind.
        /// </summary>
        private string[] TwoAuthorities()
        {
            Assume.That(_service.Create().Succeeded, Is.True);
            var first = _values.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            var document = _values.Get(first);
            _values.Remove(first);

            Assume.That(_service.Create().Succeeded, Is.True);
            var second = _values.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            _values.Plant(first, document);

            return new[] { AuthorityKeys.FingerprintInAuthority(first), AuthorityKeys.FingerprintInAuthority(second) };
        }

        /// <summary>Admits a client whose certificate the named candidate issued, answering with its key.</summary>
        private string AdmitUnder(string fingerprint, string name, params string[] profiles)
        {
            return Admit(fingerprint, name, null, profiles);
        }

        /// <summary>
        /// The same, for the client this client key names, as a renewal writes it, or for a new
        /// client when the key is null.
        /// </summary>
        private string Admit(string fingerprint, string name, string clientKey, params string[] profiles)
        {
            var authority = CertificateBlob.Load(
                AuthorityDocument.Read(_values.Get(AuthorityKeys.AuthorityKey(fingerprint)), null).Authority);

            using (authority)
            using (var client = new AuthorityIssuer().IssueClient(authority, name, profiles))
            {
                var record = clientKey == null
                    ? AdmissionRecord.For(client, Admitted, profiles)
                    : AdmissionRecord.For(client, Admitted, profiles, clientKey);
                new AdmissionStore(_values, null).Write(record);
                return record.AdmissionKey;
            }
        }

        private static IEnumerable<string> AllText(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child.Text;
                foreach (var text in AllText(child))
                {
                    yield return text;
                }
            }
        }

        private static ListView List(Control dialog, string name)
        {
            var list = ControlTree.FindByName<ListView>(dialog, name);
            Assert.That(list, Is.Not.Null, "no list called " + name);
            return list;
        }

        private static TextBox Box(Control dialog, string name)
        {
            var box = ControlTree.FindByName<TextBox>(dialog, name);
            Assert.That(box, Is.Not.Null, "no box called " + name);
            return box;
        }

        private static Label Label(Control dialog, string name)
        {
            var label = ControlTree.FindByName<Label>(dialog, name);
            Assert.That(label, Is.Not.Null, "no label called " + name);
            return label;
        }

        private static Button Button(Control dialog, string name)
        {
            var button = ControlTree.FindByName<Button>(dialog, name);
            Assert.That(button, Is.Not.Null, "no button called " + name);
            return button;
        }
    }
}
