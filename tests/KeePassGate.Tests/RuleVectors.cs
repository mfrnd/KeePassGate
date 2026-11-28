using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The vectors the rules are held to, printed from the measured model and never from this side:
    /// `tests/Shared/StoreRecordVectors.json` for the texts and the objects, and
    /// `tests/Shared/StoreMeaningVectors.json` for what a store gives at each node.
    /// </summary>
    internal static class RuleVectors
    {
        /// <summary>The records, with the texts in form and out of form under `texts`.</summary>
        public static JObject Records
        {
            get { return Read("StoreRecordVectors.json"); }
        }

        /// <summary>The stores, with what the measured reading answers for each under `meanings`.</summary>
        public static JObject Meanings
        {
            get { return Read("StoreMeaningVectors.json"); }
        }

        /// <summary>
        /// A store of the meanings read as the measured reading read it: its entries, each with its
        /// history, its deletion records, and the tree it holds, the root and every group.
        /// </summary>
        /// <param name="besideTree">The tree to read it beside where the store names none; null for none.</param>
        public static StoreReading Reading(JObject store, JObject besideTree = null)
        {
            var versions = new StoreVersions();
            var candidates = new List<StoreCandidate>();
            foreach (var entry in (JObject)store["entries"])
            {
                var current = Strings((JObject)entry.Value["strings"]);
                var history = ((JArray)entry.Value["history"]).Select(item => Strings((JObject)item)).Reverse();
                candidates.Add(versions.Candidate(StoreComposer.BytesOf(entry.Key), new[] { current }.Concat(history).ToArray()));
            }

            var deletions = ((JArray)store["deletions"]).Select(identifier => StoreComposer.BytesOf((string)identifier)).ToList();
            var tree = new List<byte[]>();
            var groups = (JObject)store["tree"] ?? besideTree;
            if (groups != null)
            {
                tree.Add(StoreComposer.BytesOf((string)groups["root"]));
                tree.AddRange(((JObject)groups["groups"]).Properties().Select(group => StoreComposer.BytesOf(group.Name)));
            }

            return StoreReading.Of(candidates, deletions, null, new StoreKept(), () => tree);
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }

        private static JObject Read(string name)
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", name);
                if (File.Exists(path))
                {
                    // Read as text: a member that looks like a date stays the text it is.
                    using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                    {
                        return JObject.Load(reader);
                    }
                }
            }

            throw new FileNotFoundException("tests/Shared/" + name + " is not above the test's directory");
        }
    }
}
