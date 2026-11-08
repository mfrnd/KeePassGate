using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Host.Tests.Live;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Two real databases open at once, served by a real listener, and either of them closed or
    /// unlocked while a request to the other is still being answered.
    /// </summary>
    /// <remarks>
    /// The operator's ruling R1: a close or an unlock of a database ends that database's own
    /// connections and nobody else's. Closing every connection on every close and every unlock
    /// reset a request to a database that was open and unlocked all along, and a pairing waiting on
    /// that database's dialog with it.
    ///
    /// Everything here is the plugin's own except the handler, which holds the request until the
    /// test lets it go, and the events, raised in the order KeePass raises them once it has closed
    /// or opened the file.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class TwoDatabasesTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        private QueueLog _log;
        private TemporaryDatabase _first;
        private TemporaryDatabase _second;
        private List<PwDatabase> _open;
        private OpenDatabaseTargets _targets;
        private byte[] _firstKey;
        private byte[] _secondKey;
        private HoldingHandler _handler;
        private GateListener _listener;
        private Events _events;
        private LockWatch _watch;

        [SetUp]
        public void SetUp()
        {
            _log = new QueueLog();
            _first = Served();
            _second = Served();

            _open = new List<PwDatabase>();
            _targets = new OpenDatabaseTargets(delegate { return _open; }, _log);
            _firstKey = Open(_first);
            _secondKey = Open(_second);

            _handler = new HoldingHandler();
            _listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), _handler, _log);
            Assert.That(_listener.Start(), Is.True);

            _events = new Events();
            _watch = new LockWatch(_events, _targets, _listener, _log);
            Assert.That(_watch.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Release();
            _watch.Dispose();
            _listener.Dispose();
            _targets.Dispose();
            _first.Dispose();
            _second.Dispose();
        }

        [Test]
        public void Closing_the_other_database_leaves_a_request_to_this_one_answered()
        {
            var reply = Held(_firstKey);

            Close(_second, false);

            var answered = Answered(reply);
            Assert.That(answered.Status, Is.EqualTo(200), answered.Failure + "; " + _log);
        }

        [Test]
        public void Unlocking_the_other_database_leaves_a_request_to_this_one_answered()
        {
            // KeePass locks every database at once and unlocks the database in front, so the other
            // is unlocked later, with this database's clients already being served again.
            Close(_first, true);
            Close(_second, true);
            Reopen(_first);

            var reply = Held(_firstKey);

            Reopen(_second);

            var answered = Answered(reply);
            Assert.That(answered.Status, Is.EqualTo(200), answered.Failure + "; " + _log);
        }

        [Test]
        public void Closing_a_database_still_ends_a_request_to_it()
        {
            // The other half of the same rule: a socket thread must not go on reading a database
            // KeePass has emptied.
            var reply = Held(_secondKey);

            Close(_second, false);

            Assert.That(Answered(reply).Answered, Is.False, "the request to the closed database was answered; " + _log);
        }

        [Test]
        public void Closing_a_database_spares_a_renewal_waiting_on_its_operator_so_it_is_answered()
        {
            // The operator's ruling of 2026-11-08, "spare it and answer 503 locked". The close
            // takes the dialog away, and the connection waiting on it is left to answer for it, as
            // a lock leaves it. It was closed with the database's other connections, so the client
            // heard nothing. The handler here answers what the withdrawn dialog's renewal answers.
            var reply = Held(_secondKey, "POST", "/gate/renew", "{}");

            Close(_second, false);

            var answered = Answered(reply);
            Assert.That(answered.Answered, Is.True, "the renewal's connection was closed with the database; " + _log);
            Assert.That(answered.Status, Is.EqualTo(503), answered.Failure + "; " + _log);
            Assert.That(answered.Body, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
        }

        /// <summary>
        /// A request to the database with this gate key, held by the handler once it arrives.
        /// </summary>
        private Task<LiveReply> Held(byte[] gateKey)
        {
            return Held(gateKey, "GET", "/gate/status", null);
        }

        /// <summary>The same, asking for something else.</summary>
        private Task<LiveReply> Held(byte[] gateKey, string method, string path, string body)
        {
            var name = SniName.For(gateKey);
            var reply = Task.Run(() => LiveGateClient.Send(
                _listener.Port, name, null, method, path, body, LiveGateClient.OnTheOperator));
            Assert.That(_handler.Entered.Wait(Wait), Is.True, "the request never reached the handler");
            return reply;
        }

        private LiveReply Answered(Task<LiveReply> reply)
        {
            _handler.Release();
            Assert.That(reply.Wait(TimeSpan.FromSeconds(30)), Is.True, "the client never finished");
            return reply.Result;
        }

        private TemporaryDatabase Served()
        {
            var database = TemporaryDatabase.New();
            var created = new AuthorityService(new CustomDataStore(database.Database), _log).Create();
            Assert.That(created.Succeeded, Is.True, created.Message);
            database.Save();
            return database;
        }

        /// <summary>Opens a database as KeePass does, and answers its gate key.</summary>
        private byte[] Open(TemporaryDatabase database)
        {
            var before = Keys();
            _open.Add(database.Database);
            _targets.Unlock(database.Path);
            return Keys().Single(key => !before.Any(known => known.SequenceEqual(key)));
        }

        private List<byte[]> Keys()
        {
            var keys = new List<byte[]>();
            foreach (var target in _targets.Current())
            {
                using (target)
                {
                    keys.Add(target.GateKey);
                }
            }

            return keys;
        }

        /// <summary>
        /// What KeePass does closing or locking a database, then what it announces.
        /// </summary>
        private void Close(TemporaryDatabase database, bool locking)
        {
            _events.RaiseClosing();
            var path = database.Path;
            database.Database.Close();
            _open.Remove(database.Database);
            _events.RaiseClosed(path, locking);
        }

        /// <summary>What KeePass does unlocking a database, then what it announces.</summary>
        private void Reopen(TemporaryDatabase database)
        {
            database.Reopen();
            _open.Add(database.Database);
            _events.RaiseOpened(database.Path);
        }

        /// <summary>
        /// Answers every request, once the test lets it: a renewal as its dialog's close leaves it
        /// to, and anything else with a success.
        /// </summary>
        private sealed class HoldingHandler : IGateHandler
        {
            private readonly ManualResetEventSlim _released = new ManualResetEventSlim();

            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim();

            public void Release()
            {
                _released.Set();
            }

            public void Handle(IGateContext context)
            {
                Entered.Set();
                _released.Wait(TimeSpan.FromSeconds(60));
                if (string.Equals(context.Path, "/gate/renew", StringComparison.Ordinal))
                {
                    context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                    return;
                }

                context.Respond(200, "{}");
            }
        }

        /// <summary>Keeps what the plugin logged, for a failure to show.</summary>
        private sealed class QueueLog : IGateLog
        {
            private readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();

            public void Info(string message)
            {
                _lines.Enqueue("INFO " + message);
            }

            public void Warn(string message)
            {
                _lines.Enqueue("WARN " + message);
            }

            public void Error(string message, Exception error)
            {
                _lines.Enqueue("ERROR " + message + ": " + error);
            }

            public void Unexpected(string message, Exception error)
            {
                _lines.Enqueue("UNEXPECTED " + message + ": " + error);
            }

            public override string ToString()
            {
                return string.Join(" | ", _lines);
            }
        }

        private sealed class Events : IDatabaseEvents
        {
            public event EventHandler<GateDatabaseEventArgs> Opened;

            public event EventHandler Closing;

            public event EventHandler<GateDatabaseEventArgs> Closed;

            /// <summary>Never raised: nothing here is saved.</summary>
            public event EventHandler Saved
            {
                add { }
                remove { }
            }

            public void RaiseOpened(string path)
            {
                Opened?.Invoke(this, new GateDatabaseEventArgs(path, false));
            }

            public void RaiseClosing()
            {
                Closing?.Invoke(this, EventArgs.Empty);
            }

            public void RaiseClosed(string path, bool locking)
            {
                Closed?.Invoke(this, new GateDatabaseEventArgs(path, locking));
            }
        }
    }
}
