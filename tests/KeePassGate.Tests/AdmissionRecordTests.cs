using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The record that decides whether a certificate is worth anything. Reading one has to be
    /// total: anything that is not exactly a record admits nobody.
    /// </summary>
    [TestFixture]
    public sealed class AdmissionRecordTests
    {
        private static readonly DateTimeOffset Admitted = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private TestAuthority _authority;
        private X509Certificate2 _certificate;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _certificate = _authority.IssueClient("agent-fictitious", "ops");
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();
        }

        [Test]
        public void A_record_is_keyed_on_the_certificates_digest()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(record.AdmissionKey, Is.EqualTo(CertificateDigest.Of(_certificate)));
            Assert.That(record.StorageKey, Is.EqualTo(AuthorityKeys.ClientPrefix + record.AdmissionKey));
        }

        [Test]
        public void It_carries_the_certificate_so_expiry_is_knowable_later()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, null);

            using (var carried = record.Certificate())
            {
                Assert.That(carried.RawData, Is.EqualTo(_certificate.RawData));
                Assert.That(carried.NotAfter, Is.EqualTo(_certificate.NotAfter));
                Assert.That(carried.HasPrivateKey, Is.False, "a record has no business holding a key");
            }
        }

        [Test]
        public void A_field_this_reader_does_not_know_is_refused()
        {
            // Not skipped. The version marker is there so a later writer can add a field that
            // narrows what a record grants; a reader that ignored it would keep admitting on the
            // strength of the fields it did recognize.
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save()), Is.Not.Null);
            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save() + "\nnote=x"), Is.Null);
        }

        [Test]
        public void A_record_carrying_a_pkcs12_rather_than_a_certificate_cannot_be_built()
        {
            // `X509Certificate2(byte[])` sniffs the blob, so a password-less PFX loads and reports
            // a `RawData` of the certificate inside it. A record keyed on the digest of the PFX
            // therefore used to construct, store, and read back as nothing for ever after.
            var pfx = _certificate.Export(X509ContentType.Pfx);

            Assert.That(pfx, Is.Not.EqualTo(_certificate.RawData));
            Assert.Throws<ArgumentException>(() => new AdmissionRecord(
                CertificateDigest.Of(pfx), "CN=agent-fictitious", Admitted, new[] { "ops" }, pfx));
        }

        [Test]
        public void It_survives_the_round_trip()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops", "readonly" });

            var read = AdmissionRecord.Load(record.AdmissionKey, record.Save());

            Assert.That(read, Is.Not.Null);
            Assert.That(read.AdmissionKey, Is.EqualTo(record.AdmissionKey));
            Assert.That(read.Subject, Is.EqualTo("CN=agent-fictitious"));
            Assert.That(read.Admitted, Is.EqualTo(Admitted));
            Assert.That(read.Profiles, Is.EqualTo(new[] { "ops", "readonly" }));
            Assert.That(read.CertificateDer, Is.EqualTo(_certificate.RawData));
        }

        [Test]
        public void The_stored_form_is_text_a_kdbx_can_hold()
        {
            var stored = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }).Save();

            Assert.That(stored, Does.StartWith("v=1"));
            Assert.That(stored, Does.Not.Contain("\0"));
            foreach (var line in stored.Split('\n').Where(l => l.Length > 0))
            {
                Assert.That(line, Does.Contain("="));
            }
        }

        [Test]
        public void The_date_is_written_the_same_way_in_every_language()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var record = AdmissionRecord.For(_certificate, Admitted, null);

                Assert.That(record.Save(), Does.Contain("admitted=2026-03-04T05:06:07Z"));
                Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save()).Admitted, Is.EqualTo(Admitted));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void A_record_with_no_profiles_is_a_client_that_can_reach_nothing()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, null);

            Assert.That(record.Profiles, Is.Empty);
            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save()).Profiles, Is.Empty);
        }

        [Test]
        public void Profile_names_that_could_not_have_been_issued_are_dropped()
        {
            // A record edited by hand into something strange grants nothing strange.
            var record = new AdmissionRecord(
                CertificateDigest.Of(_certificate),
                "CN=agent-fictitious",
                Admitted,
                new[] { "ops", "default", "with space", string.Empty, "ops" },
                _certificate.RawData);

            Assert.That(record.Profiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_record_whose_certificate_does_not_match_its_key_is_refused()
        {
            // Two things stapled together rather than a record.
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });
            var somebodyElse = new string('a', 64);

            Assert.That(AdmissionRecord.Load(somebodyElse, record.Save()), Is.Null);
        }

        [TestCase("v")]
        [TestCase("subject")]
        [TestCase("admitted")]
        [TestCase("profiles")]
        [TestCase("certificate")]
        public void A_record_missing_any_field_is_refused(string field)
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });
            var without = string.Join("\n",
                record.Save().Split('\n').Where(line => !line.StartsWith(field + "=", StringComparison.Ordinal)));

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, without), Is.Null);
        }

        [Test]
        public void A_record_from_a_version_we_do_not_know_is_refused()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save().Replace("v=1", "v=2")), Is.Null);
        }

        [Test]
        public void A_record_with_a_field_twice_is_refused()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save() + "profiles=admin\n"), Is.Null);
        }

        [TestCase("admitted=yesterday")]
        [TestCase("admitted=2026-03-04")]
        [TestCase("certificate=not base64")]
        [TestCase("certificate=")]
        public void A_field_that_will_not_parse_is_refused(string replacement)
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });
            var name = replacement.Split('=')[0];
            var damaged = string.Join("\n", record.Save().Split('\n')
                .Select(line => line.StartsWith(name + "=", StringComparison.Ordinal) ? replacement : line));

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, damaged), Is.Null);
        }

        [Test]
        public void A_line_that_is_not_a_field_is_refused()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, record.Save() + "nonsense\n"), Is.Null);
            Assert.That(AdmissionRecord.Load(record.AdmissionKey, "=value\n" + record.Save()), Is.Null);
        }

        [Test]
        public void No_truncation_of_a_record_can_produce_a_different_one()
        {
            // The property that matters is not that every prefix is refused: a record missing only
            // its last newline is still that record. It is that no prefix can ever be read as
            // admitting something other than what was written.
            var original = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });
            var stored = original.Save();
            var key = original.AdmissionKey;

            for (var length = 0; length < stored.Length; length++)
            {
                var truncated = stored.Substring(0, length);

                AdmissionRecord read = null;
                Assert.DoesNotThrow(() => read = AdmissionRecord.Load(key, truncated),
                    "threw on a " + length + " character prefix");

                if (read == null)
                {
                    continue;
                }

                Assert.That(read.AdmissionKey, Is.EqualTo(original.AdmissionKey), "at " + length);
                Assert.That(read.CertificateDer, Is.EqualTo(original.CertificateDer), "at " + length);
                Assert.That(read.Profiles, Is.EqualTo(original.Profiles), "at " + length);
                Assert.That(read.Subject, Is.EqualTo(original.Subject), "at " + length);
            }
        }

        [Test]
        public void A_record_missing_only_its_last_newline_is_still_that_record()
        {
            var original = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            var read = AdmissionRecord.Load(original.AdmissionKey, original.Save().TrimEnd('\n'));

            Assert.That(read, Is.Not.Null);
            Assert.That(read.Profiles, Is.EqualTo(original.Profiles));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("rubbish")]
        public void Anything_that_is_not_a_record_at_all_is_refused(string stored)
        {
            Assert.That(AdmissionRecord.Load(CertificateDigest.Of(_certificate), stored), Is.Null);
        }

        [Test]
        public void A_certificate_field_that_is_not_a_bare_certificate_is_refused()
        {
            // Measured: `X509Certificate2(byte[])` sniffs the blob, so a password-less PKCS#12 loads
            // through it and comes back as the leaf inside. A record carrying one would be keyed on
            // the digest of the container while describing something else, and reading it would
            // import a private key from bytes a `.kdbx` chose.
            var pkcs12 = _certificate.Export(X509ContentType.Pkcs12);
            var key = CertificateDigest.Of(pkcs12);
            var stored = Carrying(pkcs12);

            using (var sniffed = new X509Certificate2(pkcs12, (string)null, X509KeyStorageFlags.EphemeralKeySet))
            {
                Assume.That(sniffed.RawData, Is.Not.EqualTo(pkcs12),
                    "this test is about a blob that parses as something other than itself");
            }

            Assert.That(AdmissionRecord.Load(key, stored), Is.Null);
        }

        [Test]
        [NonParallelizable]
        public void Refusing_a_record_that_carries_a_private_key_writes_no_key_to_the_disk()
        {
            // Locked against every other test process this account runs, so that a key one of
            // them writes never lands inside this window: `PrivateKeyFiles.Lock` says why.
            using (PrivateKeyFiles.Lock())
            {
                Refusing_a_record_that_carries_a_private_key_writes_no_key_to_the_disk_while_locked();
            }
        }

        private void Refusing_a_record_that_carries_a_private_key_writes_no_key_to_the_disk_while_locked()
        {
            // The PKCS#12 a `.kdbx` could carry here has a key in it, and parsing one without
            // `EphemeralKeySet` writes that key into the operator's profile, from bytes the file
            // chose. The parse is inside a `using`, which deletes the file again before `Load`
            // returns, so comparing the key files before and after sees nothing either way. A
            // watcher sees one arrive.
            //
            // **Asked more than once, because the directories are not ours.** They belong to the
            // whole logon session, so a key any other process writes inside the window, the other
            // test assembly `dotnet test` runs beside this one among them, reads exactly like a
            // regression: this failed once that way and passed on the rerun. A parse that writes a
            // key writes one every time, so one attempt that sees nothing acquits it, and only
            // every attempt seeing something accuses it. A quiet moment afterwards then tells a
            // machine writing keys throughout from the parse, and says so rather than guessing.
            var pkcs12 = _certificate.Export(X509ContentType.Pkcs12);
            var stored = Carrying(pkcs12);
            var seen = new System.Collections.Generic.List<string>();

            for (var attempt = 0; attempt < KeyAttempts; attempt++)
            {
                var created = PrivateKeyFiles.CreatedDuring(
                    () => Assert.That(AdmissionRecord.Load(CertificateDigest.Of(pkcs12), stored), Is.Null));

                if (created.Length == 0)
                {
                    return;
                }

                seen.AddRange(created);
            }

            if (PrivateKeyFiles.CreatedDuring(() => System.Threading.Thread.Sleep(KeyQuietWindow)).Length > 0)
            {
                Assert.Inconclusive(
                    "something else on this machine is writing private keys throughout, so this run " +
                    "cannot tell whether refusing the record wrote one");
            }

            Assert.Fail("a private key reached the disk on every attempt: " + string.Join(", ", seen));
        }

        /// <summary>How many parses in a row have to write a key before the parse is accused.</summary>
        private const int KeyAttempts = 3;

        /// <summary>How long nothing is done while the key directories are watched for anybody else.</summary>
        private static readonly TimeSpan KeyQuietWindow = TimeSpan.FromMilliseconds(250);

        private static string Carrying(byte[] certificate)
        {
            return string.Join("\n", new[]
            {
                "v=1",
                "subject=CN=agent-fictitious",
                "admitted=2026-03-04T05:06:07Z",
                "profiles=ops",
                "certificate=" + Convert.ToBase64String(certificate)
            });
        }

        [Test]
        public void A_subject_that_the_stored_form_cannot_hold_is_refused_both_ways()
        {
            // The format is one field per line, so a newline in a subject writes a record that reads
            // back as a different one or as none. Refused where the record is made, rather than
            // relied on being kept out by whoever issued the certificate.
            Assert.Throws<ArgumentException>(() => new AdmissionRecord(
                CertificateDigest.Of(_certificate),
                "CN=agent\nprofiles=admin",
                Admitted,
                new[] { "ops" },
                _certificate.RawData));

            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });
            var injected = record.Save().Replace(
                "subject=CN=agent-fictitious", "subject=CN=agent\rnote=x");

            Assert.That(AdmissionRecord.Load(record.AdmissionKey, injected), Is.Null);
        }

        [Test]
        public void A_record_keyed_on_something_other_than_its_certificate_cannot_be_made()
        {
            // The other half of `A_record_whose_certificate_does_not_match_its_key_is_refused`.
            // Refusing it only on the way in would let pairing write a record, report success, and
            // leave a client that is admitted by nothing and revocable by nobody.
            Assert.Throws<ArgumentException>(() => new AdmissionRecord(
                new string('a', 64), "CN=agent-fictitious", Admitted, null, _certificate.RawData));
        }

        [Test]
        public void What_it_grants_cannot_be_added_to_by_whoever_holds_it()
        {
            // `IReadOnlyList<T>` is a view of a list, not a copy of one, and this is the one field
            // in a record that grants anything.
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" });

            Assert.That(record.Profiles as System.Collections.Generic.List<string>, Is.Null);
            Assert.That(record.Profiles, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<string>>());
        }

        [Test]
        public void A_record_without_the_things_that_make_it_one_is_refused_at_construction()
        {
            Assert.Throws<ArgumentException>(() =>
                new AdmissionRecord(null, "CN=x", Admitted, null, _certificate.RawData));
            Assert.Throws<ArgumentException>(() =>
                new AdmissionRecord("aa", "CN=x", Admitted, null, null));
            Assert.Throws<ArgumentException>(() =>
                new AdmissionRecord("aa", "CN=x", Admitted, null, Array.Empty<byte>()));
            Assert.Throws<ArgumentNullException>(() => AdmissionRecord.For(null, Admitted, null));
        }
    }
}
