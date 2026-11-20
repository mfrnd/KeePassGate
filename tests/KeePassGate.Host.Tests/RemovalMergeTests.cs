using System;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own synchronize makes of a removal through the API, against two real copies
    /// of one database.
    /// </summary>
    /// <remarks>
    /// A merge decides a group, an entry and the recycle bin setting by their times, and takes a
    /// group whole, its own fields with it, from whichever copy's group was modified last. So
    /// whatever a removal stamps claims to be the newer edit at the next synchronize, and these
    /// hold the vault to stamping what KeePass's own delete stamps and nothing more. The other
    /// copy's edit is given a time of its own, after the copies were made and before the removal,
    /// so that the order is the test's rather than the clock's. Both directions are asked, since
    /// `File > Synchronize` can be run on either copy. A rule is a record of the plugin's store,
    /// merged as an entry of its own, and a rule narrowed on the other copy is held beside the
    /// group's fields.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class RemovalMergeTests
    {
        private const string Notes = "notes written on the other copy";

        private TemporaryDatabase _here;
        private TemporaryDatabase _elsewhere;
        private KdbxVault _vault;
        private DateTime _made;
        private PwGroup _shared;
        private PwGroup _empty;
        private PwEntry _entry;

        [SetUp]
        public void SetUp()
        {
            _here = TemporaryDatabase.New();
            _elsewhere = TemporaryDatabase.New();
            _made = DateTime.UtcNow.AddMinutes(-10);

            // Two copies of one database. KeePass matches nodes by identifier, so the other copy is
            // made by merging this copy into an empty database with the same root.
            _elsewhere.Database.RootGroup.Uuid = _here.Database.RootGroup.Uuid;

            _shared = new PwGroup(true, true, "Shared", PwIcon.Folder);
            _here.Database.RootGroup.AddGroup(_shared, true);
            DatabaseRules.Rule(_here.Database, _shared, ("agent", "RDL"));
            _empty = new PwGroup(true, true, "Empty", PwIcon.Folder);
            _shared.AddGroup(_empty, true);
            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _shared.AddEntry(_entry, true);

            foreach (var database in new[] { _here.Database, _elsewhere.Database })
            {
                database.RecycleBinEnabled = true;
                database.RecycleBinChanged = _made;
            }

            foreach (var group in new[] { _here.Database.RootGroup, _shared, _empty })
            {
                group.LastModificationTime = _made;
                group.LocationChanged = _made;
            }

            _entry.LastModificationTime = _made;
            _entry.LocationChanged = _made;

            _elsewhere.Database.MergeIn(_here.Database, PwMergeMethod.Synchronize, null);
            _here.Save();

            _vault = new KdbxVault(
                _here.Database,
                new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                null);
        }

        [TearDown]
        public void TearDown()
        {
            _here.Dispose();
            _elsewhere.Dispose();
        }

        [TestCase(true, true, TestName = "a group's notes and rule changed elsewhere survive a subgroup binned here, synchronized here")]
        [TestCase(false, true, TestName = "a group's notes and rule changed elsewhere survive a subgroup binned here, synchronized elsewhere")]
        [TestCase(true, false, TestName = "a group's notes and rule changed elsewhere survive a subgroup deleted here, synchronized here")]
        [TestCase(false, false, TestName = "a group's notes and rule changed elsewhere survive a subgroup deleted here, synchronized elsewhere")]
        public void A_group_changed_on_another_copy_survives_a_subgroup_removed_here(bool intoHere, bool bin)
        {
            // The parent was stamped as modified by the removal of its child, which KeePass's own
            // delete never does, and a group keeps no history: the next synchronize took this
            // copy's parent whole, and what the operator had written on the other copy was gone,
            // with nothing to show it. The rule narrowed there is a version of the store, which a
            // merge brings beside the version it replaces, and the narrower answer stands.
            _here.Database.RecycleBinEnabled = bin;
            var there = Find(_elsewhere, _shared);
            there.Notes = Notes;
            there.LastModificationTime = _made.AddMinutes(5);
            DatabaseRules.Rule(_elsewhere.Database, there, ("agent", ""));

            Assert.That(_vault.DeleteGroup(Hex(_empty), VaultAskings.Free).Ok, Is.True);

            var merged = Synchronize(intoHere);

            Assert.That(Find(merged, _shared).Notes, Is.EqualTo(Notes));
            using (var reading = DatabaseAuthority.Store(merged).Read())
            {
                var row = StoredRules.Of(reading).At(DatabaseRules.Id(_shared.Uuid)).Row("agent");
                Assert.That(row.Answers, Is.True);
                Assert.That(row.Grants.SameAs(AclGrant.Nothing), Is.True, "the rule narrowed on the other copy");
            }
        }

        [TestCase(true, TestName = "an edit made elsewhere stays an entry's content after it is binned here, synchronized here")]
        [TestCase(false, TestName = "an edit made elsewhere stays an entry's content after it is binned here, synchronized elsewhere")]
        public void An_edit_made_on_another_copy_stays_what_the_entry_holds_after_it_is_binned_here(bool intoHere)
        {
            // Stamped as modified by its removal, the binned copy was the newer content, and the
            // other copy's edit, made before the removal, went into the entry's history.
            var there = Find(_elsewhere, _entry);
            there.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "edited on the other copy"));
            there.LastModificationTime = _made.AddMinutes(5);

            Assert.That(_vault.DeleteEntry(Hex(_entry), VaultAskings.Free).Ok, Is.True);

            var merged = Synchronize(intoHere);
            var entry = Find(merged, _entry);

            Assert.That(entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("edited on the other copy"));
            Assert.That(entry.ParentGroup.Uuid, Is.EqualTo(_here.Database.RecycleBinUuid),
                "and the entry is still in the bin, where its removal moved it");
        }

        [TestCase(true, TestName = "a recycle bin turned off elsewhere stays off after the first removal here, synchronized here")]
        [TestCase(false, TestName = "a recycle bin turned off elsewhere stays off after the first removal here, synchronized elsewhere")]
        public void A_recycle_bin_turned_off_on_another_copy_stays_off_after_the_first_removal_here(bool intoHere)
        {
            // Making the first bin stamped the moment the bin was last decided about, which
            // KeePass's own first delete leaves alone, and a merge reads that moment. So this
            // copy's first removal outranked the operator turning the bin off on the other copy,
            // which `DESIGN.md` §138 gives as the way to make a client's removals final.
            _elsewhere.Database.RecycleBinEnabled = false;
            _elsewhere.Database.RecycleBinChanged = _made.AddMinutes(5);

            Assert.That(_vault.DeleteEntry(Hex(_entry), VaultAskings.Free).Ok, Is.True);
            Assume.That(_here.Database.RecycleBinUuid, Is.Not.EqualTo(PwUuid.Zero), "the removal made this copy's first bin");

            Assert.That(Synchronize(intoHere).RecycleBinEnabled, Is.False);
        }

        private PwDatabase Synchronize(bool intoHere)
        {
            var target = intoHere ? _here.Database : _elsewhere.Database;
            target.MergeIn(intoHere ? _elsewhere.Database : _here.Database, PwMergeMethod.Synchronize, null);
            return target;
        }

        private static PwGroup Find(TemporaryDatabase database, PwGroup group)
        {
            return Find(database.Database, group);
        }

        private static PwGroup Find(PwDatabase database, PwGroup group)
        {
            return database.RootGroup.FindGroup(group.Uuid, true);
        }

        private static PwEntry Find(TemporaryDatabase database, PwEntry entry)
        {
            return Find(database.Database, entry);
        }

        private static PwEntry Find(PwDatabase database, PwEntry entry)
        {
            return database.RootGroup.FindEntry(entry.Uuid, true);
        }

        private static string Hex(PwGroup group)
        {
            return group.Uuid.ToHexString();
        }

        private static string Hex(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }
    }
}
