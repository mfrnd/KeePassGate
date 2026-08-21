using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

// Can one listener serve several kdbx, each with its own CA and its own server certificate?
//
// TLS forces the question: the server sends its certificate BEFORE it sees the client's, so the
// client certificate cannot select the database. SNI is carried in the ClientHello, the very first
// message, and is therefore the only selector available in time.
//
// .NET Framework's SslStream has no server-side SNI hook (ServerCertificateSelectionCallback is
// .NET Core), so this reads the ClientHello off the socket first, extracts server_name, picks the
// certificate, then replays the bytes into SslStream through a prepending stream.
//
// The SNI name is derived from the CA fingerprint, not from the host, so a replicated database
// presents the same name wherever it is served.
public static class SniProbe
{
    // ---------- survey: what does this runtime actually offer? ----------

    public static string Survey()
    {
        string r = "  SslStream.AuthenticateAsServer overloads:\n";
        MethodInfo[] ms = typeof(SslStream).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (MethodInfo m in ms)
        {
            if (m.Name != "AuthenticateAsServer") continue;
            string sig = "";
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                sig += (i > 0 ? ", " : "") + ps[i].ParameterType.Name;
            r += "    (" + sig + ")\n";
        }
        Type opts = Type.GetType("System.Net.Security.SslServerAuthenticationOptions, System");
        r += "  SslServerAuthenticationOptions present: " + (opts != null) + "\n";
        r += "  (a server-side SNI callback would live there; absent means parse it ourselves)\n";
        return r;
    }

    // ---------- ClientHello parsing ----------

