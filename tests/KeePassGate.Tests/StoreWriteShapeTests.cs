using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The shape of the product's writes, held to the writes the measured model made in the same
    /// sequence, `STORE.md` §627: what each write deletes, how high its summary entry is, how many
    /// summary entries it names, and how many items it holds.
    /// </summary>
    /// <remarks>
    /// `tests/Shared/StoreWriteVectors.json` holds the file after each act of two sequences that
    /// `model/crdt_model.py` played, written out by `probe/store.py`'s `Encoder`. Its records hold
    /// stand-ins, short names and nonces of the model's own, so the product is held by shape and
    /// never by bytes. Compared: the authorities and summary entries each write deletes, the height
    /// and the count of `after` of each summary entry present, and the count of its items, the
    /// item `format` the product writes standing where the model wrote the version of the root's
    /// rule its start holds. Not compared: the admissions the model deleted, which this build keeps
    /// beside the store, and the store the sequence starts from, which the product makes by Create.
    /// The sequence over two copies is played: a Recreate in each, a Synchronize, and keeping an
    /// authority.
    /// </remarks>
    [TestFixture]
    public sealed class StoreWriteShapeTests
    {
        [Test]
        public void Two_copies_that_each_recreate_synchronized_and_kept_write_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["two copies"]["steps"];

            var first = new MemoryStore();
            StoreSeeds.Authority(first, new InMemoryKeyValueStore());
            var second = first.Copy();

            AssertShape(steps[0], first, () => Service(first).Recreate());
            AssertShape(steps[1], second, () => Service(second).Recreate());
            AssertShape(steps[2], first, () =>
            {
                first.Synchronize(second);
                return AuthorityChange.Done();
            });

            var standing = first.Read().Answering.Select(record => record.Form.Member("id")).ToList();
            Assume.That(standing, Has.Count.EqualTo(2));
            AssertShape(steps[3], first, () => Service(first).Keep(standing[0], standing));

            Assert.That(StoreAuthorities.Served(first.Read()), Is.EqualTo(standing[0]), "the answer the model read: the authority kept is served");
        }

        private static void AssertShape(JToken step, MemoryStore store, Func<AuthorityChange> act)
        {
            var command = (string)step["command"];
            var before = Kinds(store.Read());

            var done = act();
            Assert.That(done.Succeeded, Is.True, command + ": " + done.Message);

            var reading = store.Read();
            var after = Kinds(reading);
            var deleted = before.Where(pair => !after.ContainsKey(pair.Key)).Select(pair => pair.Value)
                .Where(kind => kind == "authority" || kind == "head").OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            var modelDeleted = ((JArray)step["deleted"]).Select(item => (string)item["kind"])
                .Where(kind => kind == "authority" || kind == "head").OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            Assert.That(deleted, Is.EqualTo(modelDeleted), command + ": the authorities and summary entries deleted");

            var heads = reading.InForce.Heads
                .Select(head => (Height: head.Form.Number, After: Count(head.Form.Member("after")), Items: head.Form.Items.Count))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ToList();
            var modelHeads = ((JArray)step["heads"])
                .Select(head => (Height: (int)head["height"], After: (int)head["after"], Items: ((JArray)head["items"]).Count))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ToList();
            Assert.That(heads, Is.EqualTo(modelHeads), command + ": each summary entry's height, names and items");
        }

        private static Dictionary<string, string> Kinds(StoreReading reading)
        {
            return reading.InForce == null
                ? new Dictionary<string, string>()
                : reading.InForce.Records.ToDictionary(record => record.Hex, record => record.Form.Kind, StringComparer.Ordinal);
        }

        private static int Count(string after)
        {
            return string.IsNullOrEmpty(after) ? 0 : after.Split(' ').Length;
        }

        private static AuthorityService Service(MemoryStore store)
        {
            return new AuthorityService(store, new InMemoryKeyValueStore(), NullGateLog.Instance);
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreWriteVectors.json");
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreWriteVectors.json is not above the test's directory");
        }
    }
}
