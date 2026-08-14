using System;
using System.Windows.Forms;
using KeePass.Plugins;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Builds real KeePass dialogs in the test process. KeePass constructs its settings dialog
    /// without touching the running application, which is what lets these tests exercise the real
    /// control tree without a KeePass on the desktop.
    /// </summary>
    internal static class KeePassDialogs
    {
        public static Form NewDatabaseSettingsForm()
        {
            var type = typeof(Plugin).Assembly.GetType(KeePassContract.DatabaseSettingsFormTypeName, false);
            if (type == null)
            {
                throw new InvalidOperationException(
                    KeePassContract.DatabaseSettingsFormTypeName + " is not in this KeePass build.");
            }

            return (Form)Activator.CreateInstance(type);
        }
    }
}
