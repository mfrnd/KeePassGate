using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Does this certificate chain to exactly this authority, by its bytes.
    /// </summary>
    /// <remarks>
    /// Measured, and the reason this exists at all: the natural code accepts a certificate from a
    /// completely foreign authority. `ExtraStore` supplies candidates without constraining the
    /// anchor, and `AllowUnknownCertificateAuthority` tolerates an untrusted root without saying
    /// which one, so the build succeeds against the machine's own root stores.
    /// `X509ChainTrustMode.CustomRootTrust` would fix it and is .NET 5 and later, so the anchor is
    /// pinned here by comparing the last element's DER.
    ///
    /// Comparing names would not do either: every authority this plugin issues carries the same
    /// subject, so a foreign one matches by name and differs by every byte that matters.
    /// </remarks>
    public static class AnchorPin
    {
        /// <summary>
        /// A chain of exactly two: the leaf, and the authority that signed it. `CA.md` §207
        /// requires `pathLen 0` on the authority so it can never mint a subordinate, which means
        /// any longer chain is either not ours or not one we would accept.
        /// </summary>
        private const int LeafAndAnchor = 2;

        /// <summary>
        /// The shortest time the chain engine may spend fetching, which is as close to forbidding
        /// it as .NET Framework allows.
        /// </summary>
        /// <remarks>
        /// Measured, and this too would have shipped. When the engine cannot find the issuer it
        /// reads the leaf's authority information access extension and downloads what the leaf
        /// says signed it, RFC 5280 section 4.2.2.1, before `Build` returns. The leaf here is the
        /// peer's, so the address is the peer's: a stranger who reaches this code makes the
        /// KeePass process fetch a URL of their choosing and holds the connection thread until the
        /// engine gives up, measured at 15.0 s for one unreachable address. `CA.md` §217 rejects
        /// CRL and OCSP as infrastructure a local plugin has no business needing, and this is the
        /// same network arriving through a door nobody opened.
        ///
        /// Nothing worth fetching exists. The hierarchy is a leaf and the authority that signed
        /// it, `CA.md` §207, and that authority is handed in through `ExtraStore` a line above, so
        /// a download can only ever be the engine looking for an issuer this pin is about to
        /// refuse anyway.
        ///
        /// `X509ChainPolicy.DisableCertificateDownloads` says exactly that and is .NET 5 and
        /// later, as `X509ChainTrustMode.CustomRootTrust` is above. What this framework offers is
        /// the timeout, where zero is the default the policy already carries and means the
        /// platform's fifteen seconds, so the smallest positive value is the whole of the
        /// vocabulary. Measured: the same leaf is refused in 79 ms rather than 15.0 s, and this
        /// plugin's own client still chains, because it never needed the network.
        ///
        /// What that leaves is a millisecond, and saying so is more use than claiming the door is
        /// shut. The engine may still open the request before abandoning it, and against a
        /// loopback address the connection sometimes lands, which the test reports rather than
        /// asserts. Against anything further away a millisecond buys a packet and no answer. The
        /// wait and the completed download are what the bound takes away, and they are what the
        /// stall was made of.
        /// </remarks>
        private static readonly TimeSpan NoTimeToFetch = TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// True when the leaf chains to the anchor and to nothing else.
        /// </summary>
        /// <param name="ignoreExpiry">
        /// True when only the relationship is being asked about, as when reporting whether a
        /// stored pair belongs together. The connection path passes false and checks the dates
        /// itself.
        /// </param>
        public static bool Chains(X509Certificate2 leaf, X509Certificate2 anchor, bool ignoreExpiry)
        {
            bool evaluated;
            return Chains(leaf, anchor, ignoreExpiry, out evaluated);
        }

        /// <param name="evaluated">
        /// False only when the engine threw: a locked down machine store, a CryptoAPI service
        /// that is not running, a provider that will not load. That is not the same as a
        /// certificate from somewhere else, and the caller must not report it as one, because
        /// the remedy offered for a foreign authority destroys a healthy one.
        ///
        /// A build that returns false is left as `true`: a host signed by another authority
        /// produces a partial chain and fails exactly that way, so it is an answer.
        /// </param>
        public static bool Chains(
            X509Certificate2 leaf, X509Certificate2 anchor, bool ignoreExpiry, out bool evaluated)
        {
            // The question was answered unless one of the paths below says otherwise. A wrong
            // number of elements and a root that does not match are both answers.
            evaluated = true;

            if (leaf == null || anchor == null)
            {
                return false;
            }

            try
            {
                using (var chain = new X509Chain())
                using (new ElementsOf(chain))
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    if (ignoreExpiry)
                    {
                        chain.ChainPolicy.VerificationFlags |= X509VerificationFlags.IgnoreNotTimeValid;
                    }

                    chain.ChainPolicy.ExtraStore.Add(anchor);
                    chain.ChainPolicy.UrlRetrievalTimeout = NoTimeToFetch;

                    // A build that fails is an answer, not a refusal to answer: a host signed
                    // by another authority gives a partial chain and lands here, which is the
                    // ordinary way of being foreign.
                    if (!chain.Build(leaf))
                    {
                        return false;
                    }

                    // This hierarchy has no intermediates, so anything longer is not ours.
                    if (chain.ChainElements.Count != LeafAndAnchor)
                    {
                        return false;
                    }

                    var root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                    return root.RawData.SequenceEqual(anchor.RawData);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                evaluated = false;
                return false;
            }
        }

        /// <summary>
        /// Releases the certificates a chain build made for its elements, when it is disposed.
        /// </summary>
        /// <remarks>
        /// Measured on .NET Framework: disposing an `X509Chain` releases the chain and leaves the
        /// certificate of every element holding a duplicated context until the collector gets to
        /// it, and the first element's is a second reference to the leaf's own context. Left that
        /// way, every chain built here keeps a copy of the leaf and of the authority alive until
        /// the next collection, however promptly the caller releases the leaf it passed in.
        /// Disposed before the chain, which is what the second `using` above gives.
        /// </remarks>
        private sealed class ElementsOf : IDisposable
        {
            private readonly X509Chain _chain;

            public ElementsOf(X509Chain chain)
            {
                _chain = chain;
            }

            public void Dispose()
            {
                foreach (var element in _chain.ChainElements)
                {
                    element.Certificate.Reset();
                }
            }
        }
    }
}
