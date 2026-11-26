using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;

namespace KeePassGate.Testing
{
    /// <summary>
    /// An authority, and what else a database's store holds, put into the store for a test through
    /// the product's own commands: in memory in the unit tier, and in a real database in the host
    /// tier.
    /// </summary>
    /// <remarks>
    /// **Through the commands and not around them**, so that a test seeds exactly what an operator
    /// pressing Create leaves, and nothing a test could compose that the product would not write.
    /// The exceptions are <see cref="Beside"/> and <see cref="Retire"/>, since two authorities
    /// standing under one key, and a retirement arriving by itself, are what a merge of two copies
    /// leaves, which no single command writes; <see cref="AuthorityOf"/> and
    /// <see cref="WrittenDown"/>, for material a test fixed or crafted; and <see cref="Damaged"/>,
    /// which no command writes either.
    /// </remarks>
    internal static class StoreSeeds
    {
        /// <summary>The `id` a damaged authority is filed under unless a test names another.</summary>
        public static readonly string DamagedId = new string('d', 64);

        /// <summary>An authority created by Create, answering its `id`.</summary>
        public static string Authority(IGateStore store)
        {
            var made = new AuthorityService(store, NullGateLog.Instance).Create();
            if (!made.Succeeded)
            {
                throw new InvalidOperationException("An authority could not be seeded: " + made.Message);
            }

            using (var reading = store.Read())
            {
                return StoreAuthorities.Served(reading);
            }
        }

        /// <summary>
        /// A record of kind `client` holding these members, as whoever holds the store key can
        /// write such a record, whether or not it reads as an admission.
        /// </summary>
        public static void Client(IGateStore store, IReadOnlyDictionary<string, string> members)
        {
            Stand(store, "a record of kind client", StoreKinds.Client, key => members);
        }

        /// <summary>
        /// The admissions of the store as a page lists them, from a reading let go at once.
        /// </summary>
        public static AdmissionListing Listed(IGateStore store)
        {
            using (var reading = store.Read())
            {
                return StoreAdmissions.Listing(reading);
            }
        }

        /// <summary>
        /// The same, as it would stand were the authority of this `id` the authority served, which
        /// is what the choice between several authorities lists a candidate's clients from.
        /// </summary>
        public static AdmissionListing Listed(IGateStore store, string served)
        {
            using (var reading = store.Read())
            {
                return StoreAdmissions.Listing(reading, served);
            }
        }

        /// <summary>
        /// The `id` of the authority the store serves, or null where it serves none.
        /// </summary>
        public static string Served(IGateStore store)
        {
            using (var reading = store.Read())
            {
                return StoreAuthorities.Served(reading);
            }
        }

        /// <summary>
        /// An admission written as a pairing or a renewal writes it, through the product's own act
        /// and held to the same ground: its issuer the authority served alone.
        /// </summary>
        public static void Admit(IGateStore store, AdmissionRecord admission)
        {
            Act(store, "an admission", reading => AdmissionActs.Admit(reading, admission));
        }

        /// <summary>
        /// A pairing's admission of this certificate under the authority the store serves, written
        /// as <see cref="Admit(IGateStore, AdmissionRecord)"/> writes it, and answered.
        /// </summary>
        public static AdmissionRecord Admit(
            IGateStore store, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, DateTimeOffset admitted, params string[] profiles)
        {
            var admission = AdmissionRecord.For(certificate, admitted, profiles, Served(store));
            Admit(store, admission);
            return admission;
        }

        /// <summary>
        /// A revocation of this client, as the clients page writes it at the dialog's OK.
        /// </summary>
        public static void Revoke(IGateStore store, string clientKey)
        {
            Act(store, "a revocation", reading => AdmissionActs.Revoke(reading, clientKey));
        }

        /// <summary>
        /// A standing refusal of a client's renewals, as the renewal dialog writes it: naming the
        /// versions of the client's answer the store holds standing as it is read here, which is
        /// what a dialog raised now would show.
        /// </summary>
        public static void Refuse(IGateStore store, string clientKey, RenewalRefusal refusal)
        {
            Act(store, "a refusal of renewals", reading =>
            {
                string refused;
                var shown = StoredRenewals.Of(reading.InForce, clientKey).Standing;
                return RenewalActs.Refuse(reading, clientKey, refusal, shown, out refused);
            });
        }

