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
    /// §615 and §648, and the names read beside it, §616, and what a write of the operator's and
    /// a pairing's write into it hold the thread for, §627, recorded in `FINDINGS.md` section 22,
    /// an OK above versions that follow among them, §617 and §643.
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

        /// <summary>The versions a write deletes in the measurement of a write that replaces many.</summary>
        private const int ReplacedVersions = 2000;

        /// <summary>The OKs above versions that follow timed after a first, each renewing every version below.</summary>
        private const int OkRuns = 10;

        /// <summary>The clients of the gate the growth run measured, `STORE.md` §643.</summary>
        private const int GrowthClients = 10;

        /// <summary>The writes timed after a first in the measurement of a store after many renewals.</summary>
        private const int RenewedRuns = 5;

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

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(10, false)]
        [TestCase(10, true)]
        public void What_a_connection_s_hold_costs_with_the_names_it_reads(int history, bool removedOften)
        {
            // The hold a connection is judged in reads the names beside the admission, `STORE.md`
            // §616 and §620: beside the store the growth run left and a pairing's admission, with
            // 32 names on the list; or with a single name removed and added again as often as the
            // room allows, so that its addition names all those removals, each known as an item
            // of the summary entry.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                var served = new StoredAdmissions(database.Database);
                var authority = StoreSeeds.Authority(served.Store);
                var key = served.KeyInForce();
                var revoked = new PwGroup(true, true, "Revoked", PwIcon.Folder);
                database.RootGroup.AddGroup(revoked, true);
                served.Restated(Revocations(revoked, key));
                var admitted = Admit(database.Database, key, authority, null, null);
                var removals = removedOften ? Renamed(database.Database, served, key) : Named(database.Database, key, 32);

                using (var reading = served.Store.Read())
                {
                    Assert.That(reading.Room.HasRoom, Is.True, "the store has room");
                    Assert.That(ProfileNames.Of(reading).Names, Has.Count.EqualTo(removedOften ? 1 : 32), "the names are on the list");
                }

                Measured(
                    database.Database,
                    admitted,
                    "the hold a connection is judged in, "
                    + (removedOften ? "beside a name added again after " + removals + " removals" : "beside 32 names")
                    + (history == 0 ? ", entries without history" : ", entries with " + history + " history items"));
            }
        }

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_pairing_dialog_s_reading_holds_the_thread_for(int history)
        {
            // The reading a pairing's dialog takes on KeePass's thread as it comes up and on each
            // tick of its countdown while it stands, `PAIRING.md` §303: afresh, with the store key,
            // through the real target, beside the store a pairing leaves and 32 names on the list,
            // asked about a name on the list, a name new and a name removed.
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                var served = new StoredAdmissions(database.Database);
                StoreSeeds.Authority(served.Store);
                var key = served.KeyInForce();
                Named(database.Database, key, 32);
                StoreSeeds.Name(new KdbxStore(database.Database, GateVersion.Current, null), Profile);
                StoreSeeds.Unname(new KdbxStore(database.Database, GateVersion.Current, null), Profile);

                var hold = new Timed();
                using (var targets = new OpenDatabaseTargets(delegate { return new[] { database.Database }; }, null, hold, null))
                using (var target = targets.Current().Single())
                {
                    var asked = new[] { "named-00", "fresh", Profile };
                    var reading = target.Asked(asked);
                    Assert.That(reading.Served, Is.True, "the store serves");
                    Assert.That(reading.MarkOf(Profile), Is.EqualTo(KeePassGate.Gate.ProfileMark.Removed), "a removed name is among those read");
                    Report(
                        "a pairing dialog's reading beside 32 names"
                        + (history == 0 ? ", entries without history" : ", entries with " + history + " history items"),
                        hold,
                        () => target.Asked(asked),
                        false);
                }
            }
        }

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_pairing_s_write_approving_32_new_names_holds_the_thread_for(int history)
        {
            // A pairing whose approval adds 32 names the list does not hold, each an addition in
            // the admission's own write, `STORE.md` §627, beside the figure of a pairing that adds
            // none: the store's own part with the file hashed back.
            Print(
                "a pairing's write approving 32 new names, the store's own part with the file hashed back",
                Pairings(history, saved => saved.Modified = false, 32));
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

        [TestCase(0)]
        [TestCase(10)]
        public void What_a_write_deleting_2000_replaced_versions_holds_the_thread_for(int history)
        {
            // A write whose version names 2,000 versions of its object, each deleted with a
            // deletion record, `STORE.md` §627 step 3: the store's write timed alone on the thread
            // that makes it, the reading in the writer's turn, the plan, every entry it touches
            // asked about, and the application. Each run is a database of its own, the 2,000
            // versions written by a write before the write timed.
            var times = new List<double>();
            for (var run = 0; run <= Runs; run++)
            {
                using (var database = TemporaryDatabase.New())
                {
                    Fill(database.Database, history);
                    var plain = new KdbxStore(database.Database, GateVersion.Current, null, entry => null);
                    StoreSeeds.Authority(plain);
                    byte[] key;
                    using (var reading = plain.Read())
                    {
                        key = (byte[])reading.InForce.Key.Bytes.Clone();
                    }

                    var objectName = "switch:" + new string('5', 32);
                    var replaced = Enumerable.Range(0, ReplacedVersions).Select(index => VersionOf(key, objectName, string.Empty)).ToList();
                    Assert.That(plain.Write(new[] { new StoreAct("versions", key, StoreFormat.KeyName(key), replaced, null) }).Taken, Is.True);
                    var naming = VersionOf(
                        key, objectName, string.Join(" ", replaced.Select(record => record.Hex).OrderBy(hex => hex, StringComparer.Ordinal)));

                    var store = new TimedStore(plain);
                    var written = store.Write(new[] { new StoreAct("a version replacing them", key, StoreFormat.KeyName(key), new[] { naming }, null) });
                    Assert.That(written.Taken, Is.True, written.Reason);
                    Assert.That(
                        replaced.Count(record => database.RootGroup.FindEntry(new PwUuid(record.Identifier), true) != null), Is.EqualTo(0),
                        "every version replaced is deleted");
                    times.Add(store.Times[0]);
                }
            }

            Print("a write deleting " + ReplacedVersions + " replaced versions, history " + history, times);
        }

        [TestCase(200, false)]
        [TestCase(2000, false)]
        [TestCase(9000, false)]
        [TestCase(200, true)]
        [TestCase(2000, true)]
        [TestCase(9000, true)]
        public void What_an_OK_above_versions_that_follow_holds_the_thread_for(int following, bool belowTheRoot)
        {
            // An OK at the root group's dialog that widens or narrows a rule for the fixture's
            // profile above as many entries whose rule was taken off and follows it, `STORE.md`
            // §617: the reading in the writer's turn, the composition with every renewal, the
            // plan with every entry it touches asked about, and the application, each timed
            // apart on the thread that makes it, beside 10,000 entries in a hundred groups. Each
            // OK renews every version below, so each leaves a deletion record for each; a hold of
            // the gate is timed after those writes. The tab's opening, which reads the versions
            // below, is timed beside them. Below the root, the same at a group under the root
            // that holds the hundred groups, with a rule at the root as well: there the act walks
            // the group's parent for every mask it records and the tab for what arrives, which an
            // OK at the root, with no parent, never does.
            using (var database = TemporaryDatabase.New())
            {
                var top = database.RootGroup;
                if (belowTheRoot)
                {
                    top = new PwGroup(true, true, "Invented above", PwIcon.Folder);
                    database.RootGroup.AddGroup(top, true);
                }

                var entries = Fill(database.Database, 0, top);
                Counted(database.Database);
                var plain = new KdbxStore(database.Database, GateVersion.Current, null, entry => null);
                StoreSeeds.Authority(plain);
                var root = Hex.Of(database.RootGroup.Uuid.UuidBytes);
                var node = Hex.Of(top.Uuid.UuidBytes);
                byte[] key;
                using (var reading = plain.Read())
                {
                    key = (byte[])reading.InForce.Key.Bytes.Clone();
                }

                var rows = belowTheRoot ? new[] { root, node } : new[] { root };
                var versions = entries.Take(following)
                    .Select(entry => RuleVersionOf(key, StoreComposer.RowObject(Hex.Of(entry.Uuid.UuidBytes), Profile), "off:R"))
                    .Concat(rows.Select(ruled => RuleVersionOf(key, StoreComposer.RowObject(ruled, Profile), "row:R")))
                    .ToList();
                Assert.That(plain.Write(new[] { new StoreAct("versions", key, StoreFormat.KeyName(key), versions, null) }).Taken, Is.True);
                var read = "/entries/" + entries[0].Uuid.ToHexString();
                Held(database.Database, "a read of an entry beside those versions, before any OK", read);

                var opening = new List<double>();
                var readings = new List<double>();
                var compositions = new List<double>();
                var plans = new List<double>();
                var applications = new List<double>();
                var totals = new List<double>();
                for (var run = 0; run <= OkRuns; run++)
                {
                    // The tab, as it opens on the group's dialog: the store read, the rules worked
                    // out, what arrives from the group's parent, and the versions below kept.
                    var clock = Stopwatch.StartNew();
                    RuleChanges changes;
                    using (var reading = plain.Read())
                    {
                        var rules = StoredRules.Of(reading);
                        var arriving = AclResolver.Above(belowTheRoot ? KdbxNodes.Of(database.RootGroup, rules) : null);
                        var below = rules.Below(node, new DatabaseNodes(database.Database).Tree(reading, rules, top));
                        changes = new RuleChanges(
                            new[] { new RowChange(RowChangeKind.Set, Profile, Rules.Letters(run % 2 == 0 ? "RU" : "R"), null, rules.At(node), below) },
                            null);
                        Assert.That(arriving.Profiles.Contains(Profile), Is.EqualTo(belowTheRoot), "what arrives from the root's rule");
                    }

                    opening.Add(clock.Elapsed.TotalMilliseconds);

                    clock = Stopwatch.StartNew();
                    using (var reading = plain.ForWrite())
                    {
                        readings.Add(clock.Elapsed.TotalMilliseconds);

                        var part = Stopwatch.StartNew();
                        var act = RuleActs.Of(
                            reading,
                            node,
                            belowTheRoot ? (Func<StoredRules, IAclNode>)(rules => KdbxNodes.Of(database.RootGroup, rules)) : null,
                            rules => new DatabaseNodes(database.Database).Tree(reading, rules, top),
                            changes,
                            out var left);
                        compositions.Add(part.Elapsed.TotalMilliseconds);
                        Assert.That(left, Is.Empty);

                        // Every entry seeded is a node below the root, since no counted identifier
                        // begins with the tag of a kind, `STORE.md` §633, and every version below
                        // that follows is renewed.
                        var renewed = new HashSet<string>(act.Records.Select(record => record.Member(StoreKinds.ObjectMember)), StringComparer.Ordinal);
                        Assert.That(
                            entries.Take(following).Count(entry => renewed.Contains(StoreComposer.RowObject(Hex.Of(entry.Uuid.UuidBytes), Profile))),
                            Is.EqualTo(following));
                        Assert.That(act.Records, Has.Count.EqualTo(following + 1), "a renewal for every version below");

                        try
                        {
                            part = Stopwatch.StartNew();
                            var staged = plain.Stage(reading, new[] { act });
                            plans.Add(part.Elapsed.TotalMilliseconds);
                            Assert.That(staged.Refused ?? staged.Held, Is.Null);

                            part = Stopwatch.StartNew();
                            staged.Apply();
                            applications.Add(part.Elapsed.TotalMilliseconds);
                        }
                        finally
                        {
                            act.Forget();
                        }
                    }

                    totals.Add(clock.Elapsed.TotalMilliseconds);
                }

                var what = (belowTheRoot ? "an OK at a group below the root above " : "an OK above ")
                           + following.ToString(CultureInfo.InvariantCulture) + " versions that follow";
                Print(what + ", the tab's opening", opening);
                Print(what + ", the reading", readings);
                Print(what + ", the composition", compositions);
                Print(what + ", the plan", plans);
                Print(what + ", the application", applications);
                Print(what + ", the whole write", totals);

                Held(database.Database, "a read of an entry after " + OkRuns.ToString(CultureInfo.InvariantCulture) + " more of those writes", read);
                Assert.That(database.Database.DeletedObjects.UCount, Is.GreaterThanOrEqualTo((uint)((OkRuns + 1) * following)), "a deletion record for every version renewed");
            }
        }

        [TestCase(2000, 0)]
        [TestCase(2000, 20000)]
        [TestCase(2000, 100000)]
        [TestCase(2000, 400000)]
        [TestCase(9000, 0)]
        [TestCase(9000, 90000)]
        [TestCase(9000, 360000)]
        public void What_a_hold_and_an_OK_cost_after_this_many_renewals(int standing, int renewals)
        {
            // A store as writes above leave it, composed directly rather than written by as many
            // OKs, `STORE.md` §617 and §643: versions below the root that let the fixture's
            // profile through, each naming in `after` its share of the renewals made before it,
            // each an identifier at its own object's place with a deletion record, which KeePass
            // keeps for good, the deletion records in the order those writes leave them, a version
            // of each object in turn; and a row at the root. Beside 10,000 entries in a hundred
            // groups: a hold of the gate reading an entry; and an OK at the root group's dialog
            // above those versions, the reading in the writer's turn, the composition with every
            // renewal and the plan each timed apart and not applied, and then the same OK applied,
            // timed whole.
            using (var database = TemporaryDatabase.New())
            {
                var entries = Fill(database.Database, 0);
                Counted(database.Database);
                var plain = new KdbxStore(database.Database, GateVersion.Current, null, entry => null);
                StoreSeeds.Authority(plain);
                var root = Hex.Of(database.RootGroup.Uuid.UuidBytes);
                byte[] key;
                using (var reading = plain.Read())
                {
                    key = (byte[])reading.InForce.Key.Bytes.Clone();
                }

                var objects = entries.Take(standing).Select(entry => StoreComposer.RowObject(Hex.Of(entry.Uuid.UuidBytes), Profile)).ToList();
                var named = objects.Select((objectName, at) => Replaced(key, objectName, Share(renewals, at, standing))).ToList();
                for (var round = 0; round < named.Max(identifiers => identifiers.Count); round++)
                {
                    foreach (var identifiers in named.Where(identifiers => round < identifiers.Count))
                    {
                        database.Database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(identifiers[round]), DateTime.UtcNow));
                    }
                }

                var versions = objects.Select((objectName, at) => Renewed(key, objectName, named[at]))
                    .Concat(new[] { RuleVersionOf(key, StoreComposer.RowObject(root, Profile), "row:R") })
                    .ToList();
                Assert.That(database.Database.DeletedObjects.UCount, Is.EqualTo((uint)renewals), "a deletion record for every renewal");
                var written = plain.Write(new[] { new StoreAct("versions", key, StoreFormat.KeyName(key), versions, null) });
                Assert.That(written.Taken, Is.True, written.Reason);

                var what = "after " + renewals.ToString(CultureInfo.InvariantCulture) + " renewals beside "
                           + standing.ToString(CultureInfo.InvariantCulture) + " versions";
                Held(database.Database, what + ", a read of an entry", "/entries/" + entries[0].Uuid.ToHexString());

                var readings = new List<double>();
                var compositions = new List<double>();
                var plans = new List<double>();
                for (var run = 0; run <= RenewedRuns; run++)
                {
                    RuleChanges changes;
                    using (var reading = plain.Read())
                    {
                        var rules = StoredRules.Of(reading);
                        var below = rules.Below(root, new DatabaseNodes(database.Database).Tree(reading, rules, database.Database.RootGroup));
                        changes = new RuleChanges(
                            new[] { new RowChange(RowChangeKind.Set, Profile, Rules.Letters("RU"), null, rules.At(root), below) }, null);
                    }

                    var whole = Stopwatch.StartNew();
                    using (var reading = plain.ForWrite())
                    {
                        readings.Add(whole.Elapsed.TotalMilliseconds);

                        var part = Stopwatch.StartNew();
                        var act = RuleActs.Of(
                            reading, root, null, rules => new DatabaseNodes(database.Database).Tree(reading, rules, database.Database.RootGroup), changes, out var left);
                        compositions.Add(part.Elapsed.TotalMilliseconds);
                        Assert.That(left, Is.Empty);
                        Assert.That(act.Records, Has.Count.EqualTo(standing + 1), "a renewal for every version below");

                        try
                        {
                            part = Stopwatch.StartNew();
                            var staged = plain.Stage(reading, new[] { act });
                            plans.Add(part.Elapsed.TotalMilliseconds);
                            Assert.That(staged.Refused ?? staged.Held, Is.Null);

                            if (run == RenewedRuns)
                            {
                                part = Stopwatch.StartNew();
                                staged.Apply();
                                var application = part.Elapsed.TotalMilliseconds;
                                TestContext.Out.WriteLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "[{0}] {1}, an OK above them applied: the application {2:F2} ms, the whole write {3:F2} ms",
                                    Built,
                                    what,
                                    application,
                                    whole.Elapsed.TotalMilliseconds));
                            }
                        }
                        finally
                        {
                            act.Forget();
                        }
                    }
                }

                Print(what + ", an OK above them, the reading", readings);
                Print(what + ", an OK above them, the composition", compositions);
                Print(what + ", an OK above them, the plan", plans);
                Assert.That(
                    database.Database.DeletedObjects.UCount, Is.GreaterThanOrEqualTo((uint)(renewals + standing)), "the OK applied renewed every version");
            }
        }

        /// <summary>The renewals of a store's that the version at this place of so many below the root names.</summary>
        private static int Share(int renewals, int at, int standing)
        {
            return (renewals / standing) + (at < renewals % standing ? 1 : 0);
        }

        /// <summary>
        /// The identifiers of as many versions of this object a renewal replaced, each at the
        /// object's own place.
        /// </summary>
        private static List<byte[]> Replaced(byte[] key, string objectName, int count)
        {
            var slash = objectName.IndexOf('/');
            var place = StoreComposer.IdentifierOf(
                key, "version", objectName.Substring(0, slash), StoreComposer.ProfileTag(key, objectName.Substring(slash + 1)), new string('0', 64));
            var replaced = new List<byte[]>(count);
            for (var index = 0; index < count; index++)
            {
                var identifier = (byte[])place.Clone();
                identifier[12] = (byte)(index >> 24);
                identifier[13] = (byte)(index >> 16);
                identifier[14] = (byte)(index >> 8);
                identifier[15] = (byte)index;
                replaced.Add(identifier);
            }

            return replaced;
        }

        /// <summary>
        /// A version of this object that takes the rule off for the fixture's profile, naming these
        /// identifiers: what a version below is after as many renewals of it.
        /// </summary>
        private static StoreRecord Renewed(byte[] key, string objectName, IEnumerable<byte[]> replaced)
        {
            var named = replaced.Select(Hex.Of).OrderBy(hex => hex, StringComparer.Ordinal);
            return StoreRecord.Compose(key, "version", new Dictionary<string, string>
            {
                { "after", string.Join(" ", named) },
                { "doc", "off:R" },
                { "object", objectName },
            });
        }

        /// <summary>
        /// Every group under the root and every entry given an identifier counted out rather than
        /// drawn: a mark of its own in the first four bytes and its place in the last four, so a
        /// run repeats what the tree holds, and no identifier begins with the tag of a kind but
        /// where a tag meets the mark, which the run asserts against.
        /// </summary>
        private static void Counted(PwDatabase database)
        {
            var groups = database.RootGroup.GetGroups(true).ToList();
            var entries = database.RootGroup.GetEntries(true).ToList();
            for (var index = 0; index < groups.Count; index++)
            {
                groups[index].Uuid = new PwUuid(Counter(0x67, index));
            }

            for (var index = 0; index < entries.Count; index++)
            {
                entries[index].Uuid = new PwUuid(Counter(0x65, index));
            }
        }

        /// <summary>Sixteen bytes: `k`, `p`, `g` and a byte for the kind of node, eight zeros, and the place.</summary>
        private static byte[] Counter(byte kind, int index)
        {
            var bytes = new byte[16];
            bytes[0] = 0x6b;
            bytes[1] = 0x70;
            bytes[2] = 0x67;
            bytes[3] = kind;
            bytes[12] = (byte)(index >> 24);
            bytes[13] = (byte)(index >> 16);
            bytes[14] = (byte)(index >> 8);
            bytes[15] = (byte)index;
            return bytes;
        }

        /// <summary>A request of the gate's timed in its holds, through a vault of its own, the first hold printed apart.</summary>
        private static void Held(PwDatabase database, string what, string path)
        {
            var hold = new Timed();
            var vault = new KdbxVault(
                database,
                new DatabaseWriter(CurrentThread.Instance, saved => saved.Save(null), null, null, null),
                TestTags.Fixed,
                hold,
                null,
                new StoreReadings(database, hold, null));
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
            Report(what, hold, () => Send(router, vault, path), true);
        }

        /// <summary>A version of a rule's object under this key, holding this text and naming nothing.</summary>
        private static StoreRecord RuleVersionOf(byte[] key, string objectName, string doc)
        {
            return StoreRecord.Compose(key, "version", new Dictionary<string, string>
            {
                { "after", string.Empty },
                { "doc", doc },
                { "object", objectName },
            });
        }

        /// <summary>A version of this object under this key, naming these identifiers as replaced.</summary>
        private static StoreRecord VersionOf(byte[] key, string objectName, string after)
        {
            return StoreRecord.Compose(key, "version", new Dictionary<string, string>
            {
                { "after", after },
                { "doc", "sw:deny" },
                { "object", objectName },
            });
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

        private static List<PwEntry> Fill(PwDatabase database, int history, PwGroup parent = null)
        {
            var groups = Enumerable.Range(0, Groups).Select(index =>
            {
                var group = new PwGroup(true, true, "Invented " + index, PwIcon.Folder);
                (parent ?? database.RootGroup).AddGroup(group, true);
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
        /// An authority, a revocation for each of many clients, a rule granting the fixture's
        /// profile every letter at the root, and the summary entry restating them all, which is
        /// about the size of the store the growth run left after a thousand writes, `STORE.md`
        /// §643.
        /// </summary>
        private static void Store(PwDatabase database)
        {
            var key = StoreComposer.Key("cost");
            var group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted);
            database.RootGroup.AddGroup(group, true);
            group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                key, "authority", "authority:" + Authority,
                StoreComposer.Content(("id", Authority), ("storeKey", StoreComposer.HexOf(key))))), true);

            var rule = StoreComposer.RowObject(Hex.Of(database.RootGroup.Uuid.UuidBytes), Profile);
            var granting = StoreComposer.RuleVersion(key, rule, "row:CRUSDLAP");
            group.AddEntry(StoreEntries.Of(granting), true);

            var items = new List<string>
            {
                StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")),
                StoreComposer.RuleVersionItem(granting, rule, "row:CRUSDLAP"),
            };
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
        /// This many names on the list, each an addition naming no removal, in the root group;
        /// answers the removals the names were added after, none.
        /// </summary>
        private static int Named(PwDatabase database, byte[] key, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var name = "named-" + index.ToString("D2", CultureInfo.InvariantCulture);
                database.RootGroup.AddEntry(KeePassGate.Bridge.StoreEntries.Of(ProfileActs.Addition(key, name, Array.Empty<string>())), true);
            }

            return 0;
        }

        /// <summary>
        /// The fixture's profile removed as often as the room allows, each removal an item of the
        /// summary entry, and added again by an addition naming all those removals; answers how
        /// many removals there are.
        /// </summary>
        /// <remarks>
        /// As many as leave the summary entry an item name beside the revocations it restates, and
        /// as fit the addition's content, which lists each removal's identifier.
        /// </remarks>
        private static int Renamed(PwDatabase database, StoredAdmissions served, byte[] key)
        {
            var listed = StoreComposer.HexOf(StoreComposer.IdentifierOf(key, "removed", "profile:" + Profile, 0, new string('0', 64))).Length + 1;
            var count = Math.Min(StoreFormat.MaxItems - Decisions - 3, (StoreFormat.MaxWritten - 256) / listed);
            var identifiers = new List<string>();
            var items = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var identifier = StoreComposer.HexOf(StoreComposer.IdentifierOf(
                    key, "removed", "profile:" + Profile, 0, index.ToString("x64", CultureInfo.InvariantCulture)));
                identifiers.Add(identifier);
                items.Add(StoreComposer.Item(("id", identifier), ("kind", "removed"), ("name", Profile)));
            }

            served.Restated(items);
            database.RootGroup.AddEntry(KeePassGate.Bridge.StoreEntries.Of(ProfileActs.Addition(key, Profile, identifiers)), true);
            return count;
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
        private static List<double> Pairings(int history, Action<PwDatabase> save, int newNames = 0)
        {
            using (var database = TemporaryDatabase.New())
            {
                Fill(database.Database, history);
                var served = StoreSeeds.Authority(new KdbxStore(database.Database, GateVersion.Current, null));
                StoreSeeds.Name(new KdbxStore(database.Database, GateVersion.Current, null), Profile);
                database.Save();

                var writer = new DatabaseWriter(CurrentThread.Instance, save, null, null, null);
                var times = new List<double>();
                for (var run = 0; run <= Runs; run++)
                {
                    using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
                    {
                        // Each pairing approves names no pairing before it named, where it is asked
                        // to add names at all.
                        var profiles = newNames == 0
                            ? new[] { Profile }
                            : Enumerable.Range(0, newNames)
                                .Select(index => string.Format(CultureInfo.InvariantCulture, "new-{0:D2}-{1:D2}", run, index))
                                .ToArray();
                        var admission = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles, served);
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
