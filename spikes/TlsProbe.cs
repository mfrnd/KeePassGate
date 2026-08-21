using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

// What a hand-rolled mTLS server inside a KeePass plugin would actually get from
// .NET Framework: which TLS versions negotiate, and whether client certificates work.
public static class TlsProbe
{
    private static X509Certificate2 Build(string cn, string eku)
    {
        RSA rsa = RSA.Create(2048);
        CertificateRequest req = new CertificateRequest(
            "CN=" + cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        OidCollection oids = new OidCollection();
        oids.Add(new Oid(eku));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
        X509Certificate2 self = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        byte[] pfx = self.Export(X509ContentType.Pkcs12, "probe");
        return new X509Certificate2(pfx, "probe",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static bool AcceptAny(object s, X509Certificate c, X509Chain ch, SslPolicyErrors e)
    {
        return true;
    }

    public static string Run(SslProtocols proto, bool withClientCert)
    {
        X509Certificate2 serverCert = Build("localhost", "1.3.6.1.5.5.7.3.1");
        X509Certificate2 clientCert = withClientCert ? Build("agent-fictitious", "1.3.6.1.5.5.7.3.2") : null;

        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string result = null;
        Exception failure = null;
        Thread server = new Thread(delegate()
        {
            try
            {
                using (TcpClient c = listener.AcceptTcpClient())
                using (SslStream ssl = new SslStream(c.GetStream(), false, AcceptAny))
                {
                    ssl.AuthenticateAsServer(serverCert, withClientCert, proto, false);
                    result = string.Format("negotiated {0}  cipher={1}/{2}bit  clientCertSeen={3}",
                        ssl.SslProtocol, ssl.CipherAlgorithm, ssl.CipherStrength,
                        ssl.RemoteCertificate == null ? "no" : ssl.RemoteCertificate.Subject);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        server.IsBackground = true;
        server.Start();

        try
        {
            using (TcpClient c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);
                using (SslStream ssl = new SslStream(c.GetStream(), false, AcceptAny))
                {
                    X509CertificateCollection certs = new X509CertificateCollection();
                    if (clientCert != null) certs.Add(clientCert);
                    ssl.AuthenticateAsClient("localhost", certs, proto, false);
                }
            }
        }
        catch (Exception ex) { if (failure == null) failure = ex; }

        server.Join(10000);
        listener.Stop();
        if (failure != null)
        {
            Exception e = failure;
            while (e.InnerException != null) e = e.InnerException;
            return "FAILED: " + e.Message;
        }
        return result ?? "no result";
    }
}
