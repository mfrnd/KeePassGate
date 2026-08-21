using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

// A rotating SNI: <nonce>.<HMAC(secret, nonce)>.kpg, so a passive observer sees a different name
// every connection and cannot correlate on it.
//
// The thing that has to be proved is not the HMAC, it is the consequence: a name that changes every
// connection CANNOT be in the server certificate's SAN, so hostname verification must fail. Does the
// handshake still complete when our own callback overrides that? And is a certificate under a
// single-purpose CA still unambiguous without it?
//
// The key here is a 16-byte value standing in for a root group UUID, matching the proposal exactly.
public static class HmacSniProbe
{
    private static X509Certificate2 Reload(X509Certificate2 c)
    {
        byte[] pfx = c.Export(X509ContentType.Pkcs12, "p");
        return new X509Certificate2(pfx, "p",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string subject, string eku, bool server)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest(subject, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            OidCollection oids = new OidCollection();
            oids.Add(new Oid(eku));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            // Deliberately ONLY localhost. The rotating name can never be here.
            if (server) { san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); }
            else san.AddUri(new Uri("kpg://profile/ops"));
            r.CertificateExtensions.Add(san.Build());
            byte[] serial = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
            serial[0] &= 0x7F;
            using (X509Certificate2 issued = r.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5),
                                                     DateTimeOffset.UtcNow.AddHours(1), serial))
                return Reload(issued.CopyWithPrivateKey(k));
        }
    }

    private sealed class Db
    {
        internal string Name;
        internal byte[] Uuid;          // the "root group UUID" used as the HMAC key
        internal X509Certificate2 Ca;
        internal X509Certificate2 Server;
        internal X509Certificate2 Client;
    }

    private static Db MakeDb(string name, string agent)
    {
        Db d = new Db();
        d.Name = name;
        d.Uuid = new byte[16];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(d.Uuid);
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=KPG CA " + name, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            d.Ca = Reload(r.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10),
                                             DateTimeOffset.UtcNow.AddDays(1)));
        }
        d.Server = Issue(d.Ca, "CN=KPG " + name, "1.3.6.1.5.5.7.3.1", true);
        d.Client = Issue(d.Ca, "CN=" + agent, "1.3.6.1.5.5.7.3.2", false);
        return d;
    }

    private static string Hex(byte[] b, int count)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(b[i].ToString("x2"));
        return sb.ToString();
    }

    private static byte[] Tag(byte[] key, byte[] nonce)
    {
        using (HMACSHA256 h = new HMACSHA256(key)) return h.ComputeHash(nonce);
    }

    /// <summary>Builds a fresh SNI for this connection.</summary>
    private static string MakeSni(byte[] uuid)
    {
        byte[] nonce = new byte[16];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
        return Hex(nonce, 16) + "." + Hex(Tag(uuid, nonce), 16) + ".kpg";
    }

    /// <summary>Server side: which open database does this name belong to?</summary>
    private static Db Resolve(Db[] open, string sni)
    {
        if (sni == null) return null;
        string[] parts = sni.Split('.');
        if (parts.Length != 3 || parts[2] != "kpg") return null;
        if (parts[0].Length != 32 || parts[1].Length != 32) return null;
        byte[] nonce = new byte[16];
        try
        {
            for (int i = 0; i < 16; i++)
                nonce[i] = Convert.ToByte(parts[0].Substring(i * 2, 2), 16);
        }
        catch { return null; }

        for (int i = 0; i < open.Length; i++)
        {
            byte[] expect = Tag(open[i].Uuid, nonce);
            string want = Hex(expect, 16);
            int diff = 0;
            for (int j = 0; j < 32; j++) diff |= want[j] ^ parts[1][j];   // fixed time over the hex
            if (diff == 0) return open[i];
        }
        return null;
    }

    private static bool PinnedTo(X509Certificate2 leaf, X509Certificate2 ca, string requiredEku)
    {
        if (leaf == null) return false;
        bool ekuOk = false;
        foreach (X509Extension ext in leaf.Extensions)
        {
            X509EnhancedKeyUsageExtension e = ext as X509EnhancedKeyUsageExtension;
            if (e == null) continue;
            foreach (Oid o in e.EnhancedKeyUsages) if (o.Value == requiredEku) ekuOk = true;
        }
        if (!ekuOk) return false;

        using (X509Chain chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(ca);
            if (!chain.Build(leaf)) return false;
            if (chain.ChainElements.Count != 2) return false;
            byte[] a = chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData;
            byte[] b = ca.RawData;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    // ---- ClientHello peek, same as SniProbe ----
    private static void ReadExactly(Stream s, byte[] buf, int off, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, off + got, count - got);
            if (n <= 0) throw new IOException("closed");
            got += n;
        }
    }

    private static byte[] ReadClientHello(Stream net, out string sni)
    {
        sni = null;
        byte[] head = new byte[5];
        ReadExactly(net, head, 0, 5);
        if (head[0] != 22) return head;
        int recLen = (head[3] << 8) | head[4];
        if (recLen < 0 || recLen > 16384) return head;
        byte[] rec = new byte[5 + recLen];
        Buffer.BlockCopy(head, 0, rec, 0, 5);
        ReadExactly(net, rec, 5, recLen);
        try { sni = ExtractSni(rec, 5, recLen); }
        catch { sni = null; }
        return rec;
    }

    private static string ExtractSni(byte[] b, int off, int len)
    {
        int end = off + len, p = off;
        if (p + 4 > end || b[p] != 1) return null;
        int hsLen = (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
        p += 4;
        if (p + hsLen > end) return null;
        end = p + hsLen;
        p += 2 + 32;
        if (p >= end) return null;
        p += 1 + b[p];
        if (p + 2 > end) return null;
        p += 2 + ((b[p] << 8) | b[p + 1]);
        if (p + 1 > end) return null;
        p += 1 + b[p];
        if (p + 2 > end) return null;
        int extLen = (b[p] << 8) | b[p + 1];
        p += 2;
        int extEnd = p + extLen;
        if (extEnd > end) return null;
        while (p + 4 <= extEnd)
        {
            int type = (b[p] << 8) | b[p + 1], size = (b[p + 2] << 8) | b[p + 3];
            p += 4;
            if (p + size > extEnd) return null;
            if (type == 0)
            {
                int q = p + 2, listEnd = p + size;
                while (q + 3 <= listEnd)
                {
                    int nameType = b[q], nameLen = (b[q + 1] << 8) | b[q + 2];
                    q += 3;
                    if (q + nameLen > listEnd) return null;
                    if (nameType == 0) return Encoding.ASCII.GetString(b, q, nameLen);
                    q += nameLen;
                }
                return null;
            }
            p += size;
        }
        return null;
    }

    private sealed class PrependStream : Stream
    {
        private readonly byte[] _head; private int _pos; private readonly Stream _rest;
        internal PrependStream(byte[] h, Stream r) { _head = h; _rest = r; }
        public override int Read(byte[] buf, int off, int count)
        {
            if (_pos < _head.Length)
            {
                int n = Math.Min(count, _head.Length - _pos);
                Buffer.BlockCopy(_head, _pos, buf, off, n); _pos += n; return n;
            }
            return _rest.Read(buf, off, count);
        }
        public override void Write(byte[] b, int o, int c) { _rest.Write(b, o, c); }
        public override void Flush() { _rest.Flush(); }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long v) { throw new NotSupportedException(); }
    }

    public static string Run()
    {
        Db a = MakeDb("Alpha", "agent-fictitious");
        Db b = MakeDb("Beta", "agent-invented");
        Db[] open = new Db[] { a, b };

        string r = "  rotation check, three SNI for the same database:\n";
        for (int i = 0; i < 3; i++) r += "    " + MakeSni(a.Uuid) + "\n";
        r += "\n";

        r += Exchange(open, MakeSni(a.Uuid), a.Client, a.Ca, "valid SNI Alpha, client Alpha ", "Alpha", true);
        r += Exchange(open, MakeSni(b.Uuid), b.Client, b.Ca, "valid SNI Beta,  client Beta  ", "Beta", true);
        r += Exchange(open, MakeSni(a.Uuid), b.Client, a.Ca, "valid SNI Alpha, client BETA  ", "Alpha", false);
        r += Exchange(open, "deadbeef" + new string('0', 24) + "." + new string('a', 32) + ".kpg",
                      a.Client, a.Ca, "FORGED SNI (bad tag)          ", null, false);
        r += "\n  server certificate SAN holds only localhost and 127.0.0.1, never the rotating name.\n";
        return r;
    }

    private static string Exchange(Db[] open, string sni, X509Certificate2 clientCert,
                                   X509Certificate2 clientTrusts, string label,
                                   string expectDb, bool expectAccepted)
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string dbChosen = null; bool accepted = false; bool resolved = false;
        SslPolicyErrors clientSaw = SslPolicyErrors.None;
        string clientErr = null;

        Thread t = new Thread(delegate ()
        {
            try
            {
                using (TcpClient c = listener.AcceptTcpClient())
                {
                    NetworkStream net = c.GetStream();
                    string seen; byte[] hello = ReadClientHello(net, out seen);
                    Db pick = Resolve(open, seen);
                    if (pick == null) { resolved = false; return; }     // drop before any TLS work
                    resolved = true; dbChosen = pick.Name;

                    using (PrependStream pre = new PrependStream(hello, net))
                    using (SslStream ssl = new SslStream(pre, false,
                        delegate (object s, X509Certificate cert, X509Chain ch, SslPolicyErrors e)
                        {
                            if (cert == null) { accepted = false; return true; }
                            accepted = PinnedTo(new X509Certificate2(cert), pick.Ca, "1.3.6.1.5.5.7.3.2");
                            return true;
                        }))
                    {
                        ssl.AuthenticateAsServer(pick.Server, true, SslProtocols.Tls13, false);
                        byte[] buf = new byte[16];
                        ssl.Read(buf, 0, buf.Length);
                    }
                }
            }
            catch { }
        });
        t.IsBackground = true; t.Start();

        try
        {
            using (TcpClient c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);
                using (SslStream ssl = new SslStream(c.GetStream(), false,
                    delegate (object s, X509Certificate cert, X509Chain ch, SslPolicyErrors e)
                    {
                        clientSaw = e;
                        // Name mismatch is EXPECTED and deliberately ignored: trust comes from the
                        // anchor pin plus the serverAuth EKU, not from the name.
                        return PinnedTo(new X509Certificate2(cert), clientTrusts, "1.3.6.1.5.5.7.3.1");
                    }))
                {
                    X509CertificateCollection certs = new X509CertificateCollection();
                    if (clientCert != null) certs.Add(clientCert);
                    ssl.AuthenticateAsClient(sni, certs, SslProtocols.Tls13, false);
                    ssl.Write(new byte[] { 1 }, 0, 1); ssl.Flush();
                }
            }
        }
        catch (Exception ex)
        {
            Exception e = ex; while (e.InnerException != null) e = e.InnerException;
            clientErr = e.Message;
        }

        t.Join(8000); listener.Stop();

        string res = "  " + label + " -> ";
        if (!resolved)
        {
            res += "SNI did not resolve, connection dropped pre-TLS" +
                   (expectDb == null ? " (correct)" : " (WRONG)");
            return res + "\n";
        }
        res += "picked " + dbChosen + (dbChosen == expectDb ? " (correct)" : " (WRONG)") + "; ";
        res += "server " + (accepted ? "ACCEPTS" : "refuses") +
               (accepted == expectAccepted ? " (correct)" : " (WRONG)") + "; ";
        res += "client saw [" + clientSaw + "]";
        if (clientErr != null) res += "; client: " + clientErr;
        return res + "\n";
    }
}
