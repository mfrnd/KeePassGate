using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
    /// `tests/Shared/StoreWriteVectors.json` holds the file after each act of sequences that
    /// `model/crdt_model.py` played, written out by `probe/store.py`'s `Encoder`. Its records hold
    /// stand-ins, short names and nonces of the model's own, so the product is held by shape and
    /// never by bytes: by kind, by the number a summary entry carries, and by count.
    ///
    /// **Every sequence is played, through the product's own acts.** The commands of the authority
    /// are `AuthorityService`'s, and a pairing, a renewal and a revocation are `AdmissionActs`'s,
    /// each written as the store writes an act: a certificate is issued for each of the model's
    /// clients, a pairing admits it under the authority served, a renewal admits a further
    /// certificate under the same client key, and a revocation names that client key. The
    /// admission the model's start holds is put into the store beside the product's Create, so
    /// the records of kind `client` are compared like every other kind: what each write deletes of
    /// them because a revocation or a retirement refuses them, `STORE.md` §627, and what stays. Not
    /// compared are the kinds this build does not write: the profile name and the version of the
    /// root's rule the model's start holds, where the product's start holds the version record a
    /// Create writes, restated by the item `format` in the place of the version's item. Where the
    /// model writes otherwise than `STORE.md` fixes, as a renewal's admission without `from`, the
    /// shape is the same and the members are left to the product's own tests.
    /// </remarks>
    [TestFixture]
    public sealed class StoreWriteShapeTests
    {
        /// <summary>The kinds compared: every kind this build writes or the sequences add.</summary>
        private static readonly string[] Compared = { "authority", "client", "head", "retired", "revoked" };

        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);

        [Test]
        public void One_copy_paired_revoked_recreated_deleted_and_created_again_writes_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["one copy"]["steps"];
            using (var clients = new Clients())
            {
                var store = new MemoryStore();
                StoreSeeds.Authority(store);
                Started(store, clients);

                foreach (var step in steps)
                {
                    AssertShape(step, store, () => Played(store, step, clients));
                }
            }
        }

        [Test]
        public void Two_copies_that_each_recreate_synchronized_and_kept_write_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["two copies"]["steps"];
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
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
        }

        [Test]
        public void Two_copies_one_revoking_a_client_the_other_renews_synchronized_admit_nobody_of_that_client()
        {
            // `STORE.md` §615: the revocation names the client key, so the certificate renewed in a
            // copy that had not read it is refused once the copies meet, in each of them.
            var steps = (JArray)Vectors()["sequences"]["two copies, revoked in the first and renewed in the second"]["steps"];
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                var second = first.Copy();

                AssertShape(steps[0], first, () => Played(first, steps[0], clients));
                AssertShape(steps[1], second, () => Played(second, steps[1], clients));
                AssertShape(steps[2], first, () =>
                {
                    first.Synchronize(second);
                    return AuthorityChange.Done();
                });
                AssertShape(steps[3], second, () =>
                {
                    second.Synchronize(first);
                    return AuthorityChange.Done();
                });

                foreach (var copy in new[] { first, second })
                {
                    using (var reading = copy.Read())
                    {
                        Assert.That(StoreAdmissions.Admitted(reading, clients.Admission("c0.1").AdmissionKey), Is.Null,
                            "the certificate renewed in the copy that had not read the revocation is refused");
                        Assert.That(StoreAdmissions.Admitted(reading, clients.Admission("c0").AdmissionKey), Is.Null);
                    }
                }

                Assert.That(Held(first), Is.EquivalentTo(Held(second)), "and both copies hold the same records");
            }
        }

        /// <summary>A command of a sequence, played by the product.</summary>
        private static AuthorityChange Played(MemoryStore store, JToken step, Clients clients)
        {
            var command = (string)step["command"];
            var act = (JArray)step["act"];
            switch (command)
            {
                case "Recreate":
                    return Service(store).Recreate();
                case "Delete":
                    return Service(store).Delete();
                case "Create":
                    return Service(store).Create();
                case "Pair":
                    StoreSeeds.Admit(store, clients.Paired(StoreSeeds.Served(store)));
                    return AuthorityChange.Done();
                case "Renew":
                    StoreSeeds.Admit(store, clients.Renewed((string)act[2], StoreSeeds.Served(store)));
                    return AuthorityChange.Done();
                case "Revoke":
                    StoreSeeds.Revoke(store, clients.Admission((string)act[2]).ClientKey);
                    return AuthorityChange.Done();
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), command, "a command this test does not play");
            }
        }

        /// <summary>
        /// The admission the model's start holds beside its first summary entry, `c0`, put into the
        /// store as an entry and not by a write, since the start's summary entry is its first.
        /// </summary>
        private static void Started(MemoryStore store, Clients clients)
        {
            using (var reading = store.Read())
            {
                var admission = clients.Started(StoreAuthorities.Served(reading));
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    store.Put(StoreComposer.Record(
                        key,
                        "client",
                        "client:" + admission.ClientKey,
                        StoreComposer.Content(admission.Members().Select(member => (member.Key, member.Value)).ToArray())));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
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
                .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
            var modelHeads = ((JArray)step["heads"])
                .Select(head => (Height: (int)head["height"], After: (int)head["after"], Items: ((JArray)head["items"]).Count))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
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

        /// <summary>
        /// Every record present and every deletion record of a store, by identifier.
        /// </summary>
        private static IEnumerable<string> Held(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return Kinds(reading).Keys.Concat(reading.Deletions.Select(deleted => "deleted " + KeePassGate.Hex.Of(deleted))).ToList();
            }
        }

        private static int Count(string after)
        {
            return string.IsNullOrEmpty(after) ? 0 : after.Split(' ').Length;
        }

        private static AuthorityService Service(MemoryStore store)
        {
            return new AuthorityService(store, NullGateLog.Instance);
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

        /// <summary>
        /// The model's clients as the product admits them: a certificate issued for each of the
        /// model's names, `c0` the client the start holds, `c1` and on the clients that pair in the
        /// order they pair, and a name and `.1` the certificate a renewal of that client issued.
        /// </summary>
        private sealed class Clients : IDisposable
        {
            private readonly Dictionary<string, AdmissionRecord> _admitted = new Dictionary<string, AdmissionRecord>(StringComparer.Ordinal);
            private readonly List<X509Certificate2> _issued = new List<X509Certificate2>();

            /// <summary>The admission of the model's name.</summary>
            public AdmissionRecord Admission(string name)
            {
                return _admitted[name];
            }

            /// <summary>
            /// The admission of `c0`, with the profile the model's start gives it.
            /// </summary>
            public AdmissionRecord Started(string issuer)
            {
                return Admit("c0", AdmissionRecord.For(Issue("c0"), Then, new[] { "pf" }, issuer));
            }

            /// <summary>
            /// A pairing's admission of the next client, with the profiles a model's pairing gives.
            /// </summary>
            public AdmissionRecord Paired(string issuer)
            {
                var name = "c" + _admitted.Keys.Count(known => known.IndexOf('.') < 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Admit(name, AdmissionRecord.For(Issue(name), Then, new[] { "pf", "pg" }, issuer));
            }

            /// <summary>
            /// A renewal's admission of the client of this name, from its admission and with its
            /// profiles.
            /// </summary>
            public AdmissionRecord Renewed(string name, string issuer)
            {
                var from = _admitted[name];
                var renewed = name + ".1";
                return Admit(renewed, AdmissionRecord.For(Issue(renewed), Then, from.Profiles, issuer, from.ClientKey, from.AdmissionKey));
            }

            public void Dispose()
            {
                foreach (var certificate in _issued)
                {
                    certificate.Reset();
                }
            }

            private AdmissionRecord Admit(string name, AdmissionRecord admission)
            {
                _admitted[name] = admission;
                return admission;
            }

            private X509Certificate2 Issue(string name)
            {
                var certificate = Minted.Named("CN=" + name).Lasting(Then.AddDays(-1), Then.AddYears(1)).SelfSigned().Read();
                _issued.Add(certificate);
                return certificate;
            }
        }
    }
}
