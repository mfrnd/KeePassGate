using System;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The operator's standing answer to renewing a certificate, as it is stored and as it is asked.
    /// </summary>
    [TestFixture]
    public sealed class RenewalRefusalTests
    {
        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero);

        [Test]
        public void Forever_refuses_whenever_it_is_asked()
        {
            var forever = RenewalRefusal.Forever();

            Assert.That(forever.IsForever, Is.True);
            Assert.That(forever.IsThisSession, Is.False);
            Assert.That(forever.NotBefore, Is.Null);
            Assert.That(forever.Refuses(DateTimeOffset.MaxValue), Is.True);
            Assert.That(forever.Write(), Is.EqualTo("forever"));
        }

        [Test]
        public void This_session_refuses_whenever_it_is_asked_and_is_never_written()
        {
            var session = RenewalRefusal.ThisSession();

            Assert.That(session.IsThisSession, Is.True);
            Assert.That(session.IsForever, Is.False, "it ends when KeePass does");
            Assert.That(session.IsWritten, Is.False);
            Assert.That(session.NotBefore, Is.Null);
            Assert.That(session.Refuses(DateTimeOffset.MaxValue), Is.True);
            Assert.That(() => session.Write(), Throws.InvalidOperationException);
        }

        [Test]
        public void Until_refuses_before_its_moment_and_not_from_it()
        {
            var until = RenewalRefusal.Until(Then);

            Assert.That(until.IsForever, Is.False);
            Assert.That(until.IsThisSession, Is.False);
            Assert.That(until.Refuses(Then.AddSeconds(-1)), Is.True);
            Assert.That(until.Refuses(Then), Is.False, "the moment named is the first one asked about again");
            Assert.That(until.Write(), Is.EqualTo("2026-10-09T22:00:00Z"));
        }

        [Test]
        public void A_moment_is_kept_to_the_second_and_in_UTC_as_the_wire_writes_it()
        {
            var local = new DateTimeOffset(2026, 10, 10, 0, 0, 0, 500, TimeSpan.FromHours(2));

            Assert.That(RenewalRefusal.Until(local).Write(), Is.EqualTo("2026-10-09T22:00:00Z"));
            Assert.That(RenewalRefusal.Until(local).NotBefore, Is.EqualTo(Then));
        }

        [TestCase("forever")]
        [TestCase("2026-10-09T22:00:00Z")]
        public void What_is_written_reads_back_as_itself(string written)
        {
            Assert.That(RenewalRefusal.Read(written).Write(), Is.EqualTo(written));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Forever")]
        [TestCase("this session")]
        [TestCase("2026-10-09")]
        [TestCase("2026-10-09T22:00:00+02:00")]
        [TestCase("tomorrow")]
        public void Anything_else_is_not_a_standing_answer(string written)
        {
            Assert.That(RenewalRefusal.Read(written), Is.Null);
        }
    }
}
