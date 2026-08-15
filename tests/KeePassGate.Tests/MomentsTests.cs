using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>The three ways this plugin writes a moment, in the language that would break them.</summary>
    /// <remarks>
    /// **Two different regressions, and only one of them is what it looks like.** Every caller here
    /// passes `InvariantCulture`, so the thread's culture never reaches the formatter and a format
    /// that spelled its separators bare would go on being right. That is exactly how the wrong
    /// spelling survived in four places before this class existed, and it is why a test that only
    /// sets the culture and calls <see cref="Moments.Wire"/> proves nothing about the quoting.
    ///
    /// So the formats are asserted twice. Once through the helpers, which pins that none of them
    /// starts reading the current culture. Once against the constants with no provider at all,
    /// which is the only way to see whether the quoting is there: a bare `:` in a custom format
    /// string means "whatever this culture puts between hours and minutes".
    ///
    /// **Finnish, not German.** Only the time separator is substituted, since a bare `-` is a
    /// literal rather than the date separator. German already writes a time with `:`, so it agrees
    /// with the invariant culture and cannot fail either assertion. Finland writes `05.06.07`.
    /// </remarks>
    [TestFixture]
    public sealed class MomentsTests
    {
        private static readonly DateTimeOffset At =
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);

        [Test]
        public void Every_format_quotes_its_own_separators()
        {
            // Formatted against the current culture rather than the invariant one, which is what a
            // caller writing something for a person to read would reasonably pass. This is the
            // assertion that fails if a separator is ever left bare, and the only one that does.
            // It cannot be written by leaving the provider out: `CA1305` is an error here, which is
            // the other reason the bare spelling never actually bit.
            InFinnish(() =>
            {
                Assert.That(At.ToString(Moments.WireFormat, CultureInfo.CurrentCulture),
                    Is.EqualTo("2026-03-04T05:06:07Z"));
                Assert.That(At.ToString(Moments.OnScreenFormat, CultureInfo.CurrentCulture),
                    Is.EqualTo("2026-03-04 05:06:07"));
                Assert.That(At.UtcDateTime.ToString(Moments.LogFormat, CultureInfo.CurrentCulture),
                    Is.EqualTo("2026-03-04T05:06:07.089Z"));
            });
        }

        [Test]
        public void What_it_writes_does_not_depend_on_the_thread_it_runs_on()
        {
            // The other half: that the helpers keep passing a fixed culture rather than picking up
            // whichever one the user interface thread happens to be in.
            InFinnish(() =>
            {
                Assert.That(Moments.Wire(At), Is.EqualTo("2026-03-04T05:06:07Z"));
                Assert.That(Moments.OnScreen(At), Is.EqualTo("2026-03-04 05:06:07"));
                Assert.That(Moments.Log(At.UtcDateTime), Is.EqualTo("2026-03-04T05:06:07.089Z"));
            });
        }

        [Test]
        public void A_moment_somewhere_else_is_written_as_the_same_instant_here()
        {
            // A record written on one machine is read on another, so the zone has to go.
            var elsewhere = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));

            Assert.That(Moments.Wire(elsewhere), Is.EqualTo("2026-03-04T05:06:07Z"));
        }

        private static void InFinnish(Action what)
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fi-FI");
                what();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }
}
