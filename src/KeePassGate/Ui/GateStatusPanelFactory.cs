using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The panel the plugin shows today. It is replaced, not extended, once there is an authority
    /// to create and revoke from this tab.
    /// </summary>
    public sealed class GateStatusPanelFactory : IGatePanelFactory
    {
        /// <summary>
        /// Says plainly that nothing is served yet, so that a loaded plugin is never mistaken for
        /// a listening one.
        /// </summary>
        public const string DefaultStatus =
            "The plugin is loaded. It has no certificate authority, serves no API and opens no " +
            "listening socket yet.";

        private readonly string _version;
        private readonly string _status;

        public GateStatusPanelFactory()
            : this(GateVersion.Current, DefaultStatus)
        {
        }

        public GateStatusPanelFactory(string version, string status)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            _version = version;
            _status = status;
        }

        public Control CreatePanel()
        {
            return new GateStatusPanel(_version, _status);
        }
    }
}
