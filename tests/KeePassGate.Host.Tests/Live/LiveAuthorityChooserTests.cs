using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Spec;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The authority chooser, raised by a real synchronize in a real KeePass, and answered through
    /// its own buttons.
    /// </summary>
    /// <remarks>
    /// **What only this proves** is the claim the chooser's timing rests on: that KeePass announces
    /// a synchronize by nothing but the save it ends with. `ImportUtil.Synchronize` merges the other
    /// file in and then saves through the main window, which raises `FileSaved`, and that is the
    /// event the plugin asks on. Everything else about the chooser is tested without a desktop;
    /// that sentence was read off KeePass 2.61.1, and this is where it is run.
    ///
    /// **Two copies of one file**, each of which replaces the authority they share and admits a
    /// client of its own, which is what two machines holding one database leave behind when their
    /// operators press Recreate apart. Both are written before KeePass starts, because a running
    /// KeePass overwrites anything written to the file underneath it. The copy KeePass opens is
    /// served; the other is merged in from the menu an operator uses, File, Synchronize,
    /// Synchronize with File.
    ///
    /// **The menu rather than a trigger.** KeePass's own "Synchronize active database with a
    /// file/URL" action reaches the same `ImportUtil.Synchronize` and the same save, and was not
    /// used: it needs a configuration written for it, and KeePass 2.61.1 has no trigger event a
    /// test can raise at a moment of its choosing short of a custom toolbar button, which would
    /// then have to be clicked. The menu needs neither and is what an operator presses.
    ///
    /// **The gate keys are read while there is one authority to read each from.** Once the
    /// synchronize has saved, the file holds two, and a reading of it has none to answer with.
    ///
    /// **The choice is saved by the test, not by the choice.** The chooser marks the database
    /// changed and leaves saving to the operator, so the file is read once with the choice made and
    /// again after the save that ends the session: a KeePass closed with a change unsaved would stop
    /// to ask about it.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveAuthorityChooserTests
    {
        /// <summary>The second copy, beside the one KeePass opens.</summary>
        private const string OtherCopy = "other-copy.kdbx";

        /// <summary>What each copy's client is admitted for. The status route asks for no grant.</summary>
        private const string Profile = "ops";

        /// <summary>
        /// What KeePass 2.61.1 puts between the file's name and its own in the main window's title
        /// while the database holds changes nobody has saved, `throwaway.kdbx* - KeePass`, where a
        /// saved one reads `throwaway.kdbx - KeePass`.
        /// </summary>
        private const string UnsavedMark = "* - ";

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private string _ours;

        private LiveReply _beforeSynchronizing;
        private bool _chooserAppeared;
        private LiveReply _oursWhileChoosing;
        private LiveReply _theirsWhileChoosing;
        private string _described;
        private LiveReply _oursAfterKeeping;
        private LiveReply _theirsOnTheirOwnName;
        private LiveReply _theirsOnOurName;
        private string _titleAfterKeeping;
        private AuthorityPresence _fileAfterKeeping;
        private string _titleAfterSaving;
        private string _keptInTheFile;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            // Taken before either copy changes anything, so both start from the same authority.
            var other = keePass.CopyDatabase(OtherCopy);

            _ours = keePass.RecreateAuthority(keePass.DatabasePath);
            var theirs = keePass.RecreateAuthority(other);
            TestContext.Out.WriteLine("this copy's authority: " + _ours + ", the other copy's: " + theirs);

            var ourGate = GateKeyOf(keePass, keePass.DatabasePath);
            var theirGate = GateKeyOf(keePass, other);

            using (var ourClient = keePass.IssueClientCertificate(keePass.DatabasePath, "agent-this-copy"))
            using (var theirClient = keePass.IssueClientCertificate(other, "agent-other-copy"))
            {
                keePass.Admit(keePass.DatabasePath, ourClient, Profile);
                keePass.Admit(other, theirClient, Profile);

                var driver = KeePassDriver.Start(keePass);
                WaitForLog(keePass, driver, "serving a database with authority " + CertificateDigest.Shorten(_ours),
                    "The plugin never reported serving the copy KeePass was started with.");

                _beforeSynchronizing = Ask(keePass, ourGate, ourClient);

                driver.Synchronize(other);

                var chooser = driver.WaitForWindowById(GateTab.ChooserFormName, DialogTimeout);
                _chooserAppeared = chooser != null;
                Assert.That(chooser, Is.Not.Null,
                    "No chooser appeared after the synchronize. Windows: " + NativeWindows.Titles(keePass.Process) +
                    Environment.NewLine + driver.Diagnostics + Environment.NewLine + keePass.ReadPluginLog());

                // Both while it is open and before anything is chosen: the database answers for
                // each authority it holds, and serves neither.
                _oursWhileChoosing = Ask(keePass, ourGate, ourClient);
                _theirsWhileChoosing = Ask(keePass, theirGate, theirClient);

                _described = driver.SelectAuthority(chooser, CertificateDigest.Shorten(_ours));
                Assert.That(driver.PressButton(chooser, GateTab.KeepButtonText), Is.True, driver.Diagnostics);
                Assert.That(driver.WaitForWindowToClose(GateTab.ChooserFormName, DialogTimeout), Is.True,
                    "The chooser was still open after Keep. " + driver.Diagnostics);

                WaitForLog(keePass, driver, "authority " + CertificateDigest.Shorten(_ours) + " kept",
                    "The plugin never reported keeping the authority that was chosen.");

                _oursAfterKeeping = Ask(keePass, ourGate, ourClient);
                _theirsOnTheirOwnName = Ask(keePass, theirGate, theirClient);
                _theirsOnOurName = Ask(keePass, ourGate, theirClient);

                _titleAfterKeeping = WaitForTitle(driver, true);

                // While KeePass still runs, and with nothing of its own being written: what the
                // choice left in the file, before anybody saved.
                using (var reading = keePass.ReadAuthority())
                {
                    _fileAfterKeeping = reading.Presence;
                }

                driver.Save();
                _titleAfterSaving = WaitForTitle(driver, false);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            using (var reading = keePass.ReadAuthority())
            {
                _keptInTheFile = reading.IsPresent ? reading.Material.AuthorityDigest : reading.Presence.ToString();
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void The_copy_KeePass_opened_was_served_before_the_synchronize()
        {
            Assert.That(_beforeSynchronizing.Status, Is.EqualTo(200), Said(_beforeSynchronizing));
            Assert.That(_beforeSynchronizing.Body, Does.Contain(CertificateDigest.Shorten(_ours)));
        }

        [Test]
        public void The_synchronize_raised_the_chooser()
        {
            Assert.That(_chooserAppeared, Is.True);
        }

        [Test]
        public void While_it_was_open_the_client_of_this_copy_was_told_the_database_is_undecided()
        {
            Assert.That(_oursWhileChoosing.Status, Is.EqualTo(503), Said(_oursWhileChoosing));
            Assert.That(_oursWhileChoosing.Body, Does.Contain(UndecidedTarget.Error));
        }

        [Test]
        public void And_so_was_the_client_of_the_other_copy()
        {
            // Under the other authority's own gate key: each candidate answers, so the clients of
            // either reach an answer rather than a connection that fails for no reason they can see.
            Assert.That(_theirsWhileChoosing.Status, Is.EqualTo(503), Said(_theirsWhileChoosing));
            Assert.That(_theirsWhileChoosing.Body, Does.Contain(UndecidedTarget.Error));
        }

        [Test]
        public void The_dialog_described_the_authority_that_was_kept()
        {
            Assert.That(_described, Is.EqualTo(_ours),
                "Keep keeps what the box below the list describes, so this is the one it kept");
        }

        [Test]
        public void Keeping_this_copy_s_authority_serves_it_at_once()
        {
            Assert.That(_oursAfterKeeping.Status, Is.EqualTo(200), Said(_oursAfterKeeping));
            Assert.That(_oursAfterKeeping.Body, Does.Contain(CertificateDigest.Shorten(_ours)));
        }

        [Test]
        public void The_other_copy_s_client_reaches_nothing_on_its_own_name()
        {
            // The name it was given when it paired, under an authority that is now retired: nothing
            // answers for it, exactly as for a name no database was ever served under.
            Assert.That(_theirsOnTheirOwnName.Answered, Is.False, Said(_theirsOnTheirOwnName));
        }

        [Test]
        public void Nor_does_the_kept_authority_admit_it()
        {
            Assert.That(_theirsOnOurName.Status, Is.EqualTo(401), Said(_theirsOnOurName));
        }

        [Test]
        public void The_choice_marked_the_database_changed()
        {
            Assert.That(_titleAfterKeeping, Does.Contain(UnsavedMark),
                "KeePass's title bar was '" + _titleAfterKeeping + "'");
        }

        [Test]
        public void And_did_not_save_it()
        {
            Assert.That(_fileAfterKeeping, Is.EqualTo(AuthorityPresence.Undecided),
                "the file on disk still holds both authorities until the operator saves");
        }

        [Test]
        public void The_operator_s_save_writes_the_choice()
        {
            Assert.That(_keptInTheFile, Is.EqualTo(_ours),
                "after the save KeePass's title bar was '" + _titleAfterSaving + "'");
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
            Assert.That(_pluginLog, Does.Contain("stopped listening"));
        }

        [Test]
        public void Nothing_was_reported_but_the_undecided_database_and_the_refused_certificate()
        {
            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("until the operator keeps one"))
                .Where(line => !line.Contains("refused a certificate that does not chain to"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        /// <summary>One copy's gate key, read while it holds one authority.</summary>
        private static byte[] GateKeyOf(ThrowawayKeePass keePass, string path)
        {
            using (var reading = keePass.ReadAuthority(path))
            {
                return reading.Material.SniKey;
            }
        }

        /// <summary>
        /// The status route, on a name built from a gate key, with a certificate: what a client of
        /// that authority asks first.
        /// </summary>
        private static LiveReply Ask(ThrowawayKeePass keePass, byte[] gateKey, X509Certificate2 certificate)
        {
            return LiveGateClient.Get(keePass.Port, SniName.For(gateKey), certificate, GateOperations.StatusPath);
        }

        private static string Said(LiveReply reply)
        {
            return reply.Failure ?? reply.Body;
        }

        /// <summary>
        /// KeePass's title bar once it says what is expected of it, or as it last read.
        /// </summary>
        /// <remarks>
        /// Waited for rather than read once, because KeePass redraws its title on its own thread
        /// after the change that alters it, and a save runs there too.
        /// </remarks>
        private static string WaitForTitle(KeePassDriver driver, bool unsaved)
        {
            var deadline = DateTime.UtcNow + DialogTimeout;
            var title = driver.MainWindowTitle;

            while (title.Contains(UnsavedMark) != unsaved && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
                title = driver.MainWindowTitle;
            }

            return title;
        }

        private static void WaitForLog(
            ThrowawayKeePass keePass, KeePassDriver driver, string marker, string failure)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains(marker))
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail(failure + " " + driver.Diagnostics + Environment.NewLine +
                        "Log so far:" + Environment.NewLine + keePass.ReadPluginLog());
        }
    }
}
