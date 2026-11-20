using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using KeePassGate.Bridge;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// What UI automation reports of the pairing dialog's table of profiles: each cell a text, and
    /// each row's box a check box read out as the row, not an unnamed box.
    /// </summary>
    /// <remarks>
    /// **In this namespace and outside the end to end category**, so that it runs in the host tier
    /// and `LiveRun` ends the queue the client-side providers start, which the first walk into a
    /// window's controls starts and an unload would otherwise wait on, `ClientSideProviderQueue`.
    ///
    /// **Read on a thread of its own**, while this thread waits in a join, since the providers
    /// answer through messages to the dialog's own windows, which this thread owns, and a join on
    /// this thread answers each message as the message arrives, `OnAThreadOfItsOwn`.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class ApprovalTableAutomationTests
    {
        [Test]
        public void A_row_is_read_as_its_profile_and_its_box_as_that_profile_s_check_box()
        {
            using (var database = TemporaryDatabase.New())
            {
                var store = Listing(database, "reader", "gone");
                StoreSeeds.Unname(store, "gone");
                using (var dialog = new PairingApprovalForm(Shown(store, new[] { "ops", "gone", "reader" }), TimeSpan.FromMinutes(2)))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    Application.DoEvents();

                    var table = ProfileRows.Table(dialog, GateTab.ApprovalProfilesListName);
                    ProfileRows.Boxes(table)[1].Checked = false;
                    var read = Read(table.Handle);

                    Assert.That(read.Name, Is.EqualTo("Profiles the client asks for"), "the line above the table, without its colon");

                    // The headings, then each row's cells of text after its box, in the order of
                    // the columns; a cell with nothing in it is not read at all, where an empty
                    // label would be read by the name of the label before it.
                    Assert.That(read.Texts, Is.EqualTo(new[]
                    {
                        "Grant", "Profile", "Approve also", "Regained by",
                        "ops", PairingApprovalForm.NewState,
                        "gone", PairingApprovalForm.RemovedState,
                        "reader"
                    }));

                    // Each row's box, a check box with its check, read out as the row: the name,
                    // and what Approve does for the name in its state.
                    Assert.That(read.Boxes, Is.EqualTo(new[]
                    {
                        Box + "ops. " + PairingApprovalForm.NewTip + " On",
                        Box + "gone. " + PairingApprovalForm.RemovedTip + " Off",
                        Box + "reader On"
                    }));
                }
            }
        }

        [Test]
        public void Every_part_of_the_pairing_dialog_is_read_by_a_name_of_its_own()
        {
            // Clients on two rows: `audit` regained by a client and `backup` by two. A part with no
            // name of its own is read by the name of the part before it: the row of `backup` was
            // read as the clients of `audit`, and the headings, the lifetime and the footer as the
            // line before them.
            using (var database = TemporaryDatabase.New())
            using (var first = Minted.Named("CN=agent-a").SelfSigned().Read())
            using (var second = Minted.Named("CN=agent-b").SelfSigned().Read())
            using (var third = Minted.Named("CN=agent-c").SelfSigned().Read())
            {
                var store = Listing(database, "reader");
                StoreSeeds.Admit(store, first, DateTimeOffset.UtcNow, "audit");
                StoreSeeds.Admit(store, second, DateTimeOffset.UtcNow, "backup");
                StoreSeeds.Admit(store, third, DateTimeOffset.UtcNow, "backup");
                StoreSeeds.Unname(store, "audit");
                StoreSeeds.Unname(store, "backup");
                var request = Shown(store, new[] { "reader", "audit", "backup" });
                Assert.That(new[] { "audit", "backup" }.Select(profile => request.Returning(profile).Count), Is.EqualTo(new[] { 1, 2 }));

                using (var dialog = new PairingApprovalForm(request, TimeSpan.FromMinutes(2)))
                {
                    Away(dialog);
                    var parts = Parts(dialog.Handle);

                    Assert.That(parts.Where(part => part.Name.Length == 0), Is.Empty, "parts read by no name");
                    Assert.That(parts.Where(part => part.Type == "ControlType.Pane" && part.Id.StartsWith("Kpg", StringComparison.Ordinal))
                        .Select(part => part.Id + ": " + part.Name), Is.EqualTo(new[]
                    {
                        "KpgApprovalLayout: Pairing request",
                        "KpgApprovalDetailLabel: Details",
                        "KpgApprovalAskingRow: Status",
                        "KpgApprovalProfilesList: Profiles the client asks for",
                        "KpgProfileHeadings: Headings",
                        "KpgProfileRows: Profiles the client asks for",
                        "KpgProfileRow: reader",
                        "KpgProfileRow: audit",
                        "KpgProfileRow: backup",
                        "KpgApprovalLifetimeRow: Lifetime",
                        "KpgApprovalFooter: Answer",
                        "KpgApprovalButtonRow: Buttons"
                    }));

                    // Refresh, which has a picture and no text, by its name, and nothing stated
                    // beside it on a first dialog, where the words are hidden rather than empty.
                    Assert.That(parts.Where(part => part.Id == GateTab.ApprovalRefreshName).Select(part => part.Type + ": " + part.Name),
                        Is.EqualTo(new[] { "ControlType.Button: Refresh" }));
                    Assert.That(parts.Where(part => part.Id == GateTab.ApprovalChangeName), Is.Empty, "words beside Refresh on a first dialog");
                }
            }
        }

        [Test]
        public void The_words_beside_Refresh_are_read_by_what_they_state()
        {
            // A dialog shown again states so beside Refresh, and the words are a text read by
            // what they state, on the line naming the table and before the table.
            using (var database = TemporaryDatabase.New())
            {
                var asked = new[] { "ops", "reader" };
                var store = Listing(database, asked);
                var before = Asked(store, asked);
                StoreSeeds.Unname(store, "reader");
                using (var dialog = new PairingApprovalForm(Shown(store, asked, PairingApproval.Approved(asked), before), TimeSpan.FromMinutes(2)))
                {
                    Away(dialog);
                    var parts = Parts(dialog.Handle);
                    var ids = parts.Select(part => part.Id).ToList();

                    Assert.That(parts.Where(part => part.Id == GateTab.ApprovalChangeName).Select(part => part.Type + ": " + part.Name),
                        Is.EqualTo(new[] { "ControlType.Text: Shown again: the table changed." }));
                    Assert.That(ids.IndexOf(GateTab.ApprovalChangeName), Is.InRange(ids.IndexOf("KpgApprovalAskingRow"), ids.IndexOf(GateTab.ApprovalProfilesListName)),
                        "the words stand elsewhere than on the line naming the table");
                    Assert.That(ids.IndexOf(GateTab.ApprovalRefreshName), Is.LessThan(ids.IndexOf(GateTab.ApprovalProfilesListName)), "Refresh is read after the table");
                }
            }
        }

        [Test]
        public void Every_part_of_the_renewal_dialog_is_read_by_a_name_of_its_own_a_renewal_reaching_no_profile_among_them()
        {
            // The headings were read as the notice of a renewal reaching no profile, which stood in
            // place of the line naming the table.
            foreach (var held in new[] { new[] { "reader" }, Array.Empty<string>() })
            {
                var request = new RenewalRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                    new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow.AddDays(40),
                    held, new[] { "gone-a", "gone-b" }, "127.0.0.1:60001", null);
                using (var dialog = new RenewalApprovalForm(request, TimeSpan.FromMinutes(2), null))
                {
                    Away(dialog);
                    var parts = Parts(dialog.Handle);

                    Assert.That(parts.Where(part => part.Name.Length == 0), Is.Empty, "parts read by no name");
                    Assert.That(parts.Where(part => part.Type == "ControlType.Pane" && part.Id.StartsWith("Kpg", StringComparison.Ordinal))
                        .Select(part => part.Id + ": " + part.Name), Is.EqualTo(new[]
                    {
                        "KpgRenewalLayout: Renewal request",
                        "KpgRenewalDetail: Details",
                        "KpgRenewalProfilesList: Profiles the client holds",
                        "KpgProfileHeadings: Headings",
                        "KpgProfileRows: Profiles the client holds"
                    }.Concat(held.Select(profile => "KpgProfileRow: " + profile)).Concat(new[]
                    {
                        "KpgProfileRow: gone-a. " + RenewalApprovalForm.OmitsTip,
                        "KpgProfileRow: gone-b. " + RenewalApprovalForm.OmitsTip,
                        "KpgApprovalLifetimeRow: Lifetime",
                        "KpgRenewalFooter: Answer",
                        "KpgRenewalButtonRow: Buttons"
                    })), "holding " + held.Length);
                    Assert.That(parts.Where(part => part.Type == "ControlType.Text" && part.Id.StartsWith("KpgRenewal", StringComparison.Ordinal) && part.Id.EndsWith("Line", StringComparison.Ordinal))
                        .Select(part => part.Name), Is.EqualTo(new[] { "Profiles the client holds:" }), "the line naming the table");
                    Assert.That(parts.Where(part => part.Id == "KpgRenewalNotice").Select(part => part.Name),
                        Is.EqualTo(held.Length == 0 ? new[] { RenewalApprovalForm.NoProfilesHeld } : Array.Empty<string>()));
                }
            }
        }

        /// <summary>Shows a dialog away from the screen, where no other window takes it.</summary>
        private static void Away(System.Windows.Forms.Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            Application.DoEvents();
        }

        [Test]
        public void A_row_whose_state_changed_is_read_with_the_change_and_no_other_row_is()
        {
            // A dialog shown again: `ops` and `deploy` new on the dialog before, as a reading of a
            // store that holds neither shows them, and the operator approved `ops` alone; now
            // `ops` is removed. Its row and its box are read with the change, as its tooltip
            // states it; `deploy`, unchecked by the operator and new as before, is read as its name
            // alone.
            var asked = new[] { "ops", "deploy" };
            using (var database = TemporaryDatabase.New())
            {
                var store = Listing(database);
                var before = Asked(store, asked);
                Assert.That(asked.Select(before.MarkOf), Is.EqualTo(new[] { ProfileMark.New, ProfileMark.New }));
                StoreSeeds.Name(store, "ops");
                StoreSeeds.Unname(store, "ops");

                using (var dialog = new PairingApprovalForm(
                           Shown(store, asked, PairingApproval.Approved(new[] { "ops" }), before), TimeSpan.FromMinutes(2)))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    Application.DoEvents();

                    var table = ProfileRows.Table(dialog, GateTab.ApprovalProfilesListName);
                    var read = Read(table.Handle);

                    Assert.That(read.Boxes, Is.EqualTo(new[]
                    {
                        Box + "ops. " + PairingApprovalForm.RemovedTip + " " + PairingApprovalForm.NowRestoresTip + " Off",
                        Box + "deploy. " + PairingApprovalForm.NewTip + " Off"
                    }));
                }
            }
        }

        [Test]
        public void A_renewal_s_row_with_an_X_is_read_as_its_name_and_the_sentence_of_the_X_once()
        {
            // A profile the database no longer names has an X in place of a box, drawn on the row,
            // which is nothing of its own to UI automation: the row is read by its name and the
            // sentence the X stands for, and that sentence by nothing else.
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow.AddDays(40),
                new[] { "reader" }, new[] { "retired" }, "127.0.0.1:60001", null);
            using (var dialog = new RenewalApprovalForm(request, TimeSpan.FromMinutes(2), null))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.ShowInTaskbar = false;
                dialog.Show();
                Application.DoEvents();

                var table = ProfileRows.Table(dialog, GateTab.RenewalProfilesListName).Handle;
                var read = Read(table);

                Assert.That(read.Name, Is.EqualTo("Profiles the client holds"), "the line above the table, without its colon");
                Assert.That(read.Texts, Is.EqualTo(new[] { "Grant", "Profile", "reader", "retired" }));
                Assert.That(read.Boxes, Is.EqualTo(new[] { Box + "reader On" }));
                Assert.That(read.Rows, Has.Count.EqualTo(2), "a row for each profile");
                Assert.That(read.Rows[1], Is.EqualTo("retired. " + RenewalApprovalForm.OmitsTip), "the row with no box is read by itself");

                var parts = Parts(table);
                Assert.That(parts.Count(part => part.Name.Contains(RenewalApprovalForm.OmitsTip)), Is.EqualTo(1), "the sentence read other than once");
                Assert.That(parts.Select(part => part.Type).Distinct().OrderBy(type => type, StringComparer.Ordinal),
                    Is.EqualTo(new[] { "ControlType.CheckBox", "ControlType.Pane", "ControlType.Text" }), "a part of the table other than a row, a box or a text");
            }
        }

        [Test]
        public void No_line_under_the_table_is_read_on_either_dialog_whether_the_table_scrolls_or_not()
        {
            // A line cut in half at the bottom of the table shows that the table scrolls, and no
            // line under the table states it in words: UI automation reads no such line on either
            // dialog, under a table that shows everything or a table that scrolls.
            var lines = new List<string>();
            using (var database = TemporaryDatabase.New())
            {
                var few = new[] { "ops", "reader" };
                var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "profile-" + at).ToArray();
                var store = Listing(database, many.Concat(few).ToArray());
                foreach (var asked in new[] { few, many })
                {
                    using (var dialog = new PairingApprovalForm(Shown(store, asked), TimeSpan.FromMinutes(2)))
                    {
                        lines.AddRange(LinesRead(dialog).Select(line => "pairing, " + asked.Length + " profiles: " + line));
                    }

                    var request = new RenewalRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                        new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow.AddDays(40),
                        asked, Array.Empty<string>(), "127.0.0.1:60001", null);
                    using (var dialog = new RenewalApprovalForm(request, TimeSpan.FromMinutes(2), null))
                    {
                        lines.AddRange(LinesRead(dialog).Select(line => "renewal, " + asked.Length + " profiles: " + line));
                    }
                }
            }

            Assert.That(lines.Where(line => line.Contains("scrolls") || line.Contains("Scrolling")), Is.Empty, "a line under a table read");
            Assert.That(lines, Is.Not.Empty, "nothing of the dialogs read");
        }

        /// <summary>
        /// Every part of a dialog UI automation reads by an identifier, the dialog shown where
        /// nobody sees it.
        /// </summary>
        private static IReadOnlyList<string> LinesRead(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            Application.DoEvents();
            return Read(dialog.Handle).Identified.ToList();
        }

        /// <summary>
        /// The store of this database, serving an authority and holding these names on its list,
        /// as the dialog reads a store.
        /// </summary>
        private static KdbxStore Listing(TemporaryDatabase database, params string[] listed)
        {
            var store = new KdbxStore(database.Database, GateVersion.Current, null);
            StoreSeeds.Authority(store);
            foreach (var name in listed)
            {
                StoreSeeds.Name(store, name);
            }

            return store;
        }

        /// <summary>What a dialog for these profiles reads of this store now.</summary>
        private static AskedProfiles Asked(KdbxStore store, string[] asked)
        {
            using (var reading = store.Read())
            {
                return AskedProfiles.Of(reading, asked, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// The request for these profiles as the handler makes it, reading this store, shown as
        /// the screen shows it as the dialog comes up.
        /// </summary>
        private static PairingRequest Shown(KdbxStore store, string[] asked, PairingApproval earlier = null, AskedProfiles before = null)
        {
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, "127.0.0.1:60001", null, earlier, before,
                null, () => Asked(store, asked));
            return request.Showing(request.Read());
        }

        /// <summary>How a box is read before its name: by the identifier a fixture finds it by.</summary>
        private const string Box = GateTab.ProfileBoxName + ": ";

        /// <summary>What automation reports of the table, read on a thread of its own.</summary>
        private static Reading Read(IntPtr table)
        {
            return OnAThreadOfItsOwn(() => Reading.Of(AutomationElement.FromHandle(table)));
        }

        /// <summary>
        /// Every part automation reports under a window, as its control type, its identifier, its
        /// name and whether it is off the screen, read on a thread of its own in a single search.
        /// </summary>
        private static IReadOnlyList<Part> Parts(IntPtr window)
        {
            return OnAThreadOfItsOwn(() =>
            {
                var cache = new CacheRequest();
                cache.Add(AutomationElement.NameProperty);
                cache.Add(AutomationElement.ControlTypeProperty);
                cache.Add(AutomationElement.AutomationIdProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                using (cache.Activate())
                {
                    return AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants, Condition.TrueCondition)
                        .Cast<AutomationElement>()
                        .Select(each => new Part(each.Cached.ControlType.ProgrammaticName, each.Cached.AutomationId, each.Cached.Name, each.Cached.IsOffscreen))
                        .ToList();
                }
            });
        }

        /// <summary>A part automation reports.</summary>
        private sealed class Part
        {
            public Part(string type, string id, string name, bool offscreen)
            {
                Type = type;
                Id = id ?? string.Empty;
                Name = name ?? string.Empty;
                Offscreen = offscreen;
            }

            public string Type { get; }

            public string Id { get; }

            public string Name { get; }

            public bool Offscreen { get; }

            public override string ToString()
            {
                return Type + " [" + Id + "] '" + Name + "'" + (Offscreen ? " offscreen" : string.Empty);
            }
        }

        /// <summary>How long a reading of automation may take before the test fails.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        /// <summary>
        /// What a reading of automation answers, taken on a thread of its own while this thread
        /// waits in <see cref="Thread.Join(TimeSpan)"/>.
        /// </summary>
        /// <remarks>
        /// **Waited for in a join**, which on this thread, a single threaded apartment, answers
        /// each message sent to the dialog's windows and each call into the dialog's objects the
        /// moment the message or the call arrives: every question automation puts to a part of the
        /// dialog is answered on this thread, and the table is a part for each box and each cell.
        /// **Not a loop of `DoEvents` and a sleep**, which answers a question only once the sleep
        /// is over, a tick of the system clock of 15.6 ms: the same reading of a table that scrolls
        /// measured 41 s in such a loop and 0.9 s in a join.
        /// </remarks>
        private static T OnAThreadOfItsOwn<T>(Func<T> reading)
            where T : class
        {
            T read = null;
            Exception failed = null;
            var reader = new Thread(() =>
            {
                try
                {
                    read = reading();
                }
                catch (Exception error) when (error is ElementNotAvailableException || error is InvalidOperationException)
                {
                    failed = error;
                }
            });
            reader.SetApartmentState(ApartmentState.MTA);
            var took = System.Diagnostics.Stopwatch.StartNew();
            reader.Start();
            reader.Join(Patience);

            TestContext.Out.WriteLine("automation answered in " + took.ElapsedMilliseconds + " ms");
            Assert.That(reader.IsAlive, Is.False, "automation did not answer");
            Assert.That(failed, Is.Null, failed == null ? null : failed.ToString());
            return read;
        }

        /// <summary>
        /// The table's name, every text in it in the order of the tree, and every check box in it,
        /// as its name and its check.
        /// </summary>
        private sealed class Reading
        {
            public string Name { get; private set; }

            public IReadOnlyList<string> Texts { get; private set; }

            public IReadOnlyList<string> Boxes { get; private set; }

            /// <summary>Every text, by the identifier a fixture finds it by and its name.</summary>
            public IReadOnlyList<string> Identified { get; private set; }

            /// <summary>Every row of a table, by its name.</summary>
            public IReadOnlyList<string> Rows { get; private set; }

            /// <summary>What automation reports of this table, read in a single search.</summary>
            public static Reading Of(AutomationElement table)
            {
                var cache = new CacheRequest();
                cache.Add(AutomationElement.NameProperty);
                cache.Add(AutomationElement.ControlTypeProperty);
                cache.Add(AutomationElement.AutomationIdProperty);
                cache.Add(TogglePattern.Pattern);
                cache.Add(TogglePattern.ToggleStateProperty);

                var texts = new List<string>();
                var identified = new List<string>();
                var boxes = new List<string>();
                var rows = new List<string>();
                string name;
                using (cache.Activate())
                {
                    var found = table.FindAll(
                        TreeScope.Descendants,
                        new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
                            new PropertyCondition(AutomationElement.AutomationIdProperty, GateTab.ProfileRowName)));
                    foreach (AutomationElement each in found)
                    {
                        if (each.Cached.ControlType == ControlType.Text)
                        {
                            texts.Add(each.Cached.Name ?? string.Empty);
                            identified.Add(each.Cached.AutomationId + ": " + each.Cached.Name);
                            continue;
                        }

                        if (each.Cached.ControlType != ControlType.CheckBox)
                        {
                            rows.Add(each.Cached.Name ?? string.Empty);
                            continue;
                        }

                        var state = (ToggleState)each.GetCachedPropertyValue(TogglePattern.ToggleStateProperty);
                        boxes.Add(each.Cached.AutomationId + ": " + each.Cached.Name + " " + state);
                    }

                    name = table.Current.Name;
                }

                return new Reading { Name = name, Texts = texts, Identified = identified, Boxes = boxes, Rows = rows };
            }
        }
    }
}
