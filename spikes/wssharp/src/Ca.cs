// The certificate authority and test client, identical to the sibling proofs.

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace KeePassGate.WsSharp
{
    /// <summary>A certificate authority built on the platform alone.</summary>
    internal static class Ca
    {
        internal static X509Certificate2 CreateAuthority(string name)
        {
            using (RSA key = RSA.Create(2048))
            {
                CertificateRequest req = new CertificateRequest("CN=" + name, key,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));
                // The authority outlives what it issues, or signing fails on the notAfter bound.
                X509Certificate2 cert = req.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddDays(1));
                return Reload(cert);
            }
        }

        internal static X509Certificate2 IssueServer(X509Certificate2 ca, string dnsName)
        {
            return Issue(ca, "CN=" + dnsName, "1.3.6.1.5.5.7.3.1", delegate (CertificateRequest req)
            {
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                san.AddDnsName(dnsName);
                san.AddIpAddress(IPAddress.Loopback);
                req.CertificateExtensions.Add(san.Build());
            });
        }

        internal static X509Certificate2 IssueClient(X509Certificate2 ca, string subject)
        {
            return Issue(ca, "CN=" + subject, "1.3.6.1.5.5.7.3.2", delegate (CertificateRequest req)
            {
                // The ceiling the analysis settled on: profile bound to a database, as a SAN URI.
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                san.AddUri(new Uri("kpg://7656adc24325684aa3ca8912185c0791/ops"));
                san.AddUri(new Uri("kpg://any/readonly"));
                req.CertificateExtensions.Add(san.Build());
            });
        }

        private static X509Certificate2 Issue(X509Certificate2 ca, string subject, string ekuOid,
                                              Action<CertificateRequest> customize)
        {
            using (RSA key = RSA.Create(2048))
            {
                CertificateRequest req = new CertificateRequest(subject, key,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                OidCollection eku = new OidCollection();
                eku.Add(new Oid(ekuOid));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
                customize(req);

                byte[] serial = new byte[8];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);

                using (X509Certificate2 issued = req.Create(ca,
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), serial))
                {
                    return Reload(issued.CopyWithPrivateKey(key));
                }
            }
        }

        /// <summary>Round-trip through PKCS#12 so the private key has a usable handle.</summary>
        private static X509Certificate2 Reload(X509Certificate2 cert)
        {
            byte[] pfx = cert.Export(X509ContentType.Pkcs12, "spike");
            return new X509Certificate2(pfx, "spike",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        internal static string FirstUri(X509Certificate2 cert)
        {
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.17")
                {
                    string text = ext.Format(false);
                    return text.Length > 90 ? text.Substring(0, 90) + "..." : text;
                }
            }
            return "(none)";
        }
    }

    internal static class Client
    {
        internal static string Request(int port, X509Certificate2 clientCertificate, string raw)
        {
            using (TcpClient tcp = new TcpClient())
            {
                tcp.Connect(IPAddress.Loopback, port);
                using (SslStream ssl = new SslStream(tcp.GetStream(), false,
                    delegate { return true; }))
                {
                    X509CertificateCollection certs = new X509CertificateCollection();
                    certs.Add(clientCertificate);
                    ssl.AuthenticateAsClient("localhost", certs, SslProtocols.Tls13, false);
                    Console.WriteLine("  [client] negotiated " + ssl.SslProtocol);

                    byte[] bytes = Encoding.ASCII.GetBytes(raw);
                    ssl.Write(bytes, 0, bytes.Length);
                    ssl.Flush();

                    StringBuilder sb = new StringBuilder();
                    byte[] buffer = new byte[4096];
                    tcp.ReceiveTimeout = 8000;
                    while (true)
                    {
                        int read;
                        try { read = ssl.Read(buffer, 0, buffer.Length); }
                        catch (IOException) { break; }
                        if (read <= 0) break;
                        sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        if (sb.ToString().Contains("{\"ok\":true}")) break;
                    }
                    return sb.ToString();
                }
            }
        }
    }
}
