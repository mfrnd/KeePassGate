using System;
using System.Globalization;
using System.Reflection;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The process's local time zone, replaced for the length of a `using`, which is the one way
    /// to ask how a conversion behaves in a zone this machine is not in.
    /// </summary>
    /// <remarks>
    /// **Two caches, and both have to move.** Every conversion `DateTime` makes reads
    /// `TimeZoneInfo.Local`, while `DateTime.Now` reads a second cache filled from the operating
    /// system's own zone. Replacing the first alone left the two disagreeing by the difference
    /// between the zones, and a chain build, which judges dates against `Now`, then refused a
    /// certificate issued five minutes earlier. Measured on .NET Framework 4.8, where both are
    /// private fields of `TimeZoneInfo.CachedData`.
    ///
    /// Reflection into the framework's own fields is a dependency nothing in the plugin takes. It
    /// is taken here because the framework is frozen, and because the alternative is changing the
    /// machine's zone under every other process running on it. A framework that keeps them
    /// somewhere else fails here loudly rather than measuring this machine's zone in silence.
    ///
    /// Process wide, so a fixture using it is `NonParallelizable`.
    /// </remarks>
    internal sealed class LocalTimeZone : IDisposable
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly TimeZoneInfo _was;

        private LocalTimeZone(TimeZoneInfo was)
        {
            _was = was;
        }

        /// <summary>A zone this many hours from UTC, with no daylight saving time, until disposed.</summary>
        public static LocalTimeZone At(int hours)
        {
            var was = TimeZoneInfo.Local;
            var offset = TimeSpan.FromHours(hours);
            var name = "UTC" + hours.ToString("+0;-0;+0", CultureInfo.InvariantCulture);

            Replace(TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name), offset);
            return new LocalTimeZone(was);
        }

        /// <summary>
        /// The machine's own zone back, and its second cache emptied so that it is filled from the
        /// operating system again rather than left at the offset this put there.
        /// </summary>
        public void Dispose()
        {
            Replace(_was, null);
        }

        private static void Replace(TimeZoneInfo zone, TimeSpan? offset)
        {
            var field = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.Static | BindingFlags.NonPublic);
            var cached = field == null ? null : field.GetValue(null);
            var local = cached == null ? null : cached.GetType().GetField("m_localTimeZone", Instance);
            var oneYear = cached == null ? null : cached.GetType().GetField("m_oneYearLocalFromUtc", Instance);
            var shape = typeof(TimeZoneInfo).GetNestedType("OffsetAndRule", BindingFlags.NonPublic);

            if (local == null || oneYear == null || shape == null)
            {
                throw new InvalidOperationException(
                    "This framework keeps its local time zone somewhere other than .NET Framework 4.8 does, " +
                    "so no zone can be put in its place.");
            }

            local.SetValue(cached, zone);
            oneYear.SetValue(
                cached,
                offset.HasValue
                    ? Activator.CreateInstance(
                        shape, Instance, null, new object[] { DateTime.UtcNow.Year, offset.Value, null }, null)
                    : null);
        }
    }
}
