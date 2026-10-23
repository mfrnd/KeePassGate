using System;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>What an end to end run does once the last fixture in it has finished.</summary>
    /// <remarks>
    /// A fixture of NUnit's own kind, for the whole namespace, so it runs once whichever fixtures a
    /// filter chose, the screenshots included, and after all of them: the queue
    /// <see cref="ClientSideProviderQueue"/> ends is one every fixture reading a dialog shares.
    /// </remarks>
    [SetUpFixture]
    public sealed class LiveRun
    {
        [OneTimeTearDown]
        public void LetTheTestDomainUnload()
        {
            ClientSideProviderQueue.End(TimeSpan.FromSeconds(10));
        }
    }
}
