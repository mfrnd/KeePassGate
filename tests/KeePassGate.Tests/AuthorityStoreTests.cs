using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Reading and writing the authority. Everything that is not whole is damage, and damage is
    /// never quietly treated as absence: the two have different remedies.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityStoreTests
    {
        private InMemoryKeyValueStore _values;
        private AuthorityStore _store;
        private AuthorityIssuer _issuer;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _log = new RecordingGateLog();
            _store = new AuthorityStore(_values, _log);
            _issuer = new AuthorityIssuer();
        }

        [Test]
        public void A_store_that_will_not_list_is_reported_rather_than_read_as_empty()
        {
            // The twin of `AdmissionStoreTests.A_store_that_throws_is_reported_and_admits_nobody`.
            // There was no test here, and that is why one of the two copies of this enumeration was
            // guarded and the other was not for two steps.
            _values.ThrowOnList = true;

            using (var reading = _store.Read())
            {
                Assert.That(reading.ClientsComplete, Is.False, "it could not count them");
                Assert.That(reading.ClientRecordCount, Is.Zero);
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged),
                    "which authority there is is a listing too, and one that failed is not absence");
            }

            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void Deleting_refuses_when_the_records_cannot_be_listed()
        {
            // The authority's own parts go first, so a delete that cannot enumerate would leave
            // records behind and report that there were none.
            _values.ThrowOnList = true;

            Assert.Throws<AuthorityFailure>(() => _store.DeleteEverything());
        }

        [Test]
        public void An_untouched_database_holds_no_authority()
        {
            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(reading.Material, Is.Null);
                Assert.That(reading.Problems, Is.Empty);
            }
        }

        [Test]
        public void What_is_written_comes_back()
        {
            var written = Write();

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.Authority.RawData, Is.EqualTo(written.Authority));
                Assert.That(reading.Material.Host.RawData, Is.EqualTo(written.Host));
                Assert.That(reading.Material.SniKey, Is.EqualTo(written.SniKey));
            }
        }

        [Test]
        public void It_is_written_under_the_one_key_the_design_names()
        {
            var written = Write();

            Assert.That(_values.Keys, Is.EqualTo(new[] { AuthorityKeys.AuthorityKey(written.Fingerprint) }),
                "three parts, one key, so a merge cannot take them from different replicas");
        }

        [Test]
        public void A_document_that_will_not_read_is_damage_rather_than_absence()
        {
            _values.Plant(AuthorityKeys.AuthorityKey(Unreadable), "this is not a document");

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged),
                    "absence would invite Create, which would write over whatever is there");
                Assert.That(reading.Problems,
                    Does.Contain("The authority is stored in a form this plugin cannot read."));
            }
        }

        [Test]
        public void A_version_this_plugin_does_not_know_is_damage()
        {
            // A document some later version wrote. Reading it by guessing at the members would be
            // a gate serving with material it does not understand.
            _values.Plant(AuthorityKeys.AuthorityKey(Unreadable), "{\"v\":\"99\",\"ca\":\"x\",\"host\":\"y\",\"sniKey\":\"z\"}");

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems,
                    Does.Contain("The authority was written in a format version this plugin does not know."));
            }
        }

        [Test]
        public void Everything_written_belongs_to_this_plugin()
        {
            Write();

            Assert.That(_values.Keys.All(AuthorityKeys.IsOurs), Is.True);
        }

        [TestCase(AuthorityDocument.AuthorityPartKey, "The authority certificate is missing.")]
        [TestCase(AuthorityDocument.HostPartKey, "The host certificate is missing.")]
        [TestCase(AuthorityDocument.SniKeyPartKey, "The gate key is missing.")]
        public void A_missing_part_is_damage_rather_than_absence(string part, string expected)
        {
            Write();
            Damage(part, string.Empty);

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Does.Contain(expected));
                Assert.That(reading.Material, Is.Null);
            }
        }

        [TestCase(AuthorityDocument.AuthorityPartKey, "The authority certificate cannot be read.")]
        [TestCase(AuthorityDocument.HostPartKey, "The host certificate cannot be read.")]
        public void An_unreadable_certificate_is_damage(string part, string expected)
        {
            Write();
            Damage(part, "this is not a certificate");

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Does.Contain(expected));
            }
        }

        [Test]
        public void An_unreadable_gate_key_is_damage()
        {
            Write();
            Damage(AuthorityDocument.SniKeyPartKey, "%%%");

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Does.Contain("The gate key cannot be read."));
            }
        }

        [Test]
        public void A_gate_key_of_the_wrong_length_is_damage()
        {
            Write();
            Damage(AuthorityDocument.SniKeyPartKey, Convert.ToBase64String(new byte[8]));

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems.Any(p => p.Contains("8 bytes")), Is.True);
            }
        }

        [Test]
        public void A_host_from_a_different_authority_is_damage()
        {
            // The one shape that would otherwise look perfectly healthy and serve nothing.
            Write();
            using (var foreign = _issuer.CreateAuthority())
            using (var foreignHost = _issuer.IssueHost(foreign))
            {
                Damage(AuthorityDocument.HostPartKey, CertificateBlob.Save(foreignHost));
            }

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Does.Contain("The host certificate was not issued by this authority."));
            }
        }

        [Test]
        public void Admission_records_are_counted_whatever_state_the_authority_is_in()
        {
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.Plant(AuthorityKeys.ClientPrefix + "bbb", "record");

            using (var absent = _store.Read())
            {
                Assert.That(absent.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(absent.ClientRecordCount, Is.EqualTo(2));
            }

            Write();
            using (var present = _store.Read())
            {
                Assert.That(present.ClientRecordCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void Deleting_takes_the_admission_records_with_it()
        {
            Write();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.Plant(AuthorityKeys.ClientPrefix + "bbb", "record");

            var removed = _store.DeleteEverything();

            Assert.That(removed, Is.EqualTo(2));
            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
                Assert.That(reading.ClientRecordCount, Is.Zero);
            }
        }

        [Test]
        public void Deleting_takes_the_revocations_and_the_records_they_emptied_with_it()
        {
            // The retirement refuses every certificate the deleted authority issued, revoked or
            // not, so a revocation beside it refuses nothing more.
            var written = Write();
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('a', 64)), "record");
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('b', 64)), string.Empty);
            _values.Plant(AuthorityKeys.RevocationKey(new string('b', 64)), "2999-01-01T00:00:00Z");
            _values.Plant(AuthorityKeys.RevocationKey(new string('c', 64)), string.Empty);

            var removed = _store.DeleteEverything();

            Assert.That(removed, Is.EqualTo(1), "a revoked client was not a client to remove");
            Assert.That(_values.Keys, Is.EqualTo(new[] { AuthorityKeys.RetirementKey(written.Fingerprint) }),
                "the retirement is all that is left");
        }

        [Test]
        public void Deleting_leaves_what_belongs_to_somebody_else_alone()
        {
            _values.Plant("AnotherPlugin.Something", "not ours");
            _values.Plant("SomeOtherPlugin", "not ours either");

            var written = Write();
            _store.DeleteEverything();

            Assert.That(_values.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EqualTo(new[]
                {
                    "AnotherPlugin.Something",
                    AuthorityKeys.RetirementKey(written.Fingerprint),
                    "SomeOtherPlugin"
                }),
                "the retirement stays, and is the only thing of this plugin's that does");
        }

        [Test]
        public void Deleting_nothing_is_harmless()
        {
            Assert.That(_store.DeleteEverything(), Is.Zero);
        }

        [Test]
        public void A_store_that_swallows_writes_is_read_back_as_absent()
        {
            // What a stale dialog snapshot does, in the small: the write appears to work.
            _values.SwallowWrites = true;
            Write();

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }
        }

        [Test]
        public void Reading_twice_gives_two_usable_copies()
        {
            Write();

            using (var first = _store.Read())
            using (var second = _store.Read())
            {
                Assert.That(first.Material.Authority.HasPrivateKey, Is.True);
                Assert.That(second.Material.Authority.HasPrivateKey, Is.True);
                Assert.That(first.Material.Authority, Is.Not.SameAs(second.Material.Authority));
            }
        }

        [Test]
        public void The_gate_key_handed_out_is_a_copy()
        {
            Write();

            using (var reading = _store.Read())
            {
                var first = reading.Material.SniKey;
                Array.Clear(first, 0, first.Length);

                Assert.That(reading.Material.SniKey, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Letting_the_material_go_overwrites_the_two_secrets_that_can_be_overwritten()
        {
            // What `CA.md` §208 means by the normal host key being let go at lock. Two of the
            // three really are erased, and this is the pair: the gate key and the stored PKCS#12
            // the host certificate and its key travel in. The key the TLS server signs with is
            // neither of these and cannot be erased at all, which is why the section says
            // dereferenced rather than zeroed and why this test does not claim otherwise.
            Write();
            var reading = _store.Read();
            Assume.That(reading.Material.HostPkcs12.Any(b => b != 0), Is.True);
            Assume.That(reading.Material.SniKey.Any(b => b != 0), Is.True);

            reading.Dispose();

            Assert.That(reading.Material.SniKey.Any(b => b != 0), Is.False);
            Assert.That(reading.Material.HostPkcs12.Any(b => b != 0), Is.False);
        }

        [Test]
        public void Two_authorities_nobody_retired_are_a_choice_rather_than_either_one()
        {
            var first = Write();
            var second = Write();

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                Assert.That(reading.IsPresent, Is.False, "nothing is served while this is true");
                Assert.That(reading.Material, Is.Null);
                Assert.That(reading.Candidates.Select(c => c.Fingerprint),
                    Is.EqualTo(new[] { first.Fingerprint, second.Fingerprint }.OrderBy(f => f, StringComparer.Ordinal)),
                    "every candidate, in the order of its digest, so two reads show the same list");
                Assert.That(reading.Candidates.All(c => c.IsWhole), Is.True);
            }
        }

        [Test]
        public void Three_are_three_candidates()
        {
            Write();
            Write();
            Write();

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                Assert.That(reading.Candidates.Count, Is.EqualTo(3));
            }
        }

        [Test]
        public void A_retired_document_is_not_counted_whether_or_not_it_is_still_there()
        {
            // Straight after a merge the document a retirement names can be back beside it. The
            // decision reads the retirement, so it serves the one authority either way.
            var old = Write();
            var current = Write();
            _values.Plant(AuthorityKeys.RetirementKey(old.Fingerprint), AuthorityKeys.RetiredValue);

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(current.Fingerprint));
            }

            _store.Prune();

            using (var reading = _store.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(current.Fingerprint));
            }
        }

        [Test]
        public void Only_retired_documents_are_no_authority_at_all()
        {
            var old = Write();
            _values.Plant(AuthorityKeys.RetirementKey(old.Fingerprint), AuthorityKeys.RetiredValue);

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }
        }

        [Test]
        public void A_retirement_with_nothing_beside_it_is_no_authority_either()
        {
            _values.Plant(AuthorityKeys.RetirementKey(Unreadable), AuthorityKeys.RetiredValue);

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }
        }

        [Test]
        public void Keeping_one_retires_the_others_and_takes_their_documents_away()
        {
            var kept = Write();
            var lost = Write();
            var alsoLost = Write();

            Assert.That(_store.Keep(kept.Fingerprint), Is.EqualTo(2));

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(kept.Fingerprint));
            }

            Assert.That(_values.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(new[]
            {
                AuthorityKeys.AuthorityKey(kept.Fingerprint),
                AuthorityKeys.RetirementKey(lost.Fingerprint),
                AuthorityKeys.RetirementKey(alsoLost.Fingerprint)
            }.OrderBy(k => k, StringComparer.Ordinal)));
            Assert.That(_values.Get(AuthorityKeys.RetirementKey(lost.Fingerprint)), Is.EqualTo(AuthorityKeys.RetiredValue));
        }

        [Test]
        public void Keeping_leaves_the_admission_records_where_they_are()
        {
            // The records a lost authority issued chain to nothing that is served, which is what
            // makes them inert; the clients page says so, and taking them away is the operator's.
            var kept = Write();
            Write();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");

            _store.Keep(kept.Fingerprint);

            Assert.That(_values.Get(AuthorityKeys.ClientPrefix + "aaa"), Is.EqualTo("record"));
        }

        [Test]
        public void Keeping_something_that_is_not_a_choice_is_refused_and_changes_nothing()
        {
            var first = Write();
            var second = Write();
            var retired = Write();
            _values.Plant(AuthorityKeys.RetirementKey(retired.Fingerprint), AuthorityKeys.RetiredValue);
            var before = _values.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.Throws<AuthorityFailure>(() => _store.Keep(retired.Fingerprint), "one already retired");
            Assert.Throws<AuthorityFailure>(() => _store.Keep(Unreadable), "one that is not there");
            Assert.Throws<AuthorityFailure>(() => _store.Keep(null));

            Assert.That(_values.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(before));

            using (var reading = _store.Read())
            {
                Assert.That(reading.Candidates.Select(c => c.Fingerprint),
                    Is.EquivalentTo(new[] { first.Fingerprint, second.Fingerprint }));
            }
        }

        [Test]
        public void A_candidate_that_will_not_read_can_be_retired_and_never_kept()
        {
            var whole = Write();
            _values.Plant(AuthorityKeys.AuthorityKey(Unreadable), "this is not a document");

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                var damaged = reading.Candidates.Single(c => c.Fingerprint == Unreadable);
                Assert.That(damaged.IsWhole, Is.False);
                Assert.That(damaged.Problems, Is.Not.Empty);
            }

            Assert.Throws<AuthorityFailure>(() => _store.Keep(Unreadable));

            _store.Keep(whole.Fingerprint);
            Assert.That(_values.Get(AuthorityKeys.RetirementKey(Unreadable)), Is.EqualTo(AuthorityKeys.RetiredValue));
            Assert.That(_values.Get(AuthorityKeys.AuthorityKey(Unreadable)), Is.Null);
        }

        [Test]
        public void An_authority_filed_under_somebody_else_s_digest_is_damage()
        {
            // The key is what a retirement names, so a document under the wrong one is an
            // authority that retiring could never reach.
            var written = Write();
            var document = _values.Get(AuthorityKeys.AuthorityKey(written.Fingerprint));
            _values.Remove(AuthorityKeys.AuthorityKey(written.Fingerprint));
            _values.Plant(AuthorityKeys.AuthorityKey(Unreadable), document);

            using (var reading = _store.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Does.Contain("The authority is filed under a digest that is not its own."));
            }
        }

        [Test]
        public void Deleting_retires_every_authority_there_is_including_one_that_will_not_read()
        {
            var first = Write();
            var second = Write();
            _values.Plant(AuthorityKeys.AuthorityKey(Unreadable), "this is not a document");

            _store.DeleteEverything();

            Assert.That(_values.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(new[]
            {
                AuthorityKeys.RetirementKey(first.Fingerprint),
                AuthorityKeys.RetirementKey(second.Fingerprint),
                AuthorityKeys.RetirementKey(Unreadable)
            }.OrderBy(k => k, StringComparer.Ordinal)));
        }

        [Test]
        public void Pruning_takes_only_retired_documents_and_says_what_it_took()
        {
            var old = Write();
            var current = Write();
            var oldDocument = _values.Get(AuthorityKeys.AuthorityKey(old.Fingerprint));
            _values.Plant(AuthorityKeys.RetirementKey(old.Fingerprint), AuthorityKeys.RetiredValue);

            var pruned = _store.Prune();

            Assert.That(pruned.Select(p => p.Key), Is.EqualTo(new[] { AuthorityKeys.AuthorityKey(old.Fingerprint) }));
            Assert.That(pruned.Single().Value, Is.EqualTo(oldDocument), "so that a write that fails can put it back");
            Assert.That(_values.Get(AuthorityKeys.AuthorityKey(current.Fingerprint)), Is.Not.Null);
            Assert.That(_store.Prune(), Is.Empty, "and a second prune finds nothing");
        }

        [Test]
        public void Pruning_takes_a_record_that_came_back_beside_its_revocation_as_well()
        {
            // The one prune every write of this plugin's rides on, pairing and keeping included,
            // so a record a merge brought back goes with the next of them.
            Write();
            var cameBack = AuthorityKeys.ClientRecordKey(new string('d', 64));
            var client = AuthorityKeys.ClientRecordKey(new string('c', 64));
            _values.Plant(cameBack, "a record that came back");
            _values.Plant(AuthorityKeys.RevocationKey(new string('d', 64)), "2999-01-01T00:00:00Z");
            _values.Plant(client, "a record");

            var pruned = _store.Prune();

            Assert.That(pruned.Select(p => p.Key), Is.EqualTo(new[] { cameBack }));
            Assert.That(pruned.Single().Value, Is.EqualTo("a record that came back"), "so that a write that fails can put it back");
            Assert.That(_values.Get(client), Is.EqualTo("a record"));
        }

        [Test]
        public void A_document_is_never_written_twice()
        {
            using (var authority = _issuer.CreateAuthority())
            using (var host = _issuer.IssueHost(authority))
            {
                using (var material = new AuthorityMaterial(Copy(authority), Copy(host), new byte[AuthorityMaterial.SniKeyLength]))
                {
                    _store.Write(material);
                    Assert.Throws<AuthorityFailure>(() => _store.Write(material));
                }
            }
        }

        [Test]
        public void What_decides_the_authority_is_read_as_one_text_that_changes_with_any_of_it()
        {
            var first = Write();
            var before = _store.Stored();

            Assert.That(_store.Stored(), Is.EqualTo(before), "reading twice reads the same");

            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            Assert.That(_store.Stored(), Is.EqualTo(before), "a record decides nothing about the authority");

            _values.Plant(AuthorityKeys.RetirementKey(first.Fingerprint), AuthorityKeys.RetiredValue);
            Assert.That(_store.Stored(), Is.Not.EqualTo(before), "a retirement does");
        }

        /// <summary>A digest no authority here has, for a document that will not read.</summary>
        private static readonly string Unreadable = new string('d', 64);

        private WrittenAuthority Write()
        {
            using (var authority = _issuer.CreateAuthority())
            using (var host = _issuer.IssueHost(authority))
            {
                var sniKey = new byte[AuthorityMaterial.SniKeyLength];
                for (var i = 0; i < sniKey.Length; i++)
                {
                    sniKey[i] = (byte)(i + 1);
                }

                using (var material = new AuthorityMaterial(Copy(authority), Copy(host), sniKey))
                {
                    _store.Write(material);
                }

                return new WrittenAuthority
                {
                    Fingerprint = CertificateDigest.Of(authority),
                    Authority = authority.RawData,
                    Host = host.RawData,
                    SniKey = sniKey
                };
            }
        }

        /// <summary>
        /// Rewrites one part of the stored authority, which is how a part is damaged now that the
        /// three of them are members of one document rather than three keys.
        /// </summary>
        private void Damage(string part, string value)
        {
            var key = _values.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            var body = JsonBody.Read(_values.Get(key));
            Assume.That(body, Is.Not.Null, "the authority has to be written before it can be damaged");

            var authority = body.Text(AuthorityDocument.AuthorityPartKey);
            var host = body.Text(AuthorityDocument.HostPartKey);
            var sniKey = body.Text(AuthorityDocument.SniKeyPartKey);

            if (part == AuthorityDocument.AuthorityPartKey)
            {
                authority = value;
            }
            else if (part == AuthorityDocument.HostPartKey)
            {
                host = value;
            }
            else
            {
                sniKey = value;
            }

            _values.Plant(key, AuthorityDocument.Save(authority, host, sniKey));
        }

        private static X509Certificate2 Copy(X509Certificate2 certificate)
        {
            return CertificateBlob.Load(CertificateBlob.Save(certificate));
        }

        private sealed class WrittenAuthority
        {
            public string Fingerprint { get; set; }

            public byte[] Authority { get; set; }

            public byte[] Host { get; set; }

            public byte[] SniKey { get; set; }
        }
    }
}
