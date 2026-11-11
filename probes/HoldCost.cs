// How long a whole request holds KeePass's thread, now that a request that changes nothing is
// decided and served inside one hold. Unlike the other probes this is not built on its own: it is
// a fixture for the plugin's host test project, compiled with the plugin it measures, because the
// claim is about the plugin's own router and vault. It does nothing unless KPG_HOLD_COST_OUT names
// the file its table goes to, and it times the work inside each hold, holding nothing itself.
// cspell:ignore RLAP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Bridge;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A probe, not a test: how long each kind of read holds KeePass's thread, now that the
    /// answer is made inside the hold. Everything is invented.
    /// </summary>
    [TestFixture]
    [Explicit]
    public sealed class HoldCost
    {
        [Test]
        public void Measure()
        {
            var output = Environment.GetEnvironmentVariable("KPG_HOLD_COST_OUT");
            if (string.IsNullOrEmpty(output))
            {
                Assert.Ignore("no output file named");
            }

            var lines = new List<string>();
            using (var database = TemporaryDatabase.New())
            {
                database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RLAP")));

                var random = new Random(7);
                PwGroup someGroup = null;
                PwEntry someEntry = null;
                for (var g = 0; g < 1000; g++)
                {
                    var group = new PwGroup(true, true, "Invented group " + g, PwIcon.Folder);
                    database.RootGroup.AddGroup(group, true);
                    for (var e = 0; e < 10; e++)
                    {
                        var entry = new PwEntry(true, true);
                        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service " + g + "-" + e));
                        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "invented-user-" + e));
                        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Text(random, 24)));
                        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://invented.example/" + g + "/" + e));
                        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Text(random, 200)));
                        group.AddEntry(entry, true);
                        someEntry = entry;
                    }

                    someGroup = group;
                }

                // An entry whose strings come to just under the ceiling, half of them protected.
                var large = new PwEntry(true, true);
                large.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented large entry"));
                for (var f = 0; f < 50; f++)
                {
                    large.Strings.Set("Invented " + f, new ProtectedString(f % 2 == 0, Text(random, 10000)));
                }

                someGroup.AddEntry(large, true);

                // One attachment at the ceiling, kept in the open, and one kept protected.
                var attached = new PwEntry(true, true);
                attached.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented holder"));
                attached.Binaries.Set("open.bin", new ProtectedBinary(false, Bytes(random, VaultAttachment.MostServed)));
                attached.Binaries.Set("kept.bin", new ProtectedBinary(true, Bytes(random, VaultAttachment.MostServed)));
                someGroup.AddEntry(attached, true);

                // Forty attachments at the ceiling: what an entry's tag digests the first time.
                var many = new PwEntry(true, true);
                many.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented hoard"));
                for (var a = 0; a < 40; a++)
                {
                    many.Binaries.Set("invented-" + a + ".bin", new ProtectedBinary(a % 2 == 0, Bytes(random, VaultAttachment.MostServed)));
                }

                someGroup.AddEntry(many, true);

                var hold = new Timed();
                var vault = new KdbxVault(
                    database.Database,
                    new DatabaseWriter(CurrentThread.Instance, saved => saved.Save(null), null, null, null),
                    TestTags.Fixed,
                    hold,
                    null);
                var router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));

                var group1 = someGroup.Uuid.ToHexString();
                var paths = new[]
                {
                    Tuple.Create("the entry with forty attachments", "/entries/" + many.Uuid.ToHexString()),
                    Tuple.Create("an attachment at the ceiling, open", "/entries/" + attached.Uuid.ToHexString() + "/attachments/open.bin"),
                    Tuple.Create("an attachment at the ceiling, protected", "/entries/" + attached.Uuid.ToHexString() + "/attachments/kept.bin"),
                    Tuple.Create("every entry", "/entries"),
                    Tuple.Create("every group", "/groups"),
                    Tuple.Create("a group's entries", "/groups/" + group1 + "/entries"),
                    Tuple.Create("a group", "/groups/" + group1),
                    Tuple.Create("an entry", "/entries/" + someEntry.Uuid.ToHexString()),
                    Tuple.Create("an entry's access", "/entries/" + someEntry.Uuid.ToHexString() + "/access"),
                    Tuple.Create("an entry at the ceiling", "/entries/" + large.Uuid.ToHexString()),
                    Tuple.Create("the fields of an entry at the ceiling", "/entries/" + large.Uuid.ToHexString() + "/fields"),
                };

                var store = new CustomDataStore(database.Database);
                var authority = new AuthorityService(store, NullGateLog.Instance);
                Assume.That(authority.Create().Succeeded, Is.True);
                var names = ProfileNames.Read(null);
                Assume.That(names.Add("agent"), Is.Null);
                database.Database.CustomData.Set(ProfileNames.CustomDataKey, names.Write());

                X509Certificate2 certificate;
                using (var reading = authority.Read())
                {
                    certificate = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-fictitious", new[] { "agent" }).Read();
                }

                new AdmissionStore(store, NullGateLog.Instance).Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "agent" }));

                var databases = new[] { database.Database }.ToList();
                using (certificate)
                using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
                using (var target = targets.Current().Single())
                {
                    var client = ClientCredentials.For(certificate, target, NullGateLog.Instance);
                    Assume.That(client.IsPaired, Is.True);

                    // The just-in-time compiler's share, on a path of its own kind.
                    Ask(router, vault, target, client, "/groups/" + group1 + "/groups");

                    foreach (var path in paths)
                    {
                        hold.Lengths.Clear();
                        var first = Ask(router, vault, target, client, path.Item2);
                        Assert.That(first.Status, Is.EqualTo(200), path.Item2 + " " + first.Status);
                        Assert.That(hold.Lengths.Count, Is.EqualTo(1), path.Item2);
                        var cold = hold.Lengths[0];

                        hold.Lengths.Clear();
                        for (var run = 0; run < 41; run++)
                        {
                            Ask(router, vault, target, client, path.Item2);
                        }

                        var sorted = hold.Lengths.OrderBy(length => length).ToList();
                        lines.Add(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,-42} first {1,9:F3} ms  median {2,9:F3} ms  worst {3,9:F3} ms  answer {4,9} chars",
                            path.Item1,
                            cold,
                            sorted[sorted.Count / 2],
                            sorted[sorted.Count - 1],
                            (first.Json ?? string.Empty).Length));
                    }
                }
            }

