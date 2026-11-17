using System;
using System.Collections.Generic;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Transport;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The content of an authority's record in the plugin's store, `STORE.md` §614 (S3): what it
    /// is composed of, and what it reads as.
    /// </summary>
    /// <remarks>
    /// **Members** `id`, the digest of the authority's certificate; `storeKey`, the store key as
    /// hexadecimal; `ca` and `host`, each a certificate and its key as base64 of a PKCS#12 with no
    /// password; and `sniKey`, the gate key as base64 of its 32 bytes, `CA.md` §222.
    ///
    /// **An authority is whole or damaged, never absent.** An authority that passes its check and does not
    /// read whole stands, gives its key, and is never served: recreating is the remedy, and that is
    /// a decision with consequences only the operator can take. What is wrong is reported in words
    /// the operator can act on.
    /// </remarks>
    internal static class AuthorityRecord
    {
        /// <summary>The member holding the authority certificate and its key.</summary>
        public const string AuthorityMember = "ca";

        /// <summary>The member holding the host certificate and its key.</summary>
        public const string HostMember = "host";

        /// <summary>The member holding the gate key.</summary>
        public const string SniKeyMember = "sniKey";

        /// <summary>What an authority filed under an `id` that is not its own is reported as.</summary>
        internal const string MisfiledProblem = "The authority is filed under a digest that is not its own.";

        /// <summary>What begins a problem that is an exception, before the exception's own message.</summary>
        internal const string CouldNotReadProblem = "The authority could not be read: ";

        /// <summary>The members of an authority's record holding this material under this store key.</summary>
        public static IReadOnlyDictionary<string, string> Members(AuthorityMaterial material, byte[] storeKey)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (storeKey == null)
            {
                throw new ArgumentNullException(nameof(storeKey));
            }

            var sniKey = material.SniKey;
            try
            {
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.Id, material.AuthorityDigest },
                    { StoreKinds.StoreKey, Hex.Of(storeKey) },
                    { AuthorityMember, CertificateBlob.Save(material.AuthorityKey) },
                    { HostMember, CertificateBlob.Save(material.HostKey) },
                    { SniKeyMember, Convert.ToBase64String(sniKey) },
                };
            }
            finally
            {
                Array.Clear(sniKey, 0, sniKey.Length);
            }
        }

        /// <summary>
        /// An authority's record as the material it holds, or null with what was wrong in
        /// <paramref name="problems"/>: damaged.
        /// </summary>
        /// <param name="id">The `id` the record is filed under.</param>
        /// <param name="content">The record's content, read from the version that passed.</param>
        public static AuthorityMaterial Read(string id, string content, List<string> problems)
        {
            if (problems == null)
            {
                throw new ArgumentNullException(nameof(problems));
            }

            var members = content == null ? null : JsonBody.Read(content);
            var authorityPart = members == null ? null : members.Text(AuthorityMember);
            var hostPart = members == null ? null : members.Text(HostMember);
            var sniKeyPart = members == null ? null : members.Text(SniKeyMember);
            if (authorityPart == null || hostPart == null || sniKeyPart == null)
            {
                // A record that passes its check and lacks a part is damage, never absence:
                // absence invites Create, and an authority that stands is never created over.
                problems.Add("The authority's record lacks a part of the authority.");
                return null;
            }

            // codeql[cs/missed-using-statement] disposed only when it is not handed back, which no `using` can do
            AuthorityMaterial material = null;
            byte[] sniKey = null;
            byte[] hostPkcs12 = null;

            try
            {
                var authority = CertificateBlob.Load(authorityPart);
                if (authority == null)
                {
                    problems.Add("The authority certificate cannot be read.");
                }

                // Decoded once, for the host to be read from and for the material to keep a copy
                // of, and overwritten below with the gate key, `CA.md` §208.
                hostPkcs12 = CertificateBlob.Bytes(hostPart);
                var host = CertifiedKey.From(hostPkcs12);
                if (host == null)
                {
                    problems.Add("The host certificate cannot be read.");
                }

                sniKey = ReadSniKey(sniKeyPart, problems);

                // Asked of the authority alone, before anything needs the host as well, so that a
                // misfiled record whose host will not read is reported as both: the misfiling is
                // what points at an edit rather than at a damaged certificate. The `id` is what a
                // retirement names, so an authority filed under another digest is an authority a
                // retirement by its certificate could never reach, and no admission's issuer is
                // held to it.
                if (authority != null && !string.Equals(
                        CertificateDigest.Of(authority.CertificateDer), id, StringComparison.Ordinal))
                {
                    problems.Add(MisfiledProblem);
                }

                if (authority == null || host == null)
                {
                    return null;
                }

                material = new AuthorityMaterial(authority, host, sniKey, hostPkcs12);

                // By the anchor pin and not by name: every authority this plugin makes carries the
                // same subject, so a name compared accepts a host from somebody else's authority.
                if (!AnchorPin.Chains(material.Host, material.Authority))
                {
                    problems.Add("The host certificate was not issued by this authority.");
                }

                if (problems.Count > 0)
                {
                    return null;
                }

                var read = material;
                material = null;
                return read;
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                problems.Add(CouldNotReadProblem + error.Message);
                return null;
            }
            finally
            {
                if (sniKey != null)
                {
                    Array.Clear(sniKey, 0, sniKey.Length);
                }

                if (hostPkcs12 != null)
                {
                    Array.Clear(hostPkcs12, 0, hostPkcs12.Length);
                }

                if (material != null)
                {
                    material.Dispose();
                }
            }
        }

        /// <summary>
        /// The most one authority can be reported with at once, each at its longest, in the order
        /// <see cref="Read"/> reports them, with `thrown` where an exception's message would be.
        /// </summary>
        /// <remarks>
        /// For the KPG tab, which is measured against the longest detail a damaged reading can
        /// produce. Here, beside `Read`, because which problems can arrive together is decided by
        /// the order it asks in: a certificate that will not load stops every check that needs
        /// it, and an exception stops every check after it. So the gate key, at a length ten
        /// digits wide; then the digest the authority is filed under, which needs the authority;
        /// then a problem about the pair, which needs both certificates: the host was not issued by
        /// this authority, or reading the pair threw. The second is the problem given, because the tab
        /// cuts a problem at `Clipped.Limit` and a thrown message long enough to be cut is never
        /// shorter than the sentence it stands in for.
        /// </remarks>
        internal static IReadOnlyList<string> LongestProblems(string thrown)
        {
            return new[] { WrongLengthProblem(int.MaxValue), MisfiledProblem, CouldNotReadProblem + thrown };
        }

        private static byte[] ReadSniKey(string blob, ICollection<string> problems)
        {
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
                problems.Add(WrongLengthProblem(bytes.Length));
                Array.Clear(bytes, 0, bytes.Length);
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            return bytes;
        }

        /// <summary>What a gate key of the wrong length is reported as.</summary>
        private static string WrongLengthProblem(int length)
        {
            return "The gate key is " + length + " bytes rather than " + AuthorityMaterial.SniKeyLength + ".";
        }
    }
}
