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
    /// vault, `STORE.md` §620 and §644, and what a write of the operator's into it holds the
    /// thread for, §627, recorded in `FINDINGS.md` section 22.
    /// </summary>
    /// <remarks>
    /// **A measurement to read, and not a gate to pass**, since a time on a shared machine is no
    /// assertion. Run it with `dotnet test --filter StoreReadingCostTests` and read the output.
    /// Over 10,000 entries in a hundred groups, without history and with ten history items each,
    /// KeePass's default limit, beside a store of the size the growth run left after a thousand
    /// writes: each request is a hold of its own, timed from the moment KeePass's thread is held
    /// to the moment it is let go, the median and the worst of 21 after a first that keeps
    /// nothing.
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

                Report("the store's word, the hold a connection is judged in", hold, () => hold.Hold(() => store.Word));
                Report("a read of an entry", hold, () => Send(router, vault, "/entries/" + entry));
                Report("a listing of every entry", hold, () => Send(router, vault, "/entries"));

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
                    found => turnStore.Word == null, found => KeePassGate.Vault.VaultSight.Least, null, false);
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
                    "a change's turn: first {0:F2} ms, median {1:F2} ms, worst {2:F2} ms, {3} readings of the store a change",
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
                Report("the store's word at the plugin's ceilings", hold, () => hold.Hold(() => store.Word));
            }
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
                    "a Recreate's write: first {0:F2} ms, median {1:F2} ms, worst {2:F2} ms",
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

        private static void Report(string what, Timed hold, Action request)
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
                "{0}: first {1:F2} ms, median {2:F2} ms, worst {3:F2} ms, {4} holds a request",
                what,
                first,
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
                key, "authority", "authority:" + new string('a', 64),
                StoreComposer.Content(("id", new string('a', 64)), ("storeKey", StoreComposer.HexOf(key))))), true);

            var items = new List<string> { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) };
            var after = new List<string>();
            for (var index = 0; index < Decisions; index++)
            {
                var client = index.ToString("x64", CultureInfo.InvariantCulture);
                var record = StoreComposer.Record(key, "revoked", "client:" + client, StoreComposer.Content(("client", client)));
                group.AddEntry(StoreEntries.Of(record), true);
                items.Add(StoreComposer.Item(("client", client), ("kind", "revoked")));
                after.Add(index.ToString("x32", CultureInfo.InvariantCulture));
            }

            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "head", "store",
                StoreComposer.Content(("after", string.Join(" ", after)), ("height", "1")),
                1,
                items)), true);
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
