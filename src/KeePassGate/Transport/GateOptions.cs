using System;
using System.Globalization;
using System.Text;

namespace KeePassGate.Transport
{
    /// <summary>
    /// What the transport will and will not do. Every limit here is a refusal the design names in
    /// `DESIGN.md` §104, gathered in one place so none of them is a number buried in a method.
    /// </summary>
    public sealed class GateOptions
    {
        /// <summary>
        /// Nothing standard, and nothing a browser reaches by accident. It binds loopback only.
        /// </summary>
        public const int DefaultPort = 51776;

        /// <summary>
        /// Overrides the port. Read from the environment so that a test can take a free one
        /// without touching anybody's settings, and the only answer there is until an operator has
        /// somewhere to give one. The window under the Tools menu is where that setting is edited.
        /// </summary>
        public const string PortVariable = "KEEPASSGATE_PORT";

        /// <summary>
        /// <see cref="PairingBudget"/> as the gate starts with it, which is also how long an
        /// approval that could not be written is held for the client's retry.
        /// </summary>
        public static readonly TimeSpan DefaultPairingBudget = TimeSpan.FromMinutes(6);

        public GateOptions()
        {
            Port = DefaultPort;
            MaxHeadBytes = 8 * 1024;
            MaxBodyBytes = 1024 * 1024;
            MaxConnections = 32;
            Timeout = TimeSpan.FromSeconds(10);
            Budget = TimeSpan.FromSeconds(90);
            PairingBudget = DefaultPairingBudget;
        }

        public int Port { get; set; }

        /// <summary>The request line and headers together. Eight kilobytes, then refused.</summary>
        public int MaxHeadBytes { get; set; }

        /// <summary>One megabyte, refused by `Content-Length` before a byte of it is read.</summary>
        public int MaxBodyBytes { get; set; }

        /// <summary>
        /// How many connections are served at once. Everything past it is closed rather than
        /// queued.
        /// </summary>
        /// <remarks>
        /// The gate runs inside the password manager and borrows its thread pool, so a connection
        /// that is waiting is a thread KeePass is not using. Far more than any agent needs, and far
        /// fewer than it takes to make the application wait on somebody who proved nothing.
        /// </remarks>
        public int MaxConnections { get; set; }

        /// <summary>How long a single read may block before the socket is given up on.</summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// How long a whole connection may take, however slowly it is being fed.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Timeout"/> because that one bounds a read and not a
        /// conversation: a peer sending a byte just inside it satisfies every read and still never
        /// finishes, which is a thread held for as long as the caps allow rather than for ten
        /// seconds.
        ///
        /// **It bounds what the peer controls and stops there.** The connection's deadline is
        /// dropped the moment the whole request is off the wire, because after that the thread is
        /// blocked on this plugin rather than on the socket and closing the socket frees neither
        /// the thread nor the connection slot: all it does is destroy the answer to a write that
        /// lands anyway, leaving a client that has to guess whether its create happened and holds
        /// no identifier for the entry if it did. `GateListener.Deadline.Disarm` is where that is,
        /// with the argument.
        ///
        /// **It used to be said to have a floor**, and the floor was `DatabaseWriter.LockTimeout`
        /// plus `DatabaseWriter.WriteTimeout`, on the ground that the second of those bounds the
        /// marshaled write and save. It does not: it bounds the wait for that call to *start*, and
        /// a call that has started is waited out with no clock on it at all. So the sum was not an
        /// upper bound on anything a writing request can spend, and no number is: a save is a
        /// whole-database write of whatever size the database is, over whatever storage it is on.
        /// A raised budget made the loss rarer and nothing more.
        /// </remarks>
        public TimeSpan Budget { get; set; }

