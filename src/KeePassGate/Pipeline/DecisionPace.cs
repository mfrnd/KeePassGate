using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// How fast one client's decisions may be written down, and the wait that keeps it there.
    /// </summary>
    /// <remarks>
    /// **The audit is bounded by size, and a size is something the caller can spend.** The file
    /// rolls at eight megabytes and keeps one previous file, and every request an admitted client
    /// sends writes a line, a lookup of a node that is not there included. With nothing holding it
    /// back, one client sent enough of those to roll the file twice in under two minutes, and
    /// everything written before, its own earlier reads among it, was gone from both files. The
    /// threat model includes the caller, and a caller covering its tracks is exactly the caller
    /// the audit is for.
    ///
    /// **Waited, never dropped and never refused.** A line left out is the loss this exists to
    /// prevent, and a refusal would be a status the API does not have for a client doing nothing
    /// wrong. A slow answer is something every client already copes with. So a client may be
    /// recorded <see cref="Burst"/> times at once, and after that <see cref="PerSecond"/> times a
    /// second, and a decision past that waits on its own connection's thread for its turn, with
    /// the lock here let go. **The connection keeps its slot while it waits**, one of the
    /// `GateOptions.MaxConnections` every client shares, so a paced client with many connections
    /// open can hold most of them for seconds at a time, and other clients' connections, a
    /// pairing's among them, are closed at the door meanwhile. That is conceded, the operator's
    /// ruling R6, as `GateListener` concedes the slots to any peer that fills them.
    ///
    /// What it buys is a floor rather than a guarantee. One file holds about twenty thousand
    /// lines, so a single client flooding as fast as it is allowed pushes a decision out of both
    /// files in about half an hour, where it needed two minutes: a line written just before the
    /// file is set aside goes once the next file fills, and only a line written at the start of a
    /// file lasts about an hour. The operator issued every key this is counted against, so a flood
    /// from several of them is several clients the operator approved, and every line the flood
    /// writes names the client doing it.
    ///
    /// **Counted per client key**, the key every record of one client carries and a renewal
    /// copies, so a renewal does not hand a client a fresh allowance: counted per admission key,
    /// as it was, a client could renew and start again at a whole burst, and so roll the file
    /// faster than the pace says one client can. The key comes from what the router already holds,
    /// `ClientIdentity.ClientKey`, and costs no read.
    ///
    /// **A certificate the gate refuses has no client key**, since no record stands behind it,
    /// so every refused certificate of a database is counted against one allowance they share,
    /// <see cref="RefusedAllowance"/>. Counted by its own admission key, as it first was, each
    /// certificate a client renewed out of brought a burst and a rate of its own once it was
    /// revoked or had expired, and a client that kept them could write as many times the pace as
    /// it had certificates. Shared, the certificates the gate refuses write no faster together
    /// than one client does, and no client's own allowance is spent by them.
    ///
    /// A decision with no key at all is not paced. The only lines written without a key are
    /// pairings, whose pace is the operator answering the dialog, and anything else without a key
    /// is a defect.
    /// </remarks>
    public sealed class DecisionPace
    {
        /// <summary>How many decisions a client may have written down before it is paced.</summary>
        /// <remarks>
        /// Generous on purpose. An agent reading a folder one entry at a time is the ordinary
        /// burst, and it should never meet this.
        /// </remarks>
        public const int Burst = 1000;

        /// <summary>How many a second a client may go on having written down after that.</summary>
        public const int PerSecond = 10;

        private readonly object _lock = new object();
        private readonly Dictionary<string, Allowance> _clients =
            new Dictionary<string, Allowance>(StringComparer.Ordinal);

        private readonly Func<TimeSpan> _elapsed;
        private readonly Action<TimeSpan> _wait;

        public DecisionPace()
            : this(Monotonic(), Thread.Sleep)
        {
        }

        /// <summary>
        /// The same, on a clock and a wait of the caller's, which is how a test stands in for time
        /// passing.
        /// </summary>
        /// <param name="elapsed">A clock that only goes forward, read for every decision.</param>
        /// <param name="wait">What a decision past the allowance does until its turn comes.</param>
        internal DecisionPace(Func<TimeSpan> elapsed, Action<TimeSpan> wait)
        {
            _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
            _wait = wait ?? throw new ArgumentNullException(nameof(wait));
        }

        /// <summary>
        /// The allowance every certificate a database's authority issued and the gate refuses is
        /// counted against, by that database.
        /// </summary>
        /// <remarks>
        /// Spelled so that it can never be a client key, which is 64 hexadecimal characters.
        /// </remarks>
        public static string RefusedAllowance(string databaseId)
        {
            return "refused:" + (databaseId ?? string.Empty);
        }

        /// <summary>Returns when this client's next decision may be written down.</summary>
        /// <param name="clientKey">
        /// The client's key, or for a refused certificate, which has no client, the
        /// <see cref="RefusedAllowance"/> of its database; null for a line no client can be
        /// counted for, which is not paced.
        /// </param>
        public void Take(string clientKey)
        {
            if (clientKey == null)
            {
                return;
            }

            TimeSpan owed;

            lock (_lock)
            {
                var now = _elapsed();

                Allowance allowance;
                if (!_clients.TryGetValue(clientKey, out allowance))
                {
                    allowance = new Allowance { Left = Burst, At = now };
                    _clients.Add(clientKey, allowance);
                }

                // Earned since last asked, up to the burst. Taken before waiting, so that a
                // second connection from the same client queues behind the first rather than
                // spending the same turn: the allowance goes below zero, and how far below is
                // how long the newest decision waits.
                var earned = (now - allowance.At).TotalSeconds * PerSecond;
                allowance.Left = Math.Min(Burst, allowance.Left + earned) - 1;
                allowance.At = now;

                owed = allowance.Left >= 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(-allowance.Left / PerSecond);
            }

            if (owed > TimeSpan.Zero)
            {
                _wait(owed);
            }
        }

        private static Func<TimeSpan> Monotonic()
        {
            var watch = Stopwatch.StartNew();
            return () => watch.Elapsed;
        }

        private sealed class Allowance
        {
            public double Left;
            public TimeSpan At;
        }
    }
}
