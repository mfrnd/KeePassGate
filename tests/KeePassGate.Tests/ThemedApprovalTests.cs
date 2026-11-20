using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The checks of the approval dialogs as KeePass shows them, with visual styles on: what a
    /// first dialog, a dialog shown again and the renewal dialog open with.
    /// </summary>
    /// <remarks>
    /// **Themed, since that is where the checks went.** Under the visual styles KeePass turns on,
    /// the table's box is measured once the table has a window at another place than the estimate
    /// before it, so the table is laid again there; without them the two agree and nothing moves.
    ///
    /// **In an application domain of its own**, as <see cref="ChooserListsFitTests"/> is and for
    /// its reason: visual styles are turned on for a whole domain and cannot be turned off again.
    /// **Shown, so it runs alone and holds the desktop.**
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class ThemedApprovalTests
    {
        [Test]
        public void A_first_dialog_opens_with_every_profile_asked_for_checked()
        {
            Assert.That(Themed(approvals => approvals.FirstDialog()), Is.EqualTo(new[] { true, true, true }));
        }

        [Test]
        public void A_dialog_shown_again_opens_with_the_changed_row_unchecked_and_the_rest_as_approved()
        {
            // `ops` changed and approved before, `deploy` unchecked by the operator before, `reader`
            // approved before and unchanged, `PAIRING.md` §311.
            Assert.That(Themed(approvals => approvals.ShownAgain()), Is.EqualTo(new[] { false, false, true }));
        }

        [Test]
        public void The_renewal_dialog_opens_with_every_profile_it_keeps_checked()
        {
            Assert.That(Themed(approvals => approvals.Renewal()), Is.EqualTo(new[] { true, true, true }));
        }

        private static bool[] Themed(Func<ThemedApprovals, bool[]> look)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
            };

            var domain = AppDomain.CreateDomain("themed approvals", null, setup);
            try
            {
                var approvals = (ThemedApprovals)domain.CreateInstanceAndUnwrap(
                    typeof(ThemedApprovals).Assembly.FullName, typeof(ThemedApprovals).FullName);

                return look(approvals);
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }
    }

    /// <summary>
    /// The approval dialogs shown with visual styles on, in whichever domain this is created in.
    /// Only ever created in a domain made for it: see <see cref="ThemedApprovalTests"/>.
    /// </summary>
    internal sealed class ThemedApprovals : MarshalByRefObject
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        /// <summary>The checks a first dialog of three names on the list opens with.</summary>
        public bool[] FirstDialog()
        {
            Application.EnableVisualStyles();
            var asked = new[] { "ops", "deploy", "reader" };
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, new[] { "reader" },
                "127.0.0.1:60001", null, null, new[] { "ops" });
            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                return Checks(Shown(dialog), GateTab.ApprovalProfilesListName);
            }
        }

        /// <summary>
        /// The checks a dialog shown again opens with: `ops` on the list before and removed now,
        /// `deploy` new before and now and unchecked by the operator, `reader` on the list both
        /// times.
        /// </summary>
        public bool[] ShownAgain()
        {
            Application.EnableVisualStyles();
            var asked = new[] { "ops", "deploy", "reader" };
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Name(store, "reader");
            AskedProfiles before;
            using (var reading = store.Read())
            {
                before = AskedProfiles.Of(reading, asked, DateTimeOffset.UtcNow);
            }

            var request = new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, new[] { "reader" },
                "127.0.0.1:60001", null, null, new[] { "ops" }, PairingApproval.Approved(new[] { "ops", "reader" }), before);
            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                return Checks(Shown(dialog), GateTab.ApprovalProfilesListName);
            }
        }

        /// <summary>The checks the renewal dialog opens with for three names it keeps.</summary>
        public bool[] Renewal()
        {
            Application.EnableVisualStyles();
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow.AddDays(40),
                new[] { "ops", "deploy", "reader" }, Array.Empty<string>(), "127.0.0.1:60001", null);
            using (var dialog = new RenewalApprovalForm(request, Timeout, null))
            {
                return Checks(Shown(dialog), GateTab.RenewalProfilesListName);
            }
        }

        private static Form Shown(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            OffScreenForm.Pump();
            return dialog;
        }

        /// <summary>The check of each row of the named table or list, as its boxes hold it.</summary>
        private static bool[] Checks(Form dialog, string name)
        {
            var list = ControlTree.FindByName<CheckedListBox>(dialog, name);
            if (list != null)
            {
                return Enumerable.Range(0, list.Items.Count).Select(list.GetItemChecked).ToArray();
            }

            return ProfileRows.Checks(ProfileRows.Table(dialog, name));
        }
    }
}
