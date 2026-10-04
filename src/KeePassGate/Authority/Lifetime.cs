using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace KeePassGate.Authority
{
    /// <summary>
    /// How long a client certificate lasts: as the wire spells it, as the approval dialog offers
    /// it, and the bounds the issuer holds it to.
    /// </summary>
    /// <remarks>
    /// **The client proposes and the operator picks.** A request may ask for a lifetime, and the
    /// dialog preselects it among a few presets; one that asks for nothing is offered a year,
    /// which is what every client was given before anybody could ask. `CA.md` §207 holds the rule
    /// and `PAIRING.md` §311 the wire.
    ///
    /// **Whole days from one to 365**, written as an ISO 8601 duration of days alone, `P30D`, and
    /// in that one spelling: no leading zero, no other unit and nothing around it. The document
    /// holds the request to it with a pattern, and <see cref="Read"/> holds it again, because a
    /// reader that trusts something else to have checked first is a reader waiting to be called
    /// directly.
    /// </remarks>
    public static class Lifetime
    {
        /// <summary>The shortest a client certificate may be asked for or given.</summary>
        public const int ShortestDays = 1;

        /// <summary>The longest, which is the year every client was given before it could ask.</summary>
        public const int LongestDays = 365;

        /// <summary>
        /// What a client that asked for nothing is offered, and what the issuer gives when nobody
        /// chose.
        /// </summary>
        public static readonly TimeSpan Unasked = TimeSpan.FromDays(LongestDays);

        /// <summary>
        /// What the approval dialog offers, in days, shortest first. A lifetime asked for that is
        /// not one of these is offered as well, as a choice of its own.
        /// </summary>
        public static readonly IReadOnlyList<int> PresetDays =
            new ReadOnlyCollection<int>(new[] { 1, 7, 30, 90, 365 });

        /// <summary>
        /// The lifetime a request asked for, or null when what it wrote is not one.
        /// </summary>
        /// <remarks>
        /// `P`, then one to three digits with no leading zero, then `D`, and a number of days
        /// from <see cref="ShortestDays"/> to <see cref="LongestDays"/>. Everything else is null,
        /// which the caller answers `400 malformed`.
        /// </remarks>
        public static TimeSpan? Read(string written)
        {
            if (written == null || written.Length < 3 || written.Length > 5)
            {
                return null;
            }

            if (written[0] != 'P' || written[written.Length - 1] != 'D' || written[1] == '0')
            {
                return null;
            }

            var days = 0;
            for (var at = 1; at < written.Length - 1; at++)
            {
                var digit = written[at];
                if (digit < '0' || digit > '9')
                {
                    return null;
                }

                days = (days * 10) + (digit - '0');
            }

            return days < ShortestDays || days > LongestDays ? (TimeSpan?)null : TimeSpan.FromDays(days);
        }

        /// <summary>A lifetime as the wire writes it, `P30D` for thirty days.</summary>
        public static string Write(TimeSpan lifetime)
        {
            if (!IsAllowed(lifetime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lifetime), "A lifetime is whole days from " + ShortestDays + " to " + LongestDays + ".");
            }

            return "P" + ((int)lifetime.TotalDays).ToString(CultureInfo.InvariantCulture) + "D";
        }

        /// <summary>Whether this is a lifetime a client certificate may be given.</summary>
        public static bool IsAllowed(TimeSpan lifetime)
        {
            return lifetime.Ticks % TimeSpan.TicksPerDay == 0
                   && lifetime >= TimeSpan.FromDays(ShortestDays)
                   && lifetime <= TimeSpan.FromDays(LongestDays);
        }

        /// <summary>A lifetime as the dialog shows it: "1 day", "30 days".</summary>
        public static string Shown(TimeSpan lifetime)
        {
            var days = (int)lifetime.TotalDays;
            return days.ToString(CultureInfo.InvariantCulture) + (days == 1 ? " day" : " days");
        }
    }
}
