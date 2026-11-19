using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The authorities a store holds, `STORE.md` §614 (S3): which stand, which is served, each read
    /// whole or damaged, and a damaged authority retired like any other.
    /// </summary>
    [TestFixture]
    public sealed class StoreAuthoritiesTests
    {
        private static readonly AuthorityIssuer Issuer = new AuthorityIssuer();

        [Test]
        public void An_authority_created_reads_whole_and_is_served()
        {
            var store = new MemoryStore();
            var id = StoreSeeds.Authority(store);

            var reading = store.Read();
            using (var authority = StoreAuthorities.Read(reading, 0))
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(authority.Material.AuthorityDigest, Is.EqualTo(id), "filed under the digest of its certificate");
            }

            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(id));
        }

        [TestCase("an id that is not the digest of its certificate")]
        [TestCase("a host its authority did not issue")]
        [TestCase("a key that is not its certificate's own")]
        [TestCase("a member missing")]
        [TestCase("a gate key of the wrong length")]
        public void An_authority_that_passes_its_check_and_is_not_whole_is_damaged_gives_its_key_and_is_retired_by_Recreate(string damage)
        {
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            store.Write(new[] { Act(key, Damaged(key, damage)) });

            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Key.Bytes, Is.EqualTo(key), "its key is found");
            using (var authority = StoreAuthorities.Read(reading, 0))
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(authority.Problems, Is.Not.Empty);
            }

            // Answered by its `id` and never served: no material is read for a connection.
            var damagedId = reading.Answering.Single().Form.Member("id");

            var service = new AuthorityService(store, NullGateLog.Instance);
            Assert.That(service.Create().Succeeded, Is.False, "Create is refused while an authority stands");
            var recreated = service.Recreate();
            Assert.That(recreated.Succeeded, Is.True, recreated.Message);

            var after = store.Read();
            Assert.That(after.InForce.Taken("retired"), Does.Contain(damagedId));
            Assert.That(after.InForce.Key.Bytes, Is.EqualTo(key), "Recreate carries the key");
            using (var authority = StoreAuthorities.Read(after, 0))
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.Present));
            }
        }

        [Test]
        public void An_authority_beside_a_deletion_record_or_retired_does_not_stand()
        {
            var store = new MemoryStore();
            var id = StoreSeeds.Authority(store);
            var entry = store.Read().Answering.Single();

            var deleted = store.Copy();
            deleted.Delete(StoreComposer.BytesOf(entry.Hex));
            Assert.That(StoreAuthorities.Served(deleted.Read()), Is.Null, "beside a deletion record");

            var service = new AuthorityService(store, NullGateLog.Instance);
            Assert.That(service.Delete().Succeeded, Is.True);
            var reading = store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.Null, "retired");
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { id }));
            using (var authority = StoreAuthorities.Read(reading, 0))
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(authority.IsRetired, Is.True);
            }
        }

        [Test]
        public void Several_authorities_standing_read_as_undecided_and_none_is_served()
        {
            var store = new MemoryStore();
            var ids = StoreSeeds.Undecided(store);

            var reading = store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.Null);
            using (var authority = StoreAuthorities.Read(reading, 0))
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                Assert.That(authority.Candidates.Select(candidate => candidate.Fingerprint), Is.EqualTo(ids),
                    "in the order of their fingerprints");
                Assert.That(authority.Candidates.All(candidate => candidate.IsWhole), Is.True);
            }
        }

        [Test]
        public void Several_authorities_standing_are_offered_in_the_order_of_their_fingerprints_whatever_their_entries()
        {
            // The operator reads the authorities in the order of their fingerprints, ordinal, on
            // the chooser and on the tab alike, and never in the order of the identifiers of
            // their entries, which nobody sees and which differ with the store key. A pair whose
            // two orders differ is made here, a store at a time.
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var store = new MemoryStore();
                StoreSeeds.Undecided(store);
                var reading = store.Read();
                var byEntry = reading.Answering.Select(record => record.Form.Member(StoreKinds.Id)).ToList();
                var byFingerprint = byEntry.OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (byEntry.SequenceEqual(byFingerprint))
                {
                    continue;
                }

                using (var authority = StoreAuthorities.Read(reading, 0))
                {
                    Assert.That(authority.Candidates.Select(candidate => candidate.Fingerprint), Is.EqualTo(byFingerprint));
                }

                return;
            }

            Assert.Fail("no pair of authorities was made whose entries and fingerprints sort apart");
        }

        [Test]
        public void A_store_with_no_key_holds_no_authority_and_none_retired()
        {
            using (var none = StoreAuthorities.Read(new MemoryStore().Read(), 3))
            {
                Assert.That(none.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(none.IsRetired, Is.False, "nothing was made, which Create answers");
                Assert.That(none.ClientRecordCount, Is.EqualTo(3));
            }
        }

        [Test]
        public void Reading_twice_gives_two_usable_copies()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);

            using (var first = StoreAuthorities.Read(store.Read(), 0))
            using (var second = StoreAuthorities.Read(store.Read(), 0))
            {
                Assert.That(first.Material.AuthorityKey.PrivateKey.IsPrivate, Is.True);
                Assert.That(second.Material.AuthorityKey.PrivateKey.IsPrivate, Is.True);
                Assert.That(first.Material.Authority, Is.Not.SameAs(second.Material.Authority));
                Assert.That(first.Material.Authority.HasPrivateKey, Is.False,
                    "Windows reads the certificate and is never handed its key");
            }
        }

        [Test]
        public void The_gate_key_handed_out_is_a_copy()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);

            using (var reading = StoreAuthorities.Read(store.Read(), 0))
            {
                var first = reading.Material.SniKey;
                Array.Clear(first, 0, first.Length);

                Assert.That(reading.Material.SniKey, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Letting_the_material_go_overwrites_the_two_secrets_that_can_be_overwritten()
        {
            // What `CA.md` §208 means by the normal host key being let go at lock: the gate key
            // and the stored PKCS#12 the host certificate and its key travel in are erased, and
            // the keys themselves, which nothing can erase, are only let go.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);

            // Declared outside the `using`, because what is asserted is what it leaves behind.
            var reading = StoreAuthorities.Read(store.Read(), 0);
            using (reading)
            {
                Assume.That(reading.Material.HostPkcs12.Any(b => b != 0), Is.True);
                Assume.That(reading.Material.SniKey.Any(b => b != 0), Is.True);
            }

            Assert.That(reading.Material.SniKey.Any(b => b != 0), Is.False);
            Assert.That(reading.Material.HostPkcs12.Any(b => b != 0), Is.False);
        }

        [Test]
        public void A_reading_let_go_overwrites_the_store_key_its_forms_found()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);

            // Declared outside the `using`, because what is asserted is what it leaves behind.
            byte[] key;
            using (var reading = store.Read())
            {
                key = reading.InForce.Key.Bytes;
                Assume.That(key.Any(b => b != 0), Is.True);
            }

            Assert.That(key.Any(b => b != 0), Is.False);
        }

        [Test]
        public void Admission_records_are_counted_whatever_state_the_authority_is_in()
        {
            var store = new MemoryStore();
            var service = new AuthorityService(store, NullGateLog.Instance);

            using (var absent = service.Read())
            {
                Assert.That(absent.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(absent.ClientRecordCount, Is.Zero, "a database with no store key holds no admission");
            }

            StoreSeeds.Authority(store);
            StoreSeeds.Client(store, StoreRecords.Unread('a'));
            StoreSeeds.Client(store, StoreRecords.Unread('b'));
            using (var present = service.Read())
            {
                Assert.That(present.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(present.ClientRecordCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void A_candidate_that_will_not_read_can_be_retired_and_never_kept()
        {
            var store = new MemoryStore();
            var whole = StoreSeeds.Authority(store);
            var damaged = StoreSeeds.Damaged(store);
            var service = new AuthorityService(store, NullGateLog.Instance);

            using (var reading = service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                var candidate = reading.Candidates.Single(c => c.Fingerprint == damaged);
                Assert.That(candidate.IsWhole, Is.False);
                Assert.That(candidate.Problems, Is.Not.Empty);
            }

            var listed = new[] { whole, damaged };
            Assert.That(service.Keep(damaged, listed).Succeeded, Is.False, "the candidate that will not read is never kept");
            Assert.That(service.Keep(whole, listed).Succeeded, Is.True);

            var after = store.Read();
            Assert.That(after.InForce.Taken("retired"), Is.EquivalentTo(new[] { damaged }));
            Assert.That(after.InForce.Records.Any(record => record.Is("authority") && record.Form.Member("id") == damaged), Is.False,
                "and its entry is deleted with the retirement");
        }

        [Test]
        public void Deleting_retires_every_authority_there_is_including_an_authority_that_will_not_read()
        {
            var store = new MemoryStore();
            var ids = StoreSeeds.Undecided(store);
            var damaged = StoreSeeds.Damaged(store);

            Assert.That(new AuthorityService(store, NullGateLog.Instance).Delete().Succeeded, Is.True);

            var after = store.Read();
            Assert.That(after.InForce.Taken("retired"), Is.EquivalentTo(ids.Concat(new[] { damaged })));
            Assert.That(after.Answering, Is.Empty);
        }

        [Test]
        public void The_most_problems_an_authority_is_reported_with_are_what_the_tab_is_measured_against()
        {
            // J031: the tab is measured against `LongestProblems`, which holds which problems can
            // arrive together. This record carries the most that can without anything throwing:
            // a gate key of the wrong length, filed under a digest that is not its own, and a host
            // from another authority. A check added to the reading that can arrive beside these
            // makes one more, and this fails for it.
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            store.Write(new[] { Act(key, Damaged(key, "every problem at once")) });

            var longest = AuthorityRecord.LongestProblems(new string('x', 200));
            using (var reading = StoreAuthorities.Read(store.Read(), 0))
            {
                Assume.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems.Count, Is.EqualTo(longest.Count), string.Join(" | ", reading.Problems));
                for (var index = 0; index < longest.Count; index++)
                {
                    Assert.That(reading.Problems[index].Length, Is.LessThanOrEqualTo(longest[index].Length), reading.Problems[index]);
                }
            }
        }

        [Test]
        public void An_authority_Windows_wrote_still_reads_as_the_authority_it_was_and_still_issues()
        {
            // What is stored keeps its shape, so parts written before Bouncy Castle issued anything
            // read as the authority they were, under the digest every record names it by, and go
            // on issuing certificates that chain to it.
            var store = new MemoryStore();
            var id = StoreSeeds.WrittenDown(store, SeededAuthority.Authority, SeededAuthority.Host, SeededAuthority.SniKey);

            using (var reading = StoreAuthorities.Read(store.Read(), 0))
            {
                Assert.That(reading.IsPresent, Is.True, string.Join("; ", reading.Problems));
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(id));

                // At a moment inside the authority's life rather than at the real moment, so that
                // this tests what was written rather than the calendar.
                var issuer = new AuthorityIssuer(() => SeededAuthority.During);
                using (var client = issuer.IssueClient(reading.Material.AuthorityKey, "agent-fictitious", null).Read())
                {
                    Assert.That(AnchorPin.Chains(client, reading.Material.Authority), Is.True);
                }
            }
        }

        /// <summary>An authority's record that passes its check and does not read whole, by the damage named.</summary>
        private static StoreRecord Damaged(byte[] key, string damage)
        {
            var authority = Issuer.CreateAuthority();
            var sniKey = new byte[AuthorityMaterial.SniKeyLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(sniKey);
            }

            using (var material = new AuthorityMaterial(authority, Issuer.IssueHost(authority), sniKey))
            {
                var members = AuthorityRecord.Members(material, key).ToDictionary(pair => pair.Key, pair => pair.Value);
                switch (damage)
                {
                    case "an id that is not the digest of its certificate":
                        members["id"] = new string('e', 64);
                        break;
                    case "a host its authority did not issue":
                        var other = Issuer.CreateAuthority();
                        members["host"] = CertificateBlob.Save(Issuer.IssueHost(other));
                        break;
                    case "a key that is not its certificate's own":
                        members["ca"] = Paired(authority, Issuer.CreateAuthority());
                        break;
                    case "a member missing":
                        members.Remove("host");
                        break;
                    case "a gate key of the wrong length":
                        members["sniKey"] = Convert.ToBase64String(new byte[5]);
                        break;
                    case "every problem at once":
                        members["sniKey"] = Convert.ToBase64String(new byte[8]);
                        members["host"] = CertificateBlob.Save(Issuer.IssueHost(Issuer.CreateAuthority()));
                        members["id"] = new string('d', 64);
                        break;
                }

                return StoreRecord.Compose(key, "authority", members);
            }
        }

        /// <summary>A PKCS#12 holding a certificate beside the key of another certificate.</summary>
        private static string Paired(CertifiedKey certificate, CertifiedKey keyOf)
        {
            var store = new Pkcs12StoreBuilder()
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .Build();
            store.SetKeyEntry(
                "KeePassGate",
                new AsymmetricKeyEntry(keyOf.PrivateKey),
                new[] { new X509CertificateEntry(certificate.Certificate) });
            using (var bytes = new MemoryStream())
            {
                store.Save(bytes, Array.Empty<char>(), new SecureRandom());
                return Convert.ToBase64String(bytes.ToArray());
            }
        }


        /// <summary>An act starting a store under this key, where none stands.</summary>
        private static StoreAct Act(byte[] key, StoreRecord record)
        {
            return new StoreAct("an authority of the test's", key, null, new[] { record }, null);
        }
    }
}
