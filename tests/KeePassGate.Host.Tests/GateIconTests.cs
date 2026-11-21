using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The picture the plugin puts beside its own name, and what carrying it costs.
    /// </summary>
    /// <remarks>
    /// **None of the three dialogs brings an image list**, which is the fact this is built on and
    /// the fact that would silently reverse: a KeePass that started putting icons on its own tabs
    /// would hand us a list to append to instead of one to create, and the tab would end up with
    /// somebody else's picture on it. Measured here rather than assumed.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateIconTests
    {
        [Test]
        public void KeePass_icon_29_is_the_one_this_plugin_means()
        {
            // A number in a request is a number that has to be written down as a name somewhere, or
            // the next person changing it is choosing blind.
            Assert.That((PwIcon)GateIcon.Standard, Is.EqualTo(PwIcon.TerminalEncrypted));
        }

        [Test]
        public void Refresh_on_the_pairing_dialog_shows_KeePass_s_own_picture_for_Synchronize()
        {
            // The picture read as the product reads it, through the names of `KeePassContract`, at
            // the scale KeePass would draw it at here, and the picture Refresh shows: the same
            // size and the same pixels, and a copy of its own.
            var resources = typeof(KeePass.Program).Assembly.GetType(KeePassContract.ResourcesTypeName, false);
            Assert.That(resources, Is.Not.Null, "KeePass has no pictures under that name");
            var property = resources.GetProperty(
                KeePassContract.RefreshPictureProperty, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "KeePass has no picture for Synchronize");
            using (var read = (System.Drawing.Bitmap)property.GetValue(null, null))
            using (var database = TemporaryDatabase.New())
            {
                var scaled = (System.Drawing.Bitmap)KeePass.UI.DpiUtil.ScaleImage(read, false);
                var store = new KeePassGate.Bridge.KdbxStore(database.Database, GateVersion.Current, null);
                StoreSeeds.Authority(store);
                StoreSeeds.Name(store, "ops");
                var request = new KeePassGate.Pairing.PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", new[] { "ops" }, "127.0.0.1:60001", null, null, null,
                    null, () =>
                    {
                        using (var reading = store.Read())
                        {
                            return KeePassGate.Gate.AskedProfiles.Of(reading, new[] { "ops" }, System.DateTimeOffset.UtcNow);
                        }
                    });
                using (ReferenceEquals(scaled, read) ? null : scaled)
                {
                    using (var dialog = new PairingApprovalForm(
                               request.Showing(request.Read()), System.TimeSpan.FromMinutes(2), null, null, KeePassGate.Bridge.KeePassPictures.Refresh))
                    {
                        var shown = (System.Drawing.Bitmap)ControlTree.FindByName<Button>(dialog, GateTab.ApprovalRefreshName).Image;
                        Assert.That(shown, Is.Not.Null, "Refresh shows no picture");
                        Assert.That(shown, Is.Not.SameAs(read).And.Not.SameAs(scaled), "Refresh shows a picture other than a copy of its own");
                        Assert.That(shown.Size, Is.EqualTo(scaled.Size));
                        for (var y = 0; y < shown.Height; y++)
                        {
                            for (var x = 0; x < shown.Width; x++)
                            {
                                Assert.That(shown.GetPixel(x, y).ToArgb(), Is.EqualTo(scaled.GetPixel(x, y).ToArgb()), "a pixel other than KeePass's at " + x + ", " + y);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void None_of_the_dialogs_we_decorate_brings_its_own_tab_icons()
        {
            using (var database = TemporaryDatabase.New())
            using (var icons = new ImageList())
            {
                using (var settings = KeePassDialogs.NewDatabaseSettingsForm())
                {
                    KeePassDialogs.InitializeFor(settings, database.Database);
                    Report("database settings", settings);
                }

                using (var group = new GroupForm())
                {
                    group.InitEx(database.RootGroup, false, null, database.Database);
                    Report("group dialog", group);
                }

                using (var entry = new PwEntryForm())
                {
                    var pw = new PwEntry(true, true);
                    database.RootGroup.AddEntry(pw, true);
                    entry.InitEx(pw, PwEditMode.EditExistingEntry, database.Database, icons, false, false);
                    Report("entry dialog", entry);
                }
            }
        }

        private static void Report(string what, Control root)
        {
            var found = 0;

            foreach (var tabs in All(root))
            {
                found++;

                Assert.That(tabs.ImageList, Is.Null,
                    what + " now brings its own tab icons, so appending ours to its list is what "
                    + "would happen and the page could end up showing somebody else's picture.");
            }

            Assert.That(found, Is.GreaterThan(0), what + " has no tab control at all.");
        }

        private static System.Collections.Generic.IEnumerable<TabControl> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var tabs = child as TabControl;
                if (tabs != null)
                {
                    yield return tabs;
                }

                foreach (var deeper in All(child))
                {
                    yield return deeper;
                }
            }
        }
    }
}
