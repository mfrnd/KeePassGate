using System;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// How long a client certificate lasts, as the wire spells it and the dialog offers it.
    /// </summary>
    [TestFixture]
    public sealed class LifetimeTests
    {
        [TestCase("P1D", 1)]
        [TestCase("P7D", 7)]
        [TestCase("P30D", 30)]
        [TestCase("P99D", 99)]
        [TestCase("P100D", 100)]
        [TestCase("P365D", 365)]
        public void A_lifetime_reads_as_the_days_it_names(string written, int days)
        {
            Assert.That(Lifetime.Read(written), Is.EqualTo(TimeSpan.FromDays(days)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("P0D")]
        [TestCase("P366D")]
        [TestCase("P030D")]
        [TestCase("P1000D")]
        [TestCase("p30d")]
        [TestCase("P30")]
        [TestCase("30D")]
        [TestCase("P1W")]
        [TestCase("PT24H")]
        [TestCase("P-1D")]
        [TestCase(" P30D")]
        [TestCase("P30D ")]
        public void Anything_else_is_not_a_lifetime(string written)
        {
            // One spelling, which is what the proof is computed over as well: a second spelling of
            // thirty days would be a second commitment to the same request.
            Assert.That(Lifetime.Read(written), Is.Null);
        }

        [Test]
        public void A_lifetime_is_written_as_it_is_read()
        {
            foreach (var days in new[] { 1, 7, 30, 45, 90, 365 })
            {
                var written = Lifetime.Write(TimeSpan.FromDays(days));
                Assert.That(Lifetime.Read(written), Is.EqualTo(TimeSpan.FromDays(days)), written);
            }
        }

        [Test]
        public void Only_whole_days_from_one_to_a_year_are_a_lifetime()
        {
            Assert.That(Lifetime.IsAllowed(TimeSpan.FromDays(1)), Is.True);
            Assert.That(Lifetime.IsAllowed(TimeSpan.FromDays(365)), Is.True);
            Assert.That(Lifetime.IsAllowed(TimeSpan.Zero), Is.False);
            Assert.That(Lifetime.IsAllowed(TimeSpan.FromDays(366)), Is.False);
            Assert.That(Lifetime.IsAllowed(TimeSpan.FromHours(36)), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => Lifetime.Write(TimeSpan.FromHours(36)));
        }

        [Test]
        public void A_client_that_asks_for_nothing_is_offered_a_year()
        {
            // What every client was given before any could ask.
            Assert.That(Lifetime.Unasked, Is.EqualTo(TimeSpan.FromDays(365)));
            Assert.That(Lifetime.PresetDays, Is.EqualTo(new[] { 1, 7, 30, 90, 365 }));
        }

        [Test]
        public void A_lifetime_is_shown_in_days()
        {
            Assert.That(Lifetime.Shown(TimeSpan.FromDays(1)), Is.EqualTo("1 day"));
            Assert.That(Lifetime.Shown(TimeSpan.FromDays(30)), Is.EqualTo("30 days"));
        }
    }
}
