using KeePassGate.Transport;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
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

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _store = new AuthorityStore(_values);
            _issuer = new AuthorityIssuer();
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
            Write();

            Assert.That(_values.Keys, Is.EqualTo(new[] { AuthorityKeys.Authority }),
                "three parts, one key, so a merge cannot take them from different replicas");
        }

        [Test]
        public void A_document_that_will_not_read_is_damage_rather_than_absence()
        {
            _values.Plant(AuthorityKeys.Authority, "this is not a document");

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
            _values.Plant(AuthorityKeys.Authority, "{\"v\":\"99\",\"ca\":\"x\",\"host\":\"y\",\"sniKey\":\"z\"}");

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
        public void Deleting_leaves_what_belongs_to_somebody_else_alone()
        {
            Write();
            _values.Plant("AnotherPlugin.Something", "not ours");
            _values.Plant("SomeOtherPlugin", "not ours either");

            _store.DeleteEverything();

            Assert.That(_values.Keys.OrderBy(k => k),
                Is.EqualTo(new[] { "AnotherPlugin.Something", "SomeOtherPlugin" }));
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
            var body = JsonBody.Read(_values.Get(AuthorityKeys.Authority));
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

            _values.Plant(AuthorityKeys.Authority, AuthorityDocument.Save(authority, host, sniKey));
        }

        private static X509Certificate2 Copy(X509Certificate2 certificate)
        {
            return CertificateBlob.Load(CertificateBlob.Save(certificate));
        }

        private sealed class WrittenAuthority
        {
            public byte[] Authority { get; set; }

            public byte[] Host { get; set; }

            public byte[] SniKey { get; set; }
        }
    }
}
