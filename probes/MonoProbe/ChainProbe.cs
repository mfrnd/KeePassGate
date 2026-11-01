using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace MonoProbe
{
    /// <summary>
    /// `AnchorPin`'s chain, policy for policy: no revocation, an unknown authority allowed, the
    /// anchor in `ExtraStore`, a millisecond to fetch anything. Asked about our own leaf, a foreign
    /// one, a foreign one whose root is offered as well, one naming a `caIssuers` address, and an
    /// expired one, each answered with what `Build` returned, how many elements it built and
    /// whether the last of them is the anchor's bytes.
    /// </summary>
    internal static class ChainProbe
    {
        internal static void Run()
        {
            var later = DateTime.UtcNow.AddYears(100);
            var ours = BouncyCertificates.Authority("CN=Mono probe ours", later);
            var theirs = BouncyCertificates.Authority("CN=Mono probe theirs", later);
            var anchor = new X509Certificate2(ours.Der);

            Ask("chain.own-leaf", BouncyCertificates.Leaf(ours, "CN=own", true, later), anchor, null, false);
            Ask("chain.foreign-leaf", BouncyCertificates.Leaf(theirs, "CN=foreign", true, later), anchor, null, false);
            Ask("chain.foreign-leaf-root-offered", BouncyCertificates.Leaf(theirs, "CN=foreign", true, later), anchor,
                new X509Certificate2(theirs.Der), false);

            var expired = BouncyCertificates.Leaf(ours, "CN=expired", true, DateTime.UtcNow.AddMinutes(-1));
            Ask("chain.expired-leaf", expired, anchor, null, false);
            Ask("chain.expired-leaf-ignoring-time", expired, anchor, null, true);

            AskWithDownload(theirs, anchor, later);
        }

        private static void Ask(string key, Issued leaf, X509Certificate2 anchor, X509Certificate2 offered, bool ignoreTime)
        {
            Report.Measure(key, () =>
            {
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    if (ignoreTime)
                    {
                        chain.ChainPolicy.VerificationFlags |= X509VerificationFlags.IgnoreNotTimeValid;
                    }

                    chain.ChainPolicy.ExtraStore.Add(anchor);
                    if (offered != null)
                    {
                        chain.ChainPolicy.ExtraStore.Add(offered);
                    }

                    chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1);
                    var built = chain.Build(new X509Certificate2(leaf.Der));
                    return Describe(chain, built, anchor);
                }
            });
        }

        /// <summary>
        /// A foreign leaf that names where its issuer can be fetched, the address being a listener
        /// here that accepts and never answers. On .NET Framework the address is contacted even
        /// with a millisecond to wait; what matters is whether this runtime goes out at all.
        /// </summary>
        private static void AskWithDownload(Issued theirs, X509Certificate2 anchor, DateTime later)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var arrived = 0;
            var accepting = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        var client = listener.AcceptTcpClient();
                        Interlocked.Increment(ref arrived);
                        client.Close();
                    }
                }
                catch (SocketException)
                {
                    // The listener was stopped, which is how this thread ends.
                }
                catch (ObjectDisposedException)
                {
                    // The same, on a runtime that reports it this way.
                }
            }) { IsBackground = true };
            accepting.Start();

            var leaf = BouncyCertificates.Leaf(theirs, "CN=downloading", true, later,
                "http://127.0.0.1:" + port + "/issuer.cer");
            var clock = Stopwatch.StartNew();
            Ask("chain.ca-issuers-leaf", leaf, anchor, null, false);
            Report.Line("chain.ca-issuers-build-ms", clock.ElapsedMilliseconds);
            Thread.Sleep(2000);
            Report.Line("chain.ca-issuers-connections", Volatile.Read(ref arrived));
            listener.Stop();
        }

        private static string Describe(X509Chain chain, bool built, X509Certificate2 anchor)
        {
            var count = chain.ChainElements.Count;
            var lastIsAnchor = count > 0 && chain.ChainElements[count - 1].Certificate.RawData.SequenceEqual(anchor.RawData);
            var status = string.Join(",", chain.ChainStatus.Select(s => s.Status.ToString()).Distinct());
            return "built " + (built ? "true" : "false") + ", elements " + count + ", last is anchor "
                + (lastIsAnchor ? "true" : "false") + ", status [" + status + "]";
        }
    }
}
