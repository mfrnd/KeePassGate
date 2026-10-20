using KeePass;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The one setting this plugin keeps outside a database, against KeePass's real configuration.
    /// </summary>
    /// <remarks>
    /// `GateStatusFormTests` drives the window with a stand-in for the setting, so this is where the
    /// setting itself is written and read back: the key it lives under, a port that comes back as
    /// it went in, and what a stored value the gate cannot listen on turns into. The configuration
    /// is the process's own and is never written to a file here: every test that saves hands the
    /// settings a stand-in for KeePass's serializer, and every test puts back what the key held
    /// before it.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class KeePassSettingsTests
    {
        private string _was;
        private bool _saving;
        private int _writes;

        [SetUp]
        public void SetUp()
        {
            _was = Program.Config.CustomConfig.GetString(KeePassSettings.PortKey, null);
            Program.Config.CustomConfig.SetString(KeePassSettings.PortKey, null);
            _saving = Program.Config.Application.ConfigSave;
            _writes = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Program.Config.CustomConfig.SetString(KeePassSettings.PortKey, _was);
            Program.Config.Application.ConfigSave = _saving;
        }

        [Test]
        public void A_port_saved_is_the_port_read_back_under_its_own_key()
        {
            var settings = Writing(true);

            Assert.That(settings.Save(45678), Is.Null);
            Assert.That(settings.Port, Is.EqualTo(45678));
            Assert.That(Program.Config.CustomConfig.GetString("KPG.Port", null), Is.EqualTo("45678"),
                "a renamed key would leave every operator's saved port behind at the next start");
        }

        [Test]
        public void A_port_saved_is_written_to_the_file_then_and_there()
        {
            // K003: the dictionary is memory until KeePass writes its configuration, which it did
            // only at an ordinary exit, so a KeePass killed after the window said "Saved" started
            // on the old port. Written as KeePass's own Options dialog writes it when accepted.
            var settings = Writing(true);

            Assert.That(settings.Save(45678), Is.Null);
            Assert.That(_writes, Is.EqualTo(1), "the configuration was not written when the port was saved");
        }

        [Test]
        public void A_port_the_file_did_not_take_is_not_kept_and_the_one_before_stands()
        {
            Assume.That(Writing(true).Save(45678), Is.Null);

            var refused = Writing(false).Save(52000);

            Assert.That(refused, Does.Contain("could not write"));
            Assert.That(new KeePassSettings(NullGateLog.Instance).Port, Is.EqualTo(45678),
                "a port the operator was told was not kept would reach the file at the next exit");
        }

        [Test]
        public void A_KeePass_told_not_to_save_its_configuration_keeps_no_port()
        {
            Program.Config.Application.ConfigSave = false;

            var refused = Writing(true).Save(45678);

            Assert.That(refused, Does.Contain("not to save its configuration"));
            Assert.That(_writes, Is.Zero);
            Assert.That(new KeePassSettings(NullGateLog.Instance).Port, Is.Zero);
        }

        /// <summary>Settings over the real configuration, with its file left alone.</summary>
        private KeePassSettings Writing(bool succeeds)
        {
            return new KeePassSettings(NullGateLog.Instance, () =>
            {
                _writes++;
                return succeeds;
            });
        }

        [Test]
        public void Nothing_saved_is_no_choice()
        {
            Assert.That(new KeePassSettings(NullGateLog.Instance).Port, Is.Zero);
        }

        [TestCase(80L)]
        [TestCase(70000L)]
        [TestCase(-1L)]
        public void A_stored_port_the_gate_cannot_listen_on_is_no_choice(long stored)
        {
            // Written by hand or by another program, since the window refuses to save one. Zero
            // hands the choice to the environment and then the default, which is what a port
            // nobody can use has to mean.
            Program.Config.CustomConfig.SetLong(KeePassSettings.PortKey, stored);

            Assert.That(new KeePassSettings(NullGateLog.Instance).Port, Is.Zero);
        }

        [Test]
        public void A_stored_value_that_is_not_a_number_is_no_choice()
        {
            Program.Config.CustomConfig.SetString(KeePassSettings.PortKey, "forty-five thousand");

            Assert.That(new KeePassSettings(NullGateLog.Instance).Port, Is.Zero);
        }

        [Test]
        public void A_port_refused_is_not_written()
        {
            var settings = Writing(true);
            Assume.That(settings.Save(45678), Is.Null);

            Assert.That(settings.Save(80), Is.Not.Null.And.Not.Empty);
            Assert.That(settings.Port, Is.EqualTo(45678), "the port saved before is still the one kept");
            Assert.That(_writes, Is.EqualTo(1), "a refused port wrote the configuration anyway");
        }
    }
}
