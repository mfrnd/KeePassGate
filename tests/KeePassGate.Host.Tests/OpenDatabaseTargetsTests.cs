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
using KeePassGate.Store;
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
        public void A_connection_naming_the_gate_of_an_authority_that_no_longer_stands_finds_no_target()
        {
            // While several authorities stand, each of them is answered under its own gate, and the
            // gate of an authority retired beside them names nothing: the handshake is refused, as
            // it is once the operator has kept an authority.
            CreateAuthorityIn(_first);
            var gone = _targets.Current().Single();
            Assume.That(Served(gone), Is.EqualTo("ok"));

            new StoredAdmissions(_first.Database).RetiredBesideOthers();
            var standing = _targets.Current();
            Assume.That(standing, Has.Count.EqualTo(2), "a candidate for each authority standing");

            NoTargetFor(gone, "while several stand");
            Assert.That(Served(standing[0]), Is.EqualTo("ok"), "and the gate of a candidate is answered");

            var keep = standing[0].DatabaseId;
            var offered = standing.Select(candidate => candidate.DatabaseId).ToArray();
            var service = new AuthorityService(new KdbxStore(_first.Database, GateVersion.Current, null), NullGateLog.Instance);
            Assume.That(service.Keep(keep, offered).Succeeded, Is.True);
            Assume.That(_targets.Current().Select(target => target.DatabaseId), Is.EqualTo(new[] { keep }), "the authority kept is served");

            NoTargetFor(gone, "once the operator kept another");
        }

        /// <summary>
        /// Asserts that a handshake naming the gate of this target is refused for want of a target,
        /// by the gate's own line, and that no handshake completed over any certificate.
        /// </summary>
        private void NoTargetFor(IGateTarget target, string when)
        {
            var log = new RecordingLog();
            var answer = Served(target, log);

            Assert.That(answer, Does.Not.StartWith("ok").And.Not.StartWith("the gate served"), when + ": no handshake completes");
            Assert.That(
                log.Infos, Has.Some.StartWith("dropped a connection: " + GateOutcome.NoMatch.ToString().ToLowerInvariant()),
                when + ": for want of a target");
        }

        [Test]
        public void A_database_whose_store_will_not_read_leaves_every_other_database_served()
        {
            // What reading a database's store raises is named, and that database is left unserved:
            // a throw out of one database left `Current` for every open database at once.
            CreateAuthorityIn(_first);
            CreateAuthorityIn(_second);

            using (var targets = new OpenDatabaseTargets(delegate { return _open; }, null, new FirstAskThrows(), _log))
            {
                var current = targets.Current();
                Assert.That(current, Has.Count.EqualTo(1));
                using (var served = current[0])
                {
                    Assert.That(served.DatabaseId, Is.EqualTo(DigestOf(_second)));
                }
            }

            Assert.That(_log.Errors, Has.Some.Contains("could not be read, so it is not served"));
        }

        [Test]
        public void The_same_authorities_in_new_objects_are_not_asked_about_again_and_another_set_is()
        {
            // KeePass's Synchronize hands the database the very text of the store in objects of
            // its own, as each periodic synchronize of a trigger does under the chooser, and the
            // operator who answered or canceled is not asked again for any synchronize of the
            // same authorities.
            var store = new KdbxStore(_first.Database, GateVersion.Current, NullGateLog.Instance);
            StoreSeeds.Undecided(store);

            Assert.That(_targets.Unasked(), Is.EqualTo(new[] { _first.Database }));

            foreach (var entry in _first.Database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority"))
            {
                foreach (var name in entry.Strings.GetKeys().Where(name => name.StartsWith("KPG.", StringComparison.Ordinal)).ToList())
                {
                    var value = entry.Strings.Get(name);
                    entry.Strings.Set(name, new KeePassLib.Security.ProtectedString(value.IsProtected, value.ReadString()));
                }
            }

            Assert.That(_targets.Unasked(), Is.Empty, "the same authorities, asked about already");
            Assert.That(_log.Warnings.Count(line => line.Contains("until the operator keeps an authority")), Is.EqualTo(1),
                "and the log names them once");

            StoreSeeds.Beside(store);

            Assert.That(_targets.Unasked(), Is.EqualTo(new[] { _first.Database }), "another set is asked about");
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
        public void The_names_a_target_answers_are_the_store_s_read_at_every_hold()
        {
            // `STORE.md` §616: a name is on the list by its records, read from the store as it
            // stands, so a name added after the gate started serving is read with no reload.
            CreateAuthorityIn(_first);
            var target = _targets.Current()[0];
            Assert.That(target.Asked(new[] { "ops" }).Listed, Is.Empty);

            StoreSeeds.Name(new KdbxStore(_first.Database, GateVersion.Current, null), "ops");

            Assert.That(target.Asked(new[] { "ops" }).Listed, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_pairing_s_dialog_is_read_in_a_single_hold_with_the_clients_and_the_removals_of_each_name_off_the_list()
        {
            // `PAIRING.md` §311: the names on the list, the clients that regain a name off it and
            // the removals of such a name, from a single reading. The removals are those the
            // reading tells of the name itself, so a removal of a name the file does not know,
            // which only a deletion record tells of, is not held: the write finds it.
            CreateAuthorityIn(_first);
            var store = new KdbxStore(_first.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Name(store, "gone");
            var hold = new CountedHolds();
            using (var targets = new OpenDatabaseTargets(delegate { return _open; }, null, hold, _log))
            using (var target = targets.Current().Single())
            using (var client = new AuthorityIssuer().IssueClient(target.AuthorityKey, "agent-fictitious", new[] { "gone" }).Read())
            {
                var admissions = new StoredAdmissions(_first.Database);
                admissions.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "gone" }, target.DatabaseId));
                StoreSeeds.Unname(store, "gone");
                var unknown = RemovedApart(store, "unknown");
                string gone;
                using (var reading = store.Read())
                {
                    gone = reading.InForce.RemovalsOf("gone").Single();
                    Assert.That(reading.InForce.RemovalsOf("unknown"), Is.Empty, "a name the file does not know");
                }

                var before = hold.Taken;
                var asked = target.Asked(new[] { "ops", "gone", "unknown", "fresh" });

                Assert.That(hold.Taken - before, Is.EqualTo(1), "a single hold");
                Assert.That(asked.Served, Is.True);
                Assert.That(asked.Listed, Is.EqualTo(new[] { "ops" }));
                Assert.That(asked.Removed, Is.EqualTo(new[] { "gone" }), "not a name only a deletion record tells of");
                Assert.That(asked.Returning.Keys, Is.EqualTo(new[] { "gone" }));
                Assert.That(asked.Returning["gone"].Single().AdmissionKey, Is.EqualTo(CertificateDigest.Of(client)));
                Assert.That(asked.Held("gone", gone), Is.True, "a removal by its record");
                Assert.That(asked.Held("unknown", unknown), Is.False, "a removal only a deletion record tells of");
                Assert.That(asked.Held("fresh", unknown), Is.False, "a deletion record of another name's removal");
                Assert.That(asked.Held("ops", gone), Is.False, "a name on the list was not asked about");
            }
        }

        [Test]
        public void A_target_whose_store_stopped_serving_its_authority_holds_nothing_for_a_dialog()
        {
            // Served when the target was built, and a second authority standing since: the reading
            // the dialog would be built from serves nothing, and holds the word the write answers
            // with. Seeded so that each emptiness can fail: served, the same reading lists `ops`,
            // finds the client holding `gone` and holds the removal of `gone`.
            CreateAuthorityIn(_first);
            var store = new KdbxStore(_first.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Name(store, "gone");
            using (var target = _targets.Current().Single())
            using (var client = new AuthorityIssuer().IssueClient(target.AuthorityKey, "agent-fictitious", new[] { "gone" }).Read())
            {
                new StoredAdmissions(_first.Database).Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "gone" }, target.DatabaseId));
                StoreSeeds.Unname(store, "gone");
                string gone;
                using (var reading = store.Read())
                {
                    gone = reading.InForce.RemovalsOf("gone").Single();
                }

                var served = target.Asked(new[] { "ops", "gone" });
                Assert.That(served.Served, Is.True, "not served to begin with, so nothing below is measured");
                Assert.That(served.Listed, Is.EqualTo(new[] { "ops" }));
                Assert.That(served.Returning.Keys, Is.EqualTo(new[] { "gone" }));
                Assert.That(served.Held("gone", gone), Is.True);

                StoreSeeds.Beside(store);
                var asked = target.Asked(new[] { "ops", "gone" });

                Assert.That(asked.Served, Is.False);
                Assert.That(asked.Word, Is.EqualTo(HeldBackTarget.Undecided));
                Assert.That(asked.Listed, Is.Empty);
                Assert.That(asked.Removed, Is.Empty);
                Assert.That(asked.Returning, Is.Empty);
                Assert.That(asked.Held("gone", gone), Is.False);
            }
        }

        [Test]
        public void A_target_whose_database_closed_reads_nothing_for_a_dialog_and_throws_that_the_database_is_shut()
        {
            // As `Unavailable` and a client's admission are read: a database gone is never a
            // reading of a store, which the pairing handler answers `locked`.
            CreateAuthorityIn(_first);
            using (var target = _targets.Current().Single())
            {
                Assert.That(target.Asked(new[] { "ops" }).Served, Is.True, "not served to begin with, so nothing below is measured");

                _first.Database.Close();

                Assert.Throws<KeePassGate.Diagnostics.DatabaseShut>(() => target.Asked(new[] { "ops" }));
            }
        }

        /// <summary>
        /// A removal of this name composed under the store key and named by a deletion record
        /// alone, answering its identifier: its entry deleted for good and every summary entry
        /// restating it lost, as a Synchronize with a copy that lost them leaves it.
        /// </summary>
        private string RemovedApart(KdbxStore store, string name)
        {
            using (var reading = store.Read())
            {
                var removal = StoreRecord.Compose(
                    reading.InForce.Key.Bytes,
                    StoreKinds.Removed,
                    new Dictionary<string, string>(StringComparer.Ordinal) { { StoreKinds.NameMember, name } });
                _first.Database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(removal.Identifier), DateTime.UtcNow));
                return removal.Hex;
            }
        }

        [Test]
        public void A_connection_is_judged_in_a_hold_for_its_word_and_a_single_hold_for_its_admission_and_its_names()
        {
            // The names a client holds are read in the hold that reads its admission, from the
            // same reading, so the names granted and the names withheld are two halves of a single
            // list and no lock lands between the names and the admission. A hold of its own for
            // the names would hold KeePass's thread again at every connection.
            CreateAuthorityIn(_first);
            StoreSeeds.Name(new KdbxStore(_first.Database, GateVersion.Current, null), "ops");
            var hold = new CountedHolds();
            using (var targets = new OpenDatabaseTargets(delegate { return _open; }, null, hold, _log))
            using (var target = targets.Current().Single())
            using (var client = new AuthorityIssuer().IssueClient(
                       target.AuthorityKey, "agent-fictitious", new[] { "ops", "retired" }).Read())
            {
                var admissions = new StoredAdmissions(_first.Database);
                admissions.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops", "retired" }, target.DatabaseId));
                StoreSeeds.Unname(admissions.Store, "retired");
                var before = hold.Taken;

                var identity = KeePassGate.Pipeline.ClientCredentials.For(client, target, _log);

                Assert.That(identity.IsPaired, Is.True);
                Assert.That(identity.Profiles, Is.EqualTo(new[] { "ops" }));
                Assert.That(identity.Removed, Is.EqualTo(new[] { "retired" }));
                Assert.That(hold.Taken - before, Is.EqualTo(2), "a hold for the word, and a hold for the admission with the names");
            }
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
        public void A_node_s_tag_is_the_same_after_KeePass_starts_again_and_new_with_a_new_authority()
        {
            // The key is derived from the host certificate as the database stores it, so a client
            // that read before a restart writes after it under the tag it holds. A key drawn when
            // the plugin loaded refused every such write for nothing anybody wrote.
            CreateAuthorityIn(_first);
            var root = _targets.Current()[0].Vault;
            var before = root.GroupTag(root.RootAlias);

            _first.SaveCloseAndReopen();
            _open[0] = _first.Database;

            using (var started = new OpenDatabaseTargets(delegate { return _open; }, _log))
            {
                var again = started.Current()[0].Vault;
                Assert.That(again.GroupTag(again.RootAlias), Is.EqualTo(before), "the same tag from a new start");

                ServiceFor(_first).Recreate();
                var recreated = started.Current()[0].Vault;
                Assert.That(recreated.GroupTag(recreated.RootAlias), Is.Not.EqualTo(before),
                    "and a new authority is a new host certificate, so a new key");
            }
        }

        [Test]
        public void A_node_s_tag_is_keyed_by_the_stored_host_certificate_and_never_by_the_gate_key()
        {
            // The gate key would make the tag the same across a restart and new with a new
            // authority as well, which is all the test above can see, and every paired client holds
            // it: keyed by it, any client could compute any node's tag for a guess offline.
            CreateAuthorityIn(_first);
            var vault = _targets.Current()[0].Vault;
            var served = vault.GroupTag(vault.RootAlias);
            var root = _first.Database.RootGroup;

            using (var reading = ServiceFor(_first).Read())
            {
                var material = reading.Material;
                Assert.That(served, Is.EqualTo(new NodeTags(NodeTags.KeyFrom(material.HostPkcs12)).Of(root)));
                Assert.That(served, Is.Not.EqualTo(new NodeTags(NodeTags.KeyFrom(material.SniKey)).Of(root)),
                    "the gate key is the one every paired client holds");
            }
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
                delegate { _ = held.Authority.RawData; },
                "and the material goes as soon as it is given back");
        }

        [Test]
        public void A_target_given_back_by_its_last_holder_overwrites_its_tag_key()
        {
            // The tag key is derived from the host's private key, and was the one array of the
            // host's material a target let go without overwriting it.
            CreateAuthorityIn(_first);
            var held = _targets.Current()[0];
            var vault = held.Vault;
            Assume.That(vault.GroupTag(vault.RootAlias), Is.Not.Null);

            ServiceFor(_first).Recreate();
            foreach (var replacement in _targets.Current())
            {
                replacement.Dispose();
            }

            Assert.That(vault.GroupTag(vault.RootAlias), Is.Not.Null, "a holder is still finishing with it");

            held.Dispose();

            Assert.Throws<ObjectDisposedException>(
                delegate { vault.GroupTag(vault.RootAlias); },
                "a tag is refused rather than made under a key of zeros");
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
        public void A_database_being_served_that_loses_its_authority_is_said_to_be_dropped()
        {
            // Every other refusal on this path says something, and this one was silent: the
            // database left the answer set, and each of its clients got a dropped connection with
            // nothing in the log to say why. Said once, when it stops being served, and not again
            // on every connection that finds it has nothing to serve.
            CreateAuthorityIn(_first);
            CreateAuthorityIn(_second);
            var first = CertificateDigest.Shorten(DigestOf(_first));
            var second = CertificateDigest.Shorten(DigestOf(_second));
            Assume.That(_targets.Current(), Has.Count.EqualTo(2));

            // The first database the way synchronizes can leave it: retired by another copy's
            // answer, with an authority this cannot read standing in its place. The second the way
            // the tab takes it away.
            var store = new KdbxStore(_first.Database, GateVersion.Current, null);
            StoreSeeds.Retire(store, DigestOf(_first));
            StoreSeeds.Damaged(store);
            ServiceFor(_second).Delete();

            Assert.That(_targets.Current(), Is.Empty);
            _targets.Current();

            Assert.That(_log.Warnings, Has.Count.EqualTo(2), string.Join("\n", _log.Warnings));
            Assert.That(_log.Warnings, Has.Some.Contains(first).And.Contains("cannot be read"));
            Assert.That(_log.Warnings, Has.Some.Contains(second).And.Contains("every authority it held is retired"),
                "a delete retires the authority, and the log names that rather than an authority never made");
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
            StoreSeeds.Damaged(new KdbxStore(_second.Database, GateVersion.Current, null));

            var targets = _targets.Current();

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DatabaseId, Is.EqualTo(DigestOf(_first)));
        }

        [Test]
        public void An_authority_whose_key_identifier_will_not_read_does_not_stop_the_others_being_served()
        {
            // A crafted authority whose subject key identifier is not ASN.1 at all, beside a host
            // it did sign: the store reads the pair as whole, because nothing that reads an
            // authority in decodes that identifier, and the issuer is the first thing that does.
            // Its own reading threw an `IOException` out of the responder's mint, which nothing on
            // the way up named, and the resolver answered nothing for the healthy database beside
            // it either. Refused with the issuer's `ArgumentException`, each is served, the crafted
            // one without a responder, and the log names why.
            CreateAuthorityIn(_first);

            var authority = Minted.Named("CN=KeePassGate authority")
                .AsAuthority()
                .WithKeyIdentifier(new byte[] { 0xFF, 0xFF, 0xFF })
                .SelfSigned();
            var host = Minted.Named("CN=KeePassGate host").ForUsage(AuthorityIssuer.ServerAuthOid).IssuedBy(authority);
            var crafted = CertificateDigest.Of(authority.CertificateDer);
            var sniKey = Enumerable.Range(1, AuthorityMaterial.SniKeyLength).Select(i => (byte)i).ToArray();
            using (var material = new AuthorityMaterial(authority, host, sniKey))
            {
                StoreSeeds.AuthorityOf(new KdbxStore(_second.Database, GateVersion.Current, null), material);
            }

            var targets = _targets.Current();

            Assert.That(targets.Select(target => target.DatabaseId), Is.EquivalentTo(new[] { DigestOf(_first), crafted }));
            Assert.That(_log.Errors, Has.Some.Contains("a responder certificate could not be minted"));
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
            // appear on it however anything is loaded. Handing Windows a key without
            // `EphemeralKeySet` on the way to the stored form, which writes it to the profile,
            // fails `CertificateBlobTests` in the unit tier and leaves this one green, because that
            // is a path this one does not use. What this catches is a return to `SslStream` and
            // SChannel for the server, which would put the host key in the user profile for as long
            // as a database was open, and which `DESIGN.md` §133 records as measured and rejected.
            // So each handshake is the gate's own, through `GateListener` over these targets,
            // which is where such a return would land: one made beside it, with a TLS server built
            // for the test, would stay green over a listener that wrote a key on every connection.
            // It is worth its three handshakes for that alone.
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
                var target = _targets.Current()[0];

                var before = PrivateKeyFiles.Names();
                Assert.That(Served(target), Is.EqualTo("ok"));

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

        /// <summary>
        /// A handshake with the gate this plugin serves, its listener over these targets writing to
        /// the log given, named for the target: "ok" when it completes over that target's host
        /// certificate.
        /// </summary>
        private string Served(IGateTarget target, IGateLog log = null)
        {
            using (var listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(_targets, NullGateLog.Instance),
                KeePassGate.Spec.GateOperations.Router("9.9.9"),
                log ?? NullGateLog.Instance))
            {
                if (!listener.Start())
                {
                    return "the listener did not start";
                }

                byte[] presented = null;
                var thrown = Thrown.By(() =>
                {
                    using (var client = new TcpClient("127.0.0.1", listener.Port))
                    using (var stream = new SslStream(
                        client.GetStream(),
                        false,
                        delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
                        {
                            presented = certificate == null ? null : certificate.GetRawCertData();
                            return true;
                        }))
                    {
                        stream.AuthenticateAsClient(SniName.For(target.GateKey), null, SslProtocols.Tls13, false);
                    }
                });

                if (thrown != null)
                {
                    return thrown.GetType().Name + ": " + thrown.Message;
                }

                return presented != null && presented.SequenceEqual(target.Host.CertificateDer)
                    ? "ok"
                    : "the gate served a certificate other than the target's host";
            }
        }

        private static string Handshake(CertifiedKey host)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = "not reached";

            var server = new Thread(delegate()
            {
                var thrown = Thrown.By(() =>
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
                });

                if (thrown != null)
                {
                    result = thrown.GetType().Name + ": " + thrown.Message;
                }
            });

            server.IsBackground = true;
            server.Start();

            // The client's half fails whenever the server's does, and what is measured is the
            // server's.
            _ = Thrown.By(() =>
            {
                using (var client = new TcpClient("127.0.0.1", port))
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, SslProtocols.Tls13, false);
                }
            });

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
            return DatabaseAuthority.Of(database.Database, NullGateLog.Instance);
        }

        private static string DigestOf(TemporaryDatabase database)
        {
            using (var reading = ServiceFor(database).Read())
            {
                return reading.Material.AuthorityDigest;
            }
        }

        /// <summary>
        /// A hold whose first question about which hold a reading is in throws, as a store read
        /// that fails raises, and that answers each later question with the hold it is in.
        /// </summary>
        private sealed class FirstAskThrows : KeePassGate.Ui.IReadHold
        {
            private bool _asked;
            private long _stamp;

            public long Stamp
            {
                get
                {
                    if (!_asked)
                    {
                        _asked = true;
                        throw new InvalidOperationException("the store could not be read");
                    }

                    return _stamp;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                _stamp++;
                return read();
            }
        }

        /// <summary>
        /// Holds on the calling thread, each outermost hold under a stamp of its own, and counts
        /// the outermost holds taken.
        /// </summary>
        private sealed class CountedHolds : KeePassGate.Ui.IReadHold
        {
            private long _stamp;
            private int _depth;

            public int Taken { get; private set; }

            public long Stamp
            {
                get { return _depth > 0 ? _stamp : 0; }
            }

            public T Hold<T>(Func<T> read)
            {
                if (_depth > 0)
                {
                    return read();
                }

                Taken++;
                _depth++;
                _stamp = Taken;
                try
                {
                    return read();
                }
                finally
                {
                    _depth--;
                }
            }
        }

        private sealed class RecordingLog : IGateLog
        {
            public List<string> Errors { get; } = new List<string>();

            public List<string> Infos { get; } = new List<string>();

            public List<string> Warnings { get; } = new List<string>();

            public void Info(string message)
            {
                Infos.Add(message);
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
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
