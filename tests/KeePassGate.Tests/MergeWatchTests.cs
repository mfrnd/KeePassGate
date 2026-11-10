using System;
using System.Runtime.CompilerServices;
using System.Threading;
using KeePassGate.Bridge;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A thread asking whether it is inside a method of a given name on a given type, which is how
    /// a merge of KeePass's is seen.
    /// </summary>
    /// <remarks>
    /// Over a stand-in type, because what is pinned here is the looking: whose frame counts, and
    /// what is answered when there is no method to look for. That KeePass's own merge is seen from
    /// inside it is the host tier's, `ReadsDuringAMergeTests`.
    /// </remarks>
    [TestFixture]
    public sealed class MergeWatchTests
    {
        [Test]
        public void A_thread_inside_the_method_is_seen_and_one_outside_it_is_not()
        {
            var inside = MergeWatch.Over(typeof(Database), nameof(Database.MergeIn));
            Assert.That((object)inside, Is.Not.Null);

            Assert.That(inside(), Is.False, "before");

            var during = false;
            new Database().MergeIn(() => during = inside());
            Assert.That(during, Is.True, "asked from underneath it");

            Assert.That(inside(), Is.False, "and after");
        }

        [Test]
        public void Every_overload_counts_and_so_does_whatever_the_method_calls()
        {
            var inside = MergeWatch.Over(typeof(Database), nameof(Database.MergeIn));

            var during = false;
            new Database().MergeIn(3, () => Deeper(() => during = inside()));

            Assert.That(during, Is.True);
        }

        [Test]
        public void A_method_of_the_same_name_on_another_type_is_not_a_merge()
        {
            var inside = MergeWatch.Over(typeof(Database), nameof(Database.MergeIn));

            var during = true;
            new Lookalike().MergeIn(() => during = inside());

            Assert.That(during, Is.False);
        }

        [Test]
        public void Another_thread_s_merge_is_not_this_thread_s()
        {
            var inside = MergeWatch.Over(typeof(Database), nameof(Database.MergeIn));

            using (var entered = new ManualResetEventSlim())
            using (var leave = new ManualResetEventSlim())
            {
                var merger = new Thread(() => new Database().MergeIn(() =>
                {
                    entered.Set();
                    leave.Wait(TimeSpan.FromSeconds(10));
                })) { IsBackground = true };
                merger.Start();

                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                var seen = inside();
                leave.Set();
                merger.Join(TimeSpan.FromSeconds(10));

                Assert.That(seen, Is.False, "the question is about the thread asking, as its remarks have it");
            }
        }

        [Test]
        public void A_type_with_no_method_of_that_name_leaves_nothing_to_ask()
        {
            // Null rather than a question that always answers no, so that whoever asks is told
            // there is nothing watching.
            Assert.That((object)MergeWatch.Over(typeof(Database), "MergeOut"), Is.Null);
            Assert.That((object)MergeWatch.Over(typeof(Heir), nameof(Database.MergeIn)), Is.Null,
                "a method the type only inherits is not on the frames that would be compared");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Deeper(Action then)
        {
            then();
        }

        private class Database
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void MergeIn(Action then)
            {
                then();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void MergeIn(int method, Action then)
            {
                then();
            }
        }

        private sealed class Heir : Database
        {
        }

        private sealed class Lookalike
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void MergeIn(Action then)
            {
                then();
            }
        }
    }
}
