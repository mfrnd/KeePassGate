using System;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database whose store holds an authority made by Create, and then holds the gate back by a
    /// word of `STORE.md` §656, or has grown past half its room, §643: what the KPG tab is drawn
    /// over for each.
    /// </summary>
    internal static class HeldBack
    {
        /// <summary>The state of a store past half its room, which is no word.</summary>
        public const string Half = "half";

        /// <summary>A database whose store is in this state, with an authority made by Create.</summary>
        /// <param name="state">`split`, `outdated`, `stopped`, or <see cref="Half"/>.</param>
        public static FakeGateDatabase Database(string state)
        {
            var database = new FakeGateDatabase();
            StoreSeeds.Authority(database.Store, database.Values);

            byte[] key;
            string head;
            using (var reading = database.Store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
                head = reading.InForce.Heads.Single().Hex;
            }

            switch (state)
            {
                case StoreWords.Split:
                    // An authority under a key of its own, as a copy that made a store of its own
                    // leaves after a synchronize.
                    database.Records.Put(StoreRecords.Authority(StoreRecords.Two, 'b'));
                    break;
                case StoreWords.Outdated:
                    // A record of a kind this plugin does not know, as a later plugin writes it.
                    database.Records.Put(StoreComposer.Record(key, "later", "later", StoreComposer.Content(("held", "by a later plugin"))));
                    break;
                case StoreWords.Stopped:
                    // The summary entry gone, as a deletion by hand leaves the store.
                    database.Records.Drop(head);
                    break;
                case Half:
                    // A summary entry with half the items a summary entry has names for.
                    database.Records.Drop(head);
                    database.Records.Put(StoreRecords.Head(
                        key, 1, string.Empty,
                        Enumerable.Range(0, (StoreFormat.MaxItems + 1) / 2)
                            .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                            .ToArray()));
                    break;
                default:
                    throw new ArgumentException("no such state: " + state, nameof(state));
            }

            return database;
        }
    }
}
