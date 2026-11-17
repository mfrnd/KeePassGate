using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Versions and candidates for the store's reading, made of plain strings, with every read of
    /// a value counted.
    /// </summary>
    /// <remarks>
    /// A version holds its values as the objects it is given, so a test that hands the same
    /// string objects to two readings hands them the same version, and a test that hands another
    /// string of the same text hands them a version changed, as KeePass replaces a string's
    /// object when its text changes.
    /// </remarks>
    internal sealed class StoreVersions : IStoreStrings
    {
        /// <summary>How many values were read whole.</summary>
        public int Reads { get; private set; }

        /// <summary>How many values were asked their length.</summary>
        public int Lengths { get; private set; }

        public int Length(object value)
        {
            Lengths++;
            return ((string)value).Length;
        }

        public string Read(object value)
        {
            Reads++;
            return (string)value;
        }

        public StoreVersion Version(IReadOnlyDictionary<string, string> strings)
        {
            var names = strings.Keys.ToList();
            return new StoreVersion(names, names.Select(name => (object)strings[name]).ToList(), this);
        }

        /// <summary>An entry under an identifier, its versions the current first.</summary>
        public StoreCandidate Candidate(byte[] identifier, params IReadOnlyDictionary<string, string>[] versions)
        {
            return new StoreCandidate(identifier, versions.Select(Version).ToList());
        }

        /// <summary>A composed record, under the identifier its content gives.</summary>
        public StoreCandidate Candidate(StoreComposer.Composed record, params IReadOnlyDictionary<string, string>[] history)
        {
            return new StoreCandidate(
                record.Identifier,
                new[] { record.Strings }.Concat(history).Select(Version).ToList());
        }

        /// <summary>Whether a version's check passes under a key, whatever entry holds it.</summary>
        public static bool Passes(byte[] key, IReadOnlyDictionary<string, string> strings)
        {
            return StoreChecked.Read(new StoreVersions().Version(strings)).Checks(new FoundKey((byte[])key.Clone()), null);
        }

        /// <summary>
        /// The store key a version gives as the version of the entry under this identifier, or
        /// null, `STORE.md` §621.
        /// </summary>
        public static byte[] KeyOf(byte[] identifier, IReadOnlyDictionary<string, string> strings)
        {
            var form = StoreChecked.Read(new StoreVersions().Version(strings));
            return form.StoreKey != null && form.Passes(form.StoreKey, identifier) ? (byte[])form.StoreKey.Bytes.Clone() : null;
        }

        /// <summary>The checked form of a version, for what the reading takes of it.</summary>
        public static StoreChecked Checked(IReadOnlyDictionary<string, string> strings)
        {
            return StoreChecked.Read(new StoreVersions().Version(strings));
        }

        public static IReadOnlyDictionary<string, string> With(
            IReadOnlyDictionary<string, string> strings, string name, string value)
        {
            var changed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in strings)
            {
                changed[pair.Key] = pair.Value;
            }

            if (value == null)
            {
                changed.Remove(name);
            }
            else
            {
                changed[name] = value;
            }

            return changed;
        }
    }
}
