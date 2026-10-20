using System;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The numbers that have to agree with each other, pinned so they cannot drift apart.
    /// </summary>
    [TestFixture]
    public sealed class GateOptionsTests
    {
        [Test]
        public void A_single_read_may_not_outlast_the_whole_connection()
        {
            var options = new GateOptions();

            Assert.That(options.Timeout, Is.LessThan(options.Budget));
        }
    }
}
