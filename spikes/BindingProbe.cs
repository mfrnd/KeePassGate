using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

// An unpaired client has no trust anchor, so remote pairing is trust on first use and an on-path
// attacker holding the pairing token can impersonate the server outright. The client never reaches
// the real KeePass, so the operator sees nothing and refuses nothing.
//
// The fix is for the server to PROVE knowledge of the shared token, bound to the certificate it
// just presented. Hand-rolling that hash is possible, but RFC 5929 already defines it as
// tls-server-end-point, and .NET Framework may expose it via SslStream.TransportContext.
//
// Measured here: is a channel binding available on BOTH sides under TLS 1.3, and do they agree?
public static class BindingProbe
{
    public static string Survey()
    {
        string r = "  SslStream members that could carry key material:\n";
        MemberInfo[] ms = typeof(SslStream).GetMembers(BindingFlags.Instance | BindingFlags.Public);
        bool any = false;
        foreach (MemberInfo m in ms)
        {
            string n = m.Name;
            if (n.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Keying", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("TransportContext", StringComparison.OrdinalIgnoreCase) >= 0)
            { r += "    " + m.MemberType + " " + n + "\n"; any = true; }
        }
        if (!any) r += "    (none)\n";
        r += "  RFC 5705 exporter (SslStream.ExportKeyingMaterial): " +
             (typeof(SslStream).GetMethod("ExportKeyingMaterial") != null) + "\n";
        return r;
    }

    private static X509Certificate2 Reload(X509Certificate2 c)
    {
        byte[] pfx = c.Export(X509ContentType.Pkcs12, "p");
        return new X509Certificate2(pfx, "p",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static X509Certificate2 Ca()
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=KPG CA", k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            return Reload(r.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10),
                                             DateTimeOffset.UtcNow.AddDays(1)));
        }
    }

    private static X509Certificate2 Leaf(X509Certificate2 ca, string cn, string eku, bool server)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=" + cn, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            OidCollection o = new OidCollection(); o.Add(new Oid(eku));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(o, false));
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            if (server) { san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); }
            else san.AddUri(new Uri("kpg://profile/ops"));
            r.CertificateExtensions.Add(san.Build());
            byte[] serial = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
            serial[0] &= 0x7F;
            using (X509Certificate2 c = r.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5),
                                                 DateTimeOffset.UtcNow.AddHours(1), serial))
                return Reload(c.CopyWithPrivateKey(k));
        }
    }

    private static string Hex(byte[] b)
    {
        if (b == null) return "(null)";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < b.Length && i < 12; i++) sb.Append(b[i].ToString("x2"));
        if (b.Length > 12) sb.Append("..");
        return sb.ToString() + " (" + b.Length + " bytes)";
    }

    private static byte[] Binding(SslStream s, ChannelBindingKind kind)
    {
        try
        {
            if (s.TransportContext == null) return null;
            ChannelBinding cb = s.TransportContext.GetChannelBinding(kind);
            if (cb == null || cb.IsInvalid) return null;
            byte[] raw = new byte[cb.Size];
            System.Runtime.InteropServices.Marshal.Copy(cb.DangerousGetHandle(), raw, 0, cb.Size);
            return raw;
        }
        catch (Exception) { return null; }
    }

    public static string Run(SslProtocols proto)
    {
        X509Certificate2 ca = Ca();
        X509Certificate2 server = Leaf(ca, "KPG host", "1.3.6.1.5.5.7.3.1", true);
        X509Certificate2 client = Leaf(ca, "agent-fictitious", "1.3.6.1.5.5.7.3.2", false);

        TcpListener l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;

        byte[] sEnd = null, sUniq = null, cEnd = null, cUniq = null;
        string err = null;
        string negotiated = "?";

        Thread t = new Thread(delegate ()
        {
            try
            {
                using (TcpClient c = l.AcceptTcpClient())
                using (SslStream ssl = new SslStream(c.GetStream(), false, delegate { return true; }))
                {
                    ssl.AuthenticateAsServer(server, true, proto, false);
                    negotiated = ssl.SslProtocol.ToString();
                    sEnd = Binding(ssl, ChannelBindingKind.Endpoint);
                    sUniq = Binding(ssl, ChannelBindingKind.Unique);
                    byte[] b = new byte[4];
                    ssl.Read(b, 0, b.Length);
                }
            }
            catch (Exception ex) { Exception e = ex; while (e.InnerException != null) e = e.InnerException; err = e.Message; }
        });
        t.IsBackground = true; t.Start();

        try
        {
            using (TcpClient c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);
                using (SslStream ssl = new SslStream(c.GetStream(), false, delegate { return true; }))
                {
                    X509CertificateCollection cc = new X509CertificateCollection();
                    cc.Add(client);
                    ssl.AuthenticateAsClient("localhost", cc, proto, false);
                    cEnd = Binding(ssl, ChannelBindingKind.Endpoint);
                    cUniq = Binding(ssl, ChannelBindingKind.Unique);
                    ssl.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
                    ssl.Flush();
                }
            }
        }
        catch (Exception ex) { Exception e = ex; while (e.InnerException != null) e = e.InnerException; if (err == null) err = e.Message; }

        t.Join(8000); l.Stop();

        string r = "  " + proto + " -> negotiated " + negotiated + (err != null ? " (err: " + err + ")" : "") + "\n";
        r += "    Endpoint  server=" + Hex(sEnd) + "\n";
        r += "    Endpoint  client=" + Hex(cEnd) + "\n";
        bool match = sEnd != null && cEnd != null && sEnd.Length == cEnd.Length;
        if (match) for (int i = 0; i < sEnd.Length; i++) if (sEnd[i] != cEnd[i]) match = false;
        r += "    Endpoint agree: " + (sEnd == null || cEnd == null ? "N/A" : match.ToString()) + "\n";
        r += "    Unique    server=" + Hex(sUniq) + ", client=" + Hex(cUniq) + "\n";
        return r;
    }

    public static string RunAll()
    {
        return Survey() + "\n" + Run(SslProtocols.Tls13) + Run(SslProtocols.Tls12);
    }
}
