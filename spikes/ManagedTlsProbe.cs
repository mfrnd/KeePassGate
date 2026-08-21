using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

/// <summary>
/// Can a TLS 1.3 server run with a key that never touches the disk, and still be reached by clients
/// that are not .NET?
///
/// SChannel will not: an EphemeralKeySet certificate is refused with SEC_E_NO_CREDENTIALS, so
/// serving with it means writing the host key into the user's profile for as long as a database is
/// open. This asks whether Bouncy Castle's own TLS stack removes that, since it parses the PKCS#12
/// itself and hands Windows nothing.
///
/// Run it with the assembly the plugin builds against:
///
///   $bc = (Get-ChildItem -Recurse -Filter BouncyCastle.Cryptography.dll src\KeePassGate\bin |
///          Select-Object -First 1).FullName
///   Add-Type -TypeDefinition (Get-Content -Raw spikes\ManagedTlsProbe.cs) `
///       -ReferencedAssemblies 'System','System.Core','System.Security',$bc
///   [ManagedTlsProbe]::Run()
///
/// `Serve` is the same server left waiting for one connection, with the material an outside client
/// needs written beside it, so that something on another TLS stack entirely can be pointed at it.
/// `spikes/README.md` has the client.
/// </summary>
public static class ManagedTlsProbe
{
    public static string Run()
    {
        var report = new StringBuilder();
        report.AppendLine("Bouncy Castle " + typeof(TlsServerProtocol).Assembly.GetName().Version);

        var authority = NewAuthority();
        var host = IssuePkcs12(authority, "CN=probe host", true);

        using (var client = new X509Certificate2(
            IssuePkcs12(authority, "CN=probe client", false), (string)null, X509KeyStorageFlags.Exportable))
        {
            report.AppendLine("A. managed server, .NET client with a certificate");
            report.AppendLine("     " + Exchange(host, client, SslProtocols.Tls13));

            report.AppendLine("B. managed server, .NET client with no certificate");
            report.AppendLine("     " + Exchange(host, null, SslProtocols.Tls13));

            report.AppendLine("C. managed server, .NET client that will not speak TLS 1.3");
            report.AppendLine("     " + Exchange(host, client, SslProtocols.Tls12));
        }

        return report.ToString();
    }

    /// <summary>Serves one connection and writes what an outside client needs to reach it.</summary>
    public static string Serve(int port, string directory)
    {
        var authority = NewAuthority();
        var host = IssuePkcs12(authority, "CN=probe host", true);
        var client = IssuePkcs12(authority, "CN=probe client", false);

        File.WriteAllText(Path.Combine(directory, "ca.pem"), Pem("CERTIFICATE", authority.RawData));
        WriteClientPem(client, directory);

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        var report = new StringBuilder();
        report.AppendLine("listening on 127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);

        var before = KeyFiles();

        using (var accepted = listener.AcceptTcpClient())
        {
            var server = new ProbeTlsServer(new BcTlsCrypto(new SecureRandom()), host);
            var protocol = new TlsServerProtocol(accepted.GetStream());

            try
            {
                protocol.Accept(server);
            }
            catch (Exception error)
            {
                listener.Stop();
                return report.AppendLine("handshake FAILED " + error.GetType().Name + ": " + error.Message).ToString();
            }

            var request = FirstLine(protocol.Stream);
            Answer(protocol.Stream);

            report.AppendLine("server: " + Describe(server) + ", request " + request);
            report.AppendLine("server: key files before " + before + ", after " + KeyFiles());
            protocol.Close();
        }

        listener.Stop();
        return report.ToString();
    }

    private static string Exchange(byte[] hostPkcs12, X509Certificate2 clientCertificate, SslProtocols clientProtocols)
    {
        var before = KeyFiles();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var said = "server never ran";
        var during = -1;

        var thread = new Thread(delegate()
        {
            try
            {
                using (var accepted = listener.AcceptTcpClient())
                {
                    var server = new ProbeTlsServer(new BcTlsCrypto(new SecureRandom()), hostPkcs12);
                    var protocol = new TlsServerProtocol(accepted.GetStream());
                    protocol.Accept(server);
                    during = KeyFiles();

                    FirstLine(protocol.Stream);
                    Answer(protocol.Stream);
                    said = Describe(server);
                    protocol.Close();
                }
            }
            catch (Exception error)
            {
                said = "server FAILED " + error.GetType().Name;
            }
        });

        thread.IsBackground = true;
        thread.Start();

        var heard = "client never ran";
        try
        {
            using (var tcp = new TcpClient("127.0.0.1", port))
            using (var tls = new SslStream(
                tcp.GetStream(), false, delegate { return true; }, delegate { return clientCertificate; }))
            {
                var certificates = new X509Certificate2Collection();
                if (clientCertificate != null)
                {
                    certificates.Add(clientCertificate);
                }

                tls.AuthenticateAsClient("probe.kpg", certificates, clientProtocols, false);

                var request = Encoding.ASCII.GetBytes("GET /gate/status HTTP/1.1\r\n\r\n");
                tls.Write(request, 0, request.Length);
                tls.Flush();

                var buffer = new byte[256];
                var read = tls.Read(buffer, 0, buffer.Length);
                heard = Encoding.ASCII.GetString(buffer, 0, read).Split('\r')[0] + " over " + tls.SslProtocol;
            }
        }
        catch (Exception error)
        {
            heard = "client FAILED " + error.GetType().Name;
        }

        thread.Join(15000);
        listener.Stop();

        return said + "; client " + heard + "; key files " + before + " -> " + during + " -> " + KeyFiles();
    }

    private static string Describe(ProbeTlsServer server)
    {
        return "handshake ok, " + server.Version + ", client certificate " +
               (server.ClientSubject == null ? "none" : server.ClientSubject);
    }

    private static void Answer(Stream stream)
    {
        var body = "{\"ok\":true}";
        var response = "HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body;
        var bytes = Encoding.ASCII.GetBytes(response);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static string FirstLine(Stream stream)
    {
        var buffer = new byte[1024];
        var read = stream.Read(buffer, 0, buffer.Length);
        return read <= 0 ? "(nothing)" : Encoding.ASCII.GetString(buffer, 0, read).Split('\r')[0];
    }

    private static int KeyFiles()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft\\Crypto\\Keys");
        return Directory.Exists(directory) ? Directory.GetFiles(directory).Length : -1;
    }

    private static X509Certificate2 NewAuthority()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            new X500DistinguishedName("CN=probe authority"), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
    }

