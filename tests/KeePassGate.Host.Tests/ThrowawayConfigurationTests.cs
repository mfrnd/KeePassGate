using System.IO;
using System.Text;
using KeePass.App.Configuration;
using KeePass.Util.XmlSerialization;
using KeePassGate.Host.Tests.Live;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The configuration a throwaway KeePass starts with, read the way KeePass reads it.
    /// </summary>
    /// <remarks>
    /// With KeePass's own serializer, because a setting it does not take is left at its default
    /// without a word, and a default that arrives shows up in the end to end tier as a modal
    /// question before any plugin loads, a command line handed to another KeePass, or a workspace
    /// locked by a stray Escape: each of them far from here, and none of them saying why.
    /// </remarks>
    [TestFixture]
    public sealed class ThrowawayConfigurationTests
    {
        [Test]
        public void KeePass_takes_every_setting_the_throwaway_configuration_gives()
        {
            AppConfigEx read;
            using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(ThrowawayKeePass.Configuration)))
            {
                read = (AppConfigEx)new XmlSerializerEx(typeof(AppConfigEx)).Deserialize(stream);
            }

            Assert.That(read.Application.Start.CheckForUpdate, Is.False, "the update question would be asked");
            Assert.That(read.Application.Start.CheckForUpdateConfigured, Is.True, "and asked on the first start");
            Assert.That(read.Integration.LimitToSingleInstance, Is.False,
                "a KeePass already running would be handed the command line");
            Assert.That(read.MainWindow.EscAction, Is.EqualTo(AceEscAction.None),
                "an Escape reaching the main window would lock the workspace");
            Assert.That(read.Security.WorkspaceLocking.LockAfterTime, Is.Zero,
                "no session locks by itself unless it asks to");
        }

        [Test]
        public void KeePass_takes_the_inactivity_lock_the_timer_fixture_asks_for()
        {
            AppConfigEx read;
            using (var stream = new MemoryStream(
                       new UTF8Encoding(false).GetBytes(ThrowawayKeePass.ConfigurationLockingAfter(8))))
            {
                read = (AppConfigEx)new XmlSerializerEx(typeof(AppConfigEx)).Deserialize(stream);
            }

            Assert.That(read.Security.WorkspaceLocking.LockAfterTime, Is.EqualTo(8));
            Assert.That(read.MainWindow.EscAction, Is.EqualTo(AceEscAction.None), "and everything else as before");
            Assert.That(read.Integration.LimitToSingleInstance, Is.False);
        }
    }
}
