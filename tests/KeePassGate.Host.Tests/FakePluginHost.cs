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
    /// A plugin host that hands out nothing. The plugin's first step touches none of it, and a
    /// test that starts failing here is a test telling us the plugin has grown a dependency on
    /// KeePass's running state which needs a real KeePass to cover.
    /// </summary>
    internal sealed class FakePluginHost : IPluginHost
    {
        public MainForm MainWindow
        {
            get { return null; }
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