    private static void ReadExactly(Stream s, byte[] buf, int off, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, off + got, count - got);
            if (n <= 0) throw new IOException("stream closed while reading the ClientHello");
            got += n;
        }
    }

    /// <summary>
    /// Reads exactly one TLS record and returns it, with the SNI host name out of band.
    /// Everything is bounds-checked and any surprise yields a null name rather than an exception,
    /// because this parses attacker-controlled bytes before anything has been authenticated.
    /// </summary>
    private static byte[] ReadClientHello(Stream net, out string sni)
    {
        sni = null;
        byte[] head = new byte[5];
        ReadExactly(net, head, 0, 5);
        if (head[0] != 22) return head;                       // not a handshake record
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
        int end = off + len;
        int p = off;
        if (p + 4 > end || b[p] != 1) return null;            // handshake type 1 = client_hello
        int hsLen = (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
        p += 4;
        if (p + hsLen > end) return null;
        end = p + hsLen;

        p += 2;                                               // legacy_version
        p += 32;                                              // random
        if (p >= end) return null;
        p += 1 + b[p];                                        // session_id
        if (p + 2 > end) return null;
        p += 2 + ((b[p] << 8) | b[p + 1]);                    // cipher_suites
        if (p + 1 > end) return null;
        p += 1 + b[p];                                        // compression_methods
        if (p + 2 > end) return null;
        int extLen = (b[p] << 8) | b[p + 1];
        p += 2;
        int extEnd = p + extLen;
        if (extEnd > end) return null;

        while (p + 4 <= extEnd)
        {
            int type = (b[p] << 8) | b[p + 1];
            int size = (b[p + 2] << 8) | b[p + 3];
            p += 4;
            if (p + size > extEnd) return null;
            if (type == 0)                                    // server_name
            {
                int q = p;
                if (q + 2 > p + size) return null;
                int listLen = (b[q] << 8) | b[q + 1];
                q += 2;
                int listEnd = q + listLen;
                if (listEnd > p + size) return null;
                while (q + 3 <= listEnd)
                {
                    int nameType = b[q];
                    int nameLen = (b[q + 1] << 8) | b[q + 2];
                    q += 3;
                    if (q + nameLen > listEnd) return null;
                    if (nameType == 0)
                        return Encoding.ASCII.GetString(b, q, nameLen);
                    q += nameLen;
                }
                return null;
            }
            p += size;
        }
        return null;
    }

    /// <summary>Serves buffered bytes first, then the live stream. This is what lets SslStream
    /// re-read a ClientHello we have already taken off the socket.</summary>
    private sealed class PrependStream : Stream
    {
        private readonly byte[] _head;
        private int _pos;
        private readonly Stream _rest;

        internal PrependStream(byte[] head, Stream rest) { _head = head; _rest = rest; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos < _head.Length)
            {
                int n = Math.Min(count, _head.Length - _pos);
                Buffer.BlockCopy(_head, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            return _rest.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count) { _rest.Write(buffer, offset, count); }
        public override void Flush() { _rest.Flush(); }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long v) { throw new NotSupportedException(); }
    }

    // ---------- a "database": its own CA, server certificate and clients ----------

    private sealed class Db
    {
        internal string Name;
        internal X509Certificate2 Ca;
        internal X509Certificate2 Server;
        internal X509Certificate2 Client;
        internal string Sni;
    }

    private static X509Certificate2 Reload(X509Certificate2 c)
    {
        byte[] pfx = c.Export(X509ContentType.Pkcs12, "p");
        return new X509Certificate2(pfx, "p",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    /// <summary>SHA-256 over the DER, first 32 hex characters, so it fits one DNS label (max 63).</summary>
    private static string SniNameOf(X509Certificate2 ca)
    {
        using (SHA256 h = SHA256.Create())
        {
            byte[] d = h.ComputeHash(ca.RawData);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(d[i].ToString("x2"));
            return sb.ToString() + ".kpg";
        }
    }

    private static Db MakeDb(string name, string agent)
    {
        Db db = new Db();
        db.Name = name;
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest("CN=KPG CA " + name, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            db.Ca = Reload(r.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10),
                                              DateTimeOffset.UtcNow.AddDays(1)));
        }
        db.Sni = SniNameOf(db.Ca);
        db.Server = Issue(db.Ca, "CN=" + db.Sni, "1.3.6.1.5.5.7.3.1", db.Sni);
        db.Client = Issue(db.Ca, "CN=" + agent, "1.3.6.1.5.5.7.3.2", null);
        return db;
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string subject, string eku, string sniName)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CertificateRequest r = new CertificateRequest(subject, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            OidCollection oids = new OidCollection();
            oids.Add(new Oid(eku));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            if (sniName != null)
            {
                san.AddDnsName(sniName);                       // the CA-derived, host-independent name
                san.AddDnsName("localhost");
                san.AddIpAddress(IPAddress.Loopback);
            }
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

    private static bool PinnedTo(X509Certificate2 leaf, X509Certificate2 ca)
    {
        if (leaf == null) return false;
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

    // ---------- the run ----------

    public static string Run()
    {
        Db a = MakeDb("Alpha", "agent-fictitious");
        Db b = MakeDb("Beta", "agent-invented");
        Db[] open = new Db[] { a, b };

        string r = "  open databases and their CA-derived names:\n";
        r += "    Alpha -> " + a.Sni + "\n";
        r += "    Beta  -> " + b.Sni + "\n\n";

        r += Exchange(open, a.Sni, a.Client, a.Ca, "SNI=Alpha, client from Alpha", "Alpha", true);
        r += Exchange(open, b.Sni, b.Client, b.Ca, "SNI=Beta,  client from Beta ", "Beta", true);
        r += Exchange(open, a.Sni, b.Client, a.Ca, "SNI=Alpha, client from BETA ", "Alpha", false);
        r += Exchange(open, a.Sni, null, a.Ca, "SNI=Alpha, NO client cert     ", "Alpha", false);
        return r;
    }

    private static string Exchange(Db[] open, string sniWanted, X509Certificate2 clientCert,
                                   X509Certificate2 clientTrusts, string label,
                                   string expectDb, bool expectClientAccepted)
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string sniSeen = null, dbChosen = null, clientSubject = null;
        bool clientAccepted = false;
        string serverErr = null, clientErr = null;
        SslPolicyErrors clientSaw = SslPolicyErrors.None;

        Thread t = new Thread(delegate ()
        {
            try
            {
                using (TcpClient c = listener.AcceptTcpClient())
                {
                    NetworkStream net = c.GetStream();
                    string sni;
                    byte[] hello = ReadClientHello(net, out sni);
                    sniSeen = sni;

                    Db pick = null;
                    for (int i = 0; i < open.Length; i++)
                        if (open[i].Sni == sni) pick = open[i];
                    if (pick == null) pick = open[0];
                    dbChosen = pick.Name;

                    using (PrependStream pre = new PrependStream(hello, net))
                    using (SslStream ssl = new SslStream(pre, false,
                        delegate (object s, X509Certificate cert, X509Chain ch, SslPolicyErrors e)
                        {
                            if (cert == null) { clientAccepted = false; return true; }
                            X509Certificate2 leaf = new X509Certificate2(cert);
                            clientSubject = leaf.Subject;
                            clientAccepted = PinnedTo(leaf, pick.Ca);
                            return true;              // accept the handshake; the app decides
                        }))
                    {
                        ssl.AuthenticateAsServer(pick.Server, true, SslProtocols.Tls13, false);
                        byte[] buf = new byte[16];
                        ssl.Read(buf, 0, buf.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Exception e = ex; while (e.InnerException != null) e = e.InnerException;
                serverErr = e.Message;
            }
        });
        t.IsBackground = true;
        t.Start();

        try
        {
            using (TcpClient c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);       // connects by ADDRESS
                using (SslStream ssl = new SslStream(c.GetStream(), false,
                    delegate (object s, X509Certificate cert, X509Chain ch, SslPolicyErrors e)
                    {
                        clientSaw = e;
                        return PinnedTo(new X509Certificate2(cert), clientTrusts);
                    }))
                {
                    X509CertificateCollection certs = new X509CertificateCollection();
                    if (clientCert != null) certs.Add(clientCert);
                    ssl.AuthenticateAsClient(sniWanted, certs, SslProtocols.Tls13, false);
                    ssl.Write(new byte[] { 1 }, 0, 1);
                    ssl.Flush();
                }
            }
        }
        catch (Exception ex)
        {
            Exception e = ex; while (e.InnerException != null) e = e.InnerException;
            clientErr = e.Message;
        }

        t.Join(8000);
        listener.Stop();

        string res = "  " + label + " -> ";
        if (sniSeen == null) res += "SNI NOT SEEN; ";
        else res += "SNI ok; ";
        res += "picked " + (dbChosen ?? "?") + (dbChosen == expectDb ? " (correct)" : " (WRONG)") + "; ";
        if (clientErr != null && clientCert != null && expectClientAccepted)
            res += "client failed: " + clientErr;
        else
        {
            res += "server " + (clientAccepted ? "ACCEPTS" : "refuses") + " the client cert" +
                   (clientAccepted == expectClientAccepted ? " (correct)" : " (WRONG)");
            res += "; client saw " + clientSaw;
        }
        return res + "\n";
    }
}
