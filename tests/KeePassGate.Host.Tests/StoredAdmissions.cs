using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Gate;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The admissions of a real database as a test arranges and reads them: written into its
    /// store by the product's own acts and read as the gate reads them, the entries of the
    /// plugin's group as KeePass holds them, and the standing answers to its clients' renewals.
    /// </summary>
    /// <remarks>
    /// An admission is written with the authority the store serves as its issuer where the store
    /// serves an authority, as a pairing writes it, and as a key holder can write it where the
    /// store serves none.
    /// </remarks>
    internal sealed class StoredAdmissions
    {
        /// <summary>
        /// The `id` of an authority nobody holds, for an admission a test composes, which
        /// <see cref="Write"/> writes under the authority served where the store serves an
        /// authority.
        /// </summary>
        public static readonly string AnyIssuer = new string('a', 64);

        private readonly PwDatabase _database;

        public StoredAdmissions(PwDatabase database)
        {
            _database = database;
        }

        /// <summary>The database's store, read and written in place.</summary>
        public KdbxStore Store
        {
            get { return new KdbxStore(_database, GateVersion.Current, null); }
        }

        /// <summary>
        /// A standing refusal of a client's renewals, as the renewal dialog writes it, naming the
        /// versions of the client's answer that stand as it is read here.
        /// </summary>
        public void Refuse(string clientKey, RenewalRefusal refusal)
        {
            StoreSeeds.Refuse(Store, clientKey, refusal);
        }

        /// <summary>A lift of every version of a client's answer, as Allow renewal writes it at OK.</summary>
        public void Lift(string clientKey)
        {
            StoreSeeds.Lift(Store, clientKey);
        }

        /// <summary>The strictest stored answer about a client's renewals, as the gate reads it, or null.</summary>
        public RenewalRefusal Answer(string clientKey)
        {
            using (var reading = Store.Read())
            {
                return StoredRenewals.AnswerOf(reading.InForce, clientKey);
            }
        }

        /// <summary>What a reading holds of a client's answer, as the gate and the clients page read it.</summary>
        internal RenewalStanding Standing(string clientKey)
        {
            using (var reading = Store.Read())
            {
                return StoredRenewals.Of(reading.InForce, clientKey);
            }
        }

        /// <summary>The admission, under the authority served where the store serves any.</summary>
        public void Write(AdmissionRecord record)
        {
            var served = StoreSeeds.Served(Store);
            if (served == null)
            {
                StoreSeeds.Client(Store, record.Members());
                return;
            }

            var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            members[StoreKinds.Issuer] = served;
            StoreSeeds.Admit(Store, AdmissionRecord.Read(null, members));
        }

        /// <summary>The admission of a certificate as the gate reads it, or null.</summary>
        public AdmissionRecord Find(string admissionKey)
        {
            using (var reading = Store.Read())
            {
                return StoreAdmissions.Admitted(reading, admissionKey);
            }
        }

        /// <summary>
        /// Revokes the client a certificate is admitted for, answering whether it was.
        /// </summary>
        public bool Remove(string admissionKey)
        {
            var admission = Find(admissionKey);
            if (admission == null)
            {
                return false;
            }

            StoreSeeds.Revoke(Store, admission.ClientKey);
            return true;
        }

        /// <summary>The listing a page shows.</summary>
        public AdmissionListing All()
        {
            return StoreSeeds.Listed(Store);
        }

        /// <summary>The entries of the plugin's group of this kind, in force or not.</summary>
        public IReadOnlyList<PwEntry> Entries(string kind)
        {
            var group = Group();
            return group == null
                ? new List<PwEntry>()
                : group.Entries.Where(entry => entry.Strings.ReadSafe("KPG.Kind") == kind).ToList();
        }

        /// <summary>
        /// Whether the tree holds an entry of this kind whose content holds this text.
        /// </summary>
        public bool Holds(string kind, string text)
        {
            return Entries(kind).Any(entry => entry.Strings.ReadSafe("KPG.Data").Contains(text));
        }

        /// <summary>
        /// Whether the tree holds an entry of this kind whose content holds this member with this
        /// value.
        /// </summary>
        public bool Holds(string kind, string member, string value)
        {
            return Entries(kind).Any(entry => string.Equals(
                (string)Newtonsoft.Json.Linq.JObject.Parse(entry.Strings.ReadSafe("KPG.Data"))[member], value, StringComparison.Ordinal));
        }

        /// <summary>
        /// The store as the tree holds it: every entry of the plugin's group with the size of its
        /// history and its strings, and every deletion record, so that a write taken back out can
        /// be compared whole.
        /// </summary>
        public string Snapshot()
        {
            var text = new StringBuilder();
            var group = Group();
            if (group != null)
            {
                foreach (var entry in group.Entries.OrderBy(entry => entry.Uuid.ToHexString(), StringComparer.Ordinal))
                {
                    text.Append(entry.Uuid.ToHexString()).Append(' ').Append(entry.History.UCount).AppendLine();
                    foreach (var pair in entry.Strings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        text.Append("  ").Append(pair.Key).Append('=').Append(pair.Value.ReadString()).AppendLine();
                    }
                }
            }

            foreach (var deleted in _database.DeletedObjects.OrderBy(deleted => deleted.Uuid.ToHexString(), StringComparer.Ordinal))
            {
                text.Append("deleted ").Append(deleted.Uuid.ToHexString()).AppendLine();
            }

            return text.ToString();
        }

        /// <summary>
        /// This admission written, its client revoked, and the admission put back in the tree
        /// beside the revocation with no deletion record naming it, as a merge from a copy that
        /// touched it later leaves it.
        /// </summary>
        public void BackBesideItsRevocation(AdmissionRecord record)
        {
            Write(record);

            var entry = Entries(StoreKinds.Client).Single(found => found.Strings.ReadSafe("KPG.Data").Contains(record.AdmissionKey));
            var group = entry.ParentGroup;
            var copy = entry.CloneDeep();
            if (!Remove(record.AdmissionKey) || Holds(StoreKinds.Client, record.AdmissionKey))
            {
                throw new InvalidOperationException("The revocation did not delete the admission it refuses.");
            }

            group.AddEntry(copy, true);
            var deleted = _database.DeletedObjects.Single(found => found.Uuid.Equals(copy.Uuid));
            _database.DeletedObjects.Remove(deleted);
        }

        /// <summary>
        /// The store brought to hold the gate back with this word, as KeePass's own edits and
        /// merges leave it, `STORE.md` §656: `stopped` by the summary entry deleted for good,
        /// `lost` by every authority's entry, the store key's carrier, deleted for good,
        /// `outdated` by a record of a kind the plugin does not know, and `split` by an authority
        /// under a store key of its own. Or brought to hold several authorities standing, the
        /// authority served among them, which is answered `undecided`: a second beside the first.
        /// </summary>
        public void HoldBack(string word)
        {
            switch (word)
            {
                case StoreWords.Stopped:
                    DeleteForGood(Entries(StoreKinds.Head));
                    break;

                case StoreWords.Lost:
                    DeleteForGood(Entries(StoreKinds.Authority));
                    break;

                case StoreWords.Outdated:
                    Group().AddEntry(StoreEntries.Of(StoreComposer.Record(
                        KeyInForce(), "future", "x", StoreComposer.Content(("about", "x")))), true);
                    break;

                case StoreWords.Split:
                    var other = StoreComposer.Key("another copy");
                    var id = new string('b', 64);
                    Group().AddEntry(StoreEntries.Of(StoreComposer.Record(
                        other,
                        StoreKinds.Authority,
                        "authority:" + id,
                        StoreComposer.Content(("id", id), ("storeKey", StoreComposer.HexOf(other))))), true);
                    break;

                case HeldBackTarget.Undecided:
                    StoreSeeds.Beside(Store);
                    break;

                default:
                    throw new ArgumentException("No store is composed to hold the gate back with " + word + ".", nameof(word));
            }
        }

        /// <summary>
        /// The authority served retired, with two authorities made in other copies standing
        /// beside each other, as copies that each made an authority and a copy that retired the
        /// first leave the store once they are synchronized: several authorities stand, and the
        /// authority that was served is not among them.
        /// </summary>
        public void RetiredBesideOthers()
        {
            var served = StoreSeeds.Served(Store);
            if (served == null)
            {
                throw new InvalidOperationException("Only an authority that is served is retired beside others.");
            }

            StoreSeeds.Beside(Store);
            StoreSeeds.Beside(Store);
            StoreSeeds.Retire(Store, served);
        }

        /// <summary>
        /// The summary entry restated holding this many items, the items it held and revocations
        /// of clients nobody admitted beside them, as a long life of revocations leaves it: named
        /// by the entry that replaces it, and deleted for good, as a write of the plugin's own
        /// does.
        /// </summary>
        public void FillTo(int items)
        {
            var held = Held();
            Restated(Enumerable.Range(held, Math.Max(0, items - held))
                .Select(at => StoreComposer.Item(
                    ("client", at.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)), ("kind", "revoked"))));
        }

        /// <summary>How many items the summary entry holds.</summary>
        private int Held()
        {
            return Entries(StoreKinds.Head).Single().Strings.Count(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal));
        }

        /// <summary>
        /// The summary entry restated holding these items beside the items it held, named by the
        /// entry that replaces it and deleted for good, as a write of the plugin's own does.
        /// </summary>
        public void Restated(IEnumerable<string> items)
        {
            var head = Entries(StoreKinds.Head).Single();
            var held = head.Strings
                .Where(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value.ReadString())
                .ToList();
            var content = Newtonsoft.Json.Linq.JObject.Parse(head.Strings.ReadSafe("KPG.Data"));
            var height = (int)content["height"] + 1;

            // Naming the entry it replaces and every entry that entry named, as a write does.
            var after = ((string)content["after"] ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Concat(new[] { head.Uuid.ToHexString().ToLowerInvariant() })
                .OrderBy(name => name, StringComparer.Ordinal);

            Group().AddEntry(StoreEntries.Of(StoreComposer.Record(
                KeyInForce(),
                StoreKinds.Head,
                "store",
                StoreComposer.Content(
                    ("after", string.Join(" ", after)),
                    ("height", height.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                height,
                held.Concat(items))), true);
            DeleteForGood(new[] { head });
        }

        /// <summary>
        /// The bytes of the store key that stands, which a record composed under it holds.
        /// </summary>
        public byte[] KeyInForce()
        {
            using (var reading = Store.Read())
            {
                return (byte[])reading.InForce.Key.Bytes.Clone();
            }
        }

        /// <summary>
        /// Each entry deleted for good, leaving a deletion record, as KeePass's delete without the
        /// recycle bin does.
        /// </summary>
        private void DeleteForGood(IEnumerable<PwEntry> entries)
        {
            foreach (var entry in entries.ToList())
            {
                entry.ParentGroup.Entries.Remove(entry);
                _database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
            }
        }

        private PwGroup Group()
        {
            return _database.RootGroup.Groups.SingleOrDefault(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
        }
    }
}