#if DEBUG
            lines.Insert(0, "DEBUG build");
#else
            lines.Insert(0, "RELEASE build");
#endif
            File.WriteAllLines(output, lines, Encoding.UTF8);
        }

        private static GateContext Ask(SpecRouter router, IVault vault, IGateTarget target, ClientIdentity client, string path)
        {
            var context = new GateContext(
                new HttpRequestHead(
                    "GET",
                    path,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    0),
                string.Empty,
                client);
            new ServedDatabase(vault, target, NullGateLog.Instance).PutOn(context);
            router.Handle(context);
            return context;
        }

        private static string Text(Random random, int length)
        {
            var text = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                text.Append((char)('a' + random.Next(26)));
            }

            return text.ToString();
        }

        private static byte[] Bytes(Random random, int length)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

        /// <summary>Times every outermost hold, and holds nothing.</summary>
        private sealed class Timed : IReadHold
        {
            private int _depth;

            public List<double> Lengths { get; } = new List<double>();

            public T Hold<T>(Func<T> read)
            {
                if (_depth > 0)
                {
                    return read();
                }

                _depth++;
                var clock = Stopwatch.StartNew();
                try
                {
                    return read();
                }
                finally
                {
                    clock.Stop();
                    _depth--;
                    Lengths.Add(clock.Elapsed.TotalMilliseconds);
                }
            }
        }
    }
}