        /// <summary>
        /// How long a whole connection may take once it turns out to be a pairing or a renewal,
        /// the two exchanges that wait on a person.
        /// </summary>
        /// <remarks>
        /// **A pairing connection is not slow, it is blocked on somebody reading a dialog**, and
        /// <see cref="Budget"/> cannot tell those apart. Measured against the ordinary budget it is
        /// always over: the operator is given two minutes to decide, the prompt waits a little
        /// longer than that, and the write and save that follow have timeouts of their own. Closing
        /// the socket underneath all that does not stop the pairing. The invitation is still spent
        /// and the record still saved; it only throws away the answer, leaving a record on disk for
        /// a certificate nobody ever received.
        ///
        /// **There are two waits on the screen and not one**, which is what the number was short
        /// by. `PairingApprovalPrompt` lets one pairing at a time in front of the operator, and a
        /// second one waits for the screen for as long as it was going to give the operator anyway
        /// before it can start giving it. So a pairing that takes the screen just inside that wait
        /// still has the whole of the dialog ahead of it.
        ///
        /// **And these are the connections that keep a deadline after their request is read**,
        /// where <see cref="Budget"/> drops it. The difference is that the rest of a pairing is a
        /// sum somebody can write down, so a number for it means something; the rest of an
        /// ordinary write is a save of unknown length, so a number for it can only cut a landing
        /// write off from its answer. The last of the waits added up below is the same
        /// `DatabaseWriter.WriteTimeout` that does not bound a started save, and it is kept here
        /// because this budget only has to outlast the waits, not bound them.
        ///
        /// So it has to exceed `ApprovalTimeout` for the screen, then `ApprovalTimeout +
        /// PairingApprovalPrompt.Grace` for the dialog, then `ApprovalSettle` for a busy KeePass to
        /// settle once the operator has approved, then `DatabaseWriter.LockTimeout +
        /// DatabaseWriter.WriteTimeout` for the write, and
        /// `GateOptionsTests.The_pairing_budget_outlasts_everything_a_pairing_waits_for` is what
        /// keeps the six numbers from drifting apart.
        ///
        /// **A renewal is measured against it too**, because it waits for the same things: the
        /// same screen, which it shares with every pairing, the same dialog and grace, and the same
        /// turn at the writer.
        /// </remarks>
        public TimeSpan PairingBudget { get; set; }

        /// <summary>
        /// What the environment gives <see cref="PortVariable"/>, exactly as it gives it, or null
        /// when it gives nothing.
        /// </summary>
        /// <remarks>
        /// The text rather than a number, because whether it is a port is the question
        /// <see cref="PortToTake"/> has to answer and then report. A policy that forbids reading the
        /// environment is an environment that names nothing.
        /// </remarks>
        public static string PortFromEnvironment()
        {
            try
            {
                return Environment.GetEnvironmentVariable(PortVariable);
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>
        /// The port the gate takes as KeePass starts, or zero, with the reason, when the
        /// environment names something that is not a port.
        /// </summary>
        /// <param name="chosen">What the operator chose on the Tools window, or zero for nothing.</param>
        /// <param name="named">What <see cref="PortFromEnvironment"/> read, or null when it read nothing.</param>
        /// <param name="refused">Why nothing may be taken, or null when a port may.</param>
        /// <remarks>
        /// **The operator's choice first**, because it is the only one of the three a person made
        /// deliberately; then the variable, which is how a test takes a free port without touching
        /// anybody's settings; then <see cref="DefaultPort"/>. The variable is read only when
        /// nothing is chosen, so a chosen port stands whatever the variable holds.
        ///
        /// **A variable naming something that is not a port stops the gate**, and it used to be
        /// passed over for the default instead. The variable is read by the plugin, so a value in
        /// it that is not a port is a mistake in something somebody configured, and serving on a
        /// port nobody named hides that mistake behind a gate that looks healthy while every
        /// client set up with the intended port fails to connect. A port is what
        /// `GatePort` allows an operator to type, 1024 to 65535, written as digits and nothing
        /// else: a sign, a space, an empty value and a number out of range are all refused,
        /// because each is somebody's mistake rather than a port.
        /// </remarks>
        public static int PortToTake(int chosen, string named, out string refused)
        {
            refused = null;

            if (GatePort.Refuse(chosen) == null)
            {
                return chosen;
            }

            if (named == null)
            {
                return DefaultPort;
            }

            int port;
            if (int.TryParse(named, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && GatePort.Refuse(port) == null)
            {
                return port;
            }

            refused = PortVariable
                + (named.Length == 0 ? " is set but empty" : " is set to \"" + Shown(named) + "\"")
                + ", which is not a port the gate may listen on: a port is a number from "
                + GatePort.Lowest.ToString(CultureInfo.InvariantCulture) + " to "
                + GatePort.Highest.ToString(CultureInfo.InvariantCulture)
                + ", so the gate is not listening until the variable names one or is removed.";

            return 0;
        }

        /// <summary>
        /// A value somebody set, as it can be quoted in one line: control characters as spaces,
        /// and cut once it is longer than any port could be written.
        /// </summary>
        private static string Shown(string value)
        {
            var shown = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                shown.Append(char.IsControl(character) ? ' ' : character);
            }

            return shown.Length <= ShownLength
                ? shown.ToString()
                : shown.ToString(0, ShownLength) + "...";
        }

        /// <summary>How much of a refused value is quoted back.</summary>
        private const int ShownLength = 32;
    }
}
