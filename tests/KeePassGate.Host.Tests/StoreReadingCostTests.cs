using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What reading the plugin's store holds KeePass's thread for, through the real router and
    /// vault, `STORE.md` §620 and §644, with the admission of a connection read by meaning,
    /// §615 and §648, and what a write of the operator's and a pairing's write into it hold the
    /// thread for, §627, recorded in `FINDINGS.md` section 22.
    /// </summary>
    /// <remarks>
    /// **A measurement to read, and not a gate to pass**, since a time on a shared machine is no
    /// assertion. Run it in Release, the fixture alone in the filter, since a fixture marked
    /// explicit runs only where the filter names it alone: from the repository's root,
    /// `dotnet test tests\KeePassGate.Host.Tests -c Release` with
    /// `--filter FullyQualifiedName~StoreReadingCostTests`, and read the output, each line of
    /// which names the configuration it was built in. Over 10,000 entries in a hundred groups,
    /// without history and with ten history items each, KeePass's default limit, beside a store
    /// of the size the growth run left after a thousand writes: each request is a hold of its
    /// own, timed from the moment KeePass's thread is held to the moment it is let go, the median
    /// and the worst of 21 after a first. The first is printed as such only where it is a hold
    /// with nothing kept.
    /// </remarks>
    [TestFixture]
    [Explicit("a measurement to read, not a gate to pass")]
    public sealed class StoreReadingCostTests
    {
        private const int Entries = 10000;
        private const int Groups = 100;
        private const int Decisions = 280;
        private const int Runs = 21;
        private const string Profile = "agent";

        /// <summary>The clients of the gate the growth run measured, `STORE.md` §643.</summary>
        private const int GrowthClients = 10;

#if DEBUG
        private const string Built = "Debug";
#else
        private const string Built = "Release";
#endif

        /// <summary>The `id` of the authority a composed store serves.</summary>
        private static readonly string Authority = new string('a', 64);

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_request_holds_the_thread_for(int history)
        {
            using (var database = TemporaryDatabase.New())
            {
                var root = database.RootGroup;
                root.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting((Profile, "CRUSDLAP")));
                var entries = Fill(database.Database, history);
                Store(database.Database);

                var hold = new Timed();
                var store = new StoreReadings(database.Database, hold, null);
                var vault = new KdbxVault(
                    database.Database,
                    new DatabaseWriter(CurrentThread.Instance, saved => saved.Save(null), null, null, null),
                    TestTags.Fixed,
                    hold,
                    null,
                    store);
                var router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
                var entry = entries[entries.Count / 2].Uuid.ToHexString();

                Report("the store's word, the hold a connection is judged in", hold, () => hold.Hold(() => store.WordFor(Authority)), true);
                Report("a read of an entry", hold, () => Send(router, vault, "/entries/" + entry), false);
                Report("a listing of every entry", hold, () => Send(router, vault, "/entries"), false);

                // A change's turn, on KeePass's own thread, where no stamp is given: its lookup
                // and the client read again inside its rules each ask the store. The save is left
                // out, which is a whole database written and is no reading.
                var keePass = new OnKeePassThread();
                var turnStore = new StoreReadings(database.Database, keePass, null);
                var changing = new KdbxVault(
                    database.Database,
                    new DatabaseWriter(CurrentThread.Instance, saved => { }, null, null, null),
                    TestTags.Fixed,
                    keePass,
                    null,
                    turnStore);
                var asking = new KeePassGate.Vault.VaultAsking(
                    found => turnStore.WordFor(Authority) == null, found => KeePassGate.Vault.VaultSight.Least, null, false);
                var times = new List<double>();
                for (var run = 0; run <= Runs; run++)
                {
                    keePass.Readings = 0;
                    var clock = Stopwatch.StartNew();
                    changing.UpdateEntry(entry, new Dictionary<string, string> { { PwDefs.TitleField, "renamed " + run } }, asking);
                    times.Add(clock.Elapsed.TotalMilliseconds);
                }

                var later = times.Skip(1).OrderBy(time => time).ToList();
                TestContext.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] a change's turn: the first change of the series {1:F2} ms, median {2:F2} ms, worst {3:F2} ms, {4} readings of the store a change",
                    Built,
                    times[0],
                    later[later.Count / 2],
                    later[later.Count - 1],
                    keePass.Readings));
            }
        }

        [Test]
        public void What_a_hold_costs_at_the_plugin_s_ceilings()
        {
            // The same database beside a store near both of the plugin's ceilings: what is in
            // force and the room are worked out at every hold, over every item and every
            // deletion record of the store, `STORE.md` §623 and §627.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, 0);
                AtTheCeilings(database.Database);

                var hold = new Timed();
                var store = new StoreReadings(database.Database, hold, null);
                Assert.That(hold.Hold(() => store.Now()).Room.HasRoom, Is.True);
                Report("the store's word at the plugin's ceilings", hold, () => hold.Hold(() => store.WordFor(Authority)), false);
            }
        }

        [TestCase(0, 1)]
        [TestCase(0, GrowthClients)]
        [TestCase(10, 1)]
        [TestCase(10, GrowthClients)]
        public void What_a_connection_s_hold_costs_with_its_admission_read_by_meaning(int history, int clients)
        {
            // The hold a connection is judged in, the product's own target asked for the
            // admission of the certificate on the connection, read by meaning from the reading of
            // that hold, `STORE.md` §615 and §648: beside the store the growth run left, with a
            // single admission and with as many as the gate of the growth run admitted.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                var served = new StoredAdmissions(database.Database);
                var authority = StoreSeeds.Authority(served.Store);
                var key = served.KeyInForce();
                var revoked = new PwGroup(true, true, "Revoked", PwIcon.Folder);
                database.RootGroup.AddGroup(revoked, true);
                served.Restated(Revocations(revoked, key));
                var admitted = Enumerable.Range(0, clients).Select(client => Admit(database.Database, key, authority, null, null)).Last();

                Measured(
                    database.Database,
                    admitted,
                    "the hold a connection is judged in, its admission read by meaning, "
                    + (clients == 1 ? "beside a single admission" : "beside " + clients + " admissions")
                    + (history == 0 ? ", entries without history" : ", entries with " + history + " history items"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void What_a_connection_s_hold_costs_at_the_ceiling_of_statements(bool chained)
        {
            // A summary entry restating as many certificates taken away as leave the store room,
            // beside a pairing's admission; or with each of them renewed from the certificate
            // before it and naming the next, and the admission at the end of that chain, whose
            // judgment follows every statement back to the certificate first paired, `STORE.md`
            // §648 rule 4. Asked of the product's own target, as a connection is judged.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, 0);
                var admitted = Statements(database.Database, StoreFormat.MaxItems - 2, chained);

                using (var reading = new StoredAdmissions(database.Database).Store.Read())
                {
                    Assert.That(reading.Room.HasRoom, Is.True, "the store has room");
                }

                Measured(
                    database.Database,
                    admitted,
                    chained
                        ? "the hold a connection is judged in, at the end of a chain as long as the statements"
                        : "the hold a connection is judged in, beside the ceiling of statements");
            }
        }

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_pairing_s_write_holds_the_thread_for(int history)
        {
            // A pairing's write on KeePass's thread once the operator approved it, `STORE.md` §627
            // and `DESIGN.md` §119, each series on a database of its own: the store's own part,
            // which is the reading with every entry, the plan, the entries asked about, the
            // application and the record read back, with a save that writes nothing and the file
            // hashed back; and the whole turn with KeePass's own save.
            Print(
                "a pairing's write, the store's own part with the file hashed back",
                Pairings(history, saved => saved.Modified = false));
            Print("a pairing's write with KeePass's save", Pairings(history, saved => saved.Save(null)));
        }

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_write_of_the_operator_s_holds_the_thread_for(int history)
        {
            // A Recreate as the settings dialog's OK writes it, the store's write timed alone on
            // the thread that makes it: the reading in the writer's turn, the plan, every entry it
            // touches asked about, and the application, `STORE.md` §627. Making the authority the
            // write carries is left out, since the tab makes it at the press and not at the OK.
            // Each Recreate leaves a retirement behind, so the store gains an entry with each write.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                Store(database.Database);

                var store = new TimedStore(new KdbxStore(database.Database, GateVersion.Current, null, entry => null));
                var service = new AuthorityService(store, NullGateLog.Instance);
                for (var run = 0; run <= Runs; run++)
                {
                    Assert.That(service.Recreate().Succeeded, Is.True);
                }

                var later = store.Times.Skip(1).OrderBy(time => time).ToList();
                TestContext.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] a Recreate's write: the first write of the series {1:F2} ms, median {2:F2} ms, worst {3:F2} ms",
                    Built,
                    store.Times[0],
                    later[later.Count / 2],
                    later[later.Count - 1]));
            }
        }

        /// <summary>A store whose writes are timed, each from its call to its answer.</summary>
        private sealed class TimedStore : IGateStore
        {
            private readonly IGateStore _store;

            public TimedStore(IGateStore store)
            {
                _store = store;
            }

            public List<double> Times { get; } = new List<double>();

            public string Unwritable
            {
                get { return _store.Unwritable; }
            }

            public StoreReading Read()
            {
                return _store.Read();
            }

            public StoreWritten Write(IReadOnlyList<StoreAct> acts)
            {
                var clock = Stopwatch.StartNew();
                var written = _store.Write(acts);
                Times.Add(clock.Elapsed.TotalMilliseconds);
                return written;
            }
        }

        /// <summary>KeePass's own thread: no stamp, every question its own reading, each counted.</summary>
        private sealed class OnKeePassThread : IReadHold
        {
            public int Readings { get; set; }

            public long Stamp
            {
                get
                {
                    Readings++;
                    return 0;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                return read();
            }
        }

        /// <summary>
        /// A request timed over a first and the 21 after it, printed with the configuration the
        /// fixture was built in; the first printed as such only where it is a hold with nothing
        /// kept.
        /// </summary>
        private static void Report(string what, Timed hold, Action request, bool nothingKept)
        {
            hold.Times.Clear();
            request();
            var first = hold.Times.Sum();

            var times = new List<double>();
            for (var run = 0; run < Runs; run++)
            {
                hold.Times.Clear();
                request();
                times.Add(hold.Times.Sum());
            }

            times.Sort();
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1}: {2}median {3:F2} ms, worst {4:F2} ms, {5} holds a request",
                Built,
                what,
                nothingKept ? string.Format(CultureInfo.InvariantCulture, "first {0:F2} ms, ", first) : string.Empty,
                times[times.Count / 2],
                times[times.Count - 1],
                hold.Count));
        }

        private static void Send(SpecRouter router, KdbxVault vault, string path)
        {
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead("GET", path, new Dictionary<string, string>(), new Dictionary<string, string>(), 0),
                    null,
                    ClientIdentity.Paired("d1e2f3", certificate, new[] { Profile }));
                new ServedDatabase(vault).PutOn(context);
                router.Handle(context);
                Assert.That(context.Status, Is.EqualTo(200), context.Document());
                context.Document();
            }
        }

        private static List<PwEntry> Fill(PwDatabase database, int history)
        {
            var groups = Enumerable.Range(0, Groups).Select(index =>
            {
                var group = new PwGroup(true, true, "Invented " + index, PwIcon.Folder);
                database.RootGroup.AddGroup(group, true);
                return group;
            }).ToList();

            var made = new List<PwEntry>(Entries);
            for (var index = 0; index < Entries; index++)
            {
                var entry = new PwEntry(true, true);
                entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service " + index));
                entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "an invented user"));
                entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "an invented password"));
                for (var edit = 0; edit < history; edit++)
                {
                    entry.History.Add(entry.CloneDeep());
                }

                groups[index % Groups].AddEntry(entry, true);
                made.Add(entry);
            }

            return made;
        }

        /// <summary>
        /// An authority, a revocation for each of many clients, and the summary entry restating
        /// them all, which is about the size of the store the growth run left after a thousand
        /// writes, `STORE.md` §643.
        /// </summary>
        private static void Store(PwDatabase database)
        {
            var key = StoreComposer.Key("cost");
            var group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted);
            database.RootGroup.AddGroup(group, true);
            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "authority", "authority:" + Authority,
                StoreComposer.Content(("id", Authority), ("storeKey", StoreComposer.HexOf(key))))), true);

            var items = new List<string> { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) };
            items.AddRange(Revocations(group, key));
            var after = Enumerable.Range(0, Decisions).Select(index => index.ToString("x32", CultureInfo.InvariantCulture));

            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "head", "store",
                StoreComposer.Content(("after", string.Join(" ", after)), ("height", "1")),
                1,
                items)), true);
        }

        /// <summary>
        /// A revocation for each of many clients, a record of each in this group under this store
        /// key, answering the items that restate them.
        /// </summary>
        private static List<string> Revocations(PwGroup group, byte[] key)
        {
            var items = new List<string>();
            for (var index = 0; index < Decisions; index++)
            {
                var client = index.ToString("x64", CultureInfo.InvariantCulture);
                var record = StoreComposer.Record(key, "revoked", "client:" + client, StoreComposer.Content(("client", client)));
                group.AddEntry(StoreEntries.Of(record), true);
                items.Add(StoreComposer.Item(("client", client), ("kind", "revoked")));
            }

            return items;
        }

        /// <summary>
        /// An authority made by the product's own Create, an admission, and the summary entry
        /// restated with this many certificates taken away beside what it held, answering the
        /// admission's certificate. Unchained, the admission is a pairing's and each certificate
        /// taken away stands alone. Chained, each certificate taken away was renewed from the
        /// certificate before it and names the next, and the admission is the renewal from the
        /// last of them, under the client key of the first.
        /// </summary>
        private static string Statements(PwDatabase database, int count, bool chained)
        {
            var served = new StoredAdmissions(database);
            var authority = StoreSeeds.Authority(served.Store);
            var key = served.KeyInForce();

            var taken = Enumerable.Range(0, count).Select(index => index.ToString("x64", CultureInfo.InvariantCulture)).ToList();
            var admitted = chained ? Admit(database, key, authority, taken[0], taken[count - 1]) : Admit(database, key, authority, null, null);
            var items = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var id = StoreComposer.HexOf(
                    StoreComposer.IdentifierOf(key, "withdrawn", "certificate:" + taken[index], 0, new string('5', 64)));
                var renewed = !chained ? string.Empty : index + 1 < count ? taken[index + 1] : admitted;
                items.Add(chained && index > 0
                    ? StoreComposer.Item(
                        ("certificate", taken[index]), ("from", taken[index - 1]), ("id", id), ("kind", "withdrawn"), ("renewed", renewed))
                    : StoreComposer.Item(("certificate", taken[index]), ("id", id), ("kind", "withdrawn"), ("renewed", renewed)));
            }

            served.Restated(items);
            return admitted;
        }

        /// <summary>
        /// An admission composed under the store key and naming this issuer, as a pairing writes
        /// it, or as a renewal writes it where a client key and a certificate of origin are given;
        /// answers its certificate. It counts wherever it sits, so it sits in the root group.
        /// </summary>
        private static string Admit(PwDatabase database, byte[] key, string issuer, string client, string from)
        {
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var admission = AdmissionRecord.For(
                    certificate, DateTimeOffset.UtcNow, new[] { Profile }, issuer, client ?? CertificateDigest.Of(certificate), from);
                database.RootGroup.AddEntry(KeePassGate.Bridge.StoreEntries.Of(StoreRecord.Compose(key, StoreKinds.Client, admission.Members())), true);
                return admission.AdmissionKey;
            }
        }

        /// <summary>
        /// The hold a connection is judged in, timed: the gate's own target for the database asked
        /// for the admission of this certificate, which it reads in a hold of its own.
        /// </summary>
        private static void Measured(PwDatabase database, string admitted, string what)
        {
            var hold = new Timed();
            using (var targets = new OpenDatabaseTargets(delegate { return new[] { database }; }, null, hold, null))
            using (var target = targets.Current().Single())
            {
                Assert.That(target.Admission(admitted), Is.Not.Null, "the admission counts");
                Report(what, hold, () => target.Admission(admitted), false);
            }
        }

        /// <summary>
        /// Pairings written in turn on a database of their own beside the entries given, each
        /// timed from its call to its answer.
        /// </summary>
        private static List<double> Pairings(int history, Action<PwDatabase> save)
        {
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                var served = StoreSeeds.Authority(new KdbxStore(database.Database, GateVersion.Current, null));
                database.Save();

                var writer = new DatabaseWriter(CurrentThread.Instance, save, null, null, null);
                var times = new List<double>();
                for (var run = 0; run <= Runs; run++)
                {
                    using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
                    {
                        var admission = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { Profile }, served);
                        var clock = Stopwatch.StartNew();
                        var written = writer.WriteAdmission(database.Database, admission);
                        times.Add(clock.Elapsed.TotalMilliseconds);
                        Assert.That(written.Ok, Is.True, written.Reason);
                    }
                }

                return times;
            }
        }

        /// <summary>
        /// The first write of a series, and the median and worst of the rest, printed with the
        /// configuration the fixture was built in.
        /// </summary>
        private static void Print(string what, List<double> times)
        {
            var later = times.Skip(1).OrderBy(time => time).ToList();
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1}: the first write of the series {2:F2} ms, median {3:F2} ms, worst {4:F2} ms",
                Built,
                what,
                times[0],
                later[later.Count / 2],
                later[later.Count - 1]));
        }

        /// <summary>
        /// An authority, and a summary entry that names nearly as many summary entries as a
        /// string the plugin writes holds, each of them deleted with a deletion record, and that
        /// restates nearly as many revocations as a summary entry has item names, a tenth of them
        /// with a deletion record of its own besides, `StoreFormat.MaxWritten` and
        /// `StoreFormat.MaxItems`.
        /// </summary>
        private static void AtTheCeilings(PwDatabase database)
        {
            const int Named = 7900;
            const int Revoked = 9000;
            var key = StoreComposer.Key("ceilings");
            var group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted);
            database.RootGroup.AddGroup(group, true);
            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "authority", "authority:" + new string('a', 64),
                StoreComposer.Content(("id", new string('a', 64)), ("storeKey", StoreComposer.HexOf(key))))), true);

            var after = new List<string>();
            for (var height = 1; height <= Named; height++)
            {
                var replaced = StoreComposer.IdentifierOf(key, "head", "store", height, height.ToString("x64", CultureInfo.InvariantCulture));
                database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(replaced), DateTime.UtcNow));
                after.Add(StoreComposer.HexOf(replaced));
            }

            var items = new List<string> { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) };
            for (var index = 0; index < Revoked; index++)
            {
                var client = index.ToString("x64", CultureInfo.InvariantCulture);
                items.Add(StoreComposer.Item(("client", client), ("kind", "revoked")));
                if (index % 10 == 0)
                {
                    var revocation = StoreComposer.IdentifierOf(key, "revoked", "client:" + client, 0, new string('5', 64));
                    database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(revocation), DateTime.UtcNow));
                }
            }

            after.Sort(StringComparer.Ordinal);
            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "head", "store",
                StoreComposer.Content(("after", string.Join(" ", after)), ("height", (Named + 1).ToString(CultureInfo.InvariantCulture))),
                Named + 1,
                items)), true);
        }

        /// <summary>A hold with a stamp of its own, timing each outermost hold.</summary>
        private sealed class Timed : IReadHold
        {
            private long _given;

            public List<double> Times { get; } = new List<double>();

            public int Count
            {
                get { return Times.Count; }
            }

            public long Stamp { get; private set; }

            public T Hold<T>(Func<T> read)
            {
                if (Stamp != 0)
                {
                    return read();
                }

                Stamp = ++_given;
                var clock = Stopwatch.StartNew();
                try
                {
                    return read();
                }
                finally
                {
                    Times.Add(clock.Elapsed.TotalMilliseconds);
                    Stamp = 0;
                }
            }
        }
    }
}
