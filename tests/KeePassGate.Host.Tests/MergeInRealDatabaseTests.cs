using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassLib;
using KeePassLib.Collections;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own synchronize does to admission records, against two real databases.
    /// </summary>
    /// <remarks>
    /// The property the one-key-per-client shape is chosen for is that a replicated database
    /// merges per key, so two hosts pairing different clients keep both and one host's write
    /// cannot undo another's. It holds of `PwDatabase.CustomData` and of nothing else a plugin
    /// can write to: a group's custom data carries no per-item modification times, so
    /// `PwGroup.AssignProperties` replaces the whole dictionary from whichever replica's group
    /// was touched last.
    ///
    /// These tests are the measurement. They drive `PwDatabase.MergeIn` with KeePass's own
    /// `PwMergeMethod.Synchronize`, which is what `File > Synchronize` runs.
    /// </remarks>
    [TestFixture]
    public sealed class MergeInRealDatabaseTests
    {
        private TemporaryDatabase _here;
        private TemporaryDatabase _elsewhere;
        private X509Certificate2 _first;
        private X509Certificate2 _second;

        [SetUp]
        public void SetUp()
        {
            _here = TemporaryDatabase.New();
            _elsewhere = TemporaryDatabase.New();

            // Two copies of one database, which is what a synchronize is for. KeePass matches
            // groups by identifier, so the replica's root group carries the same one.
            _elsewhere.Database.RootGroup.Uuid = _here.Database.RootGroup.Uuid;

            var created = Authority(_here).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            using (var reading = Authority(_here).Read())
            {
                _first = new AuthorityIssuer()
                    .IssueClient(reading.Material.Authority, "agent-first-fictitious", new[] { "ops" });
                _second = new AuthorityIssuer()
                    .IssueClient(reading.Material.Authority, "agent-second-fictitious", new[] { "reader" });
            }
        }

        [TearDown]
        public void TearDown()
        {
            _here.Dispose();
            _elsewhere.Dispose();
        }

        [Test]
        public void A_client_paired_on_one_copy_and_one_paired_on_the_other_both_survive()
        {
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));
            Records(_elsewhere).Write(AdmissionRecord.For(_second, DateTimeOffset.UtcNow, new[] { "reader" }));

            Synchronize();

            Assert.That(Records(_here).Find(CertificateDigest.Of(_first)), Is.Not.Null,
                "the record written here is still here");
            Assert.That(Records(_here).Find(CertificateDigest.Of(_second)), Is.Not.Null,
                "and the one written on the other copy arrived, which is what per key means");
        }

        [Test]
        public void A_revoked_client_stays_revoked_when_the_other_copy_still_holds_the_record()
        {
            // A removal leaves nothing for the merge to prefer, so the copy in the other replica
            // would win and the client would be admitted again. The revocation is a key the other
            // copy does not hold, which a merge never removes, so no clock has to favor it.
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));
            Records(_elsewhere).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));

            Assume.That(Records(_here).Remove(CertificateDigest.Of(_first)), Is.True);

            Synchronize();

            Assert.That(Records(_here).Find(CertificateDigest.Of(_first)), Is.Null,
                "a revoked client that comes back is a credential the operator believes is gone");
            Assert.That(Records(_here).All().Records, Is.Empty,
                "and the clients page must not show it either");
        }

        [Test]
        public void The_revocation_travels_to_the_copy_it_was_not_made_on()
        {
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));
            Records(_elsewhere).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));

            Assume.That(Records(_here).Remove(CertificateDigest.Of(_first)), Is.True);

            _elsewhere.Database.MergeIn(_here.Database, PwMergeMethod.Synchronize, null);

            Assert.That(Records(_elsewhere).Find(CertificateDigest.Of(_first)), Is.Null,
                "the copy that was not revoked on learns about it from the copy that was");
        }

        [Test]
        public void A_revoked_client_stays_refused_whatever_happened_to_the_times()
        {
            // What the emptied record alone could not do, and the revocation beside it does. The
            // record lost whenever its time did: dropped from the file, which KeePassXC and KeePass
            // before 2.48 both do, or older than the other copy's, or overwritten outright by an
            // import. Here every one of those happens at once where it can: the other copy writes
            // its record no earlier than the revocation, so its time never loses on age, and the
            // times are dropped on either side, through the file each time, merged in either
            // direction.
            var admissionKey = CertificateDigest.Of(_first);

            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteExisting })
            {
                foreach (var strip in new[] { Strip.Neither, Strip.Revoker, Strip.Holder, Strip.Both })
                {
                    foreach (var intoRevoker in new[] { true, false })
                    {
                        using (var revoker = TemporaryDatabase.New())
                        using (var holder = TemporaryDatabase.New())
                        {
                            holder.Database.RootGroup.Uuid = revoker.Database.RootGroup.Uuid;
                            Records(revoker).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));
                            Assume.That(Records(revoker).Remove(admissionKey), Is.True);
                            Records(holder).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }));

                            if (strip == Strip.Revoker || strip == Strip.Both)
                            {
                                StripTimes(revoker);
                            }

                            if (strip == Strip.Holder || strip == Strip.Both)
                            {
                                StripTimes(holder);
                            }

                            revoker.SaveCloseAndReopen();
                            holder.SaveCloseAndReopen();

                            var target = intoRevoker ? revoker : holder;
                            var source = intoRevoker ? holder : revoker;
                            target.Database.MergeIn(source.Database, method, null);
                            target.SaveCloseAndReopen();

                            var named = method + ", times dropped on " + strip
                                        + (intoRevoker ? ", into the copy that revoked" : ", into the copy that did not");

                            Assert.That(Records(target).Find(admissionKey), Is.Null,
                                named + ": a revoked client was admitted again");
                            Assert.That(Records(target).All().Records, Is.Empty,
                                named + ": and the clients page listed it as a client");
                        }
                    }
                }
            }
        }

        [Test]
        public void Two_authorities_made_apart_arrive_whole_and_side_by_side()
        {
            // The three parts are one key precisely so a merge cannot take a certificate from one
            // replica and a private key from another, and each authority is a key of its own so
            // that neither beats the other on a clock. What arrives is a choice for the operator,
            // with both candidates intact.
            var created = Authority(_elsewhere).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            _elsewhere.Database.RootGroup.Touch(true, false);

            Synchronize();

            using (var reading = Authority(_here).Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided),
                    "neither copy's authority may win silently");
                Assert.That(reading.Candidates.Count, Is.EqualTo(2));
                Assert.That(reading.Candidates.All(c => c.IsWhole), Is.True,
                    "and a merge must never produce a damaged one, whichever copy it came from");
            }
        }

        [Test]
        public void The_same_two_records_on_the_root_group_do_not_both_survive()
        {
            // The counterfactual, so that the tests above cannot pass for some reason other than
            // the one they are about. These are the same two writes into the dictionary the
            // records used to live in, and the merge takes that one from whichever replica's
            // group was touched last.
            _here.Database.RootGroup.CustomData.Set("KPG.Client.here", "a record");
            _here.Database.RootGroup.Touch(true, false);

            System.Threading.Thread.Sleep(1100);
            _elsewhere.Database.RootGroup.CustomData.Set("KPG.Client.elsewhere", "another record");
            _elsewhere.Database.RootGroup.Touch(true, false);

            Synchronize();

            Assert.That(_here.Database.RootGroup.CustomData.Exists("KPG.Client.elsewhere"), Is.True,
                "the later replica's key arrives");
            Assert.That(_here.Database.RootGroup.CustomData.Exists("KPG.Client.here"), Is.False,
                "and it arrives by replacing the dictionary, taking the other write with it");
        }

        private enum Strip
        {
            Neither,
            Revoker,
            Holder,
            Both,
        }

        /// <summary>
        /// Every key written again with no time, which is what a file saved by KeePassXC or by
        /// KeePass before 2.48 holds. The public `Set` always stamps one, so this goes through the
        /// internal overload.
        /// </summary>
        private static void StripTimes(TemporaryDatabase database)
        {
            var unstamped = typeof(StringDictionaryEx).GetMethod(
                "Set", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
            Assume.That(unstamped, Is.Not.Null, "KeePass no longer lets a key be written without a time");

            foreach (var pair in database.Database.CustomData.ToList())
            {
                unstamped.Invoke(database.Database.CustomData, new object[] { pair.Key, pair.Value, null });
            }
        }

        private void Synchronize()
        {
            _here.Database.MergeIn(_elsewhere.Database, PwMergeMethod.Synchronize, null);
        }

        private static AuthorityService Authority(TemporaryDatabase database)
        {
            return new AuthorityService(new CustomDataStore(database.Database), NullGateLog.Instance);
        }

        private static AdmissionStore Records(TemporaryDatabase database)
        {
            return new AdmissionStore(new CustomDataStore(database.Database), NullGateLog.Instance);
        }
    }
}
