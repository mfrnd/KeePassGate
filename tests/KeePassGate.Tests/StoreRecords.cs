using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The keys and the records the unit tier's store tests make, each through
    /// `tests/Shared/StoreComposer.cs`.
    /// </summary>
    internal static class StoreRecords
    {
        internal static readonly byte[] One = StoreComposer.Key("one");
        internal static readonly byte[] Two = StoreComposer.Key("two");
        internal static readonly byte[] Three = StoreComposer.Key("three");

        internal static StoreComposer.Composed Authority(byte[] key, char id)
        {
            return StoreComposer.Record(
                key, "authority", "authority:" + Hex(id),
                StoreComposer.Content(("id", Hex(id)), ("storeKey", StoreComposer.HexOf(key))));
        }

        internal static StoreComposer.Composed Head(byte[] key, int height, string after, params string[] items)
        {
            return StoreComposer.Record(
                key, "head", "store",
                StoreComposer.Content(("after", after), ("height", height.ToString(CultureInfo.InvariantCulture))),
                height,
                new[] { FormatItem(StoreFormat.Number) }.Concat(items));
        }

        internal static StoreComposer.Composed Format(byte[] key, int number)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            return StoreComposer.Record(
                key, "format", "format:" + text, StoreComposer.Content(("format", text), ("plugin", "0.3.0")), number);
        }

        internal static StoreComposer.Composed Version(byte[] key, string objectName, string after)
        {
            return StoreComposer.Version(
                key, objectName, StoreComposer.Content(("after", after), ("doc", "a rule"), ("object", objectName)));
        }

        internal static string FormatItem(int number)
        {
            return StoreComposer.Item(("format", number.ToString(CultureInfo.InvariantCulture)), ("kind", "format"), ("plugin", "0.3.0"));
        }

        internal static string KeptItem(StoreComposer.Composed kept, byte[] notKept)
        {
            return StoreComposer.Item(("after", string.Empty), ("id", kept.Hex), ("key", StoreComposer.KeyName(notKept)), ("kind", "kept"));
        }

        internal static string VersionItem(StoreComposer.Composed version, string objectName, string after)
        {
            return StoreComposer.Item(
                ("after", after), ("doc", "a rule"), ("id", version.Hex), ("kind", "version"), ("object", objectName));
        }

        internal static string Hex(char digit)
        {
            return new string(digit, 64);
        }

        /// <summary>
        /// A removal of this name that the store knows by its deletion record alone, answering
        /// its identifier: composed under the store key that stands and never put, and named by a
        /// deletion record, as a Synchronize leaves it from a copy where the removal was deleted
        /// for good and every summary entry restating it was lost.
        /// </summary>
        internal static string RemovedApart(MemoryStore store, string name)
        {
            using (var reading = store.Read())
            {
                var removal = StoreRecord.Compose(
                    reading.InForce.Key.Bytes,
                    StoreKinds.Removed,
                    new Dictionary<string, string>(StringComparer.Ordinal) { { StoreKinds.NameMember, name } });
                store.Delete(removal.Identifier);
                return removal.Hex;
            }
        }

        /// <summary>
        /// The members of a record of kind `client` that does not read as an admission, holding a
        /// client key and a certificate and nothing a decision refuses.
        /// </summary>
        internal static System.Collections.Generic.Dictionary<string, string> Unread(char digit)
        {
            return new System.Collections.Generic.Dictionary<string, string>
            {
                { "client", Hex(digit) },
                { "certificate", Hex(digit) },
            };
        }
    }
}
