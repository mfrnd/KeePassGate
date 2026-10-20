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
    /// sends writes a line, a lookup of a node that is not there included. With nothing holding it back, one client sent
    /// enough of those to roll the file twice in under two minutes, and everything written before,
    /// its own earlier reads among it, was gone from both files. The threat model includes the
    /// caller, and a caller covering its tracks is exactly the one the audit is for.
    ///
    /// **Waited, never dropped and never refused.** A line left out is the loss this exists to
    /// prevent, and a refusal would be a status the API does not have for a client doing nothing
    /// wrong. A slow answer is something every client already copes with. So a client may be
    /// recorded <see cref="Burst"/> times at once, and after that <see cref="PerSecond"/> times a
    /// second, and a decision past that waits on its own connection's thread for its turn. That
    /// thread is the caller's own: nothing another client is waiting on is held, and the lock
    /// here is not held while waiting.
    ///
    /// What it buys is a floor rather than a guarantee. One file is about thirty thousand lines,
    /// so a single client flooding as fast as it is allowed needs over an hour to push one
    /// decision out of both files, where it needed two minutes. The operator issued every key this
    /// is counted against, so a flood from several of them is several clients the operator
    /// approved, and every line the flood writes names the one doing it.
    ///
    /// Counted per admission key, because that is what a line names and what the Clients page
    /// revokes. A decision with no key is not paced: only a defect reaches the audit without one.
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

        /// <param name="elapsed">A clock that only goes forward, read for every decision.</param>
        /// <param name="wait">What a decision past the allowance does until its turn comes.</param>
        internal DecisionPace(Func<TimeSpan> elapsed, Action<TimeSpan> wait)
        {
            _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
            _wait = wait ?? throw new ArgumentNullException(nameof(wait));
        }

        /// <summary>Returns when this client's next decision may be written down.</summary>
        public void Take(string admissionKey)
        {
            if (admissionKey == null)
            {
                return;
            }

            TimeSpan owed;

            lock (_lock)
            {
                var now = _elapsed();

                Allowance allowance;
                if (!_clients.TryGetValue(admissionKey, out allowance))
                {
                    allowance = new Allowance { Left = Burst, At = now };
                    _clients.Add(admissionKey, allowance);
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
