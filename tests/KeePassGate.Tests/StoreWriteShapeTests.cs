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
    /// sequence, `STORE.md` §627: what each write deletes and tells of as deleted, what is present
    /// after it, how high its summary entry is, how many summary entries it names and how many
    /// items it holds, how many store keys stand, and what the store answers.
    /// </summary>
    /// <remarks>
    /// `tests/Shared/StoreWriteVectors.json` holds the file after each act of two sequences that
    /// `model/crdt_model.py` played, written out by `probe/store.py`'s `Encoder`. Its records hold
    /// stand-ins, short names and nonces of the model's own, so the product is held by shape and
    /// never by bytes: by kind, by the number a summary entry carries, and by count.
    ///
    /// **Both sequences are played.** The commands of the authority are the product's own. A
    /// pairing and a revocation are written as an act with no ground that adds the record the
    /// model added, a client's record and a revocation of its client key, since this build keeps
    /// the admissions beside the store; what the model deleted of the admissions is therefore not
    /// compared, and neither are the kinds this build does not write: the admissions, the profile
    /// name and the version of the root's rule the model's start holds, where the product's start
    /// holds the version record a Create writes, restated by the item `format` in the place of the
    /// version's item. The store the sequence starts from is the product's Create.
    /// </remarks>
    [TestFixture]
    public sealed class StoreWriteShapeTests
    {
        /// <summary>The kinds compared: every kind this build writes or the sequences add.</summary>
        private static readonly string[] Compared = { "authority", "head", "retired", "revoked" };

        [Test]
        public void One_copy_paired_revoked_recreated_deleted_and_created_again_writes_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["one copy"]["steps"];
            var store = new MemoryStore();
            StoreSeeds.Authority(store, new InMemoryKeyValueStore());

            foreach (var step in steps)
            {
                var act = (JArray)step["act"];
                AssertShape(step, store, () => Played(store, (string)step["command"], act));
            }
        }

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

        /// <summary>A command of the sequence, played by the product.</summary>
        private static AuthorityChange Played(MemoryStore store, string command, JArray act)
        {
            switch (command)
            {
                case "Recreate":
                    return Service(store).Recreate();
                case "Delete":
                    return Service(store).Delete();
                case "Create":
                    return Service(store).Create();
                case "Pair":
                    return Added(store, "client", "client" + store.Writes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                case "Revoke":
                    return Added(store, "revoked", (string)act[2]);
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, "a command this test does not play");
            }
        }

        /// <summary>A record of a kind this build does not write yet, added by an act with no ground.</summary>
        private static AuthorityChange Added(MemoryStore store, string kind, string client)
        {
            byte[] key;
            string against;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
                against = reading.InForce.Key.Name;
            }

            try
            {
                var record = StoreRecord.Compose(key, kind, new Dictionary<string, string> { { "client", client } });
                var written = store.Write(new[] { new StoreAct(kind, key, against, new[] { record }, null) });
                return written.Taken ? AuthorityChange.Done() : AuthorityChange.Refused(written.Reason);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private static void AssertShape(JToken step, MemoryStore store, Func<AuthorityChange> act)
        {
            var command = (string)step["command"];
            var before = store.Read();
            var kinds = Kinds(before);
            var heights = before.InForce.Heads.ToDictionary(head => head.Hex, head => head.Form.Number, StringComparer.Ordinal);
            var deletedBefore = new HashSet<string>(before.Deletions.Select(KeePassGate.Hex.Of), StringComparer.Ordinal);

            var done = act();
            Assert.That(done.Succeeded, Is.True, command + ": " + done.Message);

            var reading = store.Read();
            var after = Kinds(reading);

            var deleted = kinds.Where(pair => !after.ContainsKey(pair.Key)).Select(pair => pair.Value)
                .Where(Compared.Contains).OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            var modelDeleted = ((JArray)step["deleted"]).Select(item => (string)item["kind"])
                .Where(Compared.Contains).OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            Assert.That(deleted, Is.EqualTo(modelDeleted), command + ": the records deleted");

            // The deletion records the write added: for a record the file held, by its kind and
            // the number it carries, a summary entry's height; for a record it did not hold, by
            // their count.
            var added = reading.Deletions.Select(KeePassGate.Hex.Of).Where(hex => !deletedBefore.Contains(hex)).ToList();
            var held = added.Where(kinds.ContainsKey)
                .Select(hex => (Kind: kinds[hex], Number: HeightOf(heights, hex)))
                .Where(item => Compared.Contains(item.Kind))
                .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Number).ToList();
            var previous = Previous(step);
            var modelAdded = ((JArray)step["deletions"]).Where(item => !previous.Deletions.Contains((string)item["id"])).ToList();
            var modelHeld = modelAdded.Where(item => previous.Present.Contains((string)item["id"]))
                .Select(item => (Kind: (string)item["kind"], Number: (int)item["number"]))
                .Where(item => Compared.Contains(item.Kind))
                .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Number).ToList();
            Assert.That(held, Is.EqualTo(modelHeld), command + ": the deletion records added for records the file held");
            Assert.That(
                added.Count(hex => !kinds.ContainsKey(hex)),
                Is.EqualTo(modelAdded.Count(item => !previous.Present.Contains((string)item["id"]) && Compared.Contains((string)item["kind"]))),
                command + ": the deletion records added for records the file did not hold");

            var present = after.Values.Where(Compared.Contains).GroupBy(kind => kind).ToDictionary(group => group.Key, group => group.Count());
            var modelPresent = ((JArray)step["present"]).Select(item => (string)item["kind"]).Where(Compared.Contains)
                .GroupBy(kind => kind).ToDictionary(group => group.Key, group => group.Count());
            Assert.That(present, Is.EquivalentTo(modelPresent), command + ": the records present");

            var heads = reading.InForce.Heads
                .Select(head => (Height: head.Form.Number, After: Count(head.Form.Member("after")), Items: head.Form.Items.Count))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ToList();
            var modelHeads = ((JArray)step["heads"])
                .Select(head => (Height: (int)head["height"], After: (int)head["after"], Items: ((JArray)head["items"]).Count))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ToList();
            Assert.That(heads, Is.EqualTo(modelHeads), command + ": each summary entry's height, names and items");

            Assert.That(((JArray)step["keys"]).Count, Is.EqualTo(1), command + ": the model's file holds a single store key");
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), command + ": " + reading.Reason);

            var answer = step["answer"];
            Assert.That((string)answer["status"], Is.EqualTo("ok"), command);
            Assert.That(
                StoreAuthorities.Served(reading) != null,
                Is.EqualTo(answer["served"].Type != JTokenType.Null),
                command + ": whether an authority is served");
            Assert.That(reading.Answering.Count, Is.EqualTo(((JArray)answer["auths"]).Count), command + ": the authorities standing");
            foreach (var kind in new[] { "retired", "revoked" })
            {
                var refusals = ((JArray)answer["refusals"]).Count(refusal => (string)refusal[0] == kind);
                Assert.That(reading.InForce.Taken(kind).Count, Is.EqualTo(refusals), command + ": what is " + kind);
            }
        }

        /// <summary>The height of a summary entry, or 0 for a record of another kind, as the model numbers it.</summary>
        private static int HeightOf(IDictionary<string, int> heights, string hex)
        {
            int height;
            return heights.TryGetValue(hex, out height) ? height : 0;
        }

        /// <summary>
        /// The identifiers the model's file held and its deletion records named before this step,
        /// in the copy the step was played in: its last step, or the start, which every copy
        /// begins as.
        /// </summary>
        private static (ISet<string> Present, ISet<string> Deletions) Previous(JToken step)
        {
            var steps = (JArray)step.Parent;
            var copy = (int)step["copy"];
            var last = steps.Take(steps.IndexOf(step)).LastOrDefault(earlier => (int)earlier["copy"] == copy);
            if (last == null)
            {
                var start = ((JObject)steps.Parent.Parent)["start"];
                return (
                    new HashSet<string>(((JObject)start["file"]["entries"]).Properties().Select(entry => entry.Name), StringComparer.Ordinal),
                    new HashSet<string>(((JArray)start["deletions"]).Select(Id), StringComparer.Ordinal));
            }

            return (
                new HashSet<string>(((JArray)last["present"]).Select(Id), StringComparer.Ordinal),
                new HashSet<string>(((JArray)last["deletions"]).Select(Id), StringComparer.Ordinal));
        }

        /// <summary>The identifier of an item the model lists, or the item itself where it is the identifier.</summary>
        private static string Id(JToken item)
        {
            return item.Type == JTokenType.String ? (string)item : (string)item["id"];
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
