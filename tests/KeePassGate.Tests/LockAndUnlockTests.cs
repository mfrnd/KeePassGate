using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A locked database: what it still answers with, and what it can no longer do.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §308 as tests. The table there is the specification and the assertions
    /// below are one row each: the responder and the gate key stay, the authority key and the
    /// records go, and nothing may be written.
    /// </remarks>
    [TestFixture]
    public sealed class LockedTargetTests
    {
        private TestAuthority _authority;
        private LockedTarget _locked;
        private byte[] _gateKey;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _gateKey = _authority.GateKey;
            _locked = new LockedTarget(_authority.DatabaseId, _gateKey, _authority.Host);
        }

        [TearDown]
        public void TearDown()
        {
            _locked.Dispose();
            _authority.Dispose();
        }

        [Test]
        public void It_is_still_the_same_database_and_still_answers_to_its_name()
        {
            Assert.That(_locked.DatabaseId, Is.EqualTo(_authority.DatabaseId));
            Assert.That(SniName.Matches(SniName.For(_locked.GateKey), _gateKey), Is.True,
                "the gate resolves a name before any TLS work, so dropping the gate key would " +
                "leave the responder sitting unused");
        }

        [Test]
        public void It_has_something_to_complete_a_handshake_with()
        {
            Assert.That(_locked.Host, Is.Not.Null,
                "without this a client gets a bare connection failure and cannot tell it from an attack");
        }

        [Test]
        public void One_caller_giving_it_back_does_not_destroy_it_for_the_next()
        {
            // `Current` hands the same locked target to every connection. It used to be handed out
            // bare, with a one-way dispose behind it, so the first connection to finish zeroed the
            // gate key: every later connection matched its name against 32 zero bytes, resolved
            // nothing, and was dropped without a response. A second database made it worse, because
            // one request to the other database was enough to destroy this one before it answered.
            _locked.Retain();
            _locked.Dispose();

            Assert.That(SniName.Matches(SniName.For(_locked.GateKey), _gateKey), Is.True,
                "the target one caller gave back must still answer for the next");
        }

        [Test]
        public void The_gate_key_goes_when_the_last_caller_gives_it_back()
        {
            _locked.Retain();
            _locked.Dispose();
            _locked.Dispose();

            // The constructor keeps a copy, so the fixture's own array is not the one wiped. What
            // is observable is that the target stops answering to the name it was resolving by.
            Assert.That(SniName.Matches(SniName.For(_locked.GateKey), _gateKey), Is.False,
                "the key is what a locked database still holds, so the last release has to wipe it");
        }

        [Test]
        public void It_has_no_authority_at_all()
        {
            // The privilege drop, and the reason every caller is a stranger by construction rather
            // than by a check somebody has to remember to write.
            Assert.That(_locked.Authority, Is.Null);
        }

        [Test]
        public void Nobody_is_admitted_while_it_is_locked()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _authority.Admit(certificate, "ops");

                Assert.That(_locked.Admission(CertificateDigest.Of(certificate)), Is.Null);
            }
        }

        [Test]
        public void Nothing_may_be_written_to_it()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var write = _locked.Admit(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null));

                Assert.That(write.Ok, Is.False);
                Assert.That(write.IsBusy, Is.True, "unlocking is all it takes, so it is worth trying again");
                Assert.That(_locked.AdmissionBlocked, Is.Not.Null);
            }
        }

        [Test]
        public void It_says_it_is_locked()
        {
            Assert.That(_locked.IsLocked, Is.True);
        }

        [Test]
        public void A_client_certificate_that_would_be_admitted_is_not_even_looked_at()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _authority.Admit(certificate, "ops");

                var identity = ClientCredentials.For(certificate, _locked, null);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(identity.IsLocked, Is.True, "a locked database is not the same as an unknown client");
                Assert.That(identity.Profiles, Is.Empty);
            }
        }

        [Test]
        public void Disposing_it_takes_the_gate_key_with_it()
        {
            var before = _locked.GateKey;
            _locked.Dispose();

            Assert.That(_locked.GateKey, Is.Not.EqualTo(before));
        }

        [Test]
        public void It_is_never_built_without_the_three_things_it_needs()
        {
            Assert.Throws<ArgumentException>(() => new LockedTarget(null, _gateKey, _authority.Host));
            Assert.Throws<ArgumentNullException>(() => new LockedTarget("db", null, _authority.Host));
            Assert.Throws<ArgumentNullException>(() => new LockedTarget("db", _gateKey, null));
        }
    }

    /// <summary>
    /// The transitions themselves: what is dropped, what is kept, and what happens to the
    /// connections that were already being served.
    /// </summary>
    [TestFixture]
    public sealed class LockWatchTests
    {
        private FakeDatabaseEvents _events;
        private List<string> _order;
        private RecordingTargets _targets;
        private RecordingConnections _connections;
        private RecordingGateLog _log;
        private GateIncidents _incidents;
        private LockWatch _watch;

        [SetUp]
        public void SetUp()
        {
            _events = new FakeDatabaseEvents();
            _order = new List<string>();
            _targets = new RecordingTargets(_order);
            _connections = new RecordingConnections(_order);
            _log = new RecordingGateLog();
            _incidents = new GateIncidents(_log);
            _watch = new LockWatch(_events, _targets, () => _connections, _log, _incidents);

            Assert.That(_watch.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _watch.Dispose();
        }

        [Test]
        public void Locking_drops_what_is_privileged_and_closes_every_connection()
        {
            _events.RaiseClosed("C:\\invented\\one.kdbx", true);

            Assert.That(_targets.Locked, Is.EqualTo(new[] { "C:\\invented\\one.kdbx" }));
            Assert.That(_connections.Closed, Is.EqualTo(1));
        }

        [Test]
        public void The_material_goes_before_the_connections_do()
        {
            // The other way round, a thread waking from a closed socket could still find the
            // authority sitting there and use it on the way out.
            _events.RaiseClosed("C:\\invented\\one.kdbx", true);

            Assert.That(_order, Is.EqualTo(new[] { "lock", "close" }));
        }

        [Test]
        public void A_database_merely_being_opened_closes_nothing()
        {
            // KeePass announces an open and an unlock the same way. A database that was never
            // locked has no responder to promote a connection from, so there is nothing to protect
            // against and no reason to drop somebody's connection.
            //
            // Found by the end to end tier: a KeePass starting with a database on its command line
            // raised this while a client was already connecting, and killed its handshake.
            _targets.LiftsALock = false;

            _events.RaiseOpened("C:\\invented\\one.kdbx");

            Assert.That(_targets.Unlocked, Is.Not.Empty, "it is still served in full");
            Assert.That(_connections.Closed, Is.Zero);
        }

        [Test]
        public void And_the_same_at_an_unlock()
        {
            _targets.LiftsALock = true;

            _events.RaiseOpened("C:\\invented\\one.kdbx");

            Assert.That(_order, Is.EqualTo(new[] { "unlock", "close" }));
        }

        [Test]
        public void Unlocking_serves_it_again_and_closes_every_connection()
        {
            // The interesting half. A connection that shook hands with the responder was authenticated
            // against nothing, so it must not survive into the unlocked state.
            _targets.LiftsALock = true;

            _events.RaiseOpened("C:\\invented\\one.kdbx");

            Assert.That(_targets.Unlocked, Is.EqualTo(new[] { "C:\\invented\\one.kdbx" }));
            Assert.That(_connections.Closed, Is.EqualTo(1));
        }

        [Test]
        public void Closing_for_real_forgets_it_entirely()
        {
            _events.RaiseClosed("C:\\invented\\one.kdbx", false);

            Assert.That(_targets.Forgotten, Is.EqualTo(new[] { "C:\\invented\\one.kdbx" }));
            Assert.That(_targets.Locked, Is.Empty, "a closed database is not a locked one");
            Assert.That(_connections.Closed, Is.EqualTo(1));
        }

        [Test]
        public void A_database_about_to_close_is_read_again_and_nothing_else_happens()
        {
            // The last moment a database can be read, and the lock after it answers out of what
            // was read. A Save As, or an authority created or deleted on the tab, with no
            // connection since, was otherwise invisible to that lock.
            _events.RaiseClosing();

            Assert.That(_targets.Refreshed, Is.EqualTo(1));
            Assert.That(_targets.Locked, Is.Empty, "the close may yet be canceled, so nothing is dropped");
            Assert.That(_connections.Closed, Is.Zero, "and nobody's connection is closed for it");
        }

        [Test]
        public void The_read_before_a_lock_comes_before_the_lock()
        {
            _events.RaiseClosing();
            _events.RaiseClosed("C:\\invented\\one.kdbx", true);

            Assert.That(_order, Is.EqualTo(new[] { "refresh", "lock", "close" }));
        }

        [Test]
        public void A_read_before_a_close_that_throws_is_counted_the_same_way()
        {
            _targets.Throws = new InvalidOperationException("the targets fell over");

            Assert.DoesNotThrow(() => _events.RaiseClosing());
            Assert.That(_incidents.Count, Is.EqualTo(1));
            Assert.That(_log.Surprises, Is.Not.Empty);
        }

        [Test]
        public void Nothing_happens_after_the_watch_is_released()
        {
            _watch.Dispose();

            _events.RaiseClosing();
            _events.RaiseClosed("C:\\invented\\one.kdbx", true);
            _events.RaiseOpened("C:\\invented\\one.kdbx");

            Assert.That(_targets.Refreshed, Is.Zero);
            Assert.That(_targets.Locked, Is.Empty);
            Assert.That(_targets.Unlocked, Is.Empty);
        }

        [Test]
        public void A_target_that_throws_never_reaches_KeePass()
        {
            // This runs on KeePass's own thread, from KeePass's own event.
            _targets.Throws = new InvalidOperationException("the targets fell over");

            Assert.DoesNotThrow(() => _events.RaiseClosed("C:\\invented\\one.kdbx", true));
            Assert.That(_incidents.Count, Is.EqualTo(1),
                "one of the four catch-alls, so it is counted rather than only logged");
            Assert.That(_log.Surprises, Is.Not.Empty);
        }

        [Test]
        public void An_unlock_that_throws_is_counted_the_same_way()
        {
            // The other half of the same rule. Both handlers run on KeePass's thread, so both
            // catch everything, and a count nobody can see is not a report.
            _targets.Throws = new InvalidOperationException("the targets fell over");

            Assert.DoesNotThrow(() => _events.RaiseOpened("C:\\invented\\one.kdbx"));
            Assert.That(_incidents.Count, Is.EqualTo(1));
            Assert.That(_log.Surprises, Is.Not.Empty);
        }

        [Test]
        public void A_gate_that_cannot_close_its_connections_still_drops_the_material()
        {
            _connections.Throws = new System.IO.IOException("the socket is beyond help");

            Assert.DoesNotThrow(() => _events.RaiseClosed("C:\\invented\\one.kdbx", true));

            Assert.That(_targets.Locked, Is.Not.Empty, "the privileged half is what matters most");
            Assert.That(_log.Errors, Is.Not.Empty, "expected, so an error rather than an incident");
        }

        [Test]
        public void A_plugin_with_no_listener_still_locks()
        {
            using (var watch = new LockWatch(_events, _targets, (IConnections)null, _log))
            {
                watch.Start();
                _events.RaiseClosed("C:\\invented\\two.kdbx", true);

                Assert.That(_targets.Locked, Does.Contain("C:\\invented\\two.kdbx"));
            }
        }

        [Test]
        public void Starting_twice_watches_once()
        {
            Assert.That(_watch.Start(), Is.True);

            _events.RaiseClosed("C:\\invented\\one.kdbx", true);

            Assert.That(_targets.Locked.Count, Is.EqualTo(1));
        }

        [Test]
        public void It_is_never_built_without_what_it_drives()
        {
            Assert.Throws<ArgumentNullException>(() => new LockWatch(null, _targets, _connections, _log));
            Assert.Throws<ArgumentNullException>(() => new LockWatch(_events, null, _connections, _log));
        }

        private sealed class FakeDatabaseEvents : IDatabaseEvents
        {
            public event EventHandler<GateDatabaseEventArgs> Opened;

            public event EventHandler Closing;

            public event EventHandler<GateDatabaseEventArgs> Closed;

            /// <summary>Never raised: the lock watch has nothing to do with a save.</summary>
            public event EventHandler Saved
            {
                add { }
                remove { }
            }

            public void RaiseClosing()
            {
                var handler = Closing;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            public void RaiseOpened(string path)
            {
                var handler = Opened;
                if (handler != null)
                {
                    handler(this, new GateDatabaseEventArgs(path, false));
                }
            }

            public void RaiseClosed(string path, bool locking)
            {
                var handler = Closed;
                if (handler != null)
                {
                    handler(this, new GateDatabaseEventArgs(path, locking));
                }
            }
        }

        private sealed class RecordingTargets : ILockableTargets
        {
            private readonly List<string> _locked = new List<string>();
            private readonly List<string> _unlocked = new List<string>();
            private readonly List<string> _forgotten = new List<string>();
            private readonly List<string> _order;

            public RecordingTargets(List<string> order)
            {
                _order = order;
            }

            public IReadOnlyList<string> Locked
            {
                get { return _locked; }
            }

            public IReadOnlyList<string> Unlocked
            {
                get { return _unlocked; }
            }

            public IReadOnlyList<string> Forgotten
            {
                get { return _forgotten; }
            }

            public Exception Throws { get; set; }

            public bool Lock(string path)
            {
                _order.Add("lock");
                if (Throws != null)
                {
                    throw Throws;
                }

                _locked.Add(path);
                return true;
            }

            /// <summary>True when a lock was really lifted, which is what a test decides here.</summary>
            public bool LiftsALock { get; set; }

            public bool Unlock(string path)
            {
                _order.Add("unlock");
                if (Throws != null)
                {
                    throw Throws;
                }

                _unlocked.Add(path);
                return LiftsALock;
            }

            public bool Forget(string path)
            {
                _order.Add("forget");
                if (Throws != null)
                {
                    throw Throws;
                }

                _forgotten.Add(path);
                return true;
            }

            public int Refreshed { get; private set; }

            public void Refresh()
            {
                _order.Add("refresh");
                if (Throws != null)
                {
                    throw Throws;
                }

                Refreshed++;
            }
        }

        private sealed class RecordingConnections : IConnections
        {
            private readonly List<string> _order;

            public RecordingConnections(List<string> order)
            {
                _order = order;
            }

            public int Closed { get; private set; }

            public Exception Throws { get; set; }

            public int CloseAll(string why)
            {
                Closed++;
                _order.Add("close");

                if (Throws != null)
                {
                    throw Throws;
                }

                return 0;
            }
        }
    }
}
