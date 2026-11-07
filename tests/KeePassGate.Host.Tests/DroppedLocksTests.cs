using System;
using System.Collections.Generic;
using KeePass.App.Configuration;
using KeePass.Util;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A lock KeePass was asked for during this plugin's own save, which KeePass drops, made once
    /// the request that saved has been answered.
    /// </summary>
    /// <remarks>
    /// Against a workspace this fixture answers for, because the lock itself is KeePass's and the
    /// end to end tier drives it whole: `LiveSessionLockTests`. In this tier because the reasons
    /// are KeePass's own type.
    /// </remarks>
    [TestFixture]
    public sealed class DroppedLocksTests
    {
        private FakeWorkspace _workspace;
        private bool _saving;
        private DroppedLocks _dropped;

        [SetUp]
        public void SetUp()
        {
            _workspace = new FakeWorkspace();
            _saving = false;
            _dropped = new DroppedLocks(() => _saving, _workspace, CurrentThread.Instance, null, NullGateLog.Instance);
        }

        [Test]
        public void A_lock_asked_for_during_the_plugins_save_is_made_once_the_request_is_answered()
        {
            _saving = true;
            _dropped.Asked(SessionLockReason.Lock);

            Assert.That(_dropped.Owed, Is.True);
            Assert.That(_workspace.Locks, Is.Zero, "nothing is locked while the save is still running");

            _saving = false;
            _dropped.Settle();

            Assert.That(_workspace.Locks, Is.EqualTo(1));
            Assert.That(_dropped.Owed, Is.False);

            _dropped.Settle();
            Assert.That(_workspace.Locks, Is.EqualTo(1), "and it is made once");
        }

        [Test]
        public void A_lock_asked_for_outside_the_plugins_save_is_left_to_KeePass()
        {
            // KeePass is not blocked by anything of this plugin's then, so it locks by itself.
            _dropped.Asked(SessionLockReason.Lock);
            _dropped.Settle();

            Assert.That(_dropped.Owed, Is.False);
            Assert.That(_workspace.Locks, Is.Zero);
        }

        [Test]
        public void A_lock_the_operator_did_not_ask_KeePass_for_is_not_made()
        {
            _workspace.Asked.Remove(SessionLockReason.Suspend);
            _saving = true;
            _dropped.Asked(SessionLockReason.Suspend);
            _saving = false;
            _dropped.Settle();

            Assert.That(_dropped.Owed, Is.False);
            Assert.That(_workspace.Locks, Is.Zero);
        }

        [Test]
        public void A_lock_owed_waits_while_KeePass_is_busy_again()
        {
            _saving = true;
            _dropped.Asked(SessionLockReason.UserSwitch);
            _saving = false;

            _workspace.Blocked = true;
            _dropped.Settle();

            Assert.That(_workspace.Locks, Is.Zero, "made now, KeePass would drop it again");
            Assert.That(_dropped.Owed, Is.True);

            _workspace.Blocked = false;
            _dropped.Settle();

            Assert.That(_workspace.Locks, Is.EqualTo(1));
        }

        [Test]
        public void With_no_database_open_nothing_is_locked_and_nothing_stays_owed()
        {
            _saving = true;
            _dropped.Asked(SessionLockReason.Lock);
            _saving = false;
            _workspace.AnyOpen = false;

            _dropped.Settle();

            Assert.That(_workspace.Locks, Is.Zero);
            Assert.That(_dropped.Owed, Is.False);
        }

        [Test]
        public void A_turn_that_does_not_come_leaves_the_lock_owed_for_the_next_request()
        {
            var dropped = new DroppedLocks(() => _saving, _workspace, new NeverRuns(), null, NullGateLog.Instance);
            _saving = true;
            dropped.Asked(SessionLockReason.Lock);
            _saving = false;

            dropped.Settle();

            Assert.That(_workspace.Locks, Is.Zero);
            Assert.That(dropped.Owed, Is.True);
        }

        [TestCase(SessionLockReason.Lock, "session switch")]
        [TestCase(SessionLockReason.Ending, "session switch")]
        [TestCase(SessionLockReason.UserSwitch, "session switch")]
        [TestCase(SessionLockReason.Suspend, "suspend")]
        [TestCase(SessionLockReason.RemoteControlChange, "remote control change")]
        public void Each_reason_asks_the_setting_KeePass_asks_for_it(SessionLockReason reason, string setting)
        {
            // Read off `MainForm.OnSessionLock` in KeePass 2.61.1, which `KeePassContractTests`
            // holds to the same three settings.
            var locking = new AceWorkspaceLocking();
            Assert.That(MainWindowLocking.Asks(locking, reason), Is.False, "every trigger is off by default");

            locking.LockOnSessionSwitch = setting == "session switch";
            locking.LockOnSuspend = setting == "suspend";
            locking.LockOnRemoteControlChange = setting == "remote control change";

            Assert.That(MainWindowLocking.Asks(locking, reason), Is.True);

            locking.LockOnSessionSwitch = setting != "session switch";
            locking.LockOnSuspend = setting != "suspend";
            locking.LockOnRemoteControlChange = setting != "remote control change";

            Assert.That(MainWindowLocking.Asks(locking, reason), Is.False, "and no other setting answers for it");
        }

        [Test]
        public void An_unknown_reason_asks_for_no_lock()
        {
            var locking = new AceWorkspaceLocking
            {
                LockOnSessionSwitch = true,
                LockOnSuspend = true,
                LockOnRemoteControlChange = true,
            };

            Assert.That(MainWindowLocking.Asks(locking, SessionLockReason.Unknown), Is.False);
        }

        private sealed class FakeWorkspace : IWorkspaceLocking
        {
            public HashSet<SessionLockReason> Asked { get; } = new HashSet<SessionLockReason>
            {
                SessionLockReason.Lock, SessionLockReason.Ending, SessionLockReason.UserSwitch,
                SessionLockReason.Suspend, SessionLockReason.RemoteControlChange,
            };

            public bool Blocked { get; set; }

            public bool AnyOpen { get; set; } = true;

            public int Locks { get; private set; }

            public bool Asks(SessionLockReason reason)
            {
                return Asked.Contains(reason);
            }

            public void LockAll()
            {
                Locks++;
            }
        }

        private sealed class NeverRuns : IUiThread
        {
            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                return false;
            }
        }
    }
}
