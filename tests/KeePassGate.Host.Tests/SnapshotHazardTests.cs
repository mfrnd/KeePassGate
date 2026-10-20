using System.Linq;
using System.Reflection;
using System.Threading;
using KeePass.Plugins;
using KeePassLib;
using KeePassLib.Collections;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The hazard that shaped the design: KeePass's group dialog copies a group's custom data when
    /// it opens and writes the copy back on OK, so anything written to the live group while it is
    /// open is destroyed by OK and survives Cancel.
    /// </summary>
    /// <remarks>
    /// Measured in two halves, because the dialog itself cannot be stood up outside a running
    /// KeePass. The first half is the mechanism, read off the real KeePass by reflection: the
    /// snapshot field, its type, and the fact that a group's custom data can be assigned wholesale.
    /// The second half is the behavior, reproduced with KeePass's own types, its own `CloneDeep`
    /// and a real `PwGroup`, so that only the timing is simulated.
    ///
    /// This is why an admission record is load bearing. A record lost this way makes its
    /// certificate inert rather than invisible, which is the difference between a client that has
    /// to pair again and a credential nobody can see or revoke.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SnapshotHazardTests
    {
        private const string GroupFormTypeName = "KeePass.Forms.GroupForm";
        private const string SnapshotFieldName = "m_sdCustomData";

        private TemporaryDatabase _database;
        private CustomDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _store = new CustomDataStore(_database.Database);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void The_group_dialog_still_holds_a_copy_of_the_custom_data()
        {
            var groupForm = typeof(Plugin).Assembly.GetType(GroupFormTypeName, false);
            Assert.That(groupForm, Is.Not.Null, GroupFormTypeName + " is gone from this KeePass.");

            var snapshot = groupForm.GetField(SnapshotFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(snapshot, Is.Not.Null,
                "The snapshot field is gone. Either KeePass fixed this, or it moved and the hazard is now unmeasured.");
            Assert.That(snapshot.FieldType, Is.EqualTo(typeof(StringDictionaryEx)));
        }

        [Test]
        public void A_groups_custom_data_can_be_replaced_wholesale_by_KeePass_and_not_by_us()
        {
            // Replacing it wholesale is what makes the write-back destructive rather than a merge.
            // The setter is not public, which is a second thing worth knowing: a plugin can only
            // set and remove keys one at a time, so it can never do this to somebody else.
            var setter = typeof(PwGroup).GetProperty("CustomData").GetSetMethod(true);

            Assert.That(setter, Is.Not.Null);
            Assert.That(setter.IsPublic, Is.False);
            Assert.That(typeof(StringDictionaryEx).GetMethod("CloneDeep"), Is.Not.Null);
        }

        [Test]
        public void The_database_settings_dialog_does_not_hold_one()
        {
            // The KPG tab lives on that dialog, so this is the difference between its buttons being
            // safe and being a way to lose an authority.
            var settingsForm = typeof(Plugin).Assembly.GetType(
                KeePassContract.DatabaseSettingsFormTypeName, false);

            var snapshots = settingsForm
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(StringDictionaryEx))
                .Select(f => f.Name)
                .ToList();

            Assert.That(snapshots, Is.Empty,
                "The settings dialog has grown a custom data snapshot: " + string.Join(", ", snapshots));
        }

        [Test]
        public void A_write_made_while_a_group_dialog_is_open_is_destroyed_by_OK()
        {
            // Written straight to the group, because that is what the hazard is about and the
            // plugin's own store no longer goes there. A node's access rule still does, which is
            // why this stays measured.
            var dialog = DialogOpensOn(_database.RootGroup);

            _database.RootGroup.CustomData.Set("KPG", "a rule written underneath the dialog");
            Assume.That(_database.RootGroup.CustomData.Exists("KPG"), Is.True);

            dialog.PressOk();

            Assert.That(_database.RootGroup.CustomData.Exists("KPG"), Is.False);
        }

        [Test]
        public void A_write_through_the_store_is_out_of_that_dialogs_reach_entirely()
        {
            // What the move bought. The store writes to the database's own custom data, which no
            // group dialog copies, so neither OK nor Cancel can reach it.
            var dialog = DialogOpensOn(_database.RootGroup);

            _store.Set("KPG.Test.WrittenWhileOpen", "value");
            dialog.PressOk();

            Assert.That(_store.Get("KPG.Test.WrittenWhileOpen"), Is.EqualTo("value"));
            Assert.That(_database.RootGroup.CustomData.Exists("KPG.Test.WrittenWhileOpen"), Is.False,
                "nothing of this plugin's is on the group for that dialog to take back");
        }

        [Test]
        public void An_authority_created_while_a_group_dialog_is_open_survives_its_OK()
        {
            // This was the shape that made the admission record load bearing: the authority went
            // in silently and came back reported as absent. It lives in the database's own custom
            // data now, which that dialog does not copy.
            var service = new AuthorityService(_store, NullGateLog.Instance);
            var dialog = DialogOpensOn(_database.RootGroup);

            var created = service.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            dialog.PressOk();

            using (var reading = service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
            }
        }

        [Test]
        public void An_authority_created_before_the_dialog_opened_survives_OK()
        {
            // The ordinary case, and the reason this is a hazard rather than a bar on the design:
            // what the dialog copied, the dialog writes back.
            var service = new AuthorityService(_store, NullGateLog.Instance);
            var created = service.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            string digest;
            using (var before = service.Read())
            {
                digest = before.Material.AuthorityDigest;
            }

            var dialog = DialogOpensOn(_database.RootGroup);
            dialog.PressOk();

            using (var after = service.Read())
            {
                Assert.That(after.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(after.Material.AuthorityDigest, Is.EqualTo(digest));
            }
        }

        [Test]
        public void An_admission_record_written_while_the_dialog_is_open_survives_its_OK()
        {
            // The case the design turned on while records lived on the root group, because pairing
            // writes one of these at a moment nobody controls, and the dialog's OK destroyed it.
            // They live in the database's own custom data now, which no group dialog copies, so
            // the record the operator approved is still there afterwards.
            var service = new AuthorityService(_store, NullGateLog.Instance);
            service.Create();

            var dialog = DialogOpensOn(_database.RootGroup);
            _store.Set(AuthorityKeys.ClientPrefix + "abc123", "an admission record");

            dialog.PressOk();

            using (var reading = service.Read())
            {
                Assert.That(reading.ClientRecordCount, Is.EqualTo(1),
                    "A client approved while a group dialog was open is still admitted after its OK.");
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present),
                    "The authority itself predates the dialog, so it comes back.");
            }
        }

        private static SnapshotDialog DialogOpensOn(PwGroup group)
        {
            return new SnapshotDialog(group);
        }

        /// <summary>
        /// What KeePass's group dialog does to custom data, with its own types: a deep copy when it
        /// opens, and that copy assigned back on OK. Only the timing is ours.
        /// </summary>
        private sealed class SnapshotDialog
        {
            private readonly PwGroup _group;
            private readonly StringDictionaryEx _snapshot;

            public SnapshotDialog(PwGroup group)
            {
                _group = group;
                _snapshot = group.CustomData.CloneDeep();
            }

            public void PressOk()
            {
                // Through the setter KeePass uses from inside its own assembly, which a plugin
                // cannot reach. Reflection here is modeling KeePass, not working around it.
                typeof(PwGroup).GetProperty("CustomData")
                    .GetSetMethod(true)
                    .Invoke(_group, new object[] { _snapshot });
                _group.Touch(true, false);
            }

            public void PressCancel()
            {
            }
        }
    }
}
