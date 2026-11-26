using System;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The operator's standing answer to a client's renewals, as it is asked. How a stored answer
    /// is written and read is `RenewalTextTests`'.
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
        }

        [Test]
        public void This_session_refuses_whenever_it_is_asked_and_is_never_written()
        {
            var session = RenewalRefusal.ThisSession();

            Assert.That(session.IsThisSession, Is.True);
            Assert.That(session.IsForever, Is.False, "it ends when KeePass does");
            Assert.That(session.NotBefore, Is.Null);
            Assert.That(session.Refuses(DateTimeOffset.MaxValue), Is.True);
            Assert.That(() => RenewalText.Of(session), Throws.ArgumentException, "no version of the store holds it");
        }

        [Test]
        public void Until_refuses_before_its_moment_and_not_from_it()
        {
            var until = RenewalRefusal.Until(Then);

            Assert.That(until.IsForever, Is.False);
            Assert.That(until.IsThisSession, Is.False);
            Assert.That(until.Refuses(Then.AddSeconds(-1)), Is.True);
            Assert.That(until.Refuses(Then), Is.False, "the moment named is the first one asked about again");
            Assert.That(until.NotBefore, Is.EqualTo(Then));
        }

        [Test]
        public void A_moment_is_kept_to_the_second_and_in_UTC_as_the_wire_writes_it()
        {
            var local = new DateTimeOffset(2026, 10, 10, 0, 0, 0, 500, TimeSpan.FromHours(2));

            Assert.That(RenewalRefusal.Until(local).NotBefore, Is.EqualTo(Then));
            Assert.That(RenewalRefusal.Until(local).NotBefore.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }
}
