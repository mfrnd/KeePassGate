using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KeePassLib;
using KeePassLib.Collections;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The rule KeePass merges `PwDatabase.CustomData` by, pinned against the KeePass this
    /// repository builds with.
    /// </summary>
    /// <remarks>
    /// `PwDatabase.MergeInDbProperties` visits the source's keys and no others, so a key only the
    /// target holds is never looked at, whatever the method. What decides a key both sides hold is
    /// its modification time, the method and `SettingsChanged`, and never the value. A key that is
    /// only ever added, and never changed, therefore survives every merge that copies custom data,
    /// whatever the clocks say and whether or not the times were dropped by a program that saved
    /// the file without them.
    ///
    /// `MergeInRealDatabaseTests` measures what that means for admission records; these measure
    /// the rule itself, on the dictionary directly, so a KeePass that changes it fails here before
    /// anything built on it is believed. The full matrix, and the program that produced it, are on
    /// `spikes/revocation-merge`.
    /// </remarks>
    [TestFixture]
    public sealed class CustomDataMergeRuleTests
    {
        private const string Key = "KPG.Test.Merged";
        private const string AddedKey = "KPG.Test.OnlyEverAdded";

        private static readonly PwUuid SharedRoot = new PwUuid(true);

        private static readonly DateTime Earlier = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Later = Earlier.AddMinutes(5);

        private static readonly PwMergeMethod[] EveryMethod =
        {
            PwMergeMethod.None,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.CreateNewUuids,
            PwMergeMethod.Synchronize,
        };

        /// <summary>The methods that copy custom data at all: `KeepExisting` and `None` copy none.</summary>
        private static readonly PwMergeMethod[] CopyingMethods =
        {
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.CreateNewUuids,
            PwMergeMethod.Synchronize,
        };

        private readonly List<TemporaryDatabase> _databases = new List<TemporaryDatabase>();

        [TearDown]
        public void TearDown()
        {
            foreach (var database in _databases)
            {
                database.Dispose();
            }

            _databases.Clear();
        }

        [Test]
        public void No_merge_method_removes_a_key_the_target_holds_and_the_source_does_not()
        {
            foreach (var method in EveryMethod)
            {
                foreach (var stamped in new[] { true, false })
                {
                    var target = Copy();
                    var source = Copy();
                    Write(target, Key, "held here", stamped ? (DateTime?)Earlier : null);

                    // The source knew the key and removed it, later, and its settings are newer:
                    // everything that could argue for the removal traveling is on its side.
                    Write(source, Key, "held there too", Later);
                    Assume.That(source.Database.CustomData.Remove(Key), Is.True);
                    source.Database.SettingsChanged = Later;

                    target.Database.MergeIn(source.Database, method, null);

                    Assert.That(target.Database.CustomData.Get(Key), Is.EqualTo("held here"),
                        method + (stamped ? ", stamped" : ", unstamped")
                        + ": a merge removed a key it never visited, which the design says it cannot");
                }
            }
        }

        [Test]
        public void The_value_never_decides_a_merge()
        {
            // Every arrangement of times the rule distinguishes. The same merge is run twice with
            // the two values swapped, one of them empty, which is what a revocation mark is: if the
            // value took any part, one of the two runs would pick the other side.
            var arrangements = new[]
            {
                new Arrangement("both stamped, source newer", Earlier, Later, false),
                new Arrangement("both stamped, target newer", Later, Earlier, false),
                new Arrangement("both stamped, equal", Later, Later, false),
                new Arrangement("target stamped only", Earlier, null, false),
                new Arrangement("source stamped only", null, Earlier, false),
                new Arrangement("neither stamped, settings equal", null, null, false),
                new Arrangement("neither stamped, source settings newer", null, null, true),
            };

            foreach (var method in EveryMethod)
            {
                foreach (var arrangement in arrangements)
                {
                    var once = WinningSide(method, arrangement, "a record", string.Empty);
                    var swapped = WinningSide(method, arrangement, string.Empty, "a record");

                    Assert.That(swapped, Is.EqualTo(once),
                        method + ", " + arrangement.Name + ": swapping the values changed which side won");
                }
            }
        }

        [Test]
        public void A_key_only_ever_added_survives_every_merge_that_copies_custom_data()
        {
            // Two copies of one file holding a key both know. One adds a second key and never
            // changes it; the other goes on being used. Every way the times can have been lost,
            // either direction, through the file each time.
            foreach (var method in CopyingMethods)
            {
                foreach (var strip in new[] { Strip.Neither, Strip.Adder, Strip.Other, Strip.Both })
                {
                    foreach (var otherSettingsNewer in new[] { false, true })
                    {
                        foreach (var intoAdder in new[] { true, false })
                        {
                            var adder = Copy();
                            var other = Copy();
                            Write(adder, Key, "both know this", Earlier);
                            Write(other, Key, "both know this", Earlier);

                            // Behind the other copy's clock, so a time could only ever lose.
                            Write(adder, AddedKey, "never changed", Earlier.AddSeconds(-60));

                            if (strip == Strip.Adder || strip == Strip.Both)
                            {
                                StripTimes(adder);
                            }

                            if (strip == Strip.Other || strip == Strip.Both)
                            {
                                StripTimes(other);
                            }

                            if (otherSettingsNewer)
                            {
                                other.Database.SettingsChanged = Later;
                            }

                            adder.SaveCloseAndReopen();
                            other.SaveCloseAndReopen();
                            Assume.That(Time(adder, AddedKey).HasValue,
                                Is.EqualTo(strip == Strip.Neither || strip == Strip.Other),
                                "the file kept or dropped the time as arranged");

                            var target = intoAdder ? adder : other;
                            var source = intoAdder ? other : adder;
                            target.Database.MergeIn(source.Database, method, null);
                            target.SaveCloseAndReopen();

                            Assert.That(target.Database.CustomData.Get(AddedKey), Is.EqualTo("never changed"),
                                method + ", times stripped on " + strip + (otherSettingsNewer ? ", other settings newer" : string.Empty)
                                + (intoAdder ? ", into the copy that added it" : ", into the other copy")
                                + ": a key that was only ever added did not survive the merge");
                        }
                    }
                }
            }
        }

        private enum Strip
        {
            Neither,
            Adder,
            Other,
            Both,
        }

        private sealed class Arrangement
        {
            public Arrangement(string name, DateTime? target, DateTime? source, bool sourceSettingsNewer)
            {
                Name = name;
                Target = target;
                Source = source;
                SourceSettingsNewer = sourceSettingsNewer;
            }

            public string Name { get; }

            public DateTime? Target { get; }

            public DateTime? Source { get; }

            public bool SourceSettingsNewer { get; }
        }

        private string WinningSide(PwMergeMethod method, Arrangement arrangement, string targetValue, string sourceValue)
        {
            var target = Copy();
            var source = Copy();
            Write(target, Key, targetValue, arrangement.Target);
            Write(source, Key, sourceValue, arrangement.Source);
            if (arrangement.SourceSettingsNewer)
            {
                source.Database.SettingsChanged = Later;
            }

            target.Database.MergeIn(source.Database, method, null);

            return target.Database.CustomData.Get(Key) == sourceValue ? "source" : "target";
        }

        /// <summary>
        /// A new database standing for one copy of a shared file: KeePass matches groups by
        /// identifier, so every copy carries the same root group.
        /// </summary>
        private TemporaryDatabase Copy()
        {
            var database = TemporaryDatabase.New();
            database.Database.RootGroup.Uuid = SharedRoot;
            database.Database.SettingsChanged = Earlier;
            _databases.Add(database);
            return database;
        }

        private static void StripTimes(TemporaryDatabase database)
        {
            foreach (var pair in database.Database.CustomData.ToList())
            {
                Write(database, pair.Key, pair.Value, null);
            }
        }

        /// <summary>
        /// Writes a key with a chosen time, or with none, through the internal overload: the
        /// public `Set` always stamps the current time.
        /// </summary>
        private static void Write(TemporaryDatabase database, string key, string value, DateTime? time)
        {
            var set = typeof(StringDictionaryEx).GetMethod(
                "Set", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
            Assume.That(set, Is.Not.Null, "KeePass no longer lets a key be written with a chosen time");
            set.Invoke(database.Database.CustomData, new object[] { key, value, time });
        }

        private static DateTime? Time(TemporaryDatabase database, string key)
        {
            var read = typeof(StringDictionaryEx).GetMethod(
                "GetLastModificationTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(read, Is.Not.Null, "KeePass no longer says when a key was written");
            return (DateTime?)read.Invoke(database.Database.CustomData, new object[] { key });
        }
    }
}
