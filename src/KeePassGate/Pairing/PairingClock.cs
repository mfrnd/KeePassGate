using System;
using System.Diagnostics;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// The two readings pairing takes of the time: the wall clock, for every moment shown to the
    /// operator or sent to a client, and elapsed time on a monotonic clock, for when an
    /// invitation, a hello's challenge or an approval held for a busy KeePass ends.
    /// </summary>
    /// <remarks>
    /// **What ends goes by elapsed time**, the operator's ruling of 2026-11-08. Measured against
    /// the system clock, a clock set back while a code was outstanding kept it alive by as much as
    /// the step, and kept an approval writable without a dialog past the pairing budget it was
    /// given in, and a clock set forward ended both early. Elapsed time comes from a `Stopwatch`, which
    /// no setting of the system clock moves. **What is shown and sent stays the wall clock**, read
    /// when the thing is made: an invitation's expiry on the tab, a hello's `expires`, a
    /// `503 busy`'s `heldUntil`. Those can disagree with when it really ends by as much as the
    /// system clock moved since, which is the price of telling a person and a peer a time at all.
    ///
    /// **A clock a test hands in is the wall clock alone**, and elapsed time then follows it, as
    /// its ticks since the start of the calendar, so that moving it moves both and two parts given
    /// the same clock agree. <see cref="Default"/> is the one the plugin runs on.
    /// </remarks>
    public sealed class PairingClock
    {
        private static readonly Stopwatch Running = Stopwatch.StartNew();

        /// <summary>The system's wall clock, and elapsed time since the plugin was loaded.</summary>
        public static readonly PairingClock Default =
            new PairingClock(() => DateTimeOffset.UtcNow, () => Running.Elapsed);

        private readonly Func<DateTimeOffset> _wall;
        private readonly Func<TimeSpan> _elapsed;

        public PairingClock(Func<DateTimeOffset> wall, Func<TimeSpan> elapsed)
        {
            if (wall == null)
            {
                throw new ArgumentNullException(nameof(wall));
            }

            if (elapsed == null)
            {
                throw new ArgumentNullException(nameof(elapsed));
            }

            _wall = wall;
            _elapsed = elapsed;
        }

        /// <summary>
        /// <see cref="Default"/> for null, and otherwise a clock whose elapsed time moves exactly
        /// as this wall clock does.
        /// </summary>
        public static PairingClock For(Func<DateTimeOffset> wall)
        {
            return wall == null ? Default : new PairingClock(wall, () => TimeSpan.FromTicks(wall().UtcTicks));
        }

        /// <summary>The wall clock, for what is shown and sent.</summary>
        public DateTimeOffset Now
        {
            get { return _wall(); }
        }

        /// <summary>The monotonic clock, for what ends.</summary>
        public TimeSpan Elapsed
        {
            get { return _elapsed(); }
        }

        /// <summary>Both readings, taken together.</summary>
        public PairingMoment Read()
        {
            return new PairingMoment(_wall(), _elapsed());
        }

        /// <summary>Whether elapsed time has reached this moment, whatever the wall reads.</summary>
        public bool HasReached(PairingMoment moment)
        {
            return _elapsed() >= moment.Elapsed;
        }
    }

    /// <summary>
    /// A moment as both of a <see cref="PairingClock"/>'s readings give it: the wall-clock time it
    /// is shown and sent as, and the elapsed time that decides whether it has come.
    /// </summary>
    public struct PairingMoment
    {
        public PairingMoment(DateTimeOffset wall, TimeSpan elapsed)
        {
            Wall = wall;
            Elapsed = elapsed;
        }

        /// <summary>What the operator is shown and the client is sent.</summary>
        public DateTimeOffset Wall { get; }

        /// <summary>What a <see cref="PairingClock"/> compares.</summary>
        public TimeSpan Elapsed { get; }

        /// <summary>The moment this long after this one, on both clocks.</summary>
        public PairingMoment After(TimeSpan span)
        {
            return new PairingMoment(Wall + span, Elapsed + span);
        }
    }
}
