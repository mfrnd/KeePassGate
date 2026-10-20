using System;

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
        /// somewhere to give one. TODO(step 18): the window under the Tools menu is where that
        /// setting is edited.
        /// </summary>
        public const string PortVariable = "KEEPASSGATE_PORT";

        public GateOptions()
        {
            Port = DefaultPort;
            MaxHeadBytes = 8 * 1024;
            MaxBodyBytes = 1024 * 1024;
            MaxConnections = 32;
            Timeout = TimeSpan.FromSeconds(10);
            Budget = TimeSpan.FromSeconds(30);
            PairingBudget = TimeSpan.FromMinutes(5);
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
        /// </remarks>
        public TimeSpan Budget { get; set; }

        /// <summary>
        /// How long a whole connection may take once it turns out to be a pairing, which is the one
        /// exchange that waits on a person.
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
        /// So it has to exceed `ApprovalTimeout + PairingApprovalPrompt.Grace +
        /// DatabaseWriter.LockTimeout + DatabaseWriter.WriteTimeout`, and
        /// `GateOptionsTests.The_pairing_budget_outlasts_everything_a_pairing_waits_for` is what
        /// keeps the five numbers from drifting apart.
        /// </remarks>
        public TimeSpan PairingBudget { get; set; }

        /// <summary>The defaults, with the port taken from the environment when one is named there.</summary>
        public static GateOptions FromEnvironment()
        {
            var options = new GateOptions();

            try
            {
                var text = Environment.GetEnvironmentVariable(PortVariable);
                int port;
                if (!string.IsNullOrEmpty(text) && int.TryParse(text, out port) && port >= 0 && port <= 65535)
                {
                    options.Port = port;
                }
            }
            catch (System.Security.SecurityException)
            {
                // A policy that forbids reading the environment. The default port stands.
            }

            return options;
        }
    }
}