    private static byte[] IssuePkcs12(X509Certificate2 authority, string subject, bool forServer)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            new X500DistinguishedName(subject), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(forServer ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, false));

        var serial = new byte[16];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(serial);
        }

        serial[0] &= 0x7F;

        var now = DateTimeOffset.UtcNow;
        var issued = request.Create(authority, now.AddMinutes(-5), now.AddYears(1), serial);
        return issued.CopyWithPrivateKey(key).Export(X509ContentType.Pkcs12);
    }

    private static void WriteClientPem(byte[] pkcs12, string directory)
    {
        var store = new Pkcs12StoreBuilder().Build();
        using (var stream = new MemoryStream(pkcs12, false))
        {
            store.Load(stream, Array.Empty<char>());
        }

        foreach (string alias in store.Aliases)
        {
            if (!store.IsKeyEntry(alias))
            {
                continue;
            }

            var chain = store.GetCertificateChain(alias);
            File.WriteAllText(Path.Combine(directory, "client.pem"),
                Pem("CERTIFICATE", chain[0].Certificate.GetEncoded()));

            var info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(store.GetKey(alias).Key);
            File.WriteAllText(Path.Combine(directory, "client.key"), Pem("PRIVATE KEY", info.GetDerEncoded()));
            return;
        }
    }

    private static string Pem(string label, byte[] der)
    {
        const string newLine = "\n";
        var text = new StringBuilder();
        text.Append("-----BEGIN ").Append(label).Append("-----").Append(newLine);

        var base64 = Convert.ToBase64String(der);
        for (var i = 0; i < base64.Length; i += 64)
        {
            text.Append(base64.Substring(i, Math.Min(64, base64.Length - i))).Append(newLine);
        }

        return text.Append("-----END ").Append(label).Append("-----").Append(newLine).ToString();
    }

    /// <summary>The plugin's own TLS server, in miniature.</summary>
    private sealed class ProbeTlsServer : DefaultTlsServer
    {
        private readonly BcTlsCrypto _crypto;
        private readonly AsymmetricKeyParameter _privateKey;
        private readonly Org.BouncyCastle.Tls.Certificate _certificate;

        public ProbeTlsServer(BcTlsCrypto crypto, byte[] pkcs12)
            : base(crypto)
        {
            _crypto = crypto;

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

                _privateKey = store.GetKey(alias).Key;
                var der = store.GetCertificateChain(alias)[0].Certificate.GetEncoded();
                _certificate = new Org.BouncyCastle.Tls.Certificate(
                    TlsUtilities.EmptyBytes,
                    new[] { new CertificateEntry(new BcTlsCertificate(crypto, der), null) });
                break;
            }
        }

        public string ClientSubject { get; private set; }

        public string Version
        {
            get { return m_context.ServerVersion == null ? "(none)" : m_context.ServerVersion.ToString(); }
        }

        protected override ProtocolVersion[] GetSupportedVersions()
        {
            return ProtocolVersion.TLSv13.Only();
        }

        public override TlsCredentials GetCredentials()
        {
            return new BcDefaultTlsCredentialedSigner(
                new TlsCryptoParameters(m_context),
                _crypto,
                _privateKey,
                _certificate,
                SignatureAndHashAlgorithm.GetInstance(
                    Org.BouncyCastle.Tls.HashAlgorithm.sha256, SignatureAlgorithm.ecdsa));
        }

        public override Org.BouncyCastle.Tls.CertificateRequest GetCertificateRequest()
        {
            var algorithms = new List<SignatureAndHashAlgorithm>
            {
                SignatureAndHashAlgorithm.GetInstance(
                    Org.BouncyCastle.Tls.HashAlgorithm.sha256, SignatureAlgorithm.ecdsa)
            };

            return new Org.BouncyCastle.Tls.CertificateRequest(TlsUtilities.EmptyBytes, algorithms, null, null);
        }

        public override void NotifyClientCertificate(Org.BouncyCastle.Tls.Certificate clientCertificate)
        {
            if (clientCertificate == null || clientCertificate.IsEmpty)
            {
                return;
            }

            ClientSubject = new Org.BouncyCastle.X509.X509CertificateParser()
                .ReadCertificate(clientCertificate.GetCertificateAt(0).GetEncoded())
                .SubjectDN.ToString();
        }
    }
}
