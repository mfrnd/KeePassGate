using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The admission that decides whether a certificate is worth anything, `STORE.md` §615: its
    /// members as a record of kind `client` holds them, written and read back. Reading an
    /// admission has to be total: anything that is not exactly an admission admits nobody.
    /// </summary>
    [TestFixture]
    public sealed class AdmissionRecordTests
    {
        private static readonly DateTimeOffset Admitted = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        private static readonly string Issuer = new string('b', 64);

        private TestAuthority _authority;
        private CertifiedKey _issued;
        private X509Certificate2 _certificate;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _issued = _authority.IssueClientKey("agent-fictitious", "ops");
            _certificate = _issued.Read();
        }

        [TearDown]
        public void TearDown()
        {
            _certificate.Reset();
            _authority.Dispose();
        }

        [Test]
        public void An_admission_is_keyed_on_the_certificates_digest_and_names_its_issuer()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer);

            Assert.That(record.AdmissionKey, Is.EqualTo(CertificateDigest.Of(_certificate)));
            Assert.That(record.Issuer, Is.EqualTo(Issuer));
            Assert.That(record.From, Is.Null, "a pairing's names no certificate it was renewed from");
            Assert.That(record.Entry, Is.Null, "composed, and not read from an entry");
        }

        [Test]
        public void It_carries_the_certificate_so_expiry_is_knowable_later()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, null, Issuer);

            using (var carried = record.Certificate())
            {
                Assert.That(carried.RawData, Is.EqualTo(_certificate.RawData));
                Assert.That(carried.NotAfter, Is.EqualTo(_certificate.NotAfter));
                Assert.That(carried.HasPrivateKey, Is.False, "an admission has no business holding a key");
            }
        }

        [Test]
        public void An_admission_carrying_a_pkcs12_rather_than_a_certificate_cannot_be_built()
        {
            // `X509Certificate2(byte[])` sniffs the blob, so a password-less PFX loads and reports
            // a `RawData` of the certificate inside it.
            var pfx = _issued.ToPkcs12();

            Assert.That(pfx, Is.Not.EqualTo(_certificate.RawData));
            Assert.Throws<ArgumentException>(() => new AdmissionRecord(
                CertificateDigest.Of(pfx), "CN=agent-fictitious", CertificateDigest.Of(pfx), Admitted, new[] { "ops" }, pfx, Issuer));
        }

        [Test]
        public void Its_members_are_those_of_the_table_and_read_back_as_they_were_written()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "readonly", "ops" }, Issuer);

            var members = record.Members();

            Assert.That(members.Keys, Is.EquivalentTo(new[] { "certificate", "der", "client", "issuer", "profiles", "subject", "admitted" }));
            Assert.That(members["certificate"], Is.EqualTo(record.AdmissionKey));
            Assert.That(members["der"], Is.EqualTo(Convert.ToBase64String(_certificate.RawData)));
            Assert.That(members["client"], Is.EqualTo(record.AdmissionKey), "a paired client is known by its own certificate");
            Assert.That(members["issuer"], Is.EqualTo(Issuer));
            Assert.That(members["profiles"], Is.EqualTo("ops readonly"), "each once, in ordinal order");
            Assert.That(members["subject"], Is.EqualTo("CN=agent-fictitious"));
            Assert.That(members["admitted"], Is.EqualTo("2026-03-04T05:06:07Z"));

            var read = AdmissionRecord.Read("0011", members);

            Assert.That(read, Is.Not.Null);
            Assert.That(read.Entry, Is.EqualTo("0011"));
            Assert.That(read.Members(), Is.EquivalentTo(members));
            Assert.That(read.Admitted, Is.EqualTo(Admitted));
            Assert.That(read.Profiles, Is.EqualTo(new[] { "ops", "readonly" }));
            using (var carried = read.Certificate())
            {
                Assert.That(carried.RawData, Is.EqualTo(_certificate.RawData));
            }
        }

        [Test]
        public void A_renewal_keeps_the_client_key_it_was_given_and_names_its_origin()
        {
            var first = new string('c', 64);
            var origin = new string('d', 64);
            var record = AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer, first, origin);

            Assert.That(record.ClientKey, Is.EqualTo(first));
            Assert.That(record.Members()["from"], Is.EqualTo(origin));

            var read = AdmissionRecord.Read(null, record.Members());
            Assert.That(read.ClientKey, Is.EqualTo(first));
            Assert.That(read.From, Is.EqualTo(origin));
        }

        [Test]
        public void The_date_is_written_the_same_way_in_every_language()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var record = AdmissionRecord.For(_certificate, Admitted, null, Issuer);

                Assert.That(record.Members()["admitted"], Is.EqualTo("2026-03-04T05:06:07Z"));
                Assert.That(AdmissionRecord.Read(null, record.Members()).Admitted, Is.EqualTo(Admitted));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void An_admission_with_no_profiles_is_a_client_that_can_reach_nothing()
        {
            var record = AdmissionRecord.For(_certificate, Admitted, null, Issuer);

            Assert.That(record.Members()["profiles"], Is.Empty);
            Assert.That(AdmissionRecord.Read(null, record.Members()).Profiles, Is.Empty);
        }

        [Test]
        public void Profile_names_that_could_not_have_been_issued_are_dropped_and_the_rest_read_as_a_set()
        {
            // An admission edited by hand into something strange grants nothing strange.
            var members = Members();
            members["profiles"] = "ops default with  ops readonly";

            Assert.That(AdmissionRecord.Read(null, members).Profiles, Is.EqualTo(new[] { "ops", "readonly", "with" }));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")]
        [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
        public void A_key_that_is_not_a_digest_is_refused_both_ways(string key)
        {
            Assert.Throws<ArgumentException>(() => AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer, key, null));
            Assert.Throws<ArgumentException>(() => AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, key));
            if (key != null)
            {
                Assert.Throws<ArgumentException>(
                    () => AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer, CertificateDigest.Of(_certificate), key));
            }

            foreach (var member in new[] { "client", "issuer", "certificate", "from" })
            {
                var members = Members();
                if (key == null)
                {
                    members.Remove(member);
                }
                else
                {
                    members[member] = key;
                }

                Assert.That(AdmissionRecord.Read(null, members), member == "from" && key == null ? Is.Not.Null : Is.Null, member);
            }
        }

        [TestCase("der")]
        [TestCase("client")]
        [TestCase("issuer")]
        [TestCase("profiles")]
        [TestCase("subject")]
        [TestCase("admitted")]
        [TestCase("certificate")]
        public void An_admission_missing_any_member_but_its_origin_reads_as_none(string member)
        {
            var members = Members();
            members.Remove(member);

            Assert.That(AdmissionRecord.Read(null, members), Is.Null);
        }

        [TestCase("admitted", "yesterday")]
        [TestCase("admitted", "2026-03-04")]
        [TestCase("admitted", "2026-03-04T05:06:07.000Z")]
        [TestCase("admitted", "2026-03-04T05:06:07+01:00")]
        [TestCase("der", "not base64")]
        [TestCase("der", "")]
        public void A_member_out_of_its_form_reads_as_none(string member, string value)
        {
            var members = Members();
            members[member] = value;

            Assert.That(AdmissionRecord.Read(null, members), Is.Null);
        }

        [Test]
        public void An_admission_whose_certificate_is_not_the_one_its_key_names_reads_as_none()
        {
            // Two things stapled together rather than an admission, and a PKCS#12 carried in place
            // of a bare certificate.
            using (var other = _authority.IssueClient("agent-invented"))
            {
                var members = Members();
                members["der"] = Convert.ToBase64String(other.RawData);
                Assert.That(AdmissionRecord.Read(null, members), Is.Null);
            }

            // The PKCS#12 keyed by its own digest, so that the digest matches and what refuses it
            // is the bare certificate a `der` has to be.
            var pkcs12 = _issued.ToPkcs12();
            var pfx = Members();
            pfx["der"] = Convert.ToBase64String(pkcs12);
            pfx["certificate"] = CertificateDigest.Of(pkcs12);
            Assert.That(AdmissionRecord.Read(null, pfx), Is.Null);
        }

        [Test]
        [NonParallelizable]
        public void Reading_an_admission_that_carries_a_private_key_writes_no_key_to_the_disk()
        {
            // Locked against every other test process this account runs, so that a key any of
            // them writes never lands inside this window: `PrivateKeyFiles.Lock` gives the reason.
            using (PrivateKeyFiles.Lock())
            {
                Reading_an_admission_that_carries_a_private_key_writes_no_key_to_the_disk_while_locked();
            }
        }

        private void Reading_an_admission_that_carries_a_private_key_writes_no_key_to_the_disk_while_locked()
        {
            // The PKCS#12 a `.kdbx` could carry in `der` has a key in it, and parsing it without
            // `EphemeralKeySet` writes that key into the operator's profile, from bytes the file
            // chose. Every listing and every hold reads admissions. The parse is inside a `using`,
            // which deletes the file again before `Read` returns, so comparing the key files before
            // and after sees nothing either way. A watcher sees a key arrive.
            //
            // **Asked more than once, because the directories are not ours.** They belong to the
            // whole logon session, so a key any other process writes inside the window, the other
            // test assembly `dotnet test` runs beside this assembly among them, reads exactly like
            // a regression. A parse that writes a key writes a key every time, so an attempt that
            // sees nothing acquits it, and only every attempt seeing something accuses it. A quiet
            // moment afterwards then tells a machine writing keys throughout from the parse.
            var pkcs12 = _issued.ToPkcs12();
            var members = Members();
            members["der"] = Convert.ToBase64String(pkcs12);
            members["certificate"] = CertificateDigest.Of(pkcs12);
            var seen = new List<string>();

            for (var attempt = 0; attempt < KeyAttempts; attempt++)
            {
                var created = PrivateKeyFiles.CreatedDuring(
                    () => Assert.That(AdmissionRecord.Read(null, members), Is.Null));

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
                    "cannot tell whether reading the admission wrote a key");
            }

            Assert.Fail("a private key reached the disk on every attempt: " + string.Join(", ", seen));
        }

        /// <summary>
        /// How many parses in a row have to write a key before the parse is accused.
        /// </summary>
        private const int KeyAttempts = 3;

        /// <summary>
        /// How long nothing is done while the key directories are watched for anybody else.
        /// </summary>
        private static readonly TimeSpan KeyQuietWindow = TimeSpan.FromMilliseconds(250);

        [Test]
        public void What_an_admission_grants_cannot_be_added_to_by_whoever_holds_it()
        {
            // `IReadOnlyList<T>` is a view of a list, not a copy of a list, and the profiles are
            // the member of an admission that grants anything.
            var read = AdmissionRecord.Read(null, Members());

            Assert.That(read.Profiles as List<string>, Is.Null);
            Assert.That(read.Profiles, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<string>>());
            Assert.That(AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer).Profiles,
                Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<string>>());
        }

        [Test]
        public void A_member_the_table_does_not_name_is_passed_over()
        {
            var members = Members();
            members["note"] = "anything";

            Assert.That(AdmissionRecord.Read(null, members), Is.Not.Null);
        }

        [Test]
        public void Nothing_at_all_reads_as_none()
        {
            Assert.That(AdmissionRecord.Read(null, null), Is.Null);
            Assert.That(AdmissionRecord.Read(null, new Dictionary<string, string>()), Is.Null);
        }

        /// <summary>The members of a pairing's admission of the certificate, to change.</summary>
        private Dictionary<string, string> Members()
        {
            return AdmissionRecord.For(_certificate, Admitted, new[] { "ops" }, Issuer)
                .Members()
                .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
        }
    }
}