        /// <summary>
        /// A lift of a client's standing answer, as the clients page writes it at the dialog's OK:
        /// naming every version of the answer the store holds standing and lost as it is read here.
        /// </summary>
        public static void Lift(IGateStore store, string clientKey)
        {
            Act(store, "a lift of renewals", reading =>
            {
                var standing = StoredRenewals.Of(reading.InForce, clientKey);
                return RenewalActs.Lift(reading, clientKey, standing.Standing, standing.Lost);
            });
        }

        /// <summary>A profile name added, as the Profiles page writes it at the dialog's OK.</summary>
        public static void Name(IGateStore store, string name)
        {
            Act(store, "a profile added", reading => KeePassGate.Acl.ProfileActs.Add(reading, name));
        }

        /// <summary>A profile name removed, as the Profiles page writes it at the dialog's OK.</summary>
        public static void Unname(IGateStore store, string name)
        {
            Act(store, "a profile removed", reading => KeePassGate.Acl.ProfileActs.Remove(reading, name));
        }

        /// <summary>
        /// These rules chosen at the node, given as the 32 lowercase hexadecimal characters of its
        /// identifier, in a single OK on its dialog as the product writes it: each profile granted
        /// its mask, written in either form `Rules` takes, over the versions that stand.
        /// </summary>
        public static void Rule(IGateStore store, string node, params (string Profile, string Mask)[] rules)
        {
            Changed(store, "rules chosen", node, shown => new RuleChanges(Chosen(rules, shown), null));
        }

        /// <summary>The box ticked at the node and these rules chosen there, in a single OK on its dialog as the product writes it.</summary>
        public static void Seal(IGateStore store, string node, params (string Profile, string Mask)[] rules)
        {
            Changed(store, "the box ticked", node, shown => new RuleChanges(Chosen(rules, shown), new SwitchChange(true, shown, null, null)));
        }

        /// <summary>
        /// The X on a profile's row at the node, in a single OK on its dialog as the product writes
        /// it, with the mask <paramref name="arriving"/> shown arriving for the profile at the
        /// press.
        /// </summary>
        public static void TakeOff(IGateStore store, string node, string profile, string arriving)
        {
            Changed(store, "a rule taken off", node, shown => new RuleChanges(
                new[] { new RowChange(RowChangeKind.TakeOff, profile, Rules.Letters(arriving), null, shown, null) }, null));
        }

        /// <summary>
        /// The box cleared at the node, in a single OK on its dialog as the product writes it, with
        /// each mask shown arriving for its profile at the press.
        /// </summary>
        public static void Unseal(IGateStore store, string node, params (string Profile, string Mask)[] arriving)
        {
            Changed(store, "the box cleared", node, shown => new RuleChanges(null, new SwitchChange(
                false, shown, arriving.Select(pair => new KeyValuePair<string, AclGrant>(pair.Profile, Rules.Letters(pair.Mask))), null)));
        }

        /// <summary>A rule chosen for each profile, over the rules the tab shows.</summary>
        private static IEnumerable<RowChange> Chosen((string Profile, string Mask)[] rules, NodeRule shown)
        {
            return (rules ?? Array.Empty<(string Profile, string Mask)>())
                .Select(rule => new RowChange(RowChangeKind.Set, rule.Profile, Rules.Letters(rule.Mask), null, shown, null))
                .ToList();
        }

