using System;
using System.Globalization;
using KeePass;
using KeePass.App.Configuration;
using KeePassGate.Diagnostics;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The plugin's own settings, kept in KeePass's configuration.
    /// </summary>
    /// <remarks>
    /// **In KeePass's configuration rather than in a database**, because the gate belongs to the
    /// installation: one KeePass, one listening socket, every open database served over it. A port
    /// stored in a `.kdbx` would be one answer per database to a question that has one, and would
    /// travel to whatever machine that file was copied to.
    ///
    /// KeePass keeps a dictionary for exactly this, namespaced by whatever key a plugin uses. The
    /// key here begins `KPG` for the same reason the custom data keys do: it is this plugin's
    /// namespace and touches nothing another plugin owns.
    ///
    /// **Written to the file as it is saved, and not only when KeePass exits.** The dictionary is
    /// memory until KeePass writes its configuration, which it does at an ordinary exit and when
    /// its own Options dialog is accepted. A port set here and left in memory was lost to a
    /// KeePass that was killed, crashed or lost power, while the window had already answered
    /// "Saved", so the next start bound the old port. This writes the configuration the way
    /// KeePass does after its Options dialog, and a port the file did not take is not kept.
    ///
    /// **Nothing here is wrapped, because nothing here can fail short of a defect.** That
    /// dictionary answers a value that is missing or is not a number with the default it is
    /// handed, and throws only for a null or empty key, which this one never is;
    /// `Program.Config` makes the configuration the first time it is asked for; and KeePass's
    /// serializer catches what writing its file throws, tells the operator in a dialog of its
    /// own, and answers false. A catch here would only ever have caught a mistake in this
    /// plugin, and reported it as a configuration that would not answer.
    /// </remarks>
    public sealed class KeePassSettings : IGateSettings
    {
        /// <summary>Where the chosen port is written, in KeePass's own configuration.</summary>
        public const string PortKey = "KPG.Port";

        private readonly IGateLog _log;
        private readonly Func<bool> _write;

        public KeePassSettings(IGateLog log)
            : this(log, () => AppConfigSerializer.Save(Program.Config))
        {
        }

        /// <summary>The same, writing the configuration another way, for a test.</summary>
        /// <param name="log">Where a port that was set is recorded.</param>
        /// <param name="write">
        /// Writes KeePass's configuration to its file and answers whether it did. KeePass's own
        /// serializer everywhere but in a test, which must not rewrite the configuration of the
        /// KeePass it runs against.
        /// </param>
        internal KeePassSettings(IGateLog log, Func<bool> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            _log = log ?? NullGateLog.Instance;
            _write = write;
        }

        /// <summary>
        /// The port the operator chose, or zero when they have chosen none or what is stored is
        /// not a port this may listen on, which hands the choice to the environment.
        /// </summary>
        public int Port
        {
            get
            {
                var stored = Program.Config.CustomConfig.GetLong(PortKey, 0);
                return stored >= GatePort.Lowest && stored <= GatePort.Highest ? (int)stored : 0;
            }
        }

        public string Save(int port)
        {
            var refused = GatePort.Refuse(port);
            if (refused != null)
            {
                return refused;
            }

            // Refused rather than kept in memory, because a KeePass told not to save its
            // configuration never writes this at exit either, and a port that does not reach the
            // next start is not a port that was set.
            if (!Program.Config.Application.ConfigSave)
            {
                return "KeePass is set not to save its configuration, so no port can be kept for "
                    + "the next start.";
            }

            var before = Program.Config.CustomConfig.GetString(PortKey, null);
            Program.Config.CustomConfig.SetLong(PortKey, port);

            if (!_write())
            {
                // Taken back out, so that an exit that does manage to write the file does not keep
                // a port the operator was told was not kept. KeePass has already said why.
                Program.Config.CustomConfig.SetString(PortKey, before);
                return "KeePass could not write its configuration file.";
            }

            _log.Info("the port was set to " + port.ToString(CultureInfo.InvariantCulture));
            return null;
        }
    }
}
