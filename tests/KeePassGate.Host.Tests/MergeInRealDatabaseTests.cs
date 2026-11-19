using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own synchronize does to admissions and revocations, and to the standing
    /// answers to a renewal, against copies of one real database.
    /// </summary>
    /// <remarks>
    /// An admission and a revocation are each an entry of the plugin's store, written once, so
    /// two copies that admit different clients keep both, and a revocation is an entry the other
    /// copy does not hold, which a merge brings and never takes away, `STORE.md` §615. The
    /// standing answers stay in `PwDatabase.CustomData`, the one dictionary KeePass merges key
    /// by key: a group's custom data carries no per-item modification times, so
    /// `PwGroup.AssignProperties` replaces the whole dictionary from whichever replica's group
    /// was touched last.
    ///
    /// These tests are the measurement. They drive `PwDatabase.MergeIn` with KeePass's own
    /// `PwMergeMethod.Synchronize`, which is what `File > Synchronize` runs, and with
    /// `OverwriteExisting` where a time could decide.
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
                    .IssueClient(reading.Material.AuthorityKey, "agent-first-fictitious", new[] { "ops" }).Read();
                _second = new AuthorityIssuer()
                    .IssueClient(reading.Material.AuthorityKey, "agent-second-fictitious", new[] { "reader" }).Read();
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
            Share();
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            Records(_elsewhere).Write(AdmissionRecord.For(_second, DateTimeOffset.UtcNow, new[] { "reader" }, StoredAdmissions.AnyIssuer));

            Synchronize();

            Assert.That(Records(_here).Find(CertificateDigest.Of(_first)), Is.Not.Null,
                "the admission written here is still here");
            Assert.That(Records(_here).Find(CertificateDigest.Of(_second)), Is.Not.Null,
                "and the one written on the other copy arrived, an entry of its own");
        }

        [Test]
        public void A_revoked_client_stays_revoked_when_the_other_copy_still_holds_the_admission()
        {
            // The revocation is an entry the other copy does not hold, which a merge brings, and
            // the admission the other copy still holds is refused beside it.
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            Share();

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
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            Share();
            Assume.That(Records(_elsewhere).Find(CertificateDigest.Of(_first)), Is.Not.Null);

            Assume.That(Records(_here).Remove(CertificateDigest.Of(_first)), Is.True);

            _elsewhere.Database.MergeIn(_here.Database, PwMergeMethod.Synchronize, null);

            Assert.That(Records(_elsewhere).Find(CertificateDigest.Of(_first)), Is.Null,
                "the copy that was not revoked on learns about it from the copy that was");
        }

        [Test]
        public void A_revoked_client_stays_refused_whatever_happened_to_the_times()
        {
            // A revocation is an entry of its own, and no time of any entry decides whether it
            // counts. Here the copy that did not revoke holds an admission touched after the
            // revocation, which outlives the deletion record a merge would otherwise apply to it,
            // and the revocation itself is dated a year back, through the file each time, merged
            // in either direction. A client the copies hold that nobody revoked stays admitted
            // throughout, so a store a merge left unreadable cannot pass for a refusal.
            var admissionKey = CertificateDigest.Of(_first);

            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteExisting })
            {
                foreach (var times in new[] { Times.AsWritten, Times.HolderNewer, Times.RevokerOlder, Times.Both })
                {
                    foreach (var intoRevoker in new[] { true, false })
                    {
                        using (var revoker = TemporaryDatabase.New())
                        using (var holder = TemporaryDatabase.New())
                        {
                            Assume.That(Authority(revoker).Create().Succeeded, Is.True);
                            Records(revoker).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
                            Records(revoker).Write(AdmissionRecord.For(_second, DateTimeOffset.UtcNow, new[] { "reader" }, StoredAdmissions.AnyIssuer));
                            Copy(revoker, holder);
                            Assume.That(Records(revoker).Remove(admissionKey), Is.True);

                            if (times == Times.HolderNewer || times == Times.Both)
                            {
                                Date(holder, DateTime.UtcNow.AddDays(1));
                            }

                            if (times == Times.RevokerOlder || times == Times.Both)
                            {
                                Date(revoker, DateTime.UtcNow.AddYears(-1));
                            }

                            revoker.SaveCloseAndReopen();
                            holder.SaveCloseAndReopen();

                            var target = intoRevoker ? revoker : holder;
                            var source = intoRevoker ? holder : revoker;
                            target.Database.MergeIn(source.Database, method, null);
                            target.SaveCloseAndReopen();

                            var named = method + ", " + times
                                        + (intoRevoker ? ", into the copy that revoked" : ", into the copy that did not");

                            Assert.That(Records(target).Find(CertificateDigest.Of(_second)), Is.Not.Null,
                                named + ": the client nobody revoked is still admitted");
                            Assert.That(Records(target).Find(admissionKey), Is.Null,
                                named + ": a revoked client was admitted again");
                            Assert.That(Records(target).All().Records.Select(record => record.AdmissionKey), Has.No.Member(admissionKey),
                                named + ": and the clients page listed it as a client");
                        }
                    }
                }
            }
        }

        [Test]
        public void Of_two_standing_answers_made_apart_the_later_is_the_one_kept()
        {
            // `CA.md` §204: a standing answer is changed rather than only added, so a merge decides
            // between two by their times, and the later stands. Written a moment apart, so the
            // times differ by more than the second they are kept to.
            var admissionKey = CertificateDigest.Of(_first);
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            Share();

            Records(_here).Answers.RefuseRenewal(admissionKey, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(3)));
            System.Threading.Thread.Sleep(1100);
            Records(_elsewhere).Answers.RefuseRenewal(admissionKey, RenewalRefusal.Forever());

            Synchronize();

            Assert.That(Records(_here).Answers.StandingRefusal(admissionKey).IsForever, Is.True);
        }

        [Test]
        public void A_standing_answer_a_merge_brings_beside_a_revoked_client_goes_with_the_next_write()
        {
            // A copy that still holds an answer about a certificate whose client was revoked
            // brings it, since a merge never removes a key. It refuses a renewal nothing could
            // ask for, and the next write of the plugin's own prunes it against the admissions
            // that write leaves, here a pairing of another client.
            var admissionKey = CertificateDigest.Of(_first);
            Records(_here).Write(AdmissionRecord.For(_first, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            Share();
            Records(_elsewhere).Answers.RefuseRenewal(admissionKey, RenewalRefusal.Forever());

            Assume.That(Records(_here).Remove(admissionKey), Is.True);

            Synchronize();
            Assume.That(Records(_here).Answers.StandingRefusal(admissionKey), Is.Not.Null, "the merge brought it");

            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, null);
            var paired = writer.WriteAdmission(
                _here.Database, AdmissionRecord.For(_second, DateTimeOffset.UtcNow, new[] { "reader" }, StoreSeeds.Served(Records(_here).Store)));
            Assume.That(paired.Ok, Is.True, paired.Reason);

            Assert.That(Records(_here).Answers.StandingRefusal(admissionKey), Is.Null);
            Assert.That(Records(_here).Find(admissionKey), Is.Null, "and the client is still refused");
        }

        [Test]
        public void An_expired_admission_a_merge_brings_stays_and_admits_nobody()
        {
            var then = DateTimeOffset.UtcNow.AddDays(-7);
            var issuer = new AuthorityIssuer(() => then);

            // The authority served made a week ago, since an authority issues nothing that began
            // before it, a certificate it issued then for a day, and a certificate it issues now.
            var recreated = new AuthorityService(
                new KdbxStore(_here.Database, GateVersion.Current, NullGateLog.Instance), issuer, NullGateLog.Instance).Recreate();
            Assume.That(recreated.Succeeded, Is.True, recreated.Message);
            using (var reading = Authority(_here).Read())
            using (var ended = issuer.IssueClient(reading.Material.AuthorityKey, "agent-ended-fictitious", new[] { "ops" }, TimeSpan.FromDays(1)).Read())
            using (var current = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-current-fictitious", new[] { "ops" }).Read())
            {
                Share();
                var record = AdmissionRecord.For(ended, then, new[] { "ops" }, reading.Material.AuthorityDigest);
                Records(_elsewhere).Write(record);
                Records(_elsewhere).Write(AdmissionRecord.For(current, DateTimeOffset.UtcNow, new[] { "ops" }, reading.Material.AuthorityDigest));

                Synchronize();
                Assume.That(Listed(_here), Has.Member(record.AdmissionKey), "the merge brought the admission");

                using (var targets = new OpenDatabaseTargets(delegate { return new[] { _here.Database }; }, null, NullGateLog.Instance))
                {
                    var target = targets.Current().Single();
                    Assert.That(KeePassGate.Pipeline.ClientCredentials.For(ended, target, NullGateLog.Instance).IsPaired, Is.False,
                        "the gate refuses it on its dates");
                    Assert.That(KeePassGate.Pipeline.ClientCredentials.For(current, target, NullGateLog.Instance).IsPaired, Is.True,
                        "and admits the certificate beside it that has not ended");
                }

                Records(_here).Write(AdmissionRecord.For(_second, DateTimeOffset.UtcNow, new[] { "reader" }, StoredAdmissions.AnyIssuer));

                Assert.That(Listed(_here), Has.Member(record.AdmissionKey), "and no write deletes it for its age");
            }
        }

        [Test]
        public void Two_authorities_made_apart_arrive_whole_and_side_by_side()
        {
            // Each authority is one record, whole in itself, so a merge cannot take a certificate
            // from one replica and a private key from another, and neither beats the other on a
            // clock. What arrives is a choice for the operator, with both candidates intact.
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
        public void The_same_two_keys_on_the_root_group_do_not_both_survive()
        {
            // The counterfactual, so that the standing answers' tests above cannot pass for some
            // reason other than the one they are about. These are two writes into a group's own
            // dictionary, and the merge takes that one from whichever replica's group was touched
            // last.
            _here.Database.RootGroup.CustomData.Set("KPG.Here", "a value");
            _here.Database.RootGroup.Touch(true, false);

            System.Threading.Thread.Sleep(1100);
            _elsewhere.Database.RootGroup.CustomData.Set("KPG.Elsewhere", "another value");
            _elsewhere.Database.RootGroup.Touch(true, false);

            Synchronize();

            Assert.That(_here.Database.RootGroup.CustomData.Exists("KPG.Elsewhere"), Is.True,
                "the later replica's key arrives");
            Assert.That(_here.Database.RootGroup.CustomData.Exists("KPG.Here"), Is.False,
                "and it arrives by replacing the dictionary, taking the other write with it");
        }

        private enum Times
        {
            AsWritten,
            HolderNewer,
            RevokerOlder,
            Both,
        }

        /// <summary>
        /// Every entry of the plugin's group, and every deletion record, dated to this moment.
        /// </summary>
        private static void Date(TemporaryDatabase database, DateTime when)
        {
            var group = database.Database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
            foreach (var entry in group.Entries)
            {
                entry.LastModificationTime = when;
                entry.LocationChanged = when;
            }

            foreach (var deleted in database.Database.DeletedObjects)
            {
                deleted.DeletionTime = when;
            }
        }

        /// <summary>
        /// The other copy made a copy of this one, as a file copied and opened elsewhere is.
        /// </summary>
        private void Share()
        {
            Copy(_here, _elsewhere);
        }

        private static void Copy(TemporaryDatabase from, TemporaryDatabase into)
        {
            into.Database.MergeIn(from.Database, PwMergeMethod.Synchronize, null);
        }

        private void Synchronize()
        {
            _here.Database.MergeIn(_elsewhere.Database, PwMergeMethod.Synchronize, null);
        }

        private static AuthorityService Authority(TemporaryDatabase database)
        {
            return DatabaseAuthority.Of(database.Database, NullGateLog.Instance);
        }

        private static StoredAdmissions Records(TemporaryDatabase database)
        {
            return new StoredAdmissions(database.Database);
        }

        private static string[] Listed(TemporaryDatabase database)
        {
            return Records(database).All().Records.Select(record => record.AdmissionKey).ToArray();
        }
    }
}
