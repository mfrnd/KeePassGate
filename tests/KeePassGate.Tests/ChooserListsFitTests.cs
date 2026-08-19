using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the authority chooser's two lists hold, measured on a dialog that has been shown with
    /// the theme KeePass draws its windows with.
    /// </summary>
    /// <remarks>
    /// **Shown and themed, or it proves nothing.** A list with no window handle reports no rows,
    /// and a list drawn without visual styles has a short, flat header. That is how the gallery
    /// draws this dialog, and it drew it without the scroll bar KeePass then showed beside two
    /// rows: KeePass turns visual styles on as it starts, and under them the header is taller than
    /// a row.
    ///
    /// **In an application domain of its own.** Visual styles are turned on for a whole domain
    /// and cannot be turned off again, so turning them on here would change every window every
    /// later test in this process creates, and which order the tests ran in would decide what they
    /// measured. A domain of its own has its own copy of Windows Forms, and its windows are
    /// registered under names of their own, so its theme stays in it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class ChooserListsFitTests
    {
        [Test]
        [TestCase(2, 2, 1.00f)]
        [TestCase(2, 2, 1.50f)]
        [TestCase(5, 8, 1.00f)]
        [TestCase(5, 8, 2.00f)]
        public void Every_row_a_list_is_sized_for_shows_without_a_scroll_bar(int authorities, int clients, float scaling)
        {
            foreach (var list in Themed(authorities, clients, scaling))
            {
                Assert.That(list.Scrolls, Is.False, list + " has a scroll bar beside rows it has room for");
                Assert.That(list.LastRowBottom, Is.LessThanOrEqualTo(list.ClientHeight), list + " hides part of its last row");
            }
        }

        [Test]
        public void A_list_given_more_rows_than_it_shows_scrolls_rather_than_growing()
        {
            // One more than each list is sized for: the dialog keeps its size, and the rows beyond
            // it are what a scroll bar is for. A list that came out a row taller than it was meant
            // to would show them all and fail this rather than the test above.
            foreach (var list in Themed(6, 9, 1.00f))
            {
                Assert.That(list.Scrolls, Is.True, list + " grew to fit rows it is meant to scroll");
            }
        }

        /// <summary>
        /// The dialog shown with visual styles in a domain of its own, and what its lists report.
        /// </summary>
        private static IReadOnlyList<ListFit> Themed(int authorities, int clients, float scaling)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
            };

            var domain = AppDomain.CreateDomain("themed chooser", null, setup);
            try
            {
                var chooser = (ThemedChooser)domain.CreateInstanceAndUnwrap(
                    typeof(ThemedChooser).Assembly.FullName, typeof(ThemedChooser).FullName);

                return chooser.Measure(authorities, clients, scaling);
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }
    }

    /// <summary>What one list reported once the dialog was shown.</summary>
    [Serializable]
    internal sealed class ListFit
    {
        public string Name { get; set; }

        public int Rows { get; set; }

        public bool Scrolls { get; set; }

        public int LastRowBottom { get; set; }

        public int ClientHeight { get; set; }

        public override string ToString()
        {
            return Name + " (" + Rows.ToString(CultureInfo.InvariantCulture) + " rows, last ending at " +
                   LastRowBottom.ToString(CultureInfo.InvariantCulture) + " of " +
                   ClientHeight.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// The chooser as KeePass shows it, with visual styles on, in whichever domain this is created
    /// in. Only ever created in one made for it: see <see cref="ChooserListsFitTests"/>.
    /// </summary>
    internal sealed class ThemedChooser : MarshalByRefObject
    {
        /// <summary>`GWL_STYLE`.</summary>
        private const int StyleIndex = -16;

        /// <summary>`WS_VSCROLL`, the style a list carries while its vertical scroll bar is shown.</summary>
        private const int VerticalScrollBar = 0x00200000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        public ListFit[] Measure(int authorities, int clients, float scaling)
        {
            Application.EnableVisualStyles();

            var values = new InMemoryKeyValueStore();
            var service = new AuthorityService(values, null);
            var chosen = Authorities(values, service, authorities);
            Admit(values, chosen, clients);

            AuthorityChoice choice;
            using (var reading = service.Read())
            {
                choice = AuthorityChoice.From(reading, new AdmissionStore(values, null).All(), null, null);
            }

            using (var dialog = new AuthorityChooserForm(choice, "Invented Vault"))
            using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
            {
                dialog.Font = font;
                dialog.Select(chosen);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.ShowInTaskbar = false;
                dialog.Show();
                OffScreenForm.Pump();

                var measured = new[]
                {
                    Measured(dialog, GateTab.ChooserAuthorityListName),
                    Measured(dialog, GateTab.ChooserClientListName)
                };

                dialog.Close();
                OffScreenForm.Pump();
                return measured;
            }
        }

        private static ListFit Measured(Control dialog, string name)
        {
            var list = ControlTree.FindByName<ListView>(dialog, name);
            var rows = list.Items.Count;

            return new ListFit
            {
                Name = name,
                Rows = rows,
                Scrolls = (GetWindowLong(list.Handle, StyleIndex) & VerticalScrollBar) != 0,
                LastRowBottom = rows == 0 ? 0 : list.GetItemRect(rows - 1).Bottom,
                ClientHeight = list.ClientSize.Height
            };
        }

        /// <summary>
        /// This many authorities side by side and none retired, answering with the first, which is
        /// the one the clients are admitted under.
        /// </summary>
        private static string Authorities(InMemoryKeyValueStore values, AuthorityService service, int count)
        {
            var aside = new Dictionary<string, string>();
            string first = null;

            for (var i = 0; i < count; i++)
            {
                if (!service.Create().Succeeded)
                {
                    throw new InvalidOperationException("an authority could not be made for the measurement");
                }

                var key = values.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
                aside[key] = values.Get(key);
                first = first ?? AuthorityKeys.FingerprintInAuthority(key);
                values.Remove(key);
            }

            foreach (var pair in aside)
            {
                values.Plant(pair.Key, pair.Value);
            }

            return first;
        }

        private static void Admit(InMemoryKeyValueStore values, string fingerprint, int count)
        {
            using (var authority = CertificateBlob.Load(
                       AuthorityDocument.Read(values.Get(AuthorityKeys.AuthorityKey(fingerprint)), null).Authority))
            {
                for (var i = 0; i < count; i++)
                {
                    var name = "agent-" + i.ToString("00", CultureInfo.InvariantCulture);
                    using (var client = new AuthorityIssuer().IssueClient(authority, name, new[] { "ops" }))
                    {
                        new AdmissionStore(values, null).Write(
                            AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }));
                    }
                }
            }
        }
    }
}
