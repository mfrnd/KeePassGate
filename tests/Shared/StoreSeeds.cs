using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;

namespace KeePassGate.Testing
{
    /// <summary>
    /// An authority put into a database's store for a test, through the product's own commands:
    /// in memory in the unit tier, and in a real database in the host tier.
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
        /// <param name="admissions">The custom data the database keeps its admissions in.</param>
        public static string Authority(IGateStore store, IKeyValueStore admissions)
        {
            var made = new AuthorityService(store, admissions, NullGateLog.Instance).Create();
            if (!made.Succeeded)
            {
                throw new InvalidOperationException("An authority could not be seeded: " + made.Message);
            }

            return StoreAuthorities.Served(store.Read());
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
        public static string[] Undecided(IGateStore store, IKeyValueStore admissions)
        {
            return new[] { Authority(store, admissions), Beside(store) }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
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
            using (var reading = store.Read())
            {
                key = reading.InForce == null ? StoreFormat.NewKey() : (byte[])reading.InForce.Key.Bytes.Clone();
            }

            try
            {
                var written = store.Write(new[] { new StoreAct(name, key, records(key), null) });
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
