using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Security;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Which of KeePass's windows a write into the plugin's store meets, `STORE.md` §626 and §644
    /// item 14, against the windows `DESIGN.md` §124 lists, and the refusal the measurement asks
    /// for.
    /// </summary>
    /// <remarks>
    /// Measured as `SnapshotHazardTests` measures the group dialog, in two halves, since a dialog
    /// can be built outside a running KeePass but not shown. The mechanism is read off the real
    /// types: which watched window's own code puts an entry's strings, history or attachments
    /// back, and which removes from an entry's lists. The behavior is reproduced with KeePass's own
    /// types for the window that puts back: the entry dialog's copy put back over a write of the
    /// plugin's own.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class StoreWritesUnderDialogsTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private ImageList _icons;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            Assert.That(_dialogs.Start(), Is.True);
            _writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, NullGateLog.Instance);

            // The entry dialog refuses a null image list, and what the list holds is never looked at.
            _icons = new ImageList();
        }

        [TearDown]
        public void TearDown()
        {
            _icons.Dispose();
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [TestCase(KeePassContract.GroupFormTypeName, "", "")]
        [TestCase(
            KeePassContract.EntryFormTypeName,
            "PwEntry.CreateBackup PwEntry.set_Binaries PwEntry.set_History PwEntry.set_Strings",
            "PwObjectList<PwEntry>.Clear PwObjectList<PwEntry>.Remove PwObjectList<PwEntry>.RemoveAt")]
        [TestCase(KeePassContract.MaintenanceFormTypeName, "", "PwObjectList<PwDeletedObject>.Clear PwObjectList<PwEntry>.Remove")]
        [TestCase(KeePassContract.DatabaseSettingsFormTypeName, "", "")]
        [TestCase(KeePassContract.TanWizardFormTypeName, "", "")]
        [TestCase(KeePassContract.StatusProgressFormTypeName, "", "")]
        [TestCase(KeePassContract.DataEditorFormTypeName, "", "")]
        public void What_each_watched_window_writes_into_an_entry(string window, string puts, string removes)
        {
            // The entry dialog alone puts an entry's strings, history and attachments back, which
            // are a record's content and the versions it counts by; it removes from the entry's
            // history at the operator's presses and as it saves. Database Maintenance removes
            // history items from entries and clears the database's deletion records, each at the
            // press of a button, and puts no copy of an entry back over a write: pressed where a
            // record counts by its history, the removal is the loss `STORE.md` §622 describes. No
            // other watched window calls any of these. Each call is named by the type that
            // declares it, a list by what it holds, and read in every type nested in the window's,
            // the compiler's own classes for anonymous methods among them.
            var type = typeof(Plugin).Assembly.GetType(window, false);
            Assert.That(type, Is.Not.Null, window + " is gone from this KeePass.");

            var calls = CallsIn(type).ToList();
            var putting = new[] { "set_Strings", "set_History", "set_Binaries", "AssignProperties", "CreateBackup" };
            var removing = new[] { "Clear", "Remove", "RemoveAt" };
            Assert.That(
                Names(calls.Where(call => call.DeclaringType == typeof(PwEntry) && putting.Contains(call.Name))),
                Is.EqualTo(Words(puts)),
                "what it puts back into an entry");
            Assert.That(
                Names(calls.Where(call => IsList(call.DeclaringType) && removing.Contains(call.Name))),
                Is.EqualTo(Words(removes)),
                "what it removes from KeePass's lists");
        }

        [Test]
        public void The_entry_dialog_s_copy_put_back_over_a_write_takes_off_the_protection_the_write_put_back()
        {
            // The behavior, reproduced: the dialog copies the entry's strings as it opens, and its
            // OK assigns the copy, so content the plugin protected again while it was open is
            // unprotected again, `STORE.md` §628 (S18).
            StoreSeeds.Authority(Store());
            var authority = Entry("authority");
            var content = authority.Strings.Get(StoreFormat.DataField);
            authority.CreateBackup(_database.Database);
            authority.Strings.Set(StoreFormat.DataField, content.WithProtection(false));

            using (var form = new PwEntryForm())
            {
                form.InitEx(authority, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                var copy = typeof(PwEntryForm).GetField("m_vStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(copy, Is.Not.Null, "the dialog holds no copy of the strings under this name");

                Assert.That(NameWritten(Store()), Is.True);
                Assert.That(authority.Strings.Get(StoreFormat.DataField).IsProtected, Is.True, "the write protected it again");

                authority.Strings = ((ProtectedStringDictionary)copy.GetValue(form)).CloneDeep();
            }

            Assert.That(authority.Strings.Get(StoreFormat.DataField).IsProtected, Is.False, "and the dialog's OK took that back");
        }

        [Test]
        public void A_write_asks_about_every_entry_it_deletes_sets_back_or_protects_and_about_nothing_it_adds()
        {
            // Every write deletes the summary entry it finds, `STORE.md` §627 step 4, so that entry
            // is asked about each time, and nothing the write only adds.
            StoreSeeds.Authority(Store());
            var asked = new List<PwEntry>();
            var store = new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance, entry =>
            {
                asked.Add(entry);
                return null;
            });

            var head = Entry(StoreKinds.Head);
            Assert.That(NameWritten(store), Is.True);
            Assert.That(asked, Is.EqualTo(new[] { head }), "the summary entry it deletes, and nothing it adds");

            asked.Clear();
            head = Entry(StoreKinds.Head);
            var authority = Entry("authority");
            var content = authority.Strings.Get(StoreFormat.DataField);
            authority.Strings.Set(StoreFormat.DataField, content.WithProtection(false));
            Assert.That(NameWritten(store), Is.True);
            Assert.That(asked, Is.EqualTo(new[] { head, authority }), "and the content it protects again");

            asked.Clear();
            head = Entry(StoreKinds.Head);
            authority.CreateBackup(_database.Database);
            authority.Strings.Set(StoreFormat.DataField, new ProtectedString(true, content.ReadString() + " "));
            Assert.That(NameWritten(store), Is.True);
            Assert.That(asked, Is.EqualTo(new[] { head, authority }), "and the record it sets back");
            Assert.That(authority.Strings.ReadSafe(StoreFormat.DataField), Is.EqualTo(content.ReadString()));

            asked.Clear();
            Assert.That(new AuthorityService(store, NullGateLog.Instance).Recreate().Succeeded, Is.True);
            Assert.That(asked, Does.Contain(authority), "the authority it deletes");
            Assert.That(asked.All(entry => _database.RootGroup.FindEntry(entry.Uuid, true) == null), Is.True,
                "and nothing it keeps");
        }

        [Test]
        public void A_write_that_deletes_an_entry_an_entry_dialog_holds_is_refused_for_that_dialog()
        {
            // `STORE.md` §626: once the plan exists, every entry it deletes, sets back or protects
            // is asked about. Recreate deletes the authority's entry.
            var first = StoreSeeds.Authority(Store());

            using (var form = EntryDialog(Entry("authority")))
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    var recreated = Service().Recreate();

                    Assert.That(recreated.Succeeded, Is.False);
                    Assert.That(recreated.Message, Does.Contain("an entry dialog is open on the entry this would be written to"));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(StoreAuthorities.Served(Store().Read()), Is.EqualTo(first), "nothing was written");
            Assert.That(Service().Recreate().Succeeded, Is.True, "and the same write goes through once the dialog closes");
        }

        [Test]
        public void A_keep_that_retires_an_entry_an_entry_dialog_holds_is_refused_and_another_entry_s_dialog_lets_it_through()
        {
            var both = StoreSeeds.Undecided(Store());

            using (var form = EntryDialog(EntryOf(both[1])))
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    var kept = _writer.Keep(_database.Database, both[0], both);
                    Assert.That(kept.Succeeded, Is.False);
                    Assert.That(kept.Message, Does.Contain("entry dialog"));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            using (var form = EntryDialog(EntryOf(both[0])))
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(_writer.Keep(_database.Database, both[0], both).Succeeded, Is.True,
                        "a dialog on the authority kept holds nothing the keep deletes, sets back or protects");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_keep_is_written_under_Database_Maintenance_and_the_tab_s_OK_is_not()
        {
            // `STORE.md` §626: Database Maintenance puts back its copy of the custom data and puts
            // no copy of an entry back, so a keep, which writes no custom data and saves nothing,
            // is written under it. The store write of the settings dialog's OK is refused under it
            // as a whole, as under the TAN wizard, and the tab's custom data changes beside that
            // write are asked nothing. Neither window can stand beside the settings dialog, each
            // being modal, so the writer is asked directly here.
            var both = StoreSeeds.Undecided(Store());

            using (var form = new DatabaseOperationsForm())
            {
                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);
                try
                {
                    var recreated = Service().Recreate();
                    Assert.That(recreated.Succeeded, Is.False, "the OK's store write is refused");
                    Assert.That(recreated.Message, Does.Contain("the database maintenance dialog is open on this database"));
                    Assert.That(_writer.Keep(_database.Database, both[0], both).Succeeded, Is.True);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(StoreAuthorities.Served(Store().Read()), Is.EqualTo(both[0]));
        }

        [Test]
        public void The_OK_of_a_node_dialog_s_tab_is_refused_whole_under_Database_Maintenance_and_written_once_it_has_gone()
        {
            // `STORE.md` §629: the write at a node dialog's close goes through the writer's guard
            // as the settings dialog's OK does, so under a window that puts the database's own
            // custom data back the write adds nothing, and the reason is what the operator is told.
            StoreSeeds.Authority(Store());
            var group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(group, true);
            var node = Hex.Of(group.Uuid.UuidBytes);

            using (var dialog = new GroupForm())
            using (var form = new DatabaseOperationsForm())
            {
                dialog.InitEx(group, false, null, _database.Database);
                var tab = new NodeDialogs(NullGateLog.Instance, _writer).TabFor(dialog);
                Assert.That(tab.Sheet.Set("reader", ".R......"), Is.Null);

                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(tab.Rules.Write(tab.Sheet.Changes), Does.Contain("the database maintenance dialog is open on this database"));
                    Assert.That(Rows(node), Is.Empty, "a write refused adds nothing");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                Assert.That(tab.Rules.Write(tab.Sheet.Changes), Is.Null);
                Assert.That(Rows(node), Is.EqualTo(new[] { "reader" }));
            }
        }

        /// <summary>The profiles the store holds a row for at a node, as a reading made now reads them.</summary>
        private IReadOnlyList<string> Rows(string node)
        {
            using (var reading = Store().Read())
            {
                return KeePassGate.Acl.StoredRules.Of(reading).At(node).Profiles;
            }
        }

        /// <summary>
        /// Every method a type's own code calls, its nested types' included, the classes the
        /// compiler makes for anonymous methods among them, read off the IL as
        /// `KeePassContractTests.Calls` reads it.
        /// </summary>
        private static IEnumerable<MethodBase> CallsIn(Type type)
        {
            var declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                           | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var methods = type.GetMethods(declared).Cast<MethodBase>()
                .Concat(type.GetConstructors(declared))
                .Where(method => method.GetMethodBody() != null);
            return methods.SelectMany(CallsOf)
                .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(CallsIn));
        }

        /// <summary>The methods a method's body calls, each resolved with its declaring type.</summary>
        private static IEnumerable<MethodBase> CallsOf(MethodBase caller)
        {
            var body = caller.GetMethodBody().GetILAsByteArray();
            var typeArguments = caller.DeclaringType.IsGenericType ? caller.DeclaringType.GetGenericArguments() : null;
            var methodArguments = caller.IsGenericMethod ? caller.GetGenericArguments() : null;
            for (var i = 0; i + 4 < body.Length; i++)
            {
                if (body[i] != 0x28 && body[i] != 0x6F && body[i] != 0x73)
                {
                    continue;
                }

                MethodBase called = null;
                try
                {
                    called = caller.Module.ResolveMethod(BitConverter.ToInt32(body, i + 1), typeArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                    // Not a method token, which the byte by byte scan meets in operands.
                }
                catch (BadImageFormatException)
                {
                    // The same, for a token that names no row.
                }

                if (called != null && called.DeclaringType != null)
                {
                    yield return called;
                }
            }
        }

        /// <summary>Whether a type is a list of KeePass's own, whatever it holds.</summary>
        private static bool IsList(Type type)
        {
            return type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PwObjectList<>);
        }

        /// <summary>A method by its declaring type and its name, a list by what it holds.</summary>
        private static string Named(MethodBase method)
        {
            var type = method.DeclaringType;
            var name = type.IsGenericType
                ? type.Name.Substring(0, type.Name.IndexOf('`')) + "<" + string.Join(",", type.GetGenericArguments().Select(argument => argument.Name)) + ">"
                : type.Name;
            return name + "." + method.Name;
        }

        private static IReadOnlyList<string> Names(IEnumerable<MethodBase> methods)
        {
            return methods.Select(Named).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        private static IReadOnlyList<string> Words(string names)
        {
            return names.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The store written through the writer, as the settings dialog's OK writes it.</summary>
        private AuthorityService Service()
        {
            return new AuthorityService(
                new OpenDatabase(_database.Database, delegate { }, _writer, NullGateLog.Instance).Store, NullGateLog.Instance);
        }

        private KdbxStore Store()
        {
            return new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance);
        }

        private PwEntry Entry(string kind)
        {
            return _database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == kind);
        }

        private PwEntry EntryOf(string authority)
        {
            return _database.RootGroup.GetEntries(true).Single(entry =>
                entry.Strings.ReadSafe("KPG.Kind") == "authority" && entry.Strings.ReadSafe("KPG.Data").Contains(authority));
        }

        private PwEntryForm EntryDialog(PwEntry entry)
        {
            var form = new PwEntryForm();
            form.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
            return form;
        }

        /// <summary>A write of the plugin's own that touches no authority, a profile name.</summary>
        private static bool NameWritten(KdbxStore store)
        {
            byte[] key;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            return store.Write(new[] { new StoreAct("a name", key, StoreFormat.KeyName(key), new[] { name }, null) }).Taken;
        }
    }
}