        /// <summary>
        /// The act of an OK on the node's dialog, its changes made over the node's rules as the
        /// store holds them, composed by the product and written by the store's own write; the
        /// node's parent lets through every letter, so each mask a seed gives as arriving is the
        /// mask recorded, and no node below is handed over, so a seed writes at its node alone.
        /// </summary>
        private static void Changed(IGateStore store, string name, string node, Func<NodeRule, RuleChanges> changes)
        {
            StoreAct act;
            IReadOnlyList<RuleLeft> left;
            using (var reading = store.Read())
            {
                if (reading.InForce == null)
                {
                    throw new InvalidOperationException(name + " is seeded only in a store that has a key.");
                }

                var made = changes(StoredRules.Of(reading).At(node));
                act = RuleActs.Of(reading, node, _ => Open.Over(made), null, made, out left);
            }

            if (left.Count > 0 || act == null)
            {
                throw new InvalidOperationException(name + " at " + node + " writes nothing: " +
                    (left.Count > 0 ? "a version does not fit" : "the store holds the rules so already"));
            }

            try
            {
                var written = store.Write(new[] { act });
                if (!written.Taken)
                {
                    throw new InvalidOperationException(name + " could not be seeded: " + written.Reason);
                }
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>
        /// A parent that gives every letter to each profile some changes name: what a seed's
        /// changes are composed under, so that what arrives is what the seed gives as arriving.
        /// </summary>
        private sealed class Open : IAclNode
        {
            private Open(NodeRule rule)
            {
                Rule = rule;
            }

            public NodeRule Rule { get; private set; }

            public IAclNode Parent
            {
                get { return null; }
            }

            public string Name
            {
                get { return "a parent letting every letter through"; }
            }

            /// <summary>The parent for these changes, or null where they name no profile.</summary>
            public static IAclNode Over(RuleChanges changes)
            {
                var profiles = changes.Rows.Select(row => row.Profile)
                    .Concat(changes.Switch == null ? Enumerable.Empty<string>() : changes.Switch.Arriving.Keys)
                    .Distinct(StringComparer.Ordinal)
                    .Select(profile => (profile, VerbMask.Letters))
                    .ToArray();
                return profiles.Length == 0 ? null : new Open(Rules.Node(false, profiles));
            }

            public T Held<T>(Func<T> walk)
            {
                return walk();
            }
        }

        /// <summary>This certificate taken away, as a client giving it up writes it.</summary>
        public static void TakeAway(IGateStore store, string admissionKey)
        {
            Act(store, "a certificate taken away", reading => AdmissionActs.TakeAway(reading, admissionKey));
        }

        /// <summary>The act composed from the store as it stands, written by itself.</summary>
        private static void Act(IGateStore store, string name, Func<StoreReading, StoreAct> compose)
        {
            StoreAct act;
            using (var reading = store.Read())
            {
                act = compose(reading);
            }

            if (act == null)
            {
                throw new InvalidOperationException(name + " is seeded only in a store that has a key.");
            }

            // A buffer that holds the act for a dialog's OK lets it go itself.
            StoreWritten written = null;
            try
            {
                written = store.Write(new[] { act });
                if (!written.Taken)
                {
                    throw new InvalidOperationException(name + " could not be seeded: " + written.Reason);
                }
            }
            finally
            {
                if (written == null || !written.Waits)
                {
                    act.Forget();
                }
            }
        }

        /// <summary>
        /// Another authority standing beside those that stand, under the same store key, as a
        /// merge of two copies that each made an authority leaves them, answering its `id`.
        /// </summary>
        public static string Beside(IGateStore store)
        {
            if (store.Read().InForce == null)
            {
                throw new InvalidOperationException("An authority is seeded beside another only in a store that has a key.");
            }

            var sniKey = new byte[AuthorityMaterial.SniKeyLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(sniKey);
            }

            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();
            using (var material = new AuthorityMaterial(authority, issuer.IssueHost(authority), sniKey))
            {
                return AuthorityOf(store, material);
            }
        }

        /// <summary>
        /// An authority of this material standing beside those that stand, under the store key that
        /// stands or under a new key where none does, answering its `id`: material crafted to be
        /// what no command makes.
        /// </summary>
        public static string AuthorityOf(IGateStore store, AuthorityMaterial material)
        {
            Stand(store, "an authority made in another copy", StoreKinds.Authority, key => AuthorityRecord.Members(material, key));
            return material.AuthorityDigest;
        }

        /// <summary>
        /// An authority of these written down parts, stored as they are written, standing beside
        /// those that stand under the store key that stands or under a new key where none does,
        /// answering its `id`.
        /// </summary>
        /// <remarks>
        /// As they are written, rather than read and saved again, so that a database a picture is
        /// taken of holds the very bytes `SeededAuthority` keeps, whichever program wrote them.
        /// </remarks>
        /// <param name="authority">The authority and its key, as base64 of a PKCS#12.</param>
        /// <param name="host">The host certificate and its key, as base64 of a PKCS#12.</param>
        /// <param name="sniKey">The gate key, as base64 of its bytes.</param>
        public static string WrittenDown(IGateStore store, string authority, string host, string sniKey)
        {
            var id = IdOf(authority);
            Stand(store, "an authority written down", StoreKinds.Authority, key => WrittenDownMembers(id, key, authority, host, sniKey));
            return id;
        }


        /// <summary>
        /// A retirement of this authority holding the store key, as another copy's answer to which
        /// authority to keep brings it once the copies are synchronized.
        /// </summary>
        public static void Retire(IGateStore store, string id)
        {
            if (store.Read().InForce == null)
            {
                throw new InvalidOperationException("An authority is retired only in a store that has a key.");
            }

            Stand(store, "a retirement made in another copy", StoreKinds.Retired, key => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.AuthorityMember, id },
                { StoreKinds.StoreKey, Hex.Of(key) },
            });
        }

