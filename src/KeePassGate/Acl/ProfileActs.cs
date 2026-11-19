using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The acts that add a profile name to a database's store and remove it, `STORE.md` §616 and
    /// §627, each composed under the key of the reading it is composed over.
    /// </summary>
    /// <remarks>
    /// **An act about a name holds what is to be done**, as an act on a client does: whoever holds
    /// it composes it again, `StoreAct.Again`, wherever the KPG tab's buffer is read and at the
    /// settings dialog's OK, under the key that stands then.
    ///
    /// **An addition names the removals of its name that the reading it was pressed over held**,
    /// and is composed again naming those same removals and no others: where the store knows a
    /// removal of the name beyond them, or has the name on the list, nothing is left of it to
    /// write. A removal made apart, which the operator never saw, is not overruled by a press made
    /// before it arrived. **A removal holds the name alone**, and is written where the name is on
    /// the list.
    ///
    /// **A store with no key is given no act**: only an authority starts a store, `STORE.md` §627
    /// step 1, and the caller states what the database lacks.
    /// </remarks>
    internal static class ProfileActs
    {
        /// <summary>What the log calls an addition's act.</summary>
        public const string AddName = "adding a profile";

        /// <summary>What the log calls a removal's act.</summary>
        public const string RemoveName = "removing a profile";

        /// <summary>
        /// An act of a single addition of this name, naming every removal of it that the reading
        /// holds, or null where the reading holds no store key.
        /// </summary>
        public static StoreAct Add(StoreReading reading, string name)
        {
            if (reading == null || reading.InForce == null)
            {
                return null;
            }

            return Adding(reading, name, reading.InForce.RemovalsOf(name).ToList());
        }

        /// <summary>
        /// An act of a single removal of this name, or null where the reading holds no store key.
        /// </summary>
        public static StoreAct Remove(StoreReading reading, string name)
        {
            return Act(reading, RemoveName, key => new[] { Removal(key, name) }, again => ProfileNames.Of(again).Holds(name) ? Remove(again, name) : null);
        }

        /// <summary>The name an act of these is about, or null for any other act.</summary>
        public static string About(StoreAct act)
        {
            if (act == null
                || (!string.Equals(act.Name, AddName, StringComparison.Ordinal) && !string.Equals(act.Name, RemoveName, StringComparison.Ordinal)))
            {
                return null;
            }

            return act.Records[0].Member(StoreKinds.NameMember);
        }

        /// <summary>
        /// Whether this act, composed again over this reading to nothing, is an addition that a
        /// removal of its name beyond those it names left so: the name is off the list, under a
        /// key that stands.
        /// </summary>
        /// <remarks>
        /// Asked only of an act whose composition at the settings dialog's OK came to nothing, and
        /// an addition comes to nothing under a key that stands only where the list holds its name
        /// or such a removal stands, so the list alone tells the two apart. What an operator who
        /// pressed Add is told was not written, and why: the name was removed apart, after the
        /// press, and putting it back is a decision of its own.
        /// </remarks>
        public static bool Overtaken(StoreReading reading, StoreAct act)
        {
            if (reading.InForce == null || !string.Equals(act.Name, AddName, StringComparison.Ordinal))
            {
                return false;
            }

            return !ProfileNames.Of(reading).Holds(About(act));
        }

        /// <summary>
        /// An addition of this name under this key, naming those of these removals that an
        /// addition can name, each once and in ordinal order: `after` is a list of identifiers.
        /// </summary>
        internal static StoreRecord Addition(byte[] key, string name, IEnumerable<string> removals)
        {
            var named = removals
                .Where(IsIdentifier)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(identifier => identifier, StringComparer.Ordinal);
            return StoreRecord.Compose(key, StoreKinds.Profile, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.NameMember, name },
                { StoreKinds.After, string.Join(" ", named) },
            });
        }

        /// <summary>
        /// An addition's act naming these removals, the removals the reading it was pressed over
        /// held, which it is composed again naming.
        /// </summary>
        private static StoreAct Adding(StoreReading reading, string name, IReadOnlyList<string> seen)
        {
            return Act(reading, AddName, key => new[] { Addition(key, name, seen) }, again =>
            {
                var known = new HashSet<string>(seen, StringComparer.Ordinal);
                if (ProfileNames.Of(again).Holds(name) || again.InForce.RemovalsOf(name).Any(removal => !known.Contains(removal)))
                {
                    return null;
                }

                return Adding(again, name, seen);
            });
        }

        private static StoreRecord Removal(byte[] key, string name)
        {
            return StoreRecord.Compose(key, StoreKinds.Removed, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.NameMember, name },
            });
        }

        private static bool IsIdentifier(string text)
        {
            var parsed = StoreFormat.Identifiers(text);
            return text != null && text.Length > 0 && parsed != null && parsed.Count == 1;
        }

        /// <summary>
        /// An act of the records these compose under the key the reading holds, against that key,
        /// with the copy of the key made for it overwritten, or null where no key stands.
        /// </summary>
        private static StoreAct Act(
            StoreReading reading, string name, Func<byte[], IReadOnlyList<StoreRecord>> records, Func<StoreReading, StoreAct> again)
        {
            if (reading == null || reading.InForce == null)
            {
                return null;
            }

            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                return new StoreAct(name, key, reading.InForce.Key.Name, records(key), null, again);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
