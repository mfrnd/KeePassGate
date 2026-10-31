using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using KeePassGate.Audit;
using KeePassGate.Diagnostics;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a decision looks like once it is written down.
    /// </summary>
    /// <remarks>
    /// Half of these are about what is not in a line. A log of titles is an inventory of the
    /// database sitting beside the database, so the tests that matter are the ones asserting that
    /// nothing a caller sent and nothing a database holds reaches the file.
    /// </remarks>
    [TestFixture]
    public sealed class AuditEntryTests
    {
        [Test]
        public void A_decision_is_one_flat_object_with_every_member_present()
        {
            var line = Entry(true, "a rule here grants it").Line();

            Assert.That(line, Is.EqualTo(
                "{\"at\":\"2026-03-04T05:06:07Z\"," +
                "\"database\":\"d1e2f3\"," +
                "\"admissionKey\":\"9f86d081\"," +
                "\"clientKey\":\"c0ffee11\"," +
                "\"operation\":\"readEntry\"," +
                "\"node\":\"7b1c\"," +
                "\"decidedAt\":\"3a9f\"," +
                "\"decision\":\"permitted\"," +
                "\"reason\":\"a rule here grants it\"}"));
        }

        [Test]
        public void A_refusal_a_removed_profile_caused_ends_with_the_names_that_would_have_permitted_it()
        {
            var line = new AuditEntry(
                At, "d1e2f3", "9f86d081", "c0ffee11", "readEntry", "7b1c", "3a9f", false, "profile removed",
                new[] { "ops" }).Line();

            Assert.That(line, Does.EndWith(
                "\"decision\":\"refused\"," +
                "\"reason\":\"profile removed\"," +
                "\"removedProfiles\":[\"ops\"]}"));

            // Still a line this plugin's own reader takes, being strings and a list of them.
            Assert.That(JsonBody.Read(line).Array("removedProfiles"), Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void It_reads_back_as_the_object_it_claims_to_be()
        {
            // Through this plugin's own reader, which refuses anything that is not one flat object
            // of strings. That it reads is worth asserting: a line nobody can parse is a line
            // nobody will look at twice.
            var body = JsonBody.Read(Entry(false, "a rule here does not grant it").Line());

            Assert.That(body, Is.Not.Null);
            Assert.That(body.Text("operation"), Is.EqualTo("readEntry"));
            Assert.That(body.Text("node"), Is.EqualTo("7b1c"));
            Assert.That(body.Text("decidedAt"), Is.EqualTo("3a9f"));
            Assert.That(body.Text("reason"), Is.EqualTo("a rule here does not grant it"));
            Assert.That(body.Text("decision"), Is.EqualTo(AuditEntry.Refused));
        }

        [Test]
        public void A_question_that_did_not_arise_is_null_rather_than_missing()
        {
            // A reader that has to tell "absent" from "not applicable" will guess. An explicit null
            // says the question did not arise; a missing member says the log was written by
            // something else.
            var line = new AuditEntry(
                At, "d1e2f3", null, null, "getStatus", null, null, true, "nothing to decide").Line();

            Assert.That(line, Does.Contain("\"admissionKey\":null"));
            Assert.That(line, Does.Contain("\"clientKey\":null"));
            Assert.That(line, Does.Contain("\"node\":null"));
            Assert.That(line, Does.Contain("\"decidedAt\":null"));
            Assert.That(line, Does.Not.Contain("removedProfiles"),
                "the one member written only where it applies, which is a refusal a removed profile caused");

            // And the cost of saying it that way, asserted rather than left to be discovered: this
            // plugin's own reader refuses null, so a question that never reached a node, which is
            // every certificate the gate refused, is exactly the line it cannot read back. Nothing
            // reads the audit back, so the trade is worth making; it is not worth making by
            // accident.
            Assert.That(JsonBody.Read(line), Is.Null,
                "a null member is what `JsonBody` will not take, and this is where that shows");
        }

        [Test]
        public void A_line_is_one_line_however_odd_what_went_into_it()
        {
            // Nothing a caller sends should reach here, and this is the belt for that brace: a
            // record carrying a newline would end one line early and forge the next.
            var line = new AuditEntry(
                At, "d1", "k", "c", "op\nsecond", "node\"quoted", "at\"quoted", false, "why\r\nnot").Line();

            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(line, Does.Not.Contain("\r"));

            var body = JsonBody.Read(line);

            Assert.That(body.Text("operation"), Is.EqualTo("op\nsecond"));
            Assert.That(body.Text("node"), Is.EqualTo("node\"quoted"));
        }

        [Test]
        public void The_moment_is_written_in_one_way_and_in_one_zone()
        {
            var elsewhere = new AuditEntry(
                new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2)),
                "d", "k", "c", "op", null, null, true, "why");

            Assert.That(elsewhere.Line(), Does.Contain("\"at\":\"2026-03-04T05:06:07Z\""),
                "a log read on another machine has to mean the same thing");
        }

        [Test]
        public void An_approved_pairing_names_its_invitation_and_the_key_it_admitted_and_no_certificate_that_asked()
        {
            // Its asker held no certificate yet, so the line names what it asked with instead, and
            // what the operator's approval admitted, which is the new client's client key as well.
            var line = AuditEntry.Admission(
                At, "d1e2f3", null, "9f86d081", "pairRequest", AuditEntry.Approved, "the operator approved it",
                "invented-invitation", "9f86d081").Line();

            Assert.That(line, Is.EqualTo(
                "{\"at\":\"2026-03-04T05:06:07Z\"," +
                "\"database\":\"d1e2f3\"," +
                "\"admissionKey\":null," +
                "\"clientKey\":\"9f86d081\"," +
                "\"operation\":\"pairRequest\"," +
                "\"node\":null," +
                "\"decidedAt\":null," +
                "\"decision\":\"approved\"," +
                "\"reason\":\"the operator approved it\"," +
                "\"invitation\":\"invented-invitation\"," +
                "\"issued\":\"9f86d081\"}"));
        }

        [Test]
        public void A_line_about_an_admission_leaves_out_what_it_has_no_part_in()
        {
            var refusedPairing = AuditEntry.Admission(
                At, "d1e2f3", null, null, "pairRequest", AuditEntry.Refused, "the operator refused it",
                "invented-invitation", null).Line();
            var renewal = AuditEntry.Admission(
                At, "d1e2f3", "9f86d081", "c0ffee11", "renewCertificate", AuditEntry.Approved,
                "the operator approved it", null, "3a9f").Line();
            var revocation = AuditEntry.Admission(
                At, "d1e2f3", "9f86d081", "c0ffee11", null, AuditEntry.Revoked, "the operator revoked it", null, null)
                .Line();

            Assert.That(refusedPairing, Does.Contain("\"invitation\":\"invented-invitation\""));
            Assert.That(refusedPairing, Does.Not.Contain("issued"), "a refusal admitted nobody");
            Assert.That(refusedPairing, Does.Contain("\"clientKey\":null"), "so it names no client either");
            Assert.That(renewal, Does.Contain("\"admissionKey\":\"9f86d081\",\"clientKey\":\"c0ffee11\""));
            Assert.That(revocation, Does.Contain("\"admissionKey\":\"9f86d081\",\"clientKey\":\"c0ffee11\""));
            Assert.That(renewal, Does.EndWith("\"issued\":\"3a9f\"}"));
            Assert.That(renewal, Does.Not.Contain("invitation"), "a renewal arrives on a certificate");
            Assert.That(revocation, Does.Contain("\"operation\":null"),
                "the operator's revocation, which no route asked for");
            Assert.That(revocation, Does.Not.Contain("invitation").And.Not.Contain("issued"));
        }

        [Test]
        public void A_decision_about_a_request_carries_neither_note_about_admissions()
        {
            var line = Entry(true, "a rule here grants it").Line();

            Assert.That(line, Does.Not.Contain("invitation").And.Not.Contain("issued"));
        }

        [Test]
        public void Permitted_is_the_rules_word_and_no_admission_says_it()
        {
            // The operator's words are their own, so that a line reading `permitted` still means
            // only that the rules allowed something.
            var approved = AuditEntry.Admission(
                At, "d1e2f3", null, "k", "pairRequest", AuditEntry.Approved, "the operator approved it", "i", "k");

            Assert.That(approved.Permitted, Is.False);
            Assert.That(approved.Decision, Is.EqualTo("approved"));
            Assert.That(Entry(true, "a rule here grants it").Permitted, Is.True);
            Assert.That(Entry(false, "a rule here does not grant it").Decision, Is.EqualTo("refused"));
        }

        private static readonly DateTimeOffset At =
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private static AuditEntry Entry(bool permitted, string reason)
        {
            return new AuditEntry(
                At, "d1e2f3", "9f86d081", "c0ffee11", "readEntry", "7b1c", "3a9f", permitted, reason);
        }
    }

    /// <summary>The audit as a file, and what it does when it cannot be one.</summary>
    [TestFixture]
    public sealed class JsonLinesAuditTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Paths.Join(Path.GetTempPath(), "KeePassGateTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Unseal();
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
                // An audit file still open somewhere as the test ends. The directory is named for
                // this test alone, under the temporary folder, so a leftover is in no other test's way.
            }
        }

        [Test]
        public void A_file_that_allows_only_appending_refuses_AppendAllText_and_takes_the_append_right_alone()
        {
            // The measurement #18 asked for before anything changed, kept because it is a fact about
            // .NET Framework and Windows that the audit's way of opening its file rests on. An
            // administrator who wants a record that grows and cannot be rewritten gives the account
            // the right to append, beside the synchronize right Windows asks for on every open, and
            // nothing else. `File.AppendAllText` opens with `FileAccess.Write`, which is every write
            // right at once, so it is refused outright; a `FileStream` asking for
            // `FileSystemRights.AppendData` alone is let in, and adds to the end.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "audit.jsonl");
            File.WriteAllText(path, "first\n");
            Allow(path, AppendOnly);

            Assert.Throws<UnauthorizedAccessException>(() => File.AppendAllText(path, "refused\n"));

            Append(path, "appended\n");

            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(path),
                "and the account cannot read back what it wrote, which is what such a file is for");

            // The synchronize right is not optional. Without it even the append right alone is
            // refused, which is what an administrator writing the ACL by hand has to know.
            Allow(path, FileSystemRights.AppendData);
            Assert.Throws<UnauthorizedAccessException>(() => Append(path, "refused too\n"));

            Allow(path, FileSystemRights.FullControl);
            Assert.That(File.ReadAllText(path), Is.EqualTo("first\nappended\n"));
        }

        [Test]
        public void Decisions_are_recorded_to_a_file_that_allows_only_appending()
        {
            // What the measurement above is for. The audit used `File.AppendAllText`, so on a file
            // an administrator had made append only it recorded nothing at all, and said so once.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "audit.jsonl");
            File.WriteAllText(path, string.Empty);
            Allow(path, AppendOnly);

            var log = new Fakes.RecordingGateLog();
            var audit = new JsonLinesAudit(path, log);

            audit.Record(Entry("readEntry", true));
            audit.Record(Entry("deleteEntry", false));

            Assert.That(audit.Lost, Is.Zero);
            Assert.That(System.Linq.Enumerable.Any(log.Errors), Is.False);

            Allow(path, FileSystemRights.FullControl);
            var lines = File.ReadAllLines(path);

            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.Contain("readEntry"));
            Assert.That(lines[1], Does.Contain("deleteEntry"));
        }

        [Test]
        public void A_file_that_allows_only_appending_is_written_to_when_it_cannot_be_set_aside()
        {
            // A file the account can rename away is not append only, so an administrator who means
            // it takes away removing and renaming in the directory as well. Setting the file aside
            // then fails, which the audit already survived: it goes on writing past its bound, and
            // says once that it is doing so.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "audit.jsonl");
            Grow(path, JsonLinesAudit.RollAt + 1);
            File.WriteAllText(path + ".1", "older\n");
            Allow(path, AppendOnly);
            Allow(path + ".1", AppendOnly);
            Seal(_directory);

            var log = new Fakes.RecordingGateLog();
            var audit = new JsonLinesAudit(path, log);

            audit.Record(Entry("readEntry", true));

            Assert.That(audit.Lost, Is.Zero, "the decision was recorded");
            Assert.That(System.Linq.Enumerable.Single(log.Errors).Message, Does.Contain("could not be set aside"),
                "and the operator is told once that the file is growing past its bound");

            Unseal();

            Assert.That(new FileInfo(path).Length, Is.GreaterThan(JsonLinesAudit.RollAt + 1), "on the end of the file");
            Assert.That(File.ReadAllText(path + ".1"), Is.EqualTo("older\n"), "and the previous one as it was");
        }

        /// <summary>The account these tests run as, which is the one an ACL here grants or refuses.</summary>
        private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User;

        /// <summary>
        /// What an administrator's append only ACL gives the account: the right to append, and the
        /// synchronize right Windows asks for on every open. Read attributes, which it asks for as
        /// well, comes from the directory here, since the account may list it.
        /// </summary>
        private const FileSystemRights AppendOnly = FileSystemRights.AppendData | FileSystemRights.Synchronize;

        /// <summary>
        /// Gives the current account exactly these rights on a file and nothing inherited.
        /// </summary>
        /// <remarks>
        /// Written as SDDL rather than as a `FileSystemAccessRule`, which adds the synchronize right
        /// to every rule that allows anything: a rule for the append right alone is stored as
        /// append and synchronize, and hides the very thing an ACL written by hand gets wrong.
        /// </remarks>
        private static void Allow(string path, FileSystemRights rights)
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "D:P(A;;0x{0:X};;;{1})", (int)rights, Me.Value),
                AccessControlSections.Access);
            File.SetAccessControl(path, security);
        }

        /// <summary>Appends the way the audit does, asking for the right to append and no other.</summary>
        private static void Append(string path, string text)
        {
            using (var stream = new FileStream(
                       path, FileMode.Append, FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// Leaves the current account able to list a directory and open what is in it, and not to
        /// add, remove or rename anything there.
        /// </summary>
        private static void Seal(string directory)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                Me,
                FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes | FileSystemRights.Traverse
                    | FileSystemRights.Synchronize,
                AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);
        }

        /// <summary>
        /// Gives the current account everything back on the directory and on whatever is in it, so
        /// that it can be deleted. The account owns all of it, which is what lets it do this after
        /// granting itself so little.
        /// </summary>
        private void Unseal()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                Me, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(_directory, security);

            foreach (var file in Directory.GetFiles(_directory))
            {
                Allow(file, FileSystemRights.FullControl);
            }
        }

        [Test]
        public void Decisions_are_appended_one_to_a_line()
        {
            var path = Paths.Join(_directory, "audit.jsonl");
            var audit = new JsonLinesAudit(path, null);

            audit.Record(Entry("readEntry", true));
            audit.Record(Entry("deleteEntry", false));

            var lines = File.ReadAllLines(path);

            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.Contain("readEntry"));
            Assert.That(lines[1], Does.Contain("deleteEntry"));
            Assert.That(audit.Lost, Is.Zero);
        }

        [Test]
        public void The_directory_is_made_rather_than_demanded()
        {
            var path = Paths.Join(_directory, "made", "up", "audit.jsonl");

            new JsonLinesAudit(path, null).Record(Entry("readEntry", true));

            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public void A_file_that_cannot_be_written_costs_the_line_and_not_the_request()
        {
            // The direction that matters. A full disk must not silently revoke every agent's
            // access, so a failed write is counted and reported and the gate carries on.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "taken");
            Directory.CreateDirectory(path);

            var log = new Fakes.RecordingGateLog();
            var audit = new JsonLinesAudit(path, log);

            Assert.DoesNotThrow(() => audit.Record(Entry("readEntry", true)));
            Assert.DoesNotThrow(() => audit.Record(Entry("readEntry", false)));

            Assert.That(audit.Lost, Is.EqualTo(2));
            Assert.That(System.Linq.Enumerable.Count(log.Errors), Is.EqualTo(1),
                "reported once for as long as it keeps failing, rather than once a request");
        }

        [Test]
        public void A_file_that_can_be_written_again_says_so_and_the_next_failure_is_reported_too()
        {
            // Not every failure lasts. A program reading the file without letting anybody write
            // to it stops every append for exactly as long as it holds the file. Reporting only
            // the first failure said nothing would be recorded from then on, which was false the
            // moment it let go, and spent the only report on it: a failure after that one, a full
            // disk or a directory taken away, went unsaid.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "audit.jsonl");
            var log = new Fakes.RecordingGateLog();
            var audit = new JsonLinesAudit(path, log);

            audit.Record(Entry("readEntry", true));

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                audit.Record(Entry("readEntry", true));
                audit.Record(Entry("readEntry", false));
            }

            audit.Record(Entry("readEntry", true));

            Assert.That(audit.Lost, Is.EqualTo(2));
            Assert.That(System.Linq.Enumerable.Count(log.Errors), Is.EqualTo(1), "the failure, once");
            Assert.That(System.Linq.Enumerable.Single(log.Warnings).Message, Does.Contain("2 decisions"),
                "and when it ended, with what it cost");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                audit.Record(Entry("readEntry", true));
            }

            Assert.That(System.Linq.Enumerable.Count(log.Errors), Is.EqualTo(2),
                "the next failure is reported in its own turn");
            Assert.That(File.ReadAllLines(path).Length, Is.EqualTo(2), "and the lines around them were written");
        }

        [Test]
        public void Decisions_recorded_from_many_threads_at_once_are_all_written_whole()
        {
            // Every connection thread records through this, up to `GateOptions.MaxConnections` of
            // them at once, and the lock around the append is what keeps them apart: without it a
            // probe lost about a third of 6400 lines to sharing violations. Each loss is counted
            // and only the first is reported, so nothing else would notice the lock going.
            var path = Paths.Join(_directory, "audit.jsonl");
            var audit = new JsonLinesAudit(path, null);
            const int threads = 16;
            const int each = 40;

            using (var start = new System.Threading.ManualResetEventSlim(false))
            {
                var workers = new System.Threading.Thread[threads];
                for (var i = 0; i < threads; i++)
                {
                    workers[i] = new System.Threading.Thread(() =>
                    {
                        start.Wait();
                        for (var n = 0; n < each; n++)
                        {
                            audit.Record(Entry("readEntry", true));
                        }
                    });
                    workers[i].Start();
                }

                start.Set();

                foreach (var worker in workers)
                {
                    worker.Join();
                }
            }

            var lines = File.ReadAllLines(path);

            Assert.That(audit.Lost, Is.Zero);
            Assert.That(lines.Length, Is.EqualTo(threads * each));
            Assert.That(System.Linq.Enumerable.All(lines, l => JsonBody.Read(l) != null), Is.True,
                "and every one of them whole");
        }

        // Alone, because the variable it sets names the audit for the whole process.
        [Test]
        [NonParallelizable]
        public void A_file_named_by_a_relative_path_is_made_absolute_before_it_is_said()
        {
            // Left as it was set, the name was resolved against KeePass's working directory on
            // every append, which can change while it runs, and the log line meant to say where
            // the audit is named a bare file name.
            var was = Environment.GetEnvironmentVariable(JsonLinesAudit.AuditFileVariable);
            try
            {
                Environment.SetEnvironmentVariable(JsonLinesAudit.AuditFileVariable, "audit-invented.jsonl");

                var path = JsonLinesAudit.DefaultPath();

                Assert.That(Path.IsPathRooted(path), Is.True);
                Assert.That(path, Is.EqualTo(Paths.Join(Environment.CurrentDirectory, "audit-invented.jsonl")));
            }
            finally
            {
                Environment.SetEnvironmentVariable(JsonLinesAudit.AuditFileVariable, was);
            }
        }

        [Test]
        public void An_audit_with_nowhere_to_write_is_still_an_audit()
        {
            var audit = new JsonLinesAudit(null, null);

            Assert.DoesNotThrow(() => audit.Record(Entry("readEntry", true)));
            Assert.That(audit.Lost, Is.Zero, "nowhere named is not the same as a write that failed");
        }

        [Test]
        public void An_audit_with_nowhere_to_write_says_so_rather_than_going_quiet()
        {
            // The case worth hearing about, and the one that made no sound at all: every request
            // is answered as usual, `Lost` stays zero because nothing was attempted, and not one
            // decision is recorded. An audit that is not being written and an audit with nothing
            // in it look alike in the only place anybody would look, which is the file that is
            // not there.
            var log = new Fakes.RecordingGateLog();

            new JsonLinesAudit(null, log).Record(Entry("readEntry", true));

            var reported = System.Linq.Enumerable.Single(log.Errors);

            Assert.That(reported.Message, Does.Contain(JsonLinesAudit.AuditFileVariable),
                "and it says what to set to fix it");
        }

        [Test]
        public void Where_it_is_writing_is_written_down_as_it_opens()
        {
            // Nothing on a screen names this file and the operator's page can only give the
            // default, so a machine where the variable is set, or where the default resolved
            // somewhere unexpected, left the record of what an agent reached to be guessed at.
            var path = Paths.Join(_directory, "audit.jsonl");
            var log = new Fakes.RecordingGateLog();

            var audit = new JsonLinesAudit(path, log);

            Assert.That(audit.Path, Is.EqualTo(path));
            Assert.That(
                System.Linq.Enumerable.Any(log.Entries, e => e.Message.Contains(path)), Is.True,
                "the path it settled on is in the log");
            Assert.That(System.Linq.Enumerable.Any(log.Errors), Is.False,
                "and a working audit is not an error");
        }

        [Test]
        public void The_file_rolls_rather_than_growing_without_end()
        {
            var path = Paths.Join(_directory, "audit.jsonl");
            Directory.CreateDirectory(_directory);
            Grow(path, JsonLinesAudit.RollAt + 1);

            new JsonLinesAudit(path, null).Record(Entry("readEntry", true));

            Assert.That(File.Exists(path + ".1"), Is.True, "the old lines are set aside, not deleted");
            Assert.That(File.ReadAllLines(path).Length, Is.EqualTo(1), "and the new file starts clean");
        }

        [Test]
        public void Rolling_twice_keeps_one_previous_file_and_no_more()
        {
            var path = Paths.Join(_directory, "audit.jsonl");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path + ".1", "older");
            Grow(path, JsonLinesAudit.RollAt + 1);

            new JsonLinesAudit(path, null).Record(Entry("readEntry", true));

            Assert.That(File.ReadAllText(path + ".1"), Does.Not.Contain("older"));
        }

        [Test]
        public void A_file_that_cannot_be_set_aside_is_still_written_to()
        {
            // Rotation bounds the file; it is not permission to write to it. Sharing one `try` with
            // the append meant that a file past `RollAt` whose previous copy somebody was holding
            // recorded nothing from then on. The append it skipped would have worked.
            Directory.CreateDirectory(_directory);
            var path = Paths.Join(_directory, "audit.jsonl");
            Grow(path, JsonLinesAudit.RollAt + 1);

            // Held the way anything reading a log holds it, which is how this plugin's own log
            // opens its file. That is enough to stop the previous copy being deleted.
            using (new FileStream(path + ".1", FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                var log = new Fakes.RecordingGateLog();
                var audit = new JsonLinesAudit(path, log);

                audit.Record(Entry("readEntry", true));

                Assert.That(audit.Lost, Is.Zero, "the decision was recorded, so nothing was lost");
                Assert.That(File.ReadAllText(path), Does.Contain("readEntry"));
                Assert.That(System.Linq.Enumerable.Count(log.Errors), Is.EqualTo(1),
                    "and the operator is told once that the file is now growing past its bound");
            }
        }

        [Test]
        public void Where_it_writes_by_default_is_this_user_and_never_beside_a_database()
        {
            // A database is often on a share or in somebody's cloud folder, and a record of which
            // agent read which entry does not belong in either.
            var path = JsonLinesAudit.DefaultPath();

            Assert.That(path, Is.Not.Null);
            Assert.That(path, Does.EndWith(JsonLinesAudit.DefaultFileName));
            Assert.That(path, Does.Contain("KeePassGate"));
        }

        [Test]
        public void When_a_client_was_last_written_down_is_read_from_both_files()
        {
            // The one reader of the file, for the authority chooser: the newest line for each
            // client asked about, whether it is in the file or in the one set aside before it.
            var path = Paths.Join(_directory, "audit.jsonl");
            Directory.CreateDirectory(_directory);
            var older = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
            var newer = new DateTimeOffset(2026, 9, 20, 10, 30, 0, TimeSpan.Zero);

            File.WriteAllText(path + ".1", Line(older, "aaaa") + "\n" + Line(newer, "bbbb") + "\n");

            var audit = new JsonLinesAudit(path, null);
            audit.Record(new AuditEntry(newer, "d1e2f3", "aaaa", "aaaa", "getEntry", null, null, true, "why"));
            audit.Record(new AuditEntry(older, "d1e2f3", "bbbb", "bbbb", "getEntry", null, null, false, "why"));
            audit.Record(new AuditEntry(newer, "d1e2f3", "cccc", "cccc", "getEntry", null, null, true, "why"));

            var seen = JsonLinesAudit.LastSeen(path, new[] { "aaaa", "bbbb", "dddd" });

            Assert.That(seen["aaaa"], Is.EqualTo(newer), "the newer line is in the current file");
            Assert.That(seen["bbbb"], Is.EqualTo(newer), "and here it is in the one set aside");
            Assert.That(seen.ContainsKey("cccc"), Is.False, "a client nobody asked about is not reported");
            Assert.That(seen.ContainsKey("dddd"), Is.False, "and one with no line is absent rather than invented");
        }

        [Test]
        public void A_pairing_line_does_not_stand_for_a_request_and_a_renewal_or_a_revocation_line_does()
        {
            // Step 23's lines: a pairing names its certificate under `issued` and `clientKey` with
            // `admissionKey` null, so a certificate is last seen by its first request; a renewal is
            // a request; the operator's revocation names its certificate under `admissionKey`,
            // which nothing asks about afterwards. The renewal here is of a certificate that came
            // of renewing `aaaa`, so its client key is `aaaa`, and that is no request by `aaaa`.
            var path = Paths.Join(_directory, "audit.jsonl");
            Directory.CreateDirectory(_directory);
            var older = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
            var newer = new DateTimeOffset(2026, 9, 20, 10, 30, 0, TimeSpan.Zero);

            var audit = new JsonLinesAudit(path, null);
            audit.Record(AuditEntry.Admission(
                newer, "d1e2f3", null, "aaaa", "pairRequest", AuditEntry.Approved, "why", "abcdef01", "aaaa"));
            audit.Record(AuditEntry.Admission(
                newer, "d1e2f3", "bbbb", "aaaa", "renewCertificate", AuditEntry.Approved, "why", null, "cccc"));
            audit.Record(new AuditEntry(older, "d1e2f3", "dddd", "dddd", "getEntry", null, null, true, "why"));
            audit.Record(AuditEntry.Admission(
                newer, "d1e2f3", "dddd", "dddd", null, AuditEntry.Revoked, "why", null, null));

            var seen = JsonLinesAudit.LastSeen(path, new[] { "aaaa", "bbbb", "cccc", "dddd" });

            Assert.That(seen.ContainsKey("aaaa"), Is.False,
                "issued on a pairing is not a request, and nor is a client key on a line about another certificate");
            Assert.That(seen["bbbb"], Is.EqualTo(newer), "the certificate that asked to renew");
            Assert.That(seen.ContainsKey("cccc"), Is.False, "and not the one issued in its place");
            Assert.That(seen["dddd"], Is.EqualTo(newer), "the revocation names the certificate it took away");
        }

        [Test]
        public void An_audit_with_no_file_at_all_is_no_answer_rather_than_nobody()
        {
            Assert.That(JsonLinesAudit.LastSeen(Paths.Join(_directory, "nothing.jsonl"), new[] { "aaaa" }), Is.Null);
        }

        [Test]
        public void A_line_that_is_not_one_of_ours_is_passed_over()
        {
            var path = Paths.Join(_directory, "audit.jsonl");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, "not json at all\n{\"at\":\"yesterday\",\"admissionKey\":\"aaaa\"}\n{\"at\":\"2026-09-01T08:00:00Z\"\n");

            Assert.That(JsonLinesAudit.LastSeen(path, new[] { "aaaa" }), Is.Empty);
        }

        private static string Line(DateTimeOffset at, string client)
        {
            return new AuditEntry(at, "d1e2f3", client, client, "getEntry", null, null, true, "why").Line();
        }

        /// <summary>
        /// A file of a given length, without the bytes to fill it.
        /// </summary>
        /// <remarks>
        /// `Roll` asks the file how long it is and reads none of it, so writing eight megabytes to
        /// reach the threshold cost sixteen megabytes of string per test and proved nothing that
        /// this does not.
        /// </remarks>
        private static void Grow(string path, long bytes)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                file.SetLength(bytes);
            }
        }

        private static AuditEntry Entry(string operationId, bool permitted)
        {
            return new AuditEntry(
                DateTimeOffset.UtcNow, "d1e2f3", "9f86d081", "9f86d081", operationId, "7b1c", "3a9f", permitted,
                "why");
        }
    }
}
