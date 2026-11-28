using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The act that seals a stopped store again, `STORE.md` §625: for every object a lost version
    /// makes refuse, a version that grants nothing, each composed by its type's own texts; and,
    /// written first in its write, the deletion of every entry that is damage and a summary entry
    /// over what passes and is in force, which the write adds, `StoreWrite`.
    /// </summary>
    /// <remarks>
    /// **What grants nothing is the type's own text**, so that the seal spells nothing a second
    /// way: an empty rule for a profile's row and the switch refusing for a switch, `RuleText`
    /// and `STORE.md` §617, and `refuse:forever` for an answer to a renewal, `RenewalText` and
    /// §618. An object of no type the plugin writes gets no version, and goes on refusing.
    ///
    /// **What each version names is the store's at the write**, `StoreLosses`: in `after` every
    /// version of its object in force and what those name there, and in `lost` what was lost and
    /// what those name there, no identifier in both. The act holds what is to be done and is
    /// composed again over every later reading, `StoreAct.Again`: a version whose text a merge
    /// brought back is no loss there and gets nothing, a further loss gets its version, and over
    /// a store that is whole the act comes to nothing.
    ///
    /// **A store a start stopped is kept**: for each stop a start wrote under the store key, a keep
    /// record naming the key the stop names, with the stop in `after`, so that the stop counts for
    /// nothing and the key the start made, should it come back, meets this store in a circle and
    /// the store splits until it is started again, `STORE.md` §632: keeping a store of several,
    /// §649, is not built.
    ///
    /// **Nothing for what the union keeps in force.** A revocation, a retirement, a removal and a
    /// certificate taken away that only a deletion record states are in force by `STORE.md` §623
    /// step 2, and the write's summary entry restates each, so no record is written for them: by
    /// its subject, and where the file names its subject nowhere by an item with no subject, the
    /// keyed tag and subject hash of a revocation or a retirement and the identifier of a removal
    /// (§611), which every write after it carries until the subject is named.
    ///
    /// **A version is composed only where its content and its item fit** the plugin's ceiling of a
    /// string, `STORE.md` §627: a version that does not fit is left out, its object goes on
    /// refusing, and the act names it for the log.
    /// </remarks>
    internal static class SealActs
    {
        /// <summary>What the log calls the act.</summary>
        public const string Name = "Seal again";

        /// <summary>
        /// The act sealing the store this reading holds, or null with why in
        /// <paramref name="refused"/> where it is no stopped store the seal applies to: a store in
        /// any other state, and a store with no room, which the seal does not lift.
        /// </summary>
        public static StoreAct Seal(StoreReading reading, out string refused)
        {
            refused = null;
            var losses = StoreLosses.Of(reading);
            if (losses == null)
            {
                refused = reading == null || reading.State != StoreState.Stopped
                    ? "the store is not stopped, so there is nothing to seal"
                    : "the store holds no key to write under";
                return null;
            }

            if (losses.Stops.Contains(StoreStop.NoRoom))
            {
                refused = "the store has no room for the seal's write: " + reading.InForce.Room.Reason;
                return null;
            }

            var inForce = reading.InForce;
            var key = (byte[])inForce.Key.Bytes.Clone();
            try
            {
                var records = new List<StoreRecord>();
                var left = new List<string>();
                foreach (var loss in losses.Lost)
                {
                    var doc = Granting(loss.Object);
                    if (doc == null)
                    {
                        left.Add(loss.Object + ": an object of no type the plugin writes, which goes on refusing");
                        continue;
                    }

                    var members = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.ObjectMember, loss.Object },
                        { StoreKinds.After, string.Join(" ", loss.After) },
                        { StoreKinds.Doc, doc },
                    };
                    if (loss.Settles.Count > 0)
                    {
                        members[StoreKinds.Lost] = string.Join(" ", loss.Settles);
                    }

                    var record = StoreRecord.Compose(key, StoreKinds.Version, members);
                    if (!Fits(record))
                    {
                        left.Add(loss.Object + ": its version names more than a record of the store holds, and the object goes on refusing");
                        continue;
                    }

                    records.Add(record);
                }

                // A stop a start wrote, lifted by keeping this store over the key the start made:
                // a keep record naming that key, with the stop in `after`, which the write's
                // summary entry restates beside the stop, `STORE.md` §632.
                foreach (var stop in losses.SetAside)
                {
                    records.Add(StoreRecord.Compose(key, StoreKinds.Kept, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.Key, stop.Value },
                        { StoreKinds.After, stop.Key },
                    }));
                }

                return new StoreAct(Name, key, inForce.Key.Name, records, null, Again, seals: true, leftOut: left);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// Why the store this reading holds cannot be sealed again, or null where the seal's write
        /// would be taken and lift the stop: the seal composed and planned over the reading as OK
        /// plans it, so that the page offers no press OK could not write, `STORE.md` §625.
        /// </summary>
        public static string Unsealable(StoreReading reading)
        {
            string refused;
            var act = Seal(reading, out refused);
            if (act == null)
            {
                return refused;
            }

            try
            {
                return Unsealed(reading, act);
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>The act composed again over a later reading, or null where nothing is left to seal there.</summary>
        private static StoreAct Again(StoreReading reading)
        {
            string refused;
            return Seal(reading, out refused);
        }

        /// <summary>
        /// Why this seal, composed over this reading, would not lift the stop, or null where its
        /// write would be taken and leave the store whole: planned as OK plans it, and read as
        /// the write leaves the store, `STORE.md` §625.
        /// </summary>
        /// <remarks>
        /// **Its own write can stop the store again**: a seal whose summary entry names as many
        /// as a string holds fits, and leaves no room for the write after it, which stops the store
        /// again, `STORE.md` §643. Asked before the act is held, so that a press refused holds
        /// nothing for OK to write.
        /// </remarks>
        public static string Unsealed(StoreReading reading, StoreAct act)
        {
            string unsealed;
            using (Sealed(reading, act, out unsealed))
            {
                return unsealed;
            }
        }

        /// <summary>
        /// The store as this seal's write would leave it, read in memory, for the caller to let
        /// go; or null with why in <paramref name="unsealed"/> where the write would not be taken
        /// or would not leave the store whole: what the seal's dialog counts as kept, asked before
        /// the dialog opens, so that no dialog is shown over a seal OK could not write.
        /// </summary>
        public static StoreReading Sealed(StoreReading reading, StoreAct act, out string unsealed)
        {
            unsealed = null;
            var write = StoreWrite.Plan(reading, new[] { act }, GateVersion.Current);
            if (write.Refused != null)
            {
                unsealed = write.Refused;
                return null;
            }

            var applied = write.Applied(reading.Candidates, reading.Deletions);
            var sealedStore = StoreReading.Afresh(applied.Key, applied.Value);
            if (sealedStore.State == StoreState.Whole)
            {
                return sealedStore;
            }

            unsealed = "the sealed store would still be " + (StoreWords.Of(sealedStore.State) ?? "without a store key") + ": " + sealedStore.Reason;
            sealedStore.Dispose();
            return null;
        }

        /// <summary>
        /// The text of a version of this object that grants nothing: the empty rule of a row, the
        /// switch refusing, `refuse:forever` for an answer; null for an object of no type the
        /// plugin writes.
        /// </summary>
        internal static string Granting(string objectText)
        {
            string node;
            string profile;
            if (RuleText.ReadObject(objectText, out node, out profile))
            {
                return profile == null ? RuleText.SwitchRefusing : RuleText.Row(AclGrant.Nothing);
            }

            string client;
            return RenewalText.ReadObject(objectText, out client) ? RenewalText.Forever : null;
        }

        /// <summary>
        /// Whether a version's content and the item that restates it each fit the ceiling of a
        /// string: the item is the content with the member `nonce` giving way to `id` and `kind`,
        /// none of whose names and values needs an escape.
        /// </summary>
        private static bool Fits(StoreRecord record)
        {
            if (record.Strings.Values.Any(value => value.Length > StoreFormat.MaxWritten))
            {
                return false;
            }

            var item = record.Content.Length
                       - Written(StoreKinds.Nonce, record.Member(StoreKinds.Nonce))
                       + Written(StoreKinds.Id, record.Hex)
                       + Written(StoreKinds.ItemKind, StoreKinds.Version);
            return item <= StoreFormat.MaxWritten;
        }

        /// <summary>The characters a member needing no escape takes in a flat object, with its comma.</summary>
        private static int Written(string name, string value)
        {
            return name.Length + value.Length + "\"\":\"\",".Length;
        }
    }
}
