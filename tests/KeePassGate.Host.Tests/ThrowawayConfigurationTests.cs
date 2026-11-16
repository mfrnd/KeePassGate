using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KeePass.App;
using KeePass.App.Configuration;
using KeePass.Ecas;
using KeePass.Resources;
using KeePass.Util.XmlSerialization;
using KeePassGate.Host.Tests.Live;
using KeePassLib;
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

            Assert.That(read.Meta.PreferUserConfiguration, Is.False,
                "KeePass would read the developer's own configuration rather than this one");
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
            Assert.That(read.Meta.PreferUserConfiguration, Is.False);
        }

        [TestCase(true, "SavedDatabaseFile")]
        [TestCase(false, "SavingDatabaseFile")]
        public void KeePass_takes_the_trigger_that_closes_the_database_as_it_is_saved(bool afterTheFile, string theEvent)
        {
            // Held to KeePass's own identifiers, which it keeps where no plugin reaches: a trigger
            // naming an event or an action KeePass does not know is one it keeps and never runs,
            // and the end to end fixture would then measure a save nothing closed.
            AppConfigEx read;
            using (var stream = new MemoryStream(
                       new UTF8Encoding(false).GetBytes(ThrowawayKeePass.ConfigurationClosingOnSave(afterTheFile))))
            {
                read = (AppConfigEx)new XmlSerializerEx(typeof(AppConfigEx)).Deserialize(stream);
            }

            var keePass = typeof(AppConfigEx).Assembly;
            var events = keePass.GetType("KeePass.Ecas.EcasEventIDs", true);
            var wanted = (PwUuid)events.GetField(theEvent, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var provider = (EcasActionProvider)Activator.CreateInstance(
                keePass.GetType("KeePass.Ecas.EcasDefaultActionProvider", true), true);
            var close = provider.Find(KPRes.CloseActiveDatabase);
            Assert.That(close, Is.Not.Null, "KeePass no longer offers Close active database");

            var trigger = read.Application.TriggerSystem.TriggerCollection.Single();
            Assert.That(trigger.On, Is.True, "a trigger that is off never runs");
            Assert.That(trigger.EventCollection.Single().Type, Is.EqualTo(wanted));
            Assert.That(trigger.ActionCollection.Single().Type, Is.EqualTo(close.Type), "Close active database");
            Assert.That(read.Integration.LimitToSingleInstance, Is.False, "and everything else as before");
            Assert.That(read.MainWindow.EscAction, Is.EqualTo(AceEscAction.None));
        }

        [Test]
        public void The_throwaway_names_its_fallback_configuration_with_KeePass_s_own_option()
        {
            // The file KeePass writes when it cannot write the one beside the program, which is the
            // developer's own unless the command line names another. A name KeePass does not know
            // is an argument it ignores, and the fallback would be the developer's again.
            Assert.That(ThrowawayKeePass.LocalConfigurationOption,
                Is.EqualTo("-" + AppDefs.CommandLineOptions.ConfigPathLocal + ":"));
        }
    }
}
