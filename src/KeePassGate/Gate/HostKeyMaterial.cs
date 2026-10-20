using System;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using KeePassGate.Diagnostics;

namespace KeePassGate.Gate
{
    /// <summary>
    /// A host certificate and its key, in a form the TLS server can use without Windows being
    /// involved at any point.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the transport terminates TLS in managed code. SChannel will not
    /// serve with a key that has not been written to the user's profile, which contradicts
    /// `CA.md` §202. Bouncy Castle parses the PKCS#12 itself, so the key is an object on the heap
    /// and nothing else.
    ///
    /// Nothing here is disposable, because there is nothing to release: no handle, no container, no
    /// file. The bytes go when the object does.
    ///
    /// That is also why a lock lets this go rather than erasing it. An `AsymmetricKeyParameter` is
    /// an immutable `BigInteger` and offers nothing to overwrite, so the last reference going is
    /// the whole of what happens to it. `CA.md` §208 states the bound that leaves, and it is the
    /// same one conceded for local code execution as the user.
    /// </remarks>
    public sealed class HostKeyMaterial
    {
        private readonly byte[] _certificateDer;
        private readonly AsymmetricKeyParameter _privateKey;

        private HostKeyMaterial(byte[] certificateDer, AsymmetricKeyParameter privateKey)
        {
            _certificateDer = certificateDer;
            _privateKey = privateKey;
        }

        /// <summary>The certificate as it goes on the wire.</summary>
        public byte[] CertificateDer
        {
            get { return _certificateDer; }
        }

        public AsymmetricKeyParameter PrivateKey
        {
            get { return _privateKey; }
        }

        /// <summary>
        /// Reads a PKCS#12 of the shape this plugin stores: one key entry, no password. Returns
        /// null for anything else, because a host certificate that cannot be read is a database
        /// that cannot be served, which is reported rather than thrown.
        /// </summary>
        public static HostKeyMaterial From(byte[] pkcs12)
        {
            if (pkcs12 == null || pkcs12.Length == 0)
            {
                return null;
            }

            try
            {
                var store = new Pkcs12StoreBuilder().Build();
                using (var bytes = new MemoryStream(pkcs12, false))
                {
                    store.Load(bytes, Array.Empty<char>());
                }

                foreach (string alias in store.Aliases)
                {
                    if (!store.IsKeyEntry(alias))
                    {
                        continue;
                    }

                    var key = store.GetKey(alias);
                    var chain = store.GetCertificateChain(alias);
                    if (key == null || key.Key == null || !key.Key.IsPrivate || chain == null || chain.Length == 0)
                    {
                        return null;
                    }

                    return new HostKeyMaterial(chain[0].Certificate.GetEncoded(), key.Key);
                }

                return null;
            }
            catch (Exception error) when (Expected.OfCryptography(error)
                                          || error is IOException
                                          || error is InvalidOperationException
                                          || error is CryptoException
                                          || error is SecurityUtilityException)
            {
                // A PKCS#12 that will not parse is a database that cannot be served, and the tab
                // is where an operator is told about it.
                //
                // **Bouncy Castle's own types, and every one of them was measured.** `IOException`
                // is not a mistake: Bouncy Castle reports malformed key material as an IO failure
                // with the ASN.1 complaint inside it, so bytes that are not a PKCS#12 arrive as
                // "BOOLEAN value should have 1 byte in it" wrapped in an `IOException` rather than
                // as a `CryptographicException`. The other three escaped this filter until they
                // were measured, and two of them come from files Windows accepts, which makes them
                // reachable from a database somebody crafted: beside the host's own key, a second
                // key bag whose encryption will not undo is an `InvalidCipherTextException`, a
                // `CryptoException`, and one naming a negative iteration count is an
                // `InvalidOperationException`, as is an ASN.1 length that runs out. An algorithm
                // Bouncy Castle does not know is a `SecurityUtilityException`. Each of them broke
                // the promise this makes, that anything it cannot read comes back as null, and
                // traveled up to whoever had asked for the host instead.
                return null;
            }
        }
    }
}
