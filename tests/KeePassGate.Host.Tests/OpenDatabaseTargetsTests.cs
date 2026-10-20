using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The databases the gate will serve, taken from real KeePass databases rather than from a
    /// list a test wrote.
    /// </summary>
    /// <remarks>
    /// The test that matters most is the last one: a host certificate read out of a real database
    /// has to be able to complete a handshake. Everything else about the transport is worthless if
    /// the certificate that comes out of the store cannot serve, and that failure is invisible
    /// until something connects.
    /// </remarks>
    [TestFixture]
    public sealed class OpenDatabaseTargetsTests
    {
        private TemporaryDatabase _first;
        private TemporaryDatabase _second;
        private List<PwDatabase> _open;
        private RecordingLog _log;
        private OpenDatabaseTargets _targets;

        [SetUp]
        public void SetUp()
        {
            _first = TemporaryDatabase.New();
            _second = TemporaryDatabase.New();
            _open = new List<PwDatabase> { _first.Database, _second.Database };
            _log = new RecordingLog();
            _targets = new OpenDatabaseTargets(delegate { return _open; }, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _targets.Dispose();
            _first.Dispose();
            _second.Dispose();
        }

        [Test]
        public void A_database_with_no_authority_is_not_served()
        {
            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void A_database_with_an_authority_is()
        {
            CreateAuthorityIn(_first);

            var targets = _targets.Current();

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DatabaseId, Is.EqualTo(DigestOf(_first)));
        }

        [Test]
        public void Each_database_is_served_under_its_own_authority()
        {
            CreateAuthorityIn(_first);
            CreateAuthorityIn(_second);

            var targets = _targets.Current();

            Assert.That(targets.Select(t => t.DatabaseId).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void A_target_carries_the_gate_key_that_is_in_the_database()
        {
            CreateAuthorityIn(_first);

            var target = _targets.Current()[0];

            using (var reading = ServiceFor(_first).Read())
            {
                Assert.That(target.GateKey, Is.EqualTo(reading.Material.SniKey));
            }
        }

        [Test]
        public void The_gate_key_handed_out_is_a_copy_that_a_caller_may_wipe()
        {
            // The resolver clears every key it is given, so handing out the live one would empty
            // the database's own copy after a single connection.
            CreateAuthorityIn(_first);
            var target = _targets.Current()[0];

            var first = target.GateKey;
            Array.Clear(first, 0, first.Length);

            Assert.That(target.GateKey.Any(b => b != 0), Is.True);
        }

        [Test]
        public void The_same_database_gives_the_same_target_until_something_changes()
        {
            // Building a target parses a PKCS#12 and mints a responder, so doing it per connection
            // would be disk churn on every request.
            CreateAuthorityIn(_first);

            var once = _targets.Current()[0];
            var twice = _targets.Current()[0];

            Assert.That(twice, Is.SameAs(once));
        }

        [Test]
        public void Recreating_the_authority_gives_a_new_target()
        {
            CreateAuthorityIn(_first);
            var before = _targets.Current()[0];
            var beforeDigest = before.DatabaseId;

            ServiceFor(_first).Recreate();
            var after = _targets.Current()[0];

            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.DatabaseId, Is.Not.EqualTo(beforeDigest));
        }

        [Test]
        public void A_target_that_has_been_replaced_goes_when_its_last_holder_does()
        {
            // **This reverses what this test used to assert.** It pinned disposal at the moment
            // of replacement, under a comment calling reference counting too expensive for
            // something an operator does by pressing a button. The expense turned out to be
            // paid by the client rather than saved: a connection holding the old target had the
            // certificate it was validating against reset underneath it, and was refused with a
            // 401 it had done nothing to earn. The material still goes promptly; it goes when
            // the last holder lets go rather than when the next connection asks.
            CreateAuthorityIn(_first);
            var held = _targets.Current()[0];

            ServiceFor(_first).Recreate();
            foreach (var replacement in _targets.Current())
            {
                replacement.Dispose();
            }

            Assert.That(held.Authority.RawData, Is.Not.Empty,
                "the connection that took this one can still finish with it");

            held.Dispose();

            Assert.Throws<System.Security.Cryptography.CryptographicException>(
                delegate { var unused = held.Authority.RawData; },
                "and the material goes as soon as it is given back");
        }

        [Test]
        public void Deleting_the_authority_stops_the_database_being_served()
        {
            CreateAuthorityIn(_first);
            Assume.That(_targets.Current(), Is.Not.Empty);

            ServiceFor(_first).Delete();

            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void A_database_that_is_no_longer_open_stops_being_served()
        {
            CreateAuthorityIn(_first);
            CreateAuthorityIn(_second);
            Assume.That(_targets.Current().Count, Is.EqualTo(2));

            _open.Remove(_second.Database);

            var targets = _targets.Current();
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DatabaseId, Is.EqualTo(DigestOf(_first)));
        }

        [Test]
        public void A_damaged_authority_is_not_served_and_does_not_stop_the_others()
        {
            CreateAuthorityIn(_first);
            _second.Database.CustomData.Set(AuthorityKeys.Authority, "rubbish");

            var targets = _targets.Current();

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DatabaseId, Is.EqualTo(DigestOf(_first)));
        }

        [Test]
        public void A_source_that_throws_is_reported_and_serves_nothing()
        {
            var targets = new OpenDatabaseTargets(
                delegate { throw new InvalidOperationException("the open databases could not be listed"); }, _log);

            Assert.That(targets.Current(), Is.Empty);
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void A_host_certificate_out_of_a_real_database_can_complete_a_handshake()
        {
            CreateAuthorityIn(_first);

            var target = _targets.Current()[0];

            Assert.That(Handshake(target.Host), Is.EqualTo("ok"));
        }

        [Test]
        public void Serving_a_real_database_writes_no_key_anywhere()
        {
            // The claim the whole design rests on, checked at the end of the chain rather than at
            // the start: a key that came out of a KDBX file and into a handshake, and no file.
            CreateAuthorityIn(_first);
            var host = _targets.Current()[0].Host;

            var before = KeyFiles();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(Handshake(host), Is.EqualTo("ok"));
            clock.Stop();

            var appeared = Appeared(before);
            if (appeared.Length == 0)
            {
                return;
            }

            // Something arrived, and on a quiet machine that is this plugin writing a key. But
            // both directories belong to the whole logon session, so anything else running can put
            // a file in one inside the same window and read exactly like a regression here. That
            // is not hypothetical: this failed once against the other test assembly, run beside
            // this one by `dotnet test` on the solution, and passed alone immediately after.
            //
            // So the machine is asked directly, with a window of the same shape and no handshake
            // in it. Keys appearing in that one too means this run cannot tell the two apart, and
            // saying so is worth more than accusing the code or quietly passing.
            var idle = KeyFiles();
            Thread.Sleep(clock.Elapsed < MinimumControl ? MinimumControl : clock.Elapsed);

            if (Appeared(idle).Length > 0)
            {
                Assert.Inconclusive(
                    "something else on this machine is writing private keys, so this run cannot" +
                    " tell whether the gate wrote one");
            }

            Assert.Fail("a private key reached the disk: " + string.Join(", ", appeared));
        }

        /// <summary>
        /// How long to watch an idle machine before believing it wrote nothing, when the handshake
        /// itself was quicker than that.
        /// </summary>
        private static readonly TimeSpan MinimumControl = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// The names of every private key file this account holds, in both places Windows keeps
        /// them.
        /// </summary>
        /// <remarks>
        /// **Names rather than a count, and both directories**, for the reason `PrivateKeyFiles`
        /// in the unit tier gives: these are shared by the whole logon session, so a count changes
        /// when anything else on the machine touches a key, and `Crypto\Keys` is CNG only. Written
        /// twice because that assembly cannot reference KeePass and this one must.
        /// </remarks>
        private static HashSet<string> KeyFiles()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in new[] { @"Microsoft\Crypto\Keys", @"Microsoft\Crypto\RSA" })
            {
                var path = System.IO.Path.Combine(appData, directory);
                if (!System.IO.Directory.Exists(path))
                {
                    continue;
                }

                foreach (var file in System.IO.Directory.GetFiles(
                    path, "*", System.IO.SearchOption.AllDirectories))
                {
                    names.Add(file);
                }
            }

            return names;
        }

        /// <summary>What appeared, which is the only direction that means anything.</summary>
        private static string[] Appeared(HashSet<string> before)
        {
            var after = KeyFiles();
            after.ExceptWith(before);
            return after.ToArray();
        }

        private static string Handshake(HostKeyMaterial host)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = "not reached";

            var server = new Thread(delegate()
            {
                try
                {
                    using (var accepted = listener.AcceptTcpClient())
                    {
                        var protocol = new Org.BouncyCastle.Tls.TlsServerProtocol(accepted.GetStream());
                        protocol.Accept(new GateTlsServer(
                            new Org.BouncyCastle.Tls.Crypto.Impl.BC.BcTlsCrypto(
                                new Org.BouncyCastle.Security.SecureRandom()),
                            host));
                        result = "ok";
                        protocol.Close();
                    }
                }
                catch (Exception error)
                {
                    result = error.GetType().Name + ": " + error.Message;
                }
            });

            server.IsBackground = true;
            server.Start();

            try
            {
                using (var client = new TcpClient("127.0.0.1", port))
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, SslProtocols.Tls13, false);
                }
            }
            catch (Exception)
            {
            }

            server.Join(10000);
            listener.Stop();
            return result;
        }

        private static void CreateAuthorityIn(TemporaryDatabase database)
        {
            var created = ServiceFor(database).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        private static AuthorityService ServiceFor(TemporaryDatabase database)
        {
            return new AuthorityService(new CustomDataStore(database.Database), NullGateLog.Instance);
        }

        private static string DigestOf(TemporaryDatabase database)
        {
            using (var reading = ServiceFor(database).Read())
            {
                return reading.Material.AuthorityDigest;
            }
        }

        private sealed class RecordingLog : IGateLog
        {
            public List<string> Errors { get; } = new List<string>();

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
            }

            public void Error(string message, Exception error)
            {
                Errors.Add(message);
            }

            public void Unexpected(string message, Exception error)
            {
                // Its own level, because these are the ones that mean there is a defect.
                Error(message, error);
            }
        }
    }
}
