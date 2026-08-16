using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Reads and writes the authority in a database's custom data, under the `KPG.` keys that
    /// `CA.md` §204 lists.
    /// </summary>
    /// <remarks>
    /// Everything here fails closed. A value that will not decode makes the authority damaged
    /// rather than absent, and damage is never quietly repaired: the operator is told, and the
    /// remedy is to recreate, which is a decision with consequences that only they can take.
    /// </remarks>
    public sealed class AuthorityStore
    {
        private readonly IKeyValueStore _store;

        public AuthorityStore(IKeyValueStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
        }

        /// <summary>
        /// What the database holds. The caller owns the result and disposes it, which disposes the
        /// certificates it carries.
        /// </summary>
        public AuthorityReading Read()
        {
            var records = CountClientRecords();

            var stored = _store.Get(AuthorityKeys.Authority);
            if (string.IsNullOrEmpty(stored))
            {
                return AuthorityReading.Absent(records);
            }

            var problems = new List<string>();
            var document = AuthorityDocument.Read(stored, problems);
            if (document == null)
            {
                // Something is under the key and it is not an authority this plugin can use. That
                // is damage, never absence: absence invites Create, and creating over a document
                // somebody else's plugin version wrote would destroy it.
                return AuthorityReading.Damaged(problems, records);
            }

            var authorityBlob = document.Authority;
            var hostBlob = document.Host;
            var sniBlob = document.SniKey;

            X509Certificate2 authority = null;
            X509Certificate2 host = null;

            try
            {
                if (!string.IsNullOrEmpty(authorityBlob))
                {
                    authority = CertificateBlob.Load(authorityBlob);
                    if (authority == null)
                    {
                        problems.Add("The authority certificate cannot be read.");
                    }
                }

                if (!string.IsNullOrEmpty(hostBlob))
                {
                    host = CertificateBlob.Load(hostBlob);
                    if (host == null)
                    {
                        problems.Add("The host certificate cannot be read.");
                    }
                }

                var sniKey = ReadSniKey(sniBlob, problems);

                if (authority != null && host != null)
                {
                    bool evaluated;
                    if (!IssuedBy(host, authority, out evaluated))
                    {
                        // Two different faults, and only one of them is the database's. A chain the
                        // engine could not evaluate says nothing about the authority, and reporting
                        // it as a foreign certificate points the operator at Recreate, which would
                        // throw away a healthy authority and every admission record with it.
                        problems.Add(evaluated
                            ? "The host certificate was not issued by this authority."
                            : "The host certificate could not be checked against this authority on " +
                              "this machine. Nothing here says the authority is wrong; recreating it " +
                              "would not help.");
                    }
                }

                if (problems.Count > 0)
                {
                    return AuthorityReading.Damaged(problems, records);
                }

                var material = new AuthorityMaterial(authority, host, sniKey, CertificateBlob.Bytes(hostBlob));
                Array.Clear(sniKey, 0, sniKey.Length);
                authority = null;
                host = null;
                return AuthorityReading.Whole(material, records);
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                problems.Add("The authority could not be read: " + error.Message);
                return AuthorityReading.Damaged(problems, records);
            }
            finally
            {
                if (authority != null)
                {
                    authority.Reset();
                }

                if (host != null)
                {
                    host.Reset();
                }
            }
        }

        /// <summary>
        /// Writes the authority, all three parts in the one value that keeps them together.
        /// </summary>
        /// <remarks>
        /// One write rather than three, which retires the ordering the three keys needed: there is
        /// no moment at which a reader can see a part of this, because a dictionary either has the
        /// key or does not.
        /// </remarks>
        public void Write(AuthorityMaterial material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            var sniKey = material.SniKey;
            try
            {
                _store.Set(
                    AuthorityKeys.Authority,
                    AuthorityDocument.Save(
                        CertificateBlob.Save(material.Authority),
                        CertificateBlob.Save(material.Host),
                        Convert.ToBase64String(sniKey)));
            }
            finally
            {
                Array.Clear(sniKey, 0, sniKey.Length);
            }
        }

        /// <summary>
        /// Removes the authority, and with it every admission record, because a record is
        /// meaningless once the authority that issued its certificate is gone.
        /// </summary>
        /// <returns>How many admission records were removed.</returns>
        public int DeleteEverything()
        {
            _store.Remove(AuthorityKeys.Authority);

            var records = ClientRecordKeys();
            foreach (var key in records)
            {
                _store.Remove(key);
            }

            return records.Count;
        }

        public int CountClientRecords()
        {
            return ClientRecordKeys().Count;
        }

        /// <summary>
        /// Whether the two certificates belong together, by the anchor pin rather than by name.
        /// Every authority this plugin issues carries the same subject, so a name comparison
        /// accepts a host from somebody else's authority: found by a test that was written to
        /// prove the opposite.
        ///
        /// Expiry is ignored here on purpose. An expired pair is still a pair, and expiry is
        /// reported on its own terms rather than as "this does not belong to you".
        /// </summary>
        private static bool IssuedBy(X509Certificate2 leaf, X509Certificate2 authority, out bool evaluated)
        {
            return AnchorPin.Chains(leaf, authority, true, out evaluated);
        }

        private static byte[] ReadSniKey(string blob, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(blob))
            {
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blob);
            }
            catch (FormatException)
            {
                problems.Add("The gate key cannot be read.");
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            if (bytes.Length != AuthorityMaterial.SniKeyLength)
            {
                problems.Add("The gate key is " + bytes.Length + " bytes rather than " +
                             AuthorityMaterial.SniKeyLength + ".");
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            return bytes;
        }

        private List<string> ClientRecordKeys()
        {
            var keys = _store.Keys;
            if (keys == null)
            {
                return new List<string>();
            }

            return keys.Where(AuthorityKeys.IsClientRecord).ToList();
        }
    }
}
