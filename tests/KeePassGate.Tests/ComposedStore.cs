using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A store under a key: an authority and a summary entry stating a format, to which a
    /// test adds, drops, edits and deletes records.
    /// </summary>
    internal sealed class ComposedStore
    {
        private readonly byte[] _key;
        private readonly int _format;
        private readonly List<KeyValuePair<byte[], List<IReadOnlyDictionary<string, string>>>> _entries =
            new List<KeyValuePair<byte[], List<IReadOnlyDictionary<string, string>>>>();

        public ComposedStore(byte[] key, int format = StoreFormat.Number)
        {
            _key = key;
            _format = format;
            Authority = Add(StoreRecords.Authority(key, 'a'));
            Head = Add(HeadOf(1, string.Empty));
        }

        public StoreComposer.Composed Authority { get; }

        public StoreComposer.Composed Head { get; private set; }

        public List<byte[]> Deleted { get; } = new List<byte[]>();

        /// <summary>
        /// The identifier of every group and entry of the tree the store is read beside: what a
        /// reading asks for where a deletion record is left that no object the file names explains.
        /// </summary>
        public List<byte[]> Tree { get; } = new List<byte[]>();

        /// <summary>How many times a reading asked for <see cref="Tree"/>.</summary>
        public int Walks { get; private set; }

        public StoreComposer.Composed Add(StoreComposer.Composed record)
        {
            _entries.Add(new KeyValuePair<byte[], List<IReadOnlyDictionary<string, string>>>(
                record.Identifier, new List<IReadOnlyDictionary<string, string>> { record.Strings }));
            return record;
        }

        public void Drop(StoreComposer.Composed record)
        {
            _entries.RemoveAll(entry => entry.Key.SequenceEqual(record.Identifier));
        }

        public void Delete(StoreComposer.Composed record)
        {
            Deleted.Add(record.Identifier);
        }

        /// <summary>Edits of the content, each putting the version before it into the history.</summary>
        public void Edit(StoreComposer.Composed record, params string[] contents)
        {
            var versions = _entries.Single(entry => entry.Key.SequenceEqual(record.Identifier)).Value;
            foreach (var content in contents)
            {
                versions.Insert(0, StoreVersions.With(versions[0], StoreComposer.Data, content));
            }
        }

        /// <summary>An edit by a program that keeps no history.</summary>
        public void Replace(StoreComposer.Composed record, string content)
        {
            var versions = _entries.Single(entry => entry.Key.SequenceEqual(record.Identifier)).Value;
            versions[0] = StoreVersions.With(versions[0], StoreComposer.Data, content);
        }

        /// <summary>The record under an identifier that differs in a single byte.</summary>
        public void Move(StoreComposer.Composed record, int at)
        {
            var entry = _entries.Single(pair => pair.Key.SequenceEqual(record.Identifier));
            var moved = (byte[])record.Identifier.Clone();
            moved[at] ^= 0x01;
            _entries.Remove(entry);
            _entries.Add(new KeyValuePair<byte[], List<IReadOnlyDictionary<string, string>>>(moved, entry.Value));
        }

        public StoreComposer.Composed Revoked(char client)
        {
            var revoked = Add(StoreComposer.Record(_key, "revoked", "client:" + Hex(client), StoreComposer.Content(("client", Hex(client)))));
            Restate(StoreComposer.Item(("client", Hex(client)), ("kind", "revoked")));
            return revoked;
        }

        /// <summary>A keep record under one key naming another, restated where the test asks.</summary>
        public StoreComposer.Composed Kept(byte[] under, byte[] notKept)
        {
            return Add(StoreComposer.Record(
                under, "kept", "key:" + StoreComposer.KeyName(notKept),
                StoreComposer.Content(("after", string.Empty), ("key", StoreComposer.KeyName(notKept)))));
        }

        /// <summary>A record of a kind that has to be restated, and the item that restates it.</summary>
        public string Decision(string kind)
        {
            switch (kind)
            {
                case "revoked":
                    Add(StoreComposer.Record(_key, kind, "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
                    return StoreComposer.Item(("client", Hex('c')), ("kind", kind));
                case "retired":
                    Add(StoreComposer.Record(_key, kind, "authority:" + Hex('9'), StoreComposer.Content(("authority", Hex('9')))));
                    return StoreComposer.Item(("authority", Hex('9')), ("kind", kind));
                case "removed":
                    var removed = Add(StoreComposer.Record(_key, kind, "profile:reader", StoreComposer.Content(("name", "reader"))));
                    return StoreComposer.Item(("id", removed.Hex), ("kind", kind), ("name", "reader"));
                case "withdrawn":
                    var withdrawn = Add(StoreComposer.Record(
                        _key, kind, "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", string.Empty))));
                    return StoreComposer.Item(("certificate", Hex('d')), ("id", withdrawn.Hex), ("kind", kind), ("renewed", string.Empty));
                case "kept":
                    var kept = Kept(_key, Three);
                    return KeptItem(kept, Three);
                case "format":
                    Add(Format(_key, StoreFormat.Number));
                    Drop(Head);
                    Head = null;
                    return null;
                case "version":
                    var version = Add(Version(_key, "rule:" + new string('1', 32), string.Empty));
                    return VersionItem(version, "rule:" + new string('1', 32), string.Empty);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "a kind this store does not make");
            }
        }

        /// <summary>
        /// A summary entry over the items given, replacing the one in force by naming it, as a
        /// write of the plugin's own does.
        /// </summary>
        public void Restate(string item = null, string after = null)
        {
            RestateAll(item == null ? Array.Empty<string>() : new[] { item }, after);
        }

        /// <summary>The same, over several items.</summary>
        public void RestateAll(IEnumerable<string> restating, string after = null)
        {
            var items = new List<string>(restating);
            var names = new List<string>();
            if (Head != null)
            {
                names.Add(Head.Hex);
                Drop(Head);
                Delete(Head);
            }

            if (after != null)
            {
                names.Add(after);
            }

            var height = 2;
            Head = Add(HeadOf(height, string.Join(" ", names), items.ToArray()));
        }

        public StoreReading Read()
        {
            var versions = new StoreVersions();
            var candidates = _entries
                .Select(entry => versions.Candidate(entry.Key, entry.Value.ToArray()))
                .ToList();
            return StoreReading.Of(candidates, Deleted, null, new StoreKept(), Walked);
        }

        /// <summary>The tree, counted as asked for.</summary>
        private IEnumerable<byte[]> Walked()
        {
            Walks++;
            return Tree.ToList();
        }

        private StoreComposer.Composed HeadOf(int height, string after, params string[] items)
        {
            return StoreComposer.Record(
                _key, "head", "store",
                StoreComposer.Content(("after", after), ("height", height.ToString(CultureInfo.InvariantCulture))),
                height,
                new[] { FormatItem(_format) }.Concat(items));
        }
    }
}
