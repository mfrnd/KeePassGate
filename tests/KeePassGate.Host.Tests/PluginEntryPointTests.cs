using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass itself requires of a plugin, asserted against the compiled assembly rather
    /// than against the source. KeePass finds the entry point by name, so a rename that a
    /// compiler accepts happily produces a plugin that silently does nothing.
    /// </summary>
    [TestFixture]
    public sealed class PluginNamingTests
    {
        private static readonly Assembly Plugin = typeof(KeePassGateExt).Assembly;

        [Test]
        public void The_assembly_is_called_KeePassGate()
        {
            Assert.That(Plugin.GetName().Name, Is.EqualTo("KeePassGate"));
            Assert.That(Path.GetFileName(Plugin.Location), Is.EqualTo("KeePassGate.dll"));
        }

        [Test]
        public void It_holds_exactly_one_plugin_entry_point_and_it_is_named_for_the_file()
        {
            var entryPoints = Plugin.GetTypes()
                .Where(t => typeof(KeePass.Plugins.Plugin).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            Assert.That(entryPoints, Has.Count.EqualTo(1));

            // KeePass builds the type name from the file name as "<base>.<base>Ext", so the
            // namespace has to match the file as well as the class does.
            var baseName = Path.GetFileNameWithoutExtension(Plugin.Location);
            Assert.That(entryPoints[0].FullName, Is.EqualTo(baseName + "." + baseName + "Ext"));
            Assert.That(entryPoints[0].IsPublic, Is.True);
            Assert.That(entryPoints[0].GetConstructor(Type.EmptyTypes), Is.Not.Null,
                "KeePass instantiates the entry point with Activator.CreateInstance.");
        }

        [Test]
        public void KeePass_itself_can_find_and_construct_the_entry_point()
        {
            var manager = typeof(KeePass.Plugins.Plugin).Assembly.GetType("KeePass.Plugins.PluginManager", false);
            var create = manager == null
                ? null
                : manager.GetMethod("CreatePluginInstance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (create == null)
            {
                Assert.Ignore("This KeePass build does not expose PluginManager.CreatePluginInstance.");
            }

            var baseName = Path.GetFileNameWithoutExtension(Plugin.Location);
            var instance = create.Invoke(null, new object[] { Plugin.Location, baseName });

            Assert.That(instance, Is.InstanceOf<KeePassGateExt>());
        }

        [Test]
        public void It_declares_the_product_name_KeePass_screens_on()
        {
            // KeePass reads the file's ProductName with FileVersionInfo before it opens the
            // assembly, and skips the file entirely if it does not match, without a message, an
            // error or an entry in its plugin list. This assertion is the difference between that
            // failure taking a minute and taking an evening.
            var product = (AssemblyProductAttribute)Attribute.GetCustomAttribute(
                Plugin, typeof(AssemblyProductAttribute));

            Assert.That(product, Is.Not.Null);
            Assert.That(product.Product, Is.EqualTo(RequiredProductName()));

            var onDisk = FileVersionInfo.GetVersionInfo(Plugin.Location);
            Assert.That(onDisk.ProductName, Is.EqualTo(RequiredProductName()),
                "The compiled file is what KeePass screens, not the attribute.");
        }

        /// <summary>
        /// Taken from the KeePass under test rather than written down here, so that a KeePass which
        /// ever changes the string fails this test rather than shipping a plugin that never loads.
        /// </summary>
        private static string RequiredProductName()
        {
            var appDefs = typeof(KeePass.Plugins.Plugin).Assembly.GetType("KeePass.App.AppDefs", false);
            var field = appDefs == null
                ? null
                : appDefs.GetField("PluginProductName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (field == null)
            {
                Assert.Ignore("This KeePass build does not expose AppDefs.PluginProductName.");
            }

            return (string)field.GetValue(null);
        }

        [Test]
        public void It_targets_the_framework_KeePass_runs_on()
        {
            var target = (System.Runtime.Versioning.TargetFrameworkAttribute)Attribute.GetCustomAttribute(
                Plugin, typeof(System.Runtime.Versioning.TargetFrameworkAttribute));

            Assert.That(target, Is.Not.Null);
            Assert.That(target.FrameworkName, Does.StartWith(".NETFramework,Version=v4."));
        }

        [Test]
        public void It_carries_a_version_KeePass_can_show_in_its_plugin_list()
        {
            Assert.That(Plugin.GetName().Version, Is.Not.EqualTo(new Version(0, 0, 0, 0)));
            Assert.That(GateVersion.Current, Does.Match(@"^\d+\.\d+\.\d+"));
        }
    }

    /// <summary>
    /// The entry point driven the way KeePass drives it: initialize, let a dialog open and close
    /// through the real window manager, terminate, and check that nothing of ours is left behind.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class PluginLifecycleTests
    {
        private KeePassGateExt _plugin;
        private string _logDirectory;
        private string _logWas;

        /// <summary>
        /// Points the plugin's log at a file of this fixture's own: every `Initialize` here writes
        /// to it, and with nothing named that is the log of whoever runs the tests.
        /// </summary>
        [OneTimeSetUp]
        public void KeepTheLogOutOfTheAccountsOwn()
        {
            _logWas = Environment.GetEnvironmentVariable(TraceGateLog.LogFileVariable);
            _logDirectory = Path.Combine(Path.GetTempPath(), "KeePassGateTests", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(
                TraceGateLog.LogFileVariable, Path.Combine(_logDirectory, TraceGateLog.DefaultFileName));
        }

        [OneTimeTearDown]
        public void PutTheLogBack()
        {
            Environment.SetEnvironmentVariable(TraceGateLog.LogFileVariable, _logWas);

            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    Directory.Delete(_logDirectory, true);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // A temporary directory, and nothing reads it afterwards.
            }
        }

        [SetUp]
        public void SetUp()
        {
            _plugin = new KeePassGateExt();
        }

        [TearDown]
        public void TearDown()
        {
            _plugin.Terminate();
        }

        [Test]
        public void Without_a_host_it_refuses_to_start()
        {
            Assert.That(_plugin.Initialize(null), Is.False);
        }

        [Test]
        public void Terminating_a_plugin_that_never_started_is_harmless()
        {
            Assert.DoesNotThrow(() => _plugin.Terminate());
            Assert.DoesNotThrow(() => _plugin.Terminate());
        }

        [Test]
        public void It_publishes_no_update_url_while_there_is_nowhere_to_check()
        {
            Assert.That(_plugin.UpdateUrl, Is.Empty);
        }

        [Test]
        public void A_settings_dialog_opened_by_KeePass_gains_the_tab_and_loses_it_again()
        {
            Assert.That(_plugin.Initialize(new FakePluginHost()), Is.True);

            using (var form = KeePassDialogs.NewDatabaseSettingsForm())
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(GateTabOf(form), Is.Not.Null, "the plugin did not decorate the dialog");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                Assert.That(GateTabOf(form), Is.Null, "the plugin left its tab behind on a closed dialog");
            }
        }

        [Test]
        public void Another_dialog_is_left_alone()
        {
            _plugin.Initialize(new FakePluginHost());

            using (var form = new Form())
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(GateTabOf(form), Is.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void Terminating_takes_the_tab_off_a_dialog_that_is_still_open()
        {
            _plugin.Initialize(new FakePluginHost());

            using (var form = KeePassDialogs.NewDatabaseSettingsForm())
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assume.That(GateTabOf(form), Is.Not.Null);

                    _plugin.Terminate();

                    Assert.That(GateTabOf(form), Is.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void After_terminating_it_stops_listening()
        {
            _plugin.Initialize(new FakePluginHost());
            _plugin.Terminate();

            using (var form = KeePassDialogs.NewDatabaseSettingsForm())
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(GateTabOf(form), Is.Null,
                        "a terminated plugin is still subscribed to the window manager");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void It_can_be_initialized_again_after_being_terminated()
        {
            _plugin.Initialize(new FakePluginHost());
            _plugin.Terminate();

            Assert.That(_plugin.Initialize(new FakePluginHost()), Is.True);

            using (var form = KeePassDialogs.NewDatabaseSettingsForm())
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assert.That(GateTabOf(form), Is.Not.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        private static TabPage GateTabOf(Form form)
        {
            return ControlTree.FindByName<TabPage>(form, GateTab.PageControlName);
        }
    }
}
