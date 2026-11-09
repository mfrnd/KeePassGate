using System;
using System.Resources;
using KeePass.App.Configuration;
using KeePass.DataExchange;
using KeePass.Ecas;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.PasswordGenerator;
using KeePassLib.Keys;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A plugin host that hands out nothing, the main window included.
    /// </summary>
    /// <remarks>
    /// **The plugin asks this host for its main window, and is told there is none.** Its start
    /// borrows the window's icons only when there is one, and serves nothing without one: no
    /// writer, no database watch, no listener. So the tests on this host cover what a start does
    /// before and around the gate, the tabs and the window events it subscribes to and lets go,
    /// with the gate switched off, and a dependency on the main window that is guarded does not
    /// fail them. What serving needs is covered against the real types by the fixtures that build
    /// those pieces themselves, and by the end to end tier.
    ///
    /// The constructor that counts reads of the main window, and `RefuseFromNowOn`, make a read of
    /// it throw, which is how a test makes a start, or something after it, fail part way.
    /// </remarks>
    internal sealed class FakePluginHost : IPluginHost
    {
        private Exception _refusal;
        private int _readsLeft;

        public FakePluginHost()
        {
        }

        /// <summary>
        /// A host whose main window can be asked for <paramref name="reads"/> times and then
        /// throws <paramref name="refusal"/>, which is how a test makes a start fail part way.
        /// </summary>
        public FakePluginHost(int reads, InvalidOperationException refusal)
        {
            _readsLeft = reads;
            _refusal = refusal;
        }

        /// <summary>
        /// Refuses every later read of the main window with <paramref name="refusal"/>, which is
        /// how a test makes something the plugin does after its start fail.
        /// </summary>
        public void RefuseFromNowOn(Exception refusal)
        {
            _refusal = refusal;
            _readsLeft = 0;
        }

        public MainForm MainWindow
        {
            get
            {
                if (_refusal != null && _readsLeft-- <= 0)
                {
                    throw _refusal;
                }

                return null;
            }
        }

        public PwDatabase Database
        {
            get { return null; }
        }

        public CommandLineArgs CommandLineArgs
        {
            get { return null; }
        }

        public AceCustomConfig CustomConfig
        {
            get { return null; }
        }

        public CipherPool CipherPool
        {
            get { return null; }
        }

        public KeyProviderPool KeyProviderPool
        {
            get { return null; }
        }

        public KeyValidatorPool KeyValidatorPool
        {
            get { return null; }
        }

        public FileFormatPool FileFormatPool
        {
            get { return null; }
        }

        public TempFilesPool TempFilesPool
        {
            get { return null; }
        }

        public EcasPool EcasPool
        {
            get { return null; }
        }

        public EcasTriggerSystem TriggerSystem
        {
            get { return null; }
        }

        public CustomPwGeneratorPool PwGeneratorPool
        {
            get { return null; }
        }

        public ColumnProviderPool ColumnProviderPool
        {
            get { return null; }
        }

        public ResourceManager Resources
        {
            get { return null; }
        }
    }
}
