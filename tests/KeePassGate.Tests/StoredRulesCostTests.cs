using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What working out the rules of a reading costs a hold, `STORE.md` §617, where every version
    /// names many others in `after`.
    /// </summary>
    /// <remarks>
    /// **A measurement to read, and not a gate to pass**, since a time on a shared machine is no
    /// assertion. Run it in Release, the fixture alone in the filter, since a fixture marked
    /// explicit runs only where the filter names it alone: `dotnet test tests\KeePassGate.Tests
    /// -c Release --filter FullyQualifiedName~StoredRulesCostTests`, and read the output, each line
    /// of which names the configuration it was built in.
    ///
    /// A version's `after` grows by an identifier for each edit and each renewal and is never
    /// pruned, while the walk never reads it: so a store of a row at each of many nodes, each
    /// version naming many identifiers, the reading made once, and then, the median and the worst
    /// of 21 after a first, the rules of that reading worked out and the walk run at every node,
    /// timed and with the bytes allocated beside the time. Allocations are counted for the
    /// application domain, which is why the fixture runs with nothing beside it.
    /// </remarks>
    [TestFixture]
    [Explicit("a measurement to read, not a gate to pass")]
    [NonParallelizable]
    public sealed class StoredRulesCostTests
    {
        private const int Runs = 21;

#if DEBUG
        private const string Built = "Debug";
#else
        private const string Built = "Release";
#endif

        [OneTimeSetUp]
        public void Counted()
        {
            AppDomain.MonitoringIsEnabled = true;
        }

        [TestCase(200, 50)]
        [TestCase(2000, 50)]
        [TestCase(2000, 400)]
        public void What_the_rules_of_a_reading_cost_where_every_version_names_many(int nodes, int named)
        {
            var key = StoreComposer.Key("one");
            var store = new ComposedStore(key);
            var random = new Random(617);
            var identifiers = new List<string>();
            var items = new List<string>();
            for (var at = 0; at < nodes; at++)
            {
                var node = at.ToString("x32", CultureInfo.InvariantCulture);
                var objectName = StoreComposer.RowObject(node, "pf");
                var after = Enumerable.Range(0, named).Select(_ => Hex(random)).ToArray();
                var version = store.Add(StoreComposer.RuleVersion(key, objectName, "row:RU", after));
                items.Add(StoreComposer.RuleVersionItem(version, objectName, "row:RU", after));
                identifiers.Add(node);
            }

            store.RestateAll(items);

            using (var reading = store.Read())
            {
                Assert.That(StoredRules.Of(reading).Named("pf"), Has.Count.EqualTo(nodes), "a version standing at every node");

                var times = new List<double>();
                var allocated = new List<long>();
                for (var run = 0; run <= Runs; run++)
                {
                    var before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                    var clock = Stopwatch.StartNew();

                    var rules = StoredRules.Of(reading);
                    var granted = identifiers.Count(node => AclResolver.Arrives(TestNode.Root(node).Holding(rules.At(node)), "pf").Count > 0);

                    clock.Stop();
                    var spent = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before;
                    Assert.That(granted, Is.EqualTo(nodes));

                    if (run > 0)
                    {
                        times.Add(clock.Elapsed.TotalMilliseconds);
                        allocated.Add(spent);
                    }
                }

                times.Sort();
                allocated.Sort();
                TestContext.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] the rules of a reading and a walk at each of {1} nodes, {2} identifiers in each version's after: median {3:F2} ms, worst {4:F2} ms, median {5:F1} MB allocated",
                    Built, nodes, named, times[times.Count / 2], times[times.Count - 1], allocated[allocated.Count / 2] / 1048576.0));
            }
        }

        private static string Hex(Random random)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
