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

using KeePassGate.Testing;

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
            _second.Database.CustomData.Set(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");

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
        public void Locking_a_database_nobody_served_names_its_file_and_not_where_it_is()
        {
            // The log is a file an operator may send with a support request, and a full path says
            // where somebody keeps their passwords.
            Assert.That(_targets.Lock(_first.Path), Is.False);

            var said = string.Join("\n", _log.Infos);
            Assert.That(said, Does.Contain(System.IO.Path.GetFileName(_first.Path)));
            Assert.That(said, Does.Not.Contain(System.IO.Path.GetDirectoryName(_first.Path)));
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
            // Locked against every other test process this account runs, so that a key one of
            // them writes never lands inside this window: `PrivateKeyFiles.Lock` says why.
            using (PrivateKeyFiles.Lock())
            {
                Serving_a_real_database_writes_no_key_anywhere_while_locked();
            }
        }

        private void Serving_a_real_database_writes_no_key_anywhere_while_locked()
        {
            // The claim the whole design rests on, checked at the end of the chain rather than at
            // the start: a key that came out of a KDBX file and into a handshake, and no file.
            //
            // **A guard rather than a test with a live mutant, and the difference matters to
            // whoever reads a green run here.** The serving path is BouncyCastle, which parses the
            // stored PKCS#12 itself and never asks CryptoAPI for anything, so no key file can
            // appear on it however the loading flags are set. Taking `EphemeralKeySet` off
            // `CertificateBlob` fails `CertificateBlobTests` in the unit tier and leaves this one
            // green, because that flag governs a path this one does not use. What this catches is
            // a return to `SslStream` and SChannel for the server, which would put the host key in
            // the user profile for as long as a database was open, and which `DESIGN.md` 133 says
            // was measured and rejected. It is worth its three handshakes for that alone.
            //
            // **Asked more than once, because the directories are not ours.** Both belong to the
            // whole logon session, so anything else running can put a file in one inside the
            // window and read exactly like a regression here. That is not hypothetical: it failed
            // against the other test assembly, run beside this one by `dotnet test` on the
            // solution, and passed alone immediately after.
            //
            // What tells the two apart is repetition rather than a quiet moment afterwards. A gate
            // that writes a key writes one every time it serves, so a single clean attempt is
            // enough to acquit it; something else on the machine has to interfere with every
            // attempt in a row to convict it. The earlier version watched an idle window after the
            // fact instead, which only ever caught interference that was still going on, and it
            // failed rather than saying it could not tell.
            //
            // Each attempt mints its own authority, so none of them is a second handshake with a
            // key the first one already put somewhere.
            var appeared = new List<string>();

            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                CreateAuthorityIn(_first);
                var host = _targets.Current()[0].Host;

                var before = PrivateKeyFiles.Names();
                Assert.That(Handshake(host), Is.EqualTo("ok"));

                var written = PrivateKeyFiles.Appeared(before);
                if (written.Length == 0)
                {
                    return;
                }

                appeared.AddRange(written);
            }

            // Every attempt saw one, which is either the gate or a machine writing keys throughout.
            // One last look at an idle window separates those, and saying the run cannot tell is
            // worth more than accusing the code or quietly passing.
            var idle = PrivateKeyFiles.Names();
            Thread.Sleep(IdleWindow);

            if (PrivateKeyFiles.Appeared(idle).Length > 0)
            {
                Assert.Inconclusive(
                    "something else on this machine is writing private keys throughout, so this" +
                    " run cannot tell whether the gate wrote one");
            }

            Assert.Fail("a private key reached the disk on every attempt: " +
                        string.Join(", ", appeared));
        }

        /// <summary>
        /// How many handshakes have to write a key before the gate is the one being accused.
        /// </summary>
        /// <remarks>
        /// Three. A regression writes one every time, so this costs three handshakes in the failing
        /// case and one in the passing case, which is the run everybody has.
        /// </remarks>
        private const int Attempts = 3;

        /// <summary>How long to watch an idle machine before believing it wrote nothing.</summary>
        private static readonly TimeSpan IdleWindow = TimeSpan.FromMilliseconds(250);

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

            public List<string> Infos { get; } = new List<string>();

            public void Info(string message)
            {
                Infos.Add(message);
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
