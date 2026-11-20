using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Security;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Profile names added and removed over two copies of a real database that the product wrote
    /// apart and KeePass's own merge brings together, `STORE.md` §616 and §644 item 32.
    /// </summary>
    /// <remarks>
    /// Each sequence starts from a database holding an authority, written further where a
    /// sequence calls for it, saved and copied as a file copied to another machine is. Each copy
    /// is then written by the product's own acts, the copies are saved, and each is merged into
    /// the other by `PwDatabase.MergeIn`, under every method KeePass offers, from the other copy's
    /// file opened afresh. The copy merged into is asked which names are on its list, and asked
    /// again after its next write of the plugin's own, a pairing that names no profile, which
    /// deletes every addition a removal defeated; that no such addition is left is held as well.
    ///
    /// **Create new IDs brings no record**: every entry of the other copy arrives under another
    /// identifier, where it is a copy the store passes over, so the copy imported into names what
    /// it named before, `STORE.md` §621 and §640.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StoreProfileMergeTests
    {
        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.CreateNewUuids
        };

        [Test]
        public void A_name_added_in_each_copy_apart_is_on_the_list_in_both()
        {
            Run(
                first => Name(first, "reader"),
                second => Name(second, "auditor"),
                Answers("reader", true, "auditor", true));
        }

        [Test]
        public void A_name_both_copies_hold_removed_in_a_copy_is_off_the_list_wherever_the_copies_meet()
        {
            Run(
                first => Unname(first, "ops"),
                second => { },
                Answers("ops", false),
                before: copy => Name(copy, "ops"));
        }

        [Test]
        public void A_name_removed_in_each_copy_and_added_again_in_a_copy_is_off_the_list_once_they_meet()
        {
            // The addition made again names the removal its copy made and not the other's, which
            // wins over it, `STORE.md` §642 row 23.
            Run(
                first =>
                {
                    Unname(first, "ops");
                    Name(first, "ops");
                },
                second => Unname(second, "ops"),
                Answers("ops", false),
                before: copy => Name(copy, "ops"));
        }

        [Test]
        public void A_name_added_again_after_the_copies_met_names_both_removals_and_is_on_the_list()
        {
            Run(
                first =>
                {
                    Unname(first, "ops");
                    Name(first, "ops");
                },
                second => Unname(second, "ops"),
                Answers("ops", true),
                before: copy => Name(copy, "ops"),
                afterTheMerge: copy => Name(copy, "ops"));
        }

        [Test]
        public void A_pairing_in_a_copy_that_approved_a_new_name_puts_it_on_the_list_of_a_copy_from_before()
        {
            Run(
                first => Pair(first, "fresh"),
                second => { },
                Answers("fresh", true));
        }

        [Test]
        public void A_pairing_that_put_a_removed_name_back_is_overruled_by_a_removal_in_the_other_copy()
        {
            Run(
                first =>
                {
                    Unname(first, "ops");
                    Pair(first, "ops");
                },
                second => Unname(second, "ops"),
                Answers("ops", false),
                before: copy => Name(copy, "ops"));
        }

        [Test]
        public void A_removal_deleted_for_good_still_takes_the_name_off_by_the_summary_entry_that_restates_it()
        {
            // The removal's entry deleted in KeePass leaves a deletion record, which only a
            // Synchronize carries, and an item of the summary entry its write made, which every
            // method but Create new IDs brings: the name stays off wherever the copies meet.
            Run(
                first =>
                {
                    Unname(first, "ops");
                    DeleteForGood(first, StoreKinds.Removed);
                },
                second => { },
                Answers("ops", false),
                before: copy => Name(copy, "ops"));
        }

        [TestCase("added on the Profiles page")]
        [TestCase("approved for a pairing")]
        public void A_name_whose_removal_only_a_deletion_record_tells_of_is_on_the_list_once_it_is_added_back(string how)
        {
            // The first copy removes the name, and that write deletes the addition it defeats.
            // There the removal is then deleted for good, as KeePass deletes an entry, and the
            // summary entry restating it by a program that keeps no deletion record. A
            // Synchronize brings the deletion records into the second copy, which wrote a summary
            // entry of its own: nothing in it names the name but the deletion record of its
            // removal, which the reading tells of once the name is known to the file, `STORE.md`
            // §616 and §623 step 2. Added back, the name is on the list in the file, and after
            // the next write of the plugin's own.
            //
            // The addition is dated a minute back before the copy, as an addition written before a
            // removal is: KeePass keeps whole seconds, and a Synchronize keeps an entry last
            // changed in the second of its deletion, with the name it names.
            using (var first = TemporaryDatabase.New())
            {
                StoreSeeds.Authority(Store(first));
                Name(first, "ops");
                Stamped(first, StoreKinds.Profile, DateTime.UtcNow.AddMinutes(-1));
                first.Save();
                using (var second = Opened(first))
                {
                    Unname(first, "ops");
                    var removal = new StoredAdmissions(first.Database).Entries(StoreKinds.Removed).Single().Uuid;
                    Assert.That(new StoredAdmissions(first.Database).Entries(StoreKinds.Profile), Is.Empty);
                    DeleteForGood(first, StoreKinds.Removed);
                    DeleteLeavingNoRecord(first, StoreKinds.Head);
                    first.Save();

                    Name(second, "reader");
                    using (var source = Opened(first))
                    {
                        second.Database.MergeIn(source.Database, PwMergeMethod.Synchronize, null);
                    }

                    second.SaveCloseAndReopen();
                    Assert.That(State(second), Is.EqualTo(StoreState.Whole));
                    Assert.That(second.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(removal)), Is.True,
                        "the deletion record of the removal is in the file");
                    using (var reading = Store(second).Read())
                    {
                        Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");
                        Assert.That(ProfileNames.Of(reading).Names, Is.EqualTo(new[] { "reader" }));
                    }

                    if (how == "approved for a pairing")
                    {
                        Pair(second, "ops");
                    }
                    else
                    {
                        Name(second, "ops");
                    }

                    second.SaveCloseAndReopen();
                    Assert.That(Answers(second, new[] { "ops" })["ops"], Is.True, "on the list once added back");

                    Pair(second);
                    second.SaveCloseAndReopen();
                    Assert.That(Answers(second, new[] { "ops" })["ops"], Is.True, "and after the next write");
                    Assert.That(Defeated(second), Is.Empty);
                }
            }
        }

        [Test]
        public void A_name_removed_in_a_copy_stays_off_whatever_became_of_the_entries_times()
        {
            // The removal's entry dated back a year before the copies meet, and the other copy's
            // addition stamped a day ahead: no time decides which record counts, `STORE.md` §610.
            Run(
                first =>
                {
                    Unname(first, "ops");
                    Stamped(first, StoreKinds.Removed, DateTime.UtcNow.AddYears(-1));
                },
                second => Stamped(second, StoreKinds.Profile, DateTime.UtcNow.AddDays(1)),
                Answers("ops", false),
                before: copy => Name(copy, "ops"));
        }

        [Test]
        public void An_addition_deleted_by_hand_takes_the_name_off_wherever_its_deletion_record_is()
        {
            // `STORE.md` §642 row 24: the copy that deleted it holds the deletion record, which
            // takes out of force an addition the other copy brings back; only a Synchronize
            // carries the record, so a copy merged into by another method keeps the name.
            Run(
                first => DeleteForGood(first, StoreKinds.Profile),
                second => { },
                new[] { "ops" },
                (method, intoFirst) => Answers("ops", !intoFirst && method != PwMergeMethod.Synchronize),
                copy => Name(copy, "ops"),
                null,
                null);
        }

        [Test]
        public void An_addition_deduplicated_takes_the_name_off_where_the_entry_it_was_written_as_went()
        {
            // `STORE.md` §642 row 24: KeePass's Delete Duplicate Entries after a duplicate of the
            // addition. A copy under another identifier is passed over, so the name stays where
            // the entry written as the addition stays, and goes where its deletion record goes.
            //
            // The addition is dated a minute back before the copy, as a duplicate made and deleted
            // later finds it: KeePass keeps whole seconds, and a Synchronize takes an entry last
            // changed in the second of its deletion for an entry changed since, keeping it and
            // dropping the deletion record, which leaves the name on the list as the contract has
            // it.
            var kept = false;
            Run(
                first => kept = Deduplicated(first),
                second => { },
                new[] { "ops" },
                (method, intoFirst) => Answers("ops", kept || (!intoFirst && method != PwMergeMethod.Synchronize)),
                copy =>
                {
                    Name(copy, "ops");
                    Stamped(copy, StoreKinds.Profile, DateTime.UtcNow.AddMinutes(-1));
                },
                null,
                null);
            TestContext.Out.WriteLine(kept ? "the entry written as the addition was kept" : "its duplicate was kept");
        }

        [Test]
        public void A_removal_edited_past_the_history_limit_stops_the_store_where_the_edit_is_kept()
        {
            // Both copies hold the removal; edited in a copy, then edited ten times more, the
            // version written leaves the entry's history and no version of it passes, `STORE.md`
            // §622: a store holding it stops, §621 and §624, and a store that kept the version
            // written keeps the name off. Into the first copy, OverwriteExisting writes the other
            // copy's version over the edited entry; into the second, KeepExisting keeps its own.
            Run(
                first => EditPastTheLimit(first, StoreKinds.Removed),
                second => { },
                new[] { "ops" },
                (method, intoFirst) => Answers("ops", false),
                copy =>
                {
                    Name(copy, "ops");
                    Unname(copy, "ops");
                },
                null,
                (method, intoFirst) => intoFirst
                    ? method != PwMergeMethod.OverwriteExisting
                    : method != PwMergeMethod.KeepExisting && method != PwMergeMethod.CreateNewUuids);
        }

        [Test]
        public void Twenty_names_added_in_each_copy_are_all_on_the_list_once_the_copies_meet()
        {
            var firsts = Enumerable.Range(0, 20).Select(index => "first-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var seconds = Enumerable.Range(0, 20).Select(index => "second-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Run(
                first =>
                {
                    foreach (var name in firsts)
                    {
                        Name(first, name);
                    }
                },
                second =>
                {
                    foreach (var name in seconds)
                    {
                        Name(second, name);
                    }
                },
                firsts.Concat(seconds).ToDictionary(name => name, name => true, StringComparer.Ordinal));
        }

        /// <summary>
        /// A sequence over two copies, merged each into the other under every method and asked
        /// which names are on the list.
        /// </summary>
        /// <param name="inFirst">What is done in the first copy, through the product's acts.</param>
        /// <param name="inSecond">What is done in the second copy.</param>
        /// <param name="expected">
        /// Each name asked about and whether it is on the list of the copy merged into, whichever
        /// method and direction; not asked for Create new IDs, under which the copy merged into
        /// names what it named before.
        /// </param>
        /// <param name="before">What is done before the database is copied.</param>
        /// <param name="afterTheMerge">
        /// What is done in the copy merged into once the copies met, before it is asked; not done
        /// after Create new IDs.
        /// </param>
        private static void Run(
            Action<TemporaryDatabase> inFirst,
            Action<TemporaryDatabase> inSecond,
            IDictionary<string, bool> expected,
            Action<TemporaryDatabase> before = null,
            Action<TemporaryDatabase> afterTheMerge = null)
        {
            Run(inFirst, inSecond, expected.Keys.ToList(), (method, intoFirst) => expected, before, afterTheMerge, null);
        }

        /// <summary>
        /// The same, with what is on the list by method and direction, and where the copy merged
        /// into is left stopped, which is asserted there in the place of the names.
        /// </summary>
        /// <param name="names">The names asked about.</param>
        /// <param name="expected">Whether each is on the list, by method and copy merged into.</param>
        /// <param name="stops">
        /// Where the copy merged into is left stopped, by method and copy; null for nowhere.
        /// </param>
        private static void Run(
            Action<TemporaryDatabase> inFirst,
            Action<TemporaryDatabase> inSecond,
            IReadOnlyList<string> names,
            Func<PwMergeMethod, bool, IDictionary<string, bool>> expected,
            Action<TemporaryDatabase> before,
            Action<TemporaryDatabase> afterTheMerge,
            Func<PwMergeMethod, bool, bool> stops)
        {
            using (var first = TemporaryDatabase.New())
            {
                StoreSeeds.Authority(Store(first));
                if (before != null)
                {
                    before(first);
                }

                first.Save();
                using (var second = Opened(first))
                {
                    inFirst(first);
                    inSecond(second);
                    first.Save();
                    second.Save();

                    var wrong = new List<string>();
                    var measured = new StringBuilder();
                    foreach (var method in Methods)
                    {
                        foreach (var intoFirst in new[] { true, false })
                        {
                            var named = method + (intoFirst ? ", into the first copy" : ", into the second copy");
                            using (var target = Opened(intoFirst ? first : second))
                            using (var source = Opened(intoFirst ? second : first))
                            {
                                var asBefore = Answers(target, names);
                                target.Database.MergeIn(source.Database, method, null);
                                var stopped = stops != null && stops(method, intoFirst);
                                if (stopped || State(target) != StoreState.Whole)
                                {
                                    measured.AppendLine(named + ": the store is " + State(target));
                                    if (State(target) != (stopped ? StoreState.Stopped : StoreState.Whole))
                                    {
                                        wrong.Add(named + ": the store is " + State(target) + (stopped ? ", not stopped" : string.Empty));
                                    }

                                    continue;
                                }

                                if (afterTheMerge != null && method != PwMergeMethod.CreateNewUuids)
                                {
                                    afterTheMerge(target);
                                }

                                var wanted = method == PwMergeMethod.CreateNewUuids ? asBefore : expected(method, intoFirst);
                                var after = Answers(target, names);
                                measured.AppendLine(named + ": " + Show(after));
                                Compare(wrong, named, wanted, after);

                                // The next write of the plugin's own deletes every addition a
                                // removal defeated, and leaves the list as it was.
                                Pair(target);
                                Compare(wrong, named + ", after the next write", wanted, Answers(target, names));
                                wrong.AddRange(Defeated(target).Select(name => named + ": an addition of " + name + " a removal defeated is left after the next write"));
                            }
                        }
                    }

                    TestContext.Out.WriteLine(measured.ToString());
                    Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
                }
            }
        }

        private static void Compare(List<string> wrong, string named, IDictionary<string, bool> wanted, IDictionary<string, bool> answered)
        {
            wrong.AddRange(wanted
                .Where(answer => answered[answer.Key] != answer.Value)
                .Select(answer => named + ": " + answer.Key + (answered[answer.Key] ? " is on the list" : " is off the list")));
        }

        /// <summary>Names and whether each is on the list, in pairs.</summary>
        private static IDictionary<string, bool> Answers(params object[] pairs)
        {
            var answers = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var at = 0; at < pairs.Length; at += 2)
            {
                answers[(string)pairs[at]] = (bool)pairs[at + 1];
            }

            return answers;
        }

        /// <summary>Whether each of these names is on the list of this copy now.</summary>
        private static IDictionary<string, bool> Answers(TemporaryDatabase copy, IEnumerable<string> names)
        {
            using (var reading = Store(copy).Read())
            {
                var listed = ProfileNames.Of(reading);
                return names.ToDictionary(name => name, listed.Holds, StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// The names of the additions present that do not name every removal of their name the
        /// store knows: what a write of the plugin's own deletes, `STORE.md` §627 step 3.
        /// </summary>
        private static IEnumerable<string> Defeated(TemporaryDatabase copy)
        {
            using (var reading = Store(copy).Read())
            {
                return reading.InForce.Additions
                    .Select(addition => (
                        Name: addition.Form.Member(StoreKinds.NameMember),
                        After: new HashSet<string>(StoreFormat.Identifiers(addition.Form.Member(StoreKinds.After)) ?? Array.Empty<string>(), StringComparer.Ordinal)))
                    .Where(addition => addition.Name != null && !reading.InForce.RemovalsOf(addition.Name).All(addition.After.Contains))
                    .Select(addition => addition.Name)
                    .ToList();
            }
        }

        private static string Show(IDictionary<string, bool> answers)
        {
            return string.Join(", ", answers.Select(answer => answer.Key + (answer.Value ? " on" : " off")));
        }

        private static StoreState State(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State;
            }
        }

        /// <summary>
        /// A saved database opened afresh from a copy of its file, as KeePass's import and
        /// synchronize open the other copy, and as each merge opens the copy it merges into.
        /// </summary>
        private static TemporaryDatabase Opened(TemporaryDatabase saved)
        {
            var copy = TemporaryDatabase.New();
            System.IO.File.Copy(saved.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        private static KdbxStore Store(TemporaryDatabase database)
        {
            return new KdbxStore(database.Database, GateVersion.Current, NullGateLog.Instance);
        }

        /// <summary>A name added, as the Profiles page writes it at the settings dialog's OK.</summary>
        private static void Name(TemporaryDatabase copy, string name)
        {
            StoreSeeds.Name(Store(copy), name);
        }

        /// <summary>A name removed, as the Profiles page writes it at the settings dialog's OK.</summary>
        private static void Unname(TemporaryDatabase copy, string name)
        {
            StoreSeeds.Unname(Store(copy), name);
        }

        /// <summary>
        /// A pairing that approves these profiles, as the pairing's write admits it; with none, a
        /// pairing of another client as the copy's next write of the plugin's own.
        /// </summary>
        private static void Pair(TemporaryDatabase copy, params string[] profiles)
        {
            var store = Store(copy);
            using (var certificate = new AuthorityIssuer().IssueClient(StoreSeeds.KeyOf(store, StoreSeeds.Served(store)), "agent-fictitious", profiles).Read())
            {
                StoreSeeds.Admit(store, certificate, DateTimeOffset.UtcNow, profiles);
            }
        }

        /// <summary>Every entry of this kind stamped as last changed at this moment.</summary>
        private static void Stamped(TemporaryDatabase copy, string kind, DateTime at)
        {
            foreach (var entry in new StoredAdmissions(copy.Database).Entries(kind))
            {
                entry.LastModificationTime = at;
            }
        }

        /// <summary>
        /// The addition duplicated, as KeePass's Duplicate Entry does, and the duplicates deleted
        /// by KeePass's Delete Duplicate Entries; answers whether the entry written is the entry
        /// kept.
        /// </summary>
        private static bool Deduplicated(TemporaryDatabase copy)
        {
            var written = new StoredAdmissions(copy.Database).Entries(StoreKinds.Profile).Single();
            var duplicate = written.Duplicate();
            written.ParentGroup.AddEntry(duplicate, true);
            Assume.That(copy.Database.DeleteDuplicateEntries(null), Is.EqualTo(1u), "a single duplicate deleted");
            return new StoredAdmissions(copy.Database).Entries(StoreKinds.Profile).Single().Uuid.Equals(written.Uuid);
        }

        /// <summary>
        /// The single entry of this kind edited in KeePass's entry dialog, its content changed,
        /// then edited ten times more, each edit a second after the version before it and putting
        /// that version into the entry's history, which KeePass trims to its limit.
        /// </summary>
        private static void EditPastTheLimit(TemporaryDatabase copy, string kind)
        {
            var entry = new StoredAdmissions(copy.Database).Entries(kind).Single();
            var at = entry.LastModificationTime;
            entry.CreateBackup(copy.Database);
            var content = JObject.Parse(entry.Strings.ReadSafe(StoreComposer.Data));
            content[StoreKinds.NameMember] = "edited";
            entry.Strings.Set(StoreComposer.Data, new ProtectedString(false, content.ToString(Newtonsoft.Json.Formatting.None)));
            Touched(entry, at = at.AddSeconds(1));

            for (var edit = 0; edit < 10; edit++)
            {
                entry.CreateBackup(copy.Database);
                entry.Expires = !entry.Expires;
                Touched(entry, at = at.AddSeconds(1));
            }
        }

        private static void Touched(PwEntry entry, DateTime at)
        {
            entry.Touch(true, false);
            entry.LastModificationTime = at;
        }

        /// <summary>
        /// The single entry of this kind deleted for good, leaving a deletion record, as KeePass's
        /// delete without the recycle bin does.
        /// </summary>
        private static void DeleteForGood(TemporaryDatabase copy, string kind)
        {
            var entry = new StoredAdmissions(copy.Database).Entries(kind).Single();
            entry.ParentGroup.Entries.Remove(entry);

            // A second after now, as a deletion made after the entry was written is: a merge
            // applies a deletion record only to an entry last changed before it.
            copy.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
        }

        /// <summary>
        /// The single entry of this kind deleted with no deletion record, as a program that keeps
        /// none does.
        /// </summary>
        private static void DeleteLeavingNoRecord(TemporaryDatabase copy, string kind)
        {
            var entry = new StoredAdmissions(copy.Database).Entries(kind).Single();
            entry.ParentGroup.Entries.Remove(entry);
        }
    }
}
