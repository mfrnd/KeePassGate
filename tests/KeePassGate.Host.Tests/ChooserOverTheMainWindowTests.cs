using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority chooser on the screen, over a window standing in for KeePass's main window,
    /// while KeePass blocks and unblocks that window underneath it.
    /// </summary>
    /// <remarks>
    /// **What KeePass does under the chooser.** The chooser is not registered with
    /// `GlobalWindowManager`, so KeePass's periodic trigger goes on firing inside its modal loop,
    /// and a trigger that synchronizes or saves ends in `UIBlockInteraction(false)`, which assigns
    /// `Enabled = true` to the main window. The modal loop disabled that window natively and left
    /// the property alone, so the assignment enabled it, and the operator could work in it behind
    /// a question still standing. The two assignments are made here as KeePass makes them, from a
    /// timer inside the chooser's own loop, which is where KeePass's trigger runs.
    ///
    /// On a real shown window, because the property reads true throughout: only the window's own
    /// state, asked of Windows, sees anything.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class ChooserOverTheMainWindowTests
    {
        [Test]
        public void The_main_window_stays_disabled_under_the_chooser_when_KeePass_unblocks_it()
        {
            using (var database = TemporaryDatabase.New())
            using (var windows = new GlobalWindowManagerWindows())
            using (var dialogs = new GroupDialogs(windows, NullGateLog.Instance))
            using (var main = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000)
            })
            {
                var choice = Undecided(database);
                dialogs.Start();
                var writer = new DatabaseWriter(CurrentThread.Instance, saved => { }, null, dialogs, NullGateLog.Instance);

                using (var targets = new OpenDatabaseTargets(
                           delegate { return new List<PwDatabase> { database.Database }; }, writer, NullGateLog.Instance))
                using (var chooser = new AuthorityChooser(
                           new NoEvents(), targets, writer, main, null, null, null, NullGateLog.Instance))
                {
                    main.Show();
                    Application.DoEvents();

                    var disabledUnderIt = false;
                    var enabledAfterKeePass = true;
                    var seen = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate
                        {
                            var standing = Application.OpenForms.OfType<AuthorityChooserForm>().FirstOrDefault();
                            if (standing == null)
                            {
                                return;
                            }

                            timer.Stop();
                            seen = true;
                            disabledUnderIt = !TestNativeMethods.IsWindowEnabled(main.Handle);

                            // `UIBlockInteraction(true)` and then `UIBlockInteraction(false)`.
                            main.Enabled = false;
                            main.Enabled = true;
                            enabledAfterKeePass = TestNativeMethods.IsWindowEnabled(main.Handle);

                            standing.Close();
                        };

                        timer.Start();
                        chooser.AskWhichToKeep(choice, "Invented Vault");
                    }

                    Assert.That(seen, Is.True, "the chooser never came up, so nothing below is measured");
                    Assert.That(disabledUnderIt, Is.True, "the chooser's modal loop disabled the main window");
                    Assert.That(enabledAfterKeePass, Is.False,
                        "KeePass's unblock enabled the main window behind a question still standing");
                    Assert.That(TestNativeMethods.IsWindowEnabled(main.Handle), Is.True, "and the window is back once it closes");
                    Assert.That(main.Enabled, Is.True);
                }
            }
        }

        /// <summary>
        /// Two authorities side by side and neither retired, as copies that each recreated an
        /// authority leave a database once they are merged, and the question that puts.
        /// </summary>
        private static AuthorityChoice Undecided(TemporaryDatabase database)
        {
            var records = new KdbxStore(database.Database, GateVersion.Current, null);
            StoreSeeds.Undecided(records);

            using (var reading = new AuthorityService(records, NullGateLog.Instance).Read())
            {
                Assume.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                return AuthorityChoice.From(reading, candidate => StoreSeeds.Listed(records, candidate), null, null);
            }
        }

        private sealed class NoEvents : IDatabaseEvents
        {
            public event EventHandler<GateDatabaseEventArgs> Opened
            {
                add { }
                remove { }
            }

            public event EventHandler Closing
            {
                add { }
                remove { }
            }

            public event EventHandler<GateDatabaseEventArgs> Closed
            {
                add { }
                remove { }
            }

            public event EventHandler Saved
            {
                add { }
                remove { }
            }
        }
    }
}