        /// <summary>Two authorities standing and none chosen, in the order of their `id`.</summary>
        public static string[] Undecided(IGateStore store)
        {
            return new[] { Authority(store), Beside(store) }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// An authority whose certificates and gate key will not read, standing beside those that
        /// stand, under the store key that stands or under a new key where none does, answering
        /// its `id`.
        /// </summary>
        /// <remarks>
        /// What it stands for is a record that passes its check and holds what no reader takes, as
        /// an edit by hand or another program leaves.
        /// </remarks>
        public static string Damaged(IGateStore store, string id = null)
        {
            var filed = id ?? DamagedId;
            Stand(store, "a damaged authority", StoreKinds.Authority, key => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.Id, filed },
                { StoreKinds.StoreKey, Hex.Of(key) },
                { AuthorityRecord.AuthorityMember, "rubbish" },
                { AuthorityRecord.HostMember, "rubbish" },
                { AuthorityRecord.SniKeyMember, "rubbish" },
            });
            return filed;
        }

        /// <summary>
        /// The certificate and key of an authority standing in the store, by its `id`, for a test
        /// that issues a client of it.
        /// </summary>
        public static CertifiedKey KeyOf(IGateStore store, string id)
        {
            var record = store.Read().Answering.SingleOrDefault(found => found.Form.Member(StoreKinds.Id) == id);
            if (record == null)
            {
                throw new InvalidOperationException("No authority stands under " + id + ".");
            }

            using (var material = StoreAuthorities.Material(record, new List<string>()))
            {
                if (material == null)
                {
                    throw new InvalidOperationException("The authority under " + id + " will not read.");
                }

                return material.AuthorityKey;
            }
        }

        /// <summary>The `id` of an authority written down as base64 of a PKCS#12.</summary>
        private static string IdOf(string authority)
        {
            return CertificateDigest.Of(CertificateBlob.Load(authority).CertificateDer);
        }

        /// <summary>The members of an authority's record holding written down parts as they are written.</summary>
        private static IReadOnlyDictionary<string, string> WrittenDownMembers(
            string id, byte[] key, string authority, string host, string sniKey)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.Id, id },
                { StoreKinds.StoreKey, Hex.Of(key) },
                { AuthorityRecord.AuthorityMember, authority },
                { AuthorityRecord.HostMember, host },
                { AuthorityRecord.SniKeyMember, sniKey },
            };
        }

        /// <summary>A record of this kind with these members, written as <see cref="Write"/> writes.</summary>
        private static void Stand(
            IGateStore store, string name, string kind, Func<byte[], IReadOnlyDictionary<string, string>> members)
        {
            Write(store, name, key => new[] { StoreRecord.Compose(key, kind, members(key)) });
        }

        /// <summary>
        /// Writes these records, composed under the store key that stands or under a new key where
        /// none does, by a single act with no ground.
        /// </summary>
        private static void Write(IGateStore store, string name, Func<byte[], IReadOnlyList<StoreRecord>> records)
        {
            byte[] key;
            string against;
            using (var reading = store.Read())
            {
                key = reading.InForce == null ? StoreFormat.NewKey() : (byte[])reading.InForce.Key.Bytes.Clone();
                against = reading.InForce == null ? null : reading.InForce.Key.Name;
            }

            try
            {
                var written = store.Write(new[] { new StoreAct(name, key, against, records(key), null) });
                if (!written.Taken)
                {
                    throw new InvalidOperationException(name + " could not be seeded: " + written.Reason);
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
