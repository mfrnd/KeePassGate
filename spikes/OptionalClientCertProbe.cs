using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

// The kdbx-hosted CA proposal needs ONE listener to serve both:
//   a paired client, presenting a certificate
//   an unpaired client, presenting none, to reach the pairing flow
//
// That is "optional client certificate". The question SslStream has to answer is whether
// clientCertificateRequired:false still makes the server SEND a CertificateRequest. If it does not,
// a client holding a perfectly good certificate never offers it, and the design needs two ports.
//
// Also measured: TLS 1.3 can carry client auth post-handshake, so the certificate may not be
// readable when AuthenticateAsServer returns. The reflection bridge reads it at REQUEST time, which
// is after application data, so both moments are recorded.
public static class OptionalClientCertProbe
{
    private static X509Certificate2 Reload(X509Certificate2 c)
    {
        byte[] pfx = c.Export(X509ContentType.Pkcs12, "p");
        return new X509Certificate2(pfx, "p",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static X509Certificate2 Authority()
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=KPG Authority", k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            return Reload(r.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10),
                                             DateTimeOffset.UtcNow.AddDays(1)));
        }
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string cn, string eku, bool server)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=" + cn, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            OidCollection oids = new OidCollection();
            oids.Add(new Oid(eku));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            if (server) { san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); }
            else { san.AddUri(new Uri("kpg://any/ops")); }
            r.CertificateExtensions.Add(san.Build());
            byte[] serial = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
            serial[0] &= 0x7F;
            using (X509Certificate2 issued = r.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5),
                                                     DateTimeOffset.UtcNow.AddHours(1), serial))
                return Reload(issued.CopyWithPrivateKey(k));
        }
    }

    private static string One(X509Certificate2 server, X509Certificate2 client,
                              bool required, bool clientOffers, SslProtocols proto)
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string atHandshake = "(none)";
        string afterRead = "(none)";
        string serverFail = null;
        string clientFail = null;
        string negotiated = "?";

        Thread t = new Thread(delegate ()
        {
            try
            {
                using (TcpClient c = listener.AcceptTcpClient())
                using (SslStream ssl = new SslStream(c.GetStream(), false, delegate { return true; }))
                {
                    ssl.AuthenticateAsServer(server, required, proto, false);
                    negotiated = ssl.SslProtocol.ToString();
                    if (ssl.RemoteCertificate != null)
                        atHandshake = new X509Certificate2(ssl.RemoteCertificate).Subject;

                    byte[] buf = new byte[16];
                    ssl.Read(buf, 0, buf.Length);
                    if (ssl.RemoteCertificate != null)
                        afterRead = new X509Certificate2(ssl.RemoteCertificate).Subject;
                }
            }
            catch (Exception ex)
            {
                Exception e = ex; while (e.InnerException != null) e = e.InnerException;
                serverFail = e.Message;
            }
        });
        t.IsBackground = true;
        t.Start();

        try
        {
            using (TcpClient c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);
                using (SslStream ssl = new SslStream(c.GetStream(), false, delegate { return true; }))
                {
                    X509CertificateCollection certs = new X509CertificateCollection();
                    if (clientOffers) certs.Add(client);
                    ssl.AuthenticateAsClient("localhost", certs, proto, false);
                    ssl.Write(new byte[] { 1 }, 0, 1);
                    ssl.Flush();
                }
            }
        }
        catch (Exception ex)
        {
            Exception e = ex; while (e.InnerException != null) e = e.InnerException;
            clientFail = e.Message;
        }

        t.Join(8000);
        listener.Stop();

        string label = string.Format("  required={0,-5} clientOffers={1,-5} -> ",
                                     required, clientOffers);
        if (serverFail != null || clientFail != null)
            return label + "HANDSHAKE FAILED (" + (serverFail ?? clientFail) + ")";

        string seen = afterRead != "(none)" ? afterRead : atHandshake;
        return label + negotiated + ", server sees " + seen +
               (atHandshake != afterRead ? "   [at handshake: " + atHandshake + "]" : "");
    }

    public static string Run()
    {
        X509Certificate2 ca = Authority();
        X509Certificate2 server = Issue(ca, "localhost", "1.3.6.1.5.5.7.3.1", true);
        X509Certificate2 client = Issue(ca, "agent-fictitious", "1.3.6.1.5.5.7.3.2", false);

        string r = "";
        SslProtocols[] protos = new SslProtocols[] { SslProtocols.Tls13, SslProtocols.Tls12 };
        foreach (SslProtocols p in protos)
        {
            r += " " + p + ":\n";
            r += One(server, client, true, true, p) + "\n";
            r += One(server, client, true, false, p) + "\n";
            r += One(server, client, false, true, p) + "   <-- THE DECISIVE ONE\n";
            r += One(server, client, false, false, p) + "\n";
        }
        return r;
    }
}
