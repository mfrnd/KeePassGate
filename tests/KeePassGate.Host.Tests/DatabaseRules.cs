using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Rules put into the plugin's store of a database KeePass holds for a host fixture, at a group
    /// or an entry, as the product writes them at the OK of its dialog: through `StoreSeeds`, an
    /// authority created first where the store has none, since only an authority starts a store.
    /// </summary>
    internal static class DatabaseRules
    {
        /// <summary>
        /// These rules chosen at the group in a single OK on its dialog, each mask in either form
        /// `Rules` takes; nothing written where the store already holds each as chosen, as an OK
        /// that changes nothing writes nothing.
        /// </summary>
        public static void Rule(PwDatabase database, PwGroup group, params (string Profile, string Mask)[] rules)
        {
            Chosen(Keyed(database), Id(group.Uuid), rules);
        }

        /// <summary>These rules chosen at the entry in a single OK on its dialog, as at a group.</summary>
        public static void Rule(PwDatabase database, PwEntry entry, params (string Profile, string Mask)[] rules)
        {
            Chosen(Keyed(database), Id(entry.Uuid), rules);
        }

        /// <summary>The box ticked at the group and these rules chosen there, in a single OK.</summary>
        public static void Seal(PwDatabase database, PwGroup group, params (string Profile, string Mask)[] rules)
        {
            StoreSeeds.Seal(Keyed(database), Id(group.Uuid), rules);
        }

        /// <summary>The box ticked at the entry and these rules chosen there, in a single OK.</summary>
        public static void Seal(PwDatabase database, PwEntry entry, params (string Profile, string Mask)[] rules)
        {
            StoreSeeds.Seal(Keyed(database), Id(entry.Uuid), rules);
        }

        /// <summary>
        /// A version of a profile's row at a node, holding this text, composed by the tests' own
        /// code and written by a write of the store's own, as whoever holds the store key can write
        /// it: for a node the product writes no rule for, a node of the plugin's store among them.
        /// </summary>
        public static void Crafted(PwDatabase database, string node, string profile, string text)
        {
            var store = Keyed(database);
            byte[] key;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            var version = StoreRecord.Compose(key, StoreKinds.Version, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.After, string.Empty },
                { StoreKinds.Doc, text },
                { StoreKinds.ObjectMember, StoreComposer.RowObject(node, profile) },
            });
            var written = store.Write(new[] { new StoreAct("a rule written by hand", key, StoreFormat.KeyName(key), new[] { version }, null) });
            if (!written.Taken)
            {
                throw new InvalidOperationException("a rule could not be written by hand: " + written.Reason);
            }
        }

        /// <summary>The identifier of a node as a rule names it: 32 lowercase hexadecimal characters.</summary>
        public static string Id(PwUuid uuid)
        {
            return Hex.Of(uuid.UuidBytes);
        }

        /// <summary>These rules chosen at the node, unless each already stands as chosen in a single version.</summary>
        private static void Chosen(IGateStore store, string node, (string Profile, string Mask)[] rules)
        {
            NodeRule held;
            using (var reading = store.Read())
            {
                held = StoredRules.Of(reading).At(node);
            }

            var already = rules.All(rule =>
            {
                var row = held.Row(rule.Profile);
                return row.Answers && row.Versions.Count == 1 && row.Grants.SameAs(Rules.Letters(rule.Mask));
            });

            if (!already)
            {
                StoreSeeds.Rule(store, node, rules);
            }
        }

        /// <summary>The database's store, with an authority created first where it serves none.</summary>
        public static IGateStore Keyed(PwDatabase database)
        {
            var store = DatabaseAuthority.Store(database);
            if (StoreSeeds.Served(store) == null)
            {
                StoreSeeds.Authority(store);
            }

            return store;
        }
    }
}
