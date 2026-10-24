using System;
using System.Diagnostics;
using System.Reflection;
using KeePassGate.Host.Tests.Live;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Ending the thread UI automation's client-side providers leave behind, in a domain of its own,
    /// without a desktop.
    /// </summary>
    /// <remarks>
    /// **In the host tier rather than the end to end one**, so that every run of the gate pins the
    /// framework's private names <see cref="ClientSideProviderQueue"/> reaches for. The queue is
    /// started the way the framework starts it, by asking its tracker for it, which is what the first
    /// walk into a window's controls does; no window is needed for that.
    ///
    /// **The unload is the assertion that matters.** Without the end, it throws
    /// `CannotUnloadAppDomainException` after fifteen seconds, which is what every end to end run
    /// did, so a change that left the thread running fails here rather than passing slowly.
    /// </remarks>
    [TestFixture]
    public sealed class ClientSideProviderQueueTests
    {
        [Test]
        public void A_domain_whose_queue_was_ended_unloads_at_once()
        {
            Unloaded(inside =>
            {
                Assert.That(inside.Start(), Is.True, "asking the tracker for its queue started one");
                Assert.That(inside.End(), Is.True);
            });
        }

        [Test]
        public void A_queue_asked_for_after_the_end_is_a_new_one_and_ends_as_well()
        {
            // What the cleared field is for: anything that still uses automation afterwards gets a
            // queue of its own rather than one whose thread has gone.
            Unloaded(inside =>
            {
                inside.Start();
                inside.End();

                Assert.That(inside.Start(), Is.True);
                Assert.That(inside.End(), Is.True);
            });
        }

        [Test]
        public void A_domain_that_never_walked_into_a_window_has_nothing_to_end()
        {
            Unloaded(inside => Assert.That(inside.End(), Is.False));
        }

        /// <summary>Runs this in a domain of its own, then unloads that domain and times it.</summary>
        private static void Unloaded(Action<Inside> run)
        {
            var domain = AppDomain.CreateDomain(
                "client-side providers", null, AppDomain.CurrentDomain.SetupInformation);

            var ran = false;

            try
            {
                run((Inside)domain.CreateInstanceAndUnwrap(
                    typeof(Inside).Assembly.FullName, typeof(Inside).FullName));
                ran = true;
            }
            finally
            {
                var watch = Stopwatch.StartNew();

                try
                {
                    AppDomain.Unload(domain);
                }
                catch (CannotUnloadAppDomainException) when (!ran)
                {
                    // The run's own failure is the one worth reading, a name the framework moved
                    // above all, and a domain whose queue was never ended cannot unload anyway.
                    // Thrown from here, this would have replaced it.
                }

                if (ran)
                {
                    Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the domain took this long to unload");
                }
            }
        }

        /// <summary>What runs inside the domain a test unloads.</summary>
        public sealed class Inside : MarshalByRefObject
        {
            /// <summary>Asks the tracker for its queue, which starts its thread, and says whether one came back.</summary>
            public bool Start()
            {
                var tracker = Assembly.Load(ClientSideProviderQueue.AssemblyFullName)
                    .GetType(ClientSideProviderQueue.TrackerName, true);
                var asked = tracker.GetMethod("GetCallbackQueue", BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(asked, Is.Not.Null, "the tracker no longer hands out its queue by that name");
                return asked.Invoke(null, null) != null;
            }

            public bool End()
            {
                return ClientSideProviderQueue.End(TimeSpan.FromSeconds(10));
            }
        }
    }
}
